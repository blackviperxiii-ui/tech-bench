@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, DM11/DM3 code-clear construction, adapter discovery, trend, timeline,
rem unit history, KB load/search, user-code round-trip, snapshot diff, report text,
rem settings, latest.json / SHA-256 updater, Inno Setup script checks, two-way shop sync,
rem work-order packets / IntelliDealer gateway (no live DMS),
rem and shop-laptop WinForms layout (primary buttons stay on screen).
rem Needs no adapter. Sample KB data is built in %TEMP%. The shipped field database under kb\ is loaded as-is.
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
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Security.dll" ^
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
