# Download the Inno Setup compiler (ISCC.exe) into tools\innosetup so release.bat
# can build the shop installer without a machine-wide Inno install.
# This is a build-time tool only. It is not packed into TechBench-Setup-*.exe
# and it never stores a GitHub token.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "tools\innosetup"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$existing = Get-ChildItem -Path $dest -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) {
    Write-Host "ISCC already at $($existing.FullName)"
    exit 0
}

$zip = Join-Path $env:TEMP ("Tools.InnoSetup-" + [Guid]::NewGuid().ToString("N") + ".nupkg")
$extract = Join-Path $env:TEMP ("Tools.InnoSetup-" + [Guid]::NewGuid().ToString("N"))
$ProgressPreference = "SilentlyContinue"
$urls = @(
    "https://www.nuget.org/api/v2/package/Tools.InnoSetup",
    "https://globalcdn.nuget.org/packages/tools.innosetup.6.4.3.nupkg"
)
$ok = $false
foreach ($url in $urls) {
    try {
        Write-Host "Downloading Inno Setup compiler from $url"
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        $ok = $true
        break
    } catch {
        Write-Host "Download failed: $($_.Exception.Message)"
    }
}
if (-not $ok) {
    Write-Error "Could not download Tools.InnoSetup. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
    exit 1
}

New-Item -ItemType Directory -Force -Path $extract | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
} catch {
    Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
}

$iscc = Get-ChildItem -Path $extract -Filter ISCC.exe -Recurse | Select-Object -First 1
if (-not $iscc) {
    Write-Error "Downloaded package did not contain ISCC.exe"
    exit 1
}

$from = $iscc.DirectoryName
Copy-Item -Path (Join-Path $from "*") -Destination $dest -Recurse -Force
$copied = Get-ChildItem -Path $dest -Filter ISCC.exe -Recurse | Select-Object -First 1
if (-not $copied) {
    Write-Error "Extracted Inno Setup but ISCC.exe did not land in $dest"
    exit 1
}

# Flatten so installer\build.bat can use tools\innosetup\ISCC.exe
if ($copied.FullName -ne (Join-Path $dest "ISCC.exe")) {
    Copy-Item -Path (Join-Path $copied.DirectoryName "*") -Destination $dest -Force
}

if (-not (Test-Path (Join-Path $dest "ISCC.exe"))) {
    Copy-Item -Path $copied.FullName -Destination (Join-Path $dest "ISCC.exe") -Force
}

Write-Host "ISCC ready at $(Join-Path $dest 'ISCC.exe')"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
