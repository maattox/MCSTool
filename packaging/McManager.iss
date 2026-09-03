; MCSTool — Inno Setup 6 per-user installer.
; Built by packaging\pack.ps1. Do not compile this script until pack.ps1 has
; published into packaging\out\publish\ (including the Function tar).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "MCSTool"
#define MyAppPublisher "maattox"
#define MyAppURL "https://github.com/maattox/MCSTool"
#define MyAppExeName "McManager.Hybrid.exe"

[Setup]
AppId={{D2F7A6ED-DBA3-4D2E-B67B-DE983C7C82C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\MCSTool
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user: no UAC, not Program Files.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=MCSTool-Setup-{#MyAppVersion}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\server-icons\default-icon.ico
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=no
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
MinVersion=10.0
; Unsigned is expected (code-signing cert is deferred).

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; P1 publish layout: exe + infra/ + on-box trees + Function tar next to the exe.
Source: "out\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Open {#MyAppName}"; Flags: nowait postinstall skipifsilent
