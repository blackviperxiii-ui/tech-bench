; Tech Bench shop installer (Inno Setup 6).
; Per-user / asInvoker so a tech does not need admin. RP1210 adapter drivers are a
; separate vendor package (Cummins / Noregon / etc.) and may still need admin — this
; script never ships those DLLs and never embeds a GitHub token or PAT.
;
; Compiled by installer\build.bat (called from release.bat) with:
;   ISCC /DMyAppVersion=<AppVersion.Number> installer\techbench.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "Tech Bench"
#define MyAppExeName "TechBench.exe"

[Setup]
AppId={{9F2C1A8E-4B7D-45E3-A6C1-8D0E5B7A3192}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Tech Bench
AppCopyright=Private shop tool
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\TechBench
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; User-writable folder so Help → Check for updates can replace TechBench.exe in place.
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=TechBench-Setup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
MinVersion=6.1sp1
AllowNoIcons=yes
DirExistsWarning=no
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on this PC.%n%nNo administrator account is required. INLINE 7 / RP1210 adapter drivers are not included — install those from the adapter vendor if you need live data.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion
; Field database (fault codes, service access, manual index, filters, equipment).
; Same JSON is embedded in TechBench.exe. This folder is what a normal install searches.
Source: "..\kb\*"; DestDir: "{app}\air-compressor-kb"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\TechBench.exe.new"
Type: files; Name: "{app}\TechBench.exe.new.sha256"
Type: files; Name: "{app}\apply-update.cmd"
Type: files; Name: "{app}\TechBench.exe.bak"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Tech Bench"; Flags: nowait postinstall skipifsilent

[Code]
function HasDotNet4: Boolean;
var
  Release: Cardinal;
begin
  Result :=
    RegQueryDWordValue(HKLM, 'Software\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    or RegQueryDWordValue(HKLM32, 'Software\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasDotNet4 then
  begin
    MsgBox('Tech Bench needs the 32-bit .NET Framework 4.x runtime (already on most shop PCs).' + #13#10 + #13#10 +
      'Install that runtime, then run this setup again. Adapter/RP1210 drivers are separate and are not installed here.',
      mbCriticalError, MB_OK);
    Result := False;
  end;
end;
