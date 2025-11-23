#define MyAppName "Image Occlusion Editor"
; PublishDir can be injected from the build script or csproj via /DPublishDir="..."
#ifndef PublishDir
#define PublishDir "..\ImageOcclusionEditorWinUI3\bin\x64\Release\net9.0-windows10.0.22000.0\win-x64\publish"
#endif
; Use PublishDir directly; caller must pass a clean path without trailing backslash
#define MyAppExePath PublishDir + "\ImageOcclusionEditor.exe"
#define MyAppVersion GetVersionNumbersString(MyAppExePath)
#define MyAppPublisher "Shuai Zhang"
#define MyAppURL "https://github.com/hcoona/ImageOcclusionEditor"
#define MyAppExeName "ImageOcclusionEditor.exe"
#define MyAppDescription "Application for creating image occlusion cards"

[Setup]
AppId={{C8D4F4E5-1234-4567-8901-123456789ABC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf64}\ImageOcclusionEditor
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE.GPL3.txt
OutputDir=Release
OutputBaseFilename=ImageOcclusionEditorWinUI3_Setup
SetupIconFile=..\imageocclusioneditor.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=win64
ArchitecturesInstallIn64BitMode=win64
MinVersion=10.0.17763
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; x64 version files only
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Icon file
Source: "..\imageocclusioneditor.ico"; DestDir: "{app}"; Flags: ignoreversion
; Documentation
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.GPL3.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.MIT.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.TXT"; DestDir: "{app}"; Flags: ignoreversion
; Templates (optional)
Source: "..\Resources\Template_IIOT.txt"; DestDir: "{app}\Templates"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\Resources\Template_IIOTT.txt"; DestDir: "{app}\Templates"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\imageocclusioneditor.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\imageocclusioneditor.ico"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function GetUninstallString(): String;
var
  sUnInstPath: String;
  sUnInstallString: String;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM64, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU64, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function IsUpgrade(): Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

function UnInstallOldVersion(): Integer;
var
  sUnInstallString: String;
  iResultCode: Integer;
begin
  Result := 0;
  sUnInstallString := GetUninstallString();
  if sUnInstallString <> '' then begin
    sUnInstallString := RemoveQuotes(sUnInstallString);
    if Exec(sUnInstallString, '/SILENT /NORESTART /SUPPRESSMSGBOXES','', SW_HIDE, ewWaitUntilTerminated, iResultCode) then
      Result := 3
    else
      Result := 2;
  end else
    Result := 1;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep=ssInstall) then
  begin
    if (IsUpgrade()) then
    begin
      UnInstallOldVersion();
    end;
  end;
end;
