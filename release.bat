@echo off
setlocal EnableDelayedExpansion
rem Compile TechBench.exe, SHA-256 it, write latest.json, and build the per-user Inno installer.
rem latest.json url always points at the public dist repo (tech-bench-dist). Shop PCs never
rem get a GitHub token. The setup exe is what techs run; it also does not contain a token.

call "%~dp0build.bat"
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\assert-pe-i386.ps1" "%~dp0TechBench.exe"
if errorlevel 1 exit /b 1

for /f "tokens=2 delims==" %%V in ('findstr /C:"public const string Number" "%~dp0AppVersion.cs"') do (
  set VERSION=%%V
)
set VERSION=!VERSION:"=!
set VERSION=!VERSION: =!
set VERSION=!VERSION:;=!
if "!VERSION!"=="" (
  echo Could not read version from AppVersion.cs
  exit /b 1
)

for /f %%H in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 '%~dp0TechBench.exe').Hash.ToLowerInvariant()"') do set HASH=%%H
if "!HASH!"=="" (
  echo Could not hash TechBench.exe
  exit /b 1
)

set URL=https://github.com/blackviperxiii-ui/tech-bench-dist/releases/download/v!VERSION!/TechBench.exe
> "%~dp0latest.json" (
  echo {
  echo   "version": "!VERSION!",
  echo   "sha256": "!HASH!",
  echo   "url": "!URL!"
  echo }
)

call "%~dp0installer\build.bat"
if errorlevel 1 exit /b 1

echo.
echo Release !VERSION!
echo   TechBench.exe
echo   sha256 !HASH!
echo   latest.json written
echo   dist\TechBench-Setup-!VERSION!.exe
echo.
echo Give techs the Setup exe (Start Menu + Desktop shortcuts, no admin).
echo Upload TechBench.exe and latest.json to the public dist repo
echo   https://github.com/blackviperxiii-ui/tech-bench-dist/releases
echo so Help → Check for updates can GET them without a login.
echo CI does that when DIST_REPO_TOKEN is set; otherwise upload the two files
echo onto the v!VERSION! release by hand. Shop override: update-url.txt next to the installed exe.
endlocal
