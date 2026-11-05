# SuDesApp 2.10.4 — 5 November 2026

Pembaruan ini menutup tindak lanjut audit mutu: database desa yang terenkripsi
maupun SQLite biasa dibuka dan dipulihkan dengan lebih tahan terhadap berkas
yang masih terkunci, startup dan penutupan aplikasi tidak lagi meninggalkan
jendela tanpa jalan keluar, dan galat dari pekerjaan latar belakang tercatat
alih-alih hilang diam-diam. Pemasangan versi baru juga mencopot versi lama
secara otomatis tanpa menghilangkan database atau template desa.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama
dipakai apa adanya. Database baru tetap dibuat sebagai SQLite biasa, sedangkan
database terenkripsi dari versi lama tetap dibuka memakai berkas kuncinya.

## Perbaikan

- **Database terenkripsi dan database biasa dibuka lewat jalur yang sama.**
  Lokasi database dibaca dari connection string dengan parser SQLite, bukan
  pemotongan teks, sehingga opsi lain seperti `Password` tidak lagi ikut
  dianggap bagian dari nama folder. Jalur juga langsung dinormalkan menjadi
  absolut dan konsisten, sehingga database yang sama tidak terlihat sebagai
  lokasi berbeda hanya karena campuran `/` dan `\`.
- **Pemulihan cadangan tidak lagi meninggalkan berkas terkunci.** Setelah isi
  database dipulihkan, koneksi yang sudah selesai dilepas dari pool. Sebelumnya
  handle itu masih menahan `desa.db`, sehingga pemeriksaan berikutnya, pencadangan
  lanjutan, atau penggantian berkas gagal dengan pesan "used by another process"
  justru setelah pemulihan berhasil.
- **Pemeriksaan bentuk database tidak gagal saat berkas sedang diganti.**
  Pembacaan header memakai mode berbagi baca/tulis/hapus, sama seperti operasi
  pemulihan dan pemasangan versi baru. Database yang sehat tidak lagi dilaporkan
  sebagai "berkas lama terkunci" hanya karena ada koneksi lain yang sedang aktif.
- **Sumber database di memori tidak lagi berubah menjadi berkas.** `:memory:`
  dan sumber non-berkas lain dibiarkan apa adanya, bukan ditempelkan ke folder
  aplikasi sebagai berkas bernama `:memory:`.

## Peningkatan

- **Startup yang gagal tidak meninggalkan layar splash.** Bila penyiapan
  jendela login atau jendela utama gagal, splash ditutup dan aplikasi berhenti
  dengan pesan yang menyebut langkah berikutnya — bukan proses tanpa jendela
  yang hanya bisa diakhiri lewat Task Manager.
- **Penutupan aplikasi tidak lagi membeku karena backup.** Backup harian ke
  Google Drive berjalan asinkron saat jendela ditutup dengan batas 20 detik;
  pekerjaan yang wajar tetap selesai, tetapi penutupan tidak pernah menunggu
  tanpa batas.
- **Galat pekerjaan latar belakang tercatat.** Handler tombol asinkron dan
  tugas yang dijalankan tanpa ditunggu kini melewati satu penangan galat yang
  menulis `error.log` dan menampilkan pesan yang sesuai, sehingga kegagalan
  tidak lagi hilang tanpa jejak.
- **Register Surat memuat pilihan secara berurutan.** Seluruh permintaan awal
  halaman memakai satu koneksi database yang sama; menjalankannya bersamaan
  hanya menambah risiko database terkunci tanpa mempercepat tampilan.
- **Tabel dan daftar panjang memakai virtualisasi.** Baris yang tidak terlihat
  tidak lagi dibuat, sehingga halaman register, riwayat, dan data warga tetap
  responsif pada data besar.
- **Peringatan nullable dan catch kosong dituntaskan.** Build kembali bersih
  tanpa peringatan; setiap catch yang sengaja melanjutkan operasi menyebut
  alasan konkretnya agar kegagalan tidak tersamarkan.
- **Uji asap XAML diperluas.** Kamus resource aplikasi dan seluruh view
  ber-konstruktor tanpa parameter dimuat di thread STA pada setiap build,
  sehingga kunci resource yang hilang atau XAML rusak tertangkap sebelum
  aplikasi dijalankan manual.
- **Sesi sistem API tidak lagi menulis peran global.** Peran Administrator
  untuk pekerjaan API disimpan per-aliran (AsyncLocal), sehingga tidak bocor
  ke sesi operator di UI atau ke permintaan lain yang berjalan bersamaan.
  Permintaan API yang membawa token login juga memakai peran pengguna itu,
  bukan peran sistem.
- **Berkas database yang terkunci tidak lagi disebut terenkripsi.** Pemeriksaan
  header membedakan berkas yang tidak bisa dibaca dari database SQLCipher,
  sehingga pesan yang muncul menyebut berkas sedang dipakai, bukan kunci yang
  hilang.
- **State sesi memiliki satu sumber.** Nama pengguna tidak lagi disimpan di
  properti statis terpisah dari `SessionContext`; sapaan beranda, judul jendela,
  dan notifikasi sambutan membaca identitas sesi yang sama dengan penegakan izin.
- **Upgrade mencopot versi lama lebih dulu.** Installer mencadangkan `desa.db`
  beserta sidecar WAL/SHM, menjalankan uninstaller versi sebelumnya secara
  senyap, lalu memulihkan database sebelum memasang berkas baru. Database,
  template yang sudah diedit, dan hasil PDF desa tidak ikut dihapus.
- **Database lama dipulihkan otomatis saat aplikasi pertama dibuka.** Bila
  database aktif tidak terbaca, aplikasi mencari database sehat di folder
  instalasi lama, folder data pengguna, dan cadangan `desa*.db` di sekitarnya.
  Isinya disalin ke database baru melalui pemulihan SQLite, berkas lama
  dipindahkan ke samping, dan aplikasi tetap bisa dijalankan. Bila tidak ada
  kandidat sehat, database baru dibuat dan berkas lama tidak pernah dihapus.

## Verifikasi

- `dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj -c Debug` — berhasil,
  0 peringatan, 0 galat.
- Seluruh uji database lulus, termasuk pemulihan dari cadangan terenkripsi ke
  database biasa, pembukaan database baru tanpa kunci, dan tiga uji pemulihan
  otomatis (database sehat, tanpa kandidat, serta database terkunci).
- Seluruh 393 uji lulus, termasuk uji isolasi peran sesi sistem, uji berkas
database terkunci, dan uji asap seluruh view.
- Installer diuji pada instalasi bersih dan upgrade: `desa.db` dan template
  milik desa tetap utuh setelah uninstaller versi lama dijalankan.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.10.3 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.10.3 | installer penuh 2.10.4 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi
terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain
diarahkan ke installer penuh. Jalur installer juga dipakai bila folder
aplikasi tidak dapat ditulis atau tambalan gagal dipasang. Installer penuh
menjalankan uninstaller versi lama terlebih dahulu, setelah mencadangkan
database desa.

## Aset rilis

Aset di bawah dibangun dari kode 2.10.4 pada tag `v2.10.4`.

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.4_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.4_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.4_win-x64.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.4_win-x64.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.4.zip` | Pembaruan kecil untuk aplikasi 2.10.3 yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan
versi baru, dan `Templates` tidak dihapus saat pencopotan. Tanpa instalasi,
ekstrak ZIP lalu jalankan `SuDesApp.exe`.

## Verifikasi unduhan

Setelah aset dibangun, setiap installer dan zip portable disertai berkas
`.sha256` senilai isinya. Periksa di Windows dengan
`Get-FileHash .\SuDesApp_2.10.4_Setup.exe -Algorithm SHA256`, atau di
Linux/macOS dengan `sha256sum -c SuDesApp_2.10.4_Setup.exe.sha256`. Aplikasi
juga memverifikasi SHA-256 paket sebelum memasangnya.

## Persyaratan sistem

- Windows 10 versi 1607 atau lebih baru, atau Windows 11, arsitektur x64.
- Tidak perlu memasang .NET: kedua paket membawa runtime .NET 10 sendiri.
- Ruang disk sekitar 410 MB untuk berkas program, ditambah ruang untuk data desa.
- Pemasangan memakai `%LocalAppData%\Programs\SuDesApp`, jadi tidak menuntut
  hak administrator.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur:
  <https://github.com/inomixstudio/SuDesApp/issues>
- Panduan pembaruan dan tambalan: `docs/PEMBARUAN-TAMBALAN.md`
- Tanda tangan dan verifikasi rilis: `docs/TANDA-TANGAN-RILIS.md`
