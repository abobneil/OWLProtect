param(
    [string]$EnvFile = ".env.e2e.example",
    [string]$ArtifactsRoot = "artifacts/e2e/disposable",
    [string]$AdminPortalUrl = "http://127.0.0.1:4173",
    [string]$ControlPlaneUrl = "http://127.0.0.1:5180",
    [switch]$LeaveRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Wait-HttpReady {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 180
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Seconds 3
    }

    throw "Timed out waiting for $Url"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..")).Path
$artifactsPath = Join-Path $repoRoot $ArtifactsRoot
$composeArgs = @(
    "--env-file", (Join-Path $repoRoot $EnvFile),
    "-f", (Join-Path $repoRoot "docker-compose.yml"),
    "-f", (Join-Path $repoRoot "docker-compose.e2e.yml")
)

$null = New-Item -ItemType Directory -Force -Path $artifactsPath, (Join-Path $repoRoot "artifacts/e2e/otel")

Push-Location $repoRoot
try {
    & docker compose @composeArgs up -d --build

    Wait-HttpReady -Url "$ControlPlaneUrl/health/ready"
    Wait-HttpReady -Url "http://127.0.0.1:5181/health/ready"
    Wait-HttpReady -Url "http://127.0.0.1:5182/health/ready"
    Wait-HttpReady -Url $AdminPortalUrl

    $env:OWLP_RELEASE_SMOKE_ADMIN_PASSWORD = "change-local-bootstrap-admin-password"
    $env:OWLP_RELEASE_SMOKE_CONTROL_PLANE_URL = $ControlPlaneUrl
    $env:OWLP_RELEASE_SMOKE_GATEWAY_URL = "http://127.0.0.1:5181"
    $env:OWLP_RELEASE_SMOKE_SCHEDULER_URL = "http://127.0.0.1:5182"
    node .\scripts\release-smoke.mjs | Tee-Object -FilePath (Join-Path $artifactsPath "release-smoke.log")

    $env:OWLP_E2E_ADMIN_PORTAL_URL = $AdminPortalUrl
    $env:OWLP_E2E_CONTROL_PLANE_URL = $ControlPlaneUrl
    $env:OWLP_E2E_ADMIN_USERNAME = "admin"
    $env:OWLP_E2E_ADMIN_PASSWORD = "change-local-bootstrap-admin-password"
    $env:OWLP_E2E_ADMIN_NEW_PASSWORD = "change-local-bootstrap-admin-password"

    & npx playwright install --with-deps chromium
    & npx playwright test ".\tests\e2e\admin-portal.spec.mjs" --config ".\tests\e2e\playwright.config.mjs"

    & docker compose @composeArgs logs --no-color | Set-Content -Path (Join-Path $artifactsPath "docker-compose.log")
}
finally {
    if (-not $LeaveRunning) {
        & docker compose @composeArgs down -v
    }
    Pop-Location
}
