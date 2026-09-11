@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, adapter discovery, trend, timeline,
rem unit history, KB load/search, user-code round-trip, snapshot diff, report text,
rem and shop-laptop WinForms layout (primary buttons stay on screen).
rem Needs no adapter and no knowledge base — it builds its own sample data in %TEMP%.
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Missing .NET 4 csc at %CSC%
  exit /b 1
)
"%CSC%" /nologo /platform:x86 /target:exe ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /out:SelfTest.exe ^
  SelfTest.cs J1939Decode.cs Names.cs Bam.cs Rp1210.cs Rp1210Api.cs Ini.cs ^
  BusMonitor.cs Trend.cs Timeline.cs History.cs JobReport.cs SessionData.cs ^
  KbIndex.cs UserCodes.cs
if errorlevel 1 exit /b 1
SelfTest.exe
if errorlevel 1 exit /b 1

"%CSC%" /nologo /platform:x86 /target:exe /main:LayoutAudit ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /out:LayoutAudit.exe ^
  tools\LayoutAudit.cs Program.cs ShellForm.cs SearchControl.cs CodeEditForm.cs KbIndex.cs UserCodes.cs UiLayout.cs ^
  Inline7Control.cs TrendChart.cs Rp1210.cs Rp1210Api.cs Ini.cs ^
  BusMonitor.cs BusWorker.cs Trend.cs Timeline.cs History.cs JobReport.cs JobReportPrint.cs ^
  J1939Decode.cs Names.cs CodeBook.cs FeatureBook.cs Bam.cs Session.cs SessionData.cs
if errorlevel 1 exit /b 1
LayoutAudit.exe
if errorlevel 1 exit /b 1
