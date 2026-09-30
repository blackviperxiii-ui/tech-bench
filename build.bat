@echo off
rem Response files are relative to the current directory, so build from the repo root.
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
rem /platform:x86 is required: RP1210 adapter drivers are 32-bit only.
rem Source list: sources\core.rsp and sources\app.rsp. Add new files there, not in this script.
"%CSC%" /nologo /platform:x86 /target:winexe /optimize ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Security.dll" ^
  /win32icon:assets\app.ico ^
  /win32manifest:app.manifest ^
  /resource:kb\data\kb.json,TechBench.BundledKb.data.kb.json ^
  /resource:kb\data\passwords\ifix-passwords.json,TechBench.BundledKb.data.passwords.ifix-passwords.json ^
  /resource:kb\data\usb-manuals.json,TechBench.BundledKb.data.usb-manuals.json ^
  /resource:kb\data\rental-portable-filters-oil.json,TechBench.BundledKb.data.rental-portable-filters-oil.json ^
  /resource:kb\data\rental-equipment-info.json,TechBench.BundledKb.data.rental-equipment-info.json ^
  /out:TechBench.exe ^
  @sources\core.rsp @sources\app.rsp
if errorlevel 1 exit /b 1
echo Built TechBench.exe
