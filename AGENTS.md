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
- Di Git Bash lingkungan ini `powershell` tidak ada di PATH; untuk smoke test alihkan log ke berkas dan cek proses dengan `ps -W | grep net10.0-windows/SuDesApp`.
- `desa.db` mengikuti folder kerja executable (koneksi relatif di `appsettings.json`), jadi build baru mengisinya dengan penanda `DesaContoh` ("Nama Desa", "Nama Kepala Desa") sampai Pengaturan Surat diisi. Membaca `desa.db` build debug adalah cara melihat data yang sedang dipakai.

## Render PDF & pratinjau
- Biaya satu halaman didominasi gambar kop: pindaian ±1500x2050 px hanya tampil ±70 px, sehingga mengonversi seluruh gambar (dan /SMask-nya) ke RGBA per halaman adalah jebakan performa utama. Halaman laporan padat tabel tanpa gambar tetap ±50 ms karena biayanya di jalur teks.
- Ukur performa hanya dengan `-c Release`: build Debug membuat render tampak 4-5x lebih lambat, cukup untuk membalik kesimpulan tentang bagian mana yang mahal.
- Jangan meninggalkan `Stopwatch` di dalam perulangan per piksel; dua `GetTimestamp` per pembacaan piksel menambah belasan milidetik dan mengaburkan hasil.
- Untuk perubahan rasterizer, buktikan kesetaraan hasil dengan membandingkan sidik piksel halaman sampel (mis. SHA-256 `RasterImage.Pixels`) sebelum vs sesudah, bukan hanya "tes lulus". Caranya: salin berkas sumber, `git checkout --` berkas itu, jalankan pembanding, lalu pulihkan salinan.
- PDF sampel untuk pengukuran dan inspeksi visual ada di `%TEMP%/sudesapp-sampel`, ditulis `CetakContohSuratTests` dan sengaja tidak dihapus setelah uji.
- Di mesin tata letak SudesPdf, `AlignRight()/AlignCenter() + Width(...)` pada anak kolom TIDAK menggeser isi: kotaknya sempit tapi isinya ditata selebar induk, jadi blok tanda tangan `AlignCenter` tercetak di tengah halaman. Untuk blok di sisi kanan pakai `Row(row => { row.RelativeItem(); row.ConstantItem(lebar)… })`.
- Angka muat/render per halaman dicetak `SuDesApp.Tests/Perf/HarnessRenderPratinjauTests`; jalankan `-c Release` + `--logger "console;verbosity=detailed"` lalu cari baris `[harness]`. Harness memakai `PdfRenderer` (tanpa WPF), jadi aman di thread uji.

## Git
- Branch default repo ini `master`, bukan `main`. Sesi biasanya berjalan langsung di checkout utama pada branch itu, jadi pekerjaan cukup di-commit di sana tanpa merge terpisah.
