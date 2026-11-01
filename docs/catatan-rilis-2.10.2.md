# SuDesApp 2.10.2 — 2 November 2026

Pembaruan untuk aplikasi yang sudah terpasang: memperbaiki tampilan dan
kemudahan penggunaan menu APBDes, Perdes, dan Aktivasi Lisensi, menambahkan
pemantauan masa berlaku sertifikat situs IDM, memperbaiki API Desa beserta
halamannya, dan menyelaraskan menu aplikasi Android dengan sidebar desktop.
Tidak ada perubahan format data, jadi `desa.db`, `Templates`, dan arsip surat
lama dipakai apa adanya.

## Fitur Baru

- **Pemantauan masa berlaku sertifikat situs IDM.** Aplikasi memeriksa
  sertifikat TLS situs pusat IDM secara berkala dan memberi peringatan dini jauh
  sebelum masa berlakunya habis — kejadian sertifikat kedaluwarsa pada Agustus
  2026 sempat membuat halaman IDM kosong tanpa keterangan. Hasil pemeriksaan
  disimpan agar halaman IDM dan pemantau latar membaca sumber yang sama.
- **Menu Android menyusul sidebar desktop.** Submenu beranda ponsel kini
  disusun dari satu katalog (`SuDesApp.Mobile/Katalog/katalog-surat.json`)
  sehingga daftar menu Android selalu sama dengan menu sidebar desktop; nomor
  surat ponsel dihitung per awalan seperti desktop.

## Perbaikan

- **Menu APBDes lebih ringkas.** Kartu ringkasan APBDes (Pendapatan, Belanja,
  Pembiayaan, Total, Realisasi, Sisa) diperkecil dan disusun dalam satu baris
  sehingga daftar dokumen di bawahnya tidak lagi terasa sempit.
- **Menu Perdes lebih praktis.** Kartu contoh Perdes dibuat lebih ringkas,
  ikon keadaan kosong pada tabel arsip tidak lagi menutupi isi tabel, dan
  formulir Perdes mendukung melampirkan berkas Word/PDF seperti SK dan Perkades.
- **Menu Aktivasi Lisensi disederhanakan.** Judul menu dan header halaman
  kini "Aktivasi Lisensi"; tombol "Bayar / Berlangganan (Full Service)" pada
  kartu status langganan disembunyikan karena aktivasi dijalankan secara
  manual lewat kode aktivasi.
- **API Desa lebih mudah dijalankan.** Ditambahkan lapisan sesi pada listener
  API beserta ujinya, kotak status dan panduan "Cara Menjalankan" pada halaman
  API diperbaiki, dan `docs/api-desa.md` diperbarui agar sesuai perilaku
  endpoint yang sekarang.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.7.2, 2.8.0, 2.9.0, 2.10.0, 2.10.1 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.7.2 | installer penuh 2.10.2 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi
terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain
diarahkan ke installer penuh. Jalur installer juga dipakai bila folder
aplikasi tidak dapat ditulis atau tambalan gagal dipasang. Aplikasi versi
2.9.0 ke atas mengunduh paket pembaruan sendiri di latar belakang setelah
pengguna masuk.

## Aset

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.2_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.2_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.2_win-x64.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.2_win-x64.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.2.zip` | Pembaruan kecil untuk aplikasi 2.7.2–2.10.1 yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan
versi baru, dan `Templates` tidak dihapus saat pencopotan. Tanpa instalasi,
ekstrak ZIP lalu jalankan `SuDesApp.exe`.

## Verifikasi unduhan

Setiap aset installer dan zip portable disertai berkas `.sha256` senilai
isinya. Periksa di Windows dengan
`Get-FileHash .\SuDesApp_2.10.2_Setup.exe -Algorithm SHA256`, atau di
Linux/macOS dengan `sha256sum -c SuDesApp_2.10.2_Setup.exe.sha256`. Aplikasi
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
