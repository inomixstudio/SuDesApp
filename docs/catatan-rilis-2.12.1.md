# SuDesApp 2.12.1 — 8 November 2026

Patch sinkronisasi Android: impor antrean kini berjalan otomatis di latar (tidak perlu menekan Sinkron secara manual), kop dan skema formulir diterbitkan otomatis dari desktop dan ditarik otomatis oleh Android, keterangan/keperluan yang tadinya kosong saat impor kini diisi otomatis, dan halaman Sinkron tidak lagi mewajibkan mengisi nama perangkat — cukup tekan **Buat QR** atau **Buat Kode** dan nama dibuat otomatis.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Sinkron Serba Otomatis

- **Impor antrean otomatis (±20 detik)** di latar saat ada surat menunggu, dengan notifikasi “Sinkron otomatis: *n* surat masuk” dan saklar **Sinkron otomatis** di halaman Sinkron Android (bawaan aktif). Tombol manual tetap tersedia.
- **Kop & skema diterbitkan otomatis** saat aplikasi mulai, saat pengaturan desa disimpan, dan saat `SkemaFormulir.json` berubah — tanpa menekan tombol Terbitkan. Android menariknya otomatis saat aplikasi dibuka, tiap kembali ke menu utama bila lebih dari 5 menit, dan berkala oleh `PengirimWorker`.
- **Pasang perangkat satu klik**: tanpa textbox nama, nama perangkat dibuat otomatis (`Android ddMM-HHmmss`) dan tersedia tombol terpisah **Buat QR** / **Buat Kode**.

## Perbaikan Sinkronisasi

- Keterangan bawaan (SKTM, SKD, Domisili, SKU, Ahli Waris, Pengantar Adminduk, Tidak Punya Rumah), keperluan per jenis (termasuk NTCR), hari kematian, dan pejabat penandatangan (KEMATIAN/KENAL_LAHIR) kini diisi saat impor — surat hasil impor tidak lagi ber-keterangan kosong.
- Pilihan di daftar (mis. jenis kelamin anak ahli waris), alias PDF (instansi, umur, kewarganegaraan/RT-RW, hari/tanggal kematian, sebutan N1 Jejaka/Perawan/Duda/Janda), serta tanggal kini memakai pemilih tanggal ISO yang pasti terbaca desktop. Hitungan antrean di status bar kini menyertakan baris `gagal`.
- Batas antrean 10 kini diberi peringatan 8/10 dan 10/10 di Android, pesan “antrean penuh / perangkat dinonaktifkan / sesi tidak berlaku” ditampilkan apa adanya, dan halaman Pengaturan menampilkan daftar per surat (antrean + tertunda) dengan hapus per baris.
- Surat tertunda di perangkat kini terenkripsi (`AES/GCM` + Android Keystore, berkas `*.enc`; berkas lama plaintext tetap dibaca dan dimigrasi). Baris gagal setelah 3 kali coba kini diarsipkan ke folder `Gagal` dan dapat diimpor ulang dari desktop via **Impor ulang arsip gagal** — tanpa kirim ulang dari Android dan tanpa mengetik ulang dari PDF.

## Verifikasi

- `dotnet build SuDesApp.Wpf` hijau, 0 galat.
- `dotnet test SuDesApp.Tests` — 437/437 lolos.
- Gradle `assembleDebug` Android hijau.
- Migrasi Supabase `20261114000000_hapus_baris_antrean_sendiri.sql` (policy hapus baris antrean milik perangkat sendiri) diterapkan sekali di SQL Editor sebelum memakai tombol *Hapus* baris di Android.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.12.0 | satu patch kecil | hanya berkas yang berubah |
| di bawah 2.12.0 | installer penuh 2.12.1 | ± 100 MB |

Aplikasi memilih jalurnya sendiri: tambalan hanya dipasang bila versi terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain diarahkan ke installer penuh. Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
