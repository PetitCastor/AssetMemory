# Builds the self-contained, single-file console (TUI) distributable.
#
#   ./publish-tui.ps1            -> dist/AssetMemory-Tui-win-x64.zip
#   ./publish-tui.ps1 -NoZip     -> just the publish folder, no archive
#
# Unlike the web build, the TUI has no wwwroot / static-asset manifest, so the published
# AssetMemory.Tui.exe is a genuine single-file standalone: drop it anywhere writable and run it.
# It writes its data (settings.json, assetmemory.db) next to the exe when run as sole instance,
# or attaches as a read-only viewer to a background AssetMemory if one is already running.

param(
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\AssetMemory.Tui'
$publishDir = Join-Path $project 'bin\Release\net10.0\win-x64\publish'

Write-Host "Publishing self-contained win-x64 TUI build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $publishDir 'AssetMemory.Tui.exe'
if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $publishDir  (AssetMemory.Tui.exe = $sizeMb MB)" -ForegroundColor Green

if ($NoZip) { return }

$distDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zip = Join-Path $distDir 'AssetMemory-Tui-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Zipping to $zip ..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Done: $zip  ($zipMb MB)" -ForegroundColor Green
