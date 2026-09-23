; Voice Forever installer (Inno Setup 6.7+). Build with: .\build.ps1 -Installer
; Installs per user (no admin rights needed). The only admin prompt is for Microsoft's Visual C++
; runtime, and only on PCs that don't already have version 14.44 or later.

#define AppName "Voice Forever"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B2EAC87C-FEB6-4C25-9A3E-3C76863AFA0B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
OutputDir=..\dist
OutputBaseFilename=VoiceForever-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\app\Gui\Assets\VoiceForever.ico
UninstallDisplayIcon={app}\VoiceForever.exe
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
WelcomeLabel2=This will install [name/ver] on your computer.%n%nChat in World of Warcraft: Forever by voice. Open chat, press a button on your controller (or a keyboard shortcut), speak, and your words appear in the chat box.%n%nSpeech is recognised on your own PC; nothing is sent anywhere. After installing, the app offers to download a speech model (Turbo, 574 MB, is recommended).

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Voice Forever when I &sign in to Windows"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeeded

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\VoiceForever.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\VoiceForever.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\VoiceForever.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing the Microsoft Visual C++ runtime..."; Verb: runas; Flags: shellexec waituntilterminated; Check: VCRedistNeeded
Filename: "{app}\VoiceForever.exe"; Description: "Launch Voice Forever"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM VoiceForever.exe /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM VoiceForeverCli.exe /F"; Flags: runhidden; RunOnceId: "StopCli"

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

{ Downloaded models can be several GB, so offer to remove them along with the settings. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{localappdata}\VoiceForever');
  if DirExists(Data) and not UninstallSilent then
    if MsgBox('Also delete your downloaded speech models and settings?' + #13#10#13#10 + Data,
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Data, True, True, True);
end;
