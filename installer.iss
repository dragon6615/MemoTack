; MemoTack 安裝程式腳本（Inno Setup）
; 先執行 dotnet publish（或直接跑 build-installer.bat），再用 Inno Setup 編譯本檔

#define MyAppName "MemoTack"
; 版本號由 build-installer.ps1 以 /DAppVersion=x.y.z 傳入；直接用 IDE 編譯時採用下面預設值
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define MyAppVersion AppVersion
#define MyAppPublisher "Dragon"
#define MyAppExeName "MemoTack.exe"
#define PublishDir "publish\win-x64"

[Setup]
; AppId 是解除安裝識別碼，改版時請保持不變
AppId={{C1E0B7D4-4A2E-4F5B-9B3D-7E86A1F2D9C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; 預設個人安裝（免系統管理員，裝到 %LocalAppData%\Programs）；也允許使用者選擇裝給所有人
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer
OutputBaseFilename=MemoTack-Setup-{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 安裝/解除安裝時偵測程式是否執行中（對應 Program.cs 的單一實例 Mutex）
AppMutex=Local\MemoTack_SingleInstance

[Languages]
; 繁體中文（列第一個 = 預設語言）。
; 自動偵測：優先用 Inno Setup 內建的翻譯檔，找不到才用專案資料夾內的備援版
#if FileExists(CompilerPath + "\Languages\ChineseTraditional.isl")
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
#elif FileExists(CompilerPath + "\Languages\Unofficial\ChineseTraditional.isl")
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\Unofficial\ChineseTraditional.isl"
#else
Name: "chinesetraditional"; MessagesFile: "ChineseTraditional.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 不在安裝時建立（自動啟動由程式的設定視窗控制），但解除安裝時清掉殘留的登錄值
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#MyAppName}"; \
    Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

; 註：使用者資料（便箋內容 %APPDATA%\MemoTack\notes.json）解除安裝時刻意保留，
; 重新安裝後便箋會自動回來；若要一併刪除，取消下面註解
;[UninstallDelete]
;Type: filesandordirs; Name: "{userappdata}\MemoTack"
