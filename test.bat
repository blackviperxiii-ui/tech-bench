@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, adapter discovery, trend, timeline,
rem unit history, KB load/search, user-code round-trip, snapshot diff, report text,
rem settings, latest.json / SHA-256 updater, and two-way shop sync.
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
  BusMonitor.cs BusWorker.cs Trend.cs Timeline.cs History.cs JobReport.cs SessionData.cs ^
  KbIndex.cs UserCodes.cs ShopSync.cs AppVersion.cs AppSettings.cs Updater.cs
if errorlevel 1 exit /b 1
SelfTest.exe
