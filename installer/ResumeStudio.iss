#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PackageLabel
  #error PackageLabel is required
#endif

[Setup]
AppId={{B782F0B7-68E7-4D53-9200-5A45126A71F5}
AppName=拾简 Resume Studio
AppVersion={#AppVersion}
AppVerName=拾简 {#ChannelName} {#PackageLabel}
AppPublisher=scj040921
AppPublisherURL=https://github.com/scj040921/resume-studio
AppSupportURL=https://github.com/scj040921/resume-studio/issues
AppUpdatesURL=https://github.com/scj040921/resume-studio/releases
DefaultDirName={localappdata}\Programs\ResumeStudio
DefaultGroupName=拾简 Resume Studio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
WizardStyle=modern
SetupIconFile={#SourceDir}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Assets\AppIcon.ico
UninstallDisplayName=拾简 {#ChannelName} v{#AppVersion}
OutputDir={#OutputDir}
OutputBaseFilename=ResumeStudio-v{#PackageLabel}-Setup-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll,*.jar
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#LanguageFile}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bootstrapper}"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#CompilerLicense}"; DestDir: "{app}\licenses"; DestName: "InnoSetup-LICENSE.txt"; Flags: ignoreversion
Source: "{#SourceDir}\Assets\Fonts\MiSans-Regular.ttf"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{userdesktop}\拾简 - 简历制作与投递"; Filename: "{app}\ResumeStudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Comment: "简历制作、职位收藏与求职日程"
Name: "{userprograms}\拾简 Resume Studio"; Filename: "{app}\ResumeStudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"

[Run]
Filename: "{app}\ResumeStudio.exe"; Description: "启动拾简"; Flags: nowait postinstall skipifsilent

; The uninstaller removes only files registered by Setup. There is intentionally
; no wildcard deletion of the application directory or the separate user data.
[Code]
var
  PrivateFontPath: String;
  FontRegistered: Boolean;

function AddFontResourceEx(FileName: String; Flags, Reserved: Integer): Integer;
  external 'AddFontResourceExW@gdi32.dll stdcall';
function RemoveFontResourceEx(FileName: String; Flags, Reserved: Integer): Boolean;
  external 'RemoveFontResourceExW@gdi32.dll stdcall';

procedure InitializeWizard;
begin
  ExtractTemporaryFile('MiSans-Regular.ttf');
  PrivateFontPath := ExpandConstant('{tmp}\MiSans-Regular.ttf');
  FontRegistered := AddFontResourceEx(PrivateFontPath, $10, 0) > 0;
  if FontRegistered then WizardForm.Font.Name := 'MiSans';
  WizardForm.WelcomeLabel1.Caption := '安装拾简 {#ChannelName} {#AppVersion}';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then begin
    WizardForm.NextButton.Caption := '一键安装';
    { The application directory is initialized after InitializeWizard returns. }
  WizardForm.WelcomeLabel2.Caption :=
    '点击“一键安装”，即可完成安装并自动创建桌面快捷方式。' + #13#10#13#10 +
    '安装位置：' + ExpandConstant('{app}') + #13#10#13#10 +
    '简历、职位收藏、投递记录和日程保存在独立的个人数据目录。覆盖安装和卸载会保留这些数据。' + #13#10#13#10 +
    '如果系统缺少 WebView2，安装程序会从微软下载所需运行时。';
  end
  else if CurPageID = wpFinished then WizardForm.NextButton.Caption := '完成';
end;

function HasRuntimeVersion(Root: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root,
    'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
    and (Version <> '') and (Version <> '0.0.0.0');
end;

function WebViewPresent: Boolean;
begin
  Result := HasRuntimeVersion(HKCU32) or HasRuntimeVersion(HKCU64)
    or HasRuntimeVersion(HKLM32) or HasRuntimeVersion(HKLM64);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not WebViewPresent then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
      '/silent /install', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := '无法启动 WebView2 安装，请检查网络后重试。'
    else if (ResultCode <> 0) or not WebViewPresent then
      Result := 'WebView2 未能安装完成，请连接网络后重新运行安装包。';
  end;
end;

procedure DeinitializeSetup;
begin
  if FontRegistered then RemoveFontResourceEx(PrivateFontPath, $10, 0);
end;
