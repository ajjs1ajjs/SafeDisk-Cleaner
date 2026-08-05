param(
    [string]$OutputDir = "dist-portable",
    [string]$WebView2CabUrl = "",
    [switch]$SkipWebView2
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

$tauriConf = Get-Content "src-tauri\tauri.conf.json" -Raw | ConvertFrom-Json
$Version = $tauriConf.version
$AppName = $tauriConf.productName
$exeName = "safedisk-cleaner.exe"

Write-Host "=== $AppName v$Version Portable Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Gray

Write-Host "[1/4] Building frontend assets..." -ForegroundColor Yellow
npm run build
if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }

Write-Host "Running cargo build --release..." -ForegroundColor Yellow
Set-Location src-tauri
cargo build --release
if ($LASTEXITCODE -ne 0) { throw "cargo build --release failed" }
Set-Location $projectRoot

Write-Host "[2/4] Creating portable package..." -ForegroundColor Yellow

$portableDir = "$OutputDir\$AppName Portable"
if (Test-Path $portableDir) {
    try { Remove-Item -Recurse -Force $portableDir -ErrorAction Stop }
    catch {
        $ts = Get-Date -Format "HHmmss"
        $portableDir = "$OutputDir\$AppName Portable $ts"
        Write-Host "  Original dir locked, using: $portableDir" -ForegroundColor Yellow
    }
}
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null

Copy-Item "src-tauri\target\release\$exeName" "$portableDir\" -Force

if (Test-Path "src-tauri\target\release\WebView2Loader.dll") {
    Copy-Item "src-tauri\target\release\WebView2Loader.dll" "$portableDir\" -Force
} elseif (Test-Path "src-tauri\WebView2Loader.dll") {
    Copy-Item "src-tauri\WebView2Loader.dll" "$portableDir\" -Force
}

if (Test-Path "src-tauri\icons\icon.ico") {
    Copy-Item "src-tauri\icons\icon.ico" "$portableDir\" -Force
}

Write-Host "  Portable dir: $portableDir" -ForegroundColor Gray
Write-Host "  Executable:   $portableDir\$exeName" -ForegroundColor Gray

Write-Host "[3/4] Bundling WebView2 fixed runtime..." -ForegroundColor Yellow
if (-not $SkipWebView2) {
    $runtimeDir = & "$PSScriptRoot\get-webview2-runtime.ps1" -CabUrl $WebView2CabUrl
    if ($LASTEXITCODE -ne 0) { throw "WebView2 runtime bundling failed" }
    Copy-Item -Recurse -Force $runtimeDir "$portableDir\"
    Write-Host "  WebView2 runtime bundled into portable package" -ForegroundColor Gray
} else {
    Write-Host "  Skipped (portable package requires system WebView2 runtime)" -ForegroundColor Yellow
}

Write-Host "[4/4] Creating portable ZIP archive..." -ForegroundColor Yellow

$zipName = "$AppName v${Version} Portable.zip"
$zipPath = "$OutputDir\$zipName"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Compress-Archive -Path "$portableDir\*" -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "  Installer: src-tauri\target\release\bundle\" -ForegroundColor White
Write-Host "  Portable:  $zipPath" -ForegroundColor White
Write-Host "  Folder:    $portableDir\" -ForegroundColor White
