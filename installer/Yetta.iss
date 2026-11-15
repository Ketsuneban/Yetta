; Yetta preview installer. Distribution uses Inno Setup as a build tool;
; the application source itself is written for Yetta.
#define MyAppVersion "0.3.10-preview"

[Setup]
AppId={{FAAE9E3E-7B5F-494F-9CC1-2CE4D2F33C9F}
AppName=Yetta
AppVersion={#MyAppVersion}
AppVerName=Yetta {#MyAppVersion}
AppPublisher=Yetta contributors
AppPublisherURL=https://github.com/Ketsuneban/Yetta
AppSupportURL=https://github.com/Ketsuneban/Yetta/issues
AppUpdatesURL=https://github.com/Ketsuneban/Yetta/releases
DefaultDirName={localappdata}\Programs\Yetta
DefaultGroupName=Yetta
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=..\artifacts\setup
OutputBaseFilename=YettaSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Yetta.exe
VersionInfoVersion=0.3.10

[Files]
Source: "..\artifacts\Yetta-win-x64\Yetta.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\Yetta.Desktop\Assets\OFL-Inter.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Yetta"; Filename: "{app}\Yetta.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Yetta"; Filename: "{app}\Yetta.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки"; Flags: unchecked

[Run]
Filename: "{app}\Yetta.exe"; Description: "Запустить Yetta"; Flags: nowait postinstall skipifsilent
