# SuDesApp 2.15.1 — 18 November 2026

Rilis perbaikan pasca-2.15.0. Isinya jalur yang paling sering rusak dan paling
sulit dilihat: instalasi baru yang membuka aplikasi dengan galat basis data,
tombong **Hubungkan** pada halaman Langganan, pembuatan kode pemasangan +
kode QR untuk Android, formulir Buat Surat Android yang membeku saat dibuka,
dan kartu isian yang kembali lagi ke server lama walaupun APK baru dipasang.

Tidak ada perubahan format data: `desa.db`, `Templates`, arsip surat, dan
skema Supabase dipakai apa adanya. **Tidak ada migrasi** — riwayat pembayaran
yang sudah tercatat tetap terbaca meski gateway pembayaran otomatis dihapus.

## Perbaikan

- **Installer menyertakan skema basis data.** `installer/SuDesApp.iss`
  meng-`Excludes` pola `desa.db*`, yang ternyata ikut menelan `desa.db.sql`
  — berkas skema yang dibutuhkan aplikasi saat membuka basis data pertama
  kali. Akibatnya instalasi baru gagal dengan `no such table: main.Surat` dan
  `no such table: IjinTinggal`, dan sebagian pengguna melihatnya sebagai
  "aplikasi rusak setelah install". Pola dipersempit menjadi
  `desa*.db,desa*.db-*,desa_backup*.db` sehingga skema ikut terpasang
  sementara basis data pengguna tetap tidak pernah ditimpa. Dijaga dua arah
  oleh `InstallerSkemaDatabaseTests` (8 uji): skema WAJIB ada di paket, dan
  berkas pengguna WAJIB tidak ada.
- **Server lisensi lokal ikut menyala bila alamatnya menunjuk komputer
  sendiri.** Keputusan "perlu server lokal" dulu dibaca dari preferensi yang
  bisa tertinggal, sehingga aplikasi dengan `Lisensi:UrlServer` loopback ada
  dalam keadaan tidak menjalankan servernya — gejala yang dilaporkan operator
  adalah "lisensi server tidak terhubung". Sekarang
  `PenentuAlamatLisensi.ServerLokalDibutuhkan` menentukannya dari alamat yang
  benar-benar dipakai, dan `App.Startup` menyalakan server sebelum
  `HubungkanAsync` bertanya; balapan pada tombol Hubungkan (permintaan dikirim
  sebelum server siap) ditutup. Kalau server tetap gagal start, pesan galat
  kini membawa baris log terakhirnya, bukan "tidak terhubung" tanpa sebab.
  Jalur ini ditandai ZONA TERKUNCI: berkasnya read-only + `assume-unchanged`,
  dan kontraknya dijaga `KontrakHubunganServerLisensiTests` (invarian: server
  lokal dan `Lisensi:UrlServer` satu bentuk host `http://127.0.0.1:5110`,
  loopback tidak pernah mengunci alamat, hanya HTTPS non-loopback yang
  mengunci).
- **Server lisensi lokal benar-benar ikut terpasang.** Perbaikan di atas
  membuat aplikasi menyalakan servernya sendiri, tetapi paket installer tidak
  pernah memuat `SuDesApp.Server` — `publish-release.ps1` hanya menerbitkan
  `SuDesApp.Wpf`. Akibatnya aplikasi yang baru dipasang tetap menampilkan
  "lisensi server tidak terhubung": `ServerLisensiLokal` mencari
  `<folder aplikasi>\server\SuDesApp.Server.exe`, dan folder itu tidak ada
  di paket. Sekarang paket mode lisensi lokal menerbitkan server
  (self-contained) ke folder `server` di samping aplikasi, beserta
  `server\keys\lisensi-signing.key` yang sama dengan `Lisensi:KunciPublik` —
  kunci berbeda membuat setiap token lisensi ditolak, karena `LisensiManager`
  tidak pernah menimpa kunci tertanam. Rilis produksi (alamat HTTPS terkunci)
  tidak mengirim server ini karena server lokal sengaja tidak dinyalakan.
  Penjaganya `PaketServerLisensiTests` (4 uji): skrip publish wajib menerbitkan
  server, wajib menyalin kunci, dan installer tidak boleh mengecualikan folder
  `server`.
- **Tombol Hubungkan tidak lagi membuka peramban.** `CobaBukaPeramban`
  dilepas: dengan pembayaran otomatis dihapus, membuka peramban setelah
  menagih hanya memicu salah baca "sudah bayar" sementara hak akses masih
  menunggu pembukuan administrator.
- **Pembayaran hanya lewat pembukuan manual.** Gateway Midtrans dicabut
  beserta seluruh perangkatnya (`MidtransService`, `KonfigurasiMidtrans`,
  `PembuatTagihan`, `LayananWebhook`, `PelacakWebhook`,
  `LayananRekonsiliasi`, `RekonsiliasiLatar`, `LayananUjiPembayaran`,
  `StatusPembayaran`, halaman `checkout`/`checkout-uji`, endpoint `/checkout`,
  `/webhook-pembayaran`, `/admin/rekonsiliasi`, `/admin/uji-pembayaran`) —
  dibiarkan terpasang berarti meninggalkan permukaan serangan yang tidak
  dipakai. `/buat-tagihan` tetap mencatat tagihan (`orderId`, paket, nominal,
  hari, `kode_aktivasi`) sehingga halaman Langganan dan Aktivasi tidak berubah
  bentuk; hanya medan token/link pembayaran gateway yang hilang dari balasan.
  Hak akses kini hanya bisa bertambah lewat `/admin/pelanggan/{kode}/tandai-lunas`
  yang bertoken admin — jalur yang sudah ada. Hak warisan
  `payments.reconcile`/`payments.test` tetap diterima supaya konfigurasi lama
  tidak gagal start. Dijaga `TanpaGatewayPembayaranTests` (8 uji).
- **Checkbox dan dialog persetujuan tidak terpotong.** Template `CheckBox`
  memakai `StackPanel Horizontal`, sehingga label panjang mendorong kotak
  keluar layar dan memotong teks. Sekarang `BulletDecorator` dengan teks yang
  membungkus dan kontras yang dijaga; dialog **Persetujuan Dokumen** (Policy &
  Terms) tidak lagi kehilangan kotak centang maupun isi token-nya.
  `KotakCentangMembungkusTests` (3 uji) menolak kembali ke template lama.
- **Membuka surat di Android tidak lagi membekukan layar.** Di perangkat
  (Redmi M2007J20CG) dan emulator, membuka surat Kematian sempat menahan main
  thread **3135 ms** pada bangunan pertama, lalu ±1,4 s setiap kali. Penyebab
  bukan ukuran APK (4,7 MB, R8 + resource shrinking aktif) tetapi pekerjaan di
  thread UI: `EncryptedSharedPreferences` dibuka ulang setiap panggilan
  (257 ms pertama, 45–74 ms berikutnya, dipakai 11 tempat), 586 View formulir
  dibangun sekaligus, dan halaman PDF digambar di thread UI. Sekarang sesi
  di-cache per proses, formulir dibangun bertahap per bidang dengan kuota
  45 ms (awal) / 12 ms (lanjutan), adapter pilihan dan warna di-cache, dan
  render PDF pindah ke utas sendiri dengan penutupan `PdfRenderer` pada utas
  yang sama. Bangunan main thread turun ke **±70–150 ms** (±712 ms pada
  panggilan pertama karena pemuatan kelas), semua bidang siap ±2,7 s, cold
  start aplikasi 495 ms. **Hasil cetak tidak berubah**: validasi wajib,
  pembuatan PDF, dan pengisian dari Register memaksa seluruh bidang selesai
  dibangun lebih dulu, dan penjaganya membaca sumber Kotlin
  (`AndroidBiayaFormulirTests`, 5 uji).
- **APK baru benar-benar terbaca, bukan kalah oleh cache.** Surat kembali
  menjadi satu daftar panjang tanpa kartu, dan kolom baru tidak pernah muncul,
  walaupun APK terbaru terpasang. `SkemaLokal` selalu mendahulukan
  `filesDir/skema-cache.json` — terbitan desktop yang tersinkron — tanpa tahu
  bahwa cache itu bisa lebih tua dari skema yang dibekukan di APK. Cache kini
  membawa stempel kepemilikan (sidik jari aset `skema-formulir.json`,
  `VERSION_CODE`, dan `diperbarui_pada` paling tua dari server, dinormalkan ke
  UTC): cache warisan APK lain dibuang, cache dari APK yang sama tetapi lebih
  tua dilewati, dan waktu skema APK diambil dari commit Git terakhir berkas
  skema desktop (`BuildConfig.SKEMA_SNAPSHOT_MILLIS`; 0 = tidak diketahui, maka
  aturan waktu sengaja tidak dipakai supaya tidak menebak). Pengelompokan kartu
  juga dilengkapi dari aset APK bila skema terpakai belum punya grup sama
  sekali — yang disentuh hanya `grup`/`grupUrutan`; label, kolom, opsi, dan
  isi badan surat dari server tetap menang. `SkemaCacheStampTest` (6),
  `SkemaGrupTest` (5), dan tiga uji paritas yang membaca sumber Kotlin
  mengunci keduanya.
- **Kode pemasangan dan QR tidak lagi lahir untuk bentuk yang ditolak
  ponsel.** `Supabase:UrlRest` dipakai apa adanya sebagai baris pertama QR,
  padahal ponsel menempelkan sendiri `/rest/v1` dan `/auth/v1` dan
  `LoginActivity` menolak nilai yang bukan `https://…*.supabase.co`. Operator
  yang menulis `https://<ref>.supabase.co/rest/v1` — bentuk paling wajar untuk
  kunci bernama "UrlRest" — mendapat kode yang **sudah terbit** (akun Auth +
  baris perangkat tercipta) tetapi pasti ditolak ponsel. Desktop kini
  memulihkan bentuk basis REST/Auth ke URL project dan menolak bentuk lain
  **sebelum** ada koneksi ke Supabase, dengan pesan yang menyebut kunci yang
  harus diisi; halaman Sinkron memakai hasil servis yang sama untuk isi QR.
  Ikut dibenahi pada jalur yang sama: surel login yang tidak dikembalikan
  Supabase dulu disimpan sebagai string kosong (kode tidak mungkin tertebus)
  dan sekarang membatalkan transaksi; kode 4 angka serta kata sandi perangkat
  tidak lagi direduksi dengan modulo (256 tidak habis dibagi 10 dan 56, jadi
  sebagian nilai lebih mungkin muncul); komentar lama yang menyebut "QR berisi
  surel dan kata sandi" dibetulkan. Jalur ini sebelumnya tidak punya uji
  sama sekali; sekarang 29 uji (`KodePasanganSupabaseTests`), termasuk dua uji
  yang membandingkan aturannya dengan `LoginActivity.kt`.
- **Isian Garapan Sawah disamakan dengan desktop**, termasuk tombol Tambah
  Bidang yang menambah kolom per bidang (lokasi, nomor persil, luas,
  keterangan garapan) dan pola tanggal lahir per jenis surat yang sebelumnya
  berbeda antara generator desktop dan Android. Pratinjau PDF Android kini
  selalu memakai tema terang sehingga tidak ikut gelap oleh tema aplikasi.

## Peningkatan

- **Dokumen sinkron menjelaskan tiap kunci.** Arti `Host`, `Port` (session
  pooler 5432 vs transaction pooler 6543), `Pengguna` (harus role pemilik
  karena RPC pemasangan di-`REVOKE` dari `anon`/`authenticated`), `KataSandi`,
  `Tenant` (= `tenant_id` desa, sama `pelanggan.id` pada skema lisensi — bukan
  project ref), `UrlRest` (URL project, bukan basis REST), dan `KunciPublik`
  dituliskan dalam satu tabel, ditambah subseksi "Kode pemasangan dan QR
  kosong". Lencana `KONEKSI BELUM SIAP` di kepala halaman Sinkron kini punya
  ToolTip yang menyebut kunci yang harus diisi — sebelumnya lencana itu hanya
  menyatakan gejala tanpa jalan keluar.
- **Uji pemulihan database tidak lagi bergantung mesin.** Kandidat basis data
  dari folder instalasi dapat dimatikan lewat `sertakanKandidatMesin: false`
  khusus pengujian (dua uji sebelumnya lulus/gagal tergantung apakah PC
  pengujian punya aplikasi terpasang), sementara produksi tetap memakainya —
  dan satu uji terpisah menjaga jalur produksi itu tidak diam-diam ditutup.
- **Alat rilis ditambal.** `Tools/rilis.ps1` masih menunjuk
  `CatatanRilisViewModel.cs` padahal kartu riwayat pindah ke
  `CatatanRilisViewModel.RiwayatVersi.cs`, sehingga setiap rilis berhenti pada
  "Entri catatan rilis teratas tidak ditemukan". Batas skrip yang ditemukan
  sambil menelusurinya ikut didokumentasikan: ia **tidak** menaikkan
  `versionCode`/`versionName` Android dan tidak menulis
  `docs/catatan-rilis-<versi>.md`, padahal keduanya wajib berada pada commit
  yang ditag — `PengaturanSinkron.versiCocok` membandingkan `versionName`
  dengan versi desktop secara persis, jadi APK 2.15.0 menolak sinkron dengan
  desktop 2.15.1.

## Kompatibilitas & cara naik

- Tidak ada perubahan skema basis data maupun skema Supabase. Instalasi 2.15.0
  ditimpa apa adanya; `patch.json` menyediakan pembaruan kecil bila
  perbandingannya tersedia, bila tidak aplikasi meminta installer penuh.
- Android: **2.15.1 (versionCode 21501)**. Karena sinkron bergerbang versi,
  pasang APK ini bersamaan dengan desktop 2.15.1; ponsel dengan versi berbeda
  akan menahan sinkron dan diminta memperbarui lebih dulu.
- Midtrans tidak dipakai lagi. Desa yang tagihannya pernah terbentuk lewat
  gateway tetap perlu dibukukan administrator lewat Pusat Administrasi; tidak
  ada pengembalian dana otomatis dari aplikasi.

## Verifikasi

- `dotnet build SuDesApp.slnx -c Release` — 0 galat, 0 peringatan.
- `dotnet test SuDesApp.Tests` — **562 uji, semuanya lolos**. Termasuk 4 uji
  `PaketServerLisensiTests` (server lisensi wajib ada di paket), 29 uji
  `KodePasanganSupabaseTests` (jalur kode pemasangan, sebelumnya tanpa uji),
  8 uji `InstallerSkemaDatabaseTests`, 5 uji `AndroidBiayaFormulirTests`,
  3 uji `KotakCentangMembungkusTests`, 15 uji paritas Buat Surat Android ↔
  desktop, 163 uji berfilter `Licensing`, 6 uji gerbang rilis, dan 3 uji
  keselarasan catatan rilis.
- `gradlew testDebugUnitTest` (Android) — **64/64 lolos** (11 kelas, termasuk
  `SkemaCacheStampTest` dan `SkemaGrupTest`).
- `bash Tools/uji-e2e-trial-server.sh` — SEMUA LULUS (jalur manual; endpoint
  gateway tidak diuji lagi karena memang tidak ada).
- Di perangkat sungguhan: cold start 495 ms, `Displayed .BuatSuratActivity
  +232 ms`, kartu Kematian (DATA JENAZAH/PEMOHON, DATA PELAPOR, DATA KEMATIAN)
  tampil setelah APK baru dipasang.
