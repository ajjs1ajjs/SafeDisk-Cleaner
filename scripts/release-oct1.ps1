param(
    [string]$Version = "1.7.4",
    [string]$Tag = "v1.7.4"
)

# One-shot release runner for 2026-10-01 (GitHub Actions limits reset).
# Safe to run twice: commit/push/tag steps are no-ops when there is nothing new.
$ErrorActionPreference = "Stop"
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Refusing: Tag '$Tag' does not match ^vX.Y.Z$" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Refusing: Version '$Version' does not match X.Y.Z$" }
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root "BUILD"
$log = Join-Path $logDir "release-oct1.log"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Log([string]$m) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $m"
    $line | Tee-Object -FilePath $log -Append
}

Set-Location $root
try {
    Log "=== Oct-1 release run ($Tag) ==="
    git add -A
    $st = git status --porcelain
    if ($st) {
        git commit -m "chore(release): $Tag macOS/Linux hardening" | Out-Null
        Log "committed pending changes"
    } else {
        Log "tree clean, nothing to commit"
    }
    Log "running tests (local, no Actions minutes)..."
    dotnet test tests/SafeDiskCleaner.Tests -c Release --nologo -v minimal 2>&1 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "tests failed, aborting push" }
    Log "tests green, pushing branch..."
    git push origin main
    Log "branch pushed"
    if (-not (git tag --list $Tag)) {
        git tag $Tag
        Log "tag $Tag created"
    } else {
        Log "tag $Tag already exists"
    }
    git push origin $Tag
    Log "tag pushed — CI publish job takes it from here"
    git push origin --delete release-oct1-staging 2>&1 | Tee-Object -FilePath $log -Append
    Log "staging branch cleaned up (best-effort)"
    Log "=== DONE ==="
}
catch {
    Log ("FAILED: " + $_.ToString())
    exit 1
}
