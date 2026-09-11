; GeoData Pro — Inno Setup installer script
; Requires Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;
; Build steps:
;   1. dotnet publish src/GeoDataPro.App/GeoDataPro.App.csproj ^
;         -c Release -r win-x64 --self-contained false ^
;         -o publish/win-x64
;   2. Open this file in Inno Setup Compiler and click Build → Compile
;      (or: iscc installer\GeoDataPro.iss)
;
; The resulting setup exe appears in installer\Output\.

#define AppName      "GeoData Pro"
#define AppVersion   "1.1.0"
#define AppPublisher "GeoData Pro"
#define AppURL       "https://github.com/BotirRaimqulov/GeoData"
#define AppExeName   "GeoDataPro.exe"
#define PublishDir   "..\publish\win-x64"
#define IconFile     "..\src\GeoDataPro.App\Assets\AppIcon.ico"

; Minimum .NET Desktop Runtime version required
#define DotNetMajor  9
#define DotNetMinor  0

[Setup]
AppId={{A3F2B841-9C4E-4D8F-B2C1-7E6A0F5D3821}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Require admin rights so we can write to Program Files
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=GeoDataPro-{#AppVersion}-Setup
SetupIconFile={#IconFile}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
; Keep uninstaller info in registry
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
; Version info embedded in the setup exe
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
; Minimum Windows 10
MinVersion=10.0.17763

[Languages]
Name: "uzbek";   MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Ish stoliga yorliq yaratish"; GroupDescription: "Qo'shimcha vazifalar:"; Flags: unchecked

[Files]
; Copy the entire publish output — dotnet publish puts all dependencies here
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";    Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Dasturni ochish"; Filename: "{app}\{#AppExeName}"
Name: "{group}\O'chirish";   Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; ---------------------------------------------------------------------------
; .NET Desktop Runtime prerequisite check
; ---------------------------------------------------------------------------
; We check for the winforms/wpf desktop runtime, not just the base runtime.
; Registry key written by the official .NET installer:
;   HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\<ver> = 1
; Alternatively we fall back to checking "dotnet --list-runtimes" output.
; The [Code] section below performs the check and redirects to the download
; page when the runtime is missing.
; ---------------------------------------------------------------------------

[Code]

function IsDotNetDesktopInstalled(): Boolean;
var
  SubKeyNames: TArrayOfString;
  I: Integer;
  KeyPath: String;
  Major, Minor: Integer;
  VerStr: String;
  DotPos: Integer;
begin
  Result := False;
  KeyPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if not RegGetSubkeyNames(HKLM, KeyPath, SubKeyNames) then
  begin
    // 32-bit fallback
    KeyPath := 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
    if not RegGetSubkeyNames(HKLM, KeyPath, SubKeyNames) then
      Exit;
  end;

  for I := 0 to GetArrayLength(SubKeyNames) - 1 do
  begin
    VerStr := SubKeyNames[I];
    DotPos := Pos('.', VerStr);
    if DotPos > 1 then
    begin
      Major := StrToIntDef(Copy(VerStr, 1, DotPos - 1), -1);
      Minor := StrToIntDef(Copy(VerStr, DotPos + 1, 1), -1);
      if (Major > {#DotNetMajor}) or
         ((Major = {#DotNetMajor}) and (Minor >= {#DotNetMinor})) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

procedure InitializeWizard();
begin
  // Nothing extra needed at wizard start
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetDesktopInstalled() then
  begin
    if MsgBox(
      '.NET {#DotNetMajor} Desktop Runtime topilmadi.' + #13#10 +
      'GeoData Pro ishga tushishi uchun Microsoft .NET {#DotNetMajor}.{#DotNetMinor} ' +
      'Desktop Runtime (x64) o''rnatilgan bo''lishi kerak.' + #13#10#13#10 +
      'Yuklab olish sahifasiga o''tishni xohlaysizmi?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://aka.ms/dotnet/{#DotNetMajor}/windowsdesktop-runtime-win-x64',
        '', '', SW_SHOWNORMAL, ewNoWait, Result);
    end;
    Result := False;
  end;
end;
