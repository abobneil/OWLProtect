param(
    [string]$EnvFile = ".env",
    [string]$ComposeFile = "docker-compose.yml",
    [bool]$TakeBackup = $true,
    [int]$MaxWaitSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-InputPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return (Join-Path (Get-RepoRoot) $PathValue)
}

function Read-DotEnv([string]$PathValue) {
    $values = @{}
    foreach ($line in Get-Content $PathValue) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("#")) {
            continue
        }

        $parts = $line -split "=", 2
        if ($parts.Length -eq 2) {
            $values[$parts[0].Trim()] = $parts[1].Trim()
        }
    }

    return $values
}

function Get-Setting($Settings, [string]$Name, [string]$DefaultValue = "") {
    if ($Settings.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($Settings[$Name])) {
        return $Settings[$Name]
    }

    return $DefaultValue
}

function Wait-ForEndpoint([string]$Name, [string]$Uri, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host "$Name ready: $Uri"
                return
            }
        } catch {
        }

        Start-Sleep -Seconds 2
    }

    throw "$Name did not become ready before timeout: $Uri"
}

function Test-Endpoint([string]$Uri) {
    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 300
    }
    catch {
        return $false
    }
}

$repoRoot = Get-RepoRoot
$envPath = Resolve-InputPath $EnvFile
$composePath = Resolve-InputPath $ComposeFile
$settings = Read-DotEnv $envPath
$composeArgs = @("compose", "--env-file", $envPath, "-f", $composePath)

if ($TakeBackup) {
    & (Join-Path $PSScriptRoot "backup-selfhosted.ps1") -EnvFile $EnvFile -ComposeFile $ComposeFile -OutputDirectory "./backups" -SkipArchive
}

docker @composeArgs up -d --build

$controlPlanePort = Get-Setting $settings "OWLP_CONTROL_PLANE_PORT" "5180"
$gatewayPort = Get-Setting $settings "OWLP_GATEWAY_PORT" "5181"
$schedulerPort = Get-Setting $settings "OWLP_SCHEDULER_PORT" "5182"

Wait-ForEndpoint "control-plane health" "http://localhost:$controlPlanePort/health/ready" $MaxWaitSeconds
if (-not (Test-Endpoint "http://localhost:$gatewayPort/health/ready")) {
    & (Join-Path $PSScriptRoot "issue-gateway-trust-material.ps1") -EnvFile $EnvFile
}
Wait-ForEndpoint "gateway health" "http://localhost:$gatewayPort/health/ready" $MaxWaitSeconds
Wait-ForEndpoint "scheduler health" "http://localhost:$schedulerPort/health/ready" $MaxWaitSeconds
Wait-ForEndpoint "control-plane metrics" "http://localhost:$controlPlanePort/metrics" $MaxWaitSeconds
Wait-ForEndpoint "gateway metrics" "http://localhost:$gatewayPort/metrics" $MaxWaitSeconds
Wait-ForEndpoint "scheduler metrics" "http://localhost:$schedulerPort/metrics" $MaxWaitSeconds

Write-Host "Upgrade validation passed."
