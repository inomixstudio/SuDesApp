# Audit: Sistem Tema & Implementasinya

Catatan internal (akar repo, **bukan** `docs/`) — dijalankan 1 Oktober 2026 atas permintaan
pemelihara. Setiap temuan bertingkat **Tinggi/Sedang/Rendah**, yang sudah dikerjakan ditandai
**BERES** beserta buktinya. Nomor baris mengikuti keadaan **sesudah** perbaikan.

Ruang lingkup: tujuh tema berwarna (`Themes/{Light,Dark,Green,Blue,Pink,Slate,HighContrast}Theme.xaml`),
kamus gaya bersama (`ThemeStyles.xaml`, `NavKiri`, `NotifikasiInline`, `KartuCatatanRilis`),
`ThemeService` yang menukar kamus warna, dan cara XAML memakai token warna — termasuk alat
audit yang sudah ada, `Tools/check-kontras-wcag.ps1`.

Satu kebenaran dasar yang dipakai seluruh audit ini: **token `XxxBrush` untuk latar dan token
`OnXxxTextBrush` untuk teks di atasnya adalah dua hal berbeda.** Setiap kegagalan di bawah
bermula dari salah satu dari dua hal ini — token `on` tidak ada, atau warna isian dipakai
sebagai warna teks.

## Ringkasan

| # | Temuan | Tingkat | Status |
|---|--------|---------|--------|
| T1 | Tiga token `OnSuccess/OnWarning/OnErrorTextBrush` yang **dituntut alat audit sendiri** tidak ada di satu pun tema: 18 pemeriksaan selalu gagal, skrip tidak pernah bisa dilaporkan lolos | Tinggi | BERES |
| T2 | Teks putih kaku `Foreground="White"` di atas brush tema di 8 berkas — kontrasnya salah di beberapa tema dan tidak ikut berganti tema | Tinggi | BERES |
| T3 | `SuccessButton`/`DangerButton` memakai `OnAccentTextBrush` (token latar aksen) untuk latar sukses/galat; di DarkTheme itu putih di atas hijau terang (2,28:1) | Sedang | BERES |
| T4 | Duplikat `OnAccentTextBrush` di `ThemeStyles.xaml` — dua sumber kebenaran untuk satu token; kalau urutan kamus berubah, seluruh tema langsung kembali putih diam-diam | Sedang | BERES |
| T5 | **Nomor langkah Panduan WhatsApp tidak terlihat**: teks lencana berwarna `AccentBrush` di atas lencana `AccentBrush` — teks sejajar warna latarnya (rasio 1:1) | Tinggi | BERES |
| T6 | Nomor langkah wizard Pengaturan Aplikasi putih statis di atas lingkaran abu-abu saat langkah belum aktif — putih di atas abu muda tidak terbaca | Sedang | BERES |
| T7 | Token teks status (`SuccessText/WarningText/ErrorText`) memakai **warna isian** di 4 tema: rasio 2,6–4,4:1 (Green: 3,00 di badge, 3,19 di kartu) | Tinggi | BERES |
| T8 | Subjudul header di atas gradien terlalu terang: Light 3,76 · Green 4,09 · Pink 3,68 · **Dark 1,55** | Tinggi | BERES |
| T9 | Tombol primer DarkTheme: putih di atas `#059669` = 3,77:1 | Sedang | BERES |
| T10 | Warna aksen dipakai sebagai warna teks lewat `AccentBrush` (isian) di 12 tempat — tab terpilih, tautan hover, ikon kartu: 3,1–4,0:1 | Sedang | BERES |
| T11 | Teks sekunder dan chip status di dua tema nyaris gagal: Pink 4,08–4,48 · Slate chip 3,79 | Sedang | BERES |
| T12 | Alat audit sendiri tidak pernah berjalan (butuh `pwsh`, tidak ada di mesin ini) sehingga 47 pelanggaran menumpuk tanpa terdeteksi; tidak ada uji otomatis yang menggantikannya | Tinggi (kelas bug) | BERES + penjaga |
| T13 | Putih/hex tetap yang **memang disengaja** (avatar Google, pratinjau kertas, PDF, kode terminal panduan) — dicatat dan dikecualikan tertulis di uji | Rendah | tercatat |
| T14 | Daftar pasangan di alat tidak mewakili kode sebenarnya: tombol primer memakai `SecondaryBrush` tapi alat menguji `PrimaryBrush`; teks aksen dipakai di latar halaman & zebra tapi tidak diuji | Sedang | BERES |
| T15 | Putaran kedua (1 Oktober 2026): **tidak ada sisa** dari 47 temuan — ke-18 label pelanggaran itu sudah ada di gerbang. Celah sebenarnya di sisi lain: 10 pasangan warna yang dipakai XAML tetapi tidak pernah diperiksa skrip maupun uji (bilah status Google Drive, galat di jendela Masuk/Data Warga, teks status di latar halaman, teks di kartu peringatan, judul header tanpa latar) | Sedang | BERES |
| T16 | Permintaan pemelihara: **tema ketujuh opsional "Kontras Tinggi"** yang memenuhi WCAG AAA. Gerbang kontras diperluas dengan ambang per tema, bukan daftar terpisah | Fitur | BERES |

---

## 1. Token `on` tidak pernah ada (T1)

| Kode | Temuan | Bukti |
|------|--------|-------|
| T1 | `check-kontras-wcag.ps1` mewajibkan pasangan `OnSuccessTextBrush`↔`SuccessBrush`, `OnWarningTextBrush`↔`WarningBrush`, `OnErrorTextBrush`↔`ErrorBrush` pada keenam tema — ketiga token tidak didefinisikan di mana pun (`grep` 0 temuan sebelum perbaikan). Karena skrip berhenti pada `kehilangan brush`, 18 pemeriksaan itu mustahil lolos. | `Tools/check-kontras-wcag.ps1:136-138` |

**Perbaikan:** ketiga token ditambahkan ke **enam** tema dengan nilai yang dihitung agar lolos
4,5:1 terhadap latarnya masing-masing (hijau/merah cerah butuh tinta gelap `#0F172A`, hijau
tua Light dan merah terang butuh putih). Nilainya berbeda per tema karena latarnya berbeda —
itulah gunanya token per tema, bukan satu warna global. Contoh: `OnWarningTextBrush` = `#0F172A`
di semua tema (5,4–5,7:1 di atas amber), `OnSuccessTextBrush` = `#FFFFFF` hanya di Light
(`#2E7D32` gelap, putih 5,13:1).

## 2. Teks putih kaku (T2, T3, T5, T6)

Delapan berkas menulis `Foreground="White"` langsung di atas latar yang warnanya berasal dari
tema — putih itu tetap putih saat tema berganti, latarnya tidak:

| Berkas | Elemen | Masalah nyata |
|--------|--------|---------------|
| `MainWindow.xaml` | Badge notifikasi belum dibaca (`ErrorBrush`) | Dark/Slate `#EF4444` + putih = 3,76 |
| `KartuCatatanRilis.xaml` | Lencana `TERBARU` (`AccentBrush`) | Slate amber `#F59E0B` + putih = 3,32 |
| `MessageDialogWindow.xaml` | Ikon dialog (`PrimaryBrush`) | putih `OnPrimary` sudah ada, tidak dipakai |
| `PanduanWa/Bagian{B,C,D}View` | Ikon centang/seru/peringatan | amber + putih = 3,18 |
| `RegisterSuratView` + `RegisterNtcrView` | Lencana & tombol filter Draft (`WarningBrush`) | amber + putih = 3,18 |
| `PengaturanAplikasiView` | Label `ON` toggle + nomor langkah wizard | putih di atas `BorderBrush` (abu muda) nyaris tak terbaca |
| `PanduanWa/PanduanWaStyles.xaml` (T5) | `StepBadgeText` `Foreground=AccentBrush` di atas `StepBadge` `Background=AccentBrush` | **teks sejajar latarnya — nomor 1/2/3 hilang** |
| `ThemeStyles.xaml` (T3) | `SuccessButton`/`DangerButton` memakai token aksen | salah semantik; Dark `#22C55E` + putih = 2,28 |

**Perbaikan:** semua diganti ke token `On*` yang sesuai (`OnError`, `OnAccent`,
`OnSuccess`, `OnWarning`, `OnPrimary`). Langkah wizard diberi warna teks **berdasarkan
keadaan**: `TextSecondaryBrush` saat belum aktif, `OnAccentTextBrush` saat aktif/selesai
(awalnya putih statis, jadi nomor langkah yang belum aktif hilang di atas lingkaran abu).
Karena token `on` kini nilainya dihitung per tema, teks-teks itu ikut benar sendiri saat
operator mengganti tema — tanpa menyentuh XAML halaman.

## 3. Token kembar & isian dipakai sebagai teks (T4, T10)

| Kode | Temuan | Bukti |
|------|--------|-------|
| T4 | `ThemeStyles.xaml` ikut mendefinisikan `OnAccentTextBrush` `Color="White"`, padahal keenam tema juga mendefinisikannya. Kamus tema dicari *sebelum* `ThemeStyles` (urutan `App.xaml` MergedDictionaries), jadi duplikat itu mati — tetapi menutupi nilai tema begitu urutannya berubah, dan komentar di berkas itu sendiri menjanjikan "semua warna memakai DynamicResource". | `ThemeStyles.xaml:16` (baris sudah dihapus) |
| T10 | 12 tempat memakai `Foreground="{DynamicResource AccentBrush}"` — **isian** — padahal token teks sudah ada dan dipakai di 12 tempat lain: `AccentTextBrush`. Tab terpilih, hover tautan di `App.xaml`, ikon & teks kartu, tanda bintang formulir. | lihat daftar di §5 |

**Perbaikan:** duplikat `OnAccentTextBrush` dihapus dari `ThemeStyles.xaml`; dua belas
pemakaian teks dialihkan ke `AccentTextBrush`. Pemakaian `Foreground=AccentBrush` yang
**bukan** teks — isian `ProgressBar`, `Slider`, `ScrollBar` — sengaja dibiarkan: di sana
`Foreground` memang berarti warna isi, dan menggantinya justru merusak.

## 4. Angka palette yang terlalu terang untuk teksnya (T7, T8, T9, T11)

`SuccessText/WarningText/ErrorTextBrush` di Light/Green/Blue/Pink hanyalah alias
`{StaticResource SuccessColor}` — yaitu warna **isian** badge. Hasilnya kegagalan sistematis
di kartu putih dan chip pastel (badge Green 3,00; chip Blue 2,61; badge Light 2,81). Subjudul
header (putih 85%) dan tombol primer Dark punya masalah sejenis.

**Perbaikan** (semua nilai dihitung otomatis terhadap seluruh pasangan yang harus dipenuhi
token itu, lalu diverifikasi ulang oleh uji):

| Tema | Token | Lama → Baru | Alasan |
|------|-------|-------------|--------|
| Light | `PrimaryColor`/`SecondaryColor` | `#1976D2`/`#1565C0` → `#1565C0`/`#0D47A1` | subjudul putih 85% perlu latar satu tingkat lebih gelap |
| Light | `SuccessText`/`WarningText` | `#2E7D32`/`#ED6C02` → `#2D7A31`/`#AC4F01` | 4,49 dan 2,73 → 4,67 dan 4,75 |
| Green | `PrimaryColor`/`SecondaryColor` | `#15803D`/`#166534` → `#166534`/`#14532D` | subjudul 4,09 → lolos |
| Green | `SuccessText`/`WarningText`/`ErrorText` | `#16A34A`/`#D97706`/`#DC2626` → `#117F3A`/`#A15904`/`#C92020` | 3,00/2,86/3,95 → 4,64/4,78/4,63 |
| Blue | `SuccessText`/`WarningText`/`ErrorText` | → `#107435`/`#995404`/`#C92020` | chip 2,61 → 4,75+ |
| Pink | `PrimaryColor`/`SecondaryColor` | `#DB2777`/`#BE185D` → `#BE185D`/`#9D174D` | subjudul 3,68 + tautan aksen 3,91 |
| Pink | `TextSecondaryColor` + teks status | `#8F6680` → `#835E75`, status → `#107836`/`#9D5604`/`#C92020` | 4,08 → 4,67 dst. |
| Dark | `PrimaryColor`/`SecondaryColor` | `#059669` → `#047857`/`#065F46` | tombol primer putih 3,77 → 5,48 |
| Dark | `HeaderSubtitleBrush` | `#A6A6A6` → `#D1FAE5` | subjudul **1,55** → 4,83 |
| Slate | `HeaderSubtitleBrush`/`TextSecondaryColor` | `#94A3B8`/`#9AA9BD` → `#A3B0C2`/`#B0BCCC` | 4,04/3,79 → 4,71 |
| Dark | `TextSecondaryColor` | `#9E9E9E` → `#BDBDBD` | teks sekunder di atas kartu sukses/peringatan/galat 3,40/3,39/3,74 → 4,85/4,83/5,33 |
| Pink | `ErrorSubtleColor` | `#FEE2E2` → `#FEEAEA` | teks sekunder di kartu galat 4,497 → 4,753 |
| Blue | `AccentLightColor` | `#60A5FA` → `#93C5FD` | teks aksen terang di atas `#1E3A8A` 4,07 → 5,74 |
| Pink | `AccentLightColor` | `#F9A8D4` → `#FCE7F3` | 3,33 → 5,14 |

Perubahan yang paling terasa hanya **header gradien** di Light/Green/Pink/Dark (satu tingkat
lebih gelap) dan nomor langkah wizard yang akhirnya terbaca. Isian badge, tombol, dan aksen
tidak berubah — yang berubah adalah warna *tulisan* di atasnya.

## 5. Daftar sebelum → sesudah (T2, T10)

```text
App.xaml                       2 tautan hover            AccentBrush → AccentTextBrush
KartuCatatanRilis.xaml         lencana TERBARU, poin     putih/Accent → OnAccent/AccentText
ThemeStyles.xaml               SuccessButton, DangerButton, header Expander, tab terpilih
MainWindow.xaml                badge notifikasi          putih → OnErrorTextBrush
MessageDialogWindow.xaml       ikon dialog               putih → OnPrimaryTextBrush
PanduanWa/BagianB,C,DView      ikon centang/seru/warning putih → OnSuccess/OnWarningTextBrush
PanduanWa/PanduanWaStyles      StepBadgeText (T5)        Accent → OnAccentTextBrush
RegisterSurat/RegisterNtcr     lencana & filter Draft    putih/OnAccent → OnWarningTextBrush
PengaturanAplikasiView         label ON, 3 nomor langkah putih → OnAccent + keadaan teks
DokumentasiView (2), IjinTinggal, NtcrPaket
                               teks/ikon aksen           AccentBrush → AccentTextBrush
```

Putaran kedua (T15), daftar sebelum → sesudah:

```text
PerangkatDesaView              kepala halaman tanpa latar   → dibungkus PageHeader
GoogleDriveView:101            bilah status             TextSecondary → StatusBarTextBrush
LoginWindow, WargaView         pesan galat              ErrorBrush → ErrorTextBrush
PembaruanView                  pesan galat              ErrorBrush → ErrorTextBrush
PembaruanView                  lencana                   PrimaryBrush → AccentTextBrush
DokumentasiView                lencana                   BorderBrush → PanelBrush
IsiTemplateSuratView, BerandaView, TemplateSuratWizardView (3×),
MainWindow:712, TutupBukuTahunView
                               teks/angka aksen         PrimaryBrush → AccentTextBrush
TutupBukuTahunView             baris "tidak ada catatan" TextSecondary → TextBrush
SetelanView (2×)               subjudul di kartu         putih → TextSecondaryBrush
PengaturanAplikasiView:308     butir navigasi mock       SidebarText → TextBrush
WordImporWindow                keterangan kertas         TextSecondary → #4B5563 (tetap)
```

## 6. Penjaga otomatis (T12, T14)

`tests/SuDesApp.Core.Tests/KontrasTemaTests.cs` — 7 uji, meniru aturan skrip PowerShell
sehingga `dotnet test` menjadi gerbangnya (pwsh tidak ada di mesin build):

1. **49 pasangan × 7 tema** dihitung sendiri (resolusi token, komposit opasitas, rasio WCAG)
   — gagal kalau ada satu pun di bawah ambangnya (AA untuk enam tema, AAA untuk tema
   kontras tinggi — lihat §10).
2. **Daftar pasangan diuji identik dengan daftar di skrip** — skrip dan uji tidak boleh
   melenceng satu sama lain (T14).
3. Paritas kunci antar ketujuh tema (kunci hilang/lebih = halaman tampil beda diam-diam).
4. Kelima token `On*TextBrush` wajib ada di semua tema (T1 tidak bisa kambuh).
5. Setiap `{DynamicResource X}` di seluruh XAML wajib terdefinisi — salah ketik token tidak
   pernah dilaporkan WPF, nilainya hanya kosong dan tampilan diam-diam jadi putih.
6. Teks putih kaku di seluruh XAML hanya boleh di daftar pengecualian yang beralasan
   (avatar Google `#4285F4`, warna merek) — T2 tidak bisa kambuh.

Tiga pasangan baru ditambahkan ke alat **dan** uji karena kode memakainya tetapi alat tidak mengujinya (T14): tombol primer di atas `SecondaryBrush` (bentuk gradien yang benar-benar dipakai `PrimaryButton`), serta teks aksen di atas latar halaman dan baris zebra.

Putaran kedua (T15) menambah sepuluh pasangan penjaga, semuanya karena warna itu benar-benar
dipakai XAML tetapi belum masuk daftar:

| Pasangan baru | Ditemukan di |
|---------------|--------------|
| Teks utama di kartu peringatan (`TextBrush`/`WarningSubtleBrush`) | baris "tidak ada catatan" `TutupBukuTahunView` |
| Peringatan di panel (`WarningTextBrush`/`PanelBrush`) | kotak info di `PengaturanAplikasiView` |
| Galat di panel (`ErrorTextBrush`/`PanelBrush`) | pesan galat di dalam kartu |
| Peringatan / Sukses / Galat di latar halaman (…/`WindowBackgroundBrush`) | teks status langsung di atas latar halaman |
| Teks aksen sidebar (`SidebarAccentBrush`/`SidebarBackgroundBrush`) | kepala sidebar `MainWindow` |
| Teks sekunder di kartu sukses / peringatan / galat (`TextSecondaryBrush` × `Success/Warning/ErrorSubtleBrush`) | keterangan di dalam kartu status berwarna |

Seluruh pasangan berambang 4,5. Karena daftar ini dijaga identik oleh uji ke-2, menambah
pasangan **wajib** dikerjakan di kedua berkas sekaligus — kalau tidak, gerbang dan skrip
langsung berselisih pada saat `dotnet test`.

## 7. Yang sengaja dibiarkan (T13)

- **Avatar Google** `AboutView` `#4285F4` + huruf putih: warna merek, bukan token tema; sudah
  tercatat di keterangan skrip dan jadi satu-satunya pengecualian uji teks putih.
- **Pratinjau kertas kop surat** (`SetelanView` `#FFFFFF` + `#111827` + `#D7DDE5`): kertas
  tetap putih di tema apa pun — justru salah kalau ikut tema. Amber `#92400E`/`#FEF3C7` di
  dalamnya memakai angka yang sama dengan token `Warning*` agar seragam antar tema.
- **Latar PDF** `#525659`, **kode terminal** panduan `#7EE787` di atas kotak gelap, **titik
  jendela macOS** `#FF5F57/#FEBC2E/#28C840`, **dekorasi login** putih `Opacity 0,07`:
  semuanya latar tetap, bukan teks di atas warna tema.
- **`Fill="White"` pada ibu jari toggle** dan **`#12FFFFFF`-`#33FFFFFF`**: putih di atas
  aksen memang konvensi (karena `OnAccentTextBrush` kini `#0F172A` di empat tema, tinta
  gelap di latar aksen terang — lihat catatan §8).

## 8. Alarm palsu penelusur — jangan dikejar lagi

Selain gerbang otomatis, putaran kedua memakai penelusur XAML sekali pakai (membaca gaya
dari **semua** XAML, mengikuti `BasedOn` dan gaya implisit, lalu menghitung rasio setiap
pasangan teks/latar yang benar-benar muncul). Alat itu menemukan semua perbaikan di §5
putaran kedua, tetapi **selalu** menyisakan daftar alarm palsu berikut. Semuanya sudah
ditelusuri satu per satu dan terbukti bukan pelanggaran — dicatat di sini supaya audit
berikutnya tidak mengulang penelusurannya:

| Alarm | Sebab sebenarnya |
|-------|------------------|
| `TextSecondary vs SecondaryBrush` di `AgendaSuratView`, `ExImdbView`, `FormulirView`, `KeputusanView` | tombol ✕ pencarian dengan `Background="Transparent"`; penelusur tidak membaca latar literal `Transparent` sehingga jatuh ke latar gaya implisit `Button` (= `SecondaryBrush`) |
| `AccentTextBrush vs SecondaryBrush` (`FormulirView`) | tombol "Pratinjau" — pola sama |
| `HeaderText/HeaderSubtitle vs SidebarBackgroundBrush` (`MainWindow`) | kepala sidebar memakai `Border.Background` berisi `LinearGradientBrush` (Primary→Secondary); penelusur tidak membaca properti-elemen gradien dan jatuh ke latar leluhur. Dua ujung gradien itu sudah diuji gerbang |
| `TextBrush vs PrimaryBrush` (`MainWindow`) | kepala flyout notifikasi `PrimaryBrush`; teks `TextBrush` yang dihitung berasal dari baris `ListBox` di baris grid **berikutnya** (saudara, bukan anak) |
| `OnPrimaryTextBrush vs SurfaceBrush` (`FormulirView`, `TemplateSuratWizardView`) | centang kotak (tanda ✓) dan nomor langkah: keduanya `Collapsed` sampai aktif, dan saat aktif latarnya berubah jadi `PrimaryBrush` — keadaan yang diuji `OnPrimaryTextBrush/PrimaryBrush` |
| `OnAccentTextBrush vs WindowBackgroundBrush` (`PengaturanAplikasiView`) | label "ON" pada toggle: `Collapsed` kecuali sakelar menyala, saat itu latar track menjadi `AccentBrush` |
| `TextBrush vs #FFFFFF` (`PanduanAwalView`) | `HeaderIconChip` berlatar putih berisi glyph emoji (📋); emoji tidak memakai `Foreground`, jadi warnanya tidak pernah menentukan keterbacaan |
| `HeaderText/HeaderSubtitle vs #22FFFFFF`/`#26FFFFFF`/`#33FFFFFF` (`AboutView`) | latar lencana bening berlapis di atas header gradien; penelusur membacanya sebagai latar teks |
| `@FFFFFF vs @4285F4` (`AboutView`) | avatar Google — pengecualian T13 |
| `TextSecondary vs SidebarBackgroundBrush` (`PengaturanAplikasiView:618`) | keterangan di **bawah** kotak sidebar tiruan (saudara, di luar `Border` berlatar sidebar); latar sebenarnya kartu putih |

Aturan praktisnya: penelusur hanya untuk **mencari kandidat**, bukan untuk memutuskan.
Yang memutuskan tetap gerbang 49 pasangan (`dotnet test`) — dan setiap kandidat baru wajib
ditelusuri tangan seperti tabel di atas sebelum disebut pelanggaran.

## 9. Konsekuensi yang perlu diketahui

- **Empat tema kini memakai tinta gelap di atas aksen** (Light/Dark/Green/Pink:
  `OnAccentTextBrush` = `#0F172A`, seperti yang sudah dilakukan Slate sejak awal). Alasannya
  angka: putih di atas `#2196F3` = 3,12, di atas `#16A34A` = 3,30, di atas `#EC4899` = 3,53,
  di atas `#34D399` = **1,92** — semuanya di bawah 4,5. Karena pemakaian `OnAccentTextBrush`
  ternyata sedikit (badge, label `ON`, nomor langkah, hover tautan, tab terpilih), perubahan
  tampilannya terbatas pada elemen-elemen kecil itu, dan semuanya justru jadi terbaca.
- Skrip `Tools/check-kontras-wcag.ps1` kini punya padanan uji; tetap bisa dijalankan di mesin
  yang punya pwsh dan akan melaporkan hal yang sama.- Seluruh perubahan warna diverifikasi angka, bukan hanya oleh mata: 531/531 uji lulus,
  termasuk 343 perhitungan kontras (49 pasangan × 7 tema).

## 10. Tema ketujuh: Kontras Tinggi (AAA)

Ditambahkan 1 Oktober 2026 atas permintaan pemelihara — tema **opsional** untuk pengguna yang
butuh keterbacaan maksimum (low vision, layar di ruang terang, atau proyektor). Dipilih dari
akordeon **Tema** di sidebar seperti tema lain, tersimpan di `theme.config`, dan tidak pernah
terpilih sendiri: bawaan aplikasi tetap Hijau.

| Aspek | Nilai |
|-------|-------|
| Latar & kartu | `#000000` keduanya — pemisahan dibawa garis tepi, seperti tema kontras tinggi Windows |
| Teks utama / sekunder | `#FFFFFF` (21:1) · `#D0D0D0` (13,6:1 di atas latar) |
| Aksen | `#FFD400` kuning menyala, dengan tinta `#000000` di atasnya |
| Lencana status (isian) | sukses `#00E676`, peringatan `#FFD400`, galat `#FF7070` — semuanya berteks hitam (7,8–14,7:1) |
| Teks status terang | sukses `#69F0AE`, peringatan `#FFD400`, galat `#FF9E9E` (8,2–14,7:1 di atas latarnya) |
| Kunci token | 63, sama persis dengan enam tema lain (dijaga uji paritas kunci) |

Dua angka dipilih karena batas matematis, dan keduanya perlu diketahui sebelum diubah:

- **`BorderColor` `#595959`** adalah nilai paling terang yang masih memberi teks putih ≥ 7:1
  di atasnya (terukur 7,01:1). Itu penting karena tombol geser OFF menaruh label teksnya tepat
  di atas `BorderBrush`. Konsekuensinya garis tepi hanya 2,997:1 terhadap latar hitam — di bawah
  3:1 WCAG 1.4.11 — tetapi garis itu murni dekoratif: keadaan kontrol dibawa label OFF/ON
  (7,01:1) dan ibu jari putih, jadi tidak ada informasi yang bergantung padanya.
- **`ErrorColor` `#FF7070`**, bukan merah jenuh `#FF5252`: tinta hitam di atas lencana galat
  perlu ≥ 7:1, sedangkan `#FF5252` hanya mencapai 6,58:1 dengan tinta hitam (3,19:1 dengan putih).

Gerbang kontras **diperluas, bukan diduplikasi**: `HighContrastTheme` masuk ke daftar tema yang
sama di kedua berkas, lalu ambangnya dinaikkan per tema — `Get-AmbangEfektif` di skrip,
`TemaKontrasTinggi_MemenuhiAmbangWcagAaa` di uji C# (teks normal 7:1, teks besar 4,5:1).
Pasangan yang wajib lolos tetap **49 pasangan yang sama**, jadi tema ini tidak bisa "lolos"
lewat daftar yang lebih longgar; ia hanya bisa lulus dengan angka yang lebih baik.

Titik registrasi tema ketujuh — semuanya harus disentuh bila kelak menambah tema lagi:

- `SuDesApp.Wpf/Themes/HighContrastTheme.xaml` — berkas palet baru (kunci tokennya wajib sama
  dengan tema lain; uji paritas kunci akan menolak kalau ada yang kurang atau berlebih),
- `ThemeService` — konstanta, URI, `GetAvailableThemes`, `GetDisplayName`, `IsValidTheme`, dan
  `switch` pemilihan berkas tema,
- `KontrasTemaTests.TemaBerwarna` **dan** `$temaList` di `Tools/check-kontras-wcag.ps1`
  (keduanya wajib sama, kalau tidak uji paritas daftar pasangan langsung gagal),
- teks halaman Tentang, catatan rilis, dan dokumen ini.
