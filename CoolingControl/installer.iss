; Inno Setup script for CollingControl
#define MyAppName "CoolingControl"
#define MyAppVersion "1.0.0.0"
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
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=.\cooling_control.ico
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
;Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\config\config.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "publish\config\cooling_control.lua"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "publish\config\config_sample.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_functions.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_aio_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "publish\config\cooling_control_aircooling_sample.lua"; DestDir: "{app}\config"; Flags: ignoreversion

[Icons]
;Name: "{group}\{#MyAppName} Console"; Filename: "{app}\{#MyAppExeName}"; Parameters: "console"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
;Name: "{autodesktop}\{#MyAppName} Console"; Filename: "{app}\{#MyAppExeName}"; Parameters: "console"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create CoolingControl binPath= ""{app}\{#MyAppExeName}"" DisplayName= ""CoolingControl"" start= auto"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description CoolingControl ""Service that monitors hardware sensors and applies control settings based on a user-defined script"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure CoolingControl reset= 0 actions= restart/5000/restart/5000/none/0"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start CoolingControl"; Description: "Start Windows Service"; Flags: runhidden waituntilterminated;

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop CoolingControl"; Flags: runhidden waituntilterminated
Filename: "{sys}\timeout.exe"; Parameters: "/T 5"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete CoolingControl"; Flags: runhidden waituntilterminated

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  begin
    if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
    begin
      Exec('sc.exe', 'stop CoolingControl', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        Log('Failed to stop CoolingControl service. ResultCode: ' + IntToStr(ResultCode));
    end;
  end;
end;