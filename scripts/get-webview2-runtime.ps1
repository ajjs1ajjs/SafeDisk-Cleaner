param(
    [string]$CabUrl = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$cacheDir = "$projectRoot\src-tauri\.webview2-runtime"
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null

if (-not $CabUrl) {
    Write-Host "  Detecting latest WebView2 fixed runtime x64..." -ForegroundColor Gray
    $page = curl.exe -sL "https://developer.microsoft.com/en-us/microsoft-edge/webview2/" 2>$null
    $pattern = 'https:\\u002F\\u002Fmsedge\.sf\.dl\.delivery\.mp\.microsoft\.com\\u002Ffilestreamingservice\\u002Ffiles\\u002F[0-9a-f-]+\\u002FMicrosoft\.WebView2\.FixedVersionRuntime\.(\d+\.\d+\.\d+\.\d+)\.x64\.cab'
    $matches = [regex]::Matches($page, $pattern) | ForEach-Object {
        [pscustomobject]@{
            Version = [version]$_.Groups[1].Value
            Url     = $_.Value -replace '\\u002F', '/'
        }
    }
    $latest = $matches | Sort-Object Version -Descending | Select-Object -First 1
    if ($latest) {
        $CabUrl = $latest.Url
        Write-Host "  Latest WebView2 runtime: $($latest.Version)" -ForegroundColor Gray
    } else {
        throw "Could not detect WebView2 runtime URL. Pass -CabUrl manually."
    }
}

$cabName = [System.IO.Path]::GetFileName($CabUrl)
$cabPath = Join-Path $cacheDir $cabName
$runtimeDirName = $cabName -replace '\.cab$', ''
$runtimeDir = Join-Path $cacheDir $runtimeDirName

if (-not (Test-Path "$runtimeDir\msedgewebview2.exe")) {
    if (-not (Test-Path $cabPath)) {
        Write-Host "  Downloading $cabName ..." -ForegroundColor Gray
        curl.exe -sL --fail --retry 3 -o $cabPath $CabUrl
        if ($LASTEXITCODE -ne 0) { throw "Failed to download WebView2 runtime: $CabUrl" }
    }

    Write-Host "  Extracting $cabName ..." -ForegroundColor Gray
    $extractDir = Join-Path $cacheDir "extract"
    if (Test-Path $extractDir) { Remove-Item -Recurse -Force $extractDir }
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    & expand.exe "$cabPath" -F:* "$extractDir"
    if ($LASTEXITCODE -ne 0) { throw "Failed to extract WebView2 runtime cab" }

    $inner = Get-ChildItem $extractDir -Directory | Where-Object { Test-Path "$($_.FullName)\msedgewebview2.exe" } | Select-Object -First 1
    if (-not $inner) { throw "WebView2 runtime folder not found after extraction" }
    if (Test-Path $runtimeDir) { Remove-Item -Recurse -Force $runtimeDir }
    Move-Item $inner.FullName $runtimeDir
    Remove-Item -Recurse -Force $extractDir
    Remove-Item -Force $cabPath
    Write-Host "  Cached runtime at: $runtimeDir" -ForegroundColor Gray
} else {
    Write-Host "  Using cached runtime: $runtimeDirName" -ForegroundColor Gray
}

Write-Output $runtimeDir
