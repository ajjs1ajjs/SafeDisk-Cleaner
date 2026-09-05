; SafeDisk Cleaner installer
; Usage: iscc scripts/installer.iss (version comes from the CI tag)
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

; Root of the repository, relative to this script's directory (scripts\)
#ifndef Root
  #define Root ".."
#endif

#define MyAppName "SafeDisk Cleaner"
#define MyAppPublisher "SafeDisk"
#define MyAppExeName "SafeDiskCleaner.exe"

[Setup]
AppId={{7F4E8C2A-1D6B-4E5C-9A3B-6F8D2E4C1B0A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
SourceDir={#Root}
DefaultDirName={localappdata}\Programs\SafeDisk Cleaner
DefaultGroupName=SafeDisk Cleaner
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=src\SafeDiskCleaner.App\app-icon.ico
OutputDir=BUILD\ci
OutputBaseFilename=SafeDiskCleaner-{#MyAppVersion}-setup-win64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
CloseApplications=yes

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "BUILD\ci\portable\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "BUILD\ci\portable\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist uninsneveruninstall
Source: "scripts\run.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "src\SafeDiskCleaner.App\app-icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SafeDisk Cleaner"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon.ico"
Name: "{autodesktop}\SafeDisk Cleaner"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,SafeDisk Cleaner}"; Flags: nowait postinstall skipifsilent
