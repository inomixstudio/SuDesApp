# SuDesApp 2.13.0 — 9 November 2026

Pembaruan kecil: kartu Aktivasi Lisensi dan Server Lisensi kini seragam dan sederhana, kode aktivasi manual dinormalisasi otomatis (huruf kecil/spasi dari salinan WhatsApp dirapikan) dengan petunjuk format live dan tombol Tempel/Hapus, halaman IDM menampilkan data terakhir yang terbit walau sertifikat situs pusat kedaluwarsa, pembuatan PDF dipindah ke thread latar supaya aplikasi tetap responsif, baris Hubungan Pelapor selalu tercetak sesuai formulir asli, dan Beranda memakai animasi masuk halus dengan angka menghitung naik.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Aktivasi Lisensi

- Kartu **Aktivasi Manual** hanya menulis “Masukan Kode.” dan kartu **Server Lisensi** diratakan dengan pola yang sama (label di atas, tombol sejajar). Blok Pusat Administrasi yang bersifat rahasia tidak lagi dicantumkan di informasi lisensi server.
- Tombol **Hubungkan** pada kartu Server Lisensi: simpan alamat, nyalakan server lokal bila tersedia, lalu coba sambung berulang sampai server menjawab, dan langsung periksa status langganan.
- Kode aktivasi manual kini dinormalisasi (huruf kecil, spasi, baris baru, dan tanda hubung yang hilang dirapikan menjadi `SUDES-XXXX-XXXX`), dicek bentuknya sebelum disimpan, didukung Enter = Simpan & Cek, Tempel dari papan klip, dan Hapus. Contoh format pada tooltip diperbaiki menjadi `SUDES-K7Q2-4831`.
- Perbaikan pelanggaran thread UI saat server lisensi lokal menyala (`InvalidOperationException: calling thread…`).

## IDM & API Desa

- **Halaman IDM menampilkan data terakhir yang terbit** (mis. desa Sumberjaya kini menampilkan penilaian 2024, 55 indikator) meski sertifikat `idm.kemendesa.go.id` kedaluwarsa sejak 16 Agustus 2026: klien turun ke HTTP dengan peringatan terbuka “Data ini diambil tanpa enkripsi…”, dan galat sertifikat hanya tampil bila jalur tanpa enkripsi pun tidak terjangkau. API Desa (`SuDesApp.Api`) juga diuji hidup: `/api/health`, `/api/versi`, menu, statistik, login sesi, dan proteksi 401 tanpa bearer đều menjadi benar.

## Surat & PDF

- Baris **Hubungan Pelapor dengan yang Meninggal Dunia** selalu tercetak pada Surat Keterangan Kematian sesuai formulir contoh F-2.17 — berisi nilai, atau titik-titik bila kosong (sebelumnya baris hilang total saat kosong).
- Pembuatan PDF (surat bawaan, Rekening Koran, Template Surat, dan Laporan IDM) dipindah ke thread latar supaya pratinjau, simpan, dan cetak ulang dari Register tidak membekukan aplikasi.

## Beranda

- Animasi masuk bertahap (fade + naik) pada header, statistik, pintasan, dan aktivitas; angka statistik menghitung naik; kartu pintasan terangkat halus saat dikursor.

## Verifikasi

- `dotnet build SuDesApp.slnx` hijau, 0 galat, 0 peringatan.
- `dotnet test SuDesApp.Tests` — 443/443 lolos.
- Gradle `assembleDebug` Android hijau.
- Uji hidup klien IDM asli: 2024 Berhasil (0,7179, MAJU, 55 baris), 2023 Berhasil, 2025/2026 TidakDitemukan — tahun terakhir yang terbit otomatis ditampilkan.

## Cara memasang

Aplikasi memilih jalurnya sendiri: tambalan hanya dipasang bila versi terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain diarahkan ke installer penuh. Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
