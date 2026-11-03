# SuDesApp 2.10.3 — 3 November 2026

Pembaruan untuk aplikasi yang sudah terpasang: database desa dari versi lama
yang masih terenkripsi kembali bisa dibuka, database tanpa enkripsi tetap
dipakai apa adanya, dan aplikasi tidak lagi langsung menutup diri ketika
database tidak bisa dibuka — pengguna ditawari memilih berkas database lain.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama
dipakai apa adanya. Bentuk database (SQLite biasa atau terenkripsi SQLCipher)
ditentukan dari berkasnya sendiri, bukan dari nomor versi aplikasi.

## Perbaikan

- **Database terenkripsi versi lama kembali bisa dibuka.** Sejak enkripsi
  SQLCipher dihapus pada 2.10.2, aplikasi hanya mengenal database SQLite biasa
  sehingga database desa dari versi sebelumnya ditolak dan aplikasi berhenti
  di layar splash. Dukungan membaca SQLCipher dipasang kembali, dan database
  baru tetap dibuat sebagai SQLite biasa seperti pada 2.10.2.
- **Kedua bentuk database bisa dibaca dan ditulis.** Database terenkripsi
  dibuka memakai berkas kunci `DesaKunciDb.bin` yang sudah ada di komputer
  desa (DPAPI, akun Windows yang sama). Database tanpa enkripsi tidak
  memerlukan kunci apa pun.
- **Bentuk database tidak diubah diam-diam.** Database SQLite biasa tidak
  pernah dienkripsi ulang, dan database terenkripsi tidak pernah didekripsi
  saat aplikasi dibuka. Berkas kunci hanya dibaca — aplikasi tidak pernah
  membuat atau menimpa kunci.
- **Cadangan, pemulihan, dan impor menangani dua bentuk.** Cadangan terenkripsi
  yang dipulihkan ke aplikasi tanpa enkripsi menghasilkan database tanpa
  enkripsi, dan sebaliknya. Impor database menyesuaikan bentuk berkas dengan
  database yang sedang dipakai, sehingga hasil impor tetap bisa dibuka pada
  sesi berikutnya.
- **Pesan yang bisa ditindaklanjuti.** Bila database terenkripsi tidak bisa
  dibuka karena berkas kuncinya tidak ada atau berasal dari akun Windows lain,
  pesannya menyebut lokasi berkas kunci yang dicari, cara menyalinnya, dan
  pilihan memulihkan cadangan — bukan galat teknis di setiap halaman.

## Peningkatan

- **Pemulihan saat database tidak bisa dibuka.** Aplikasi tidak lagi langsung
  menutup diri. Pengguna ditawari memilih berkas database lain (mis. hasil
  pemulihan cadangan) dari layar pembuka; berkas diperiksa lebih dahulu, lokasi
  disimpan, lalu aplikasi dibuka ulang dengan berkas itu. Berkas yang gagal
  dibuka tidak pernah diubah atau ditimpa, dan pilihan keluar tetap tersedia.
- **Penjelasan di Pengaturan dan Tentang Aplikasi.** Penjelasan bagian Database
  Desa dan daftar keamanan pada halaman Tentang Aplikasi kini menyebut bahwa
  database baru berupa SQLite biasa, sedangkan database terenkripsi dari versi
  lama tetap didukung selama berkas kuncinya tersedia.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.7.2, 2.8.0, 2.9.0, 2.10.0, 2.10.1, 2.10.2 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.7.2 | installer penuh 2.10.3 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi
terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain
diarahkan ke installer penuh. Jalur installer juga dipakai bila folder
aplikasi tidak dapat ditulis atau tambalan gagal dipasang. Aplikasi versi
2.9.0 ke atas mengunduh paket pembaruan sendiri di latar belakang setelah
pengguna masuk.

## Aset

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.3_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.3_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.3_win-x64.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.3_win-x64.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.3.zip` | Pembaruan kecil untuk aplikasi 2.7.2–2.10.2 yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan
versi baru, dan `Templates` tidak dihapus saat pencopotan. Tanpa instalasi,
ekstrak ZIP lalu jalankan `SuDesApp.exe`.

## Verifikasi unduhan

Setiap aset installer dan zip portable disertai berkas `.sha256` senilai
isinya. Periksa di Windows dengan
`Get-FileHash .\SuDesApp_2.10.3_Setup.exe -Algorithm SHA256`, atau di
Linux/macOS dengan `sha256sum -c SuDesApp_2.10.3_Setup.exe.sha256`. Aplikasi
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
