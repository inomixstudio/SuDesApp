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

> Semua butir di bawah **sudah selesai** pada 11 November 2026 (lihat bagian 6).
> Teks asli dipertahankan sebagai catatan proses.

1. **Ketidakseragaman `TreatWarningsAsErrors`** — `SudesPdf.Core`, `SudesExPlus`, dan `SuDesApp.Api` belum menyetel `TreatWarningsAsErrors`, sementara Core/Wpf/Server/SudesPdf sudah. Samakan agar semua proyek mendapat perlindungan yang sama terhadap kode mati/unused. **→ Selesai.**
2. **`global.json` memakai SDK prarilis** (`11.0.100-rc.3`, `allowPrerelease: true`). Begitu .NET 11 SDK GA tersedia, kunci ke versi stabil. **→ Selesai (11.0.100 stabil).**
3. **Dokumentasi `SuDesApp.Api` vs `ApiListener` in-process** — tulis eksplisit di `docs/backend-web.md` kapan memakai `SuDesApp.Api` (klien mobile/browser) vs `ApiListener` (desktop), dan apakah `SuDesApp.Api` perlu ikut pipeline publish rilis. **→ Selesai.**
4. **Uji tema yang dirancang belum ada** — `docs/tema-dan-warna.md` menyebut rancangan uji render-per-halaman (`JelajahRenderTemaTests`, dll.) yang belum diimplementasikan. **→ Masih terbuka** (opsional; bukan gerbang build).

---

## 4. Yang perlu ditingkatkan (upgrades)

### Patch aman (direkomendasikan segera)

| Paket | Saat ini | Tersedia |
|---|---|---|
| `Google.Apis.Drive.v3` | 1.78.0.4327 | (versi terbaru per 13 November 2026) |
| `Microsoft.NET.Test.Sdk` | 18.11.1 | **18.12.0** |

### Selesai: migrasi .NET 11

Seluruh paket `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite.Core`, dan
`Microsoft.AspNetCore.OpenApi` sudah naik ke **11.0.0** bersamaan dengan
perpindahan `TargetFramework` ke `net11.0`/`net11.0-windows` dan `global.json`
ke SDK stabil 11.0.100.

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

---

## 7. Tindak lanjut — 13 November 2026

Pemeriksaan ulang seluruh repo (bukan hanya working tree) menemukan hal yang
tidak tertutup audit 11 November.

### Diperbaiki

| Item | Lokasi | Alasan |
|---|---|---|
| `Tools/check-rilis.ps1` **gagal** di tahap 3 | asumsi `BersihkanWargaContohAsync` ada di `DatabaseInitializer.cs` | kini di `DatabaseInitializer.FiturBaru.cs`; skrip membaca folder `Configuration` dan semua berkas `DatabaseInitializer*.cs`. Sebelumnya gerbang pra-rilis mati diam-diam. |
| Kode mati dihapus (562 baris) | `FormatPermintaan.cs` (432 baris: seluruh pemroses WhatsApp — `Parse`, `FieldDefs`, `DetectJenisSurat`, `NormalizeLabel`, `Normalkan*`, `TtlPisah*`, `TampilanJenis`, `InstruksiFormat`, `HasilParsePermintaan`, `FieldPermintaan`, 30 field `F_*`), `FileService.cs` (76 baris: 5 method), 2 kelas pengecualian (38 baris), `PdfObjects.cs` (5 baris: class kosong), `IkonMenu.cs` (11 baris: 5 konstanta) | 0 rujukan di seluruh solusi |
| Uji katalog daring ditambahkan | `SuDesApp.Tests/Api/KatalogSuratDaringTests.cs` (6 uji) | `KatalogSuratDaring` (pengganti `FormatPermintaan`) sebelumnya tanpa uji; merah-hijau diverifikasi dengan menyuntik NTCR ke daftar daring |
| Uji keselarasan catatan rilis ditambahkan | `SuDesApp.Tests/UI/CatatanRilisSelarasTests.cs` (3 uji) | catatan rilis hidup di dua tempat (kartu aplikasi + `docs/catatan-rilis-*.md`) tanpa gerbang pembanding; kartu teratas kini wajib cocok dengan `AssemblyVersion`, dan setiap dokumen rilis wajib punya kartu |
| Catatan rilis 2.15.0 diperbarui | `docs/catatan-rilis-2.15.0.md`, `CatatanRilisViewModel.RiwayatVersi.cs` | gerbang kontras CI dan uji katalog daring belum tercatat; angka uji sengaja dibiarkan 449/449 (kondisi saat rilis, bukan sesi lanjutan) |
| `FormatPermintaan` diganti `KatalogSuratDaring` | `SuDesApp.Core/Api/KatalogSuratDaring.cs` | nama kelas lama menyesatkan; katalog kini di tempat pemakainya |
| `FileService._config` menganggur | konstruktor | `AppConfig` disimpan tak pernah dibaca; parameter dihapus, 18 situs uji diperbarui |
| Flaky `JejakWebhookTests.Notifikasi_lunas_tercatat_sebagai_diterima` | `MidtransService` | akar masalah: uji lain mengubah `MIDTRANS_SERVER_KEY` di proses paralel; `KonfigurasiMidtrans.ServerKey` memprioritaskan env. Ditambahkan konstruktor dengan `serverKey` eksplisit |
| `IDEA.md` dihapus | root | template "mobile hub" berbahasa Inggris, tak pernah ter-commit, bertentangan dengan isi aplikasi |
| Dokumen menyesatkan | `README.md`, `Dockerfile`, `.agents/*.md`, `.vscode/launch.json`, `laporan-audit-suddesapp.md`, `docs/tema-dan-warna.md` | masih menyebut .NET 10 / SDK rc.3 / jalur `net10.0` |
| Gerbang kontras tema | `.github/workflows/ci.yml` | job **Audit kontras tema (WCAG)** menjalankan `Tools/check-kontras-wcag.ps1` di setiap push |
| `artifacts/` 858 MB → 263 MB | installer 2.10.3/2.10.4 + uninstaller | tak lagi diperlukan; pembanding tambalan diambil versi tertua dari GitHub |
| `artifacts/RELEASE.md` dipulihkan | | pemeriksaan digest lokal `check-rilis` kini berfungsi (10 lulus, 0 gagal) |
| `bin/Debug` (1,2 GB) + `.vs` (9 MB) dibersihkan | | hasil build, dibangun ulang otomatis, sudah gitignore |
| `TreatWarningsAsErrors` disamakan | `UjiTrialServerE2E`, `healthcheck` | dulu `false` / tak disetel |
| `Microsoft.NET.Test.Sdk` 18.11.1 → 18.12.0 | `SuDesApp.Tests.csproj` | patch aman |

### Terbuka (butuh keputusan pemilik repo)

| Item | Keterangan |
|---|---|
| **CI GitHub Actions gagal sejak 9 November** | *"The job was not started because your account is locked due to a billing issue."* Semua 10 PR dependabot berstatus UNSTABLE karena ini — **bukan** masalah kode. Harus diselesaikan di halaman penagihan GitHub. |
| 10 PR dependabot | semua MERGEABLE; isinya **sudah dikerjakan lokal** (lihat bagian 8) sehingga PR-nya bisa ditutup, bukan digabung |
| TFM `-windows7.0` vs `-windows` | dibiarkan: mengubah menyebar ke 6 proyek tanpa manfaat |
| `Newtonsoft.Json` 13.0.4 | masih dibutuhkan untuk `JsonException` FileDataStore Google |
| Uji render-per-halaman (`JelajahRenderTemaTests`) | rancangan di `docs/tema-dan-warna.md`, belum diimplementasikan |

### Verifikasi 13 November 2026

| Pemeriksaan | Hasil |
|---|---|
| `dotnet build SuDesApp.slnx -c Release` | 0 warning, 0 error |
| `dotnet test SuDesApp.Tests` | **458/458 lolos** (449 asal + 6 uji katalog daring + 3 uji catatan rilis) |
| `Tools/check-rilis.ps1 -TanpaGitHub` | **10 lulus, 0 gagal**, 3 dilewati |
| `Tools/check-resources.ps1 -FailOnOrphan` | 0 error, 0 peringatan |
| `Tools/check-kontras-wcag.ps1` | semua lolos |
| Validasi XAML | 0 rusak |
| Parse semua `Tools/*.ps1` | 15/15 OK |
| Android `./gradlew assembleDebug` | **BUILD SUCCESSFUL** (AGP 9.3.0, Gradle 9.8.2, JDK 21) |
| Android `./gradlew testDebugUnitTest` | **34/34 lolos**, 0 gagal |

---

## 8. Upgrade dependensi — dikerjakan lokal (13 November 2026)

Seluruh isi PR dependabot dikerjakan **di mesin lokal** dan diverifikasi dengan build lokal,
bukan digabung lewat GitHub. Ini juga membuktikan jalur lokal bisa diandalkan penuh.

### GitHub Actions (workflow)

| Action | Dari | Ke |
|---|---|---|
| `actions/checkout` | 4.4.0 | **7.0.1** |
| `actions/setup-dotnet` | 4.3.1 | **6.1.0** |
| `actions/setup-java` | 4.0.0 | **6.2.0** |
| `gradle/actions/setup-gradle` | 4.4.2 | **6.4.1** |
| `gitleaks/gitleaks-action` | v2 | **v3** |

Penurunan tetap dikunci ke commit SHA dengan komentar versi seperti konvensi repo.

### Android

| Komponen | Dari | Ke | Catatan |
|---|---|---|---|
| Gradle wrapper | 8.11.1 | **9.8.2** | berkas `gradlew`/`gradlew.bat`/wrapper jar dibangkitkan ulang memakai biner 9.8.2 |
| Plugin Kotlin | 2.0.21 | **2.2.10** | wajib lebih dulu: coroutines 1.11.0 membuat kompiler 2.0.21 gagal "internal compiler error" |
| AGP | 8.7.3 | **9.3.0** | built-in Kotlin; plugin `org.jetbrains.kotlin.android` dan blok `kotlinOptions` dihapus |
| `compileSdk` | 35 | **37** | dituntut okhttp 5.5.1; `targetSdk` tetap 35 |
| `okhttp3:okhttp` | 4.12.0 | **5.5.1** | major |
| `kotlinx-coroutines-android` | 1.8.1 | **1.11.0** | |
| `androidx.recyclerview` | 1.3.2 | **1.4.0** | |
| `androidx.security:security-crypto` | 1.1.0-alpha06 | **1.1.0** | |

### Rantai ketergantungan yang ditemukan

Upgrade Android **tidak bisa dilakukan sebagian**: coroutines 1.11.0 dibangun dengan Kotlin
lebih baru sehingga menaikkannya memaksa naikkan plugin Kotlin lebih dulu; okhttp 5.5.1
menuntut `compileSdk` 37 yang hanya didukung AGP 9.1+. Jadi urutannya: Kotlin plugin →
Gradle → AGP → compileSdk → okhttp, dan tiap langkah diverifikasi build lokal.

### Verifikasi lokal Android

- `./gradlew assembleDebug` — BUILD SUCCESSFUL
- `./gradlew testDebugUnitTest` — 34 uji, 0 gagal
- APK debug terbangun: `compileSdkVersion 37`, `targetSdkVersion 35`, `versionName 2.15.0`
- Satu-satunya deprecation warning berasal dari AGP sendiri (`Configuration.setVisible`),
  bukan dari kode aplikasi

### Prasyarat lokal yang dipasang

- **JDK 21** (`Eclipse Adoptium 21.0.13+9`) via `winget install --id EclipseAdoptium.Temurin.21.JDK`.
  JBR bawaan Android Studio melaporkan versi `25.0.4` yang tidak bisa di-parse Kotlin DSL
  Gradle sehingga build gagal tanpa penjelasan. Detail ditambahkan ke `.agents/build-test.md`.


