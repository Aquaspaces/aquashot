; Inno Setup script for Aquashot.
; Compiled by ISCC; the release workflow passes the version and paths via /D defines:
;   ISCC /DMyAppVersion=1.2.3 /DPublishDir=..\publish /DOutputDir=..\dist installer\Aquashot.iss
; Defaults below let it compile standalone for local testing too.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "Aquashot"
#define MyAppExe "Aquashot.exe"
#define MyAppPublisher "Aquashot"
#define MyAppUrl "https://github.com/REPLACE_ME/aquashot"

[Setup]
; Stable AppId — do NOT change between releases (keeps upgrades/uninstall coherent).
AppId={{8F2A6C1E-3B4D-4E5F-9A7B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#MyAppExe}
OutputDir={#OutputDir}
OutputBaseFilename=Aquashot-{#MyAppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-machine install -> needs admin; targets x64 only.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Gracefully close a running tray instance during install/upgrade.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
