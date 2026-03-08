param(
    [string]$EnvFile = ".env",
    [string]$ComposeFile = "docker-compose.yml",
    [string]$OutputDirectory = "./backups",
    [switch]$SkipArchive
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

$repoRoot = Get-RepoRoot
$envPath = Resolve-InputPath $EnvFile
$composePath = Resolve-InputPath $ComposeFile
$settings = Read-DotEnv $envPath

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Resolve-InputPath $OutputDirectory
$backupPath = Join-Path $backupRoot "owlprotect-backup-$timestamp"
$null = New-Item -ItemType Directory -Force -Path $backupPath

$postgresDb = Get-Setting $settings "OWLP_POSTGRES_DB" "owlprotect"
$postgresUser = Get-Setting $settings "OWLP_POSTGRES_USER" "owlprotect"
$postgresPassword = Get-Setting $settings "OWLP_POSTGRES_PASSWORD" "owlprotect"
$auditExportDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_AUDIT_EXPORT_DIRECTORY" "./audit-exports")
$gatewayStateDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_GATEWAY_STATE_PATH" "./.gateway-state")
$dumpPath = Join-Path $backupPath "postgres.sql"

$composeArgs = @("compose", "--env-file", $envPath, "-f", $composePath)
$dumpCommand = "PGPASSWORD=`"$postgresPassword`" pg_dump -U `"$postgresUser`" -d `"$postgresDb`" --clean --if-exists"
docker @composeArgs exec -T postgres sh -lc $dumpCommand | Out-File -FilePath $dumpPath -Encoding utf8

if (Test-Path $auditExportDirectory) {
    Copy-Item $auditExportDirectory -Destination (Join-Path $backupPath "audit-exports") -Recurse -Force
}

if (Test-Path $gatewayStateDirectory) {
    Copy-Item $gatewayStateDirectory -Destination (Join-Path $backupPath "gateway-state") -Recurse -Force
}

$metadata = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    gitCommit = (git -C $repoRoot rev-parse HEAD)
    envFile = $envPath
    composeFile = $composePath
}
$metadata | ConvertTo-Json | Out-File -FilePath (Join-Path $backupPath "metadata.json") -Encoding utf8

if (-not $SkipArchive) {
    Compress-Archive -Path $backupPath -DestinationPath "$backupPath.zip" -Force
}

Write-Host "Backup created at $backupPath"
