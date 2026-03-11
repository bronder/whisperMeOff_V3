; Inno Setup Script for whisperMeOff
; Download Inno Setup from https://jrsoftware.org/isinfo.php

#define MyAppName "whisperMeOff"
#define MyAppVersion "1.2.3"
#define MyAppPublisher "whisperMeOff"
#define MyAppURL "https://github.com/yourrepo"
#define MyAppExeName "whisperMeOff.exe"

[Setup]
AppId={{F8C3E4B2-1D56-4A98-9E3F-7C4D5E6F7A8B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.
OutputBaseFilename=whisperMeOff-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main executable and DLLs
Source: "publish\whisperMeOff.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\whisperMeOff.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\whisperMeOff.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\whisperMeOff.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; Windows x64 runtime files
Source: "publish\runtimes\win-x64\native\*"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

; Optional: Uncomment the following line to include a Whisper model (download from https://huggingface.co/ggerganov/whisper.cpp/tree/main)
; Source: "models\ggml-small.bin"; DestDir: "{userappdata}\whisperMeOff\models\whisper"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
