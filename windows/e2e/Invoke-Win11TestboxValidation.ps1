param(
    [string]$VmrunPath = "C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe",
    [string]$VmxPath = "C:\Users\nchester\Documents\Virtual Machines\Win11-testbox\Win11-testbox.vmx",
    [string]$SnapshotName = "Fresh",
    [string]$GuestUsername,
    [string]$GuestPassword,
    [string]$ClientBundleRoot = "artifacts/windows-client/bundle",
    [string]$ControlPlaneBaseUrl = "http://192.168.114.1:5180",
    [string]$ArtifactsRoot = "artifacts/e2e/win11-testbox",
    [string]$SilentUsername = "user",
    [string]$InteractiveUsername = "user",
    [switch]$ConnectClient
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Vmrun {
    param([string[]]$Arguments)
    & $VmrunPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "vmrun failed: $($Arguments -join ' ')"
    }
}

function Wait-ToolsRunning {
    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        $state = & $VmrunPath -T ws checkToolsState $VmxPath
        if ($state -match "running") {
            return
        }

        Start-Sleep -Seconds 5
    }

    throw "Timed out waiting for VMware Tools."
}

function Wait-GuestFile {
    param([string]$GuestPath)
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "fileExistsInGuest", $VmxPath, $GuestPath)
            return
        }
        catch {
            Start-Sleep -Seconds 3
        }
    }

    throw "Timed out waiting for guest file $GuestPath"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$bundleRoot = Join-Path $repoRoot $ClientBundleRoot
$artifactsPath = Join-Path $repoRoot $ArtifactsRoot
$guestRoot = "C:\Temp\owlprotect-e2e"
$guestBundleRoot = "$guestRoot\bundle"
$guestArtifactsRoot = "$guestRoot\artifacts"
$guestScriptPath = "$guestRoot\Invoke-ClientValidation.ps1"

if (-not (Test-Path $bundleRoot)) {
    throw "Client bundle root not found at $bundleRoot"
}

$null = New-Item -ItemType Directory -Force -Path $artifactsPath

Invoke-Vmrun @("-T", "ws", "revertToSnapshot", $VmxPath, $SnapshotName)
Invoke-Vmrun @("-T", "ws", "start", $VmxPath, "nogui")
Wait-ToolsRunning

Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "runProgramInGuest", $VmxPath, "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "New-Item -ItemType Directory -Force -Path '$guestRoot','$guestBundleRoot','$guestArtifactsRoot' | Out-Null")
Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, (Join-Path $repoRoot "windows\e2e\guest\Invoke-ClientValidation.ps1"), $guestScriptPath)
Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, (Join-Path $bundleRoot "README.txt"), "$guestBundleRoot\README.txt")

Get-ChildItem -Recurse -File -Path $bundleRoot | ForEach-Object {
    $relativePath = $_.FullName.Substring($bundleRoot.Length).TrimStart('\')
    $guestPath = Join-Path $guestBundleRoot $relativePath
    $guestDirectory = Split-Path -Parent $guestPath
    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "runProgramInGuest", $VmxPath, "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "New-Item -ItemType Directory -Force -Path '$guestDirectory' | Out-Null")
    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, $_.FullName, $guestPath)
}

$guestCommand = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $guestScriptPath,
    "-BundleRoot", $guestBundleRoot,
    "-ArtifactsRoot", $guestArtifactsRoot,
    "-ControlPlaneBaseUrl", $ControlPlaneBaseUrl,
    "-SilentUsername", $SilentUsername,
    "-InteractiveUsername", $InteractiveUsername
)
if ($ConnectClient) {
    $guestCommand += "-Connect"
}

try {
    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "runProgramInGuest", $VmxPath, "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", $guestCommand)
}
catch {
    try {
        Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromGuestToHost", $VmxPath, "$guestArtifactsRoot\validation.log", (Join-Path $artifactsPath "validation.log"))
    }
    catch {
    }

    throw
}
Wait-GuestFile -GuestPath "$guestArtifactsRoot\validation-result.json"

Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromGuestToHost", $VmxPath, "$guestArtifactsRoot\validation-result.json", (Join-Path $artifactsPath "validation-result.json"))

foreach ($artifactName in @("pre-connect-status.json", "post-connect-status.json", "support-bundle-status.json", "desktop.png", "validation.log")) {
    try {
        Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromGuestToHost", $VmxPath, "$guestArtifactsRoot\$artifactName", (Join-Path $artifactsPath $artifactName))
    }
    catch {
    }
}
