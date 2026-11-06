# SuDesApp 2.10.5 — 6 November 2026

Pembaruan ini memperbaiki masalah keluar aplikasi yang membeku, menguatkan pembersihan sumber daya (CTS), dan mencegah server lisensi lokal mati karena izin berkas. Alur surat sampai PDF kini lebih tahan — jumlah halaman dihitung oleh mesin PDF, nomor surat yang tertabrak dipulihkan, dan tampilan diseragamkan ke skala ukuran huruf baku.

Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Perbaikan

- **Penutupan aplikasi tidak lagi membeku.** Backup harian ke Google Drive dan pemasangan pembaruan yang dijadwalkan kini berjalan di `App.OnExit` setelah jendela utama ditutup, bukan di `MainWindow.Closing` yang bersifat sinkron. Dialog konfirmasi keluar muncul seperti biasa, lalu aplikasi tutup; backup dijalankan di sana dengan batas 20 detik, dan pemasangan diproses oleh proses terpisah yang tetap hidup setelah aplikasi keluar.
- **Server lisensi lokal dimatikan tanpa menunggu.** `Matikan()` pada `ServerLisensiLokal` memanggil `Kill(entireProcessTree: true)` tanpa `WaitForExit`. Sebelumnya aplikasi menunggu proses `dotnet run` benar-benar berakhir sebelum keluar, yang membuat aplikasi freeze saat diklik "Keluar". Sekarang proses dibunuh secara asinkron dan aplikasi langsung tutup.
- **Batasan CTS dibersihkan dengan aman.** `Dispose()` pada `DetakSambunganServer` dan `PembaruanLatarService` membungkus `Cancel()` dan `Dispose()` pada `CancellationTokenSource` dengan try/catch. Loop denyut sambungan menangkap `ObjectDisposedException` agar tidak melempar saat CTS dibuang dari thread lain.
- **Server tetap jalan bila appsettings.lokal.json tidak terbaca.** `SuDesApp.Server/Program.cs` menangkap `UnauthorizedAccessException` saat memuat berkas lokal, bukan membatalkan start server. Pada sistem dengan izin berkas yang ketat, server tetap berjalan tanpa kredensial lokal.
- **Default server lisensi lokal diaktifkan.** `IsServerLisensiLokalAktif()` sekarang default `true` (sebelumnya `false`) sehingga server lisensi lokal menyala bersama aplikasi secara bawaan.
- **Fitur "pasang pembaruan kecil otomatis saat aplikasi ditutup" dihapus dari halaman Pengaturan.** Pilihan ini sudah tidak digunakan karena pemasangan pembaruan diatur melalui notifikasi ("Instal Sekarang" atau "Instal Saat Keluar").
- **Jumlah halaman pada pratinjau dan cetak dihitung oleh mesin PDF.** Sebelumnya jumlah halaman ditebak dengan pola teks di atas berkas PDF, dan helper itu diduplikasi di dua tempat. Sekarang `GeneratePdf` menerima jumlah halaman langsung dari mesin tata letak, jadi hasilnya tidak bergeser bila format penulis berubah.
- **Baris jabatan pada blok tanda tangan disatukan.** Aturan "A/N Kepala Desa …" / "Kepala Desa …" kini ditulis sekali dan dipakai bersama oleh PDF surat dan pratinjau kop di Pengaturan Surat, sehingga keduanya tidak mungkin berbeda.
- **Nomor surat yang tertabrak operator lain tidak lagi membuat simpan gagal keras.** Aplikasi membangkitkan nomor segar lalu menyimpan, dan kejadiannya tercatat di log; galat basis data saat menghitung nomor tidak lagi ditutupi dengan mengembalikan nomor 1.
- **Form surat menunggu pemuatan awalnya selesai sebelum menyimpan.** Nomor otomatis pasti terisi, dan kegagalan pemuatan muncul di log alih-alih hilang diam-diam.
- **Berkas pratinjau PDF ditulis lewat penulis berkas yang sama dengan surat.** Berkas sementara lalu diganti namanya, dengan cadangan bila berkas tujuan sedang terkunci — sebelumnya pratinjau menulis langsung dan bisa gagal tanpa penjelasan.
- **Baris hubungan pelapor pada surat keterangan kematian tidak lagi tercetak kosong** bila hubungannya tidak diisi.

## Tampilan

- **Ukuran huruf diseragamkan ke skala baku.** 35 angka di luar skala (12, 9, 16, 19, 22) pada 10 berkas tampilan diganti nilai baku terdekat (10/11/13/14/15/20/24/28), jadi hierarki teks antar halaman seimbang. Emoji dan piagam berukuran lebih besar sengaja dibiarkan.
- **Warna literal diganti kunci tema.** `Foreground="White"` pada ikon di halaman Keputusan diganti `{DynamicResource OnPrimaryTextBrush}`, jadi ikon ikut berganti saat tema diganti — sebelumnya putih yang membeku di sebelas tema.

## Verifikasi

- `dotnet build SuDesApp.slnx` — berhasil, 0 peringatan, 0 galat.
- Seluruh uji database lulus, termasuk pemulihan dari cadangan terenkripsi ke database biasa, pembukaan database baru tanpa kunci, dan tiga uji pemulihan otomatis.
- Seluruh 392 uji lulus (naik dari 388), termasuk uji isolasi peran sesi sistem, uji berkas database terkunci, uji asap seluruh view, dan dua penjaga baru: kesamaan kunci serta larangan warna literal/StaticResource pada tema, dan ketaatan ukuran huruf pada skala baku.
- Installer diuji pada instalasi bersih dan upgrade: `desa.db` dan template milik desa tetap utuh setelah uninstaller versi lama dijalankan.

## Kerja rakit

- GitHub Actions memakai SDK `11.0.100-rc.3`, sama dengan `global.json` — sebelumnya `10.0.401` sehingga bangsal CI gagal sejak SDK dinaikkan.
- `Dockerfile` memakai image `sdk:11.0` (sebelumnya `sdk:10.0`) karena aplikasi bertarget `net10.0`.
- Repo dibersihkan dari berkas dan folder yang tidak dipakai (lingkungan conda lokal, `.freebuff/`, `.kilo/`, berkas rancangan desain, `.vs/`, `artifacts/` proyek), dan `.gitignore` menutup lingkungan conda lokal.
- `docs/tema-dan-warna.md` dicatat ulang mana penjaga yang benar-benar ada dan mana yang masih rancangan.

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
