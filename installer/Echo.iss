#define EchoVersion GetEnv("ECHO_INSTALLER_VERSION")
#define PackageDirectory GetEnv("ECHO_INSTALLER_PACKAGE")
#if EchoVersion == "" || PackageDirectory == ""
  #error Use scripts/build-installer.ps1 to compile the Echo installer.
#endif

[Setup]
AppId={{8F87EF57-DBE7-4A6E-9A68-385B5A770C6D}
AppName=Echo
AppVersion={#emit EchoVersion}
AppPublisher=Echo contributors
AppPublisherURL=https://github.com/Yimikami/echo-lol
AppSupportURL=https://github.com/Yimikami/echo-lol/issues
AppUpdatesURL=https://github.com/Yimikami/echo-lol/releases/latest
DefaultDirName={localappdata}\Programs\Echo
DefaultGroupName=Echo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
SetupIconFile=..\assets\brand\echo.ico
UninstallDisplayIcon={app}\Echo.exe
OutputBaseFilename=Echo-Setup-{#emit EchoVersion}-x64
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#emit PackageDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Echo"; Filename: "{app}\Echo.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Echo"; Filename: "{app}\Echo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Echo.exe"; Description: "Open Echo"; Flags: nowait postinstall skipifsilent
