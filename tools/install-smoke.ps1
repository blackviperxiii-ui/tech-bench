# First-install gate: silent per-user install, TechBench.exe --smoke, then uninstall.
# Fail closed. Inno [Run] is skipifsilent, so /VERYSILENT does not launch the UI.
param(
    [Parameter(Mandatory = $true)]
    [string]$Setup,
    [string]$Expected
)

$ErrorActionPreference = 'Stop'

$script:InstallDir = $null
$script:InstallExe = $null
$script:ReadyToUninstall = $false

function Fail([string]$Message) {
    throw $Message
}

function Test-VersionPrefix([string]$Actual, [string]$Want) {
    if ([string]::IsNullOrWhiteSpace($Actual) -or [string]::IsNullOrWhiteSpace($Want)) { return $false }
    if ($Actual -eq $Want) { return $true }
    if ($Actual.StartsWith($Want + ".", [System.StringComparison]::Ordinal)) { return $true }
    if ($Actual.StartsWith($Want + " ", [System.StringComparison]::Ordinal)) { return $true }
    if ($Actual.StartsWith($Want + "+", [System.StringComparison]::Ordinal)) { return $true }
    return $false
}

function Read-AppVersion {
    $candidates = @(
        (Join-Path $PSScriptRoot "..\AppVersion.cs"),
        (Join-Path (Get-Location) "AppVersion.cs")
    )
    foreach ($path in $candidates) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = [string](Get-Content -LiteralPath $path -Raw)
        $m = [regex]::Match($text, 'public const string Number\s*=\s*"([^"]+)"')
        if (-not $m.Success) { Fail "Could not parse AppVersion.Number in $path" }
        return $m.Groups[1].Value
    }
    Fail "AppVersion.cs not found (looked next to tools\ and in the current directory)"
}

function Assert-SkipIfSilent {
    $iss = Join-Path $PSScriptRoot "..\installer\techbench.iss"
    if (-not (Test-Path -LiteralPath $iss)) { Fail "installer\techbench.iss not found: $iss" }
    $text = [string](Get-Content -LiteralPath $iss -Raw)
    $runAt = $text.IndexOf("[Run]")
    if ($runAt -lt 0) { Fail "installer\techbench.iss has no [Run] section" }
    $rest = $text.Substring($runAt)
    $next = $rest.IndexOf("`n[")
    $section = if ($next -gt 0) { $rest.Substring(0, $next) } else { $rest }
    if ($section -notmatch 'postinstall') { Fail "Inno [Run] entry is missing postinstall" }
    if ($section -notmatch 'skipifsilent') {
        Fail "Inno [Run] postinstall entry must keep skipifsilent so a silent install does not launch the UI"
    }
    Write-Host "Step: Inno [Run] keeps skipifsilent"
}

function Remove-SmokeInstall {
    Write-Host "Step: silent uninstall"
    $unins = Join-Path $script:InstallDir "unins000.exe"
    if (-not (Test-Path -LiteralPath $unins)) { Fail "unins000.exe is missing: $unins" }
    $unArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
    # One argument string so Start-Process does not re-quote the switches.
    $unProc = Start-Process -FilePath $unins -ArgumentList $unArgs -PassThru
    if (-not $unProc.WaitForExit(120000)) {
        Write-Host "Uninstaller timed out after 120s. Killing pid $($unProc.Id)"
        try { $unProc.Kill() } catch { Write-Host "Kill failed: $($_.Exception.Message)" }
        try { if (-not $unProc.HasExited) { $unProc.WaitForExit(5000) | Out-Null } } catch {}
        Fail "Uninstaller did not exit within 120s"
    }
    try { $unProc.WaitForExit() | Out-Null } catch {}
    try { $unProc.Refresh() } catch {}
    Write-Host "Uninstaller process exit: $($unProc.ExitCode)"
    $deadline = (Get-Date).AddSeconds(90)
    while ((Test-Path -LiteralPath $script:InstallExe) -and ((Get-Date) -lt $deadline)) {
        Start-Sleep -Seconds 2
    }
    if (Test-Path -LiteralPath $script:InstallExe) {
        Fail "Uninstall left TechBench.exe in place: $($script:InstallExe)"
    }
    Write-Host "Uninstall removed TechBench.exe"
}

$exitCode = 1
try {
    Assert-SkipIfSilent

    if ([string]::IsNullOrWhiteSpace($Expected)) {
        $Expected = Read-AppVersion
    }
    $Expected = $Expected.Trim()
    if ([string]::IsNullOrWhiteSpace($Expected)) { Fail "Expected version is empty" }
    Write-Host "Step: expected version $Expected"

    if (-not (Test-Path -LiteralPath $Setup)) { Fail "Setup not found: $Setup" }
    $Setup = (Resolve-Path -LiteralPath $Setup).Path
    Write-Host "Step: setup $Setup"

    $base = $env:RUNNER_TEMP
    if ([string]::IsNullOrWhiteSpace($base)) { $base = [IO.Path]::GetTempPath() }
    if ([string]::IsNullOrWhiteSpace($base)) { Fail "No temp directory (RUNNER_TEMP and GetTempPath are empty)" }
    $base = $base.TrimEnd('\', '/')
    $id = [guid]::NewGuid().ToString("N")
    $script:InstallDir = Join-Path $base ("tb-smoke-" + $id)
    $log = Join-Path $base ("tb-smoke-" + $id + ".log")
    $outFile = Join-Path $base ("tb-smoke-" + $id + "-out.txt")
    Write-Host "Step: install dir $($script:InstallDir)"
    Write-Host "Step: setup log $log"

    # One argument string. pwsh Start-Process copies ArgumentList onto the raw command line
    # (it does not quote a single string again). /VERYSILENT plus skipifsilent keeps the UI closed.
    $setupArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /MERGETASKS="!desktopicon" /DIR="' + $script:InstallDir + '" /LOG="' + $log + '"'
    Write-Host "Step: silent install"
    $setupProc = Start-Process -FilePath $Setup -ArgumentList $setupArgs -PassThru
    if (-not $setupProc.WaitForExit(300000)) {
        Write-Host "Setup timed out after 300s. Killing pid $($setupProc.Id)"
        try { $setupProc.Kill() } catch { Write-Host "Kill failed: $($_.Exception.Message)" }
        try { if (-not $setupProc.HasExited) { $setupProc.WaitForExit(5000) | Out-Null } } catch {}
        Write-Host "---- setup log ----"
        if (Test-Path -LiteralPath $log) {
            Get-Content -LiteralPath $log | ForEach-Object { Write-Host $_ }
        } else {
            Write-Host "(no log file)"
        }
        Write-Host "---- end setup log ----"
        Fail "Silent install did not exit within 300s"
    }
    try { $setupProc.WaitForExit() | Out-Null } catch {}
    try { $setupProc.Refresh() } catch {}
    Write-Host "Setup exit code: $($setupProc.ExitCode)"
    if ($null -eq $setupProc.ExitCode -or $setupProc.ExitCode -ne 0) {
        Write-Host "---- setup log ----"
        if (Test-Path -LiteralPath $log) {
            Get-Content -LiteralPath $log | ForEach-Object { Write-Host $_ }
        } else {
            Write-Host "(no log file)"
        }
        Write-Host "---- end setup log ----"
        Fail "Silent install failed with exit $($setupProc.ExitCode)"
    }

    $script:InstallExe = Join-Path $script:InstallDir "TechBench.exe"
    $kb = Join-Path $script:InstallDir "air-compressor-kb\data\kb.json"
    $unins = Join-Path $script:InstallDir "unins000.exe"
    Write-Host "Step: check installed files"
    foreach ($required in @($script:InstallExe, $kb, $unins)) {
        if (-not (Test-Path -LiteralPath $required)) { Fail "Missing installed file: $required" }
        Write-Host "Found $required"
    }
    $script:ReadyToUninstall = $true

    $vi = (Get-Item -LiteralPath $script:InstallExe).VersionInfo
    $product = ([string]$vi.ProductVersion).Trim()
    $fileVer = ([string]$vi.FileVersion).Trim()
    $wantFile = $Expected + ".0"
    Write-Host "Step: ProductVersion=$product FileVersion=$fileVer"
    if (-not (Test-VersionPrefix $product $Expected)) {
        Fail "ProductVersion '$product' does not start with or equal $Expected"
    }
    if ($fileVer -ne $wantFile) {
        Fail "FileVersion '$fileVer' is not $wantFile"
    }

    # Same single-string command line so --smoke and --smoke-out stay separate arguments.
    # A shop TECHBENCH_KB would outrank the installed copy. Drop it for this process
    # so the child cannot inherit a higher-priority knowledge base.
    Remove-Item Env:TECHBENCH_KB -ErrorAction SilentlyContinue
    $smokeArgs = '--smoke --smoke-out "' + $outFile + '"'
    Write-Host "Step: run --smoke"
    $smokeProc = Start-Process -FilePath $script:InstallExe -WorkingDirectory $script:InstallDir -ArgumentList $smokeArgs -PassThru
    if (-not $smokeProc.WaitForExit(120000)) {
        Write-Host "Smoke timed out after 120s. Killing pid $($smokeProc.Id)"
        try { $smokeProc.Kill() } catch { Write-Host "Kill failed: $($_.Exception.Message)" }
        try { if (-not $smokeProc.HasExited) { $smokeProc.WaitForExit(5000) | Out-Null } } catch {}
        Fail "TechBench.exe --smoke did not exit within 120s"
    }
    try { $smokeProc.WaitForExit() | Out-Null } catch {}
    try { $smokeProc.Refresh() } catch {}
    Write-Host "Smoke exit code: $($smokeProc.ExitCode)"
    if ($null -eq $smokeProc.ExitCode -or $smokeProc.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $outFile) {
            Write-Host "Smoke report: $([string](Get-Content -LiteralPath $outFile -Raw))"
        }
        Fail "TechBench.exe --smoke failed with exit $($smokeProc.ExitCode)"
    }
    if (-not (Test-Path -LiteralPath $outFile)) { Fail "Smoke report was not written: $outFile" }
    $report = [string](Get-Content -LiteralPath $outFile -Raw)
    Write-Host "Smoke report: $report"
    if ([string]::IsNullOrWhiteSpace($report)) { Fail "Smoke report is empty" }
    if ($report -notlike "*smoke OK*") { Fail "Smoke report does not contain 'smoke OK'" }
    if ($report.IndexOf($Expected, [System.StringComparison]::Ordinal) -lt 0) {
        Fail "Smoke report does not contain version $Expected"
    }
    $installedKb = Join-Path $script:InstallDir 'air-compressor-kb'
    $kbMarker = 'kb=' + $installedKb
    if ($report.IndexOf($kbMarker, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Fail "Smoke report kb= path is not the installed knowledge base: $installedKb"
    }

    $script:ReadyToUninstall = $false
    Remove-SmokeInstall
    Write-Host "Smoke gate passed for $Expected"
    $exitCode = 0
}
catch {
    # exit inside try is a flow-control exception and would be caught here, so leave the process after the try.
    $message = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($message)) { $message = "$_" }
    Write-Host "ERROR: $message"
    if ($script:ReadyToUninstall) {
        $script:ReadyToUninstall = $false
        try { Remove-SmokeInstall } catch { Write-Host "Cleanup uninstall failed: $($_.Exception.Message)" }
    }
    $exitCode = 1
}
exit $exitCode
