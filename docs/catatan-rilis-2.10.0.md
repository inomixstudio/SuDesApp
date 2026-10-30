# SuDesApp 2.10.0 — 30 Oktober 2026

Rilis ini menyatukan aplikasi desktop dengan backend web/API, mengganti EPPlus
dengan pustaka Excel sendiri, dan merapikan pratinjau PDF serta antarmuka.

## Fitur baru

- **Excel mandiri (SudesExPlus).** Seluruh ekspor dan impor Excel kini memakai
  pustaka `.xlsx` milik aplikasi sendiri; EPPlus beserta permintaan lisensi
  komersialnya dihapus. Berkas dari pihak lain bisa dibuka lalu disimpan ulang
  tanpa kehilangan font, isian, garis, perataan, bungkus teks, format angka,
  lebar kolom, tinggi baris, sel gabungan, panel beku, dan warna tema Excel.
  Rumus sel didukung, termasuk rumus bersama (shared) yang digeser per baris.
- **Website desa dan backend API.** Backend `SuDesApp.Api` dan antarmuka Blazor
  `SuDesApp.Ui` melayani browser: Formulir Warga di `/layanan-online` tanpa
  akun, antrean operator yang sama dengan API desa, serta halaman Register Surat
  dan Data Warga yang membaca database desa yang sama dengan desktop.
- **Menu web mengikuti sidebar desktop** — BUAT SURAT, FORMULIR, DATA DESA,
  DATA WARGA, PENGATURAN, LANGGANAN, dan BANTUAN, lengkap dengan akordeon.
- **Chip tahun IDM bisa diklik.** Memilih tahun langsung menampilkan skor,
  status, dan rincian indikator tahun itu; tahun terakhir tetap menjadi pilihan
  awal dan penanda TERBARU tidak ikut berpindah.

## Peningkatan

- Pratinjau dan cetak PDF memakai mesin SudesPdf sendiri: huruf tidak pecah,
  halaman pindai CCITT tampil benar, dan hasil cetak sama dengan pratinjau.
- Jarak garis kop ke judul surat dilonggarkan; Garuda pada naskah Perdes
  benar-benar di tengah dan paragrafnya rata kiri-kanan.
- Cetak Daftar Hadir menempatkan kop dan judul hanya di halaman pertama,
  sementara baris kepala tabel berulang di setiap halaman.
- Pratinjau permohonan Rekening Koran tersedia sebelum form disimpan.

## Perbaikan

- Teks tooltip mengikuti tema dan terbaca di tema terang maupun gelap.
- Ikon di dalam halaman fitur memakai set glyph yang sama dengan menu utama;
  tidak ada lagi glyph kotak atau ikon ganda, dan ukuran huruf navigasi sama.
- Kolom tanggal menyisipkan tanda `-` otomatis saat mengetik (`01-01-1990`).
- API tidak lagi memanggil lapisan enkripsi database yang sudah dihapus pada
  2.8.0, dan uji sesi memakai izin yang masih berlaku.

## Aset

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.0_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.0_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.0_win-x64_portable.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.0_win-x64_portable.zip.sha256` | Sidik jari SHA-256 paket portable |

Jalankan `SuDesApp_2.10.0_Setup.exe` untuk memasang aplikasi; data desa
(`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru dan
`Templates` tidak dihapus saat pencopotan. Tanpa instalasi, ekstrak ZIP lalu
jalankan `SuDesApp.exe`. Kedua paket belum ditandatangani Authenticode dan
konfigurasi server lisensinya masih menunjuk `localhost`; paket produksi dibuat
lewat workflow Release dengan sertifikat dan secret lisensi. Aplikasi web/API
dijalankan terpisah dari paket desktop ini.

## Verifikasi unduhan

Sidik jari SHA-256 berkas yang diterbitkan:

```
75e2b720d821467bdca70db4c8e889707fdc656740a5b1d451c956e4dff96925  SuDesApp_2.10.0_Setup.exe
67fae6d4399770831ebdcb9289db25505e66f2586a3f19a6d2941133f4f745dd  SuDesApp_2.10.0_win-x64_portable.zip
```

Periksa di Windows dengan
`Get-FileHash .\SuDesApp_2.10.0_Setup.exe -Algorithm SHA256`, atau di Linux/macOS
dengan `sha256sum -c SuDesApp_2.10.0_Setup.exe.sha256`. Nilai tersebut juga
tersedia sebagai berkas `.sha256` di setiap aset rilis.

## Persyaratan sistem

- Windows 10 versi 1607 atau lebih baru, atau Windows 11, arsitektur x64.
- Tidak perlu memasang .NET: kedua paket membawa runtime .NET 10 sendiri.
- Ruang disk sekitar 410 MB untuk berkas program, ditambah ruang untuk data desa.
- Pemasangan memakai `%LocalAppData%\Programs\SuDesApp`, jadi tidak menuntut hak
  administrator.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur:
  <https://github.com/inomixstudio/SuDesApp/issues>
- Panduan pembaruan dan tambalan: `docs/PEMBARUAN-TAMBALAN.md`
- Tanda tangan dan verifikasi rilis: `docs/TANDA-TANGAN-RILIS.md`
