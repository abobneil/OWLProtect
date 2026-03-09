param(
    [string]$OutputDirectory = "artifacts/containers",
    [string]$TagPrefix = "owlprotect",
    [string]$Version = "0.1.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Write-ChecksumManifest([string]$DirectoryPath, [string[]]$ArtifactPaths) {
    $checksums = foreach ($artifactPath in $ArtifactPaths) {
        $hash = Get-FileHash -Path $artifactPath -Algorithm SHA256
        "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $artifactPath)
    }

    $checksumPath = Join-Path $DirectoryPath "SHA256SUMS"
    Set-Content -Path $checksumPath -Value $checksums
    return $checksumPath
}

$repoRoot = Get-RepoRoot
$outputRoot = Join-Path $repoRoot $OutputDirectory
$null = New-Item -ItemType Directory -Force -Path $outputRoot

$targets = @(
    @{ Name = "admin-portal"; Dockerfile = "apps/admin-portal/Dockerfile" },
    @{ Name = "control-plane-api"; Dockerfile = "services/control-plane-api/Dockerfile" },
    @{ Name = "gateway"; Dockerfile = "services/gateway/Dockerfile" },
    @{ Name = "scheduler"; Dockerfile = "services/scheduler/Dockerfile" }
)

$artifacts = New-Object System.Collections.Generic.List[string]
$manifestTargets = @()

foreach ($target in $targets) {
    $tag = "$TagPrefix/$($target.Name):$Version"
    $archivePath = Join-Path $outputRoot "$($target.Name)-$Version.tar"
    $dockerfilePath = Join-Path $repoRoot $target.Dockerfile

    Write-Host "Building $tag from $($target.Dockerfile)"
    docker build -f $dockerfilePath -t $tag $repoRoot

    Write-Host "Saving $tag to $archivePath"
    docker save -o $archivePath $tag
    $artifacts.Add($archivePath) | Out-Null

    $imageId = docker image inspect $tag --format "{{.Id}}"
    $manifestTargets += @{
        name = $target.Name
        dockerfile = $target.Dockerfile
        tag = $tag
        imageId = $imageId.Trim()
        archive = (Split-Path -Leaf $archivePath)
    }
}

$checksumPath = Write-ChecksumManifest $outputRoot $artifacts.ToArray()
$manifest = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    version = $Version
    checksumFile = (Split-Path -Leaf $checksumPath)
    artifacts = $manifestTargets
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $outputRoot "manifest.json")
Write-Host "Container packages written to $outputRoot"
