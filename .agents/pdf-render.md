# Render PDF & Pratinjau Guidelines

## Performa
- Biaya satu halaman didominasi gambar kop: pindaian ±1500x2050 px hanya tampil ±70 px, sehingga mengonversi seluruh gambar (dan /SMask-nya) ke RGBA per halaman adalah jebakan performa utama. Halaman laporan padat tabel tanpa gambar tetap ±50 ms karena biayanya di jalur teks.
- Ukur performa hanya dengan `-c Release`: build Debug membuat render tampak 4-5x lebih lambat, cukup untuk membalik kesimpulan tentang bagian mana yang mahal.
- Jangan meninggalkan `Stopwatch` di dalam perulangan per piksel; dua `GetTimestamp` per pembacaan piksel menambah belasan milidetik dan mengaburkan hasil.

## Verifikasi Rasterizer
- Untuk perubahan rasterizer, buktikan kesetaraan hasil dengan membandingkan sidik piksel halaman sampel (mis. SHA-256 `RasterImage.Pixels`) sebelum vs sesudah, bukan hanya "tes lulus". Caranya: salin berkas sumber, `git checkout --` berkas itu, jalankan pembanding, lalu pulihkan salinan.
- PDF sampel untuk pengukuran dan inspeksi visual ada di `%TEMP%/sudesapp-sampel`, ditulis `CetakContohSuratTests` dan sengaja tidak dihapus setelah uji.

## Tata Letak SudesPdf
- Di mesin tata letak SudesPdf, `AlignRight()/AlignCenter() + Width(...)` pada anak kolom TIDAK menggeser isi: kotaknya sempit tapi isinya ditata selebar induk, jadi blok tanda tangan `AlignCenter` tercetak di tengah halaman. Untuk blok di sisi kanan pakai `Row(row => { row.RelativeItem(); row.ConstantItem(lebar)… })`.

## Harness
- Angka muat/render per halaman dicetak `SuDesApp.Tests/Perf/HarnessRenderPratinjauTests`; jalankan `-c Release` + `--logger "console;verbosity=detailed"` lalu cari baris `[harness]`. Harness memakai `PdfRenderer` (tanpa WPF), jadi aman di thread uji.
