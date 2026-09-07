; Namestitveni skript za Hidria EC - Modbus zapisovalnik (Inno Setup 6).
; Zgradi ga: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ModbusLogger.iss
;
; Namestitev je per-user (brez admin pravic), ker aplikacija piše nastavitve
; (devices.json, profili, CSV logi) neposredno v mapo poleg .exe.

#define MyAppName "Hidria EC - Modbus zapisovalnik"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Hidria"
#define MyAppExeName "ModbusLogger.App.exe"

[Setup]
AppId={{9F3E9C9E-6C1D-4E7A-9C2B-3B7B3C9A2F10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ModbusLogger
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=ModbusLoggerSetup
SetupIconFile=..\src\ModbusLogger.App\Assets\hidria.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Ustvari bližnjico na namizju"; GroupDescription: "Dodatne ikone:"; Flags: unchecked

[Files]
Source: "..\dist\ModbusLogger.App.exe"; DestDir: "{app}"; Flags: ignoreversion
; Prvotna nastavitvena datoteka in profil - namestita se samo, če še ne obstajata (da se ob nadgradnji
; ne prepiše uporabnikova živa konfiguracija) in se nikoli ne izbrišeta ob odstranitvi programa.
Source: "..\dist\config\devices.json"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\dist\config\profiles\hidria-ec-fan.json"; DestDir: "{app}\config\profiles"; Flags: onlyifdoesntexist uninsneveruninstall
; Navodila za uporabo (HTML, po jezikih) - vedno posodobljena na najnovejšo verzijo ob nadgradnji.
Source: "..\dist\docs\*.html"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Odmesti {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Zaženi {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
  BasePath: string;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(BasePath) then
  begin
    if FindFirst(BasePath + '\8.*', FindRec) then
    begin
      Result := True;
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    if MsgBox('Ta program potrebuje .NET 8 Desktop Runtime (x64), ki na tem računalniku ni zaznan.' + #13#10 + #13#10 +
      'Namestitev lahko nadaljujete, vendar se program ne bo zagnal, dokler ne namestite .NET 8 Desktop Runtime.' + #13#10 + #13#10 +
      'Ali želite zdaj odpreti stran za prenos?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime', '', '', SW_SHOW, ewNoWait, ErrorCode);
  end;
end;
