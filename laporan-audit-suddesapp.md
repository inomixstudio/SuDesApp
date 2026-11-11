# Laporan Audit menyeluruh — SuDesApp

**Tanggal:** 11 November 2026 (UTC+7)
**Repo:** D:\Aplikasi\DesaApp · branch `master`
**SDK:** 11.0.100-rc.3 (cocok dengan `global.json`)

---

## 1. Ringkasan baseline (verifikasi independen)

| Pemeriksaan | Hasil |
|---|---|
| `dotnet build SuDesApp.slnx -c Release` | **0 Warning, 0 Error** |
| `dotnet test SuDesApp.Tests` | **443/443 Passed** |
| `Tools/check-kontras-wcag.ps1` | **Lolos** — 11 tema × 60 kunci konsisten, semua pasangan kontras lolos |
| `[Obsolete]` dalam .cs/.xaml | 0 |
| TODO/FIXME nyata | 0 (hanya false-positive pola "XXXX" pada format kode) |
| Tipe (class/record/struct/interface/enum) tanpa rujukan | **0** dari 1.170 tipe |
| Method tanpa pemanggilan | **0** (7 kandidat heuristik semuanya false positive) |
| Field privat tanpa rujukan | **0** (dijaga `TreatWarningsAsErrors`) |
| Resource gambar/font tak terpakai | **0** (times.ttf/timesbd.ttf/timesi.ttf dimuat dinamis oleh `PdfStandardFontSource`) |
| Rujukan CI/workflow ke file yang tidak ada | **0** |

---

## 2. Kode mati / file tak terpakai yang DIHAPUS

### Dihapus pada audit ini

| Item | Lokasi | Alasan |
|---|---|---|
| 30 resource XAML mati | `SuDesApp.Wpf` (Themes/*.xaml, PaletTetap, ThemeStyles, App.xaml, 5 view) | 0 rujukan statis/dinamis; terverifikasi melalui pemindaian seluruh .xaml/.cs/tests/tools/dokumen |
| — token tema (×11 berkas) | `AccentDarkColor`, `LogoCircleBackgroundBrush`, `ShadowColor` | dihapus seragam dari 11 tema; paritas kunci tetap (uji `TemaKonsistenTests` lolos) |
| — palet mockup | `Mockup*Brush`/`Mockup*Color` (10 kunci), `TerminalAksenBrush` | blok "Mockup panduan" tak pernah dirujuk |
| — token layout | `PaddingForm/Halaman/Kartu`, `RadiusKecil/Kartu/Header/Dialog/Penuh`, `WindowCaptionStyle`, `IndukTombolAda` | hanya `RadiusKontrol` yang dipakai |
| — view | `DayCheck`, `Divider`, `InlineNote` (PengaturanAplikasi), `FiturList` (About), `KepingAngka` (Idm), `JenisSuratItemsView` (RegisterNtcr & RegisterSurat), `HyperlinkStyle` (App) | tak dirujuk |
| `ByteArrayToImageConverter.cs` | `SuDesApp.Wpf/Converters/` | converter + resource-nya mati |
| `StringToBoolConverter.cs` | `SuDesApp.Wpf/Converters/` | converter + resource-nya mati |
| Paket NuGet `System.Security.Cryptography.Xml` | `SuDesApp.Core.csproj` | 0 pemakaian (namespace tak pernah dirujuk; DPAPI memakai paket `ProtectedData` yang berbeda) |
| `xmlns:local` (Converters) | `App.xaml` | tak terpakai setelah converter mati dihapus |

Catatan: `TextSelectedBrush` sempat terindikasi mati tetapi **dipertahankan** karena dirujuk
tabel pasangan kontras `Tools/check-kontras-wcag.ps1`.

### Dihapus sesi sebelumnya (masih uncommitted, terverifikasi ulang)

- 3 skrip mati: `Tools/check-dok-links.sh`, `Tools/ekspor-catatan-rilis.sh`, `Tools/Buat-Installer-Advanced.ps1` (beserta entri `installer/*.aip` di `.gitignore` dan `SuDesApp.Server/libman.json`).
- Method mati di `SuDesApp.Core` (mis. `AppConfig.IsValidTemplate`, bagian `FileService`/`GoogleDriveService`/`FormatPermintaan` yang tak terpakai) — build + 443 uji tetap hijau setelah penghapusan.

### TIDAK dihapus (sengaja)

| Item | Alasan |
|---|---|
| Skrip `Tools/` yang tak dirujuk CI (Admin-Lisensi.ps1, Buat-Tambalan.ps1, sinkron-migrasi-supabase.py, Set-KonfigurasiSupabaseLokal.ps1, gen-tema-preset.py, embed-google-sans.py, download-logo-kabupaten-v2.py, verifikasi-asal-usul.py, generate-app-icon.ps1) | alat operasional/generator aset yang dijalankan manusia; penghapusannya tak bisa diverifikasi build/uji |
| `Tools/siapkan-pembanding.ps1` | **dipakai** `.github/workflows/release.yml:159` — klaim audit lama bahwa skrip ini tak terujung adalah keliru |
| `Resources/LogosKabupaten/` (517 berkas) | dipakai glob `SuDesApp.Wpf.csproj` |
| `Resources/Fonts/times*.ttf` | dimuat dinamis berdasarkan nama berkas oleh `SudesPdf.Core/PdfStandardFontSource` |
| `Tools/WilayahGen`, `Tools/BawaanGen` | generator data wilayah/template bawaan; BawaanGen dipakai `Tools/check-rilis.ps1` |
| `SuDesApp.Api` | backend untuk klien mobile/browser; dirujuk `SuDesApp.Tests` + `docs/backend-web.md` |

---

## 3. Yang perlu diperbaiki (fixes)

1. **Ketidakseragaman `TreatWarningsAsErrors`** — `SudesPdf.Core`, `SudesExPlus`, dan `SuDesApp.Api` belum menyetel `TreatWarningsAsErrors`, sementara Core/Wpf/Server/SudesPdf sudah. Samakan agar semua proyek mendapat perlindungan yang sama terhadap kode mati/unused.
2. **`global.json` memakai SDK prarilis** (`11.0.100-rc.3`, `allowPrerelease: true`). Begitu .NET 11 SDK GA tersedia, kunci ke versi stabil.
3. **Dokumentasi `SuDesApp.Api` vs `ApiListener` in-process** (temuan lama, belum tuntas): tulis eksplisit di `docs/backend-web.md` kapan memakai `SuDesApp.Api` (klien mobile/browser) vs `ApiListener` (desktop), dan apakah `SuDesApp.Api` perlu ikut pipeline publish rilis.
4. **Uji tema yang dirancang belum ada** — `docs/tema-dan-warna.md` (sudah dikoreksi akurasinya) menyebut rancangan uji render-per-halaman (`JelajahRenderTemaTests`, dll.) yang belum diimplementasikan. Opsional: implementasikan bila dokumen dijadikan spesifikasi.

---

## 4. Yang perlu ditingkatkan (upgrades)

### Patch aman (direkomendasikan segera)

| Paket | Saat ini | Tersedia |
|---|---|---|
| `Google.Apis.Drive.v3` | 1.78.0.4313 | **1.78.0.4327** |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | **18.11.1** |

### Menunggu keputusan migrasi .NET 11

Seluruh paket `Microsoft.Extensions.*` (10.0.14), `Microsoft.Data.Sqlite.Core` (10.0.14), dan
`Microsoft.AspNetCore.OpenApi` (10.0.14) punya versi **11.0.0**. Naikkan **bersamaan** dengan
perpindahan `TargetFramework` ke `net11.0`/`net11.0-windows` (SDK 11 RC sudah terpasang).
Jangan naikkan paket 11.x sementara TFM masih net10.0.

### Opsional

- **xunit 2.9.3 → xunit v3** (paket `xunit.v3`): performa pengujian lebih baik, dukungan modern; perlu penyesuaian `SuDesApp.Tests` (global using, runner).
- **Konsolidasi JSON**: `Newtonsoft.Json 13.0.4` masih dipakai 9 berkas; `System.Text.Json` (bawaan runtime) bisa menggantikan dan mengurangi satu dependensi pihak ketiga.
- **Windows TFM**: `SuDesApp.Core` memakai `net10.0-windows7.0` sementara Wpf `net10.0-windows` — pertimbangkan samakan baseline Windows-nya.

---

## 5. Verifikasi yang dilakukan (audit ini)

| Pemeriksaan | Metode |
|---|---|
| Build + uji | `dotnet build SuDesApp.slnx -c Release`, `dotnet test` → 443/443 |
| Konsistensi tema | `Tools/check-kontras-wcag.ps1` → lolos |
| Tipe mati | pemindaian 1.170 tipe terhadap seluruh .cs/.xaml/.ps1/.json |
| Method mati | pemindaian deklarasi vs pemanggilan (termasuk lookup resource dinamis `TryFindResource`/`FindResource` — semuanya literal yang aman) |
| Kunci XAML mati | 384 kunci dipindai ulang dengan filter ketat (tanpa komentar/nama class), 30 mati → dihapus |
| Validitas XAML | `xml.etree` parse ulang semua berkas yang diubah |
| Paket NuGet | `dotnet list package --outdated` + grep pemakaian simbol tiap paket |
| Rujukan CI | semua path `Tools/…` di `.github/workflows/**` diverifikasi ada |
| File terhapus lama | grep referensi `Buat-Installer-Advanced`/`check-dok-links`/`ekspor-catatan-rilis`/`libman` → bersih |

---

## 6. Ringkasan aksi

**Semua butir di bawah sudah dilaksanakan pada 11 November 2026** (lihat riwayat
commit): kode mati dihapus, disiplin build disamakan, paket patch diperbarui,
dokumentasi backend web dilengkapi, uji tema baru ditambahkan, migrasi .NET 11
(TFM + paket 11.0.0 + SDK GA 11.0.100) diselesaikan, dan serialisasi token
Google dipindahkan ke System.Text.Json dengan uji kompatibilitas dua arah.

| Prioritas | Tindakan | Status |
|---|---|---|
| P2 | Hapus 30 resource XAML mati + 2 converter mati + 1 paket NuGet mati | **Selesai** |
| P2 | Samakan `TreatWarningsAsErrors` di SudesPdf.Core/SudesExPlus/SuDesApp.Api | **Selesai** |
| P3 | Patch upgrade Google.Apis.Drive.v3 & Microsoft.NET.Test.Sdk | **Selesai** |
| P3 | Migrasi .NET 11 (TFM + paket 11.x + global.json SDK GA) | **Selesai** |
| P3 | Dokumentasi SuDesApp.Api vs ApiListener | **Selesai** |
| P3 | Newtonsoft.Json → System.Text.Json (kode aplikasi) | **Selesai** — paket tetap dirujuk untuk menangkap JsonException milik FileDataStore Google |
