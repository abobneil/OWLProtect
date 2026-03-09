param(
    [string]$EnvFile = ".env.local.example",
    [string]$ComposeFile = "docker-compose.yml",
    [string]$OutputDirectory = "artifacts/recovery-rehearsal",
    [string]$AdminPassword = "",
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

function Login-Admin([string]$ApiBase, [string]$Username, [string[]]$PasswordCandidates) {
    foreach ($candidate in $PasswordCandidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        try {
            $response = Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/admin/login" -ContentType "application/json" -Body (@{
                username = $Username
                password = $candidate
            } | ConvertTo-Json)
            return @{
                Login = $response
                Password = $candidate
            }
        }
        catch {
            if ($_.Exception.Response -is [System.Net.HttpWebResponse] -and $_.Exception.Response.StatusCode -eq [System.Net.HttpStatusCode]::BadRequest) {
                continue
            }

            throw
        }
    }

    throw "Unable to authenticate the bootstrap admin with the provided password candidates."
}

function Wait-ForEndpoint([string]$Name, [string]$Uri, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return
            }
        }
        catch {
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
$outputRoot = Resolve-InputPath $OutputDirectory
$null = New-Item -ItemType Directory -Force -Path $outputRoot

$controlPlanePort = Get-Setting $settings "OWLP_CONTROL_PLANE_PORT" "5180"
$gatewayPort = Get-Setting $settings "OWLP_GATEWAY_PORT" "5181"
$schedulerPort = Get-Setting $settings "OWLP_SCHEDULER_PORT" "5182"
$adminUsername = Get-Setting $settings "OWLP_BOOTSTRAP_ADMIN_USERNAME" "admin"
$adminPassword = Get-Setting $settings "OWLP_BOOTSTRAP_ADMIN_PASSWORD" "change-local-bootstrap-admin-password"
$providedAdminPassword = if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $env:OWLP_CURRENT_ADMIN_PASSWORD } else { $AdminPassword }
$newAdminPassword = "RecoveryRehearsal!234"
$passwordCandidates = @($providedAdminPassword, $newAdminPassword, $adminPassword) | Select-Object -Unique
$auditExportDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_AUDIT_EXPORT_DIRECTORY" "./audit-exports")
$gatewayStateDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_GATEWAY_STATE_PATH" "./.gateway-state")

docker @composeArgs up -d --build

Wait-ForEndpoint "control-plane health" "http://127.0.0.1:$controlPlanePort/health/ready" $MaxWaitSeconds
if (-not (Test-Endpoint "http://127.0.0.1:$gatewayPort/health/ready")) {
    & (Join-Path $PSScriptRoot "issue-gateway-trust-material.ps1") -EnvFile $EnvFile -AdminPassword $providedAdminPassword -NewAdminPassword $newAdminPassword
}
Wait-ForEndpoint "gateway health" "http://127.0.0.1:$gatewayPort/health/ready" $MaxWaitSeconds
Wait-ForEndpoint "scheduler health" "http://127.0.0.1:$schedulerPort/health/ready" $MaxWaitSeconds

& (Join-Path $PSScriptRoot "backup-selfhosted.ps1") -EnvFile $EnvFile -ComposeFile $ComposeFile -OutputDirectory $OutputDirectory
$backupArchive = Get-ChildItem -Path $outputRoot -Filter "owlprotect-backup-*.zip" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $backupArchive) {
    throw "Recovery rehearsal could not locate the generated backup archive."
}

$controlPlaneApi = "http://127.0.0.1:$controlPlanePort/api/v1"
$loginResult = Login-Admin $controlPlaneApi $adminUsername $passwordCandidates
$login = $loginResult.Login
$currentAdminPassword = $loginResult.Password
$adminToken = $login.tokens.accessToken
$bootstrapBeforeMutation = Invoke-RestMethod -Method Get -Uri "$controlPlaneApi/bootstrap"
$restoredAdminPassword = $currentAdminPassword

Invoke-RestMethod -Method Post -Uri "$controlPlaneApi/admins/default/password" -Headers @{ Authorization = "Bearer $adminToken" } -ContentType "application/json" -Body (@{
    currentPassword = $currentAdminPassword
    newPassword = $newAdminPassword
} | ConvertTo-Json)
Invoke-RestMethod -Method Post -Uri "$controlPlaneApi/admins/default/mfa" -Headers @{ Authorization = "Bearer $adminToken" } -ContentType "application/json" -Body "{}"

$users = Invoke-RestMethod -Method Get -Uri "$controlPlaneApi/users" -Headers @{ Authorization = "Bearer $adminToken" }
$testUser = $users | Where-Object { $_.username -eq "user" } | Select-Object -First 1
if ($null -eq $testUser) {
    throw "Recovery rehearsal could not locate the seeded test user."
}

Invoke-RestMethod -Method Post -Uri "$controlPlaneApi/users/$($testUser.id)/enable" -Headers @{ Authorization = "Bearer $adminToken" } -ContentType "application/json" -Body "{}"

$null = New-Item -ItemType Directory -Force -Path $auditExportDirectory, $gatewayStateDirectory
$markerId = [guid]::NewGuid().ToString("n")
$auditMarker = Join-Path $auditExportDirectory "recovery-rehearsal-marker-$markerId.txt"
$gatewayMarker = Join-Path $gatewayStateDirectory "recovery-rehearsal-marker-$markerId.txt"
Set-Content -Path $auditMarker -Value "created-after-backup"
Set-Content -Path $gatewayMarker -Value "created-after-backup"

& (Join-Path $PSScriptRoot "restore-selfhosted.ps1") -EnvFile $EnvFile -ComposeFile $ComposeFile -BackupPath $backupArchive.FullName

Wait-ForEndpoint "control-plane health after restore" "http://127.0.0.1:$controlPlanePort/health/ready" $MaxWaitSeconds
Wait-ForEndpoint "gateway health after restore" "http://127.0.0.1:$gatewayPort/health/ready" $MaxWaitSeconds
Wait-ForEndpoint "scheduler health after restore" "http://127.0.0.1:$schedulerPort/health/ready" $MaxWaitSeconds

$bootstrap = Invoke-RestMethod -Method Get -Uri "$controlPlaneApi/bootstrap"
if ($bootstrap.requiresPasswordChange -ne $bootstrapBeforeMutation.requiresPasswordChange) {
    throw "Bootstrap password-change state did not match the pre-rehearsal snapshot after restore."
}

if ($bootstrap.requiresMfaEnrollment -ne $bootstrapBeforeMutation.requiresMfaEnrollment) {
    throw "Bootstrap MFA enrollment state did not match the pre-rehearsal snapshot after restore."
}

if ($bootstrap.testUserEnabled -ne $bootstrapBeforeMutation.testUserEnabled) {
    throw "Seeded test-user enablement did not match the pre-rehearsal snapshot after restore."
}

if (Test-Path $auditMarker) {
    throw "Audit export marker survived restore."
}

if (Test-Path $gatewayMarker) {
    throw "Gateway state marker survived restore."
}

$restoredLogin = Invoke-RestMethod -Method Post -Uri "$controlPlaneApi/auth/admin/login" -ContentType "application/json" -Body (@{
    username = $adminUsername
    password = $restoredAdminPassword
} | ConvertTo-Json)

if ([string]::IsNullOrWhiteSpace($restoredLogin.tokens.accessToken)) {
    throw "Expected restored bootstrap admin login to issue an access token."
}

$rehearsalReport = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    backupArchive = $backupArchive.FullName
    adminUsername = $adminUsername
    bootstrapStatusBeforeMutation = $bootstrapBeforeMutation
    bootstrapStatusAfterRestore = $bootstrap
    evidence = @(
        "Restored bootstrap admin credentials accepted the pre-backup password.",
        "Bootstrap state after restore matched the pre-rehearsal snapshot.",
        "Audit export and gateway-state markers created after backup were removed by restore."
    )
}

$reportPath = Join-Path $outputRoot "recovery-rehearsal-report.json"
$rehearsalReport | ConvertTo-Json -Depth 8 | Set-Content -Path $reportPath
Write-Host "Recovery rehearsal evidence written to $reportPath"
