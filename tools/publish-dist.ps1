# Best-effort upload of TechBench.exe + latest.json to the public dist repo.
# The default Actions token cannot create releases on another repository.
# Requires repository secret DIST_REPO_TOKEN: a PAT with contents:write on
# blackviperxiii-ui/tech-bench-dist. If that secret is missing, skip (exit 0).
param(
    [string]$Exe = "TechBench.exe",
    [string]$Manifest = "latest.json",
    [string]$Repo = "blackviperxiii-ui/tech-bench-dist"
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:DIST_REPO_TOKEN)) {
    Write-Host "DIST_REPO_TOKEN is not set."
    Write-Host "The default Actions token cannot publish to $Repo."
    Write-Host "Add a PAT (contents:write on $Repo) as repository secret DIST_REPO_TOKEN."
    Write-Host "Skipping cross-repo upload. URL retarget and PE assert still apply."
    exit 0
}

if (-not (Test-Path -LiteralPath $Exe)) { Write-Host "ERROR: missing $Exe"; exit 1 }
if (-not (Test-Path -LiteralPath $Manifest)) { Write-Host "ERROR: missing $Manifest"; exit 1 }

$json = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$version = [string]$json.version
if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "ERROR: $Manifest has no version"
    exit 1
}

$tag = "v$version"
$env:GH_TOKEN = $env:DIST_REPO_TOKEN

$exists = $false
& gh release view $tag --repo $Repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { $exists = $true }

if ($exists) {
    & gh release upload $tag $Exe $Manifest --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & gh release edit $tag --repo $Repo --latest
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
else {
    $notes = "Public updater payload ($version). Source stays in the private tech-bench repo."
    & gh release create $tag $Exe $Manifest --repo $Repo --title "Tech Bench $version" --notes $notes --latest
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Published $tag ($Exe, $Manifest) to $Repo"
exit 0
