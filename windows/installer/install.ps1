param(
    [string]$LayoutRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OWLProtect\\WindowsClient"),
    [string]$ServiceName = "OWLProtectWindowsClient",
    [string]$ControlPlaneBaseUrl = "http://localhost:5180",
    [string]$SilentUsername = "user",
    [string]$InteractiveUsername = "user",
    [string]$OtlpEndpoint = "",
    [string]$SupportBundleDirectory = "",
    [bool]$LaunchTrayAtLogon = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceSource = Join-Path $LayoutRoot "service"
$uiSource = Join-Path $LayoutRoot "ui"
$serviceTarget = Join-Path $InstallRoot "service"
$uiTarget = Join-Path $InstallRoot "ui"
$serviceExe = Join-Path $serviceTarget "OWLProtect.WindowsClientService.exe"
$serviceConfigPath = Join-Path $serviceTarget "appsettings.Production.json"
$startupShortcutDirectory = Join-Path $env:ProgramData "Microsoft\\Windows\\Start Menu\\Programs\\StartUp"
$startupShortcutPath = Join-Path $startupShortcutDirectory "OWLProtect Client.lnk"
$uiExe = Join-Path $uiTarget "OWLProtect.WindowsClientUi.exe"

if (-not (Test-Path $serviceSource) -or -not (Test-Path $uiSource)) {
    throw "The bundle layout is incomplete. Expected 'service' and 'ui' folders under $LayoutRoot."
}

$null = New-Item -ItemType Directory -Force -Path $serviceTarget, $uiTarget
robocopy $serviceSource $serviceTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy $uiSource $uiTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null

if (-not (Test-Path $serviceExe)) {
    throw "The published Windows service executable was not found at $serviceExe."
}

if ([string]::IsNullOrWhiteSpace($SupportBundleDirectory)) {
    $SupportBundleDirectory = Join-Path $env:ProgramData "OWLProtect\\Support"
}

$null = New-Item -ItemType Directory -Force -Path $SupportBundleDirectory

$serviceConfig = @{
    WindowsClient = @{
        ControlPlaneBaseUrl = $ControlPlaneBaseUrl
        SilentUsername = $SilentUsername
        InteractiveUsername = $InteractiveUsername
        OtlpEndpoint = $OtlpEndpoint
        SupportBundleDirectory = $SupportBundleDirectory
        LaunchTrayAtLogon = $LaunchTrayAtLogon
    }
    Observability = @{
        OtlpEndpoint = $OtlpEndpoint
    }
}

$serviceConfig | ConvertTo-Json -Depth 5 | Set-Content -Path $serviceConfigPath -Encoding UTF8

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }

    sc.exe config $ServiceName binPath= "`"$serviceExe`"" start= auto | Out-Null
}
else {
    sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto DisplayName= "OWLProtect Windows Client" | Out-Null
}

Start-Service -Name $ServiceName

if ($LaunchTrayAtLogon) {
    if (-not (Test-Path $uiExe)) {
        throw "The published Windows UI executable was not found at $uiExe."
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupShortcutPath)
    $shortcut.TargetPath = $uiExe
    $shortcut.WorkingDirectory = $uiTarget
    $shortcut.Description = "Launch OWLProtect Client at logon"
    $shortcut.Save()
}
elseif (Test-Path $startupShortcutPath) {
    Remove-Item -Force $startupShortcutPath
}

Write-Host "Installed OWLProtect Windows Client to $InstallRoot"
