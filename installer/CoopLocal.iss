; Instalador grafico (setup.exe) de Blasphemous CoopLocal - Inno Setup 6 (https://jrsoftware.org/isinfo.php)
; Compilar: iscc installer\CoopLocal.iss   (antes ejecuta installer\make-release.ps1 para preparar dist\payload)
#define AppName "Blasphemous CoopLocal"
#define AppVersion "1.0.0"

[Setup]
AppId={{B7A5C3E0-6C6B-4D3B-9E57-0C00FE100001}
AppName={#AppName}

DefaultDirName={code:FindGameDir}
DisableProgramGroupPage=yes
DisableDirPage=no
DirExistsWarning=no
AppendDefaultDirName=no
Uninstallable=yes
UninstallFilesDir={app}\Modding\CoopLocal_uninstall
OutputDir=..\dist
OutputBaseFilename=BlasphemousCoopLocal_Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\payload\*"; DestDir: "{app}\Modding"; Flags: recursesubdirs createallsubdirs ignoreversion

[Code]
function IsGame(Dir: string): Boolean;
begin
  Result := FileExists(AddBackslash(Dir) + 'Blasphemous.exe');
end;

function TryLibrary(Root: string; var Found: string): Boolean;
begin
  Found := AddBackslash(Root) + 'steamapps\common\Blasphemous';
  Result := IsGame(Found);
end;

function FindGameDir(Param: string): string;
var Steam, Found: string;
begin
  Result := ExpandConstant('{pf32}\Steam\steamapps\common\Blasphemous');
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Valve\Steam', 'InstallPath', Steam) or
     RegQueryStringValue(HKCU, 'SOFTWARE\Valve\Steam', 'SteamPath', Steam) then
    if TryLibrary(Steam, Found) then begin Result := Found; Exit; end;
  if TryLibrary('D:\SteamLibrary', Found) then Result := Found
  else if TryLibrary('D:\Steam', Found) then Result := Found
  else if TryLibrary('E:\SteamLibrary', Found) then Result := Found;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if not IsGame(WizardDirValue) then
    begin
      MsgBox('No se encuentra Blasphemous.exe en esa carpeta. Elige la carpeta del juego.', mbError, MB_OK);
      Result := False;
    end
    else if not FileExists(AddBackslash(WizardDirValue) + 'Modding\plugins\ModdingAPI.dll') then
    begin
      MsgBox('Falta el Modding de Blasphemous (BepInEx + ModdingAPI). Instalalo primero y vuelve a ejecutar este instalador.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
