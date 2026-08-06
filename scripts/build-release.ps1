param(
    [string]$Version = "0.3.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root "BUILD\release"
$final = Join-Path $release "final"

function Invoke-Dotnet {
    param([string[]]$ArgsList, [string]$WorkDir = $root)
    Write-Host ">> dotnet $($ArgsList -join ' ')" -ForegroundColor Cyan
    & dotnet @ArgsList
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($ArgsList -join ' ')" }
}

function Invoke-Wix {
    param([string[]]$ArgsList, [string]$WorkDir)
    Write-Host ">> wix $($ArgsList -join ' ') (in $WorkDir)" -ForegroundColor Cyan
    Push-Location $WorkDir
    try {
        & wix @ArgsList
        if ($LASTEXITCODE -ne 0) { throw "wix failed: $($ArgsList -join ' ')" }
    }
    finally {
        Pop-Location
    }
}

# 1. Clean
Remove-Item (Join-Path $release "portable") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $release "installer") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $release "setup") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $final -Force | Out-Null

# 2. Publish portable single-file
Invoke-Dotnet @(
    "publish", "$root\src\SafeDiskCleaner.App",
    "-c", "Release", "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", (Join-Path $release "portable")
)

# 3. Publish installer folder
Invoke-Dotnet @(
    "publish", "$root\src\SafeDiskCleaner.App",
    "-c", "Release", "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-o", (Join-Path $release "installer\app")
)

# 4. Generate WiX component list and build MSI
$installerDir = Join-Path $release "installer"
& (Join-Path $PSScriptRoot "gen-wix-files.ps1") -SourceDir (Join-Path $installerDir "app") -OutFile (Join-Path $installerDir "AppFiles.wxs")
if ($LASTEXITCODE -ne 0) { throw "gen-wix-files failed" }

$installerWxs = Join-Path $PSScriptRoot "Installer.wxs"
if (-not (Test-Path $installerWxs)) {
    throw "scripts\Installer.wxs not found"
}
Copy-Item $installerWxs $installerDir -Force

Invoke-Wix @(
    "build", "-o", "SafeDiskCleaner.msi", "AppFiles.wxs", "Installer.wxs", "-arch", "x64", "-culture", "en-US"
) -WorkDir $installerDir

# 5. Publish setup bootstrapper (embeds the MSI)
Copy-Item (Join-Path $installerDir "SafeDiskCleaner.msi") (Join-Path $root "src\SafeDiskCleaner.Setup\SafeDiskCleaner.msi") -Force
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
Invoke-Dotnet @(
    "publish", "$root\src\SafeDiskCleaner.Setup",
    "-c", "Release", "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", (Join-Path $release "setup")
)

# 6. Stage final artifacts
Copy-Item (Join-Path $release "portable\SafeDiskCleaner.exe") (Join-Path $final "SafeDiskCleaner-$Version-portable-win64.exe") -Force
Copy-Item (Join-Path $release "setup\SafeDiskCleaner-Setup.exe") (Join-Path $final "SafeDiskCleaner-Setup-$Version-win64.exe") -Force

Write-Host ""
Write-Host "Artifacts ready:" -ForegroundColor Green
Get-ChildItem $final | ForEach-Object {
    Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
