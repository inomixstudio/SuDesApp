# AGENTS.md

SuDesApp — Aplikasi pengelolaan surat desa (WPF, .NET 11).

## Quick Reference

- **Package Manager:** `dotnet` (no custom commands)
- **Build:** `dotnet build`
- **Test:** `dotnet test --logger "console;verbosity=detailed"`
- **Default Branch:** `master`

## Universal Rules

- Balas dalam bahasa Indonesia; istilah teknis boleh tetap bahasa Inggris seperti di kode.
- Pesan commit berbahasa Indonesia dengan awalan konvensional (`refactor:`, `perf:`, `fix:`, `docs:`, `ci:`) dan badan satu paragraf yang menjelaskan alasannya, bukan daftar perubahan.

## Non-obvious Learnings

- **ZONA PARITAS Buat Surat Android ↔ desktop.** Android membangun formulir dan PDF dari `SuDesApp.Core/Configuration/SkemaFormulir.json` yang salinannya ikut di APK (`android/app/src/main/assets/skema-formulir.json`) dan diterbitkan desktop ke Supabase. Mengedit salah satu tanpa yang lain = hasil cetak beda diam-diam. Penjaga: `dotnet test --filter "FullyQualifiedName~ParitasBuatSurat"` (menolak aset beda dari skema, `type`/`kustumId`/`$kunci` badan yang tidak pernah diisi Android, dan target yang tidak terpetakan ke `SuratData`). Aturan penamaan yang berlaku: kunci `$kunci` di blok `badan` HARUS sama dengan `target` skema atau dengan `nilai["..."]` yang ditulis `BuatSuratActivity` — ejaan karangan (mis. `$kematian.pukul`, `$surat.dataSource1`, `$ntcr.AyahCalonSuami` vs `$ntcr.ayahCalonSuami`) tercetak KOSONG karena peta kunci Android bersifat case-sensitive. Format tanggal dalam PDF mengikuti desktop: `dd MMMM yyyy` locale id-ID, umur `"<n> Tahun"`, dan properti `string` tanggal di database desktop selalu ISO `yyyy-MM-dd` (`SkemaFormulir` menormalkannya saat impor). Halaman isian Android JUGA mengikuti desktop: dipecah per kartu dengan judul section XAML, disimpan sebagai `grupUrutan` + `grup` per bidang dan diturunkan oleh `Tools/turunkan-grup.py --tulis` (jalankan tiap menambah/memindah section di `SuDesApp.Wpf/Views/*InputView.xaml`); binding di dalam `<DataGrid>` dihitung terhadap objek baris sehingga tidak boleh mengunci kolom tingkat atas. Kalau form Android kembali jadi satu daftar panjang, `grup`/`grupUrutan` hilang atau `grupkan()` tidak lagi dipakai. `--tulis` juga menerbitkan `Configuration/BidangFormDesktop.json` (kolom yang benar-benar ber-binding di XAML) dan `--audit` daftar kolom asing; uji `SkemaTidakMenambahKolomDiLuarFormDesktop` menolak kolom yang tidak ada di form desktop dan tidak tercetak di PDF (Status Perkawinan Garapan Sawah lolos tanpa penjaga ini). Kolom identitas yang desktop baca dari kartu warga tetapi Android ketik harus masuk daftar `Dikecualikan` dengan alasan. Pola tanggal lahir JUGA tidak seragam: `GarapanGenerator` + `IzinOrtuGenerator` mencetak `dd-MM-yyyy`, sisanya `dd MMMM yyyy` — Android menormalkannya lewat `TanggalIndonesia.polaTanggalLahir(idJenis)`, dijaga `PolaTanggalLahirAndroidSamaDenganGeneratorDesktop`.

- **ZONA TERKUNCI — hubungan server lisensi.** Alur tombol Hubungkan dan alamat server lisensi berkali-kali rusak oleh pengeditan berikutnya (gejala: "lisensi server tidak terhubung"). Sejak perbaikan ini, berkas berikut DON'T EDIT tanpa menjalankan `dotnet test --filter "FullyQualifiedName~Licensing"` dan menulis alasan di commit: `SuDesApp.Core/Services/PenentuAlamatLisensi.cs`, `SuDesApp.Core/Services/ServerLisensiLokal.cs`, `SuDesApp.Wpf/ViewModels/LanggananViewModel.Tindakan.cs` (blok `HubungkanAsync`), `SuDesApp.Wpf/App.Startup.cs` (`MulaiServerLisensiLokal`), `appsettings.json` (`Lisensi:UrlServer`). Perilaku dijaga kontrak oleh `SuDesApp.Tests/Licensing/KontrakHubunganServerLisensiTests.cs` — bila tes itu gagal, edit Anda yang salah, bukan tesnya. Invarian kunci: server lokal dan `Lisensi:UrlServer` HARUS `http://127.0.0.1:5110` (satu bentuk host); konfigurasi loopback tidak boleh mengunci alamat; hanya HTTPS non-loopback yang mengunci. Berkas terkunci ditandai read-only + `git update-index --assume-unchanged`; bila suatu perbaikan memang WAJIB, buka kuncinya dengan `attrib -R <berkas>` dan `git update-index --no-assume-unchanged <berkas>` lebih dulu, jangan sekadar menimpa galat izin. Bila banner "TERKUNCI" sudah ada di berkas, jangan menambah salinannya lagi.

- **Paket mode lisensi lokal wajib membawa server lisensinya.** `ServerLisensiLokal.CariTarget` hanya mencari `<folder aplikasi>\server\SuDesApp.Server.exe` (lalu `.dll`, lalu `.csproj`). `Tools/publish-release.ps1` menerbitkan `SuDesApp.Server` self-contained ke `artifacts/publish/server` hanya bila `-IzinkanLisensiLokal`, beserta `server\keys\lisensi-signing.key` yang harus sama dengan `Lisensi:KunciPublik` — `LisensiManager` tidak pernah menimpa kunci tertanam dengan kunci dari `/kunci-publik`, jadi kunci berbeda membuat semua token ditolak. Tanpa folder `server` di paket, aplikasi yang baru dipasang menampilkan "lisensi server tidak terhubung" tanpa sebab yang terlihat. Penjaga: `SuDesApp.Tests/Configuration/PaketServerLisensiTests.cs`.
- Server lisensi lokal (`SuDesApp.Server`) dijalankan via `dotnet run` dari `SuDesApp.Server` — butuh .NET SDK terpasang dan port 5110 bebas; kalau `appsettings.lokal.json` tak terbaca (izin), server tetap harus jalan (guard `UnauthorizedAccessException`).
- `Matikan()` pada `ServerLisensiLokal` harus `Kill(entireProcessTree)` tanpa `WaitForExit` — menunggu proses `dotnet run` berakhir membuat aplikasi freeze saat keluar.
- `DetakSambunganServer.Berhenti()` membatalkan dan membuang CTS; loop harus menangkap `ObjectDisposedException` karena `IsCancellationRequested` dibaca setelah dispose.
- `PembaruanLatarService.Dispose()` dan `PembaruanLatarService` CTS harus cancel+dispose dengan try/catch; `Dispose` ganda memicu `ObjectDisposedException`.
- IDM mengambil data dari `idm.kemendesa.go.id`; sertifikat kadaluarsa → fallback HTTP (`LewatHttp=true`). Offline → halaman menampilkan kartu galat/cadangan, bukan data.
- `AppPreferenceStore.GetBool(key, default)` hanya pakai default bila kunci belum tersimpan; preferensi lama (false) menimpa default baru (true).
- Exit: `MainWindow_Closing` tidak boleh `e.Cancel` untuk backup/pemasangan async — pengguna langsung keluar pada klik "Ya" pertama. Backup harian dan pemasangan terjadwal dijalankan di `App.OnExit` (backup dibatasi 20 detik; pemasangan oleh proses eksternal).

For specific guidelines, see:
- [Build & Uji](.agents/build-test.md)
- [Menjalankan Aplikasi](.agents/running-app.md)
- [Uji GUI Otomatis](.agents/gui-testing.md)
- [Render PDF & Pratinjau](.agents/pdf-render.md)
- [WPF](.agents/wpf.md)
- [Git](.agents/git.md)