# AGENTS.md

Catatan kerja untuk sesi berikutnya. README sudah memuat perintah build/uji dasar;
berkas ini hanya mencatat hal yang tidak terbaca dari kode atau dokumentasi.

## Bahasa & commit
- Balas dalam bahasa Indonesia; istilah teknis boleh tetap bahasa Inggris seperti di kode.
- Pesan commit berbahasa Indonesia dengan awalan konvensional (`refactor:`, `perf:`, `fix:`, `docs:`, `ci:`) dan badan satu paragraf yang menjelaskan alasannya, bukan daftar perubahan.

## Build & uji
- Jangan menambahkan `-f net10.0` pada `dotnet test`: proyek uji hanya punya target `net10.0-windows`, hasilnya galat NETSDK1005 yang menyesatkan (seolah restore belum jalan).
- Perintah `dotnet test ... -v q` ala README menyembunyikan keluaran `ITestOutputHelper`; tambahkan `--logger "console;verbosity=detailed"` lalu saring dengan grep bila butuh angka dari tes.
- Analyzer SUDES0001 (dipasang lewat `Directory.Build.props`) menggagalkan build bila ada blok `catch` kosong tanpa komentar alasan di dalamnya.
- `JejakWebhookTests.Notifikasi_lunas_tercatat_sebagai_diterima` kadang gagal saat seluruh suite berjalan, tetapi lolos bila difilter sendiri — flaky karena berkas jejak sementara bersama, bukan kemunduran kode.

## Menjalankan aplikasi
- Executable hasil build bernama `SuDesApp.exe`, bukan `SuDesApp.Wpf.exe`, di `SuDesApp.Wpf/bin/<Config>/net10.0-windows/`.
- Di Git Bash lingkungan ini `powershell` tidak ada di PATH; panggil langsung `/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe`. Untuk smoke test alihkan log ke berkas dan cek proses dengan `ps -W | grep net10.0-windows/SuDesApp`.
- `desa.db` mengikuti folder kerja executable (koneksi relatif di `appsettings.json`), jadi build baru mengisinya dengan penanda `DesaContoh` ("Nama Desa", "Nama Kepala Desa") sampai Pengaturan Surat diisi. Membaca `desa.db` build debug adalah cara melihat data yang sedang dipakai.
- Akun login build debug: `admin`/`admin` (tersimpan di tabel `Pengguna` dengan PBKDF2; 3 kali salah akan terkunci — verifikasi hashnya dulu dengan `hashlib.pbkdf2_hmac('sha256', sandi, salt, 120000, 32)` sebelum mengetik di aplikasi).

## Uji GUI otomatis (UI Automation)
- Skrip uji halaman ada di `artifacts/uji-api-halaman/uji.ps1` (dengan `cek-parse.ps1` untuk cek sintaks tanpa eksekusi). Skrip menjalankan aplikasi, login, membuka halaman via UIA, mengekstrak teks, dan screenshot ke folder yang sama.
- **Windows PowerShell 5.1 membaca berkas tanpa BOM sebagai ANSI**: satu karakter non-ASCII (mis. em dash UTF-8) berubah menjadi tanda kutip liar dan merusak parsing. Berkas `.ps1` harus ASCII murni; periksa dengan `python -c "print(sum(1 for b in open(f,'rb').read() if b>127))"`.
- PowerShell 5.1 tidak menerima `New-Object Tipe(arg1, arg2)` multiline; pakai `[Tipe]::new(a, b)`.
- **Dialog `MessageDialogWindow` ber-Owner diekspos UIA sebagai descendant jendela pemiliknya**, bukan jendela tingkat atas root — cari dialog dari `$main.FindAll(Descendants, ControlTypeWindow)`, bukan dari root Children. Judulnya mengikuti jenis pesan ('Informasi', 'Peringatan'), bukan selalu 'Pesan'.
- Proxy UIA bisa basi di tengah alur (UI render ulang); sentraliskan pencarian elemen segar tiap percobaan dan sediakan fallback klik mouse (`SetCursorPos` + `mouse_event`) bila `InvokePattern` melempar.
- Pantau `error.log` di folder executable (ditulis `LogGalat.Tulis`): ukur ukurannya sebelum dan sesudah sesi uji untuk membuktikan "tanpa galat baru", bukan sekadar tidak ada dialog yang terlihat.
- Preferensi aplikasi ada di `%LOCALAPPDATA%\SuDesApp\login_prefs.json` (baris `kunci=nilai`). Baca untuk membuktikan keadaan tersimpan; kembalikan ke nilai awal setelah mengubahnya lewat UI — jangan tinggalkan keadaan uji di mesin pengguna.

## Aplikasi Android (MAUI)
- Isi submenu beranda ponsel harus menyalin sidebar WPF (`MainWindowViewModel.Navigasi`, `NtcrKatalog`, contoh template), dan sumber tunggalnya `SuDesApp.Mobile/Katalog/katalog-surat.json` — menambah menu desktop berarti menambah entri JSON itu, bukan mengubah Razor.
- Nomor surat ponsel dihitung per **awalan** (`HitungNomorAwalan`), bukan per kode: semua surat 470 berbagi satu deret, N1–N6 berbagi 474.3, sama seperti `JenisSuratConfig.json` desktop.
- "Paket N1–N6" ponsel memakai `PencetakSurat.BuatBerkasBanyak` (satu `container.Page` per blanko); jangan membuat PDF per blanko lalu menggabungkannya.
- Emulator 1080x2400 tetapi tangkapan `screencap` tampil 900x2000: koordinat `input tap` harus dari `uiautomator dump`, bukan dari gambar. APK Debug tanpa `-p:EmbedAssembliesIntoApk=true` gagal dibuka bila dipasang lewat `adb install` (mundur dengan "No assemblies found … Fast Deployment").

## Render PDF & pratinjau
- Biaya satu halaman didominasi gambar kop: pindaian ±1500x2050 px hanya tampil ±70 px, sehingga mengonversi seluruh gambar (dan /SMask-nya) ke RGBA per halaman adalah jebakan performa utama. Halaman laporan padat tabel tanpa gambar tetap ±50 ms karena biayanya di jalur teks.
- Ukur performa hanya dengan `-c Release`: build Debug membuat render tampak 4-5x lebih lambat, cukup untuk membalik kesimpulan tentang bagian mana yang mahal.
- Jangan meninggalkan `Stopwatch` di dalam perulangan per piksel; dua `GetTimestamp` per pembacaan piksel menambah belasan milidetik dan mengaburkan hasil.
- Untuk perubahan rasterizer, buktikan kesetaraan hasil dengan membandingkan sidik piksel halaman sampel (mis. SHA-256 `RasterImage.Pixels`) sebelum vs sesudah, bukan hanya "tes lulus". Caranya: salin berkas sumber, `git checkout --` berkas itu, jalankan pembanding, lalu pulihkan salinan.
- PDF sampel untuk pengukuran dan inspeksi visual ada di `%TEMP%/sudesapp-sampel`, ditulis `CetakContohSuratTests` dan sengaja tidak dihapus setelah uji.
- Di mesin tata letak SudesPdf, `AlignRight()/AlignCenter() + Width(...)` pada anak kolom TIDAK menggeser isi: kotaknya sempit tapi isinya ditata selebar induk, jadi blok tanda tangan `AlignCenter` tercetak di tengah halaman. Untuk blok di sisi kanan pakai `Row(row => { row.RelativeItem(); row.ConstantItem(lebar)… })`.
- Angka muat/render per halaman dicetak `SuDesApp.Tests/Perf/HarnessRenderPratinjauTests`; jalankan `-c Release` + `--logger "console;verbosity=detailed"` lalu cari baris `[harness]`. Harness memakai `PdfRenderer` (tanpa WPF), jadi aman di thread uji.

## WPF
- Jangan memakai `ConfigureAwait(false)` di perintah ViewModel yang menyentuh state terikat UI setelah await: setter seperti `SedangUji` memicu `RaiseCanExecuteChanged` → `ButtonBase.UpdateCanExecute` → `DependencyObject.GetValue` yang melempar "The calling thread cannot access this object" dari thread latar. Galat ditangkap `AsyncRelayCommand.LaporkanGagal` dan memunculkan dialog "Perintah tidak selesai" yang menyesatkan (terbukti di `error.log` dari tombol Uji Koneksi halaman API sebelum diperbaiki).
- `HttpListener` prefix `/sudes/api/v1/` tidak cocok dengan permintaan ke `/sudes/api/v1` tanpa segmen rute — endpoint uji koneksi harus mengarah ke `/status`, bukan alamat prefix.

## Git
- Branch default repo ini `master`, bukan `main`. Sesi biasanya berjalan langsung di checkout utama pada branch itu, jadi pekerjaan cukup di-commit di sana tanpa merge terpisah.
