#define MyAppName "Image Occlusion Editor"
#define ScriptDir SourcePath
#ifndef ProjectDir
#define ProjectDir GetEnv("IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR")
#endif
#if ProjectDir == ""
#undef ProjectDir
#define ProjectDir AddBackslash(ScriptDir) + ".."
#endif
#define ProjectDirWithBackslash AddBackslash(ProjectDir)
#ifndef MyAppVersion
#define MyAppVersion GetEnv("IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION")
#endif
#if MyAppVersion == ""
#error MyAppVersion must be supplied by Build-InnoInstaller.ps1
#endif
#ifndef PublishDir
#define PublishDir GetEnv("IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR")
#endif
#if PublishDir == ""
#error PublishDir must be supplied by Build-InnoInstaller.ps1
#endif
#define MyAppPublisher "Shuai Zhang"
#define MyAppURL "https://github.com/hcoona/ImageOcclusionEditor"
#define MyAppExeName "ImageOcclusionEditor.exe"
#define MyAppDescription "Application for creating image occlusion cards"
#define PublishDirWithBackslash AddBackslash(PublishDir)
#if DirExists(ProjectDir) == 0
#expr Error("ProjectDir does not exist: " + ProjectDir)
#endif
#if DirExists(PublishDir) == 0
#expr Error("PublishDir does not exist: " + PublishDir)
#endif
#if FileExists(PublishDirWithBackslash + MyAppExeName) == 0
#expr Error("Published executable does not exist: " + PublishDirWithBackslash + MyAppExeName)
#endif
#if FileExists(ProjectDirWithBackslash + "imageocclusioneditor.ico") == 0
#expr Error("Inno icon input does not exist: " + ProjectDirWithBackslash + "imageocclusioneditor.ico")
#endif
#if FileExists(ProjectDirWithBackslash + "LICENSE.GPL3.txt") == 0
#expr Error("Inno license input does not exist: " + ProjectDirWithBackslash + "LICENSE.GPL3.txt")
#endif

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
LicenseFile={#ProjectDirWithBackslash}LICENSE.GPL3.txt
OutputBaseFilename=ImageOcclusionEditorWinUI3_Setup
SetupIconFile={#ProjectDirWithBackslash}imageocclusioneditor.ico
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
Source: "{#PublishDirWithBackslash}*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Icon file
Source: "{#ProjectDirWithBackslash}imageocclusioneditor.ico"; DestDir: "{app}"; Flags: ignoreversion
; Documentation
Source: "{#ProjectDirWithBackslash}README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDirWithBackslash}LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDirWithBackslash}LICENSE.GPL3.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDirWithBackslash}LICENSE.MIT.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProjectDirWithBackslash}THIRD-PARTY-NOTICES.TXT"; DestDir: "{app}"; Flags: ignoreversion
; Templates (optional)
Source: "{#ProjectDirWithBackslash}Resources\Template_IIOT.txt"; DestDir: "{app}\Templates"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ProjectDirWithBackslash}Resources\Template_IIOTT.txt"; DestDir: "{app}\Templates"; Flags: ignoreversion skipifsourcedoesntexist

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
