@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, DM11/DM3 code-clear construction, adapter discovery, trend, timeline,
rem unit history, KB load/search, user-code round-trip, snapshot diff, report text,
rem settings, latest.json / SHA-256 updater, Inno Setup script checks, two-way shop sync,
rem work-order packets / IntelliDealer gateway (no live DMS),
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
  SelfTest.cs J1939Decode.cs Names.cs Bam.cs Rp1210.cs Rp1210Api.cs Ini.cs J1939Clear.cs ^
  BusMonitor.cs BusWorker.cs Trend.cs Timeline.cs History.cs JobReport.cs SessionData.cs ^
  KbIndex.cs UserCodes.cs ShopSync.cs AppVersion.cs AppSettings.cs Updater.cs ^
  WorkOrder.cs WorkOrderStore.cs IdSettings.cs IdGateway.cs
if errorlevel 1 exit /b 1
SelfTest.exe
if errorlevel 1 exit /b 1

"%CSC%" /nologo /platform:x86 /target:exe /main:LayoutAudit ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /out:LayoutAudit.exe ^
  tools\LayoutAudit.cs Program.cs ShellForm.cs SearchControl.cs CodeEditForm.cs NoteEditForm.cs SyncForm.cs ^
  KbIndex.cs UserCodes.cs ShopSync.cs AppVersion.cs AppSettings.cs Updater.cs UiLayout.cs ^
  WorkOrder.cs WorkOrderStore.cs WorkOrderControl.cs IdSettings.cs IdSettingsForm.cs IdGateway.cs ^
  Inline7Control.cs TrendChart.cs Rp1210.cs Rp1210Api.cs Ini.cs J1939Clear.cs ^
  BusMonitor.cs BusWorker.cs Trend.cs Timeline.cs History.cs JobReport.cs JobReportPrint.cs ^
  J1939Decode.cs Names.cs CodeBook.cs FeatureBook.cs Bam.cs Session.cs SessionData.cs
if errorlevel 1 exit /b 1
LayoutAudit.exe
if errorlevel 1 exit /b 1
