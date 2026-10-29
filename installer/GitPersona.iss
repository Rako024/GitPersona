; Inno Setup 6.3+ script for GitPersona: a per-user install that never asks for administrator rights.
; Build: powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Installer
; (or publish first, then: ISCC.exe /DAppVersion=1.0.0 installer\GitPersona.iss)

#define AppName "GitPersona"
#define AppExe "GitPersona.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B6E5C3A2-7F1D-4C9B-9E2A-3D5F8A1C0B47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
; Everything lives in the user profile: no UAC prompt, no machine-wide changes.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\GitPersona.App\Assets\GitPersona.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Close a running GitPersona (it lives in the tray) before replacing or removing it.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.StartWithWindows=Start {#AppName} when I sign in to Windows
english.BeforeUninstall=Uninstalling keeps the git and ssh configuration GitPersona wrote, so your repositories keep working.%n%nTo remove it as well, cancel now and use GitPersona > Settings > Remove GitPersona's configuration first.%n%nContinue uninstalling?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value GitPersona's own "Start with Windows" setting uses, so the two stay in step.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GitPersona"; \
    ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopGitPersona"

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := UninstallSilent or
    (MsgBox(CustomMessage('BeforeUninstall'), mbConfirmation, MB_YESNO) = IDYES);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // The app may have enabled "Start with Windows" itself, so remove the value whoever created it.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'GitPersona');
end;
