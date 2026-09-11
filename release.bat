@echo off
setlocal EnableDelayedExpansion
rem Compile TechBench.exe, SHA-256 it, write latest.json, and build the per-user Inno installer.
rem Upload TechBench.exe and latest.json to a *public* HTTPS location (GitHub Release on a
rem public repo, or any file host). Shop PCs never get a GitHub token. The setup exe is what
rem techs run; it also does not contain a token.

call "%~dp0build.bat"
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

set URL=https://github.com/blackviperxiii-ui/tech-bench/releases/download/v!VERSION!/TechBench.exe
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
echo Upload TechBench.exe and latest.json where HTTPS GET works without a login —
echo the in-app updater replaces the *installed* TechBench.exe, not a Desktop copy.
echo If this GitHub repo stays private, do not use github.com/.../releases/... as the URL —
echo put both files on a public host and, on shop PCs, a one-line update-url.txt next to the installed exe.
endlocal
