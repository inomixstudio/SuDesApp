# SuDesApp 2.16.0 — 26 November 2026

Rilis ini dikerjakan di dua sisi meja operator. Sisi pertama apa yang **dilihat dan
ditekan**: tema hijau dibersihkan dari warna mint yang membuat seluruh halaman
terasa lembap, tombol dan kartu diberi gerak pegas yang benar-benar berjalan
(sebelumnya animasinya mati diam-diam), sudut tombol disatukan, judul kepala halaman
dinaikkan satu anak tangga tipografi, dan **Ctrl+P** akhirnya mencetak halaman yang
sedang dibuka. Sisi kedua apa yang **keluar dari printer**: kertas F4 diukur ulang ke
ukuran fisik yang benar, dan saat mencetak aplikasi sekarang memaksa ukurannya ke
printer — bukan memperkecil surat supaya muat ke kertas bawaan printer.

Ditambah satu koreksi yang berdampak pada arsip desa: awalan nomor surat tidak lagi
470 untuk semua jenis, tetapi mengikuti kelas klasifikasi arsip Karawang.

**Tidak ada migrasi manual.** `desa.db`, `Templates`, arsip surat, dan skema Supabase
dipakai apa adanya. Satu indeks (`idx_surat_antrean_sinkron`) dibuat otomatis dengan
`CREATE INDEX IF NOT EXISTS` saat database dibuka, dan definisi query ganda ditolak
saat startup — keduanya idempoten dan tidak mengubah isi data.

## Tampilan & Gerak

- **GreenTheme: warna hanya pada chrome.** Latar halaman kini netral dingin, kartu
  putih, dan warna hijau tinggal di sidebar, header, dan tombol. Sebelumnya seluruh
  halaman diberi warna mint sehingga kartu tidak terbaca sebagai objek dan teks ikut
  memudar. Palet baru ini juga disamakan ke Android (`colors.xml`, `bg_header.xml`,
  `themes.xml`) supaya ponsel dan desktop tidak lagi dua aplikasi berbeda rasa.
- **Hover tombol tidak lagi memakai `Opacity` tombol keseluruhan** — itu yang membuat
  teks memudar saat kursor lewat. Template `Button` sekarang punya lapisan
  `SorotLapis`/`TekanLapis` di dalam bentuk tombol sehingga yang berubah hanya warna.
  `IconButton` menyusul di commit terpisah; warna akhirnya sengaja tetap `PanelBrush`
  supaya tidak ada yang berubah rasa bagi operator, hanya perpindahannya.
- **Gerak pegas untuk kartu dan accordion** (`KartuHalus` + `PegasEase`): kartu yang
  membuka sesuatu terangkat saat disorot dan membal saat ditekan. Semuanya hanya
  menyentuh `Opacity` dan `RenderTransform` (kompositor) agar tidak memicu layout
  pass, dan menghormati preferensi kecepatan animasi serta pengaturan
  "reduce animations" Windows.
- **Dua jebakan WPF yang membuat animasi mati diam-diam ikut ditutup.**
  `UIElement.RenderTransform` defaultnya `MatrixTransform` identity, **bukan null**,
  sehingga pemeriksaan `!= null` selalu membatalkan animasi; dan pita kosong di tepi
  bawah kartu membuat kursor bergetar keluar-masuk zona angkat. Keduanya kini dijaga
  `AnimasiKartuTombolTests`.
- **Pindah halaman justru sengaja tanpa fade.** Satu `ContentHost` tidak bisa memudar
  tanpa menyisakan satu frame kosong — itulah kedip yang dikeluhkan, dan menghapus
  transisinya lebih benar daripada menambah durasi.
- **Satu sumber sudut tombol.** `RadiusTombol` (12) dan `RadiusTombolIkon` (10)
  menggantikan angka mentah yang tersebar (8, 4, 11). Tiga `Border` pada template
  `Button` (luar, `SorotLapis`, `TekanLapis`) wajib mengunci sumber yang sama: kalau
  salah satunya angka mentah, sudut lapisan keluar dari bentuk tombol saat hover dan
  tidak ada galat yang memperingatkan — dijaga `RadiusTombolTests`. Kotak isian
  dinaikkan ke 8 agar aksi tetap terasa lebih bulat daripada kolom isian; kartu
  dibiarkan 8–10 supaya hierarki permukaan tidak terbalik.
- **Judul kepala halaman 15 → 20 px.** Selisih satu langkah dengan `SectionTitle`
  13 px membuat halaman tampak seperti satu daftar teks dan nama halaman justru
  menyatu dengan keterangan section. Angka 20 adalah anak tangga "judul" pada skala
  baku yang dijaga `UkuranHurufStandarTests` (18/19 akan ditolak penjaga itu).
  Sekaligus memperbaiki cacat yang menyamar sebagai "header rusak": `TextBlock`
  ber-`MaxWidth` yang merentang **tidak** dipotong di kiri, melainkan dibagi ruang
  sisanya ke kiri DAN kanan, sehingga subtitle Dokumentasi, API Desa, Kelola
  Pengguna, Sinkron, dan Verifikasi Surat mengambang ±200 px di bawah judulnya.
  `HorizontalAlignment="Left"` mengembalikannya; `HeaderHalamanTests` mewajibkannya
  pada setiap `HeaderSubtitle` ber-`MaxWidth`, karena kedua cacat ini gagal senyap.
- **Jendela pembuka** tetap menampilkan identitas versi, tanpa bilah persen: strip
  progres tak tentu dipulihkan atas permintaan, dan penjaganya kini hanya memeriksa
  versi.
- Tombol **Buka Folder Dokumen** di halaman Dokumentasi dihapus beserta
  `BukaFolderCommand` dan `BerkasDokumentasi.Buka()` yang tidak terpakai: halaman itu
  berjanji dokumen dibaca di dalam aplikasi, dan satu tombol yang melempar pengguna ke
  File Explorer membuat janji tersebut terdengar opsional.

## Cetak & Ukuran Kertas

- **F4 diukur ulang: 215 × 330 mm (609,45 × 935,43 pt).** Angka lama 607,5 pt adalah
  214,3 mm — 0,7 mm tidak terpakai di kiri-kanan dan berbeda dari `PageSizes.F4` di
  SudesPdf.Core. Ukuran halaman adalah dasar printer memilih formulir kertas, jadi
  selisih itu ikut menentukan apakah driver mengenali F4 atau jatuh ke kertas lain.
  Label "8,5 x 13 inci" di Pengaturan Cetak juga menyesatkan karena itu Folio Amerika
  (215,9 mm) dan diganti "215 x 330 mm". Android memakai angka yang sama di
  `SuratPdf.kt`, sehingga keduanya naik bersamaan; penjaganya membaca **PDF asli hasil
  generator**, bukan hanya konstanta.
- **Ukuran kertas aplikasi yang dipasang ke printer, bukan surat yang diperkecil.**
  Bila driver tidak mencantumkan ukuran halaman di daftar kertasnya, `PdfPrinter`
  mengirimnya sebagai formulir `DMPAPER_USER` (DEVMODE) sehingga printer disetel ulang.
  Sebelumnya aplikasi menyerah dan `PdfPrintLayout` menyusutkan halaman ke kertas
  bawaan printer — gejala "hasil cetak tidak pas dengan kertas".
- **Ukuran lembar dibaca dari DC printer, bukan dari `PageSettings.PaperSize`.**
  Properti .NET itu hanya menggemakan permintaan aplikasi. Lewat `PrintDc`
  (`GetDeviceCaps(PHYSICALWIDTH/HEIGHT)`) terbukti pada mesin yang sama: PDF-XChange
  menerima 215×330 mm, Microsoft Print to PDF menolak formulir kustom dan kembali ke
  Letter, driver Fax mengembalikan lebar 0 (menjatuhkan ke keadaan "tidak diketahui").
- **Satu galat yang membuat cetak F4 gagal total ditemukan saat uji end-to-end:**
  `graphics.DpiX` dibaca selagi HDC masih dipinjam, dan GDI+ melempar
  "Object is currently in use elsewhere" untuk *metode apa pun* dalam keadaan itu.
  Pembacaan DPI dipindah ke sebelum `GetHdc()`, dan urutannya dijaga teks uji.
- Bila driver tetap menolak, tata letak memperkecil ke **ukuran lembar** (bukan ke
  kotak tinta — pemaskan ke `MarginBounds` itulah yang membuat surat ±4% lebih kecil),
  dan `Document.PrintAsync` mengembalikan `HasilCetak` berisi skala serta ukuran lembar
  sebenarnya. `PdfPrintService` menyampaikannya lewat dialog peringatan supaya operator
  tahu harus menambah formulir kertas, bukan menduga kertasnya salah potong.
- Dijaga `PdfCetakUkuranKertasTests` (15), termasuk pemeriksaan `DEVMODE` nyata lewat
  `GetHdevmode` + `GlobalLock` — tanpa `GlobalLock` yang terbaca hanya sampah, dan uji
  itu pernah "lulus" tanpa memeriksa apa pun.

## Penomoran Surat

- **Awalan nomor mengikuti kelas klasifikasi arsip, bukan 470 untuk semua.** Dua puluh
  dari 28 jenis surat memakai 470 dalam satu deret. Angka itu terkesan punya dasar
  hukum karena lampiran Perbup Karawang 74/2020 memang mencetak 470 — tetapi hanya pada
  empat contoh (SK umum, kelahiran, kematian, ahli waris), dan keempatnya urusan
  kependudukan/capil. Pada Perbup Karawang 28/2019 tentang Pedoman Klasifikasi Arsip,
  470 adalah KELAS "Kependudukan dan Catatan Sipil", jadi SKTM, surat usaha, garapan
  sawah, pengantar SKCK, dan surat ke bank yang semuanya bernomor 470 berarti nomor
  surat desa tidak pernah cocok dengan klasifikasi arsipnya sendiri.
- Kelas diambil dari lampiran resmi JDIH Karawang: 465 Kesejahteraan Sosial (dengan
  465.3 Masyarakat Miskin untuk SKTM), 471 Pendaftaran Penduduk (471.21 perpindahan,
  471.23 tinggal sementara), 472 Pencatatan Sipil (472.2 perkawinan), 330
  Keamanan/331 Kepolisian, 510 Perdagangan, 518 UMKM, 521 Tanaman Pangan, 580
  Perbankan. Rekening Koran pindah ke 580 karena 130 di Karawang adalah Pemerintah
  Kabupaten, bukan keuangan. Blanko NTCR tetap 474.3/474.2 (dasar sektoral Kepdirjen
  Bimas Islam 473/2020) dan Template tetap TMPL.
- **Sub-kode yang tadinya dikutip dari pencocokan kata kunci diluruskan:** Sawah itu
  521.211 (di bawah 521.21 Padi), Persawahan 521.431, Pembinaan Usaha Mikro 518.311
  (518.31 hanyalah "Pembinaan Usaha"). Kelas tiga digitnya tidak berubah, jadi nomor
  yang sudah terbit tetap sama.
- Jenis yang berbagi awalan ditandai `IsSharedNumbering=true` karena cabang deret
  menghitung per `KodeJenis`: dua jenis berawalan sama akan sama-sama mulai 001 dan
  nomornya bentrok — bug yang dulu menimpa blanko N1–N6.
- **Surat lama tidak diubah**, dan pola yang sudah disesuaikan operator di Pengaturan
  Nomor Surat tetap menang. Yang belum didukung contoh atau kelas eksplisit ditandai
  "penafsiran" di dokumentasi.
- **Android ikut tanpa menyalin angka:** ponsel mengambil pola dari muatan
  `pengaturan_desa.nomor.format` yang diterbitkan desktop. Rantai itu belum ada
  penjaganya — jenis surat yang lupa didaftarkan di katalog tidak menghasilkan galat,
  hanya nomor 470 di HP sementara register desktop memakai kelas yang benar.
  `ParitasNomorAndroidDesktopTests` (3) membandingkan kunci skema dengan katalog dan
  mengunci nama kunci kontrak di sisi penerbit maupun pembaca.

## Formulir Isian

- **Keterangan terisi sejak form dibuka.** Izin Keramaian, Izin Tinggal Sementara, dan
  Domisili Instansi punya pembangkit kalimat keterangan, tetapi pemanggilannya hanya
  tercantol di jalur simpan dan jalur edit — bukan di `InitializeSpecificAsync` seperti
  surat lain. Operator melihat kotak kosong di form baru, dan validasi "Keterangan
  harus diisi" menahan Simpan.
- **Suntingan operator tidak lagi ditimpa.** Pada Izin Tinggal, kalimat baru selalu
  menutupi kotak Keterangan saat menyimpan sehingga hasil sunting hilang tanpa jejak;
  sekarang hanya diisi bila kosong, dan keterangan tersimpan dipakai lagi saat edit.
- Kalimat Izin Keramaian tidak lagi disalin dua kali — dipindah ke
  `KeramaianGenerator.KeteranganTeks(DesaData)` yang dipakai form dan PDF bersama,
  karena salinan di form menulis "Kabupaten" apa pun wilayahnya sehingga surat dari
  kota tercetak berbeda dari yang dibaca operator. Android tidak perlu ikut berubah:
  bila keterangan dibiarkan kosong, generator memakai pembangun yang sama.
  `KeteranganIsianAwalTests` mengikat isian form persis dengan kalimat generator.

## Pintasan Papan Ketik

- **Ctrl+P mencetak halaman yang sedang dibuka**, di 10 halaman (Register Surat,
  Register NTCR, Data Warga, Perangkat Desa, Tutup Buku Tahunan, Laporan, Agenda
  Surat, Daftar Hadir, Keputusan, IDM) dan di halaman pratinjau PDF. Operator surat
  sudah mengharapkan Ctrl+P dari Word dan browser; sebelumnya satu-satunya jalan
  adalah menemukan tombol Cetak yang posisinya beda tiap halaman.
- Pintasan dipasang **per halaman**, bukan global di `MainWindow`, karena perintah
  cetaknya berbeda-beda (`CetakCommand`, `CetakPdfCommand`, `PrintRegisterCommand`)
  dan pintasan global harus menebak halaman mana yang sedang tampil.
- Di pratinjau, dokumennya hidup di view, jadi pintasnya didaftarkan di kode-behind dan
  sengaja memakai `AsyncRelayCommand` yang membungkus `PrintAsync` **yang sama** dengan
  tombol Cetak — tidak ada jalur cetak kedua. `AsyncRelayCommand` app menolak jalan
  berbarengan, jadi Ctrl+P dobel tidak mengirim dua job ke printer.
- Tooltip tombol cetak kini menyebut `(Ctrl+P)` dan Panduan Awal menambah satu kiat:
  pintasan yang tidak diberitahukan sama dengan pintasan yang tidak ada.
  `PintasanCetakTests` (5) menolak halaman bertombol cetak tanpa Ctrl+P, Ctrl+P yang
  menunjuk perintah bukan-cetak, tooltip yang tidak menyebut pintasan, dan menjalankan
  `KeyBinding` P+Control pada halaman pratinjau sungguhan di thread STA.

## Pembaruan Aplikasi, Sinkron, & Lisensi

- **Berkas pembaruan dipulihkan utuh bila penerapan gagal di tengah.** Skrip lama
  menimpa berkas sebelum mencatat keadaan awal, tidak mencadangkan berkas yang akan
  dihapus, dan tidak menghapus berkas baru saat rollback — kegagalan di tengah bisa
  meninggalkan aplikasi campuran versi. Sekarang setiap berkas dicadangkan lebih dulu,
  dipulihkan dalam urutan terbalik bila ada langkah yang gagal, dan galatnya dicatat ke
  `penerapan.log`. Aplikasi hanya dibuka lagi bila penerapan atau pemulihan lengkap.
  `SkripPenerapanTests` menjalankan skrip sungguhan di folder sementara untuk kedua jalur.
- **Installer pembaruan wajib lolos Authenticode.** Pemeriksaan sebelumnya hanya
  SHA-256 dan dilewati bila digest kosong, padahal hash tidak membuktikan asal rilis.
- **Nomor surat yang bentrok dibangkitkan ulang.** `INSERT` yang jatuh pada UNIQUE
  `NomorSurat` kini mencoba nomor baru alih-alih menggagalkan penyimpanan, karena
  penomoran paralel (WPF dan sinkron Android) tidak lintas-proses.
- **Impor surat antrean Android tidak lagi bergantung pada hapusnya baris antrean.**
  Surat yang `IdAntreanSinkron`-nya sudah ada dilaporkan berhasil dengan id dan nomor
  resmi yang sama, sehingga crash di antara insert dan hapus tidak menggandakan surat.
  Kolom itu diberi indeks, dan definisi `InsertSurat` yang tersambung dua kali di
  `SuratQueries.sql` (definisi terakhir diam-diam membuang `AdditionalData`, `Status`,
  `IdAntreanSinkron`) kini ditolak saat startup.
- **Token lisensi tidak lagi dibuang pada 404 buta.** 404 juga bisa datang dari proxy
  desa atau rute yang hilang, sehingga lisensi yang masih sah terhapus tanpa sebab yang
  terlihat. Cache hanya dibuang bila balasan `/aktivasi` menyatakan kode aktivasi tidak
  dikenal; 404 lain dicatat sebagai peringatan.
- Migrasi penghapusan model N7 dibungkus satu transaksi agar tidak meninggalkan data
  setengah terhapus.

## Pengaturan Aplikasi

- **Pemilih bahasa Indonesia/English dibuang.** Pilihan itu hanya benar-benar mengubah
  tiga teks: baris pemilihnya sendiri, label versi di footer halaman itu, dan label
  versi di splash. Sisanya — ratusan judul, tooltip, pesan galat, dan seluruh PDF —
  teks Indonesia yang ditulis langsung di XAML dan ViewModel, jadi memilih "English"
  menghasilkan aplikasi yang tampak setengah Inggris. Kerangka lokalisasi
  (`Lokalisasi.cs`, `SettingSelectionRowVM.cs` beserta `PilihanItem`, partial `Bahasa.cs`
  dengan `RowsPilihan`/`SelectedBahasa`, kunci preferensi `bahasa`) dihapus; teks versi
  kini string Indonesia biasa, jadi baris versi di halaman pengaturan tampil sama
  persis. Kunci `bahasa` yang masih tersisa di `login_prefs.json` lama cukup dilewati —
  tidak ada migrasi. `PengaturanBahasaDihapusTests` (4) menjaga kerangka itu tidak
  tumbuh lagi.

## Dokumentasi

- Petikan lampiran Perbup Karawang 28/2019 dibundel di repo (`docs/regulasi/`), dipilih
  per kelas yang dipakai aplikasi, supaya pemeriksaan satu baris tidak lagi mengharuskan
  mengunduh 94 halaman daring.
- `docs/referensi-penomoran-surat.md` menambah bab rantai penomoran desktop → Android.
- `AGENTS.md` menambah aturan ZONA KERTAS (F4 harus sama di tiga tempat), penjaga
  pintasan Ctrl+P, dan larangan menghidupkan lagi pemilih bahasa.
- `docs/tema-dan-warna.md`, `.agents/pdf-render.md`, dan `docs/arsitektur-pdf-surat.md`
  diselaraskan dengan perilaku kertas dan tema baru.

## Uji

- `dotnet test SuDesApp.slnx` — **697/697 lolos**, termasuk uji baru rilis ini:
  `AnimasiKartuTombolTests`, `HeaderHalamanTests`, `KeteranganIsianAwalTests`,
  `RadiusTombolTests`, `PdfCetakUkuranKertasTests` (15), `PenomoranKodeKarawangTests`,
  `ParitasNomorAndroidDesktopTests` (3), `PintasanCetakTests` (5), dan
  `PengaturanBahasaDihapusTests` (4).
- Gerbang tetap hijau: `check-kontras-wcag.ps1` (11 tema, WCAG 2.1 AA),
  `CatatanRilisSelarasTests` (kartu teratas = `AssemblyVersion`; setiap dokumen rilis
  harus punya kartu), `UkuranHurufStandarTests`, paritas Buat Surat Android ↔ desktop,
  dan `KontrakHubunganServerLisensiTests` (ZONA TERKUNCI) — server lisensi tidak
  disentuh.
- APK 2.16.0 ditandatangani dengan kunci yang sama seperti APK rilis sebelumnya dan
  sidik jarinya dibandingkan otomatis sebelum diunggah; `versionCode`/`versionName`
  Android ikut naik di commit rilis yang sama karena ponsel membandingkan
  `versionName` dengan versi desktop secara persis.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.15.2 | tambalan kecil bila `patch.json` rilis ini menyertainya | hanya berkas yang berubah |
| di bawah 2.15.2, atau rilis tanpa tambalan | installer penuh 2.16.0 | ± 117 MB |

Aplikasi memilih jalurnya sendiri: tambalan hanya dipasang bila versi terpasang
tercantum di `dariVersi` pada `patch.json`; selain itu diarahkan ke installer penuh.
Data desa (`desa.db`, `Templates`, hasil PDF, `appsettings.json`) tidak ditimpa saat
pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
