# SuDesApp 2.14.0 — 11 November 2026

Rilis pemeliharaan besar: Beranda diperkuat, installer beralih penuh ke Inno Setup, Android menambah peringatan versi dan perbaikan kop, serta seluruh god class desktop dipecah tanpa mengubah perilaku.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Beranda

- **Pintasan tahan perubahan katalog**: `SusunPintasan` semula memakai `First()` atas enam jenis bawaan — bila satu jenis keluar dari `MenuSuratKatalog`, halaman pertama aplikasi melempar `InvalidOperationException` saat dibuka. Kini `FirstOrDefault` + filter: paling buruk satu pintasan hilang, halaman tetap terbuka.
- **Ikon satu sumber**: pemetaan jenis surat → ikon dipindah ke `SuDesApp.Wpf/Utilities/IkonJenisSurat.cs`, dipakai bersama oleh sidebar Buat Surat dan pintasan Beranda. Ikon kedua tempat tidak bisa lagi saling berbeda.

## Installer

- **Proyek `SuDesApp.Installer` (WPF) dihapus** dari solusi; pemasangan sepenuhnya lewat Inno Setup (`installer/SuDesApp.iss`). Skrip lama `Tools/Buat-Installer-Wpf.ps1` ikut dihapus.
- **Wizard berbahasa Indonesia** (`installer/Languages/Indonesian.isl`) dan aset gambar wizard (`installer/Assets/`) disertakan; dapat dibuat ulang konsisten lewat `Tools/buat-aset-wizard.ps1`.
- Versi installer tetap diberikan saat build: `ISCC.exe /DAppVersion=2.14.0 /DAppVersionNumeric=2.14.0.0 installer\SuDesApp.iss`.

## Android

- **Peringatan versi desktop**: versi desktop yang ikut dalam kop tersinkron disimpan (`PengaturanSinkron.versiDesktop`) dan dibandingkan saat halaman Pengaturan dibuka — bila versi desktop **berbeda** dengan versi aplikasi Android, dialog "Pembaruan tersedia" tampil dan mengajak memperbarui APK.
- **Kop surat**: baris wilayah menghormati jenis selain Kabupaten (mis. "Kota" tidak lagi dipotong jadi "Kab."), nama desa pada jabatan dibersihkan dari kata "Desa" ganda, placeholder bertitik di akhir (`{nama.`) terbaca benar, dan alamat surel disaring dengan `Kop.SurelBersih`.
- **Validasi form dipisah** ke modul `ValidasiForm` disertai uji unit junit (`ValidasiFormTest`, `SuratPdfTest`, `DaftarHadirPdfTest`, `PengaturanSuratTest`, `AlamatSusunTest`); `testImplementation junit` ditambahkan di Gradle.
- **Persiapan produksi**: `proguard-rules.pro` dan `network_security_config.xml` ditambahkan; `versionCode 21400` / `versionName 2.14.0` sejalan dengan desktop.

## Kode Internal — Tanpa Perubahan Perilaku

- **Pemecahan god class**: 25 kelas raksasa (`WargaViewModel`, `SetelanViewModel`, `RegisterSuratViewModel`, `FormulirViewModel`, `IdmViewModel`, `LanggananViewModel`, `ApbdesViewModel`, `TemplateSurat*`, `PerangkatDesaViewModel`, `InputKeputusanViewModel`, `ExImdbViewModel`, `PembaruanViewModel`, `AboutViewModel`, `PengaturanAplikasiViewModel`, `CatatanRilisViewModel`, `DatabaseInitializer`, `SuratRepository`, `WargaRepository`, `ImporWargaService`, `TemplateSuratWordImporService`, `NtcrGenerator`, `SupabaseStore`, `XlsxReader`, `LayoutEngine`) dipecah menjadi ±190 file partial per tanggung jawab.
- `SkPerangkatData` dipecah menjadi model fokus (`SkJenisPerangkat`, `SkKelompokTemplate`, `SkPerangkatIsi`, `SkPerangkatKatalog`); modul font/raster `SudesPdf` (PdfCff/PdfContent/PdfFonts/PdfRaster/PdfTrueType) dipecah menjadi kelas-kelas kecil bernama jelas.
- Verifikasi silang: total sisipan file baru (±36,5 ribu baris) menutupi baris yang keluar dari file induk (±32,7 ribu baris); tidak ada duplikasi member antar partial dan tidak ada stub tersisa.

## Verifikasi

- `dotnet build SuDesApp.slnx` — 0 galat, 0 peringatan.
- `dotnet test SuDesApp.Tests` — 443/443 lolos (termasuk `BerandaPintasanTests`).
- Uji unit Android (5 berkas junit, termasuk `PengaturanSuratTest` untuk `Kop.SurelBersih`) disertakan dalam sumber; dijalankan lewat `gradlew test` pada lingkungan ber-JDK.
