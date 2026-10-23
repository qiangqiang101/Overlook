; Inno Setup script. Build with: iscc /DAppVersion=2.0.0 /DSourceDir=<publish dir> /DOutputDir=<dir> Overlook.iss
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6B1D3C4A-52A7-4E0B-9C0E-7F3A0C5D2B11}
AppName=Overlook
AppVersion={#AppVersion}
AppPublisher=Overlook
DefaultDirName={autopf}\Overlook
DefaultGroupName=Overlook
UninstallDisplayIcon={app}\Overlook.exe
OutputDir={#OutputDir}
OutputBaseFilename=Overlook-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\BigBro.App\Assets\app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Overlook when I sign in"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Overlook"; Filename: "{app}\Overlook.exe"
Name: "{autodesktop}\Overlook"; Filename: "{app}\Overlook.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Overlook"; ValueData: """{app}\Overlook.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Overlook.exe"; Description: "Launch Overlook"; Flags: nowait postinstall skipifsilent
