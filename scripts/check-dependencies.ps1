param(
    [ValidateSet("all", "services", "windows")]
    [string]$ProjectSet = "all",
    [switch]$SkipNpmAudit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$projects = @(
    @{
        Path = "services/control-plane-api/OWLProtect.ControlPlane.Api.csproj"
        Group = "services"
        RequiresWindows = $false
    },
    @{
        Path = "services/gateway/OWLProtect.Gateway.csproj"
        Group = "services"
        RequiresWindows = $false
    },
    @{
        Path = "services/scheduler/OWLProtect.Scheduler.csproj"
        Group = "services"
        RequiresWindows = $false
    },
    @{
        Path = "windows/windows-client-service/OWLProtect.WindowsClientService.csproj"
        Group = "windows"
        RequiresWindows = $false
    },
    @{
        Path = "windows/windows-client-ui/OWLProtect.WindowsClientUi.csproj"
        Group = "windows"
        RequiresWindows = $true
    }
)

function Convert-JsonReport([string]$RawJson, [string]$Context) {
    $trimmed = $RawJson.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "$Context returned no JSON output."
    }

    $objectStart = $trimmed.IndexOf("{")
    $arrayStart = $trimmed.IndexOf("[")
    $jsonStart = if ($objectStart -ge 0 -and ($arrayStart -lt 0 -or $objectStart -lt $arrayStart)) {
        $objectStart
    }
    else {
        $arrayStart
    }

    if ($jsonStart -gt 0) {
        $openingCharacter = $trimmed[$jsonStart]
        $closingCharacter = if ($openingCharacter -eq "{") { "}" } else { "]" }
        $jsonEnd = $trimmed.LastIndexOf($closingCharacter)
        if ($jsonEnd -gt $jsonStart) {
            $trimmed = $trimmed.Substring($jsonStart, $jsonEnd - $jsonStart + 1).Trim()
        }
    }

    try {
        $convertFromJson = Get-Command ConvertFrom-Json
        if ($convertFromJson.Parameters.ContainsKey("Depth")) {
            return $trimmed | ConvertFrom-Json -Depth 20
        }

        return $trimmed | ConvertFrom-Json
    }
    catch {
        throw "$Context returned invalid JSON. $($_.Exception.Message)"
    }
}

function Get-ProblemText($Report) {
    if ($null -eq $Report -or -not ($Report.PSObject.Properties.Name -contains "problems")) {
        return ""
    }

    return (@($Report.problems) | ForEach-Object { $_.text } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "; "
}

function Get-Projects($Report) {
    if ($null -eq $Report -or -not ($Report.PSObject.Properties.Name -contains "projects")) {
        return @()
    }

    return @($Report.projects)
}

function Get-Frameworks($ProjectEntry) {
    if ($null -eq $ProjectEntry -or -not ($ProjectEntry.PSObject.Properties.Name -contains "frameworks")) {
        return @()
    }

    return @($ProjectEntry.frameworks)
}

$selectedProjects = switch ($ProjectSet) {
    "services" { @($projects | Where-Object { $_.Group -eq "services" }) }
    "windows" { @($projects | Where-Object { $_.Group -eq "windows" }) }
    default { $projects }
}

if (-not $SkipNpmAudit) {
    Write-Host "Running npm audit for production dependencies"
    $npmAudit = npm audit --audit-level=high --omit=dev --json | Out-String
    $npmAuditReport = Convert-JsonReport $npmAudit "npm audit"
    if ($npmAuditReport.metadata.vulnerabilities.high -gt 0 -or $npmAuditReport.metadata.vulnerabilities.critical -gt 0) {
        throw "npm audit reported high or critical production vulnerabilities."
    }
}

$vulnerableProjects = @()
foreach ($project in $selectedProjects) {
    if ($project.RequiresWindows -and -not $isWindowsPlatform) {
        Write-Host "Skipping NuGet scan for $($project.Path) because it requires Windows project evaluation."
        continue
    }

    $projectPath = Join-Path $repoRoot $project.Path
    Write-Host "Scanning NuGet dependencies for $($project.Path)"
    $rawReport = dotnet list $projectPath package --vulnerable --include-transitive --format json | Out-String
    $exitCode = $LASTEXITCODE
    $report = Convert-JsonReport $rawReport "dotnet list package for $($project.Path)"
    $problemText = Get-ProblemText $report
    if ($exitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($problemText)) {
            throw "NuGet vulnerability scanning failed for $($project.Path): $problemText"
        }

        throw "NuGet vulnerability scanning failed for $($project.Path) with exit code $exitCode."
    }

    foreach ($entry in (Get-Projects $report)) {
        foreach ($framework in (Get-Frameworks $entry)) {
            if ($null -eq $framework) {
                continue
            }

            $topLevelPackages = @($framework.topLevelPackages)
            $transitivePackages = @($framework.transitivePackages)
            if ($topLevelPackages.Count -gt 0 -or $transitivePackages.Count -gt 0) {
                $vulnerableProjects += $project.Path
            }
        }
    }
}

if ($vulnerableProjects.Count -gt 0) {
    $joinedProjects = ($vulnerableProjects | Sort-Object -Unique) -join ", "
    throw "NuGet vulnerability scanning reported vulnerable packages in: $joinedProjects"
}

Write-Host "Dependency security validation passed."
