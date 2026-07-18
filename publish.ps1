# Builds the self-contained, zero-install Windows distributable and zips it for sharing.
#
#   ./publish.ps1            -> dist/AssetMemory-win-x64.zip
#   ./publish.ps1 -NoZip     -> just the publish folder, no archive
#
# The output is fully self-contained: the target machine needs no .NET runtime installed.
# Unzip anywhere and run AssetMemory.exe. The app's data (settings.json, assetmemory.db) is
# written next to the exe, so keep it somewhere writable (not Program Files).

param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\AssetMemory.UI'
$publishDir = Join-Path $project 'bin\Release\net10.0-windows\win-x64\publish'

Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $publishDir 'AssetMemory.exe'
if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }

$icon = Join-Path $project 'app.ico'
if (Test-Path $icon) { Copy-Item $icon $publishDir -Force }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $publishDir  (AssetMemory.exe = $sizeMb MB)" -ForegroundColor Green

if ($NoZip) { return }

$distDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zip = Join-Path $distDir 'AssetMemory-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Zipping to $zip ..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Done: $zip  ($zipMb MB)" -ForegroundColor Green
