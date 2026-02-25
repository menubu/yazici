#define MyAppName "MenuBu Printer Agent"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "MenuBu"
#define MyAppExeName "MenuBuPrinterAgent.exe"
#define PublishDir "..\publish\win-x64"
#define WebView2InstallerFile "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#define WebView2InstallerSource "dependencies\" + WebView2InstallerFile

[Setup]
AppId={{7DBA89CB-2F6B-42F5-A03B-57ED1D2C9AC4}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\MenuBu\PrinterAgent
DefaultGroupName=MenuBu
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MenuBuPrinterAgent-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Windows başlangıcında başlat"; GroupDescription: "Ek Seçenekler"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2InstallerSource}"; DestDir: "{tmp}"; DestName: "{#WebView2InstallerFile}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MenuBuPrinterAgent"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{tmp}\{#WebView2InstallerFile}"; Parameters: "/silent /install"; StatusMsg: "Microsoft Edge WebView2 Runtime kuruluyor..."; Flags: waituntilterminated; Check: ShouldInstallWebView2
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MenuBuPrinterAgent"

[Code]
function ShouldInstallWebView2: Boolean;
begin
  Result := FileExists(ExpandConstant('{tmp}\{#WebView2InstallerFile}'));
end;
