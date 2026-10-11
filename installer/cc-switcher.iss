; Inno Setup script for the per-user installer. Built by .github/workflows/build.yml from the
; self-contained publish output in ..\publish:
;   ISCC.exe /DAppVersion=1.2.3 installer\cc-switcher.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B0C7E2A-5C1D-4E0B-9B5E-3F2A8D4C9E71}
AppName=CC Switcher
AppVersion={#AppVersion}
AppPublisher=David Cramer
AppPublisherURL=https://github.com/dcramer/cc-switcher
AppSupportURL=https://github.com/dcramer/cc-switcher/issues
VersionInfoVersion={#AppVersion}
; Same place build.ps1 -Install uses, so shortcuts and link routing keep pointing at the app.
DefaultDirName={localappdata}\Programs\cc-switcher
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=cc-switcher-setup
SetupIconFile=..\src\CcSwitcher\app.ico
UninstallDisplayName=CC Switcher
UninstallDisplayIcon={app}\cc-switcher.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\CC Switcher"; Filename: "{app}\cc-switcher.exe"; Comment: "Open Claude Desktop with a chosen profile"

[Run]
Filename: "{app}\cc-switcher.exe"; Description: "Open CC Switcher"; Flags: nowait postinstall skipifsilent

; Profiles and settings in %USERPROFILE%\.cc-switcher are left alone on uninstall, but the
; claude:// link handler the app may have registered would otherwise point at a missing exe.
[Registry]
Root: HKCU; Subkey: "Software\Classes\CcSwitcher.claude"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\CcSwitcher"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueName: "CC Switcher"; Flags: dontcreatekey uninsdeletevalue
