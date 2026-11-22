; Yetta preview installer. Distribution uses Inno Setup as a build tool;
; the application source itself is written for Yetta.
#define MyAppVersion "0.7.0-preview"

[Setup]
AppId={{FAAE9E3E-7B5F-494F-9CC1-2CE4D2F33C9F}
AppName=Yetta
AppVersion={#MyAppVersion}
AppVerName=Yetta {#MyAppVersion}
AppPublisher=Tallow
AppPublisherURL=https://github.com/Ketsuneban/Yetta
AppSupportURL=https://github.com/Ketsuneban/Yetta/issues
AppUpdatesURL=https://github.com/Ketsuneban/Yetta/releases
DefaultDirName={localappdata}\Programs\Yetta
DefaultGroupName=Yetta
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=..\artifacts\setup
OutputBaseFilename=YettaInstaller
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=Yetta.exe,Yetta.DAX.exe
RestartApplications=no
UninstallDisplayIcon={app}\Yetta.exe
VersionInfoVersion=0.7.0

[Files]
Source: "{tmp}\Yetta.exe"; DestDir: "{app}"; Flags: external ignoreversion; ExternalSize: {#PayloadBytes}
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Yetta"; Filename: "{app}\Yetta.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Yetta"; Filename: "{app}\Yetta.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки"; Flags: unchecked

[Run]
Filename: "{app}\Yetta.exe"; Description: "Запустить Yetta"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Yetta.exe"; Flags: nowait skipifnotsilent; Check: ShouldLaunch

[Code]
var
  DownloadPage: TDownloadWizardPage;

function ShouldLaunch(): Boolean;
begin
  Result := ExpandConstant('{param:NOLAUNCH|0}') <> '1';
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage('Загрузка Yetta', 'Подготовка полного приложения для Windows.', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Cached, Target: String;
begin
  Result := '';
  Cached := ExpandConstant('{param:PAYLOAD|}');
  Target := ExpandConstant('{tmp}\Yetta.exe');
  try
    if Cached <> '' then begin
      if not FileCopy(Cached, Target, False) then RaiseException('Не удалось прочитать загруженный файл Yetta.');
      if CompareText(GetSHA256OfFile(Target), '{#PayloadSHA}') <> 0 then
        RaiseException('Контрольная сумма Yetta.exe не совпала.');
    end else begin
      DownloadPage.Clear;
      DownloadPage.Add('https://github.com/Ketsuneban/Yetta/releases/download/v{#MyAppVersion}/Yetta.exe',
        'Yetta.exe', '{#PayloadSHA}');
      DownloadPage.Show;
      try
        DownloadPage.Download;
      finally
        DownloadPage.Hide;
      end;
    end;
  except
    Result := GetExceptionMessage;
  end;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (ExpandConstant('{param:PURGEYETTADATA|0}') = '1') then begin
    if not DelTree(ExpandConstant('{localappdata}\Yetta'), True, True, True) then
      Log('Could not remove the complete Yetta user data directory.');
    if not DelTree(GetTempDir + '\.net\Yetta', True, True, True) then
      Log('Could not remove the complete default Yetta runtime cache.');
  end;
end;
