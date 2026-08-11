param(
    [string]$Version = "0.4.0"
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

# 1. Clean
Remove-Item (Join-Path $release "portable") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $release "installer") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $release "setup") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $release "final") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $final -Force | Out-Null

# 2. Publish portable single-file
# The -p:Version/-p:AssemblyVersion/-p:FileVersion overrides make the built
# binary report the actual release version, so the in-app update check compares
# against the real version instead of the Directory.Build.props default.
Invoke-Dotnet @(
    "publish", "$root\src\SafeDiskCleaner.App",
    "-c", "Release", "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-o", (Join-Path $release "portable")
)

# 3. Stage final artifact
$finalExe = Join-Path $final "SafeDiskCleaner-$Version-portable-win64.exe"
Copy-Item (Join-Path $release "portable\SafeDiskCleaner.exe") $finalExe -Force

# 4. Ship launcher alongside the exe and drop Mark-of-the-Web from artifacts
Copy-Item (Join-Path $PSScriptRoot "run.cmd") (Join-Path $final "run.cmd") -Force
Get-ChildItem $final -File | ForEach-Object {
    Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Artifacts ready:" -ForegroundColor Green
Get-ChildItem $final | ForEach-Object {
    Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
