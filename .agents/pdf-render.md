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

## Kertas & Pencetakan
- Satu jalur cetak untuk semua dokumen: `SudesPdf/PdfPrinter.cs` (PrintDialog WinForms +
  PrintDocument, raster per halaman). Geometri kertas dipisah ke `PdfPrintLayout` supaya
  bisa diuji tanpa printer; ukuran fisik lembar dibaca dari DC lewat `PrintDc`.
- Ukuran halaman PDF = ukuran kertas FISIK: A4 595,28 x 841,89 pt (210 x 297 mm),
  F4 609,45 x 935,43 pt (215 x 330 mm). Angka itu ada di TIGA tempat yang wajib sama:
  `PengaturanCetak.Dimensi`, `PageSizes` (SudesPdf.Core), dan `SuratPdf.kt` Android.
  F4 desktop dulu 607,5 pt = 214,3 mm (0,7 mm kertas tak terpakai, dan beda dari
  `PageSizes.F4`). Penjaga: `UkuranKertasSamaDiDesktopAndroidDanMesinPdf` dan
  `PengaturanCetakF4MenjadiUkuranHalamanPdf` (uji isi PDF asli, bukan konstanta).
- Urutan memilih kertas: padanan terdekat di `PrinterSettings.PaperSizes` (toleransi
  0,1 inci, orientasi diabaikan) - kalau driver tidak punya, kirim formulir kustom
  `DMPAPER_USER` dengan ukuran halaman PERSIS. Yang diubah adalah printer, bukan
  suratnya. Driver menolak ukuran aplikasi = hasil tercetak kecil dan itu bug lama.
- `PageSettings.PaperSize` hanya menggemakan permintaan aplikasi, BUKAN bukti driver
  menuruti. Kertas sebenarnya = `GetDeviceCaps(PHYSICALWIDTH/PHYSICALHEIGHT)`. Sikap
  driver nyata berbeda-beda di mesin yang sama: PDF-XChange menerima 215x330 (melapor
  216x330), Microsoft Print to PDF menolak formulir kustom dan kembali ke Letter,
  driver Fax mengembalikan lebar 0 (maka `PrintDc.Lembar.TidakDiketahui` dan fallback
  ke PaperSize).
- Jebakan GDI+: `Graphics` yang HDC-nya sedang dipegang menolak SEMUA panggilan lain —
  `graphics.DpiX` di dalam blok `GetHdc()` melempar "Object is currently in use
  elsewhere" dan job cetak mati tanpa pesan. Baca dpi sebelum meminjam HDC (dijaga
  urutan teks oleh `PdfPrinterMemakaiLembarDariDcDanMelepasHdc`).
- Satuan: `PaperSize` .NET = persepuluh INCI, `DEVMODE.dmPaperWidth/dmPaperLength` =
  persepuluh MILIMETER. `dmFormName` dibiarkan kosong oleh .NET untuk formulir kustom,
  jadi jangan menguji namanya - driver membaca angka. `GetHdevmode()` mengembalikan
  HGLOBAL yang wajib di-`GlobalLock` sebelum dibaca; tanpa itu isinya sampah dan tes
  bisa "lulus" tanpa memeriksa apa pun (pernah terjadi).
- Bila driver tetap memakai kertas lebih kecil, `PdfPrintLayout.Hitung` memperkecil ke
  ukuran LEMBAR (bukan ke kotak tinta - itu bug "hasil cetak 4% lebih kecil") lalu
  memusatkan; margin keras hanya memotong tepi yang memang kosong (margin desain surat
  45 pt ~ 16 mm). `Document.PrintAsync` mengembalikan `HasilCetak`
  (skala + ukuran lembar sebenarnya) dan lapisan WPF
  `PdfPrintService.TampilkanPeringatanKertas` menyampaikan alasannya ke pengguna -
  jangan kembali diam-diam.

## Harness
- Angka muat/render per halaman dicetak `SuDesApp.Tests/Perf/HarnessRenderPratinjauTests`; jalankan `-c Release` + `--logger "console;verbosity=detailed"` lalu cari baris `[harness]`. Harness memakai `PdfRenderer` (tanpa WPF), jadi aman di thread uji.
