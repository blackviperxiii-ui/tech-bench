@echo off
rem Offline self-test: J1939 decoding, BAM reassembly, KB load/search.
rem Needs no adapter and no knowledge base — it builds its own sample data in %TEMP%.
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Missing .NET 4 csc at %CSC%
  exit /b 1
)
"%CSC%" /nologo /platform:x86 /target:exe ^
  /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll" ^
  /out:SelfTest.exe ^
  SelfTest.cs J1939Decode.cs Bam.cs Rp1210.cs KbIndex.cs
if errorlevel 1 exit /b 1
SelfTest.exe
