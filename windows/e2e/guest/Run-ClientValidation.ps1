Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$configPath = Join-Path $PSScriptRoot "validation-config.json"
$validationScriptPath = Join-Path $PSScriptRoot "Invoke-ClientValidation.ps1"
$config = Get-Content -Raw -Path $configPath | ConvertFrom-Json
$artifactsRoot = [string]$config.ArtifactsRoot
$artifactsArchivePath = Join-Path (Split-Path -Parent $artifactsRoot) "artifacts.zip"
$exitCode = 0

$null = New-Item -ItemType Directory -Force -Path $artifactsRoot
Set-Content -Path (Join-Path $artifactsRoot "runner-start.txt") -Value ([DateTimeOffset]::UtcNow.ToString("O"))

try {
    $invokeParameters = @{
        BundleRoot = [string]$config.BundleRoot
        ArtifactsRoot = $artifactsRoot
        ControlPlaneBaseUrl = [string]$config.ControlPlaneBaseUrl
        SilentUsername = [string]$config.SilentUsername
        InteractiveUsername = [string]$config.InteractiveUsername
    }

    if ([bool]$config.Connect) {
        $invokeParameters.Connect = $true
    }

    & $validationScriptPath @invokeParameters
}
catch {
    $exitCode = 1
    ($_ | Format-List * -Force | Out-String) | Set-Content -Path (Join-Path $artifactsRoot "runner-error.txt")
}
finally {
    try {
        if (Test-Path $artifactsArchivePath) {
            Remove-Item -Force $artifactsArchivePath
        }

        Compress-Archive -Path (Join-Path $artifactsRoot "*") -DestinationPath $artifactsArchivePath -Force
    }
    catch {
        ($_ | Format-List * -Force | Out-String) | Set-Content -Path (Join-Path $artifactsRoot "runner-compress-error.txt")
    }
}

exit $exitCode
