param(
    [string]$EnvFile = ".env.local.example",
    [string]$NewAdminPassword = "",
    [string]$AdminPassword = ""
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

$repoRoot = Get-RepoRoot
$envPath = Resolve-InputPath $EnvFile
$settings = Read-DotEnv $envPath
$controlPlanePort = Get-Setting $settings "OWLP_CONTROL_PLANE_PORT" "5180"
$gatewayId = Get-Setting $settings "OWLP_GATEWAY_ID" "gw-1"
$gatewayStateDirectory = Resolve-InputPath (Get-Setting $settings "OWLP_GATEWAY_STATE_PATH" "./.gateway-state")
$trustBundleContainerPath = Get-Setting $settings "OWLP_GATEWAY_TRUST_BUNDLE_FILE" "/var/lib/owlprotect/gateway-trust-bundle.json"
$trustBundleHostPath = Join-Path $gatewayStateDirectory (Split-Path -Leaf $trustBundleContainerPath)
$adminUsername = Get-Setting $settings "OWLP_BOOTSTRAP_ADMIN_USERNAME" "admin"
$adminPassword = Get-Setting $settings "OWLP_BOOTSTRAP_ADMIN_PASSWORD" "change-local-bootstrap-admin-password"
$apiBase = "http://127.0.0.1:$controlPlanePort/api/v1"
$targetPassword = if ([string]::IsNullOrWhiteSpace($NewAdminPassword)) { $adminPassword } else { $NewAdminPassword }
$providedAdminPassword = if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $env:OWLP_CURRENT_ADMIN_PASSWORD } else { $AdminPassword }
$passwordCandidates = @($providedAdminPassword, $targetPassword, $adminPassword) | Select-Object -Unique

$bootstrap = Invoke-RestMethod -Method Get -Uri "$apiBase/bootstrap"
$loginResult = Login-Admin $apiBase $adminUsername $passwordCandidates
$login = $loginResult.Login
$currentPassword = $loginResult.Password

$accessToken = $login.tokens.accessToken
$headers = @{ Authorization = "Bearer $accessToken" }

if ($bootstrap.requiresPasswordChange) {
    Invoke-RestMethod -Method Post -Uri "$apiBase/admins/default/password" -Headers $headers -ContentType "application/json" -Body (@{
        currentPassword = $currentPassword
        newPassword = $targetPassword
    } | ConvertTo-Json) | Out-Null
    $currentPassword = $targetPassword
}

if ($bootstrap.requiresMfaEnrollment) {
    Invoke-RestMethod -Method Post -Uri "$apiBase/admins/default/mfa" -Headers $headers -ContentType "application/json" -Body "{}" | Out-Null
}

Invoke-RestMethod -Method Post -Uri "$apiBase/auth/step-up" -Headers $headers -ContentType "application/json" -Body (@{
    password = $currentPassword
} | ConvertTo-Json) | Out-Null

$existingTrustMaterials = Invoke-RestMethod -Method Get -Uri "$apiBase/trust-material/query?kind=Gateway&subjectId=$gatewayId" -Headers $headers
$hasExistingTrustMaterials = $null -ne $existingTrustMaterials -and @($existingTrustMaterials).Count -gt 0
$issuePath = if ($hasExistingTrustMaterials) {
    "$apiBase/gateways/$gatewayId/trust-material/rotate"
}
else {
    "$apiBase/gateways/$gatewayId/trust-material"
}

$trustBundle = Invoke-RestMethod -Method Post -Uri $issuePath -Headers $headers -ContentType "application/json" -Body "{}"
$null = New-Item -ItemType Directory -Force -Path $gatewayStateDirectory
$trustBundle | ConvertTo-Json -Depth 8 | Set-Content -Path $trustBundleHostPath
Write-Host "Gateway trust material written to $trustBundleHostPath"
