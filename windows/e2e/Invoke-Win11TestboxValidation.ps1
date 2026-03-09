param(
    [string]$VmrunPath = "C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe",
    [string]$VmxPath = "C:\Users\nchester\Documents\Virtual Machines\Win11-testbox\Win11-testbox.vmx",
    [string]$SnapshotName = "Fresh",
    [string]$GuestUsername,
    [string]$GuestPassword,
    [string]$ClientBundleRoot = "artifacts/windows-client/bundle",
    [string]$ClientBundleArchive = "",
    [string]$ControlPlaneBaseUrl = "http://192.168.114.1:5180",
    [string]$ArtifactsRoot = "artifacts/e2e/win11-testbox",
    [string]$SilentUsername = "user",
    [string]$InteractiveUsername = "user",
    [int]$GuestValidationTimeoutMinutes = 8,
    [switch]$ConnectClient
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Vmrun {
    param(
        [string[]]$Arguments,
        [int]$TimeoutSeconds = 0
    )

    if ($TimeoutSeconds -le 0) {
        & $VmrunPath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "vmrun failed: $($Arguments -join ' ')"
        }

        return
    }

    $standardOutputPath = [System.IO.Path]::GetTempFileName()
    $standardErrorPath = [System.IO.Path]::GetTempFileName()

    try {
        $process = Start-Process -FilePath $VmrunPath -ArgumentList $Arguments -PassThru -NoNewWindow -RedirectStandardOutput $standardOutputPath -RedirectStandardError $standardErrorPath
        if (-not (Wait-Process -Id $process.Id -Timeout $TimeoutSeconds -ErrorAction SilentlyContinue)) {
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            catch {
            }

            throw "vmrun timed out after $TimeoutSeconds second(s): $($Arguments -join ' ')"
        }

        $process.Refresh()
        $standardOutput = (Get-Content -Raw -Path $standardOutputPath -ErrorAction SilentlyContinue).Trim()
        $standardError = (Get-Content -Raw -Path $standardErrorPath -ErrorAction SilentlyContinue).Trim()
        if ($process.ExitCode -ne 0) {
            throw "vmrun failed: $($Arguments -join ' ')`n$standardOutput`n$standardError"
        }
    }
    finally {
        Remove-Item -Force -ErrorAction SilentlyContinue $standardOutputPath, $standardErrorPath
    }
}

function Format-ProcessArgument {
    param([string]$Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Quote-PowerShellLiteral {
    param([string]$Value)

    return "'" + ($Value -replace "'", "''") + "'"
}

function ConvertTo-EncodedCommand {
    param([string]$Script)

    return [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($Script))
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

function Wait-HostFile {
    param([string]$Path)
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            return
        }

        Start-Sleep -Seconds 3
    }

    throw "Timed out waiting for host file $Path"
}

function Copy-GuestFileToHostIfExists {
    param(
        [string]$GuestPath,
        [string]$HostPath,
        [int]$TimeoutSeconds = 60
    )

    try {
        Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "fileExistsInGuest", $VmxPath, $GuestPath)
    }
    catch {
        return $false
    }

    $hostDirectory = Split-Path -Parent $HostPath
    if (-not [string]::IsNullOrWhiteSpace($hostDirectory)) {
        $null = New-Item -ItemType Directory -Force -Path $hostDirectory
    }

    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromGuestToHost", $VmxPath, $GuestPath, $HostPath) -TimeoutSeconds $TimeoutSeconds
    return $true
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$bundleRoot = Join-Path $repoRoot $ClientBundleRoot
$artifactsPath = Join-Path $repoRoot $ArtifactsRoot
$clientScreenshotsPath = Join-Path $artifactsPath "client-screenshots"
$guestRoot = "C:\Temp\owlprotect-e2e"
$guestBundleRoot = "\\vmware-host\Shared Folders\owlprotect-bundle"
$guestArtifactsRoot = "$guestRoot\artifacts"
$guestScriptPath = "$guestRoot\Invoke-ClientValidation.ps1"
$guestRunnerScriptPath = "$guestRoot\Run-ClientValidation.ps1"
$guestConfigPath = "$guestRoot\validation-config.json"
$guestArtifactsArchivePath = "$guestRoot\artifacts.zip"
$hostArtifactsArchivePath = Join-Path $artifactsPath "guest-artifacts.zip"

if (-not (Test-Path $bundleRoot)) {
    throw "Client bundle root not found at $bundleRoot"
}

$null = New-Item -ItemType Directory -Force -Path $artifactsPath
$null = New-Item -ItemType Directory -Force -Path $clientScreenshotsPath
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $artifactsPath "validation-result.json")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $artifactsPath "validation.log")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $artifactsPath "ui-automation-report.json")
Remove-Item -Force -ErrorAction SilentlyContinue $hostArtifactsArchivePath
Get-ChildItem -Path $clientScreenshotsPath -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Invoke-Vmrun @("-T", "ws", "revertToSnapshot", $VmxPath, $SnapshotName)
Invoke-Vmrun @("-T", "ws", "start", $VmxPath, "nogui")
Wait-ToolsRunning
Invoke-Vmrun @("-T", "ws", "enableSharedFolders", $VmxPath, "runtime")
try {
    Invoke-Vmrun @("-T", "ws", "removeSharedFolder", $VmxPath, "owlprotect-bundle")
}
catch {
}
Invoke-Vmrun @("-T", "ws", "addSharedFolder", $VmxPath, "owlprotect-bundle", $bundleRoot)

Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "runProgramInGuest", $VmxPath, "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "New-Item -ItemType Directory -Force -Path '$guestRoot','$guestArtifactsRoot' | Out-Null")
Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, (Join-Path $repoRoot "windows\e2e\guest\Invoke-ClientValidation.ps1"), $guestScriptPath)
Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, (Join-Path $repoRoot "windows\e2e\guest\Run-ClientValidation.ps1"), $guestRunnerScriptPath)
$localConfigPath = Join-Path ([System.IO.Path]::GetTempPath()) ("owlprotect-validation-config-" + [guid]::NewGuid().ToString("n") + ".json")
@{
    BundleRoot = $guestBundleRoot
    ArtifactsRoot = $guestArtifactsRoot
    ControlPlaneBaseUrl = $ControlPlaneBaseUrl
    SilentUsername = $SilentUsername
    InteractiveUsername = $InteractiveUsername
    Connect = [bool]$ConnectClient
} | ConvertTo-Json | Set-Content -Path $localConfigPath
try {
    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "CopyFileFromHostToGuest", $VmxPath, $localConfigPath, $guestConfigPath)
}
finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $localConfigPath
}
Invoke-Vmrun @(
    "-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword,
    "runProgramInGuest", $VmxPath,
    "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
    "-NoProfile", "-ExecutionPolicy", "Bypass",
    "-Command", "if (Test-Path '$guestArtifactsRoot') { Remove-Item -Recurse -Force '$guestArtifactsRoot' }; if (Test-Path '$guestArtifactsArchivePath') { Remove-Item -Force '$guestArtifactsArchivePath' }; New-Item -ItemType Directory -Force -Path '$guestArtifactsRoot' | Out-Null"
) -TimeoutSeconds 120
 
$guestCommand = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-Command", ("Set-Location " + (Quote-PowerShellLiteral $guestRoot) + "; & " + (Quote-PowerShellLiteral $guestRunnerScriptPath))
)

$guestFailure = $null
try {
    Invoke-Vmrun @("-T", "ws", "-gu", $GuestUsername, "-gp", $GuestPassword, "runProgramInGuest", $VmxPath, "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", $guestCommand) -TimeoutSeconds ($GuestValidationTimeoutMinutes * 60)
}
catch {
    $guestFailure = $_
}

try {
    if (Copy-GuestFileToHostIfExists -GuestPath $guestArtifactsArchivePath -HostPath $hostArtifactsArchivePath -TimeoutSeconds 120) {
        Expand-Archive -LiteralPath $hostArtifactsArchivePath -DestinationPath $artifactsPath -Force
    }
}
catch {
    if ($null -eq $guestFailure) {
        throw
    }
}

try {
    Wait-HostFile -Path (Join-Path $artifactsPath "validation-result.json")
}
catch {
    if ($null -eq $guestFailure) {
        throw
    }
}

if ($null -ne $guestFailure) {
    throw $guestFailure
}
