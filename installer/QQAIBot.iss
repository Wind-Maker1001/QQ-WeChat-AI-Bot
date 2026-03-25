#ifndef SourceDir
  #error SourceDir preprocessor variable is required.
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "qq-ai-bot-setup"
#endif

#define AppName "QQ AI Bot"
#define AppPublisher "Wind-Maker1001"
#define AppId "{{A8CC8E14-9E8D-4A51-9C8B-9BE8E7A5D3A2}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\QQAIBot
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\app\desktop-publish\QQAIBot.Desktop.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\.env.example"; DestDir: "{app}\app"; DestName: ".env"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autoprograms}\QQ AI Bot"; Filename: "{app}\app\desktop-publish\QQAIBot.Desktop.exe"; WorkingDir: "{app}\app"
Name: "{autodesktop}\QQ AI Bot"; Filename: "{app}\app\desktop-publish\QQAIBot.Desktop.exe"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Run]
Filename: "{app}\app\desktop-publish\QQAIBot.Desktop.exe"; Description: "Launch QQ AI Bot"; WorkingDir: "{app}\app"; Flags: nowait postinstall skipifsilent
