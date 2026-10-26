#ifndef AppVersion
  #define AppVersion "0.1.0-preview.1"
#endif
#ifndef FileVersion
  #define FileVersion "0.1.0"
#endif
#ifndef SourcePath
  #error SourcePath must point to a validated publish directory.
#endif
#ifndef OutputPath
  #define OutputPath "..\artifacts\releases"
#endif

[Setup]
AppId={{570AF1C8-B103-47C3-9604-7CBA6EBF31B4}
AppName=Framelet
AppVersion={#AppVersion}
AppVerName=Framelet {#AppVersion}
AppPublisher=Framelet contributors
AppPublisherURL=https://github.com/Mxlted/framelet-windows
AppSupportURL=https://github.com/Mxlted/framelet-windows/issues
AppUpdatesURL=https://github.com/Mxlted/framelet-windows/releases
DefaultDirName={localappdata}\Programs\Framelet
DefaultGroupName=Framelet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22621
OutputDir={#OutputPath}
OutputBaseFilename=Framelet-{#AppVersion}-win-x64-setup
VersionInfoVersion={#FileVersion}
UninstallDisplayIcon={app}\Framelet.exe
SetupIconFile=..\src\Framelet.App\Assets\framelet.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#SourcePath}\LICENSE
CloseApplications=no
RestartApplications=no
AppMutex=Local\Framelet.{username}
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Framelet"; Filename: "{app}\Framelet.exe"
Name: "{autodesktop}\Framelet"; Filename: "{app}\Framelet.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Framelet.exe"; Description: "Open Framelet"; Flags: nowait postinstall skipifsilent

; Deliberately no UninstallDelete entries: captures and %LOCALAPPDATA%\Framelet settings are user data.
