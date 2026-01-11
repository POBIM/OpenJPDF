; OpenJPDF Windows Installer Script
; Requires Inno Setup 6.x - https://jrsoftware.org/isinfo.php

#define MyAppName "OpenJPDF"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Sittichat Pothising"
#define MyAppURL "https://github.com/POBIM/OpenJPDF"
#define MyAppExeName "OpenJPDF.exe"

[Setup]
; App identity (IMPORTANT: Generate new GUID for your project)
AppId={{B2C3D4E5-F6A7-8901-BCDE-F12345678901}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Install location
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output settings
OutputDir=.\output
OutputBaseFilename=OpenJPDF-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\pomeranian_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; License file (for License Agreement page)
LicenseFile=..\LICENSE

; Compression
Compression=lzma2
SolidCompression=yes

; Privileges (no admin required)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Appearance
WizardStyle=modern
WizardSizePercent=100
; Note: Using default wizard images (WizardImageFile/WizardSmallImageFile removed for compatibility)

; Version info for upgrades
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} - Comprehensive PDF Editor - Licensed under AGPLv3
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; Upgrade settings
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes

; Misc
AllowNoIcons=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Thai language file not included in default Inno Setup - using English only
; Name: "thai"; MessagesFile: "compiler:Languages\Thai.isl"

[CustomMessages]
; English messages
english.InstalledVersionInfo=Currently installed version: %1
english.NewerVersionInstalled=A newer version (%1) is already installed.%nCurrent installer version: %2%n%nDo you want to downgrade?
english.SameVersionInstalled=Version %1 is already installed.%n%nDo you want to reinstall?
english.OlderVersionFound=Version %1 is currently installed.%nThis installer will upgrade to version %2.%n%nDo you want to continue?
english.FreshInstall=OpenJPDF will be installed on your computer.%n%nVersion: %1%n%nOpenJPDF is licensed under GNU Affero General Public License v3 (AGPLv3).
english.UpgradeComplete=OpenJPDF has been upgraded to version %1.
english.InstallComplete=OpenJPDF version %1 has been installed.
english.LicenseAgreement=License Agreement
english.LicenseAgreementText=OpenJPDF is distributed under the GNU Affero General Public License v3 (AGPLv3).%n%nBy clicking "I Agree", you accept the terms of this license.%n%nPlease read the license file carefully before proceeding.
english.ClickAgreeToAccept=You must accept the license agreement to continue installation.
english.LicenseFileNotFound=License file not found!%n%nPlease ensure LICENSE file exists in the installation directory.

; Thai messages (commented out - Thai language file not available in Inno Setup 6)
; To enable Thai, you need a custom Thai.isl file
; thai.InstalledVersionInfo=เวอร์ชันที่ติดตั้งอยู่: %1
; thai.NewerVersionInstalled=มีเวอร์ชันใหม่กว่า (%1) ติดตั้งอยู่แล้ว%nเวอร์ชันตัวติดตั้งนี้: %2%n%nต้องการดาวน์เกรดหรือไม่?
; thai.SameVersionInstalled=เวอร์ชัน %1 ติดตั้งอยู่แล้ว%n%nต้องการติดตั้งใหม่หรือไม่?
; thai.OlderVersionFound=เวอร์ชัน %1 ติดตั้งอยู่ในเครื่อง%nตัวติดตั้งนี้จะอัพเกรดเป็นเวอร์ชัน %2%n%nต้องการดำเนินการต่อหรือไม่?
; thai.FreshInstall=OpenJPDF จะถูกติดตั้งบนคอมพิวเตอร์ของคุณ%n%nเวอร์ชัน: %1%n%nOpenJPDF ใช้ใบอนุญาต GNU Affero General Public License v3 (AGPLv3)
; thai.UpgradeComplete=OpenJPDF ถูกอัพเกรดเป็นเวอร์ชัน %1 แล้ว
; thai.InstallComplete=OpenJPDF เวอร์ชัน %1 ถูกติดตั้งแล้ว
; thai.LicenseAgreement=ข้อตกลงใบอนุญาต
; thai.LicenseAgreementText=OpenJPDF ใช้ใบอนุญาต GNU Affero General Public License v3 (AGPLv3)%n%nการคลิก "ฉันยอมรับ" ถือว่าคุณยอมรับเงื่อนไขของใบอนุญาต%n%nโปรดอ่านไฟล์ LICENSE อย่างละเอียดก่อนทำการติดตั้ง
; thai.ClickAgreeToAccept=คุณต้องยอมรับข้อตกลงใบอนุญาตเพื่อดำเนินการติดตั้ง
; thai.LicenseFileNotFound=ไม่พบไฟล์ LICENSE!%n%nโปรดตรวจสอบว่าไฟล์ LICENSE อยู่ในไดเรกทอรีติดตั้ง

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application files (published folder)
Source: "..\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; OCR trained data files (Tesseract)
; NOTE: Download eng.traineddata and tha.traineddata from https://github.com/tesseract-ocr/tessdata
; Place them in the tessdata folder before building installer
Source: "..\tessdata\*.traineddata"; DestDir: "{app}\tessdata"; Flags: ignoreversion skipifsourcedoesntexist

; ONNX Machine Learning Models
; Background removal model (snap.onnx) is included in onnx-models folder
Source: "..\onnx-models\*.onnx"; DestDir: "{app}\models"; Flags: ignoreversion skipifsourcedoesntexist

; Icon for uninstaller
Source: "..\Assets\pomeranian_icon.ico"; DestDir: "{app}"; Flags: ignoreversion

; License and Documentation Files
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE-FONTS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CONTRIBUTING.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

; Third-party license files
Source: "..\Docs\LICENSES.txt"; DestDir: "{app}\Docs"; Flags: ignoreversion
Source: "..\Docs\LICENSES\*"; DestDir: "{app}\Docs\LICENSES"; Flags: ignoreversion

[Icons]
; Start Menu
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; Desktop (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; Store version info for future upgrades
Root: HKA; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "InstallDate"; ValueData: "{code:GetInstallDate}"; Flags: uninsdeletekey

; Register as PDF handler (adds to "Open with" list)
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "OpenJPDF.PDF"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\OpenJPDF.PDF"; ValueType: string; ValueName: ""; ValueData: "PDF Document (OpenJPDF)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\OpenJPDF.PDF\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\OpenJPDF.PDF\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Add to "Open with" program list
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Register in Windows "Default Apps" settings
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\{#MyAppName}\Capabilities"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "PDF Viewer and Editor"
Root: HKA; Subkey: "Software\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "OpenJPDF.PDF"

[UninstallRun]
; Kill app before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
var
  InstalledVersion: String;
  IsUpgrade: Boolean;
  LicenseAccepted: Boolean;
  
// Get current date for install date tracking
function GetInstallDate(Param: String): String;
begin
  Result := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':');
end;

// Compare version strings (returns: -1 if V1<V2, 0 if equal, 1 if V1>V2)
function CompareVersions(V1, V2: String): Integer;
var
  P1, P2: Integer;
  N1, N2: Integer;
begin
  Result := 0;
  while (Length(V1) > 0) or (Length(V2) > 0) do
  begin
    // Extract first number from V1
    P1 := Pos('.', V1);
    if P1 > 0 then
    begin
      N1 := StrToIntDef(Copy(V1, 1, P1-1), 0);
      V1 := Copy(V1, P1+1, Length(V1));
    end
    else
    begin
      N1 := StrToIntDef(V1, 0);
      V1 := '';
    end;
    
    // Extract first number from V2
    P2 := Pos('.', V2);
    if P2 > 0 then
    begin
      N2 := StrToIntDef(Copy(V2, 1, P2-1), 0);
      V2 := Copy(V2, P2+1, Length(V2));
    end
    else
    begin
      N2 := StrToIntDef(V2, 0);
      V2 := '';
    end;
    
    // Compare
    if N1 < N2 then
    begin
      Result := -1;
      Exit;
    end
    else if N1 > N2 then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

// Get installed version from registry
function GetInstalledVersion(): String;
var
  Version: String;
begin
  Result := '';
  if RegQueryStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppName}', 'Version', Version) then
    Result := Version
  else if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'Software\{#MyAppName}', 'Version', Version) then
    Result := Version;
end;

// Check if app is running
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec('tasklist', '/FI "IMAGENAME eq {#MyAppExeName}" /NH', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // tasklist always returns 0, we need to check differently
    // Using FindWindowByClassName or similar would be more reliable
  end;
end;

// Kill running instance
procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500); // Wait for process to fully terminate
end;

// Initialize setup - check for existing installation
function InitializeSetup(): Boolean;
var
  CompareResult: Integer;
  Msg: String;
begin
  Result := True;
  IsUpgrade := False;
  LicenseAccepted := False;
  
  // Kill any running instance first
  KillRunningApp();
  
  // Check for existing installation
  InstalledVersion := GetInstalledVersion();
  
  if InstalledVersion <> '' then
  begin
    IsUpgrade := True;
    CompareResult := CompareVersions(InstalledVersion, '{#MyAppVersion}');
    
    if CompareResult > 0 then
    begin
      // Installed version is NEWER than this installer
      Msg := FmtMessage(CustomMessage('NewerVersionInstalled'), [InstalledVersion, '{#MyAppVersion}']);
      if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end
    else if CompareResult = 0 then
    begin
      // Same version installed
      Msg := FmtMessage(CustomMessage('SameVersionInstalled'), [InstalledVersion]);
      if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end
    else
    begin
      // Installed version is OLDER - normal upgrade
      Msg := FmtMessage(CustomMessage('OlderVersionFound'), [InstalledVersion, '{#MyAppVersion}']);
      if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

// Customize welcome page text
procedure InitializeWizard();
var
  Msg: String;
begin
  if InstalledVersion <> '' then
  begin
    Msg := FmtMessage(CustomMessage('InstalledVersionInfo'), [InstalledVersion]);
    WizardForm.WelcomeLabel2.Caption := WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + Msg;
  end
  else
  begin
    Msg := FmtMessage(CustomMessage('FreshInstall'), ['{#MyAppVersion}']);
    WizardForm.WelcomeLabel2.Caption := Msg;
  end;
end;

// Check if license is accepted before proceeding
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  
  // Show license page for new installs or license updates
  if PageID = wpLicense then
  begin
    // Skip license page only if upgrade and already accepted
    if IsUpgrade and LicenseAccepted then
      Result := True;
  end;
end;

// Validate license acceptance
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  
  if CurPageID = wpLicense then
  begin
    if not WizardForm.LicenseAcceptedRadio.Checked then
    begin
      MsgBox(CustomMessage('ClickAgreeToAccept'), mbError, MB_OK);
      Result := False;
    end
    else
    begin
      LicenseAccepted := True;
    end;
  end;
end;

// Show completion message
procedure CurStepChanged(CurStep: TSetupStep);
var
  Msg: String;
begin
  if CurStep = ssPostInstall then
  begin
    if IsUpgrade then
      Msg := FmtMessage(CustomMessage('UpgradeComplete'), ['{#MyAppVersion}'])
    else
      Msg := FmtMessage(CustomMessage('InstallComplete'), ['{#MyAppVersion}']);
    // Log the message (shown in finish page)
    Log(Msg);
  end;
end;
