param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projects = @(
    "services/control-plane-api/OWLProtect.ControlPlane.Api.csproj",
    "services/gateway/OWLProtect.Gateway.csproj",
    "services/scheduler/OWLProtect.Scheduler.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Building $project"
    dotnet build $projectPath -c Release --nologo
}

Write-Host "Service build validation passed."
