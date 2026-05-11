#define MyAppName "hcoona-release-smoke-inno"
#ifndef PublishDir
  #error PublishDir must be supplied by Build-InnoInstaller.ps1
#endif
#define MyAppExeName "hcoona-release-smoke-inno.exe"
#define MyAppExePath PublishDir + "\" + MyAppExeName
#define MyAppVersion GetVersionNumbersString(MyAppExePath)

[Setup]
AppId={{B47D47BC-0804-4ED1-B657-34B6B578583E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=hcoona
DefaultDirName={autopf}\hcoona-release-smoke-inno
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=hcoona-release-smoke-inno-setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
