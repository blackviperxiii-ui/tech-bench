@echo off
setlocal EnableDelayedExpansion
rem Compile TechBench-Setup-<version>.exe with Inno Setup (ISCC).
rem Version always comes from AppVersion.cs — same stamp as the app and latest.json.
rem Does not embed a GitHub token. Techs only need the finished setup exe.

set ROOT=%~dp0..
for %%I in ("%ROOT%") do set ROOT=%%~fI

for /f "tokens=2 delims==" %%V in ('findstr /C:"public const string Number" "%ROOT%\AppVersion.cs"') do (
  set VERSION=%%V
)
set VERSION=!VERSION:"=!
set VERSION=!VERSION: =!
set VERSION=!VERSION:;=!
if "!VERSION!"=="" (
  echo Could not read version from AppVersion.cs
  exit /b 1
)

if not exist "%ROOT%\TechBench.exe" (
  echo TechBench.exe missing — building it first.
  call "%ROOT%\build.bat"
  if errorlevel 1 exit /b 1
)
if not exist "%ROOT%\assets\app.ico" (
  echo Missing %ROOT%\assets\app.ico
  exit /b 1
)

set ISCC=
if exist "%ROOT%\tools\innosetup\ISCC.exe" set "ISCC=%ROOT%\tools\innosetup\ISCC.exe"
if "!ISCC!"=="" if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if "!ISCC!"=="" if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if "!ISCC!"=="" if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if "!ISCC!"=="" (
  for /f "delims=" %%P in ('where ISCC 2^>nul') do (
    if "!ISCC!"=="" set "ISCC=%%P"
  )
)

if "!ISCC!"=="" (
  echo Inno Setup compiler not found — fetching a local copy into tools\innosetup
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-iscc.ps1"
  if errorlevel 1 exit /b 1
  if exist "%ROOT%\tools\innosetup\ISCC.exe" set "ISCC=%ROOT%\tools\innosetup\ISCC.exe"
)
if "!ISCC!"=="" if exist "%ROOT%\tools\innosetup\ISCC.exe" set "ISCC=%ROOT%\tools\innosetup\ISCC.exe"
if "!ISCC!"=="" (
  for /f "delims=" %%P in ('dir /s /b "%ROOT%\tools\innosetup\ISCC.exe" 2^>nul') do (
    if "!ISCC!"=="" set "ISCC=%%P"
  )
)
if "!ISCC!"=="" (
  echo Could not find ISCC.exe. Install Inno Setup 6 and re-run, or put ISCC.exe under tools\innosetup.
  exit /b 1
)

if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"

echo Compiling installer with:
echo   ISCC    !ISCC!
echo   version !VERSION!
"!ISCC!" /DMyAppVersion=!VERSION! "%~dp0techbench.iss"
if errorlevel 1 exit /b 1

if not exist "%ROOT%\dist\TechBench-Setup-!VERSION!.exe" (
  echo Installer was not written to dist\TechBench-Setup-!VERSION!.exe
  exit /b 1
)

echo Installer written:
echo   dist\TechBench-Setup-!VERSION!.exe
endlocal
