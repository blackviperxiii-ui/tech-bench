@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, DM11/DM3 code clear (simulated ECMs), adapter discovery, trend, timeline,
rem unit history, KB load/search, user-code round-trip, snapshot diff, report text,
rem settings, latest.json / SHA-256 updater, Inno Setup script checks, two-way shop sync,
rem work-order packets / IntelliDealer gateway (no live DMS),
rem and shop-laptop WinForms layout (primary buttons stay on screen).
rem Needs no adapter. Sample KB data is built in %TEMP%. The shipped field database under kb\ is loaded as-is.
rem Source list: sources\core.rsp (SelfTest) and sources\core.rsp plus sources\app.rsp (LayoutAudit).
rem Add new source files to those response files. This script only names SelfTest.cs and tools\LayoutAudit.cs.
rem Response files are relative to the current directory, so test from the repo root.
cd /d "%~dp0."
if not exist "sources\core.rsp" (
  echo Could not enter the repo root
  exit /b 1
)
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Missing .NET 4 csc at %CSC%
  exit /b 1
)
"%CSC%" /nologo /platform:x86 /target:exe ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Security.dll" ^
  /resource:kb\data\kb.json,TechBench.BundledKb.data.kb.json ^
  /resource:kb\data\passwords\ifix-passwords.json,TechBench.BundledKb.data.passwords.ifix-passwords.json ^
  /resource:kb\data\usb-manuals.json,TechBench.BundledKb.data.usb-manuals.json ^
  /resource:kb\data\rental-portable-filters-oil.json,TechBench.BundledKb.data.rental-portable-filters-oil.json ^
  /resource:kb\data\rental-equipment-info.json,TechBench.BundledKb.data.rental-equipment-info.json ^
  /out:SelfTest.exe ^
  SelfTest.cs @sources\core.rsp
if errorlevel 1 exit /b 1
SelfTest.exe
if errorlevel 1 exit /b 1

"%CSC%" /nologo /platform:x86 /target:exe /main:LayoutAudit ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Security.dll" ^
  /out:LayoutAudit.exe ^
  tools\LayoutAudit.cs @sources\core.rsp @sources\app.rsp
if errorlevel 1 exit /b 1
LayoutAudit.exe
if errorlevel 1 exit /b 1
