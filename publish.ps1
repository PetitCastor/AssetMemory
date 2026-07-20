# Builds the self-contained, zero-install Windows distributables and zips them for sharing.
#
#   ./publish.ps1               -> both editions into dist/
#   ./publish.ps1 -WebOnly      -> just the Blazor tray/web edition
#   ./publish.ps1 -TuiOnly      -> just the console (TUI) edition
#   ./publish.ps1 -NoZip        -> publish folders only, no archives
#
# Two editions over the same Core/Data/Collector layer:
#
#   Web (AssetMemory.exe)      - tray app + Blazor UI on http://localhost:9222. Self-contained but
#                                a multi-file drop: the whole folder is required (wwwroot/ + the
#                                static-asset manifest sit next to the exe). -> dist/AssetMemory-win-x64.zip
#
#   TUI (AssetMemory.Tui.exe)  - terminal UI, no browser, no wwwroot, so a genuine single-file exe.
#                                Runs standalone, or attaches as a read-only viewer to a running web
#                                instance and delegates writes to it. -> dist/AssetMemory-Tui-win-x64.zip
#
# Both are self-contained (no .NET runtime needed on the target). App data (settings.json,
# assetmemory.db) lives in %LOCALAPPDATA%\AssetMemory, not next to the exe — so it survives
# redeploys and never ends up inside dist/*.zip. AppPaths.EnsureReady() migrates a legacy
# next-to-exe install on first run of a build that has the new path.

param(
    [switch]$NoZip,
    [switch]$WebOnly,
    [switch]$TuiOnly
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$distDir = Join-Path $root 'dist'

function Compress-Edition {
    param([string]$PublishDir, [string]$ZipName)
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir $ZipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Write-Host "Zipping to $zip ..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Done: $zip  ($zipMb MB)" -ForegroundColor Green
}

function Publish-Web {
    $project = Join-Path $root 'src\AssetMemory.UI'
    $publishDir = Join-Path $project 'bin\Release\net10.0-windows\win-x64\publish'

    Write-Host "Publishing self-contained win-x64 WEB build..." -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    dotnet publish $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (web) failed (exit $LASTEXITCODE)" }

    $exe = Join-Path $publishDir 'AssetMemory.exe'
    if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }

    # PublishSingleFile + Sdk.Web drops loose Content items, so ship the tray icon explicitly.
    $icon = Join-Path $project 'app.ico'
    if (Test-Path $icon) { Copy-Item $icon $publishDir -Force }

    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Published: $publishDir  (AssetMemory.exe = $sizeMb MB)" -ForegroundColor Green

    if (-not $NoZip) { Compress-Edition -PublishDir $publishDir -ZipName 'AssetMemory-win-x64.zip' }
}

function Publish-Tui {
    $project = Join-Path $root 'src\AssetMemory.Tui'
    $publishDir = Join-Path $project 'bin\Release\net10.0\win-x64\publish'

    Write-Host "Publishing self-contained win-x64 TUI build..." -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    dotnet publish $project -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (tui) failed (exit $LASTEXITCODE)" }

    $exe = Join-Path $publishDir 'AssetMemory.Tui.exe'
    if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Published: $publishDir  (AssetMemory.Tui.exe = $sizeMb MB)" -ForegroundColor Green

    if (-not $NoZip) { Compress-Edition -PublishDir $publishDir -ZipName 'AssetMemory-Tui-win-x64.zip' }
}

if ($WebOnly -and $TuiOnly) { throw "Pass at most one of -WebOnly / -TuiOnly." }

if (-not $TuiOnly) { Publish-Web }
if (-not $WebOnly) { Publish-Tui }
