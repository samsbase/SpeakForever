; Speak Forever installer (Inno Setup 6.7+). Build with: .\build.ps1 -Installer
; Installs per user (no admin rights needed). The only admin prompt is for Microsoft's Visual C++
; runtime, and only on PCs that don't already have version 14.44 or later.

#define AppName "Speak Forever"
; The same as App.AppUserModelId in the app.

#define AppUserModelId "SpeakForever.App"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Kept from when the app was called Voice Forever, so this installs over that as an upgrade.
AppId={{B2EAC87C-FEB6-4C25-9A3E-3C76863AFA0B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
; Not the Voice Forever folder an earlier version used; [InstallDelete] clears that out.
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
OutputDir=..\dist
OutputBaseFilename=SpeakForever-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\app\Gui\Assets\SpeakForever.ico
UninstallDisplayIcon={app}\SpeakForever.exe
UninstallDisplayName={#AppName}

; Branding: the app's arcane night-sky look, in Inno's dark wizard. The welcome page (off by
; default in the modern style) is where the large side panel shows.
DisableWelcomePage=no
WizardStyle=modern dark
WizardBackColor=#0B0E1A
WizardImageFile=art\wizard-large-100.png,art\wizard-large-125.png,art\wizard-large-150.png,art\wizard-large-175.png,art\wizard-large-200.png,art\wizard-large-250.png
WizardSmallImageFile=art\wizard-small-100.png,art\wizard-small-125.png,art\wizard-small-150.png,art\wizard-small-175.png,art\wizard-small-200.png,art\wizard-small-250.png

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nChat in World of Warcraft: Forever with your voice. Open chat, press a button on your controller or a keyboard shortcut, and speak: your words appear in the chat box.%n%nSpeech is recognised on your own PC, and nothing you say is sent anywhere. When it first opens, Speak Forever helps you download a voice model: Turbo (574 MB) is recommended.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Speak Forever when I &sign in to Windows"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeeded

; Upgrading from Voice Forever: its program folder and shortcuts.
[InstallDelete]
Type: filesandordirs; Name: "{autopf}\Voice Forever"
Type: files; Name: "{autoprograms}\Voice Forever.lnk"
Type: files; Name: "{autodesktop}\Voice Forever.lnk"

; The Start menu entry is what Windows search finds and what "Pin to Start" and "Pin to taskbar"
; pin. Its AppUserModelID matches the one the app sets on itself, so the running window groups
; under a pinned icon instead of appearing beside it.
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\SpeakForever.exe"; AppUserModelID: "{#AppUserModelId}"; Comment: "Chat in World of Warcraft: Forever with your voice"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\SpeakForever.exe"; AppUserModelID: "{#AppUserModelId}"; Comment: "Chat in World of Warcraft: Forever with your voice"; Tasks: desktopicon

[Registry]
; Upgrading from Voice Forever: its App Paths entry and sign-in startup value.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\VoiceForever.exe"; Flags: deletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Voice Forever"; Flags: deletevalue
; App Paths: Win+R and Start search both find "SpeakForever".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\SpeakForever.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\SpeakForever.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\SpeakForever.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\SpeakForever.exe"""; Flags: uninsdeletevalue; Tasks: startup
; The app can turn starting at sign-in on itself (setup and Settings), so uninstalling removes it either way.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing the Microsoft Visual C++ runtime..."; Verb: runas; Flags: shellexec waituntilterminated; Check: VCRedistNeeded
Filename: "{app}\SpeakForever.exe"; Description: "Launch Speak Forever"; Flags: nowait postinstall skipifsilent
; The app updating itself runs this silently with /RELAUNCH=1, and expects to be opened again.
Filename: "{app}\SpeakForever.exe"; Flags: nowait; Check: RelaunchRequested

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM SpeakForever.exe /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM SpeakForeverCli.exe /F"; Flags: runhidden; RunOnceId: "StopCli"

[Code]
const
  VCRuntimeKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';

{ The speech engine was built with Visual C++ 14.44, so it needs that runtime or newer. }
function VCRedistNeeded: Boolean;
var
  Installed, Major, Minor: Cardinal;
begin
  Result := not (RegQueryDWordValue(HKLM64, VCRuntimeKey, 'Installed', Installed) and (Installed = 1)
    and RegQueryDWordValue(HKLM64, VCRuntimeKey, 'Major', Major)
    and RegQueryDWordValue(HKLM64, VCRuntimeKey, 'Minor', Minor)
    and ((Major > 14) or ((Major = 14) and (Minor >= 44))));
end;

function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

{ Downloaded models can be several GB, so offer to remove them along with the settings. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{localappdata}\SpeakForever');
  if DirExists(Data) and not UninstallSilent then
    if MsgBox('Also delete your downloaded voice models and settings?' + #13#10#13#10 + Data,
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Data, True, True, True);
end;
