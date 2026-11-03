; =====================================================================
; WinterTray - Inno Setup Telepítő Szkript (.iss)
; Használat:
;   1. Nyisd meg az Inno Setup Compiler alkalmazásban, majd nyomj Ctrl+F9-et (Compile), VAGY
;   2. Parancssorból: "C:\Program Files\Inno Setup 7\ISCC.exe" "Installer\WinterTray.iss"
; A kész telepítő a ..\Output\WinterTray-Setup-v1.0.exe helyre kerül.
; =====================================================================

#define MyAppName "WinterTray"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WinterTray"
#define MyAppExeName "WinterTray.exe"

[Setup]
AppId={{9B7E348F-7F01-4A59-86BC-9B84E347A211}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\Output
OutputBaseFilename=WinterTray-Setup-v1.0
SetupIconFile=..\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=force
RestartApplications=no
UsedUserAreasWarning=no

[Languages]
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Automatikus indítás a Windows bejelentkezésekor"; GroupDescription: "Rendszerbeállítások:"

[Files]
Source: "..\App_Release\*"; DestDir: "{app}"; Excludes: "*.pdb,*.old"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Resources\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Resources\app.ico"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinterTray"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WinterTray"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillWinterTray"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Telepítés előtt leállítjuk az esetleg futó WinterTray példányt, hogy a fájlok felülírhatók legyenek
  Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
