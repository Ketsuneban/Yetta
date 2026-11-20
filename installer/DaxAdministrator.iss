; Optional Windows service installed separately from the per-user Yetta app.
[Setup]
AppId=YettaDaxAdministrator
AppName=Yetta DAX Administrator
AppVersion=0.6.2
AppPublisher=Tallow
DefaultDirName={autopf}\Yetta DAX
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\setup
OutputBaseFilename=YettaDaxAdministratorSetup
Compression=lzma2/fast
WizardStyle=modern
CloseApplications=no
RestartApplications=no
MinVersion=10.0.22000
InfoBeforeFile=DaxAdministrator-setup.txt

[Files]
Source: "..\artifacts\DaxAdministrator\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Code]
function RunHelper(const Argument: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(ExpandConstant('{app}\Yetta.DAX.Administrator.exe'), Argument,
    ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Directory: String;
begin
  Result := '';
  Directory := ExpandConstant('{app}');
  if CompareText(Directory, ExpandConstant('{autopf}\Yetta DAX')) <> 0 then begin
    Result := 'DAX Administrator requires its fixed Program Files directory.';
    exit;
  end;
  if FileExists(Directory + '\Yetta.DAX.Administrator.exe') and not RunHelper('--stop') then
    Result := 'Could not stop the installed DAX Administrator service.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not RunHelper('--configure') then
    RaiseException('DAX Administrator could not be configured. Check the Windows Event Log and run setup again.');
end;

function InitializeUninstall(): Boolean;
begin
  Result := RunHelper('--remove');
  if not Result then
    MsgBox('Could not stop or remove the DAX Administrator service.', mbError, MB_OK);
end;
