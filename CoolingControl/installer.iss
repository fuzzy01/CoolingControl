; Inno Setup script for CoolingControl
#define MyAppName "CoolingControl"
#define MyAppVersion "1.1.10.0"
#define MyAppPublisher "Fuzzy01 - Peter Laszlo"
#define MyAppExeName "CoolingControl.exe"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.\output
OutputBaseFilename=CoolingControlSetup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64os
CloseApplications=force
SetupIconFile=.\cooling_control.ico
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startuptray"; Description: "Start CoolingControl tray at login"; GroupDescription: "Additional icons:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "config\*"
Source: "publish\config\config.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "publish\config\cooling_control.lua"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "publish\config\config_sample.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_functions.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_aio_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_aio_with_sensor_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_aircooling_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_cfg_access_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_gpu_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_profiles_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion

[Icons]
Name: "{group}\CoolingControl Tray"; Filename: "{app}\CoolingControlTray.exe"; WorkingDir: "{app}"; AppUserModelID: "CoolingControl.Tray"
Name: "{commonstartup}\CoolingControl Tray"; Filename: "{app}\CoolingControlTray.exe"; WorkingDir: "{app}"; Tasks: startuptray; AppUserModelID: "CoolingControl.Tray"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create CoolingControl binPath= ""{app}\{#MyAppExeName}"" DisplayName= ""CoolingControl"" start= auto"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description CoolingControl ""Service that monitors hardware sensors and applies control settings based on a user-defined script"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure CoolingControl reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failureflag CoolingControl 1"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start CoolingControl"; Description: "Start Windows Service"; Flags: runhidden waituntilterminated;
Filename: "{app}\CoolingControlTray.exe"; Description: "Start the CoolingControl tray"; Flags: nowait postinstall runasoriginaluser; Tasks: startuptray

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM CoolingControlTray.exe"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "stop CoolingControl"; Flags: runhidden waituntilterminated
Filename: "{sys}\timeout.exe"; Parameters: "/T 5"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM CoolingControlTray.exe /F"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete CoolingControl"; Flags: runhidden waituntilterminated

[Code]
procedure InitializeWizard;
begin
  WizardForm.Show;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  begin
    if FileExists(ExpandConstant('{app}\CoolingControlTray.exe')) then
    begin
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM CoolingControlTray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if (ResultCode <> 0) and (ResultCode <> 128) then
        Log('Failed to close CoolingControlTray. ResultCode: ' + IntToStr(ResultCode));
    end;
    if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
    begin
      Exec('sc.exe', 'stop CoolingControl', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        Log('Failed to stop CoolingControl service. ResultCode: ' + IntToStr(ResultCode));
      Exec(ExpandConstant('{sys}\timeout.exe'), '/T 5 /NOBREAK', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;