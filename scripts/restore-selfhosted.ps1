param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$EnvFile = ".env",
    [string]$ComposeFile = "docker-compose.yml"
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

function Reset-Directory([string]$PathValue) {
    if (Test-Path $PathValue) {
        Remove-Item -Path (Join-Path $PathValue "*") -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        $null = New-Item -ItemType Directory -Path $PathValue -Force
    }
}

$repoRoot = Get-RepoRoot
$envPath = Resolve-InputPath $EnvFile
$composePath = Resolve-InputPath $ComposeFile
$resolvedBackupPath = Resolve-InputPath $BackupPath
$settings = Read-DotEnv $envPath

$workingPath = $resolvedBackupPath
if ($resolvedBackupPath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
    $workingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("owlprotect-restore-" + [guid]::NewGuid().ToString("n"))
    Expand-Archive -Path $resolvedBackupPath -DestinationPath $workingPath -Force
    $childItems = Get-ChildItem $workingPath
    if ($childItems.Count -eq 1 -and $childItems[0].PSIsContainer) {
        $workingPath = $childItems[0].FullName
    }
}

$postgresDb = Get-Setting $settings "OWLP_POSTGRES_DB" "owlprotect"
$postgresUser = Get-Setting $settings "OWLP_POSTGRES_USER" "owlprotect"
$postgresPassword = Get-Setting $settings "OWLP_POSTGRES_PASSWORD" "owlprotect"
$auditExportDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_AUDIT_EXPORT_DIRECTORY" "./audit-exports")
$gatewayStateDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_GATEWAY_STATE_PATH" "./.gateway-state")
$sqlPath = Join-Path $workingPath "postgres.sql"

$composeArgs = @("compose", "--env-file", $envPath, "-f", $composePath)
docker @composeArgs stop control-plane-api gateway scheduler admin-portal

if (Test-Path $sqlPath) {
    $restoreCommand = "PGPASSWORD=`"$postgresPassword`" psql -U `"$postgresUser`" -d `"$postgresDb`""
    Get-Content -Raw $sqlPath | docker @composeArgs exec -T postgres sh -lc $restoreCommand
}

$auditBackup = Join-Path $workingPath "audit-exports"
if (Test-Path $auditBackup) {
    Reset-Directory $auditExportDirectory
    Copy-Item (Join-Path $auditBackup "*") -Destination $auditExportDirectory -Recurse -Force
}

$gatewayBackup = Join-Path $workingPath "gateway-state"
if (Test-Path $gatewayBackup) {
    Reset-Directory $gatewayStateDirectory
    Copy-Item (Join-Path $gatewayBackup "*") -Destination $gatewayStateDirectory -Recurse -Force
}

docker @composeArgs up -d control-plane-api gateway scheduler admin-portal
Write-Host "Restore completed from $resolvedBackupPath"
