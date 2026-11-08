; ============================================================================
;  Skrip Inno Setup untuk SuDesApp.
;  Dipakai oleh .github/workflows/release.yml. Versi di-inject via:
;     ISCC.exe /DAppVersion=2.9.0 /DAppVersionNumeric=2.9.0.0 installer\SuDesApp.iss
;
;  Instalasi ke {localappdata}\Programs\SuDesApp (tanpa UAC) supaya
;  database/aplikasi yang ditulis relatif ke folder aplikasi tetap bisa
;  diakses pengguna biasa.
;
;  Prinsip paket:
;   - aplikasi self-contained (tidak perlu memasang .NET terpisah);
;   - data pengguna (desa.db, Templates, hasil PDF) TIDAK PERNAH ditimpa atau
;     dihapus oleh upgrade; berkas program selalu diganti;
;   - AppMutex menunjuk mutex yang benar-benar dibuat aplikasi, sehingga setup
;     tahu aplikasi sedang berjalan sebelum menimpa DLL/EXE-nya.
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppVersionNumeric
  ; VersionInfoVersion hanya menerima angka x.y.z[.w]; prarilis (2.9.0-rc1)
  ; wajib memberi AppVersionNumeric dari alur rilis.
  #define AppVersionNumeric AppVersion
#endif

; AppId/GroupName bisa diganti saat kompilasi UJI (/DAppId=...): installer uji
; tidak boleh berbagi identitas dengan instalasi nyata pengguna, karena AppId
; yang sama berarti entri Uninstall, ikon, dan folder Start Menu saling timpa.
#ifndef AppId
  #define AppId "SuDesAppSumberjaya"
#endif

#ifndef GroupName
  #define GroupName "SuDesApp"
#endif

[Setup]
AppId={#AppId}
AppName=SuDesApp
AppVersion={#AppVersion}
AppVerName=SuDesApp {#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
VersionInfoCompany=Sumberjaya Dev.
VersionInfoDescription=SuDesApp — Sistem Surat Desa
VersionInfoProductName=SuDesApp
AppPublisher=Sumberjaya Dev.
AppPublisherURL=https://github.com/inomixstudio/SuDesApp
AppSupportURL=https://github.com/inomixstudio/SuDesApp/issues
AppUpdatesURL=https://github.com/inomixstudio/SuDesApp/releases
DefaultDirName={localappdata}\Programs\SuDesApp
DefaultGroupName={#GroupName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
SetupLogging=yes
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
; Mutex ini dibuat App.OnStartup (App.NamaMutexInstansi). Inno memeriksanya
; sebelum memasang dan meminta pengguna menutup aplikasi lebih dulu; tanpa
; mutex nyata, upgrade saat aplikasi berjalan bisa gagal di tengah karena
; desa.db terkunci, dengan pesan Inno yang tidak menjelaskan apa pun.
AppMutex=SuDesApp_SingletonMutex
Uninstallable=yes

[Languages]
Name: "indonesian"; MessagesFile: "compiler:Languages\Indonesian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat ikon Desktop"; GroupDescription: "Ikon:"; Flags: unchecked

[Files]
; Seluruh berkas program. Templates DIKECUALIKAN di sini dan ditangani entri
; berikutnya: setelah pemasangan pertama folder itu milik pengguna (petugas
; mengedit template surat di sana), jadi upgrade tidak boleh menimpanya dengan
; contoh bawaan.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.pdb,Templates\*,Templates\*\*,desa.db*,desa_backup*.db"

; Template formulir awal. Dulu berkas ini disalin lewat [Code] dari
; {src}\..\artifacts\publish\Templates — jalur folder BUILD yang tidak pernah
; ada di komputer pengguna, sehingga instalasi baru selalu berakhir dengan
; folder Templates kosong. Sekarang sumbernya ikut dikemas installer dan
; onlyifdoesntexist membuat template yang sudah diedit desa tidak pernah
; ditimpa saat upgrade; skipifsourcedoesntexist menjaga build yang tidak
; membawa contoh template tetap bisa dikompilasi.
;
; uninsneveruninstall WAJIB: setelah dipasang, folder Templates adalah milik
; desa (petugas mengedit/menambah template surat di sana). Tanpa flag ini,
; uninstaller menghapus setiap berkas yang tercatat di log pemasangan —
; termasuk template yang sudah diedit — sehingga desa kehilangan pekerjaannya
; hanya karena mencopot aplikasi. Diuji pada instalasi nyata: berkas template
; yang ditambahkan desa selamat, tetapi template bawaan yang diedit terhapus.
Source: "..\artifacts\publish\Templates\*"; DestDir: "{app}\Templates"; \
  Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist \
         skipifsourcedoesntexist uninsneveruninstall

[Dirs]
; Folder data pengguna selalu ada, termasuk pada instalasi yang tidak membawa
; contoh template sama sekali.
Name: "{app}\Templates"

[InstallDelete]
; Native library untuk RID yang sudah tidak dipakai (runtimes\linux-musl-*,
; maccatalyst-*, dan seterusnya — ada belasan RID dalam satu paket) harus
; dibuang saat versi naik; tanpa ini berkas lama tetap tertinggal dan bisa
; menang dalam resolusi assembly.
Type: filesandordirs; Name: "{app}\runtimes"
Type: filesandordirs; Name: "{app}\x64"
Type: files;       Name: "{app}\*.pdb"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\runtimes"
Type: filesandordirs; Name: "{app}\x64"
Type: filesandordirs; Name: "{app}\TempPDF"
; desa.db, Templates, dan hasil PDF sengaja TIDAK dihapus: itu data desa, bukan
; berkas aplikasi. Uninstaller hanya membuang folder aplikasi bila sudah kosong.
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{group}\SuDesApp"; Filename: "{app}\SuDesApp.exe"
Name: "{autodesktop}\SuDesApp"; Filename: "{app}\SuDesApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SuDesApp.exe"; Description: "Jalankan SuDesApp"; Flags: nowait postinstall skipifsilent

[Code]
var
  FolderCadanganDatabase: String;
  AdaDatabaseLama: Boolean;

function CadangkanDatabase(Nama: String): Boolean;
var
  Sumber, Tujuan: String;
begin
  Result := True;
  Sumber := ExpandConstant('{app}\') + Nama;
  if not FileExists(Sumber) then Exit;

  Tujuan := FolderCadanganDatabase + '\' + Nama;
  if not CopyFile(Sumber, Tujuan, False) then
  begin
    Result := False;
    MsgBox('Database desa tidak bisa dicadangkan sebelum upgrade:' + #13#10 +
           Sumber + #13#10#13#10 +
           'Instalasi dibatalkan agar data desa tidak hilang. Tutup aplikasi dan ' +
           'pastikan folder aplikasi bisa ditulis, lalu jalankan installer lagi.',
           mbError, MB_OK);
  end;
end;

procedure PulihkanDatabase(Nama: String);
var
  Sumber, Tujuan: String;
begin
  Sumber := FolderCadanganDatabase + '\' + Nama;
  Tujuan := ExpandConstant('{app}\') + Nama;

  // Jangan menimpa berkas yang masih ada: uninstaller versi lama memang tidak
  // menghapus database, jadi berkas asli adalah sumber yang paling baru.
  if FileExists(Sumber) and not FileExists(Tujuan) then
  begin
    if not CopyFile(Sumber, Tujuan, False) then
      MsgBox('Cadangan database tidak bisa dipulihkan:' + #13#10 +
             Tujuan + #13#10#13#10 +
             'Cadangan masih ada di:' + #13#10 + Sumber + #13#10#13#10 +
             'Salin berkas itu ke folder aplikasi sebelum membuka SuDesApp.',
             mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Uninstaller: String;
  Hasil: Integer;
  Percobaan: Integer;
begin
  if CurStep <> ssInstall then Exit;

  FolderCadanganDatabase := ExpandConstant('{tmp}\SuDesApp-database-lama');
  ForceDirectories(FolderCadanganDatabase);

  AdaDatabaseLama := FileExists(ExpandConstant('{app}\desa.db'));
  if AdaDatabaseLama then
  begin
    if not CadangkanDatabase('desa.db') then Abort;
    if not CadangkanDatabase('desa.db-wal') then Abort;
    if not CadangkanDatabase('desa.db-shm') then Abort;
    if not CadangkanDatabase('desa.db-journal') then Abort;
  end;

  // Jalankan uninstaller versi lama secara senyap. Inno Setup tidak otomatis
  // mencopot versi yang sudah ada dengan AppId yang sama; tanpa langkah ini
  // berkas program lama yang sudah tidak dipakai tetap tertinggal. Uninstaller
  // tidak menghapus desa.db, Templates, atau hasil PDF pengguna.
  Uninstaller := ExpandConstant('{app}\unins000.exe');
  if FileExists(Uninstaller) then
  begin
    if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
                SW_HIDE, ewWaitUntilTerminated, Hasil) then
    begin
      MsgBox('Uninstaller versi lama tidak bisa dijalankan. Instalasi dibatalkan ' +
             'agar berkas program tidak bercampur.', mbError, MB_OK);
      Abort;
    end;

    // Uninstaller Inno menyalin dirinya ke folder sementara lalu keluar lebih
    // dulu; proses salinan itu masih bekerja. Tunggu sampai berkas program lama
    // benar-benar hilang (maksimal 60 detik) sebelum memasang berkas baru.
    for Percobaan := 1 to 300 do
    begin
      if not FileExists(Uninstaller) and
         not FileExists(ExpandConstant('{app}\SuDesApp.exe')) then Break;
      Sleep(200);
    end;

    if FileExists(Uninstaller) or FileExists(ExpandConstant('{app}\SuDesApp.exe')) then
    begin
      MsgBox('Uninstaller versi lama belum selesai. Tutup jendela uninstaller yang ' +
             'muncul, lalu jalankan installer ini lagi.', mbError, MB_OK);
      Abort;
    end;

    if Hasil <> 0 then
    begin
      MsgBox('Uninstaller versi lama berhenti dengan kode ' + IntToStr(Hasil) + '.' +
             Chr(13) + Chr(10) + Chr(13) + Chr(10) +
             'Instalasi dibatalkan. Jalankan installer lagi; database desa sudah ' +
             'dicadangkan dan akan dipulihkan otomatis.', mbError, MB_OK);
      Abort;
    end;
  end;

  ForceDirectories(ExpandConstant('{app}'));
  if AdaDatabaseLama then
  begin
    PulihkanDatabase('desa.db');
    PulihkanDatabase('desa.db-wal');
    PulihkanDatabase('desa.db-shm');
    PulihkanDatabase('desa.db-journal');
  end;
end;

