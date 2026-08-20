@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Missing .NET 4 csc at %CSC%
  exit /b 1
)
"%CSC%" /nologo /platform:x86 /target:winexe /optimize ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /win32icon:assets\app.ico ^
  /out:TechBench.exe ^
  Program.cs ShellForm.cs SearchControl.cs KbIndex.cs ^
  MainForm.cs Rp1210.cs J1939Decode.cs CodeBook.cs FeatureBook.cs Bam.cs Session.cs
if errorlevel 1 exit /b 1
echo Built TechBench.exe
