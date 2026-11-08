# SuDesApp 2.12.0 — 8 November 2026

Pembaruan ini menyederhanakan masa uji coba menjadi trial otomatis 30 hari tanpa form, mengunci cetak dan ekspor setelah masa itu habis dengan pengecualian Daftar Hadir, ikut mengunci sinkron Android, dan merapikan dialog persetujuan awal agar teksnya tidak terpotong. Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Trial Otomatis & Kunci Setelah Habis

- **Trial 30 hari kini aktif otomatis saat aplikasi dijalankan** — tanpa form dan tanpa tombol Mulai Trial. Semua fitur langsung berjalan normal pada pemasangan pertama.
- **Setelah trial habis, cetak PDF dan ekspor Excel dikunci di semua menu, kecuali cetak Daftar Hadir** yang tetap boleh dipakai. Data tetap bisa dibaca dan dikelola.
- **Sinkron Android ikut dikunci** setelah trial habis (tarik antrean, pasang/cabut perangkat, dan terbitkan skema/pengaturan).
- **Popup peringatan mengarahkan ke menu AKTIVASI LISENSI atau hubungi developer** lewat tombol WhatsApp di halaman itu. Pesan kunci pada tiap fitur memakai kalimat yang sama agar tidak membingungkan.

## Dialog Awal & Tampilan

- **Dialog persetujuan kebijakan awal (Kebijakan Privasi & Syarat Ketentuan) kini tidak memotong teks** — pernyataan persetujuan, daftar dokumen, pesan galat, dan catatan kaki semuanya membungkus pada lebar dialog. Jendela pembaca dokumen juga bisa digulir mendatar untuk baris/tabel lebar agar isinya tidak hilang diam-diam.
- Ikon Beranda memakai glyph tema (bukan emoji warna OS) dan statistik warga + ringkasan register dimuat paralel dengan cache 30 menit, sehingga muat awal lebih ringan.

## IDM & Surat

- **Halaman IDM dapat dibatalkan pemuatannya**; pencocokan wilayah (sekitar 90 ribu baris) kini di thread latar agar antarmuka tidak tersendat, dan pemeriksaan Indeks Desa 2025 berjalan paralel dengan loop tahun.
- **Kotak kode formulir F-2.17 pada Surat Kematian kini benar-benar di sisi kanan** (sebelumnya menempel di kiri); jarak baris dilonggarkan agar lebih lega.
- Katalog menu surat kini menjadi satu sumber alias label lama (`IZIN ORTU / SUAMI`, `NUMPANG NIKAH`, `IJIN TINGGAL`) yang dipakai WPF dan backend bersama, sehingga label lama dari dokumen desa tetap dikenali.

## Verifikasi

- `dotnet build SuDesApp.slnx` hijau, 0 peringatan, 0 galat.
- `dotnet test SuDesApp.Tests` — 425/425 lolos.
- Migrasi Supabase `20261112000000_revoke_pasang_cabut.sql` (cabut akses anon ke pasang/cabut perangkat) dan `20261113000000_hapus_sandi_pasangan_limit_kode.sql` (hapus sandi pasangan setelah dipakai + batas percobaan per kode) diterapkan sekali di SQL Editor sebelum perangkat Android baru dipakai bersamaan.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.11.0 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.11.0 | installer penuh 2.12.0 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain diarahkan ke installer penuh. Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
