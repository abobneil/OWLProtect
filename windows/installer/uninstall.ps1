param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OWLProtect\\WindowsClient"),
    [string]$ServiceName = "OWLProtectWindowsClient"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$startupShortcutPath = Join-Path $env:ProgramData "Microsoft\\Windows\\Start Menu\\Programs\\StartUp\\OWLProtect Client.lnk"
Get-Process -Name "OWLProtect.WindowsClientTray" -ErrorAction SilentlyContinue | Stop-Process -Force
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }

    sc.exe delete $ServiceName | Out-Null
}

if (Test-Path $InstallRoot) {
    Remove-Item -Recurse -Force $InstallRoot
}

if (Test-Path $startupShortcutPath) {
    Remove-Item -Force $startupShortcutPath
}

Write-Host "Removed OWLProtect Windows Client from $InstallRoot"
