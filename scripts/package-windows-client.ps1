param(
    [string]$OutputDirectory = "artifacts/windows-client",
    [string]$Version = "0.1.0",
    [string]$RuntimeIdentifier = "win-x64"
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
$bundleRoot = Join-Path $outputRoot "bundle"
$serviceOutput = Join-Path $bundleRoot "service"
$uiOutput = Join-Path $bundleRoot "ui"
$scriptsOutput = Join-Path $bundleRoot "scripts"
$archivePath = Join-Path $outputRoot "OWLProtect-windows-client-$Version.zip"

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $bundleRoot
$null = New-Item -ItemType Directory -Force -Path $serviceOutput, $uiOutput, $scriptsOutput

dotnet publish (Join-Path $repoRoot "windows/windows-client-service/OWLProtect.WindowsClientService.csproj") `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained false `
    -o $serviceOutput

dotnet publish (Join-Path $repoRoot "windows/windows-client-ui/OWLProtect.WindowsClientUi.csproj") `
    -c Release `
    -r $RuntimeIdentifier `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=false `
    -o $uiOutput

Copy-Item (Join-Path $repoRoot "windows/installer/install.ps1") -Destination $scriptsOutput -Force
Copy-Item (Join-Path $repoRoot "windows/installer/uninstall.ps1") -Destination $scriptsOutput -Force

$bundleReadme = @"
OWLProtect Windows Client Bundle $Version

Contents:
- service\: Windows service publish output
- ui\: WinUI client publish output
- scripts\install.ps1: installs or updates the service-owned client bundle
- scripts\uninstall.ps1: removes the installed client bundle
"@
Set-Content -Path (Join-Path $bundleRoot "README.txt") -Value $bundleReadme

if (Test-Path $archivePath) {
    Remove-Item -Force $archivePath
}

Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $archivePath -Force
$checksumPath = Write-ChecksumManifest $outputRoot @($archivePath)
$manifest = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    archive = (Split-Path -Leaf $archivePath)
    checksumFile = (Split-Path -Leaf $checksumPath)
    installScript = "scripts/install.ps1"
    uninstallScript = "scripts/uninstall.ps1"
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $outputRoot "manifest.json")
Write-Host "Windows client package written to $outputRoot"
