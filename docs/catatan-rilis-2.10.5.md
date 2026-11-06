# SuDesApp 2.10.5 — 6 November 2026

Pembaruan ini memperbaiki masalah keluar aplikasi yang membeku, menguatkan pembersihan sumber daya (CTS), dan mencegah server lisensi lokal mati karena izin berkas.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Perbaikan

- **Penutupan aplikasi tidak lagi membeku.** Backup harian ke Google Drive dan pemasangan pembaruan yang dijadwalkan kini berjalan di `App.OnExit` setelah jendela utama ditutup, bukan di `MainWindow.Closing` yang bersifat sinkron. Dialog konfirmasi keluar muncul seperti biasa, lalu aplikasi tutup; backup dijalankan di sana dengan batas 20 detik, dan pemasangan diproses oleh proses terpisah yang tetap hidup setelah aplikasi keluar.
- **Server lisensi lokal dimatikan tanpa menunggu.** `Matikan()` pada `ServerLisensiLokal` memanggil `Kill(entireProcessTree: true)` tanpa `WaitForExit`. Sebelumnya aplikasi menunggu proses `dotnet run` benar-benar berakhir sebelum keluar, yang membuat aplikasi freeze saat diklik "Keluar". Sekarang proses dibunuh secara asinkron dan aplikasi langsung tutup.
- **Batasan CTS dibersihkan dengan aman.** `Dispose()` pada `DetakSambunganServer` dan `PembaruanLatarService` membungkus `Cancel()` dan `Dispose()` pada `CancellationTokenSource` dengan try/catch. Loop denyut sambungan menangkap `ObjectDisposedException` agar tidak melempar saat CTS dibuang dari thread lain.
- **Server tetap jalan bila appsettings.lokal.json tidak terbaca.** `SuDesApp.Server/Program.cs` menangkap `UnauthorizedAccessException` saat memuat berkas lokal, bukan membatalkan start server. Pada sistem dengan izin berkas yang ketat, server tetap berjalan tanpa kredensial lokal.
- **Default server lisensi lokal diaktifkan.** `IsServerLisensiLokalAktif()` sekarang default `true` (sebelumnya `false`) sehingga server lisensi lokal menyala bersama aplikasi secara bawaan.
- **Fitur "pasang pembaruan kecil otomatis saat aplikasi ditutup" dihapus dari halaman Pengaturan.** Pilihan ini sudah tidak digunakan karena pemasangan pembaruan diatur melalui notifikasi ("Instal Sekarang" atau "Instal Saat Keluar").

## Verifikasi

- `dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj -c Debug` — berhasil, 0 peringatan, 0 galat.
- Seluruh uji database lulus, termasuk pemulihan dari cadangan terenkripsi ke database biasa, pembukaan database baru tanpa kunci, dan tiga uji pemulihan otomatis.
- Seluruh 388 uji lulus, termasuk uji isolasi peran sesi sistem, uji berkas database terkunci, dan uji asap seluruh view.
- Installer diuji pada instalasi bersih dan upgrade: `desa.db` dan template milik desa tetap utuh setelah uninstaller versi lama dijalankan.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.10.4 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.10.4 | installer penuh 2.10.5 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain diarahkan ke installer penuh.

## Aset rilis

Aset di bawah dibangun dari kode 2.10.5 pada tag `v2.10.5`.

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.5_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.5_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.5_win-x64.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.5_win-x64.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.5.zip` | Pembaruan kecil untuk aplikasi 2.10.4 yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru, dan `Templates` tidak dihapus saat pencopotan. Tanpa instalasi, ekstrak ZIP lalu jalankan `SuDesApp.exe`.

## Verifikasi unduhan

Setelah aset dibangun, setiap installer dan zip portable disertai berkas `.sha256` senilai isinya. Periksa di Windows dengan `Get-FileHash .\SuDesApp_2.10.5_Setup.exe -Algorithm SHA256`, atau di Linux/macOS dengan `sha256sum -c SuDesApp_2.10.5_Setup.exe.sha256`. Aplikasi juga memverifikasi SHA-256 paket sebelum memasangnya.

## Persyaratan sistem

- Windows 10 versi 1607 atau lebih baru, atau Windows 11, arsitektur x64.
- Tidak perlu memasang .NET: kedua paket membawa runtime .NET 10 sendiri.
- Ruang disk sekitar 410 MB untuk berkas program, ditambah ruang untuk data desa.
- Pemasangan memakai `%LocalAppData%\Programs\SuDesApp`, jadi tidak menuntut hak administrator.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
- Panduan pembaruan dan tambalan: `docs/PEMBARUAN-TAMBALAN.md`
- Tanda tangan dan verifikasi rilis: `docs/TANDA-TANGAN-RILIS.md`
