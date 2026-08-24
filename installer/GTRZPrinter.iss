#define MyAppName "GTRZ Printer"
#define MyAppVersion "2.0.1"
#define MyAppExeName "GTRZ Printer.exe"

[Setup]
AppId={{A77B4D74-9922-4D26-901E-1C95B9DA6B61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=GTRZ
DefaultDirName={autopf}\GTRZ Printer
DefaultGroupName=GTRZ Printer
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=GTRZ-Printer-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GTRZ Printer"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GTRZ Printer"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir GTRZ Printer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C schtasks /Delete /TN ""GTRZ Printer"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""GTRZ Printer IPP"""; Flags: runhidden; RunOnceId: "RemoveFirewallIPP"
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""GTRZ Printer API"""; Flags: runhidden; RunOnceId: "RemoveFirewallAPI"
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""GTRZ Printer Discovery"""; Flags: runhidden; RunOnceId: "RemoveFirewallDiscovery"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{cmd}'), '/C taskkill /IM "GTRZ Printer.exe" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/C taskkill /IM "GTRZPrinter.exe" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/C schtasks /Delete /TN "GTRZ Printer" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
