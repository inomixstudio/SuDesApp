# SuDesApp 2.13.2 — 9 November 2026

Tambalan Android: cetak PDF Daftar Hadir (dan seluruh PDF surat) tidak lagi error “Unsupported render mode”.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Android

- **Akar masalah**: `PdfZoomActivity` meraster halaman PDF dengan nilai mentah `0`, padahal `PdfRenderer.Page.render()` hanya menerima `RENDER_MODE_FOR_DISPLAY` atau `RENDER_MODE_FOR_PRINT` — nilai 0 melempar `IllegalArgumentException("Unsupported render mode")` yang membuat tombol Cetak selalu gagal tampil.
- **Perbaikan**: memakai konstanta resmi `PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY`. Terverifikasi di emulator: Cetak PDF Daftar Hadir membuka viewer zoomable dengan dokumen ter-render penuh (kopol, judul, tabel 10 baris, tanda tangan) tanpa error.

## Verifikasi

- Gradle `assembleDebug` Android hijau.
- Uji emulator: buka Daftar Hadir → Cetak PDF → viewer menampilkan `Halaman 1 dari 1` dengan isi utuh; tidak ada FATAL EXCEPTION / toast “Gagal membuat PDF”.
