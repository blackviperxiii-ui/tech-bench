@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Missing .NET 4 csc at %CSC%
  exit /b 1
)
rem /platform:x86 is required: RP1210 adapter drivers are 32-bit only.
"%CSC%" /nologo /platform:x86 /target:winexe /optimize ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /win32icon:assets\app.ico ^
  /win32manifest:app.manifest ^
  /resource:kb\data\kb.json,TechBench.BundledKb.data.kb.json ^
  /resource:kb\data\passwords\ifix-passwords.json,TechBench.BundledKb.data.passwords.ifix-passwords.json ^
  /resource:kb\data\usb-manuals.json,TechBench.BundledKb.data.usb-manuals.json ^
  /resource:kb\data\rental-portable-filters-oil.json,TechBench.BundledKb.data.rental-portable-filters-oil.json ^
  /resource:kb\data\rental-equipment-info.json,TechBench.BundledKb.data.rental-equipment-info.json ^
  /out:TechBench.exe ^
  Program.cs ShellForm.cs SearchControl.cs CodeEditForm.cs NoteEditForm.cs SyncForm.cs ^
  KbIndex.cs UserCodes.cs ShopSync.cs AppVersion.cs AppSettings.cs Updater.cs UiLayout.cs ^
  WorkOrder.cs WorkOrderStore.cs WorkOrderControl.cs IdSettings.cs IdSettingsForm.cs IdGateway.cs ^
  Inline7Control.cs TrendChart.cs Rp1210.cs Rp1210Api.cs Ini.cs J1939Clear.cs ^
  BusMonitor.cs BusWorker.cs Trend.cs Timeline.cs History.cs JobReport.cs JobReportPrint.cs ^
  J1939Decode.cs Names.cs CodeBook.cs FeatureBook.cs Bam.cs Session.cs SessionData.cs
if errorlevel 1 exit /b 1
echo Built TechBench.exe
