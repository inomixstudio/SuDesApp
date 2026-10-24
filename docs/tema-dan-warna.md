# Tema & Warna

Aplikasi WPF punya sebelas tema (Green, Light, Dark, Blue, Pink, Slate,
HighContrast, plus empat adaptasi preset tema desain populer: Orbit, Ember,
Kanagawa, Blossom) plus **Auto** yang mengikuti mode gelap Windows. Dokumen
ini menjelaskan di mana warna harus ditulis, supaya tidak ada lagi bagian
yang "tidak ikut tema" saat pengguna mengganti tema.

## Tiga lapis warna

| Lapis | Berkas | Isi | Berubah saat tema diganti? |
|---|---|---|---|
| **Palet tetap** | `SuDesApp.Wpf/Themes/PaletTetap.xaml` | Warna yang memang harus begitu: kertas pratinjau, latar QR, tirai modal, warna merek Google, mockup peramban/terminal | ❌ tidak |
| **Tema** | `Themes/{Green,Light,Dark,Blue,Pink,Slate,HighContrast,Orbit,Ember,Kanagawa,Blossom}Theme.xaml` | Warna identitas aplikasi: `PrimaryBrush`, `BackgroundBrush`, `PanelBrush`, `TextBrush`, status (`Success`/`Warning`/`Error`), aksen | ✅ ya, ditukar `ThemeService` |
| **Gaya** | `Themes/ThemeStyles.xaml` + kamus bersama (`NavKiri`, `NotifikasiInline`, `KartuCatatanRilis`) | Ukuran, jarak, template kontrol, gaya halaman | — (memakai dua lapis di atas) |

Urutan muat dijaga oleh `PemuatanTema.BerkasDasar` (dipakai `App.xaml`,
uji tampilan, dan alat render):

1. `PaletTetap.xaml`
2. `ThemeStyles.xaml` (di dalamnya palet dimuat lagi agar kamus ini bisa
   berdiri sendiri)
3. tema aktif (`GreenTheme.xaml` saat mulai)
4. kamus bersama

## Batas tanggung jawab tema

- `Services/ThemeCatalog.cs` memiliki daftar tema yang tidak berubah dan
  metadata tampilannya: nama, label, swatch pemilih, validasi, serta nama
  resource tema yang sesuai. Tambahkan definisi tema satu kali di sini; jangan
  membuat daftar tema kedua di layanan.
- `Services/ThemeService.cs` memiliki pilihan tema pengguna dan siklus hidupnya:
  membaca/menulis `theme.config`, menyelesaikan **Auto** berdasarkan Windows,
  mengganti kamus aktif aplikasi, dan memberi tahu jendela setelah perubahan.
  Fakta tema diambil dari katalog, bukan disalin ke tabel metadata lain.
- `Themes/PemuatanTema.cs` memiliki pembuatan pack URI absolut dan pemuatan
  kamus dasar berurutan untuk aplikasi serta alat render. Tema aktif tetap
  disimpan dalam koleksi resource aplikasi; nilai `Current` di layanan adalah
  preferensi pilihan (jadi pilihan **Auto** tetap terlihat sebagai pilihan).

Alur data bergerak dari katalog ke pemilih dan layanan. Layanan membaca
preferensi, menentukan tema efektif, lalu memperbarui kamus resource aplikasi.
Tampilan memakai dynamic resource; tampilan tidak memiliki atau menyalin status
tema.

## Aturan menulis warna

1. **Jangan menulis warna literal** (`#RRGGBB`, `White`, `Black`, …) di XAML
   tampilan. Pilih salah satu:
   - warna yang harus ikut tema → kunci dari `*Theme.xaml`;
   - warna yang memang tetap → kunci dari `PaletTetap.xaml`.
2. **Kunci tema wajib `DynamicResource`**, bukan `StaticResource`.
   `StaticResource` disalin sekali saat XAML dimuat, jadi bagian itu tetap
   memakai warna tema lama setelah pengguna menukar tema.
3. **Kunci palet tetap boleh `StaticResource`** di dalam `Themes/…` (palet
   sudah dimuat lebih dulu), dan `DynamicResource` di tampilan.
4. **`Transparent` bukan warna** — boleh dipakai langsung (artinya "tidak
   mengisi").
5. **Jangan membuat kuas dari kode** untuk warna yang harus ikut tema. Ambil
   dari kamus: `TryFindResource("KertasBrush")`, dan sediakan cadangan hanya
   untuk keadaan kamus belum dimuat (mis. alat render).

## Menambah warna baru

- **Ikut tema?** Tambahkan kunci yang sama ke **sebelas** berkas
  `*Theme.xaml` (nama identik, nilai boleh beda). Uji
  `SemuaTema_MendefinisikanKunciYangSama` gagal bila ada tema yang ketinggalan
  — inilah yang mencegah "bagian tertentu terang di tema gelap".
- **Tetap?** Tambahkan ke `PaletTetap.xaml` beserta komentar **mengapa** warna
  itu tidak boleh mengikuti tema. Kalau ragu, masukkan ke tema, bukan ke palet.

## Menambah tema baru

1. Buat `Themes/NamaTheme.xaml` dengan kunci yang sama seperti tema lain.
   Untuk tema adaptasi preset desain (Orbit, Ember, Kanagawa, Blossom),
   jangan tulis tangan: jalankan `py Tools/gen-tema-preset.py` — generator
   itu menulis keempat berkas tema dari palet preset asli dengan kunci yang
   sama persis seperti tema bawaan (termasuk `AppFontFamily`).
   Warna teks sekunder/status dan aksen sidebar TIDAK ditulis tetap: generator
   menggelapkan/menerangkan warna hasil turunan sampai lolos WCAG AA di semua
   permukaan tema (kartu, panel, zebra, chip status, sidebar). Menambah latar
   baru berarti menambahkannya ke daftar `latar` di generator dan ke
   `Tools/check-kontras-wcag.ps1` (daftar tema di alat itu memuat kesebelas
   tema — termasuk keempat preset — sehingga preset tidak lolos dari audit).
2. Tambahkan satu entri ke `Services/ThemeCatalog.cs` dengan nama, label, dan
   warna swatch. `ThemeService` mengambil daftar serta nama resource dari sana,
   lalu `PemuatanTema` membentuk pack URI; jangan menambahkan daftar tema kedua.
3. Uji `SetiapTemaDiLayanan_PunyaBerkasDanSebaliknya` memastikan daftar layanan
   dan berkas tidak berbeda.

## Gerbang CI wajib (setiap pull request)

`.github/workflows/ci.yml` punya job **Audit kontras tema** yang berjalan pada
setiap `pull_request` (dan push) di `windows-latest`. Dua langkahnya sengaja
dipisah karena menangkap kelas kesalahan yang berbeda:

| Langkah | Memeriksa | TIDAK menangkap |
|---|---|---|
| `Tools/check-kontras-wcag.ps1` | pasangan token teks/latar di **berkas tema** (AA; AAA untuk HighContrast) + konsistensi kunci antar sebelas tema | teks yang kehilangan `Foreground` karena `Style` eksplisit, atau warna warisan yang tertimpa gaya implisit |
| `dotnet test --filter JelajahRenderTemaTests` | teks yang **benar-benar dirender**: setiap `TextBlock` di semua halaman × sebelas tema diukur terhadap latar efektifnya | pasangan token yang belum dipakai halaman mana pun |

Job ini adalah **job terpisah**, bukan langkah di dalam `build-test`, supaya punya
nama status sendiri. Agar benar-benar mengikat, pemilik repo menandainya sebagai
*required status check* di **Settings → Branches → Branch protection rules**
(centang `Audit kontras tema`). Tanpa penandaan itu job tetap berjalan dan merah,
tetapi GitHub belum menolak merge otomatis; workflow sendiri tidak bisa
memaksa branch protection karena itu pengaturan repo, bukan berkas.

Penjaga di dalam repo memastikan gerbang ini tidak hilang diam-diam:
`SuDesApp.Tests/AuditKontrasCiTests.cs` gagal bila job-nya dihapus/diganti nama,
langkah palet atau render hilang, urutannya terbalik, langkahnya dilunakkan
dengan `continue-on-error`, atau pemeriksa palet berhenti keluar dengan kode 1.

## Penjaga otomatis

`SuDesApp.Tests/TemaKonsistenTests.cs` dijalankan pada setiap `dotnet test`:

| Uji | Mencegah |
|---|---|
| `SemuaTema_MendefinisikanKunciYangSama` | tema yang ketinggalan kunci → bagian tampil dengan warna bawaan WPF |
| `SetiapTema_RujukanWarnanyaSendiriLengkap` | salah ketik nama `*Color` di dalam tema |
| `Tampilan_TidakMemakaiWarnaLiteral` | warna literal di XAML tampilan (termasuk bentuk `<Setter Value="#…">`) |
| `KunciWarnaYangDirujuk_TersediaDiTema_AtauPalet` | kunci warna yang dirujuk tetapi tidak pernah didefinisikan |
| `KunciMilikTema_TidakDirujukDenganStaticResource` | kunci tema yang "membeku" pada tema saat dimuat |
| `KunciResourceDiKodeCSharp_Tersedia` | nama kunci salah di `TryFindResource`/`FindResource` |
| `PaletTetap_DimuatSebelumTemaDanStyles` | urutan muat yang membuat palet tertimpa atau tak terbaca |
| `DaftarKamusDiApp_SamaDenganPemuatanTema` | `App.xaml` dan daftar bersama berbeda (uji merender aplikasi yang lain) |
| `SetiapTemaDiLayanan_PunyaBerkasDanSebaliknya` | tema terdaftar tanpa berkas, atau berkas yang tak bisa dipilih |
| `SetiapTema_BisaDimuatTanpaGalatResource` | `StaticResource` salah ketik di dalam tema (baru gagal saat BAML dimuat) |
| `GantiTema_MengubahWarnaTema_TetapiTidakWarnaTetap` | kuas tema yang tidak ikut berganti saat kamus tema ditukar, atau palet yang ikut berubah |
| `JelajahRenderTemaTests.SemuaHalaman_DiSemuaTema_TidakMemakaiChromeBawaanWindows` | kendali chrome yang jatuh ke template bawaan WPF di halaman mana pun — uji jelajah merender semua halaman DataTemplate App.xaml pada sebelas tema (± 10 ribu kendali diperiksa) dan membandingkan identitas template terhadap patokan bawaan |
| `JelajahRenderTemaTests.SemuaHalaman_DiSemuaTema_TeksnyaTerbaca` | teks yang gagal WCAG AA saat DIRENDER — setiap `TextBlock` di semua halaman × sebelas tema diukur terhadap latar efektif di belakangnya (menelusuri Border/Panel/Control), jadi menangkap teks hitam di atas latar gelap, teks putih di atas latar terang, dan warna warisan yang tertimpa gaya implisit — hal yang tidak terlihat oleh pemeriksaan pasangan warna tema |
| `FontAntarmukaTests` | font Google Sans hilang/rusak di resource rakitan, muka Bold asli hilang (faux bold), atau satu tema memakai keluarga font berbeda |
| `UkuranHurufStandarTests` | literal FontSize di luar skala baku (10/11/13/14/15/20/24/28) menyebar ke tampilan — diperiksa dalam tiga bentuk: atribut, `<Setter Property="FontSize" …>`, dan literal C#, di seluruh proyek WPF |
| `GayaKpiDanJudulTerpusatTests` | `KpiValue`, `KpiLabel`, `KpiCard`, `KpiTile`, `KpiIconChip`, `SectionTitle`, atau `HeaderTitle` disalin lagi ke halaman, sehingga rupa KPI/judul antar halaman menyimpang tanpa build gagal |
| `PemilihTemaTests` | baris tema kehilangan titik swatch, slot ikon kosong menyisakan celah, atau tanda centang tema aktif tidak tampil |

## Kendali yang tidak punya gaya implisit di WPF

Beberapa kendali tetap memakai chrome Aero2 walau properti `Background`,
`Foreground`, dan `BorderBrush` sudah disetel, karena template bawaan mereka
yang menggambar warna sendiri. Dua di antaranya sudah ditangani di
`ThemeStyles.xaml`:

| Kendali | Gejala di tema gelap | Penanganan |
|---|---|---|
| `ListView` + `GridView` (tabel halaman IDM & APBDES) | kepala kolom bergradien perak dengan teks hitam, baris memakai `#042271` dan sorotan seleksi biru Windows | gaya `ListView`, `ListViewItem`, dan `GridViewColumnHeader` bergaya tema (termasuk `PART_HeaderGripper`) |
| `DatePicker` / `DatePickerTextBox` | kotak tanggal bergradien putih berbingkai perak, tombol kalender berupa chip terang | template tema penuh; `PART_TextBox`, `PART_Button`, `PART_Popup`, `PART_ContentHost`, dan `PART_Watermark` dipertahankan |

Dijaga uji `TabelListViewTemaTests` dan `PemilihTanggalTemaTests`: tabel dan
kolom tanggal benar-benar dirender, warna diambil dari tema, dan ikut berganti
saat tema ditukar di runtime.

**Kepala kolom `DataGrid`** juga memakai latar `PanelBrush` + `TextBrush`,
seperti kepala kolom `GridView` — bukan `PrimaryBrush` + teks putih. Alasannya:
gaya implisit `TextBlock` (`Foreground = TextBrush`) menang atas `Foreground`
yang diwariskan kepala kolom, jadi teks putih di kepala berwarna berubah menjadi
warna teks tema; di tema terang hasilnya hitam di atas biru tua, di preset
seperti Orbit/Ember putih di atas primary setengah gelap (3,5–3,8:1). Latar
netral membuat warna kepala tidak lagi bergantung pada palet aksen dan aman di
semua tema. Hover memakai `AccentSubtleBrush`, bukan `AccentBrush`, karena teks
kepala tetap `TextBrush`.

Uji jelajah `JelajahRenderTemaTests` menemukan dan memperbaiki beberapa kasus
serupa: gaya lokal tanpa `BasedOn` (tombol kepala kolom `TabelKepala` di kedua
halaman register, kotak cari `RiwayatView`, `ProgressBar` impor `WargaView`),
kotak isian editable ComboBox yang `Style`-nya di-set nol, dan `Expander`
tanpa template (tombol header chrome sistem di halaman Panel WA). Kendali
wadah polos (`ScrollViewer`, `ListBox`, `ListView`, `TabControl`, `DataGrid`,
`GridSplitter`, …) sengaja tidak ikut diperiksa: template bawaannya hanya
Border/penyaji dengan `TemplateBinding`, jadi warna tema tetap tembus dan
chrome terlihat justru dari bagian yang digayakan.

## Font antarmuka & skala ukuran huruf

- **Font**: Google Sans — font yang sama dengan yang dipakai antarmuka
  modern. Berkas `SuDesApp.Wpf/Fonts/GoogleSans.ttc` (subset latin,
  SIL OFL 1.1, `Fonts/OFL.txt` ikut ditanam) berisi dua muka asli: Regular
  (400) dan Bold (700), dirakit oleh `Tools/embed-google-sans.py` dari subset
  WOFF2 font aslinya. Semua tema mendefinisikan
  `AppFontFamily` = rantai `GoogleSans.ttc#Google Sans, Segoe UI Variable,
  Segoe UI` (cadangan bila glyph tidak tercakup subset); MainWindow dan
  LoginWindow memakainya lewat `DynamicResource`.
- **Skala ukuran huruf baku** (diadaptasi dari skala tipografi antarmuka modern):
  10 (caption), 11 (label), 13 (body kecil), 14 (pesan/kontrol), 15 (judul
  kecil), 20 (judul), 24 (angka besar), 28 (tampilan). Nilai di atas 28
  hanya untuk emoji/piagam, nilai di bawah 5 untuk pemisah hairline.
  Skrip `Tools/standarkan-ukuran-huruf.py` men-standard-kan literal lama;
  uji `UkuranHurufStandarTests` menjaga agar tidak ada ukuran baru di luar
  skala (11.5, 12.5, 13.5, dst. dilarang).

  Nilai yang ditulis di dalam Style — `<Setter Property="FontSize" Value="12"/>`
  — dulu lolos dari penjaga karena hanya bentuk atribut yang dibaca, sehingga
  satu halaman bisa memakai hierarki huruf yang berbeda dari halaman lain
  tanpa ada uji yang gagal. Sejak audit 2026-10-19 penjaga membaca ketiga
  bentuk itu di **seluruh** `SuDesApp.Wpf` (termasuk `App.xaml` dan jendela di
  root), bukan daftar folder tetap; skrip standariser juga menulis ulang
  bentuk Setter. 34 nilai di 11 berkas (12 / 12.5 / 11.5 / 10.5 / 16 / 17 /
  22) dipetakan ke skala dengan skrip yang sama.

### Gaya KPI & judul section: satu kamus

Gaya yang dipakai lebih dari satu halaman tinggal di
[`ThemeStyles.xaml`](../SuDesApp.Wpf/Themes/ThemeStyles.xaml), bukan di
`UserControl.Resources` masing-masing halaman:

| Gaya | Dipakai | Catatan |
|---|---|---|
| `KpiLabel` / `KpiValue` | Daftar Hadir, Langganan | label 11 bold + membungkus; angka 15 semibold + membungkus |
| `KpiCard` / `KpiTile` | Daftar Hadir / Langganan | kartu berbayang di atas latar halaman; petak datar di dalam kartu |
| `KpiIconChip` | KPI & kepala section kedua halaman | chip 36×36, sudut 8, latar aksen lembut |
| `SectionTitle` | halaman data, Tentang Aplikasi, Panduan WA | 14 bold, membungkus, sejajar tengah |
| `SectionTitleLarge` | Langganan | turunan `SectionTitle` dengan 15 |
| `HeaderTitle` / `HeaderSubtitle` | seluruh kepala halaman | sudah lama terpusat; tidak berubah |

Alasannya nyata: sebelum penyatuan, Daftar Hadir memakai angka **Bold** tanpa
membungkus sementara Langganan memakai **SemiBold** dengan margin, dan
`SectionTitle` disalin di tiga berkas. Perbedaan sekecil itu tidak pernah
gagal saat dibangun, tetapi membuat dua halaman terlihat berasal dari aplikasi
berbeda. `GayaKpiDanJudulTerpusatTests` sekarang menolak salinan lokal baru
untuk kesembilan kunci itu, sekaligus memastikan halaman yang dulu menyalin
memang memakai versi bersama (bukan berhenti memakai gayanya).

`FormSectionTitle` (128 pemakaian di form input) tetap ada sebagai nama lama,
tetapi kini hanya turunan `SectionTitle` — bukan definisi kedua yang bisa
menyimpang.

## Pemilih tema visual

Menu **Tema** di sidebar (akordeon di bagian BANTUAN) memakai pemilih tema
visual: setiap baris tema menampilkan titik swatch warna aksen
paletnya (`ThemeService.WarnaSwatch`), nama tema, dan tanda centang di ujung
kanan untuk tema yang sedang aktif. Centang digerakkan oleh `NavItem.IsTemaAktif`
saat item diklik (`MainWindowViewModel.PilihTema`). Karena swatch
mewakili tema LAIN, warnanya disimpan sebagai metadata di `ThemeCatalog`
— bukan kunci tema (mengikuti tema aktif justru membuat titik itu tidak
menggambarkan palet yang diwakilinya). Dijaga uji `PemilihTemaTests`.

## Batas yang diketahui

- **Dokumen Markdown** (halaman Dokumentasi) disusun menjadi `FlowDocument`
  saat halaman dibuka. Warna diambil dari tema saat itu; tema yang diganti
  ketika dokumen sedang terbuka baru terlihat setelah dokumen dibuka ulang.
- **Pratinjau PDF** memakai warna kertas tetap (putih) — memang disengaja,
  karena halaman dicetak di kertas putih.
- **Ikon jendela/taskbar** dibuat dari `AccentColor` tema aktif dan dibuat
  ulang saat tema berubah; bila resource belum tersedia, dipakai hijau baku.
- **Cadangan (fallback)** di kode view hanya berlaku bila kamus gagal dimuat
  (mis. alat render tanpa `App.xaml`); pada aplikasi biasa kunci tema selalu
  ditemukan.
- **Uji kontras render** memeriksa halaman yang terdaftar sebagai `DataTemplate`
  di `App.xaml`. Jendela mandiri (login, dialog input/pesan, splash) tidak ikut
  dirender uji itu; warnanya tetap dijaga pemeriksaan pasangan palet dan uji
  tampilan masing-masing.
