; MC Manager — Inno Setup 6 per-user installer.
; Built by packaging\pack.ps1. Do not compile this script until pack.ps1 has
; published into packaging\out\publish\ (including the Function tar).

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName "MC Manager"
#define MyAppPublisher "maattox"
#define MyAppURL "https://github.com/maattox/oci-mc-server"
#define MyAppExeName "McManager.Hybrid.exe"

[Setup]
AppId={{B3E8C5A2-7F14-4D9E-A6C1-9E2F0B8D4A71}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\MC Manager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user: no UAC, not Program Files.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=MCManager-Setup-{#MyAppVersion}
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
; Unsigned is expected for closed beta (code-signing cert is deferred).

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
