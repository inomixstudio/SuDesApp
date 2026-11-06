# AGENTS.md

SuDesApp — Aplikasi pengelolaan surat desa (WPF, .NET 10).

## Quick Reference

- **Package Manager:** `dotnet` (no custom commands)
- **Build:** `dotnet build`
- **Test:** `dotnet test --logger "console;verbosity=detailed"`
- **Default Branch:** `master`

## Universal Rules

- Balas dalam bahasa Indonesia; istilah teknis boleh tetap bahasa Inggris seperti di kode.
- Pesan commit berbahasa Indonesia dengan awalan konvensional (`refactor:`, `perf:`, `fix:`, `docs:`, `ci:`) dan badan satu paragraf yang menjelaskan alasannya, bukan daftar perubahan.

## Non-obvious Learnings

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