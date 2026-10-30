# Arsitektur pembuatan PDF surat

Catatan struktur lapisan PDF surat (mesin SudesPdf milik aplikasi sendiri) supaya
perubahan berikutnya menambah ke tempat yang sudah ada, bukan menyalinnya lagi.
Generator surat berada di `SuDesApp.Core/GeneratorPdf/`, sedangkan mesin PDF-nya di
`SudesPdf.Core/` dan `SudesPdf/`.

## Peta modul

| Modul | Tanggung jawab |
|---|---|
| `KopSurat.cs` | **Kop surat**: isi baris (`BarisKop`), ukuran font, dan cara menggambarnya (`Gambar`). |
| `SuratRenderer.cs` | Primitive tata letak bersama: `Teks`, `Paragraf`, `ParagrafCampur`, `TabelFormulir`, `DaftarBernomor`, `JudulTengah`, `JudulDanNomor`, `TandaTangan`. Tidak memiliki susunan kop. |
| `Rata.cs` | Perataan teks (`Kiri/Tengah/Kanan/Justify`). |
| `KerapatanSurat.cs` | Kerapatan tata letak (jarak baris/paragraf, margin, ruang tanda tangan) + `Aktif` per konteks async. |
| `BadanSurat.cs` | Pemilik urutan blok yang diisi generator dan satu-satunya yang merender blok itu ke kolom SudesPdf. Daftar internal tidak diekspos. |
| `SuratGeneratorBase.cs` | Kerangka satu dokumen: validasi data, ukuran halaman, penyesuaian kerapatan, urutan kop → judul → badan → tanda tangan. |
| `SuratPdfSave.cs` | Pemilik siklus hidup berkas PDF: menulis ke berkas sementara unik, mengganti nama akhir tanpa menghapus versi lama lebih dulu, mempertahankan sementara bila tujuan terkunci, dan membersihkan sementara bila penulis gagal. |
| `*Generator.cs` | Isi masing-masing jenis surat (`ComposeBody`/`ComposeHalaman`). |

## Aturan yang dipegang

1. **Kop punya satu penulis.** Komposisi kop hanya di `KopSurat.Gambar`. Generator
   lain tidak menyalin susunan logo + baris + garis ganda; mereka memanggilnya
   (termasuk dokumen landscape lewat `skalaJudul`). `SuratRenderer` tidak membungkus
   atau mengalias metode kop itu.
2. **Perataan bukan kerapatan.** `Rata` dipakai sebagai tipe tersendiri supaya bisa
   menjadi pola `switch`; `KerapatanSurat` hanya mengatur jarak.
3. **Isi surat milik generator, bentuk milik renderer.** Generator tidak menghitung
   ukuran huruf, garis, atau ruang tanda tangan sendiri — itu kebijakan `SuratRenderer`
   dan `KerapatanSurat`.
4. **Aturan cetak yang berlaku untuk semua surat** (judul kapital bergaris bawah +
   baris `NOMOR :`, baris "Nama" kapital, titik-titik saat pratinjau form lewat
   `PratinjauIsian`, blok tanda tangan Perbup 74/2020) tinggal di `SuratRenderer`.
5. **Berkas PDF ditulis lewat `SuratPdfSave.SimpanAsync`** — termasuk oleh pemanggil
   WPF (`SuratPdfHelper`), yang hanya menyusun nama berkas, memilih generator, lalu
   memberikan callback penulis. Pemilik penyimpanan mengatur benturan nama, fallback
   untuk berkas yang terkunci, serta pembersihan saat callback gagal.
6. **Kelas bantu bersama tinggal di Core.** `TanggalIsian`, `NamaFormatter`,
   `AlamatFormatter`, dan `BerkasDokumentasi` hanya punya satu salinan; proyek WPF
   memakainya lewat referensi proyek, bukan dengan menyalin berkasnya.
7. **Jarak di luar latar memakai margin, bukan padding.** `Background` dicat sampai
   ke padding, jadi `PaddingTop(3).Height(0.8).Background(...)` menghasilkan balok
   setinggi 3,8 pt — garis tipis kop hilang dan kedua garis menyatu. Garis ganda kop
   karena itu memakai `MarginTop` supaya tingginya benar-benar 0,8 pt dan 1,5 pt.
7. **`ISuratGenerator` tidak lagi ada.** Kontrak generator surat adalah
   `SuratGeneratorBase`; generator yang berdiri sendiri (`SuratRegisterGenerator`,
   `TemplateSuratGenerator`) dipakai langsung oleh pemanggilnya. `RekeningKoranGenerator`
   memakai kerangka dasar yang sama; komposisi khusus hanya mengganti badan suratnya.
8. **State jenis NTCR lokal pada komposisi.** Judul berasal dari jenis surat yang sedang
   dikomposisikan, bukan state mutable pada instance generator. Kerapatan tetap milik
   konteks render (`AsyncLocal`) dan dipulihkan lewat `using` setelah tiap percobaan.

## Cara memeriksa

```bash
dotnet build SuDesApp.slnx --nologo -v q
dotnet test SuDesApp.Tests/SuDesApp.Tests.csproj --nologo -v q
```

`SuDesApp.Tests/Surat/CetakContohSuratTests.cs` mencetak contoh nyata tiap keluarga
surat dan menulis berkasnya ke `%TEMP%\sudesapp-sampel`, sehingga tata letak bisa
diperiksa langsung (mis. lewat `pdftotext -layout`, atau dirender ke PNG lewat
`SudesPdf.Core` — lihat catatan struktur di bawah).

Dua uji regresi di berkas itu memeriksa ISI hasil cetak lewat ekstraksi teks SudesPdf —
baris kop dari `KopSurat`, judul + `NOMOR :`, nama yang kapital, dan blok tanda
tangan (termasuk jabatan Lurah + NIP). Jadi menghilangkan aturan cetak bersama akan
menggagalkan uji, bukan cuma mengubah tampilan. `SuDesApp.Tests/Surat/SuratPdfSaveTests.cs`
menjalankan penyimpanan melalui sistem berkas sungguhan: penggantian tujuan, kegagalan
penulis beserta pembersihan, tujuan yang terkunci, dan dua penulis bersamaan.

## Struktur mesin PDF sendiri (SudesPdf)

Aplikasi tidak lagi memakai pustaka PDF pihak ketiga. Susunannya dua proyek:

- **`SudesPdf.Core`** — mesin, tanpa WPF dan tanpa paket NuGet sama sekali:
  - `PdfSyntax` / `PdfDocument` / `PdfObjects` — objek, xref, dan stream PDF;
  - `PdfFilters` (inflate/PNG/dash), `PdfEncryption` — penyandi isi;
  - `PdfFonts` (enkoding, CMap/ToUnicode), `PdfTrueType`, `PdfCff` — bentuk glif;
  - `PdfContent` — penafsir operator aliran isi (jalur, teks, XObject, warna);
  - `PdfRaster` — penggambar raster (jalur, gambar, gradien, pola, PNG);
  - `PdfTeksGamma` — tabel gamma cakupan huruf, salinan persis milik pdfium
    (lihat di bawah);
  - `PdfRenderer` — pintu masuk: `RenderPage` dan `ExtractText` (`IPdfSink`).
  - `PdfWriter*` (`PdfWriter`, `PdfWriterContent`, `PdfWriterFonts`, `PdfWriterImages`,
    `PdfWriterPage`) — **penulis** dokumen: katalog/halaman/xref, aliran isi, dan
    sumber gambar. Font surat adalah **Times New Roman asli yang disematkan**: tiap
    muka diambil dari font sistem (`times.ttf`, `timesbd.ttf`, …), dipotong menjadi
    subset `/FontFile2` berisi glif yang benar-benar dipakai, lalu lebar huruf pada
    `/Widths` tetap memakai metrik `PdfStandardTimes` supaya pemenggalan baris tidak
    berubah. Bila berkas font sistem tidak ada, penulis jatuh kembali ke font
    standar PDF (`Times-Roman`) agar dokumen tetap terbentuk.
  - `Layout/*` — **penata letak surat**: `LayoutApi` (API berantai yang dipakai
    generator), `LayoutNodes` (model simpul + nilai), `LayoutTextLayout` (pemotongan
    baris), `LayoutEngine` (pengukuran, alih halaman, gambar). Berkas diproses
    `PageDescriptor` → `LayoutEngine.Flow` → `PdfWriter`.
- **`SudesPdf`** — lapisan WPF: `Document` (pemilik tunggal state dokumen, memuat,
  menyajikan halaman sebagai bitmap, membaca teks, mencetak), `PdfViewer` untuk
  tata letak pratinjau, dan `PdfPrinter`/`PdfPrintPaginator` yang meraster halaman
  satu per satu saat printer memintanya.

  Pratinjau merender halaman **tepat satu piksel bitmap per piksel layar**
  (`RenderAsync(..., dpi = 96 × skala tampilan Windows)`) lalu menampilkannya tanpa
  penskalaan. Menggambar lebih rapat lalu membiarkan WPF memperkecilnya justru
  mengaburkan huruf tebal, karena tepi glif melewati penyaringan bilinear.
  Dua hal kecil yang mudah lolos dan tetap mengaburkan pratinjau:
  lebar bitmap harus dihitung dari jumlah piksel layar (bukan DIP lalu dibulatkan),
  dan bayangan kartu tidak boleh memakai `Effect` pada elemen yang memuat halaman —
  effect WPF menggambar seluruh isi elemen ke bitmap antara sebelum dikomposisikan.
  Bayangan karena itu ditaruh pada elemen kosong di belakang kartu.

  Halaman **tidak** diraster sekaligus. `PdfPreviewView` lebih dulu membuat kartu
  placeholder berukuran pasti untuk semua halaman, lalu menggambar hanya halaman
  yang terlihat (ditambah cadangan setengah layar) satu per satu; sisanya menyusul
  saat digulir. Dokumen panjang karena itu terbuka secepat dokumen satu halaman,
  zoom tidak menunggu seluruh dokumen, dan penggulungan tidak melompat karena
  ukuran kartu sudah ditetapkan. Bitmap yang jauh dari layar dilepas kembali
  (maksimal 10 halaman tersimpan) agar memori tidak membengkak.

### Ketajaman huruf

Dua langkah menentukan apakah teks terbaca tajam, keduanya disamakan dengan cara
pdfium membangun sebuah huruf menjadi piksel:

1. **Cakupan luas eksak** (`Rasterizer.FillTepat`). Cakupan tiap piksel dihitung
   sebagai luas bentuk yang benar-benar jatuh di piksel itu — bukan dari beberapa
   sampel. Rasterizer FreeType memakai cara yang sama; dengan 8 sampel hanya ada
   9 tingkat keabuan, sehingga tepi lengkung dan batang tebal tampak bergaris.
2. **Tabel gamma cakupan** (`PdfTeksGamma`). Cakupan parsial dari tepi huruf
   dilewatkan tabel 256 entri yang diambil persis dari `kTextGammaAdjust`
   (`core/fxge/cfx_renderdevice.cpp`) sebelum di-blend. Nilai kecil dinaikkan
   (10 → 13, 64 → 73), nilai penuh dibiarkan (255 → 255): batang yang sudah pekat
   tidak melebar, sedangkan tepi yang tadinya abu-abu menjadi lebih gelap sehingga
   baris terbaca utuh.

Bentuk glif dipakai apa adanya — tanpa pembulatan lebar batang ke grid piksel.
Versi sebelumnya melakukan pembulatan itu (meniru hinting), tetapi diukur terhadap
render pdfium pada berkas surat yang sama: batang tebal 12pt yang lebar aslinya
±2,6 piksel dipaksa menjadi 3 piksel penuh, sehingga baris tebal membawa ±7% tinta
lebih banyak dan jarak antara dua batang menutup — huruf tebal justru terlihat
tersumbat. Tanpa pembulatan itu, jumlah tinta berada dalam ±0,3% pdfium pada 1×
dan di bawah 4% pada 0,7×–1,33× (pdfium masih memakai hinting FreeType, yang
paling terasa pada huruf sangat kecil), sementara gamma menjaga ketajamannya.

Satu-satunya penyesuaian posisi yang tersisa adalah garis dasar: satu baris digeser
ke baris piksel utuh (kurang dari setengah piksel), supaya garis lintang huruf tidak
terjepit di antara dua baris. Pergeseran ini berlaku sama untuk seluruh baris, jadi
tata letak dan pemenggalan baris tidak berubah.

Arah data saat **menulis**: generator (`SuDesApp.Core/GeneratorPdf/*`) →
`SudesPdf.Layout` → `PdfWriter` → berkas PDF. Saat **membaca**: berkas →
`PdfDocument` → penafsir → `IPdfSink` → raster atau teks.
`Document` menyerialkan akses ke mesin (satu kunci) supaya cache font/bentuk tidak
diakses bersamaan oleh pratinjau dan cetak. Pemakai di aplikasi hanya berbicara
dengan `Document`, bukan dengan kelas mesin.

## Konvensi koordinat tata letak

Seluruh `LayoutEngine` bekerja dalam **titik PDF: y nol di tepi bawah kertas dan
bertambah ke atas**. Simpul selalu menerima `yTop` = tepi atasnya; turun ke bawah
berarti mengurangi (`y -= tinggi`), dan `boxTop = yTop - margin`,
`contentTop = boxTop - padding`, `baseline = yTop - line.Baseline`. Kotak berisi
(dimensi apa pun) memakai `top - height` sebagai titik bawahnya. Menambah arah y
(seperti alih halaman atau kop bersusun) adalah tanda bahwa kode memakai konvensi
yang salah — dulu itulah yang membuat baris demi baris surat keluar melewati tepi
atas halaman.
