# SuDesApp — Sistem Surat Desa

Aplikasi desktop Windows (WPF) untuk administrasi surat-menyurat desa: pembuatan
surat resmi dengan penomoran sesuai perbup, register & agenda digital, data
penduduk lengkap dengan mutasi, layanan permintaan surat via WhatsApp/API, sampai
alur persetujuan (ajukan → verifikasi → tanda tangan) yang opsional per surat.

## Fitur utama

- **Buat & cetak surat** — 20+ jenis surat bawaan (SKTM, SKU, domisili, kematian,
  NTCR, rekening koran, …) plus Template Surat sendiri lewat wizard atau impor
  berkas Word; penomoran otomatis mengikuti Perbup (rujukan di
  `docs/referensi-penomoran-surat.md`).
- **Register & agenda** — register surat, register NTCR, buku agenda surat
  masuk/keluar, arsip keputusan/perdes, lengkap dengan ekspor Excel/CSV/JSON/PDF.
- **Data warga** — penduduk, Kartu Keluarga, mutasi (aktif/baru/pindah/meninggal),
  impor & ekspor massal, laporan rekapitulasi kependudukan (PDF + Excel).
- **Alur persetujuan opsional** — per surat: ajukan → verifikasi → tanda tangan
  (atau tolak), dengan label cetak status dan lampiran scan surat bertanda
  tangan. Surat tanpa alur tetap terbit langsung seperti biasa
  (lihat `docs/alur-persetujuan-surat.md`).
- **QR verifikasi** — setiap surat terbit membawa kode + QR di kaki surat;
  pihak luar (bank/instansi) bisa memeriksa keasliannya.
- **API desa** — endpoint HTTP lokal untuk sistem luar memantau statistik, status
  persetujuan, dan rekap laporan bulanan (`docs/api-desa.md`).
- **Layanan online** — permintaan surat masuk via WhatsApp Cloud API / Google
  Sheet / API desa ke antrean operator.
- **Multi-pengguna** — akun dengan peran & izin, kunci akun setelah percobaan
  login gagal (`docs/policy-peran-pengguna.md`).
- **Keamanan data** — pencadangan database ke Google Drive (otomatis saat
  ditutup) + ekspor/impor manual, tutup buku tahunan
  (`docs/policy-tutup-buku-dan-retensi.md`), riwayat aktivitas yang bisa diekspor
  ke Excel.

## Kebutuhan

- Windows 10/11 dengan **.NET 8 SDK** (build) atau runtime .NET 8 (menjalankan).
- Microsoft Word opsional — hanya untuk konversi berkas `.doc` lama dan impor
  template.

## Build & uji

```bash
# Library inti (TreatWarningsAsErrors aktif)
dotnet build SuDesApp.Core/SuDesApp.Core.csproj --nologo -v q

# Aplikasi WPF
dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj --nologo -v q

# Seluruh uji otomatis (SQLite in-memory)
dotnet test tests/SuDesApp.Core.Tests/SuDesApp.Core.Tests.csproj --nologo -v q
```

CI (GitHub Actions) menjalankan build + uji yang sama di `windows-latest`
(lihat `.github/workflows/ci.yml`); rilis berjalan lewat `release.yml`. Bila Actions
sedang tidak bisa dipakai, rilis dapat dibuat dan diunggah dari komputer ini dengan
`Tools/publish-release.ps1` (lihat `PANDUAN-GITHUB-ACTIONS.md`).

## Struktur repositori

| Folder | Isi |
|---|---|
| `SuDesApp.Core/` | Logika bisnis: data (SQLite/Dapper), layanan, generator PDF (QuestPDF), API desa, integrasi WhatsApp/Google |
| `SuDesApp.Wpf/` | Aplikasi WPF: ViewModel, View, tema, navigasi |
| `tests/SuDesApp.Core.Tests/` | Uji otomatis seluruh Core (termasuk end-to-end listener API) |
| `docs/` | Dokumentasi kebijakan & API (tautan di bawah) |
| `Templates/`, `Resources/`, `installer/`, `Tools/` | Template surat, sumber daya, installer, alat bantu |

## Dokumentasi

- [`docs/api-desa.md`](docs/api-desa.md) — API desa untuk sistem luar
- [`docs/alur-persetujuan-surat.md`](docs/alur-persetujuan-surat.md) — alur persetujuan opsional
- [`docs/policy-peran-pengguna.md`](docs/policy-peran-pengguna.md) — peran & izin pengguna
- [`docs/policy-tutup-buku-dan-retensi.md`](docs/policy-tutup-buku-dan-retensi.md) — tutup buku tahunan & retensi
- [`docs/referensi-penomoran-surat.md`](docs/referensi-penomoran-surat.md) — acuan penomoran
- [`docs/PEMBARUAN-TAMBALAN.md`](docs/PEMBARUAN-TAMBALAN.md) — mekanisme pembaruan kecil
