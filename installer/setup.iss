#define AppVersion "0.2.0-beta"
[Setup]
AppId=AppShelf.Desktop
AppName=AppShelf
AppVersion={#AppVersion}
AppPublisher=AppShelf contributors
DefaultDirName={localappdata}\Programs\AppShelf
DefaultGroupName=AppShelf
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=AppShelf-Setup-{#AppVersion}
UninstallDisplayIcon={app}\AppShelf.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\AppShelf.ico
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
VersionInfoVersion=0.2.0.0
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "..\AppShelf.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\AppShelf"; Filename: "{app}\AppShelf.exe"
Name: "{group}\Удалить AppShelf"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AppShelf"; Filename: "{app}\AppShelf.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\AppShelf.exe"; Description: "Открыть AppShelf"; Flags: nowait postinstall skipifsilent
; Intentionally preserve user registry backups in %LOCALAPPDATA%\AppShelf on uninstall.

