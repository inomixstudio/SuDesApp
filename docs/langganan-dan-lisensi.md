# Langganan & Lisensi (hibris + lisensi server)

Keputusan arsitektur untuk menjadikan SuDesApp aplikasi berbayar berlangganan
bulanan dengan target banyak desa (multi-tenant).

## Model yang dipilih

Aplikasi desktop tetap memakai SQLite lokal terenkripsi (offline-first penuh).
Status langganan dicek lewat server lisensi pusat:

```
SuDesApp.WPF ── HTTPS ──> SuDesApp.Server (minimal API)
                              ├─ data pelanggan & langganan (Supabase Postgres)
                              ├─ tagihan QRIS/VA (Midtrans Snap)
                              ├─ token lisensi BERTANDATANGAN (ECDSA P-256)
                              └─ webhook pembayaran → perpanjang akses +31 hari
```

- **Bukan Supabase penuh untuk data desa**:rombakan besar tidak dilakukan;
  SQLite lokal tetap jalan offline. Supabase dipakai sebagai penyimpanan
  data server (pelanggan/langganan), bukan sebagai tempat data surat.
- **Bukan kutu lisensi lokal saja**: status diverifikasi dari tanda tangan
  server, sehingga tidak bisa ditembus dengan menyunting berkas lokal.
- **Masa tenggang 7 hari**: aplikasi tetap sah 7 hari sejak cek sukses
  terakhir, supaya internet putus tidak langsung memblokir kerja desa.

## Kenapa token harus bertanda tangan

Versi pertama memakai cache JSON biasa
(`{"aktif":true,"cekTerakhir":"<sekarang>"}`). Berkas itu tinggal disunting di
`%LocalAppData%\SuDesApp` — lisensi gratis selamanya, tanpa internet, tanpa
jejak. Field `aktif` dan `cekTerakhir` yang dipakai sebagai sumber kebenaran
itulah yang bisa disunting bebas.

Sekarang client hanya menyimpan **token bertanda tangan** dan memverifikasinya
dengan kunci publik yang ada di dalam binary:

- Kunci privat hanya ada di server (`SuDesApp.Server/keys/lisensi-signing.key`,
  di-`gitignore`).
- Client memverifikasi tanda tangan secara **lokal**, jadi aplikasi tetap bisa
  diverifikasi penuh saat offline.
- Field `cek` (waktu konfirmasi server) berada DI DALAM payload yang
  ditandatangani. Ini yang menutup jalur putar-balik: menyimpan token lama
  tidak menolong, karena `cek`-nya sudah tua dan masa tenggangnya habis.
- Edit apa pun pada cache membuat verifikasi gagal → status "tidak aktif"
  (fail closed), bukan status palsu.

Format token: `v1.<payload base64url>.<signature base64url>`.

## Aturan kelayakan

Sah sampai `max(exp, cek + 7 hari)`:

- Yang sudah dibayar sampai 30 September tetap sah sampai 30 September walau
  offline berbulan-bulan — `exp` sudah ditandatangani.
- Masa tenggang hanya menjembatani setelah tanggal pembayaran lewat, memberi
  waktu 7 hari menyambungkan internet tanpa kehilangan pekerjaan.

## Paket langganan

Harga **tidak pernah** dikirim dari klien. `KatalogPaket` (server) adalah satu
sumber kebenaran; klien hanya memilih paket by kode.

Sejak Oktober 2026 ada **tiga paket berbayar** plus satu masa percobaan gratis:

| Mode | Harga | Masa aktif | Per hari | Jalur |
|---|---|---|---|---|
| **Trial** | gratis | 14 hari | — | `POST /aktifkan-trial` — TIDAK lewat Midtrans |
| **Bulanan** | Rp150.000 | 30 hari | Rp5.000 | tagihan QRIS/VA (`POST /buat-tagihan`) |
| **6 Bulan** | Rp750.000 | 180 hari | Rp4.167 (hemat ±17%) | tagihan QRIS/VA |
| **Tahunan** | Rp1.200.000 | 365 hari | Rp3.288 (hemat ±34%) | tagihan QRIS/VA |

Semuanya memberi akses penuh yang sama; yang berbeda hanya panjang komitmen
dan harga per harinya. Harga per hari **turun** seiring panjang paket — kalau
konfigurasi suatu saat membuat paket panjang justru lebih mahal per hari, itu
kesalahan yang harus ketahuan, dan uji `KatalogPaketTests` menahannya.

Aturan trial (dijaga di server, bukan di klien):

- berlaku sekali per desa seumur hidup (kolom `pelanggan.trial_sudah`,
  ditambah lewat schema.sql);
- hanya bila langganan belum sedang aktif (yang masih berlaku tidak perlu);
- batas perangkat tetap berlaku; percobaan yang ditolak tidak menghabiskan hak
  coba;
- jumlah hari datang dari konstanta server (`LayananAktivasi.HariTrial`),
  jadi klien tidak bisa memesan trial lebih panjang.

Ada **dua** trial 14 hari dan keduanya tidak saling menggantikan:

| Trial | Syarat | Dijaga oleh | Kapan dipakai |
|---|---|---|---|
| Server (`POST /aktifkan-trial`) | Kode aktivasi + internet | Server (sekali per desa, batas perangkat) | Desa sudah punya kode, minta masa coba resmi |
| Lokal (`TrialLokal`) | Tidak perlu apa pun | Berkas di perangkat (sekali per instalasi) | Instalasi baru / belum punya kode, atau server tidak terjangkau saat trial diminta |

Urutan pemakaiannya: `PenjagaLisensi.MulaiPercobaanAsync` mencoba **trial server
lebih dulu** bila kode aktivasi sudah terpasang dan server menjawab; trial lokal
hanya dipakai bila kode belum ada atau server tidak terjangkau. Server yang
menjawab menolak **tidak** diberi cadangan lokal, supaya satu desa tidak bisa
mengumpulkan dua kali masa coba (detail di bagian *Penggatingan fitur*).

Trial lokal hanya membuka gerbang menu selama 14 hari; setelah itu aplikasi
kembali terkunci (baca-saja) sampai ada token langganan yang sah. Bila server
kemudian menerbitkan token trial maupun berbayar, keadaan `Berlangganan` menang
atas trial lokal — jadi desa yang membayar tidak pernah “dimundurkan” oleh
sisa trial lokalnya.

Trial sengaja **tidak** masuk katalog `Langganan:Paket` — harga 0 di katalog
tagihan berarti tagihan Rp0 yang bisa dikali ulang. Karena itu uji
`TanpaKonfigurasi_TetapAdaPaketBawaan_YangWajar` memastikan `Cari("trial")`
tetap `null`.

Ketiga paket itu adalah **bawaan katalog** (`KatalogPaket`), jadi server yang
baru dipasang tanpa `appsettings` tetap menawarkan ketiganya — aplikasi desa,
halaman `/checkout`, dan formulir *Tandai lunas* di `/admin` membaca daftar yang
sama dari `GET /paket`, sehingga tidak ada tempat yang perlu diubah terpisah.
Tagihan lama yang belum dibayar tetap sah walau harga berubah — harga & durasi
dibaca dari tabel `tagihan`, bukan katalog.

Konfigurasi (`SuDesApp.Server/appsettings.lokal.json`) — untuk memaksakan
harga lain tanpa membangun ulang (entri di sini menimpa bawaan dengan kode yang
sama):

```json
"Langganan": { "Paket": {
    "bulanan":   { "Nama": "Bulanan",  "Harga": 150000,  "Hari": 30 },
    "enambulan": { "Nama": "6 Bulan",  "Harga": 750000,  "Hari": 180 },
    "tahunan":   { "Nama": "Tahunan",  "Harga": 1200000, "Hari": 365 } } }
```

Tambah paket (mis. `tahunan` 2 tahun) cukup menambah satu blok — server,
UI, dan webhook otomatis menyesuaikan.

### Durasi diambil dari tagihan, bukan dari konfigurasi global

Tabel `tagihan` menyimpan `paket` + `nominal` + `hari` saat tagihan dibuat.
Webhook membacanya dari sana. Kalau harga paket berubah setelah tagihan
terbit, yang dipakai adalah harga yang benar-benar **ditagihkan** ke
pengguna — bukan harga yang berlaku saat webhook tiba.

## Alur

1. Menu **Langganan** (berdiri sendiri di navigasi utama) → pengguna
   memasukkan **kode aktivasi** (contoh `SUDES-XXXX`).
2. `LisensiManager.CekAsync()` POST `/aktivasi` (kirim `idPerangkat`) →
   server membalas **token bertanda tangan** → disimpan di
   `%LocalAppData%\SuDesApp\lisensi-cache.json`.
3. Aktivasi juga mencatat `idPerangkat`; server menolak di atas
   `pelanggan.max_perangkat` (bawaan 2) supaya satu langganan tidak dipakai
   di banyak PC. Perangkat yang **ditolak tidak ikut dicatat**: kalau ikut,
   jumlah perangkat desa melewati batas untuk selamanya dan aktivasi dari
   komputer yang sah pun ditolak — pelanggan yang sudah bayar kehilangan
   akses karena satu percobaan dari komputer lain.
4. Belum aktif / jatuh tempo → `POST /buat-tagihan` → QRIS/VA.
5. Bayar → webhook `POST /webhook-pembayaran` → server memperpanjang
   `berlaku_sampai` +31 hari.
6. Aplikasi cek via `LisensiManager.MasihSah()` → tetap jalan selama sah.

### Dua pintu masuk pembayaran

Langkah 4 bisa ditempuh dari **dua sisi**, keduanya memakai endpoint publik
yang sama (`/paket` + `/buat-tagihan`) — tidak ada yang menentukan harga di
sisi pembeli:

- **Aplikasi desktop** — tombol Bayar di menu Langganan membuat tagihan lalu
  membuka halaman pembayaran Midtrans di peramban bawaan.
- **Halaman web `/checkout`** — dibuat untuk pembayaran dari telepon atau
  komputer lain tanpa membuka aplikasi: pilih paket, isi kode aktivasi,
  tekan Buat tagihan. Lihat `SuDesApp.Server/wwwroot/checkout.html`.

Dari dalam aplikasi, menu Langganan tetap membuka pembayaran langsung; tautan
halaman web tetap tersedia untuk yang ingin membayar dari perangkat lain
(bagikan `https://<host>/checkout`).

### QRIS statis + "Tandai lunas" (pintu ketiga, tanpa gateway)

Tombol **Tampilkan QRIS** di kartu paket menampilkan QRIS merchant langsung di
layar aplikasi, jadi desa bisa membayar tanpa membuka peramban dan tanpa
menunggu tagihan Midtrans. Alurnya berbeda kelas dari dua pintu di atas dan itu
disengaja:

| Aspek | Tagihan gateway | QRIS statis (manual) |
|---|---|---|
| Nominal | Diteguhkan server di tabel `tagihan` | Ditampilkan dari katalog; pembeli mengetiknya sendiri di aplikasi bank |
| Deteksi bayar | Webhook/rekonsiliasi Midtrans | Operator melihat dana masuk, lalu menekan **Tandai lunas** |
| Jejak | `pembayaran` berisi `settlement`, order `SUDES-…` | `pembayaran` berisi `manual`, order `MANUAL-…` |
| Kecepatan aktif | Otomatis | Menunggu operator (biasanya saat itu juga) |

Kenapa harus manual: QRIS statis tidak membawa nomor tagihan maupun nominal,
jadi gateway tidak punya apa pun untuk diberitahukan. Karena itu gambar QR
ditanam di rakitan aplikasi (`Resources/Qris-Pembayaran.png`, `<Resource>` di
csproj), **bukan** berkas di folder instalasi — gambar yang bisa ditukar dari
disk berarti pembayaran desa bisa dialihkan ke rekening lain tanpa jejak.

Isi berkasnya HANYA kode QR-nya: potongan dari poster QRIS merchant, sudah
dibuat hitam-putih dan berzona-tenang sendiri sehingga modulnya tajam. Poster
lengkapnya (logo QRIS/GPN, judul toko, NMID) sengaja tidak ikut tampil — yang
perlu dilihat pembeli hanya QR-nya, sedangkan identitas penerima ditulis
sebagai teks di panel: `QrisPenerima` = **arieino (NMID ID1026608792580,
Karawang)**, diambil dari ISI QR (tag 59/51/60 payload, penerbit Shopee) karena
itulah nama yang muncul di aplikasi bank pembeli — bukan judul di poster.
Potongan ini sudah diuji ulang: payload terbaca identik dan tetap terbaca saat
diperkecil sampai 160 px.

Salah ganti berkas di sini berakibat uang desa masuk ke rekening orang lain
tanpa aplikasi terlihat berubah, jadi isinya dijaga uji, bukan hanya
keberadaannya. `QrisPembayaranTests` membaca byte gambar yang benar-benar
tersemat di rakitan, membandingkannya byte demi byte dengan berkas di repo,
lalu MENDekODE QR-nya dan memeriksa payload EMVCo: CRC tag 63 sah, tag 51
sub-tag 02 = `QrisNmid` (`ID1026608792580`), tag 59/60 = `QrisNama` /
`KARAWANG`, tag 58/53 = `ID`/`360`, dan tidak ada tag 54 (nominal tetap).
Menukar gambar dengan QR merchant lain membuat uji gagal dengan pesan
`Expected: "ID1026608792580" / Actual: "ID…"`.

Konsekuensinya saat QR benar-benar diganti: ubah `QrisNmid` **dan** `QrisNama`
di `LanggananViewModel` (dari situ `QrisPenerima` disusun, jadi teks di layar
tidak bisa menyimpang dari isi QR), ganti berkasnya, lalu jalankan uji. Uji
negatif `PembacaNmid_MembacaPayload_BukanKonstanta` memastikan pemeriksaan itu
benar-benar membaca payload, bukan sekadar membandingkan konstanta dengan
dirinya sendiri.

Aplikasi tidak pernah mengklaim langganan aktif sendiri di jalur ini; kalimat
yang tampil selalu "aktif setelah admin menandai lunas". Kode aktivasi wajib
sudah terisi sebelum panel QRIS terbuka: itulah satu-satunya penanda desa yang
bisa dicocokkan operator, sebab QRIS statis tidak punya nomor tagihan.

Membukukannya (halaman `/admin`, panel desa → *Tandai lunas*, atau endpoint):

```bash
curl -X POST http://localhost:5000/admin/pelanggan/SUDES-0001/tandai-lunas \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID" -H "Content-Type: application/json" \
  -d '{"paket":"bulanan","nominal":120000,"catatan":"QRIS 17/10 21:04"}'
```

Aturannya (`LayananPembayaranManual`): paket tak dikenal → paket bawaan katalog
(bukan gratis), `nominal` yang dicatat adalah **uang yang benar-benar diterima**
(boleh berbeda dari katalog; selisihnya masuk log), dan perpanjangan memakai
`PerpanjangAsync` yang sama dengan webhook — jadi idempoten dan dihitung dari
`max(berlaku_sampai, now())`. Order manual tidak pernah muncul sebagai tagihan
menunggu, sehingga rekonsiliasi tidak menanyakan transaksi ini ke Midtrans.

`catatan` **tersimpan** di kolom `pembayaran.catatan` (kolomnya nullable,
ditambah lewat `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` di `schema.sql`
sehingga instalasi lama ikut termigrasi). Isinya muncul sebagai kolom
**Catatan** di riwayat pembayaran halaman `/admin`, di JSON
`GET /admin/pelanggan/{kode}/pembayaran`, dan sebagai kolom (`catatan`) pada
CSV pembayaran — baik unduhan dari peramban maupun
`GET /admin/ekspor/pembayaran/{kode}.csv`. Pembayaran gateway membiarkannya
kosong: buktinya notifikasi Midtrans, bukan ketikan operator. Spasi di tepi
dibuang dan catatan berisi spasi saja diperlakukan kosong (NULL), jadi laporan
tidak pernah "terisi" tanpa isi.

### Lampiran gambar bukti transfer

Catatan teks tidak cukup saat bendahara desa meminta pertanggungjawaban: yang
dia butuhkan adalah gambar transfernya. Karena itu formulir *Tandai lunas* juga
menerima **berkas gambar** (opsional) — foto kuitansi ATM, tangkapan layar
m-banking, atau bukti QRIS — dan menyimpannya bersama baris pembayaran:

```bash
curl -X POST http://localhost:5000/admin/pelanggan/SUDES-0001/tandai-lunas \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID" -H "Content-Type: application/json" \
  -d "{\"paket\":\"bulanan\",\"nominal\":120000,\"catatan\":\"QRIS 17/10\",
       \"buktiBase64\":\"$(base64 -w0 bukti.png)\",\"buktiNama\":\"bukti.png\"}"
```

Aturannya dijaga `BuktiTransfer`, satu tempat yang menguji tanpa HTTP/database:

| Aturan | Alasan |
|---|---|
| Jenis ditentukan dari **isi** berkas (magic bytes), bukan dari `Content-Type` atau ekstensi | Berkas yang mengaku PNG tapi isinya PDF tetap ditolak, dan gambar asli diterima walau tipenya tidak disebut |
| Hanya JPEG, PNG, WEBP | Ketiganya bisa ditampilkan peramban tanpa risiko skrip; **SVG sengaja ditolak** karena bisa memuat JavaScript |
| Maksimum 2 MB | Cukup untuk tangkapan layar; lebih dari itu biasanya foto kamera mentah yang tidak perlu disimpan |
| Nama berkas hanya untuk tampilan | Jalur (`C:\Users\...`) dibuang, karakter kendali dipotong, panjang dibatasi 120 huruf |
| Lampiran yang ditolak membatalkan **seluruh** penandaan lunas | Kalau hanya lampirannya diabaikan, operator mengira bukti tersimpan padahal akses desa sudah bertambah tanpa jejak gambar |

Gambarnya **tidak** ikut di daftar riwayat (hanya jenis & ukurannya, mis.
`PNG · 240 KB`) supaya membuka satu desa dengan sepuluh pembayaran tidak
mengunduh puluhan megabyte. Isinya diambil satu per satu lewat
`GET /admin/pelanggan/{kode}/pembayaran/{orderId}/bukti`, yang tetap memerlukan
token operator, disajikan `inline` dengan `X-Content-Type-Options: nosniff` dan
`Cache-Control: private, no-store` — gambar transfer memuat nama pengirim dan
nominal, jadi ia tidak boleh terbaca dari luar maupun tinggal di cache disk
peramban bersama.

Halaman `/admin` mengecilkan gambar besar **di peramban lebih dulu** (sisi
maksimum 1600 px, JPEG) sebelum mengirim base64. Tanpa itu, foto 12 MP dari
ponsel operator selalu ditolak hanya karena kameranya bagus. Karena base64
membengkakkan isi ~4/3, batas body 128 KB yang berlaku untuk endpoint lain
dinaikkan **hanya** untuk jalur `POST .../tandai-lunas` (4 MB); endpoint lain
tetap 128 KB.

Yang tersisa sebagai batas: `catatan` bebas teks sehingga operator tetap bisa
menulis referensi yang tidak informatif, dan lampiran hanya tersimpan untuk
pembukuan manual — pembayaran gateway tidak punya gambar bukti serupa (bukti
setornya tetap dari bank/e-wallet pembayar). Untuk QR, nama
penerima yang tampil (`QrisPenerima`) dan NMID di dalam gambar tidak bisa lagi
menyimpang diam-diam: keduanya disusun dari `QrisNmid`/`QrisNama` dan diperiksa
uji terhadap isi QR yang dibundel.

Batas lain yang jujur: penjagaan ini berjalan saat build/uji, bukan saat
aplikasi dijalankan. Seseorang yang mengganti byte gambar di rakitan yang sudah
terpasang (mis. menambal DLL) tidak akan dihentikan aplikasi — yang dijaga
adalah rilis resmi tidak pernah berisi QR yang salah tanpa ketahuan lebih dulu.

## Kuitansi pembayaran (bukti untuk bendahara desa)

Desa membelanjakan uang APBDes untuk langganan ini. Tanpa bukti pengeluaran
yang menyebut penerima, jumlah, dan tanggal, pembelian sering tertahan di
pertanggungjawaban anggaran — bukan karena aplikasinya kurang, tetapi karena
tidak ada dokumennya. Karena itu setiap pembayaran bisa dicetak sebagai
kuitansi dari halaman `/admin`: kolom **Kuitansi** di riwayat pembayaran
membuka dokumen siap cetak di tab baru (tombol **Cetak / simpan PDF**).

Isinya diambil dari catatan pembayaran di database, **bukan** dari isian ulang:

| Bagian | Contoh |
|---|---|
| Nomor | `KW/202610/MANUAL-1750000000000` (tahun-bulan WIB + order id) |
| Pihak | Nama desa, kode aktivasi |
| Tanggal | `17 Oktober 2026` (WIB) |
| Jumlah | `Rp 120.000` + terbilang *seratus dua puluh ribu rupiah* |
| Uraian | Langganan SuDesApp — paket `bulanan`, total dibayar |
| Keterangan | Order ID, status **LUNAS**, referensi bayar (catatan operator), `Bukti transfer — PNG · 240 KB` bila lampiran ada, masa aktif saat ini |

Aturannya ada di `KuitansiPembayaran` (HTML murni, tanpa dependensi perender
PDF), jadi bisa diuji tanpa HTTP: nomor & tanggal memakai **WIB** (bukan UTC
mentah — tanggal yang meleset sehari membuat kuitansi tidak cocok dengan buku
kas desa), nominal memakai pemisah ribuan Indonesia, dan nama desa/catatan
operator di-escape karena dokumennya dibuka di peramban.

Lampiran gambar bukti transfer hanya **disebut** di kuitansi (jenis & ukurannya),
tidak ditanam di dokumen: kuitansi tetap ringan dibuka dan dicetak, sedangkan
gambarnya disimpan di pusat administer dan dibuka dengan kredensial operator. Ini juga
mencegah kuitansi yang dikirim ke bendahara ikut membawa gambar rekening.

Batas yang jujur: (1) tabel pembayaran tidak menyimpan rentang per pembayaran,
jadi yang dicetak adalah **masa aktif saat kuitansi dibuat** — diberi label
begitu, bukan diklaim sebagai periode yang dibeli; (2) tanggal memakai WIB,
belum menyesuaikan WITA/WIT; (3) dokumennya HTML siap cetak, bukan PDF yang
ditandatangani — kuitansi ini bukti internal, bukan bukti setor bank.

## Detak lisensi (heartbeat)

Saat jendela utama tampil, `App.OnStartup` memanggil `MulaiPemantauanLisensi()`:
satu cek langsung lalu berkala tiap 6 jam, di thread latar, tanpa menahan UI.
Hasilnya diterapkan ke `PenjagaLisensi` — sumber keputusan Demo/Trial/Full Service/baca-saja
(lihat *Penggatingan fitur*). Detak itu juga menyegarkan klaim `cek` di dalam
token supaya masa tenggang 7 hari dihitung dari koneksi terakhir yang
benar-benar berhasil. Bila kode aktivasi belum diisi, cek hanya membaca cache
dan tidak menghubungi server. Kegagalan jaringan wajar dan tidak pernah
ditampilkan sebagai galat.

### Denyut sambungan server

Selain detak lisensi di atas, `DetakSambunganServer` menanyakan `GET /healthz`
di latar belakang: tiap **30 detik** selama server belum tersambung dan tiap
**5 menit** setelah tersambung. Hasilnya dicatat ke `SambunganServerLisensi`;
halaman Langganan memakai langganan lemah ke event itu untuk memuat ulang
katalog saat keadaan berubah ke *Terhubung*, sehingga chip status pulih sendiri
tanpa membuka ulang halaman. Untuk pengembangan lokal aplikasi menunjuk
`http://127.0.0.1:5110` — port yang dipakai `.tmp-render/run-uji-server*.sh`
(server dijalankan dengan `--no-launch-profile`).

## Keamanan webhook

`/webhook-pembayaran` adalah pintu masuk yang menambah hak akses, jadi:

- **Fail closed.** Signature Midtrans (SHA512) wajib valid. Server **menolak
  start** bila `MIDTRANS_SERVER_KEY` kosong di luar mode Development —
  tanpa itu siapa pun bisa POST order palsu dan mendapat akses gratis.
- **Nominal divalidasi.** `gross_amount` harus sama dengan harga paket di
  server. Menutup jalur "bayar Rp1".
- **Idempoten.** `pembayaran.order_id` jadi PRIMARY KEY; insert dan
  perpanjangan dalam satu transaksi. Midtrans mengirim ulang webhook saat
  tidak menerima jawaban dalam 24 jam — tanpa proteksi ini satu transfer
  bisa menambah 31 hari berulang kali.
- **Harga ditentukan server.** `BuatTagihanRequest` sengaja tidak punya medan
  nominal.

## Rekonsiliasi pembayaran (backstop webhook)

Webhook adalah jalur utama, tapi ia bisa tidak pernah sampai: URL notifikasi
berubah, server sempat mati, atau kiriman Midtrans hilang. Pembeli yang uangnya
sudah masuk tetapi hak aksesnya tidak bertambah akan menelepon desa — dan tanpa
backstop, satu-satunya obatnya adalah memperbaiki data manual.

`LayananRekonsiliasi` menutup itu: ia mengambil daftar tagihan yang **belum punya
baris `pembayaran`**, menanyakan status tiap order langsung ke Midtrans
(`GET {api}/v2/{order_id}/status`, Basic auth ServerKey), lalu menyerahkan
hasilnya ke **`LayananWebhook.SelesaikanAsync` — fungsi yang sama** yang dipakai
webhook setelah signature diverifikasi.

Yang dijaga sama, tanpa salinan kedua:

| Aspek | Bagaimana |
|---|---|
| Aturan penyelesaian | Tagihan harus ada, nominal harus cocok, status harus `settlement`/`capture`, `fraud_status` kartu harus `accept` — satu fungsi untuk dua jalur |
| Idempotensi | `PRIMARY KEY pembayaran.order_id`. Order yang sudah diproses webhook tidak menambah hari saat rekonsiliasi menjumpainya lagi; urutan sebaliknya juga sama |
| Gagal tertutup | Status yang tidak bisa dipastikan (404 / HTTP gagal / jaringan mati) **tidak** dianggap lunas — dihitung `tidakPasti` dan dibiarkan |

Perbedaan proseduralnya cuma satu: di jalur ini tidak ada signature yang perlu
diverifikasi, karena statusnya diminta sendiri oleh server memakai ServerKey.

Penjadwalan: `RekonsiliasiLatar` (pekerja latar) menjalankan satu putaran tiap
`REKONSILIASI_INTERVAL_MENIT` menit (bawaan 15), dan `POST /admin/rekonsiliasi`
(operator berizin) menjalankannya saat itu juga. Tanpa `MIDTRANS_SERVER_KEY`
penjadwalan dimatikan dan alasannya tercatat di log — di mode pengembangan ini
keadaan normal, bukan galat. Pengaturan lengkapnya ada di
[deploy-server.md](deploy-server.md).

Satu catatan operasional: daftar tagihan diambil urut waktu terbit lalu dipotong
jumlahnya, jadi tagihan yang sudah mati (tidak akan pernah dibayar) ikut disaring
oleh batas umur maksimal — kalau tidak, ia akan menghabiskan kuota batch
selamanya dan tagihan baru tidak pernah ikut diperiksa.

## Uji pembayaran nyata: halaman `/checkout-uji`

Halaman `/checkout` melayani **pembeli**. Halaman `/checkout-uji` melayani
**operator**: tujuannya bukan menjual, tapi membuktikan bahwa alur "bayar →
notifikasi Midtrans → akses bertambah" benar-benar bekerja di akun merchant
yang sedang dipakai — termasuk di production.

| | `/checkout` | `/checkout-uji` |
|---|---|---|
| Siapa | Pembeli desa | Operator (izin `payments.test`) |
| Kode aktivasi | Milik desa yang membayar | Desa uji (didaftarkan otomatis, lalu dihapus) |
| Harta | Tagihan Snap sungguhan | Tagihan Snap sungguhan — **uang sungguhan** |
| Notifikasi | Ke "Payment Notification URL" di dashboard akun | Ke server ini lewat header `X-Override-Notification` (`MIDTRANS_NOTIFICATION_URL`) |
| Bukti | — | Jejak notifikasi: waktu, order, status, kanal, sah/tidaknya signature, HTTP balasan, hari yang bertambah |

Alurnya di halaman: masuk dengan ID + token operator → **Buat transaksi uji**
→ buka tautan Midtrans → **bayar sungguhan** → halaman menyegarkan diri tiap
5 detik dan menampilkan notifikasi yang masuk → **Hapus desa uji** untuk
membersihkan sisa jejak.

Yang dijaga di server (`LayananUjiPembayaran`), bukan di halaman:

- **Harga & durasi selalu dari katalog server.** Halaman hanya memilih nama
  paket; nominal tidak pernah dikirim klien — kalau boleh, orang bisa bayar
  Rp1 untuk sebulan akses.
- **Tagihan dicatat sebelum gateway dipanggil**, sama seperti jalur pembayaran
  pelanggan — jadi order id yang dipakai Midtrans selalu punya padanan di
  tabel `tagihan`, dan webhook tidak pernah menabrak order tak dikenal.
- **Tanpa ServerKey, endpoint menolak (503)** — bukan membuat tagihan tiruan
  yang mengaku transaksi sungguhan.
- **Perpanjangan tetap lewat `PeranjangAsync` yang sama** dengan webhook,
  jadi menguji jalur uji tidak melewati aturan idempotensi: notifikasi
  berulang tidak menambah hari dua kali.

Jejak notifikasi disimpan **di memori server saja** (`PelacakWebhook`, maksimum
100 catatan) dan hilang saat server restart. Yang permanence tetap satu: tabel
`pembayaran`. Jejak di memori sengaja tidak diarsipkan supaya server tidak
jadi gudang data pembayaran yang tak pernah dibersihkan.

## Komponen

| Berkas | Isi |
|---|---|
| [SuDesApp.Server/Program.cs](../SuDesApp.Server/Program.cs) | Endpoint + rate limiting + fail-closed |
| [SuDesApp.Server/PaketLangganan.cs](../SuDesApp.Server/PaketLangganan.cs) | Katalog paket: harga & durasi |
| [SuDesApp.Server/SigningService.cs](../SuDesApp.Server/SigningService.cs) | Penandatangan & kunci publik (ECDSA P-256) |
| [SuDesApp.Server/SupabaseStore.cs](../SuDesApp.Server/SupabaseStore.cs) | Pelanggan, langganan, tagihan, pembayaran, perangkat |
| [SuDesApp.Server/MidtransService.cs](../SuDesApp.Server/MidtransService.cs) | Snap, verifikasi signature, & query status transaksi (Core API) |
| [SuDesApp.Server/LayananWebhook.cs](../SuDesApp.Server/LayananWebhook.cs) | Aturan webhook (signature → tagihan → nominal → status → idempoten), terpisah agar bisa diuji |
| [SuDesApp.Server/LayananAktivasi.cs](../SuDesApp.Server/LayananAktivasi.cs) | Aturan `/aktivasi`: kode → batas perangkat → kedaluwarsa → token |
| [SuDesApp.Server/LayananAdmin.cs](../SuDesApp.Server/LayananAdmin.cs) | Aturan pusat administer: daftar/pendaftaran desa, daftar/hapus perangkat, ubah batas, & riwayat pembayaran |
| [SuDesApp.Server/LayananTagihan.cs](../SuDesApp.Server/LayananTagihan.cs) | Aturan `/buat-tagihan`: kode → paket & harga dari katalog server → order id → tagihan dicatat → gateway |
| [SuDesApp.Server/PembuatTagihan.cs](../SuDesApp.Server/PembuatTagihan.cs) | Antarmuka pembuat tagihan gateway (`IPembuatTagihan`) supaya jalur gagal bisa dipalsukan di uji |
| [SuDesApp.Server/LayananRekonsiliasi.cs](../SuDesApp.Server/LayananRekonsiliasi.cs) | Rekonsiliasi: selesaikan tagihan belum lunas lewat status gateway, memakai aturan webhook |
| [SuDesApp.Server/RekonsiliasiLatar.cs](../SuDesApp.Server/RekonsiliasiLatar.cs) | Penjadwal pekerja latar untuk rekonsiliasi |
| [SuDesApp.Server/StatusPembayaran.cs](../SuDesApp.Server/StatusPembayaran.cs) | Antarmuka sumber status pembayaran (bisa dipalsukan di uji) + model hasilnya |
| [SuDesApp.Server/IRepositoryLangganan.cs](../SuDesApp.Server/IRepositoryLangganan.cs) | Antarmuka penyimpanan + `LanggananMemori` untuk uji tanpa database |
| [SuDesApp.Server/schema.sql](../SuDesApp.Server/schema.sql) | Skema (bisa dijalankan dari SQL Editor) |
| [SuDesApp.Server/wwwroot/checkout.html](../SuDesApp.Server/wwwroot/checkout.html) | Halaman web `/checkout`: pilih paket → buat tagihan → bayar |
| [SuDesApp.Server/wwwroot/admin.html](../SuDesApp.Server/wwwroot/admin.html) | Halaman web `/admin`: pusat administer — daftar/ubah/hapus desa, kelola perangkat, riwayat pembayaran + CSV (token di `sessionStorage`, bukan di HTML) |
| [SuDesApp.Server/wwwroot/checkout-uji.html](../SuDesApp.Server/wwwroot/checkout-uji.html) | Halaman web `/checkout-uji`: buat transaksi uji → bayar di Midtrans → pantau notifikasi webhook yang masuk (token di `sessionStorage`, bukan di HTML) |
| [SuDesApp.Server/LayananUjiPembayaran.cs](../SuDesApp.Server/LayananUjiPembayaran.cs) | Aturan jalur uji: transaksi Snap sungguhan untuk desa uji, arahkan notifikasi, status gateway + jejak webhook |
| [SuDesApp.Server/PelacakWebhook.cs](../SuDesApp.Server/PelacakWebhook.cs) | Jejak notifikasi webhook di memori (maks. 100) untuk halaman uji |
| [SuDesApp.Server/KonfigurasiMidtrans.cs](../SuDesApp.Server/KonfigurasiMidtrans.cs) | Satu tempat membaca `MIDTRANS_SERVER_KEY` / `MIDTRANS_SANDBOX` / `MIDTRANS_NOTIFICATION_URL` (env menang atas appsettings; kosong = belum diisi) |
| [SuDesApp.Server/CsvLaporan.cs](../SuDesApp.Server/CsvLaporan.cs) | Pembentuk CSV laporan (desa & pembayaran) — satu aturan untuk peramban maupun CLI |
| [Dockerfile](../Dockerfile) | Image container untuk deploy server |
| [docs/deploy-server.md](deploy-server.md) | Panduan deploy (Railway/Render/VPS) + env vars + konfigurasi Midtrans |
| [SuDesApp.Core/Services/VerifikasiLisensi.cs](../SuDesApp.Core/Services/VerifikasiLisensi.cs) | Verifikasi tanda tangan di klien |
| [SuDesApp.Core/Services/LisensiManager.cs](../SuDesApp.Core/Services/LisensiManager.cs) | Klien lisensi + cache token |
| [SuDesApp.Core/Services/PenjagaLisensi.cs](../SuDesApp.Core/Services/PenjagaLisensi.cs) | Gerbang fitur + chip status: Demo / Trial / Full Service / Baca-saja |
| [SuDesApp.Core/Services/TrialLokal.cs](../SuDesApp.Core/Services/TrialLokal.cs) | Trial 14 hari lokal (`%LocalAppData%\SuDesApp\trial-lokal.json`), sekali per instalasi |
| [SuDesApp.Core/Services/RingkasanLangganan.cs](../SuDesApp.Core/Services/RingkasanLangganan.cs) | Penerjemah status lisensi → kalimat banner, nada warna, nama paket, dan petak ringkasan — satu-satunya tempat aturan “sah sampai” dan “masih tenggang” ditulis |
| [SuDesApp.Core/Services/LanggananKlien.cs](../SuDesApp.Core/Services/LanggananKlien.cs) | Klien endpoint publik langganan: katalog harga (`GET /paket`), pembuatan tagihan (`POST /buat-tagihan`), jeda coba ulang setelah gagal, & kebijakan tampilan harga (`KatalogLangganan`) |
| [SuDesApp.Core/Services/KatalogPaketTersimpan.cs](../SuDesApp.Core/Services/KatalogPaketTersimpan.cs) | Salinan katalog terakhir yang dikonfirmasi server (`%LocalAppData%\SuDesApp\katalog-paket.json`) supaya kartu tetap berangka saat server tak terjangkau |
| [SuDesApp.Wpf/ViewModels/LanggananViewModel.cs](../SuDesApp.Wpf/ViewModels/LanggananViewModel.cs) | Halaman Langganan (navigasi utama): keadaan yang diikat XAML + urutan tindakan pengguna. Kalimat status, angka paket, dan nominal disalin dari Core — halaman ini tidak punya aturan lisensi maupun perhitungan harga sendiri |
| [SuDesApp.Wpf/Views/LanggananView.xaml](../SuDesApp.Wpf/Views/LanggananView.xaml) | Tampilan halaman Langganan |
| [SuDesApp.Wpf/ViewModels/MainWindowViewModel.cs](../SuDesApp.Wpf/ViewModels/MainWindowViewModel.cs) | Menu rahasia **Pusat Administrasi** di bagian `LANGGANAN` (hanya untuk sesi akun pusat): membuka halaman operator server (`/admin`) di dalam aplikasi lewat WebView2 (profil privat), dengan tombol **Buka di Browser** sebagai cadangan; alamatnya dari `LisensiManager.AlamatPusatAdministrasi()` |
| [SuDesApp.Wpf/ViewModels/FeatureNavigationViewModel.cs](../SuDesApp.Wpf/ViewModels/FeatureNavigationViewModel.cs) | Tempat menu **Pusat Administrasi** ditambahkan ke sidebar — di dalam `if (AkunPusat.SesiIniPusat)`, jadi sesi lain tidak melihatnya sama sekali |
| [SuDesApp.Wpf/Views/PusatAdministrasiView.xaml.cs](../SuDesApp.Wpf/Views/PusatAdministrasiView.xaml.cs) | Halaman tertanam Pusat Administrasi: WebView2 profil InPrivate, tanpa devtools/autofill, membersihkan data situs saat ditutup, dan melaporkan galat HTTP/jaringan apa adanya |
| [SuDesApp.Wpf/Utilities/AkunPusat.cs](../SuDesApp.Wpf/Utilities/AkunPusat.cs) | Akun rahasia penyedia (`adminpusat`) — nama pengguna, nama tampilan, kata sandi bawaan, dan penilaian “sesi ini sesi pusat?” |
| [SuDesApp.Wpf/Utilities/BerkasLogin.cs](../SuDesApp.Wpf/Utilities/BerkasLogin.cs) | Bentuk & enkripsi berkas akun lokal `Login.dat` (DPAPI, multi-akun, penanda `pusat`) |
| [SuDesApp.Tests/](../SuDesApp.Tests/) | Uji Core & Server (xUnit): skema & kriptografi lisensi, migrasi Supabase, CSV laporan, penomoran, pembayaran, dan kebijakan status langganan (`RingkasanLanggananTests`) |

### Di mana halaman Langganan berada

Langganan adalah **halaman tersendiri di navigasi menu utama** (bagian
`LANGGANAN`). Posisinya **tepat di atas bagian `BANTUAN`**, yaitu persis setelah
menu terakhir `PENGATURAN` (Cadangkan & Pulihkan): ia berdampingan dengan
kelompok pengaturan/teknis di ujung daftar dan tidak lagi menyela menu kerja
harian (Buat Surat, Data Desa) di bagian atas sidebar. Ia tetap bukan bagian di
dalam Pengaturan Aplikasi: status masa aktif dan pembayaran adalah hal yang
dicari tersendiri, sementara Pengaturan Aplikasi berisi setelan teknis yang
jarang disentuh.

### Menu Pusat Administrasi (navigasi utama, rahasia)

Bagian `LANGGANAN` memuat satu menu tambahan, **Pusat Administrasi**, berdiri
tepat di bawah **Langganan** — ia ikut pindah bersama bagian itu ke atas
`BANTUAN`. Menu ini tidak membuka halaman WPF berisi data desa, melainkan
halaman operator di server lisensi (rute `/admin`, berkas `wwwroot/admin.html`)
yang ditampilkan **di dalam jendela aplikasi** lewat WebView2. Tombol **Buka di
Browser** (di bilah atas dan di panel galat) tetap tersedia sebagai cadangan
bila WebView2 Runtime tidak terpasang — alamat yang dibuka sama, hanya tanpa
penanda `sudesEmbedded`.

**Hanya sesi akun pusat yang melihatnya.** Akun itu (`adminpusat`) hidup di
berkas lokal `Login.dat` (terenkripsi DPAPI), bukan di tabel `Pengguna`, jadi ia
tidak muncul di halaman Kelola Pengguna — rinciannya di
[policy-peran-pengguna.md](policy-peran-pengguna.md) bagian 3. Menu ditambahkan
ke sidebar hanya bila `AkunPusat.SesiIniPusat` benar, dan tindakan membukanya
memeriksa ulang syarat itu sebelum halaman dimuat; penentunya adalah
penanda sesi (`SessionContext.SesiPusat`) yang hanya dinyalakan jalur login akun
pusat — akun database yang kebetulan bernama sama tetap tidak membukanya.

Alamatnya berasal dari `LisensiManager.AlamatPusatAdministrasi()` — yaitu
`AlamatEndpoint("admin")`, penyusun alamat yang sama dengan endpoint lain —
sehingga menu ini tidak bisa menunjuk server yang berbeda dari yang dipakai
halaman Langganan. Token pribadi operator tetap diketik di halaman itu
(`Lisensi:AdminOperators:<indeks>` di server) dan tidak pernah ikut ke aplikasi
desa; yang dilindungi akun pusat adalah keterlihatan menunya, bukan halaman
operatornya.

**Server yang dituju tampil di sidebar, bukan hanya setelah halaman dimuat.**
Baris menu **Pusat Administrasi** menampilkan alamat tujuannya sebagai baris
kecil di bawah judul: `Server: lisensi.contoh.go.id`, atau
`Server: localhost:5000` untuk server uji — jadi server uji dan server produksi
tidak bisa tertukar diam-diam sebelum token admin diketik. Yang tampil hanya
host dan port (bila bukan port bawaan); jalur `/admin` dan token tidak pernah
masuk ke sidebar. Teks itu disegarkan oleh `LisensiManager.AlamatBerubah`
setiap kali alamat benar-benar berubah — mis. setelah **Simpan & Uji** di
halaman Langganan — sehingga sidebar tidak pernah menyebut server lama. Saat
sidebar diciutkan (label disembunyikan), alamat yang sama tetap terbaca di
tooltip barisnya.

Dua hal yang dijaga halaman tertanam ini: (1) **sesi masuk tidak disimpan** —
halaman `/admin` dibuka dengan penanda `sudesEmbedded=1`, sehingga token
operator hanya hidup di memori JavaScript dan tidak ditulis ke
`sessionStorage`; profil WebView2-nya InPrivate di folder sementara milik
profil tunggal, tanpa devtools/autofill, dan data situsnya dibersihkan saat
halaman ditutup; (2) **status koneksi jujur** — kegagalan jaringan *dan*
jawaban HTTP 4xx/5xx (mis. rute `/admin` belum ada di server versi lama)
ditampilkan sebagai galat dengan kode yang jelas, bukan "Terhubung".
Bila alamat server belum diatur, aplikasi menampilkan pesan pengarahan ke
halaman Langganan, bukan memuat halaman dari alamat kosong.

### Struktur halaman Langganan

Halaman terdiri dari empat kartu bertumpuk:

1. **Status Langganan** — menampilkan keadaan lisensi (Aktif s.d. / Tidak
   aktif) dengan kotak berwarna mengikuti tone (hijau/kuning/merah), ditambah
   empat petak ringkasan: **PAKET**, **SAH SAMPAI**, **SISA HARI**, dan
   **TERAKHIR DIPERIKSA**. Tombol **Cek Status** memanggil
   `LisensiManager.CekAsync()` dan menerapkan hasilnya ke `PenjagaLisensi`
   seketika.

   **Audit 2026-10-19 — masa tenggang tidak lagi tampil sebagai "Aktif s.d.
   tanggal yang sudah lewat":** baris status dulu memakai tanggal bayar, jadi
   saat langganan sudah lewat tetapi aplikasi masih sah karena masa tenggang
   7 hari, layar menampilkan tanggal di masa lalu dengan label "Aktif" —
   kalimat yang saling meniadakan tepat saat desa perlu tahu sisa waktunya.
   Sekarang satu rumus di Core (`LisensiManager.BatasSah`, yang sama dipakai
   gerbang fitur) menentukan batas sah efektif `max(tanggal bayar, cek terakhir
   + 7 hari)`; baris status dan petak **SAH SAMPAI** menampilkan tanggal itu,
   dan nadanya kuning (perlu perpanjang) bukan hijau selama tanggal bayar sudah
   lewat. `StatusLisensi.SahSampai` membawa angkanya, jadi halaman tidak
   menghitung sendiri.
2. **Paket Langganan** — tiga kartu berdampingan (Bulanan Rp150.000 / 6 Bulan
   Rp750.000 / Tahunan Rp1.200.000), masing-masing menyebut harga per hari dan
   penghematannya, dengan satu kartu bertanda **PALING HEMAT**. Harga dan durasi
   diisi dari `GET /paket` (katalog server); sebelum server menjawab, kartu
   menampilkan teks tanpa angka. Di bawah kartu ada **Coba Gratis 14 Hari**
   (hanya tampil selama langganan belum aktif) dan tombol **Bayar /
   Perpanjang** yang memanggil `POST /buat-tagihan` lalu membuka link
   pembayaran QRIS/VA di peramban. Teks kartu dan langkah panduan memakai
   `TextWrapping` di dalam Grid — bukan StackPanel horizontal — supaya kalimat
   panjang membungkus, bukan terpotong di tepi kartu.

   **Audit 2026-10-19 — tagihan sudah dibuat ≠ peramban gagal dibuka:**
   `Process.Start` dulu berada di dalam blok `try` yang sama dengan pembuatan
   tagihan, sehingga kegagalan membuka peramban dilaporkan sebagai "Gagal
   membuat tagihan" — padahal tagihannya SUDAH tercatat di server. Pesan itu
   mengundang desa membuat tagihan kedua, dan itu berarti dua tagihan untuk satu
   pembayaran. Sekarang pembukaan peramban punya jalurnya sendiri
   (`CobaBukaPeramban`): kalau gagal, pesannya menyebut tagihan sudah dibuat
   beserta **link pembayarannya sebagai teks** supaya bisa dibuka manual.
   Untuk jalur QRIS statis, kalimat pengarah yang memuat nominal ikut
   disegarkan saat paket berganti selagi panel terbuka (QRIS statis tidak
   membawa nominal, jadi angka di kalimat itulah yang ditransfer desa), dan
   panel ditutup otomatis begitu langganan terbaca aktif.
3. **Kode Aktivasi** — input teks untuk kode aktivasi desa (contoh
   `SUDES-XXXX-001`). Tombol **Simpan & Cek** menyimpan kode ke preferensi
   lalu langsung mengecek status.
4. **Server Lisensi** — input alamat server lisensi. Tombol **Simpan & Uji**
   menyimpan URL lalu menguji koneksi dengan langsung mengecek status
   (seluruh simpan-uji berjalan sebagai satu operasi sibuk: dulu tombol ini
   satu-satunya yang tidak mengunci, sehingga dua klik beruntun menjalankan dua
   pemuatan katalog + dua cek status yang saling menimpa, dan tombol Bayar/Trial
   tetap bisa ditekan di tengah pengujian alamat).
   Chip keadaannya (**Belum diatur / Belum diuji / Terhubung / Belum
   terjangkau**) dinilai dari **hasil panggilan sungguhan**, bukan dari bentuk
   alamatnya: alamat `localhost` yang servernya benar-benar menjawab (server uji
   di komputer sendiri) dihitung *Terhubung*. Sebelumnya setiap alamat yang
   memuat `localhost`/`127.0.0.1` selalu dilabeli "Belum diatur" beserta pesan
   "Cek Status dan pembayaran akan gagal" — padahal pada alamat itu status dan
   pembayaran justru berjalan normal. Keadaan diubah oleh pemuatan katalog
   (saat halaman dibuka, sesudah Simpan & Uji, dan sesudah Cek Status);
   percobaan yang sengaja dilewati karena jeda coba ulang tidak mengubahnya,
   dan mengetik alamat baru mengembalikannya ke *Belum diuji*.

   **Alamat yang sudah terhubung terkunci.** Setelah server benar-benar
   menjawab, kotak alamat menjadi read-only agar salah ketik tidak menggeser
   server yang sedang dipakai. Tombol **Ubah alamat** membuka kunci itu secara
   sadar untuk sesi berjalan. Di build rilis, `Lisensi:UrlServer` yang berisi
   URL HTTPS produksi juga mengunci alamat secara permanen: preferensi lama di
   `%LOCALAPPDATA%\SuDesApp\login_prefs.json` tidak lagi bisa menimpanya, dan
   halaman Langganan hanya menampilkan keterangan bahwa alamat dikunci
   konfigurasi. Aturan pemilihan alamat ada di satu tempat,
   `SuDesApp.Core/Services/PenentuAlamatLisensi.cs`, dan dijaga uji
   `PenentuAlamatLisensiTests`.

### HttpClient bersama (audit 2026-10-14)

`LanggananViewModel` sebelumnya membuat `new HttpClient` di setiap panggilan
`MuatHargaKatalogAsync` dan `BayarLanggananAsync` — anti-pattern yang bisa
menyebabkan socket exhaustion di bawah beban berulang. Sekarang klien HTTP
disuntik via DI (singleton `HttpClient` yang sama dipakai `LisensiManager`),
sehingga koneksi di-reuse dan tidak ada kebocoran socket.

### Pembagian tugas di klien (audit 2026-10-14)

Pekerjaan halaman Langganan dipisah menurut siapa yang berhak memiliki datanya,
supaya perubahan bentuk balasan server atau aturan harga tidak menyentuh
tampilan (dan sebaliknya):

| Bagian | Pemilik | Isi |
|---|---|---|
| Endpoint publik server | `SuDesApp.Core/Services/LanggananKlien.cs` | `GET /paket` & `POST /buat-tagihan`: permintaan, bentuk JSON, jeda coba ulang setelah gagal, dan penulisan salinan offline |
| Alamat server | `LisensiManager.AlamatEndpoint` | Satu-satunya tempat alamat endpoint disusun — dipakai klien lisensi maupun klien katalog/tagihan |
| Kebijakan tampilan harga | `KatalogLangganan` + `PaketLanggananKlien` (satu berkas dengan `LanggananKlien`) | Rupiah, harga per hari, persen hemat, penanda “paling hemat”, dan teks cadangan sebelum angka server tiba |
| Token & cache lisensi | `LisensiManager` | `/aktivasi`, `/kunci-publik`, verifikasi tanda tangan, masa tenggang |
| Gerbang fitur | `PenjagaLisensi` | Full Service / Trial / Demo / baca-saja |
| Status → kalimat & angka | `RingkasanLangganan` (Core) | Judul+nada banner, keterangan, nama paket, “sah sampai”, sisa hari, terakhir diperiksa, dan tampil-tidaknya tombol trial |
| Halaman | `LanggananViewModel` | Keadaan yang diikat XAML + urutan tindakan pengguna; semua teks/angka status disalin dari `RingkasanLangganan` |

Aliran datanya satu arah: **server → Core → ViewModel → XAML**. ViewModel tidak
pernah menyusun permintaan ke server maupun menghitung nominal; ia memanggil
`LanggananKlien` lalu menampilkan hasilnya, jadi angka yang tampil dan angka yang
ditagih tidak bisa berbeda.

### Struktur halaman setelah audit 2026-10-22

Halaman Langganan tidak lagi menyimpan aturan lisensi di dalam ViewModel-nya.
Batasnya sekarang:

- **Kebijakan → Core.** `RingkasanLangganan.Dari(status, kodeAktivasi)` adalah
  satu-satunya tempat status diterjemahkan menjadi kata-kata: batas **sah**
  efektif (bukan tanggal bayar), nada kuning saat tanggal bayar lewat tetapi
  tenggang masih berjalan, dan tombol trial yang hanya tampil bila belum aktif.
  Sebelumnya aturan tanggal itu ditulis dua kali — di banner dan di petak KPI —
  sehingga keduanya bisa menyimpang. ViewModel kini hanya menyalin hasilnya,
  dan `RingkasanLanggananTests` menguji semua keadaan itu tanpa membuka jendela.
- **Satu penjagaan sibuk.** `JalankanSibukAsync` memiliki aturan “satu operasi
  pada satu waktu” untuk Cek Status, Simpan & Cek, Simpan & Uji, Trial, dan
  Bayar. Lima salinan penjagaan lama dihapus; “Simpan & Uji” kini juga menahan
  penanda sibuk selama cek status yang dipicunya, bukan melepasnya di tengah.
- **Satu penulis banner.** Semua pesan sementara (galat simpan, petunjuk QRIS,
  hasil salin rincian) lewat `Beritahu(nada, kalimat)`, sehingga nada warna dan
  kalimatnya tidak bisa berbeda. Pesan status tetap dari `RingkasanLangganan`.
- **Yang dihapus** karena tidak ada yang membacanya: `AdaStatus`,
  `UrlServerTerkunciKonfigurasi`, `KeadaanServerTerakhir`,
  `PaketOpsi.HargaDiketahui`, dan lima penanda `Tag="langganan-…"` di XAML yang
  tidak dipakai otomasi mana pun.
- **Yang sengaja tetap di ViewModel:** kartu paket (`PaketOpsi`), panel QRIS
  manual, dan kartu alamat server. Ketiganya berbagi keadaan dengan alur yang
  sama (pilihan paket menentukan nominal QRIS; simpan alamat memicu muat katalog
  dan cek status), jadi memecahnya jadi ViewModel anak hanya menambah saling
  panggil tanpa memisahkan tanggung jawab.

Aturan yang sama berlaku untuk halaman lain yang memuat data di latar belakang:
pekerjaan murni (baca database, cocokkan data wilayah) boleh berjalan di utas
lain, tetapi setiap perubahan keadaan yang diikat XAML dijalankan di utas
halaman — lihat `SetelanViewModel.JalankanDiUtasHalamanAsync`. Alasannya nyata:
kelanjutan `await` tanpa `SynchronizationContext` berjalan di utas kolam, dan WPF
menolak perubahan koleksi terikat dari utas lain (gejala: pratinjau/kartu kosong
sesaat, dan pada halaman Pengaturan Surat dulu malah gagal memuat).

### Ukuran huruf halaman (audit 2026-10-19)

Skala baku aplikasi adalah **10 / 11 / 13 / 14 / 15 / 20 / 24 / 28**
(dijaga `UkuranHurufStandarTests`). Halaman Langganan sekarang seluruhnya
berada di skala itu:

| Elemen | Sebelum | Sesudah |
|---|---|---|
| Deskripsi kartu (`LanggananDesc`) | 12 | **13** |
| Teks langkah panduan (`LangkahTeks`) | 12 | **13** |
| Label petak ringkasan (`KpiLabel`) | 10.5 | **11** |
| Nomor langkah (`LangkahNomorTeks`) | 11.5 | **11** |
| Judul section, ikon, lencana header | 15 / 13 | tetap (sudah di skala) |

Alasan celah ini bisa tumbuh: `UkuranHurufStandarTests` hanya membaca bentuk
atribut (`FontSize="12"`), sedangkan nilai di dalam Style ditulis sebagai
`<Setter Property="FontSize" Value="12"/>` — tidak pernah diperiksa. Audit
2026-10-19 menutup celah itu di sumbernya: penjaga sekarang membaca **tiga**
bentuk (atribut, Setter, literal C#) di seluruh `SuDesApp.Wpf`, bukan hanya
daftar folder tertentu. 34 nilai di luar skala pada 11 berkas lain (12 / 12.5 /
11.5 / 10.5 / 16 / 17 / 22) ikut dipetakan dengan skrip standariser, jadi
aturan ini berlaku menyeluruh — bukan hanya halaman Langganan. Penjaga khusus
halaman yang semula ditambahkan untuk menambal celah ini dihapus agar aturannya
tidak punya dua salinan yang bisa menyimpang.

## Uji

```bash
dotnet test SuDesApp.Tests/SuDesApp.Tests.csproj
```

639 uji dalam beberapa kelompok (lisensi, katalog paket, klien langganan,
tampilan halaman Langganan, webhook, aktivasi, tagihan, penggatingan, serta uji
unit lainnya di seluruh aplikasi):

**`LisensiKeamananTests`** — `Token_Server_DapatDiverifikasi_OlehVerifikatorKlien`
membuktikan format token server dan klien saling kompatibel (bukan dua
implementasi yang hanya diuji masing-masing), dan tiga tes "ditolak"
membuktikan penyuntingan payload, pemunduran `cek`, dan kunci asing semuanya
ditolak.

**`KatalogPaketTests`** — menjaga agar paket tahunan tidak pernah lebih
mahal per hari daripada bulanan, entri tidak lengkap tidak menimpa paket
dengan harga Rp0, dan harga/hari nol ditolak saat muat (konfigurasi salah
harus menggagalkan start, bukan diam-diam menerbitkan tagihan gratis).

**`LanggananKlienTests`** + **`LanggananViewModelTests`** — sisi klien katalog:
kartu tidak pernah mengarang angka (tanpa katalog server → teks tanpa rupiah,
bukan Rp0), salinan offline hanya untuk tampilan, percobaan setelah gagal
ditunda 30 detik kecuali saat pengguna menekan sendiri tombolnya, permintaan
tagihan tidak pernah memuat nominal, dan kebijakan tampilan (harga per hari,
persen hemat, penanda “paling hemat”) diuji langsung tanpa membuka jendela.
Chip keadaan server ikut diuji di situ: alamat localhost yang menjawab ditandai
*Terhubung* (bukan *Belum diatur*), server yang tidak menjawab ditandai *Belum
terjangkau* dengan pesan yang menyebut sebabnya, alamat kosong tetap *Belum
diatur* tanpa memanggil server sama sekali, dan mengetik alamat baru
mengembalikan chip ke *Belum diuji*. Sejak audit 2026-10-19 juga dijaga: masa
tenggang (tanggal bayar lewat → baris status memakai tanggal sah efektif dan
nada kuning, bukan hijau), panel QRIS ditutup saat status terbaca aktif, dan
kalimat pengarah QRIS ikut berubah begitu paket berganti selagi panel terbuka.

**`LanggananTampilanKontrakTests`** — setiap nama yang diikat XAML di halaman
Langganan harus benar-benar ada di ViewModel halaman atau di kartu paketnya:
pengikatan yang salah sasaran tidak menggagalkan build, ia hanya membuat kolom
kosong diam-diam — persis risiko saat keadaan dipindah antar kelas. Sejak audit
2026-10-19 uji yang sama juga menjaga aturan teks-panjang-membungkus. Skala
ukuran huruf halaman (termasuk nilai di dalam
`<Setter Property="FontSize" Value="…"/>`) tidak lagi dijaga di sini, karena
`UkuranHurufStandarTests` sekarang memeriksanya untuk seluruh aplikasi.

**Menu navigasi utama** — aturan menu **Pusat Administrasi** tidak diuji lewat
unit test (menu dibangun di lapisan WPF, sementara proyek uji hanya merujuk
Core & Server): alamat `/admin` mengikuti alamat server yang sama dengan
endpoint lisensi lain dan kosong bila alamat server belum diatur — supaya menu
tidak membuka alamat tak berarti — dan penambahannya berada di dalam syarat
akun pusat. Karena itu perubahan pada menu ini hanya terlihat dari aplikasi
yang dijalankan, bukan dari build; aturan sesi pusatnya sendiri dijelaskan di
[policy-peran-pengguna.md](policy-peran-pengguna.md) bagian 3.

**`BerkasLoginTests`** — berkas akun lokal: isinya benar-benar enkripsi DPAPI
(bukan teks biasa — diperiksa dari byte berkasnya), berkas versi lama yang hanya
memuat satu akun tetap terbaca, penyemaian akun pusat idempoten dan tidak
mengembalikan kata sandi yang sudah diganti, akun pusat **tidak pernah** dibuat
ke tabel `Pengguna` (service palsu mencatat bila ada upaya), dan menu pusat
tertutup untuk sesi akun biasa maupun akun database bernama sama.

**`LayananWebhookTests`** — aturan pemberi hak akses diuji tanpa HTTP dan
tanpa database lewat `LanggananMemori`: signature salah ditolak 403 (termasuk
saat `ServerKey` kosong — gagal tertutup, bukan terbuka), order tak dikenal
404, nominal tidak cocok 400, status bukan-dana-masuk tidak memperpanjang,
webhook yang dikirim berulang hanya menambah masa aktif **sekali** (idempoten),
durasi mengikuti paket yang ditagihkan, dan masa aktif baru menumpuk dari sisa
tanpa menghapus hari yang belum terpakai.

**`AktivasiTests`** — aturan `/aktivasi` diuji tanpa HTTP dan tanpa database:
batas perangkat (perangkat ke-2 di atas batas ditolak 403 tanpa token,
perangkat yang ditolak **tidak** mengunci komputer yang sah, perangkat yang
sama diulang tidak menghabiskan kuota, tanpa `idPerangkat` tidak dibatasi),
kedaluwarsa (belum pernah bayar / sudah berakhir / jatuh tempo tepat sekarang
tidak menerbitkan token; klaim `exp`, `iat`, `cek` token diverifikasi ulang),
dan kode aktivasi sebagai kredensial (kosong 400, tak dikenal 404).

**`BuatTagihanTests`** — aturan `/buat-tagihan` diuji dengan gateway palsu:
harga & durasi selalu dari katalog server (kode tak dikenal jatuh ke paket
bawaan, bukan gratis), angka yang tersimpan di tabel tagihan sama dengan yang
ditagihkan ke gateway, tagihan sudah tercatat **sebelum** gateway dipanggil,
format order id (termasuk id desa pendek dan tanggal tanpa `Kind`),
mode demo tanpa token, dan gateway gagal → 500 sementara tagihannya tetap
tercatat supaya rekonsiliasi bisa menyusul. Satu aturan yang justru wajib:
desa yang belum membayar tetap boleh membuat tagihan — itu jalan keluarnya.

**`PenjagaLisensiTests`** — aturan alur uji coba 14 hari: instalasi baru yang
belum pernah trial berkeadaan `Demo` dan terkunci, trial yang berjalan membuka
menu vital, trial yang habis mengunci lagi walau belum pernah membayar, kode
aktivasi tanpa status aktif tetap terkunci, dan langganan aktif menang atas
sisa trial. Daftar rute vital diuji dari dua sisi (rute pembuatan surat/data
terkunci; Register, Riwayat, Langganan, dan pencadangan tetap terbuka), begitu
pula `TrialLokal`: berjalan tepat 14 hari, tidak bisa diulang, dan bertahan
setelah objek/instans aplikasi baru.

**`MulaiPercobaanTests`** — urutan trial server vs lokal, diuji dengan HTTP
palsu: tanpa kode aktivasi langsung trial lokal tanpa menghubungi server; kode
ada + server menolak 403 → tanpa cadangan lokal; server tak terjangkau atau 5xx
→ cadangan lokal; server menerima tetapi token belum tiba → `ServerTertunda`
tanpa trial lokal; trial lokal yang sudah dipakai tidak diulang; dan status HTTP
yang dianggap penolakan tegas (400/401/403/409) dipisahkan dari gangguan sesaat
(404/408/429/5xx).

**`StatusMidtransTests`** — pembacaan status transaksi dari Core API, diuji
dengan HTTP palsu (tanpa jaringan): jawaban "order belum ada" — yang
sebenarnya dibalas Midtrans sebagai **HTTP 200** dengan
`{"status_code":"404","status_message":"Transaction doesn't exist."}` — harus
berarti "tidak bisa dipastikan", **bukan** "transaksi ditemukan"; jawaban
yang benar-benar memuat status diteruskan apa adanya, dengan `gross_amount`
tetap TEKS asli (`"150000.00"`, bukan `150000`).

### Uji end-to-end ke Supabase & Midtrans sungguhan

`Tools/uji-e2e-server-lisensi.sh` menjalankan **seluruh** pemeriksaan server
lisensi lewat HTTP terhadap Supabase dan akun Midtrans yang **sungguhan**
(production atau sandbox, mengikuti konfigurasi server), lalu membersihkan
jejaknya:

```bash
# server harus sudah berjalan (Supabase + MIDTRANS_SERVER_KEY terisi)
OPERATOR_TOKEN=... TUNEL_PUBLIK=https://tunnel.example \
  Tools/uji-e2e-server-lisensi.sh
```

Yang diperiksa: endpoint publik & ketiga halaman, gerbang token/izin admin
(401/403), pendaftaran & perubahan data desa, aktivasi + trial + **verifikasi
tanda tangan ES256 token terhadap `/kunci-publik`**, batas perangkat
(perangkat yang ditolak tidak mengunci kuota), pembukuan manual + lampiran
bukti transfer (yang sah tersimpan, yang ditolak membatalkan seluruh
penandaan), transaksi Snap **nyata**, semua pintu penolakan webhook (tanpa
signature, kunci salah, signature tidak cocok dengan isi body, nominal Rp1,
order tak dikenal, bukan JSON), perpanjangan idempoten, rekonsiliasi, ekspor
CSV, siklus pengumuman, serta penghapusan desa (409 selama ada jejak uang,
lalu hapus paksa).

**Idempoten:** kode desa uji tetap (`UJI-E2E-SERVER`, bisa diganti lewat
`KODE_UJI`); sisa run yang terputus dihapus lebih dulu, pembersihan akhir
berjalan lewat `trap` walau uji gagal di tengah, dan jumlah desa setelah
putaran sama dengan sebelumnya. Satu hal yang tidak bisa dibersihkan: order
Snap yang dibuat tetap tercatat sebagai transaksi **belum dibayar** di
dashboard Midtrans — skrip mencetak order id-nya untuk dihapus manual.

Dua pemeriksaan butuh konfigurasi tambahan dan **dilewati** (bukan gagal) bila
tidak ada: 403 izin (setel `OPERATOR_ID_TERBATAS` + `OPERATOR_TOKEN_TERBATAS`
untuk operator berizin terbatas) dan tagihan uji ikut direkonsiliasi (server
memakai umur minimum bawaan 10 menit; setel `REKONSILIASI_MINIMAL_UMUR_MENIT=0`).
Skrip ini tidak menyalakan server sendiri — header berkasnya memuat contoh
environment variable operator, Supabase, dan Midtrans.

## Konfigurasi

Server (`SuDesApp.Server/appsettings.lokal.json`, di luar git):

| Kunci | Kegunaan |
|---|---|
| `Supabase:Host`, `Supabase:DbPassword` | Koneksi Postgres |
| `Midtrans:ServerKey` | ServerKey Snap (wajib di produksi) |
| `Midtrans:UseSandbox` | `true` saat uji |
| `Langganan:Paket:<kode>:Nama/Harga/Hari` | Katalog paket (mis. `bulanan` 50000/31, `tahunan` 500000/365) |
| `Lisensi:KeyPath` | Lokasi kunci privat (default `keys/lisensi-signing.key`) |
| `Lisensi:AdminOperators:<indeks>` | Operator bernama (`Id`, `Name`, `Token`, `Permissions`); token unik minimal 32 byte |

Mendaftarkan desa baru (operator harus punya `customers.create`):

```bash
curl -X POST http://localhost:5000/admin/pelanggan \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID" \
  -H "Content-Type: application/json" \
  -d '{"kodeAktivasi":"SUDES-0001","namaDesa":"Desa Sukamaju","maxPerangkat":2}'
```

Tanpa operator terkonfigurasi endpoint ini menolak semua permintaan (503). Identitas/token salah menghasilkan `401`; izin yang tidak cukup menghasilkan `403`.

### Pusat administer: kelola desa & perangkat

Bila aktivasi ditolak karena kuota perangkat penuh, operator perlu membebaskan
slot (mis. pelanggan mengganti komputer); pendaftaran & perbaikan data desa juga
dilayani di sini. Setiap permintaan memakai token pribadi dan ID operator
(`Authorization: Bearer <token>` dan `X-Admin-Operator: <id>`), dengan izin sesuai tugas:

| Izin | Aksi |
|---|---|
| `customers.read`, `customers.create`, `customers.update`, `customers.delete`, `customers.export` | Membaca, mendaftarkan, mengubah, menghapus, dan mengekspor data desa |
| `devices.read`, `devices.manage` | Membaca dan mengelola perangkat desa |
| `payments.read`, `payments.record`, `payments.export`, `payments.reconcile` | Membaca, mencatat, mengekspor pembayaran, dan menjalankan rekonsiliasi |
| `payments.test` | Jalur uji pembayaran nyata (halaman `/checkout-uji`) — **terpisah** dari `payments.record`, karena endpoint-nya membuat transaksi sungguhan di akun merchant |
| `announcements.read`, `announcements.manage` | Membaca dan mengelola pengumuman |

Endpoint sesi hanya mengembalikan identitas operator yang sedang login dan
daftar izinnya; setiap operator dapat melihat sesi miliknya sendiri.

| Endpoint | Kegunaan |
|---|---|
| `GET /admin/pelanggan` | Daftar semua desa + jumlah perangkat (`penuh` bila sudah mentok) |
| `GET /admin/pelanggan/{kode}` | Data satu desa (nama, kontak, batas) |
| `POST /admin/pelanggan` | Daftarkan desa baru (`{"kodeAktivasi","namaDesa","kontak","maxPerangkat"}`) |
| `PUT /admin/pelanggan/{kode}` | Ubah nama & kontak desa (`{"namaDesa","kontak"}`); kode tetap |
| `DELETE /admin/pelanggan/{kode}` | Hapus desa; **ditolak `409`** bila masih ada tagihan/pembayaran |
| `GET /admin/pelanggan/{kode}/perangkat` | Perangkat terdaftar satu desa |
| `DELETE /admin/pelanggan/{kode}/perangkat/{id}` | Hapus satu perangkat |
| `DELETE /admin/pelanggan/{kode}/perangkat` | Reset SEMUA perangkat desa |
| `POST /admin/pelanggan/{kode}/batas` | Ubah batas perangkat (`{"batas": 3}`) |
| `GET /admin/pelanggan/{kode}/pembayaran` | Riwayat pembayaran (dana masuk) satu desa |
| `GET /admin/pelanggan/{kode}/pembayaran/{orderId}/kuitansi` | **Kuitansi pembayaran** siap cetak (HTML) untuk satu pembayaran |
| `GET /admin/pelanggan/{kode}/pembayaran/{orderId}/bukti` | **Lampiran gambar bukti transfer** satu pembayaran (JPEG/PNG/WEBP, `inline`, `no-store`) |
| `POST /admin/pelanggan/{kode}/tandai-lunas` | Catat pembayaran QRIS manual/transfer (`{"paket","nominal","catatan","buktiBase64","buktiNama"}`) — langsung memperpanjang masa aktif; order berawalan `MANUAL` |
| `GET /admin/ekspor/desa.csv` | Unduh seluruh daftar desa sebagai berkas CSV |
| `GET /admin/ekspor/pembayaran/{kode}.csv` | Unduh riwayat pembayaran satu desa sebagai CSV |

Antarmuka webnya ada di **`/admin`** (`http://localhost:5000/admin` saat
lokal). Operator memasukkan ID dan token pribadi sekali per tab; keduanya disimpan di
`sessionStorage` dan dikirim sebagai header `X-Admin-Operator` dan `Authorization` di setiap
permintaan. Server mencatat ID operator, izin, rute, hasil, dan trace ID per permintaan admin; retensinya mengikuti konfigurasi log deployment. Dari halaman ini operator
bisa: **mendaftarkan desa baru**, melihat daftar desa (beserta kolom "penuh"
dan **status langganan** + penyaring bertanggal), **mengubah atau menghapus
desa**, membuka perangkat satu desa, menghapus satu/semua perangkat, mengubah
batas, melihat **riwayat pembayaran** (beserta tombol **unduh CSV**, tautan
**Kuitansi**, dan tautan **Lihat bukti** untuk pembayaran yang punya lampiran
gambar), dan menjalankan rekonsiliasi.

### Ekspor CSV: peramban vs CLI

Dua jalan, satu bentuk berkas:

| Jalur | Cara |
|---|---|
| Halaman `/admin` | Tombol "Unduh CSV desa" / "Unduh CSV" — dibangun di peramban |
| CLI (tanpa peramban) | `Tools/Admin-Lisensi.ps1 -Aksi ekspor-desa` / `-Aksi ekspor-bayar -Kode …` |

Bentuk kolomnya sengaja dibuat **identik** oleh `CsvLaporan.cs` (server) dan
fungsi CSV di `admin.html` (peramban) — kalau keduanya punya aturan sendiri,
laporan dari CLI bisa berbeda kolom dari laporan peramban tanpa ada yang sadar.
Berkasnya UTF-8 **dengan BOM**: tanpa BOM, Excel versi Indonesia salah membaca
huruf beraksen dalam nama desa. Nilai yang memuat koma/kutip dikutip dan kutip
di dalamnya digandakan, sehingga `Desa Kuta, Bali` tidak merusak kolom.

Kolom CSV pembayaran: `order_id,paket,nominal,status,dicatat,catatan,bukti`.
Kolom `bukti` hanya berisi keterangan lampiran (`JPEG · 240 KB`), bukan
gambarnya — berkas 2 MB per baris tidak masuk akal untuk laporan, dan yang
perlu terlihat dari laporan hanyalah baris mana yang punya bukti transfer.

### Hapus desa: dua tingkat perlindungan

Menghapus desa membuang **tagihan dan pembayaran** (jejak uang), jadi ada dua
tingkat:

| Kondisi desa | Tanpa konfirmasi | Dengan `?konfirmasi=<kode>` |
|---|---|---|
| Belum pernah bertagihan | ✅ langsung terhapus | ✅ terhapus (sama saja) |
| Ada tagihan **belum** dibayar | ❌ `409` + jumlah tagihan | ✅ terhapus beserta tagihannya |
| Ada **pembayaran** (uang masuk) | ❌ `409` + jumlah tagihan & pembayaran | ⚠️ terhapus beserta jejak uangnya |

Nilai `konfirmasi` harus **kode aktivasi yang sama persis** — `true`, `ya`, atau
string apa pun yang cocok sebagian **tidak** dihitung, dan keliru akan mendapat
`400`. Ini pengganti kotak dialog: satu klik tidak bisa menghapus jejak uang,
tapi operator yang memang berniat tetap punya jalan — dan jalannya punya jejak
di log (level Warning, dengan jumlah tagihan & pembayaran yang musnah).

```bash
# Ditolak — masih ada pembayaran:
curl -X DELETE http://localhost:5000/admin/pelanggan/SUDES-0001 \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"

# Konfirmasi salah:
curl -X DELETE "http://localhost:5000/admin/pelanggan/SUDES-0001?konfirmasi=true" \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"

# Hapus paksa (memang disengaja):
curl -X DELETE "http://localhost:5000/admin/pelanggan/SUDES-0001?konfirmasi=SUDES-0001" \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"
```

Sengaja **tidak** ada penghapusan berantai otomatis dari sisi client, tidak ada
`undelete`, dan tidak ada soft-delete: desa yang terhapus paksa benar-benar
hilang. Kalau nanti perlu pemulihan, itu perubahan desain (kolom `dihapus_pada`),
bitu bukan tambalan kecil.

Tarif operasi admin dibatasi 30 permintaan/menit per IP (`admin` policy), cukup
lapang untuk satu operator sambil tetap menutup enumerasi massal.

```bash
# Desa mana saja yang sudah penuh?
curl http://localhost:5000/admin/pelanggan -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"

# Perangkat desa SUDES-0001
curl http://localhost:5000/admin/pelanggan/SUDES-0001/perangkat -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"

# Riwayat pembayaran desa SUDES-0001
curl http://localhost:5000/admin/pelanggan/SUDES-0001/pembayaran -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"

# Perbaiki nama desa
curl -X PUT http://localhost:5000/admin/pelanggan/SUDES-0001 \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID" -H "Content-Type: application/json" \
  -d '{"namaDesa":"Desa Sukamaju","kontak":"0812-3456"}'

# Hapus satu perangkat agar komputer baru bisa aktivasi
curl -X DELETE http://localhost:5000/admin/pelanggan/SUDES-0001/perangkat/pc-lama \
  -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"
```

Tanpa menulis curl, [Tools/Admin-Lisensi.ps1](../Tools/Admin-Lisensi.ps1)
membungkus semuanya:

```powershell
$env:SUDES_ADMIN_OPERATOR_ID = 'operator-dukungan'
$env:SUDES_ADMIN_OPERATOR_TOKEN = '<token>'
./Tools/Admin-Lisensi.ps1 -Aksi pelanggan
./Tools/Admin-Lisensi.ps1 -Aksi perangkat  -Kode SUDES-0001
./Tools/Admin-Lisensi.ps1 -Aksi hapus      -Kode SUDES-0001 -Perangkat pc-lama
./Tools/Admin-Lisensi.ps1 -Aksi reset      -Kode SUDES-0001
./Tools/Admin-Lisensi.ps1 -Aksi batas      -Kode SUDES-0001 -Batas 3
./Tools/Admin-Lisensi.ps1 -Aksi pembayaran -Kode SUDES-0001
./Tools/Admin-Lisensi.ps1 -Aksi daftar     -Kode SUDES-0002 -NamaDesa 'Desa Baru' -Batas 3
./Tools/Admin-Lisensi.ps1 -Aksi ubah       -Kode SUDES-0001 -NamaDesa 'Desa Sukamaju' -Kontak '0812-3456'
./Tools/Admin-Lisensi.ps1 -Aksi hapus-desa -Kode SUDES-0002
./Tools/Admin-Lisensi.ps1 -Aksi ekspor-desa
./Tools/Admin-Lisensi.ps1 -Aksi ekspor-bayar -Kode SUDES-0001
```

Menghapus perangkat tidak menyentuh data desa maupun masa langganan: perangkat
akan tercatat ulang begitu komputer yang sah melakukan aktivasi berikutnya.
Aksi operator dicatat di log server (level Warning), jadi "kenapa kuota desa ini
berubah" selalu punya jejak.

Aplikasi (`appsettings.json`):

| Kunci | Kegunaan |
|---|---|
| `Lisensi:UrlServer` | Alamat server lisensi |
| `Lisensi:KunciPublik` | Kunci publik base64url — **isi sebelum rilis** |
| `Lisensi:KunciTanpaLangganan` | Warisan konfigurasi lama. Instalasi baru kini SELALU terkunci sampai trial 14 hari dimulai atau lisensi dibayar, jadi kunci ini tidak lagi mengubah perilaku |

Bila `KunciPublik` kosong, aplikasi mengambilnya sekali dari
`/kunci-publik` lalu memetiknya di `%LocalAppData%`.

## Menjalankan server lokal tanpa Supabase (Development)

Untuk menguji aplikasi dan menu **Pusat Administrasi** tanpa database, jalankan
server dengan environment `Development`. Bila `SUPABASE_HOST` dan
`SUPABASE_DB_PASSWORD` kosong, server memakai `LanggananMemori` dan
`PengumumanMemori`: `/healthz`, `/paket`, `/kunci-publik`, `/admin`, serta alur
aktivasi/pembayaran bisa dicoba, tetapi **seluruh data hilang saat server
berhenti**. Mode ini tidak pernah aktif di Production — tanpa kredensial
Supabase, server Production tetap menolak start.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:SUDES_ADMIN_OPERATORS__0__ID = 'operator-lokal'
$env:SUDES_ADMIN_OPERATORS__0__NAME = 'Operator Lokal'
$env:SUDES_ADMIN_OPERATORS__0__TOKEN = '<token-acak-minimal-32-byte>'
$env:SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0 = 'session.read'
$env:SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1 = 'customers.read'
dotnet run --project SuDesApp.Server
```

Aplikasi desa yang masih memakai `Lisensi:UrlServer = http://localhost:5000`
akan menampilkan chip **Terhubung** setelah halaman Langganan dibuka. Menu
Pusat Administrasi memakai alamat yang sama (`/admin`) dan operator masuk
dengan ID/token di atas. Untuk produksi, tetap isi kredensial Supabase dan
pakai URL HTTPS nyata.

> Catatan audit 2026-10-22: environment variable
> `SUDES_ADMIN_OPERATORS__<indeks>__*` sebelumnya **tidak pernah terbaca**
> karena provider environment menghasilkan kunci `SUDES_ADMIN_OPERATORS:*`,
> bukan `Lisensi:AdminOperators:*`. Server sekarang memetakannya secara
> eksplisit saat start. Tanpa perbaikan itu, deployment dengan env yang benar
> tetap menolak semua login admin dengan HTTP 503.

## Koneksi Supabase (status 2026-10-13)

**Terhubung dan terverifikasi end-to-end.** Proyek `jotnrhjzyslyaozzawwq`
(region **ap-northeast-2 / Seoul**) diakses lewat **Session pooler**:

```json
"Supabase": {
    "Host": "<host Session pooler persis dari Dashboard Connect>",
    "Port": 5432,
    "User": "sudes_server.jotnrhjzyslyaozzawwq",
    "DbPassword": "<kata sandi role sudes_server>",
    "MigrateUser": "postgres.jotnrhjzyslyaozzawwq",
    "MigratePassword": "<kata sandi role postgres>",
    "SslMode": "Require"
}
```

Dua role dengan pembagian tugas yang tegas: **`sudes_server`** untuk melayani
permintaan (RLS berlaku), **`postgres`** hanya untuk menerapkan `schema.sql`
saat start dan boleh dikosongkan (`MigratePassword`) bila skema dijalankan
manual dari SQL Editor.

Yang membuat pencarian awal lama (162 kombinasi ditolak) adalah **region yang
salah**: dikira `ap-northeast-1` (Tokyo), ternyata `ap-northeast-2` (Seoul).
Dua hal yang mudah salah pada koneksi pooler:

1. **Username wajib berformat `<user>.<project-ref>`** (`postgres.<ref>`),
   bukan `<ref>` saja dan bukan `postgres`.
2. **Host pooler** berbentuk `aws-<index>-<region>.pooler.supabase.com`. Salin persis dari Dashboard → Connect; indeks cluster tidak bisa diturunkan dari region, jadi jangan menebak `aws-0`.

Kesalahan yang muncul bila salah: `XX000 (ENOTFOUND) tenant/user ... not found`
(region/tenant salah) dan `XX000 (ENOIDENTIFIER) no tenant identifier provided`
(username tanpa `<ref>`).

### Isolasi antar-desa (tenant_id + RLS, 2026-10-13)

Setiap tabel operasional punya `tenant_id` (= `pelanggan.id`). Kelima tabel
memakai RLS dengan predicate `tenant_id = (SELECT tenant_aktif())` — dibungkus
subquery agar dievaluasi sekali per kueri, bukan per baris — dan `tenant_aktif()`
membaca GUC `app.tenant_id` yang disetel server **per transaksi** dari desa yang
sudah diverifikasi — tidak pernah dari klien. Tanpa GUC: nol baris.
`pengumuman` juga ber-RLS, dengan kebijakan terpisah: baca publik, tulis hanya
`sudes_server` (hak tulis `anon`/`authenticated` dicabut di `schema.sql`).

Di Supabase, role `postgres` punya **BYPASSRLS**, jadi memakai role itu berarti
seluruh isolasi dilewati tanpa peringatan. Karena itu server memakai
`sudes_server` dan **menolak start** bila role koneksinya istimewa atau RLS-nya
belum aktif. Pencarian yang memang lintas-tenant semuanya lewat fungsi
`SECURITY DEFINER` yang hanya bisa dipanggil role server dan hanya
mengembalikan kolom yang diperlukan: `dir_cari_pelanggan(kode)`,
`dir_cari_tagihan(order_id)`, `dir_tagihan_belum_dibayar(...)` (rekonsiliasi),
dan `dir_daftar_pelanggan()` (pusat administer). Operasi tulis pusat administer
tidak menambah fungsi baru: setelah desa ditentukan dari kode aktivasi, baris
yang disentuh dibatasi RLS seperti transaksi biasa.

| Uji terhadap Supabase sungguhan (Postgres 17.11) | Hasil |
|---|---|
| `schema.sql` dijalankan server sendiri lewat koneksi migrasi (termasuk blok `DO $$ … $$` dan policy) | ✅ |
| Role tanpa kata sandi → server menolak start dengan instruksi `ALTER ROLE sudes_server PASSWORD '…'` | ✅ |
| Pooler Supabase butuh waktu sesaat untuk menerima kata sandi yang baru disetel | ✅ terukur ±17 detik; percobaan kedua lolos |
| `postgres`: `bypassrls=true`; `sudes_server`: `super=false, bypassrls=false` | ✅ |
| Menyajikan server dengan role `postgres` | ✅ DITOLAK start ("melewati RLS") |
| Tanpa `app.tenant_id` — `pelanggan` / `tagihan` / `perangkat` | ✅ 0 / 0 / 0 baris |
| Konteks tenant A: `tagihan` terlihat=1, baris tenant lain=0, order milik B tak tampak | ✅ |
| INSERT baris tenant B dengan konteks A | ✅ DITOLAK `42501 row-level security policy` |
| Kontrol lewat owner: 2/2 tagihan uji memang ada (hasil nol bukan karena data kosong) | ✅ |
| `PerpanjangAsync` di bawah RLS, lalu webhook ulang | ✅ settle B, ulangan `duplikat=True`, A tidak ikut diperpanjang |
| Batas perangkat dihitung per tenant (desa berbatas 1) | ✅ perangkat ke-2 ditolak |
| Batas perangkat lewat HTTP sungguhan (desa berbatas 1): pc-1 → pc-2 → pc-1 lagi | ✅ `200 / 403 / 200`; `perangkat` nyata berisi **hanya** `pc-1` |
| `/buat-tagihan` paket `tahunan` lewat HTTP | ✅ `nominal=500000`, `hari=365`, `mode=demo` |
| `/buat-tagihan` paket `GRATIS` (tidak dikenal) lewat HTTP | ✅ jatuh ke `bulanan` `nominal=50000`, bukan gratis |
| `/buat-tagihan` kode kosong / tidak dikenal | ✅ 400 / 404 |
| `/buat-tagihan` saat gateway menolak (ServerKey palsu) | ✅ 500 `Midtrans gagal`, **dan barisnya ada di tabel** `tagihan` (Rekonsiliasi menyusul) |
| Data uji dibersihkan setelah pengujian | ✅ 3 desa uji dihapus |
| Data uji sesi ekstraksi ini dibersihkan | ✅ 5 desa uji dihapus (`SUDES-BATAS-*`, `SUDES-VERIF-*`, `SUDES-GW-*`, beserta tagihan/perangkatnya lewat CASCADE) |

### Hasil uji end-to-end (server lokal → Supabase sungguhan)

| Uji | Hasil |
|---|---|
| Skema dibuat saat start | ✅ `pelanggan, langganan, pembayaran, perangkat, tagihan` |
| `POST /admin/pelanggan` tanpa token | ✅ 401 |
| `POST /admin/pelanggan` dengan token | ✅ pelanggan tersimpan |
| `GET /admin/pelanggan` tanpa token / dengan token | ✅ 401 / daftar desa + jumlah perangkat |
| `GET /admin/pelanggan/{kode}/perangkat` | ✅ daftar perangkat desa; kode tak dikenal → 404 |
| `DELETE /admin/pelanggan/{kode}/perangkat/{id}` | ✅ perangkat terhapus & komputer baru diterima `/aktivasi` |
| `DELETE /admin/pelanggan/{kode}/perangkat` | ✅ semua perangkat direset |
| `POST /admin/pelanggan/{kode}/batas` (`{"batas":3}`) | ✅ batas berubah; `{"batas":0}` → 400 |
| `POST /admin/pelanggan` kode/nama kosong | ✅ 400 (validasi LayananAdmin) |
| `GET /admin/pelanggan/{kode}/pembayaran` | ✅ riwayat dana masuk + paket; kode tak dikenal → 404; tanpa token → 401 |
| `GET /admin/pelanggan/{kode}` | ✅ nama/kontak/batas; kode tak dikenal → 404; tanpa token → 401 |
| `PUT /admin/pelanggan/{kode}` | ✅ nama & kontak berubah (kode tetap); nama kosong → 400; kode tak dikenal → 404 |
| `DELETE /admin/pelanggan/{kode}` | ✅ desa (& perangkat) terhapus; kode tak dikenal → 404; tanpa token → 401 |
| `DELETE /admin/pelanggan/{kode}` saat ada pembayaran | ✅ **409** — jejak uang tidak dihapus |
| `GET /admin` (halaman pusat administer) | ✅ 200 HTML; token diketik operator, tidak tertanam |
| `GET /admin/ekspor/desa.csv` | ✅ `text/csv` + `Content-Disposition`; BOM `ef bb bf`; tanpa token → 401 |
| `GET /admin/ekspor/pembayaran/{kode}.csv` | ✅ 5 kolom; kode tak dikenal → 404 |
| CLI `-Aksi ekspor-desa` / `-Aksi ekspor-bayar` | ✅ berkas tersimpan utuh; tanpa `-Kode` → exit 1 |
| `GET /paket` | ✅ `bulanan` + `tahunan` |
| `/aktivasi` sebelum bayar | ✅ `aktif=false`, tanpa token |
| Webhook tanpa signature | ✅ 403 (gagal tertutup) |
| Webhook signature sah tapi nominal Rp1 | ✅ 400 |
| Webhook signature sah, order tak dikenal | ✅ 404 |
| Webhook sah | ✅ 200, `berlaku_sampai` +365 hari |
| Webhook sah dikirim 4× | ✅ hanya **1** baris `pembayaran`, `total_terbayar=1` (idempoten) |

### Uji ulang 2026-10-22 — langsung ke Supabase sungguhan (bukan lokal)

Pemeriksaan ini sengaja **tidak** memakai Supabase lokal: server dijalankan
dengan `SuDesApp.Server/appsettings.lokal.json` yang menunjuk Session pooler
proyek `jotnrhjzyslyaozzawwq` (region ap-northeast-2). Kunci publishable proyek
dipakai langsung sebagai *klien penyerang* untuk menguji apa yang sebenarnya
bisa dibaca kunci publik itu.

| Uji | Hasil |
|---|---|
| Start server terhadap Supabase sungguhan | ✅ log start: `Isolasi RLS terverifikasi: role 'sudes_server' tidak istimewa, 7/7 tabel ber-RLS, fungsi pencarian siap` |
| `GET /paket` | ✅ 150000 / 750000 / 1200000 (harga katalog resmi) |
| `POST /admin/pelanggan` tanpa token / token salah | ✅ 401 / 401 |
| `POST /admin/pelanggan` dengan token | ✅ desa tersimpan di Supabase |
| `POST /aktifkan-trial` | ✅ token bertanda tangan, berlaku 14 hari |
| `POST /buat-tagihan` | ✅ order `SUDES-…`, nominal 150000 |
| `POST /admin/pelanggan/{kode}/tandai-lunas` | ✅ langganan diperpanjang, order `MANUAL-…` |
| `POST /aktivasi` | ✅ token paket `bulanan` |
| `GET /admin/ekspor/desa.csv` dan `pembayaran/{kode}.csv` | ✅ CSV kanonik (BOM, waktu ISO-8601) |
| Izin kurang (`customers.export` / `payments.reconcile` tidak diberikan) | ✅ 403 — pagar izin bekerja |
| Webhook tanpa signature | ✅ 403 "Signature tidak valid" |
| Pengumuman: buat → baca publik → hapus | ✅ 200 / 200 / 200 |
| `DELETE /admin/pelanggan/{kode}` tanpa konfirmasi saat ada pembayaran | ✅ 409 — jejak uang tidak terhapus |
| `DELETE /admin/pelanggan/{kode}?konfirmasi={kode}` | ✅ terhapus beserta pembayarannya |
| **Kunci publishable** → `GET /rest/v1/pelanggan` | ✅ **401 permission denied** (sebelum diperbaiki: seluruh data desa terbaca) |
| **Kunci publishable** → `GET /rest/v1/tagihan` | ✅ 401 |
| **Kunci publishable** → `POST /rest/v1/rpc/dir_daftar_pelanggan` | ✅ 401 permission denied for function |
| **Kunci publishable** → `GET /rest/v1/pengumuman` | ✅ 200 — baca publik memang diizinkan |
| Jejak uji di database | ✅ 0 baris di semua tabel (termasuk `admin_action_audit`) |
| `supabase db push --db-url` (tanpa `link`) | ✅ "Remote database is up to date" |

Catatan: `supabase link` sendiri belum bisa dijalankan di mesin ini karena
`supabase login` menuntut **personal access token** (`sbp_…`), sedangkan yang
tersedia hanya kunci publishable (`sb_publishable_…`) — bukan jenis token yang
diterima CLI. Semua pemeriksaan di atas tidak bergantung pada CLI: semuanya
lewat Postgres langsung dan PostgREST.

### Uji 2026-10-22 — Midtrans production sungguhan (bukan sandbox)

Server yang sama (Supabase sungguhan) dijalankan dengan kredensial merchant
Midtrans **production** (`Mid-server-…`, `UseSandbox=false`). Server lisensi
tidak pernah memakai `ClientKey`/`Merchant ID`: Snap dibuka lewat
`redirect_url` di peramban, jadi hanya `ServerKey` yang relevan.

| Uji | Hasil |
|---|---|
| `POST /buat-tagihan` | ✅ order `SUDES-…`, mode `midtrans`, tautan `https://app.midtrans.com/snap/v4/redirection/<token>` (host production, bukan sandbox) |
| Order hasil tagihan di `GET /v2/{order_id}/status` | ⚠️ `{"status_code":"404","status_message":"Transaction doesn't exist."}` — dan balasannya **HTTP 200**, bukan 404; order Snap yang belum dibuka pembeli belum punya catatan di Core API (halaman Snap-nya sendiri 200) |
| Webhook tanpa signature | ✅ 403 "Signature tidak valid" |
| Webhook dengan signature SHA512 asli (`SHA512(order_id+status_code+gross_amount+server_key)`, `gross_amount` = `"150000.00"` seperti dikirim Midtrans) | ✅ 200, `diterima: true`, `hariDitambah: 30` |
| Webhook yang sama dikirim ulang | ✅ `duplikat: true`, tanpa tambahan hari |
| Webhook signature sah tapi `gross_amount` Rp1 | ✅ 400 "Nominal tidak sesuai tagihan" |
| Jejak di database | ✅ baris `pembayaran` (`status: settlement`, 150000) + `berlaku_sampai` +30 hari |
| `POST /admin/rekonsiliasi` (operator berizin `payments.reconcile`) | ✅ `diperiksa: 1, tidakPasti: 1` — status ke Midtrans production benar-benar ditanyakan; order yang tidak dikenal dijawab **gagal tertutup**, bukan dianggap lunas |
| Jejak uji di database setelah pembersihan | ✅ 0 baris di `pelanggan`/`tagihan`/`pembayaran`/`langganan` |

Dua hal yang perlu diketahui operator:

- **Rekonsiliasi butuh pembayaran yang benar-benar dimulai.** Order Snap yang
  belum dibuka pembeli tidak ada di Core API, jadi satu putaran rekonsiliasi
  terlalu dini (`REKONSILIASI_MINIMAL_UMUR_MENIT`) tidak akan menyelesaikannya.
  Itu perilaku yang benar: status yang tidak bisa dipastikan tidak pernah
  diperlakukan sebagai "lunas". Catatan: Core API menjawab "order belum ada"
  itu dengan **HTTP 200** dan `status_code: "404"` di dalam body — jadi
  penentuannya dibaca dari `transaction_status` di body, bukan dari kode HTTP.
- **Pengujian di production meninggalkan tagihan yang belum dibayar.** Dua
  order Snap uji (`SUDES-89f22cc7-20261022-46fb96` dan
  `SUDES-6a907319-20261022-e4eafc`) masih tercatat sebagai pending di dasbor
  merchant. Dana tidak masuk karena tidak pernah dibayar; hapus manual dari
  dasbor Midtrans bila tidak diinginkan.

### Uji 2026-10-22 — halaman uji pembayaran (`/checkout-uji`) di production

Server yang sama dijalankan dengan `MIDTRANS_NOTIFICATION_URL` menunjuk URL
publik tunnel, lalu operator memakai halaman `/checkout-uji` (izin
`payments.test`) untuk membuat transaksi Snap production sungguhan. Notifikasi
dikirim ke URL publik itu dengan bentuk PERSIS seperti yang dikirim Midtrans
(`gross_amount` bertulis `"150000.00"`, signature SHA512 dihitung dari
ServerKey yang sama).

| Uji | Hasil |
|---|---|
| `GET /checkout-uji` (lewat URL publik) | ✅ 200 `text/html`; halaman statis tanpa kredensial tertanam |
| `GET /admin/uji-pembayaran` | ✅ `lingkungan: production`, `notifikasiDialihkan: true`, `gatewayAktif: true` |
| `POST /admin/uji-pembayaran` (desa uji) | ✅ order `SUDES-0755da0d-20261022-7ce1f6`, token Snap + tautan `https://app.midtrans.com/snap/v4/redirection/…` |
| Order itu di Midtrans production | ⚠️ 404 di Core API — order Snap yang belum dibuka pembeli belum punya catatan (perilaku yang sudah diketahui, bukan kegagalan) |
| Status transaksi di gateway untuk order yang belum dibayar | ✅ "belum ada" (`ditemukan: false`, catatan "Gateway menjawab status_code 404: Transaction doesn't exist.") — bukan "?", karena Core API membalas HTTP 200 + `status_code: "404"` di body |
| Notifikasi tanpa signature ke URL publik | ✅ 403 "Signature tidak valid" — gagal tertutup |
| Notifikasi bertanda tangan SHA512 ke URL publik | ✅ 200, `diterima: true`, `hariDitambah: 30` — langganan desa uji bertambah 30 hari |
| Notifikasi yang sama dikirim ulang | ✅ `duplikat: true`, tanpa tambahan hari |
| Notifikasi tanpa signature untuk order lain | ✅ 403 |
| Jejak notifikasi di halaman (`GET /admin/uji-pembayaran/{orderId}`) | ✅ waktu, order, status transaksi, kanal, signature sah, HTTP 200, hari bertambah |
| Pembersihan (`DELETE /admin/pelanggan/{kode}?konfirmasi={kode}`) | ✅ desa uji + 1 tagihan + 1 pembayaran terhapus; `GET …/pembayaran` → 404; tidak ada sisa di daftar desa |

Batasnya yang jujur: notifikasi di atas **disimulasikan** — signature dihitung
dengan ServerKey yang sama dan badannya persis seperti kiriman Midtrans — agar
seluruh jalur server teruji tanpa harus benar-benar transferring uang.
Notifikasi **benar-benar** dari Midtrans hanya terjadi setelah operator
membayar di halaman Snap; itu langkah yang tidak bisa dilakukan tanpa
kartu/e-wallet sungguhan. Halaman `/checkout-uji` dibuat persis untuk langkah
itu: begitu pembayaran selesai, notifikasi aslinya masuk dan halaman
menampilkannya (waktu, order, status, kanal, signature, HTTP balasan, hari
yang bertambah).

### Skema

`SuDesApp.Server/schema.sql` — tempel ke **SQL Editor → Run**. Aman
diulang (semua `CREATE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, dan
`DROP POLICY` sebelum `CREATE POLICY`). Isinya kini mencakup `tenant_id`,
kebijakan RLS, role `sudes_server` beserta grant-nya, dan fungsi pencarian
lintas-tenant (`dir_cari_pelanggan`, `dir_cari_tagihan`,
`dir_tagihan_belum_dibayar`, `dir_daftar_pelanggan`).

Berkas ini juga **ditanam ke assembly server** dan dijalankan dari sana saat
start bila `SUPABASE_MIGRATE_PASSWORD` diisi — satu sumber kebenaran, bukan
salinan kedua di C#. Tanpa kredensial migrasi, server tidak mengubah skema dan
hanya memverifikasi bahwa RLS sudah aktif.

> ⚠️ **Putar kata sandi DB sebelum produksi.** Kata sandi yang dipakai selama
> percakapan ini sudah terekspos di riwayat chat — putar di
> Dashboard → Settings → Database.

## Penggatingan fitur & alur trial 14 hari (status 2026-10-24)

Empat keadaan akses, ditentukan `PenjagaLisensi` (Core) dari status lisensi
BERTANDA TANGAN plus trial lokal:

| Keadaan | Kapan | Akses |
|---|---|---|
| `Berlangganan` | Token sah (dalam masa aktif / tenggang) | **Penuh.** Menang atas trial |
| `Trial` | Trial lokal 14 hari masih berjalan | **Penuh sementara** |
| `Demo` | Instalasi baru, trial belum pernah dimulai | **Terkunci** + popup ajakan trial |
| `BacaSaja` | Trial habis, atau langganan berakhir, atau kode aktivasi tanpa pembayaran | **Terkunci** + popup wajib bayar |

Urutan itu penting dan ditulis sekali di `PenjagaLisensi.HitungKeadaan`:
langganan aktif menang atas trial, trial aktif menang atas kunci, dan `Demo`
dibedakan dari `BacaSaja` supaya popup yang muncul tepat (ajakan memulai trial
vs kewajiban membayar). `HitungTerkunci` dan `ApakahMenuVital` diturunkan dari
aturan yang sama, jadi gerbang navigasi tidak punya daftar kedua.

**Saat aplikasi dijalankan.** Setelah jendela utama tampil (bukan menahan
penggambaran awal), `App.TampilkanPemberitahuanLisensi` membaca keadaan dari
cache lalu:

- `Demo` → popup “Mulai masa uji coba 14 hari”. Ya → `MulaiPercobaanAsync()`
  (trial server bila kode aktivasi ada & server terjangkau, selain itu trial
  lokal; gerbang langsung terbuka, chip berubah). Tidak → popup “Menu vital
  terkunci” dan menu tetap terkunci.
- `BacaSaja` → popup peringatan setiap kali aplikasi dibuka: “Masa uji coba
  berakhir” (trial lokal habis) atau “Lisensi belum aktif” (langganan
  berakhir), dengan tawaran membuka halaman Langganan untuk membayar.
- `Trial` / `Berlangganan` → tidak ada popup.

**Menu vital.** Rute yang membuat/mengubah data administrasi dan mencetak
dikunci selama keadaan terkunci; daftarnya di `PenjagaLisensi.RuteVital`
dan diperiksa di satu tempat (`MainWindowViewModel.ResolveFeatureRoute`):

- Rute tetap: `template-surat`, `rekening-koran`, `ntcr-paket`, `daftar-hadir`,
  `warga`, `perangkat-desa`, `sotk-desa`, `apbdes`, `idm`, `formulir`,
  `pengaturan-surat`, `pengaturan-formulir`, `penomoran-surat`, `kelola-pengguna`.
- Awalan berparameter: `surat:`, `keputusan:`, `agenda:`, `template-pdf:`.
- Menekan menu terkunci tidak membuka halaman: `Demo` menawarkan memulai trial
  (dan langsung membuka rute yang tadi diminta bila pengguna setuju);
  `BacaSaja` menampilkan pesan wajib bayar lalu membuka halaman Langganan.

**Trial server didahulukan.** `MulaiPercobaanAsync` memilih jalur trial dengan
urutan tetap:

| Kondisi | Yang diberikan | Mengapa |
|---|---|---|
| Langganan sudah aktif | Tidak ada trial | Tidak perlu, server pun akan menolak |
| Kode aktivasi ada & server menjawab menerima | **Trial server** | Sekali per desa (`trial_sudah`), token bertanda tangan, batas perangkat |
| Kode ada, server tak terjangkau (timeout/5xx/404/408/429) | **Trial lokal** | Desa tetap bisa mencoba saat offline; gangguan server tidak mengunci desa |
| Kode ada, server menjawab MENOLAK (400/401/403/409 dst.) | Tidak ada — arahkan bayar | Kalau diberi lokal, satu desa bisa mengumpulkan 28 hari dari dua jalur |
| Kode belum ada | **Trial lokal** | Instalasi pertama memang belum punya kode |
| Trial lokal sudah pernah dipakai | Tidak ada | Trial lokal tidak bisa diulang |

Keputusan “server menjawab menolak” vs “gangguan sesaat” ada di
`PenjagaLisensi.MenolakFinal` dan diuji tanpa jaringan (`MulaiPercobaanTests`).
Balasan server yang menerima trial tetapi tokennya belum sampai ke komputer
diberi hasil `ServerTertunda` — pengguna diminta Cek Status, dan **tidak**
diberi trial lokal, supaya hitungannya tidak dobel.

Penguncian itu **terlihat sebelum menu diklik**: setiap menu vital yang
terkunci diberi gembok di ujung kanan baris, judul/ikonnya diredupkan, dan
tooltip-nya menyebut jalan keluarnya — “klik untuk mulai trial 14 hari” pada
`Demo`, “klik untuk menyelesaikan pembayaran lisensi” setelah trial habis.
Akordeon ikut bergembok bila SELURUH isinya terkunci (mis. **Buat Surat Baru**),
termasuk saat daftarnya masih terlipat; akordeon campuran seperti **Formulir**
tidak diberi gembok di header agar tidak menjanjikan kunci yang tidak ada.
Penandaan ini menyalin keputusan `PenjagaLisensi` yang sama
(`KunciMenuSidebar` → `NavItem.Terkunci`), jadi tidak mungkin berbeda dari
gerbang rutenya, dan langsung dilepas begitu trial dimulai atau langganan aktif.

Sengaja TETAP TERBUKA: Register Surat, Riwayat, Register NTCR, Verifikasi,
Beranda, Laporan Penduduk, halaman Langganan, pencadangan/pemulihan, dan
bantuan. Data desa harus tetap bisa dibaca, diekspor, dan diselamatkan walau
lisensi belum dibayar — mengunci semuanya justru membuat desa kehilangan data.

Selain gerbang rute, lapis dalam tetap berlaku: `SuratSaveService` menolak
`SimpanBaruAsync` / `SimpanDalamTransaksiAsync` saat terkunci
(`KunciLisensiException`), dan tombol **Cetak** pada pratinjau PDF ikut mati
(PDF masih bisa diekspor). Pintasan Beranda menuju form surat lewat jalur yang
sama (`LoadSuratAsync`), jadi tidak ada jalan memutar.

Chip keadaan di status bar (bisa diklik → halaman Langganan) selalu tampil dan
membedakan keempat keadaan dari satu sumber (`PenjagaLisensi.Keadaan`):
**Full Service** (hijau), **Trial** (kuning, “sisa N hari”), **Demo** (kuning),
dan **Baca-saja** (merah). Status dipantau detak lisensi tiap 6 jam, dan halaman
Langganan menerapkannya seketika setelah Cek Status — pengguna yang baru bayar
tidak menunggu detak berikutnya. Karena aturannya statis (`HitungKeadaan`),
keputusan “terkunci?” tidak bergantung pada jaringan: offline saat tenggang
berakhir tetap terkunci.

> **Batas trial lokal.** Waktu mulai disimpan di berkas dan bisa diakali dengan
> menghapus/mengeditnya. Ini keputusan produk yang diterima: trial hanyalah
> jembatan menuju langganan berbayar yang diverifikasi tanda tangannya oleh
> server, dan server tetap menegakkan batas perangkat serta tanggal bayar.

## Batasan sebelum produksi

1. **Server belum dijalankan di produksi** — `Lisensi:UrlServer` di repo masih
   `localhost`; server lokal Development bisa dijalankan dengan mode memori
   (lihat bagian di atas), tetapi data hilang saat proses berhenti. Untuk
   produksi wajib Supabase + HTTPS. Panduan langkah demi langkah
   (Railway/Render/VPS) ada di [docs/deploy-server.md](deploy-server.md); ada
   juga `Dockerfile` di akar repo untuk build image.
2. **Kunci penandatangan wajib persistent.** Image container menyimpan kunci di
   `LISENSI_KEY_PATH`; tanpa volume permanen, kunci baru dibuat tiap container
   diganti dan semua token desa jadi tidak valid.
3. **Skema dibuat dari kode server** (`CREATE TABLE IF NOT EXISTS`), belum
   berversi. Beralih ke migrasi bila skema mulai berkembang.
4. **Rotasi kunci** belum ada alur operasional. Menghapus berkas kunci
   membuat semua token lama tidak valid — wajar saat kunci dicurigakan bocor.

Halaman web `/checkout` sudah ada dan hanya memakai endpoint publik
(`/paket`, `/buat-tagihan`), tetapi masih bergantung pada server yang berjalan
di HTTPS nyata.

## Rencana lanjutan

- Tahap gating berikutnya bila diminta: kuota surat (mis. 50 surat/bulan
  untuk masa tenggang) sebelum kunci penuh.
- Pusat administer sudah lengkap di halaman `/admin`: daftar desa, daftarkan
  desa, ubah (nama/kontak), hapus, kelola perangkat, ubah batas, riwayat
  pembayaran + ekspor CSV, status langganan & saringan bertanggal, serta
  rekonsiliasi — semuanya setara penuh lewat CLI (`Tools/Admin-Lisensi.ps1`).
  Yang belum: menghapus desa yang sudah punya tagihan/pembayaran (sengaja
  ditolak `409`; bila benar-benar perlu, membereskan jejak itu dulu lewat
  basis data).
- (Jauh) migrasi penuh data ke Supabase: tabel `tenant_id` + Row Level
  Security, backend tetap satu.
