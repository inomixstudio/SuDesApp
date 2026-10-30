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
| `SuDesApp_2.10.0_win-x64_portable.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.0_win-x64_portable.zip.sha256` | Sidik jari SHA-256 paket |

Ekstrak ZIP lalu jalankan `SuDesApp.exe`. Paket ini belum ditandatangani
Authenticode dan konfigurasi server lisensinya masih menunjuk `localhost`;
paket produksi dibuat lewat workflow Release dengan sertifikat dan secret
lisensi. Aplikasi web/API dijalankan terpisah dari paket desktop ini.
