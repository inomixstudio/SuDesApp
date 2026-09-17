; ============================================================================
;  Skrip Inno Setup untuk SuDesApp.
;  Dipakai oleh .github/workflows/release.yml. Versi di-inject via:
;     ISCC.exe /DAppVersion=2.2.0 installer\SuDesApp.iss
;
;  Instalasi ke {localappdata}\Programs\SuDesApp (tanpa UAC) supaya
;  database/aplikasi yang ditulis relatif ke folder aplikasi tetap bisa
;  diakses pengguna biasa.
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId=SuDesAppSumberjaya
AppName=SuDesApp
AppVersion={#AppVersion}
AppVerName=SuDesApp {#AppVersion}
AppPublisher=Sumberjaya Dev.
AppPublisherURL=https://github.com/
DefaultDirName={localappdata}\Programs\SuDesApp
DefaultGroupName=SuDesApp
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=SuDesApp_{#AppVersion}_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Resources\AppIcon.ico
UninstallDisplayIcon={app}\SuDesApp.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat ikon Desktop"; GroupDescription: "Ikon:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SuDesApp"; Filename: "{app}\SuDesApp.exe"
Name: "{autodesktop}\SuDesApp"; Filename: "{app}\SuDesApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SuDesApp.exe"; Description: "Jalankan SuDesApp"; Flags: nowait postinstall skipifsilent
