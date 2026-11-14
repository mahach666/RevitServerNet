<#
.SYNOPSIS
    Packs release assets into artifacts: NuGet packages (nupkg and snupkg), a source zip and SHA256SUMS.txt.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\pack-release.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'RevitServerNet.csproj'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode -or -not $versionNode.InnerText) { throw 'Version property not found in RevitServerNet.csproj' }
$version = $versionNode.InnerText.Trim()

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path

# 1. NuGet packages. GeneratePackageOnBuild is off here: pack builds the project itself and the package is written once, to $Output.
Write-Host 'Packing NuGet packages...'
& dotnet pack $project -c Release -p:GeneratePackageOnBuild=false -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }
foreach ($name in "RevitServerNet.$version.nupkg", "RevitServerNet.$version.snupkg") {
    if (-not (Test-Path -LiteralPath (Join-Path $Output $name))) { throw "$name was not created in $Output" }
}

# 2. Source code archive
Write-Host 'Creating source code archive...'
$sourceZip = Join-Path $Output "RevitServerNet-$version-source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
& git -C $root archive --format=zip --prefix=RevitServerNet/ --output=$sourceZip HEAD
if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE" }

# 3. Checksums
Write-Host 'Computing SHA256...'
$files = @(Get-ChildItem -LiteralPath $Output -File | Where-Object { $_.Extension -in '.zip', '.nupkg', '.snupkg' } | Sort-Object Name)
$sums = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value $sums -Encoding Ascii
$sums
