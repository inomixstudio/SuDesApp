# SuDesApp 2.13.1 — 9 November 2026

Tambalan Android: menu Daftar Hadir tidak lagi error saat dibuka, simpan hasil edit surat, cetak ulang dari Register, dan penampil PDF diperbaiki.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Android

- **Daftar Hadir crash saat dibuka diperbaiki**: `tambahBaris` memasang baris peserta lewat rantai parent yang rapuh sehingga activity langsung tutup (`IllegalStateException: child already has a parent`); kini layout bingkai dikembalikan langsung.
- **Simpan hasil edit selalu gagal diperbaiki**: query MediaStore membaca kolom ID dari proyeksi DISPLAY_NAME; kini memproyeksikan `_ID` dan membacanya lewat indeks kolom yang benar.
- **Cetak ulang dari Register tidak lagi crash**: entri tanpa berkas lokal (`berkas!!`) tidak lagi NPE — ditangani URI atau berkas, lalu toast bila keduanya tidak ada.
- **Cetak PDF aman dari sinyal batal null**: `pembatalan!!` diganti sinyal nullable sepanjang jalur `onWrite`.
- **Penampil PDF tidak lagi me-recycle bitmap sebelum diganti**: gambar lama di-recycle setelah gambar baru terpasang, mencegah artefak/recycle-draw.

## Verifikasi

- `dotnet build SuDesApp.slnx` hijau, 0 galat.
- `dotnet test SuDesApp.Tests` — 443/443 lolos.
- Gradle `assembleDebug` Android hijau.
- Uji emulator: Daftar Hadir dibuka tanpa FATAL EXCEPTION (sebelum perbaikan reproduksi menunjukkan crash `DaftarHadirActivity.tambahBaris`).
