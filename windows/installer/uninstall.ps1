param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OWLProtect\\WindowsClient"),
    [string]$ServiceName = "OWLProtectWindowsClient"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

Write-Host "Removed OWLProtect Windows Client from $InstallRoot"
