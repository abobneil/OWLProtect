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

function Write-ValidationResult {
    param(
        [bool]$Success,
        [string]$Message,
        [string]$ErrorDetail = "",
        [object]$PreConnectStatus = $null,
        [object]$PostConnectStatus = $null,
        [object]$SupportBundleStatus = $null
    )

    @{
        success = $Success
        executedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        machineName = $env:COMPUTERNAME
        controlPlaneBaseUrl = $ControlPlaneBaseUrl
        message = $Message
        errorDetail = $ErrorDetail
        preConnectState = if ($null -ne $PreConnectStatus) { $PreConnectStatus.status.state } else { $null }
        postConnectState = if ($null -ne $PostConnectStatus) { $PostConnectStatus.status.state } else { $null }
        supportBundlePath = if ($null -ne $SupportBundleStatus) { $SupportBundleStatus.exportPath } else { $null }
    } | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $ArtifactsRoot "validation-result.json")
}

function Wait-ServiceRunning {
    param(
        [string]$Name,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -eq "Running") {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Service '$Name' did not reach the Running state within $TimeoutSeconds seconds."
}

function Wait-PipeReady {
    param(
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            return (Send-PipeCommand -Command "status" -SilentSsoPreferred $true)
        }
        catch {
            Start-Sleep -Seconds 3
        }
    }

    throw "The owlprotect-client named pipe did not become ready within $TimeoutSeconds seconds."
}

$null = New-Item -ItemType Directory -Force -Path $ArtifactsRoot
$transcriptPath = Join-Path $ArtifactsRoot "validation.log"
$transcriptStarted = $false
$preConnectStatus = $null
$postConnectStatus = $null
$supportBundleStatus = $null
try {
    Start-Transcript -Path $transcriptPath -Force | Out-Null
    $transcriptStarted = $true

    $installScript = Join-Path $BundleRoot "scripts\install.ps1"
    $uiExe = Join-Path $BundleRoot "ui\OWLProtect.WindowsClientUi.exe"
    $automationExe = Join-Path $BundleRoot "automation\OWLProtect.WindowsClientUiAutomation.exe"
    $previewScenarioRoot = Join-Path $BundleRoot "automation\fixtures"

    & $installScript `
        -LayoutRoot $BundleRoot `
        -ControlPlaneBaseUrl $ControlPlaneBaseUrl `
        -SilentUsername $SilentUsername `
        -InteractiveUsername $InteractiveUsername `
        -SupportBundleDirectory "$env:ProgramData\OWLProtect\Support" `
        -LaunchTrayAtLogon $true

    Wait-ServiceRunning -Name "OWLProtectWindowsClient"
    $preConnectStatus = Wait-PipeReady
    $preConnectStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "pre-connect-status.json")

    if (-not (Test-Path $automationExe)) {
        throw "The UI automation executable was not found at $automationExe."
    }

    if ($Connect) {
        $null = Send-PipeCommand -Command "connect" -SilentSsoPreferred $true
        Start-Sleep -Seconds 20
        $postConnectStatus = Send-PipeCommand -Command "status" -SilentSsoPreferred $true
        $postConnectStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "post-connect-status.json")
    }

    & $automationExe `
        --ui-exe $uiExe `
        --artifacts-root $ArtifactsRoot `
        --preview-scenarios $previewScenarioRoot

    $supportBundleStatus = Send-PipeCommand -Command "support-bundle" -SilentSsoPreferred $false
    $supportBundleStatus | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $ArtifactsRoot "support-bundle-status.json")
    Capture-Screenshot -Path (Join-Path $ArtifactsRoot "desktop.png")

    Write-ValidationResult `
        -Success $true `
        -Message "Validation completed." `
        -PreConnectStatus $preConnectStatus `
        -PostConnectStatus $postConnectStatus `
        -SupportBundleStatus $supportBundleStatus
}
catch {
    try {
        Capture-Screenshot -Path (Join-Path $ArtifactsRoot "desktop.png")
    }
    catch {
    }

    Write-ValidationResult `
        -Success $false `
        -Message "Validation failed." `
        -ErrorDetail ($_ | Format-List * -Force | Out-String) `
        -PreConnectStatus $preConnectStatus `
        -PostConnectStatus $postConnectStatus `
        -SupportBundleStatus $supportBundleStatus

    throw
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}
