param(
    [string]$LayoutRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$InstallRoot = (Join-Path $env:ProgramFiles "OWLProtect\\WindowsClient"),
    [string]$ServiceName = "OWLProtectWindowsClient"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceSource = Join-Path $LayoutRoot "service"
$uiSource = Join-Path $LayoutRoot "ui"
$serviceTarget = Join-Path $InstallRoot "service"
$uiTarget = Join-Path $InstallRoot "ui"
$serviceExe = Join-Path $serviceTarget "OWLProtect.WindowsClientService.exe"

if (-not (Test-Path $serviceSource) -or -not (Test-Path $uiSource)) {
    throw "The bundle layout is incomplete. Expected 'service' and 'ui' folders under $LayoutRoot."
}

$null = New-Item -ItemType Directory -Force -Path $serviceTarget, $uiTarget
robocopy $serviceSource $serviceTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy $uiSource $uiTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null

if (-not (Test-Path $serviceExe)) {
    throw "The published Windows service executable was not found at $serviceExe."
}

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
Write-Host "Installed OWLProtect Windows Client to $InstallRoot"
