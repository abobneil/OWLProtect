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
$traySource = Join-Path $LayoutRoot "tray"
$uiSource = Join-Path $LayoutRoot "ui"
$assetsSource = Join-Path $LayoutRoot "assets"
$serviceTarget = Join-Path $InstallRoot "service"
$trayTarget = Join-Path $InstallRoot "tray"
$uiTarget = Join-Path $InstallRoot "ui"
$assetsTarget = Join-Path $InstallRoot "assets"
$serviceExe = Join-Path $serviceTarget "OWLProtect.WindowsClientService.exe"
$serviceConfigPath = Join-Path $serviceTarget "appsettings.Production.json"
$startupShortcutDirectory = Join-Path $env:ProgramData "Microsoft\\Windows\\Start Menu\\Programs\\StartUp"
$startupShortcutPath = Join-Path $startupShortcutDirectory "OWLProtect Client.lnk"
$uiExe = Join-Path $uiTarget "OWLProtect.WindowsClientUi.exe"
$trayExe = Join-Path $trayTarget "OWLProtect.WindowsClientTray.exe"
$shortcutIconPath = Join-Path $assetsTarget "icon-pack\\disconnected\\owlprotect-disconnected.ico"

if (-not (Test-Path $serviceSource) -or -not (Test-Path $traySource) -or -not (Test-Path $uiSource)) {
    throw "The bundle layout is incomplete. Expected 'service', 'tray', and 'ui' folders under $LayoutRoot."
}

$null = New-Item -ItemType Directory -Force -Path $serviceTarget, $trayTarget, $uiTarget, $assetsTarget
robocopy $serviceSource $serviceTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy $traySource $trayTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy $uiSource $uiTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if (Test-Path $assetsSource) {
    robocopy $assetsSource $assetsTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
}

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
    if (-not (Test-Path $trayExe)) {
        throw "The published Windows tray executable was not found at $trayExe."
    }

    Get-Process -Name "OWLProtect.WindowsClientTray" -ErrorAction SilentlyContinue | Stop-Process -Force

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupShortcutPath)
    $shortcut.TargetPath = $trayExe
    $shortcut.WorkingDirectory = $trayTarget
    $shortcut.Description = "Launch OWLProtect tray at logon"
    if (Test-Path $shortcutIconPath) {
        $shortcut.IconLocation = $shortcutIconPath
    }
    $shortcut.Save()

    Start-Process -FilePath $trayExe -WorkingDirectory $trayTarget | Out-Null
}
elseif (Test-Path $startupShortcutPath) {
    Remove-Item -Force $startupShortcutPath
}

Write-Host "Installed OWLProtect Windows Client to $InstallRoot"
