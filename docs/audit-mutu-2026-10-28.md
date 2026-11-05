# Audit Mutu Aplikasi — 28 Oktober 2026

Catatan hasil audit menyeluruh atas alur data, penyimpanan, keamanan akun, dan
dokumentasi. Isinya: apa yang sudah baik, apa yang diperbaiki pada pass ini
(beserta bukti ujinya), dan saran yang **belum** dikerjakan agar tidak
diklaim selesai.

## 1. Ruang lingkup dan cara pemeriksaan

- Membaca jalur masuk utama: startup WPF, login, layanan pengguna, API lokal,
  cadangan/pemulihan database, serta penyimpanan preferensi.
- Menjalankan build solusi dan seluruh uji proyek.
- Menjalankan aplikasi WPF sungguhan sampai layar login, lalu menutupnya untuk
  memeriksa urutan shutdown.
- Bukan uji penetrasi dan bukan audit kriptografi formal; temuan di bawah hanya
  yang bisa dibuktikan dari kode atau uji terfokus.

## 2. Yang sudah baik dan tidak perlu diubah

- Kata sandi disimpan sebagai PBKDF2 bersalt (`KataSandiPengguna`), bukan teks
  biasa; validasi kekuatan sandi menolak sandi yang sama dengan username.
- Kunci API dan token memakai perbandingan waktu-tetap (`ApiKunci`).
- Lisensi memakai token bertanda tangan RSA dan diverifikasi sebelum dipakai.
- Data sensitif di mesin pengguna (token Google, kunci aktivasi) dilindungi
  DPAPI, bukan berkas teks biasa.
- Database desa sengaja disimpan sebagai SQLite biasa tanpa enkripsi/kunci
  terpisah; keputusan itu sudah didokumentasikan dan tidak menimbulkan
  ketergantungan lisensi pihak ketiga.
- Pipeline PDF sudah memakai `SudesPdf` sendiri (tanpa QuestPDF/PDFium) sehingga
  distribusi komersial tidak terikat lisensi pustaka pihak ketiga.

## 3. Temuan yang diperbaiki pada pass ini

### 3.1 Cadangan database kehilangan isi WAL

**Masalah.** `BuatBackupAsync` menyalin berkas database dengan `File.Copy`.
Database berjalan pada `journal_mode=WAL`, sehingga transaksi terakhir masih
berada di `desa.db-wal` dan tidak ikut tersalin. Cadangan tampak berhasil,
tetapi surat terbaru hilang — dan justru cadangan inilah jaring pengaman
sebelum operasi destruktif.

**Perbaikan.** Cadangan dibuat dengan `VACUUM INTO` (snapshot konsisten lewat
SQLite sendiri), dan pemulihan memakai backup API SQLite
(`BackupDatabase`), bukan mengganti berkas di level OS. Cadangan pra-restore
juga dibuat dengan cara yang sama sehingga kegagalan di tengah pemulihan masih
bisa dipulihkan.

**Bukti.** `SuDesApp.Tests/Configuration/PemulihanDatabaseTests.cs`
(memegang koneksi aktif + data di WAL, lalu memulihkan dan memeriksa isi).

### 3.2 Penulisan preferensi tidak atomik

**Masalah.** `AppPreferenceStore` menulis berkas preferensi di tempat. Bila
proses berhenti di tengah penulisan (listrik putus, aplikasi ditutup paksa),
berkas terpotong dan pembaca jatuh ke nilai bawaan: alamat server lisensi,
kode aktivasi, dan pengaturan lain hilang sekaligus.

**Perbaikan.** Penulisan dipindahkan ke `BerkasPreferensi`: tulis berkas
sementara, lalu ganti secara atomik dengan `File.Replace`, dengan salinan
`.bak`. Kebijakan kunci tetap satu tempat di `AppPreferenceStore`.

**Bukti.** `SuDesApp.Tests/Utilities/BerkasPreferensiTests.cs`.

### 3.3 Kebijakan kunci login punya dua pemilik yang saling menumpuk

**Masalah.** Setelah tiga kegagalan, layar login menolak semua percobaan
selama lima menit. Penghitungnya tidak dibersihkan saat login berhasil, jadi
setelah masa tunggu lewat satu salah ketik langsung mengunci layar lagi.
Pesannya juga selalu "tunggu 5 menit" meski sisa waktunya tinggal satu menit.
Penghitung itu juga berlaku untuk seluruh proses: tiga salah ketik pada akun
warga ikut mengunci akun pusat penyedia yang dipakai operator.

**Perbaikan.** Kepemilikan dipisah. Akun database memakai kunci menetap milik
`PenggunaService` (tersimpan di database, tetap berlaku setelah aplikasi
dibuka ulang). Akun pusat memakai `PembatasPercobaanLogin` yang menyimpan
hitungan dan waktu gagal terakhir di preferensi lokal — tanpa kata sandi —
sehingga kuncinya juga bertahan setelah restart. Cadangan `Login.dat` memakai
pembatas memori dengan batas dan durasi yang sama. Keduanya diperiksa sebelum
layanan dipanggil dan dibersihkan pada login berhasil. Pesan menyebut sisa
menit sebenarnya; jam dapat disuntikkan agar perilakunya bisa diuji tanpa
menunggu lima menit.

**Bukti.** `SuDesApp.Tests/UI/PercobaanLoginTests.cs` (batas akun pusat,
kunci pusat yang bertahan setelah restart tanpa menyimpan sandi, reset limiter,
dan sandi berhuruf besar lewat database sungguhan).

### 3.4 Kata sandi berhuruf besar tidak bisa dipakai masuk

**Masalah.** Layar login menurunkan seluruh kata sandi ke huruf kecil
(`Password.ToLower()`), sedangkan akun yang dibuat atau direset lewat Kelola
Pengguna menyimpan hash dari sandi apa adanya. Akun bersandi `Rahasia123`
karena itu **tidak akan pernah bisa masuk**, sementara pencariannya mengurangi
entropi sandi secara diam-diam.

**Perbaikan.** Hanya spasi tepi yang dibuang; huruf besar/kecil dipertahankan,
sama seperti saat sandi dibuat.

**Bukti.** Uji `SandiBerhurufBesar_BisaMasukLewatDatabaseSungguhan` memakai
`PenggunaService` + database SQLite sungguhan, bukan layanan palsu.

### 3.5 Dokumentasi menyebut fitur yang sudah dihapus

Beberapa dokumen masih mengklaim integrasi WhatsApp/Google Sheet/Formulir yang
sudah tidak ada. Diperbarui:

- `PANDUAN-GITHUB-ACTIONS.md` — kredensial Google hanya untuk pencadangan Drive.
- `docs/policy-peran-pengguna.md` — alasan akun aplikasi, contoh kode internal,
  dan lingkup koneksi Google.
- `docs/tema-dan-warna.md` — contoh halaman memakai Panduan Awal.
- `Tools/check-kontras-wcag.ps1` — teks laporan warna tetap.

Catatan rilis historis sengaja **tidak** ditulis ulang: itu arsip versi lama,
dan entri penghapusan fitur sudah ada di catatan rilis terbaru.

### 3.6 Catch kosong tanpa alasan

**Masalah.** Ada 12 `catch` yang badannya benar-benar kosong — pada penutupan
aplikasi, pembatalan tugas latar, pembersihan berkas sementara, dan
pemeriksaan berkas pratinjau — ditambah beberapa komentar samar seperti
`/* abaikan */` atau `/* ignore */`. Tanpa alasan, penelanan galat menyulitkan
diagnosis dan menyamarkan kesalahan pemrograman.

**Perbaikan.** Setiap `catch` kosong kini menyebut sebab konkretnya (mis.
"timer mungkin sudah berhenti", "dibatalkan permintaan berikutnya", "berkas
sementara terkunci"), dan komentar samar diganti dengan akibat yang jelas.
Tidak ada log baru yang ditambahkan: pada jalur penutupan, pembatalan, dan
pembersihan terbaik, catatan log hanya akan menjadi derau. Dua pemeriksaan
berkas best-effort disederhanakan menjadi satu `catch (Exception)` dengan
alasan yang sama, karena keduanya memang hanya ingin melanjutkan pemuatan.

**Bukti.** Build 0 peringatan, 261 uji lulus, dan pencarian ulang tidak lagi
menemukan `catch` kosong di kode produksi — yang tersisa hanya teks skrip
PowerShell di dalam string `PatchUpdateService`.

## 4. Saran berikutnya (belum dikerjakan)

1. **Tandatangani installer dan executable.** **Selesai 29 Oktober 2026.**
   `Tools/Tanda-Tangani-Rilis.ps1` menandatangani `SuDesApp.exe` dan installer
   lewat `publish-release.ps1`, dan `.github/workflows/release.yml` mengambil
   sertifikat dari secret. Rincian di [`TANDA-TANGAN-RILIS.md`](TANDA-TANGAN-RILIS.md).
2. **Naikkan pemeriksaan izin ke lapis layanan sebelum API dibuka ke jaringan.**
   **Selesai 5 November 2026.** `SessionContext.Peran` kini menulis ke identitas
   per-aliran, dan `SesiSistem()` tidak lagi mengubah peran global. Listener API
   memakai identitas pengguna saat permintaan membawa token sesi; endpoint tulis
   backend memanggil `Wajib`. API tetap mendengarkan di localhost secara bawaan,
   dan opsi jaringan tetap memerlukan keputusan operator.
3. **Rapikan `MainWindowViewModel` (≈1.850 baris).** **Selesai 29 Oktober 2026.**
   Kelas kini `partial`: berkas utama menyimpan state bersama dan konstruktor,
   sedangkan navigasi, perintah, dan status masing-masing punya berkas
   `MainWindowViewModel.Navigasi.cs`, `.Perintah.cs`, dan `.Status.cs`.
   Strukturnya dicatat di `SuDesApp.Wpf/ViewModels/ARCHITECTURE.md`.
4. **Uji asap UI otomatis.** **Selesai 5 November 2026.**
   `SuDesApp.Tests/UI/XamlAsapTests.cs` memuat kamus resource dari `App.xaml`
   lalu membuat seluruh view ber-konstruktor tanpa parameter di thread STA. Uji
   ini menangkap kunci resource yang hilang dan BAML yang rusak pada setiap
   build. Alur startup → login → tutup tetap diuji terpisah lewat harness UIA
   (`artifacts/uji-api-halaman/uji.ps1`) karena membutuhkan database, DI, dan
   interaksi login.

## 5. Verifikasi pass ini

- `dotnet build SuDesApp.slnx` — berhasil, 0 peringatan, 0 galat.
- `dotnet test SuDesApp.Tests` — seluruh uji lulus (lihat keluaran terakhir;
  jumlah bertambah dengan uji baru di atas).
- Aplikasi WPF dijalankan sungguhan: startup, layar login, sesi, dan halaman
  dokumentasi, lalu ditutup. Log menunjukkan `API desa dimatikan` saat keluar
  dan proses benar-benar berhenti — server lokal tidak tertinggal.

### Verifikasi tindak lanjut 5 November 2026

- `dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj -c Debug` — berhasil,
  0 peringatan, 0 galat.
- 29 tes database lulus: database terenkripsi dan SQLite biasa, pembuatan
  database baru, pemulihan lintas bentuk, serta pelepasan handle setelah
  pemulihan.
- 39 tes UI lulus, termasuk `XamlAsapTests` dan uji sesi/login.
- Catatan rilis 2.10.4 memuat perubahan yang terlihat pengguna
  (`docs/catatan-rilis-2.10.4.md`).

### Penutupan temuan audit ulang 5 November 2026

- `SessionContext.SesiSistem()` tidak lagi menulis `Peran` global; peran
  Administrator sesi sistem disimpan per-aliran. Uji
  `SesiSistem_Tidak_Menimpa_Peran_Global` menjaga sifat ini.
- Listener API memakai `MulaiSesiPermintaan` untuk permintaan bertoken, sehingga
  peran pengguna yang membawa token tidak berubah menjadi Administrator.
- Berkas database yang terkunci dilaporkan sebagai `Terkunci`, bukan
  `BerkasLamaTerkunci`; uji `Berkas_yang_dipakai_eksklusif_dilaporkan_terkunci_bukan_terenkripsi`
  menutup diagnosa yang menyesatkan itu.
- Uji asap XAML memuat seluruh view tanpa parameter, bukan tiga halaman saja.
- Dua komentar catch yang masih samar (`LisensiManager`, `RiwayatPembaruanStore`)
  diganti dengan alasan konkret.
- `artifacts/patch.json` sudah menyebut versi 2.10.4 dan menandai bahwa artefak
  belum dibangun; catatan rilis juga tidak lagi mengklaim aset yang belum ada.
- Seluruh suite: **390 lulus, 0 gagal**. Build WPF dan API: **0 peringatan,
  0 galat**.
