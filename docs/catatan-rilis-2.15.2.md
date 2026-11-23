# SuDesApp 2.15.2 — 23 November 2026

Rilis perbaikan tiga jalur yang gejalanya sama-sama terbaca sebagai "aplikasinya
rusak": tombol **Cetak** yang lenyap di pratinjau PDF walaupun lisensi masih
berlaku, **pembaruan otomatis** yang bisa menjatuhkan pilihan pada berkas yang
salah ketika sebuah rilis tidak menyertakan installer, dan **ukuran hasil cetak**
yang sedikit lebih kecil daripada kertas aslinya.

Tidak ada perubahan format data: `desa.db`, `Templates`, arsip surat, dan skema
Supabase dipakai apa adanya. **Tidak ada migrasi.**

## Antarmuka & Cetak

- **Tombol Cetak dan Ekspor kini dikendalikan lisensi, bukan jenis surat.**
  `PdfPreviewView` mengikat `Visibility` tombol Cetak pada aturan lama "hanya
  pratinjau Surat Kematian (dan Daftar Hadir) yang boleh dicetak langsung dari
  tombol Pratinjau", sehingga desa yang trial-nya masih jalan atau yang sudah
  berlangganan tidak melihat tombol itu sama sekali pada surat lain — dan
  melaporkannya sebagai fitur cetak yang dihapus. Sekarang kedua tombol selalu
  tampil; satu-satunya yang mematikannya adalah `PenjagaLisensi`
  (`BolehCetak`/`BolehEkspor`): hidup selama trial berjalan atau langganan aktif,
  mati dengan tooltip yang menyebut sebabnya (`PesanCetakTerkunci`) ketika trial
  habis, dan cetak Daftar Hadir tetap dikecualikan seperti aturan produk.
- **Penyetelan `IsEnabled` di kode-behind dilepas.** `PrintBtn.IsEnabled` dan
  `ExportBtn.IsEnabled` pernah disetel langsung dari kode-behind; nilai lokal itu
  melepas binding keadaan lisensi — sumber gejala "tombol mati permanen untuk sisa
  sesi". Kesiapan PDF sekarang dilaporkan ke ViewModel lewat
  `TerapkanKesiapanDokumen`, sehingga "PDF gagal dimuat" dan "lisensi terkunci"
  digabung di satu tempat, dan halaman yang sedang terbuka ikut segar ketika
  keadaan lisensi berubah (langganan lemah ke `PenjagaLisensi.Berubah`, mengikuti
  pola yang sudah dipakai halaman Langganan) tanpa menunggu navigasi ulang.
- Aturan lama tidak bisa balik diam-diam: `TombolCetakEksporPratinjauTests`
  (18 uji) mengunci keadaan tombol per keadaan lisensi dan menolak lagi binding
  `Visibility` per jenis surat di XAML maupun penyetelan `IsEnabled` manual di
  kode-behind.
- **Hasil cetak kini mengacu ke lembar kertas, bukan ke kotak tinta printer.**
  `PdfPrinter` memaskan halaman ke `MarginBounds` — area yang benar-benar bisa
  ditandai printer — sehingga margin keras printer (umumnya 4–5 mm per sisi dan
  lebih lebar di bawah) ikut menyusut SELURUH halaman: surat A4 210 mm keluar
  ±2–6% lebih kecil dan bergeser, persis keluhan "ukuran cetak tidak sama dengan
  kertas aslinya, tapi cuma sedikit". Margin itu juga dihitung dua kali (sekali
  sebagai acuan skala, sekali sebagai offset gambar). acuannya sekarang
  `PageSettings.PaperSize` + `Landscape`: halaman digambar 1:1 pada lembar dan
  hanya diperkecil bila halamannya memang lebih besar dari kertas yang tersedia
  (mis. surat F4 di atas kertas A4), dipusatkan pada tepi lembar, dan sisi yang
  jatuh di luar kotak tinta dipotong sebagai sumber — bukan diskala dua kali.
  Ukuran kertas driver terdekat dengan halaman PDF juga ikut dipilih setelah
  dialog cetak (toleransi 2,5 mm, orientasi diabaikan) supaya surat F4 tidak
  tercetak di atas antrean A4.
- Perhitungan itu dipisah ke `SudesPdf/PdfPrintLayout.cs` supaya bisa diuji tanpa
  printer, dan dijaga `PdfPrintTataLetakTests` (21 uji): 1:1 untuk A4-di-A4, tidak
  pernah memperbesar, F4-di-A4 hanya diperkecil secukupnya, potong-sumber saat
  keluar kotak tinta, batas skala raster pada printer 1200/2400 dpi, dan
  pemilihan ukuran kertas driver. Margin desain surat (45 pt ≈ 16 mm) tidak berubah,
  jadi tidak ada isi yang terpotong oleh perbaikan ini.

## Pembaruan Aplikasi

- **Installer hanya diambil dari aset `.exe`.** `UpdateService.ParseGitHubRelease`
  mempunyai fallback "aset pertama apa pun" ketika tidak ada aset berakhiran
  `.exe`. Satu rilis yang lupa menyertakan `Setup.exe` membuat `patch.json`,
  zip portable, atau APK dianggap installer: berkas yang salah diunduh (hingga
  ~117 MB) lalu dijalankan sebagai program, dan pemasangan gagal dengan pesan yang
  tidak menjelaskan apa pun. Rilis seperti itu kini tetap dikabarkan — versinya
  terbaca, `DownloadUrl` kosong — dan setiap pemanggil sudah memperlakukan keadaan
  itu sebagai "tautan unduhan belum tersedia" (`PembaruanLatarService`,
  `PembaruanViewModel.BisaInstaller`), sedangkan pembaruan kecil tidak terpengaruh
  karena mencari `patch.json`/`patch-<versi>.zip` di daftar aset.
- **Sebab galat disebutkan apa adanya.** Status yang sudah dijawab server
  sebelumnya diringkas menjadi "Tidak bisa menghubungi GitHub", sehingga
  pemeriksaan diarahkan ke jaringan desa. Kini 403/429 dilaporkan sebagai
  pembatasan permintaan (limit tanpa token 60 permintaan/jam per alamat IP), 401
  menunjuk `AppConfig.githubPersonalAccessToken`, dan 5xx sebagai GitHub yang sedang
  gangguan. Status yang tidak dikenali tetap memakai perilaku lama, dan tipe
  exception tidak berubah sehingga semua `catch` yang ada tetap berlaku.
- **Gerbang uji baru.** Jalur parsing rilis sebelumnya tidak punya satu pun uji.
  `UpdateServiceRilisTests` (31 uji, seluruhnya luring) mengunci bentuk rilis
  v2.15.1 yang nyata: pemilihan `SuDesApp_2.15.1_Setup.exe` walau
  `.exe.sha256` didaftarkan lebih dulu, ekstensi `.EXE` huruf besar, digest yang
  tidak lengkap diabaikan, aset tanpa tautan, rilis tanpa aset, JSON rusak,
  normalisasi tag (`v2.15.1`, `V2.15.1-rc1`, `release-2.15.1`), dan pemetaan
  tiap status HTTP. Terverifikasi juga terhadap rilis hidup: hasil sebelum dan
  sesudah perbaikan identik (`2.15.1`, installer, digest, 7 aset, `patch.json`).

## Android

Tidak ada perubahan perilaku Android pada rilis ini. `versionCode`/`versionName`
naik ke **21502 / 2.15.2** karena `PengaturanSinkron.versiCocok` membandingkan
`versionName` dengan versi desktop secara persis — APK 2.15.1 akan menolak sinkron
dengan desktop 2.15.2.

## Verifikasi

- `dotnet build SuDesApp.slnx` hijau, 0 galat dan 0 peringatan.
- `dotnet test SuDesApp.slnx` — **632/632 lolos**, termasuk 18 uji tombol
  Cetak/Ekspor, 31 uji parsing rilis, 21 uji tata letak cetak
  (`PdfPrintTataLetakTests`, ukuran kertas dan pemotongan kotak tinta), gerbang
  `CatatanRilisSelarasTests` (kartu teratas harus sama dengan `AssemblyVersion`;
  setiap dokumen rilis harus punya kartu), dan paritas Buat Surat Android ↔
  desktop.
- Uji manual otomatis: `PenjagaLisensi` tidak diubah, jadi kontrak
  `KontrakHubunganServerLisensiTests` (ZONA TERKUNCI hubungan server lisensi) tidak
  tersentuh.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.15.1 | tambalan kecil bila `patch.json` rilis ini menyertakannya | hanya berkas yang berubah |
| di bawah 2.15.1, atau rilis tanpa tambalan | installer penuh 2.15.2 | ± 117 MB |

Aplikasi memilih jalurnya sendiri: tambalan hanya dipasang bila versi terpasang
tercantum di `dariVersi` pada `patch.json`; selain itu diarahkan ke installer penuh.
Data desa (`desa.db`, `Templates`, hasil PDF, `appsettings.json`) tidak ditimpa saat
pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
