# Builds the self-contained, zero-install Windows distributable and zips it for sharing.
#
#   ./publish.ps1        -> dist/AssetMemory-win-x64.zip
#   ./publish.ps1 -NoZip -> publish folder only, no archive
#
# AssetMemory.exe is a tray app + Blazor UI on http://localhost:9222. Self-contained (no .NET
# runtime needed on the target) and a genuine single-file exe -- every static asset (CSS, the
# vendored blazor.web.js, the app icon) is an embedded resource served by a custom IFileProvider
# (see ManifestResourceFileProvider.cs / Program.cs) instead of living under wwwroot/, so the
# publish folder is just the one exe.
#
# App data (settings.json, assetmemory.db) lives in %LOCALAPPDATA%\AssetMemory, not next to the
# exe — so it survives redeploys and never ends up inside dist/AssetMemory-win-x64.zip.
# AppPaths.EnsureReady() migrates a legacy next-to-exe install on first run of a build that has
# the new path.

param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$distDir = Join-Path $root 'dist'
$project = Join-Path $root 'src\AssetMemory'
$publishDir = Join-Path $project 'bin\Release\net10.0-windows\win-x64\publish'

Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $publishDir 'AssetMemory.exe'
if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $publishDir  (AssetMemory.exe = $sizeMb MB)" -ForegroundColor Green

if (-not $NoZip) {
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir 'AssetMemory-win-x64.zip'
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Write-Host "Zipping to $zip ..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Done: $zip  ($zipMb MB)" -ForegroundColor Green
}
