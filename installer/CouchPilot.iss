; Inno Setup script for CouchPilot.
; Per-user install, so it never needs administrator rights. The app itself
; runs unelevated on purpose, and only elevates for the one powercfg call
; that arms controller wake.

#define AppName    "CouchPilot"
#define AppVersion GetEnv('COUCHPILOT_VERSION')
#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{8E2C0A51-4C6F-4E63-9A2B-1E8D0C4F77A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CouchPilot
AppSupportURL=https://github.com/BIGDANO-86/couchpilot
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=CouchPilot-Setup-{#AppVersion}
SetupIconFile=..\assets\couchpilot.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\CouchPilot.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\CouchPilot.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\CouchPilot.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\CouchPilot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CouchPilot.exe"; Description: "Start CouchPilot"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app adds its own Run key; this clears it if it is still there.
Type: files; Name: "{userappdata}\CouchPilot\couchpilot.log"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'CouchPilot');
end;
