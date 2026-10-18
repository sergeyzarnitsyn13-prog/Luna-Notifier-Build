; Luna Notify — per-user install, no elevated permissions, no tray.
#define AppVersion "0.8.0"
[Setup]
AppId={{C02E263F-5C7B-4A52-B796-671937F6E8DA}
AppName=Luna Notify
AppVersion={#AppVersion}
AppVerName=Luna Notify {#AppVersion}
AppPublisher=РУСКЛИМАТ ИМ
DefaultDirName={localappdata}\Programs\Luna Notify
DefaultGroupName=РУСКЛИМАТ ИМ
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\Luna.Notify.exe
OutputDir=..\installer-output
OutputBaseFilename=Luna-Notify-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\luna.ico
CloseApplications=yes
RestartApplications=no
AppMutex=Local\RusklimatIM.LunaOwnerTest.v1
ArchitecturesAllowed=x64compatible x86compatible
UsePreviousAppDir=yes
SetupLogging=yes

[Files]
Source: "..\bin\Release\Luna.Notify.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RusklimatIM.LunaNotify"; ValueData: """{app}\Luna.Notify.exe"""; Flags: uninsdeletevalue

[Icons]
Name: "{group}\Luna Notify — проверить уведомление"; Filename: "{app}\Luna.Notify.exe"; Parameters: "--preview"; IconFilename: "{app}\Luna.Notify.exe"

[Run]
Filename: "{app}\Luna.Notify.exe"; Parameters: "--preview"; Description: "Запустить Luna Notify и показать тестовое уведомление"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function InitializeSetup(): Boolean;
var release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) and (release >= 528040);
  if not Result then
    MsgBox('Для Luna Notify требуется Microsoft .NET Framework 4.8 или новее.', mbError, MB_OK);
end;
