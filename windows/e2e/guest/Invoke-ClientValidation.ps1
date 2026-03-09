param(
    [string]$BundleRoot,
    [string]$ArtifactsRoot,
    [string]$ControlPlaneBaseUrl,
    [string]$SilentUsername = "user",
    [string]$InteractiveUsername = "user",
    [switch]$Connect
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Send-PipeCommand {
    param(
        [string]$Command,
        [bool]$SilentSsoPreferred = $true
    )

    $client = [System.IO.Pipes.NamedPipeClientStream]::new(".", "owlprotect-client", [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $client.Connect(5000)
        $reader = [System.IO.StreamReader]::new($client, [System.Text.Encoding]::UTF8, $false, 1024, $true)
        $writer = [System.IO.StreamWriter]::new($client, [System.Text.Encoding]::UTF8, 1024, $true)
        $writer.AutoFlush = $true

        $request = @{
            protocolVersion = 1
            requestId = [guid]::NewGuid().ToString("n")
            command = $Command
            silentSsoPreferred = $SilentSsoPreferred
        } | ConvertTo-Json -Compress

        $writer.WriteLine($request)
        return ($reader.ReadLine() | ConvertFrom-Json)
    }
    finally {
        $client.Dispose()
    }
}

function Capture-Screenshot {
    param([string]$Path)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

$null = New-Item -ItemType Directory -Force -Path $ArtifactsRoot
$transcriptPath = Join-Path $ArtifactsRoot "validation.log"
$transcriptStarted = $false
try {
    Start-Transcript -Path $transcriptPath -Force | Out-Null
    $transcriptStarted = $true

    $installScript = Join-Path $BundleRoot "scripts\install.ps1"
    $uiExe = Join-Path $BundleRoot "ui\OWLProtect.WindowsClientUi.exe"

    & $installScript `
        -LayoutRoot $BundleRoot `
        -ControlPlaneBaseUrl $ControlPlaneBaseUrl `
        -SilentUsername $SilentUsername `
        -InteractiveUsername $InteractiveUsername `
        -SupportBundleDirectory "$env:ProgramData\OWLProtect\Support" `
        -LaunchTrayAtLogon $true

    $service = Get-Service -Name "OWLProtectWindowsClient"
    if ($service.Status -ne "Running") {
        Start-Service -Name "OWLProtectWindowsClient"
    }

    Start-Sleep -Seconds 5
    $preConnectStatus = Send-PipeCommand -Command "status" -SilentSsoPreferred $true
    $preConnectStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "pre-connect-status.json")

    if (Test-Path $uiExe) {
        Start-Process -FilePath $uiExe | Out-Null
    }

    if ($Connect) {
        $null = Send-PipeCommand -Command "connect" -SilentSsoPreferred $true
        Start-Sleep -Seconds 20
        $postConnectStatus = Send-PipeCommand -Command "status" -SilentSsoPreferred $true
        $postConnectStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "post-connect-status.json")
    }

    $supportBundleStatus = Send-PipeCommand -Command "support-bundle" -SilentSsoPreferred $false
    $supportBundleStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "support-bundle-status.json")
    Capture-Screenshot -Path (Join-Path $ArtifactsRoot "desktop.png")

    @{
        executedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        machineName = $env:COMPUTERNAME
        controlPlaneBaseUrl = $ControlPlaneBaseUrl
        preConnectState = $preConnectStatus.status.state
        postConnectState = if ($Connect) { (Get-Content (Join-Path $ArtifactsRoot "post-connect-status.json") | ConvertFrom-Json).status.state } else { $null }
        supportBundlePath = $supportBundleStatus.exportPath
    } | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $ArtifactsRoot "validation-result.json")
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}
