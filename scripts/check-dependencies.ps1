param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projects = @(
    "services/control-plane-api/OWLProtect.ControlPlane.Api.csproj",
    "services/gateway/OWLProtect.Gateway.csproj",
    "services/scheduler/OWLProtect.Scheduler.csproj",
    "windows/windows-client-service/OWLProtect.WindowsClientService.csproj",
    "windows/windows-client-ui/OWLProtect.WindowsClientUi.csproj"
)

Write-Host "Running npm audit for production dependencies"
$npmAudit = npm audit --audit-level=high --omit=dev --json | Out-String
$npmAuditReport = $npmAudit | ConvertFrom-Json
if ($npmAuditReport.metadata.vulnerabilities.high -gt 0 -or $npmAuditReport.metadata.vulnerabilities.critical -gt 0) {
    throw "npm audit reported high or critical production vulnerabilities."
}

$vulnerableProjects = @()
foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Scanning NuGet dependencies for $project"
    $rawReport = dotnet list $projectPath package --vulnerable --include-transitive --format json | Out-String
    $report = $rawReport | ConvertFrom-Json

    foreach ($entry in @($report.projects)) {
        if (-not ($entry.PSObject.Properties.Name -contains "frameworks")) {
            continue
        }

        foreach ($framework in @($entry.frameworks)) {
            if ($null -eq $framework) {
                continue
            }

            $topLevelPackages = @($framework.topLevelPackages)
            $transitivePackages = @($framework.transitivePackages)
            if ($topLevelPackages.Count -gt 0 -or $transitivePackages.Count -gt 0) {
                $vulnerableProjects += $project
            }
        }
    }
}

if ($vulnerableProjects.Count -gt 0) {
    $joinedProjects = ($vulnerableProjects | Sort-Object -Unique) -join ", "
    throw "NuGet vulnerability scanning reported vulnerable packages in: $joinedProjects"
}

Write-Host "Dependency security validation passed."
