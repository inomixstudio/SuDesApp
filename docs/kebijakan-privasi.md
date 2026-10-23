# Kebijakan Privasi (Privacy Policy) — SuDesApp

> Status dokumen: **berlaku sejak 19 Oktober 2026 · Versi 1.0**
>
> Ringkasnya: **data warga tetap tinggal di komputer desa.** Server lisensi
> hanya menerima data langganan (kode aktivasi, nama desa, tagihan, dan
> perangkat yang dipakai). Tidak ada NIK, Kartu Keluarga, isi surat, atau
> database desa yang dikirim ke kami.

Dokumen ini menjelaskan data pribadi apa yang diproses aplikasi **SuDesApp**,
di mana data itu disimpan, siapa yang dapat mengaksesnya, dan hak Anda sebagai
subjek data. Syarat penggunaan layanan ada di
[Syarat dan Ketentuan](syarat-ketentuan.md).

---

## 1. Pihak yang berperan

Aplikasi ini dipakai oleh pemerintah desa, tetapi datanya tidak dikelola oleh
satu pihak saja. Peran tiap pihak berbeda dan penting untuk dipahami:

| Pihak | Peran | Data yang dipegang |
|---|---|---|
| Pemerintah desa/kelurahan pengguna | **Pengendali Data Pribadi** | data warga, keluarga, surat, arsip, keuangan desa |
| PT Duta Publik melalui unit Sumberjaya Dev | **Prosesor** untuk data langganan; pengembang aplikasi | kode aktivasi, nama desa, tagihan, perangkat, bukti pembayaran |
| Warga/pemohon layanan desa | **Subjek Data Pribadi** | data yang diserahkan kepada desa |

Karena desa adalah pengendali, permintaan warga untuk melihat, memperbaiki,
atau menghapus data pribadinya ditujukan **kepada desa**, bukan kepada kami.
Kami membantu desa memenuhi permintaan itu sejauh data yang kami simpan
(bagian 4 dan 8).

**Kontak kami:** [sumberjayadev@gmail.com](mailto:sumberjayadev@gmail.com) ·
[github.com/inomixstudio/SuDesApp](https://github.com/inomixstudio/SuDesApp) ·
[dutapublik.com](https://dutapublik.com)

---

## 2. Prinsip yang kami pegang

1. **Lokal lebih dulu.** Data administrasi desa disimpan di komputer desa dan
   dapat dipakai tanpa internet. Server tidak pernah menjadi salinan database
   desa.
2. **Seminimal mungkin.** Server hanya menerima data yang benar-benar
   dibutuhkan untuk mengaktifkan lisensi, menagih, dan membatasi jumlah
   perangkat.
3. **Tidak dijual.** Kami tidak menjual, menyewakan, atau memakai data desa
   untuk iklan maupun pelatihan model.
4. **Tanpa telemetri.** Aplikasi tidak mengirim statistik pemakaian, isi
   layar, daftar fitur yang dibuka, atau laporan kerusakan secara otomatis.

---

## 3. Data yang tersimpan di komputer desa

Seluruh data pada bagian ini berada di **komputer/PC kantor desa**, di dalam
database `desa.db` yang dienkripsi dengan SQLCipher. Kuncinya 256-bit acak dan
disimpan untuk akun Windows yang sedang dipakai melalui DPAPI
(`%LOCALAPPDATA%\SuDesApp\DesaKunciDb.bin`), sehingga berkas database yang
disalin ke komputer atau akun Windows lain tidak dapat dibuka.

| Kategori | Contoh isi | Keterangan |
|---|---|---|
| Data warga & keluarga | NIK, nomor KK, nama, tempat/tanggal lahir, jenis kelamin, agama, pekerjaan, pendidikan, alamat, RT/RW/dusun, nomor HP, status perkawinan, hubungan keluarga, mutasi (baru/pindah/meninggal) | Termasuk data pribadi bersifat spesifik (agama) dan data warga di bawah umur |
| Surat & arsip | data pemohon pada tiap surat, nomor dan tanggal surat, status persetujuan, lampiran scan KTP/KK/surat tanda tangan | Banyak surat memuat NIK dan alamat; lampiran dapat memuat dokumen identitas |
| Perangkat desa & SK | nama, NIP/NIK, jabatan, masa jabatan, alamat, nomor HP/WhatsApp, riwayat SK | Dipakai untuk kop surat dan penerbitan SK |
| Keuangan desa | APBDes, realisasi, tanda bukti, laporan | Bukan data pribadi warga, tetapi dapat memuat nama penerima |
| Akun & keamanan | nama pengguna, peran, hash kata sandi (PBKDF2-SHA256, 120.000 putaran, salt 16 byte), status akun, riwayat aktivitas | Kata sandi asli tidak pernah disimpan |
| Preferensi & cache teknis | kode aktivasi, alamat server lisensi, token WhatsApp Cloud API, token OAuth Google, cache katalog harga, token lisensi bertanda tangan, **catatan persetujuan Kebijakan Privasi & Syarat Ketentuan (versi dokumen, tanggal berlaku, waktu persetujuan)** | Sebagian tersimpan di `%LOCALAPPDATA%\SuDesApp` (bagian 10); catatan persetujuan hanya disimpan di komputer ini |
| Log lokal | `error.log`, log aplikasi | Dapat memuat jalur berkas dan pesan galat; **tidak dikirim otomatis** |

Data pada tabel di atas **tidak** dikirim ke server lisensi kami. Yang keluar
hanya bila operator sendiri mengaktifkan integrasi opsional (bagian 6).

### 3.1 Data warga adalah tanggung jawab desa

Desa menentukan dasar pemrosesan, memberi tahu warga, membatasi siapa yang
boleh membuka data, dan menetapkan masa simpannya. Aplikasi menyediakan
alatnya: peran dan izin, layar kunci otomatis setelah diam (bawaan 15 menit),
riwayat aktivitas, serta tutup buku tahunan. Rinciannya ada di
[Kebijakan Pengguna dan Peran](policy-peran-pengguna.md) dan
[Kebijakan Tutup Buku & Retensi](policy-tutup-buku-dan-retensi.md).

### 3.2 Ekspor, cetak, dan pencadangan

Ekspor Excel/CSV/PDF, surat yang dicetak, dan cadangan database adalah
**salinan di luar kendali aplikasi**. Setelah diekspor atau dicetak, berkas itu
mengikuti aturan penyimpanan desa. Cadangan Google Drive (bila diaktifkan)
berada di akun Google desa sendiri (bagian 6.1).

---

## 4. Data yang dikirim ke server lisensi

Server lisensi hanya melayani aktivasi, penagihan, pembatasan perangkat, dan
pengumuman. Data yang diproses:

| Data | Kapan dikirim | Tujuan |
|---|---|---|
| **Kode aktivasi** desa | saat aktivasi/cek lisensi dan saat membuat tagihan | mengenali langganan; kode ini berfungsi sebagai kredensial |
| **ID perangkat** | saat aktivasi/cek lisensi | membatasi jumlah PC per langganan. Berupa SHA-256 dari nama komputer + nama pengguna Windows, dipotong ±128 bit — bukan nomor seri perangkat keras, bukan nama komputer mentah |
| **Nama desa, paket, masa aktif** | dikembalikan server di dalam token lisensi bertanda tangan | menampilkan status langganan dan mengizinkan pemakaian offline |
| **Tagihan** | saat tagihan dibuat | order id, paket, nominal, dan jumlah hari — dipakai mencocokkan pembayaran |
| **Pembayaran** | saat pembayaran dikonfirmasi gateway atau dicatat operator | nominal, status, waktu, referensi; pada pembayaran manual dapat ditambah catatan dan **gambar bukti transfer** (JPEG/PNG/WEBP maks. 2 MB) |
| **Kontak desa** | bila dicatat operator saat pendaftaran | keperluan menagih dan menghubungi desa |
| **Daftar perangkat** | otomatis saat aktivasi | waktu pertama dan terakhir terlihat tiap perangkat |
| **Log server** | otomatis | alamat IP, waktu, jalur permintaan, kode jawaban — untuk keamanan dan pembatas laju |

**Yang tidak pernah dikirim ke server lisensi:** NIK, nomor KK, nama warga,
alamat warga, nomor HP warga, isi surat, lampiran surat, data perangkat desa,
APBDes, maupun berkas database desa.

Token lisensi yang disimpan di komputer desa memuat nama desa, paket, kode
aktivasi, dan tanggal berlaku — ditandatangani secara kriptografis (ES256) agar
tidak dapat dipalsukan. Token ini juga disimpan di tabel `LisensiAktivasi`
dalam database desa supaya ikut tercadang bersama data desa.

---

## 5. Pembayaran

Pembayaran berbayar diproses oleh **Midtrans** sebagai penyedia gateway.
Halaman pembayaran Midtrans dapat meminta nama, surel, nomor telepon, dan
instrumen pembayaran sesuai kebijakan Midtrans. **Kami tidak menerima dan tidak
menyimpan nomor kartu, kode CVV, atau PIN.** Yang kami terima hanyalah status
transaksi (mis. lunas/kedaluwarsa), order id, nominal, dan waktu pembayaran.

Untuk pembayaran manual (QRIS statis atau transfer bank), operator kami
mencatatnya ke sistem pembukuan. Catatan dan gambar bukti transfer dapat
memuat nama pengirim, nomor rekening, dan nominal — disimpan hanya untuk
verifikasi dan audit pembayaran, serta hanya dapat dibuka operator yang
berwenang.

Halaman `checkout` hanya meminta **kode aktivasi** dan pilihan paket. Halaman
itu tidak memasang cookie analitik maupun pelacak iklan.

---

## 6. Integrasi opsional dan pihak ketiga

Semua integrasi di bagian ini **mati secara bawaan** dan hanya berjalan bila
operator menyalakannya. Bila tidak dipakai, tidak ada data yang keluar.

### 6.1 Google Drive, Sheets, dan Forms (opsional)

Setelah pengguna masuk ke aplikasi, operator dapat menyambungkan akun Google
desa. Cakupan (scope) yang diminta adalah **Drive penuh**
(`.../auth/drive`), **Forms body**, dan **Forms responses readonly** — Drive
penuh dibutuhkan karena aplikasi membuat folder, mengunggah, mengunduh, dan
memangkas cadangan di Drive akun tersebut.

- **Cadangan**: salinan database terenkripsi (`VACUUM INTO`), zip lampiran
  arsip, dan zip template diunggah ke folder `SuDesApp-Backup` di akun Google
  desa. Maksimum 30 berkas per kelompok; berkas lama dipangkas otomatis.
  Data ini berada di akun Google desa, bukan di server kami.
- **Formulir/Sheet**: jawaban warga pada Google Form dan baris Google Sheet
  dibaca untuk membuat permintaan surat; PDF surat dan tautan unduh dapat
  diunggah ke Drive desa.
- **Token OAuth** disimpan terenkripsi DPAPI (`CurrentUser`) di profil Windows
  pengguna (`%LOCALAPPDATA%\SuDesApp\GoogleDriveToken\<pengguna Windows>`).
  Hanya akun Windows yang sama yang dapat membukanya. Token lama yang tersimpan
  sebagai JSON oleh versi sebelumnya dipindahkan ke format terenkripsi saat
  berikutnya dipakai.
- **Pencabutan**: putuskan akun dari aplikasi (token dihapus) dan cabut akses
  aplikasi di [myaccount.google.com/permissions](https://myaccount.google.com/permissions).

Pemrosesan oleh Google mengikuti kebijakan privasi Google. Data dapat
diproses di luar Indonesia.

### 6.2 WhatsApp Cloud API — Meta (opsional)

Bila layanan permintaan surat WhatsApp diaktifkan, pesan warga dikirim melalui
**WhatsApp Cloud API milik Meta** dan diterima aplikasi lewat webhook HTTPS.
Isi pesan dapat memuat nama, NIK, nomor HP, alamat, dan keperluan surat.
Data tersebut:

- diproses Meta sebagai penyedia platform sesuai kebijakan Meta;
- disimpan oleh aplikasi di database desa sebagai permintaan surat;
- dibalas berupa teks/tautan — aplikasi tidak mengirim berkas melalui
  WhatsApp, hanya tautan unduh.

Token akses WhatsApp dan app secret disimpan sebagai preferensi lokal di
komputer desa. Bila integrasi dimatikan, webhook berhenti menerima pesan.

### 6.3 API Desa (opsional)

API Desa adalah server HTTP kecil **di dalam komputer desa**. Bawaannya hanya
mendengarkan `localhost` dan wajib memakai kunci API. Bila operator membuka
akses jaringan, API dapat dipanggil komputer lain di jaringan desa.

- Endpoint statistik, perangkat desa, dan rekap hanya mengeluarkan **angka
  agregat** atau data pejabat (tanpa NIP/NIK/alamat/nomor HP).
- Endpoint verifikasi surat dapat dipanggil tanpa kunci karena dipakai bank/
  instansi pemegang surat; jawabannya dibatasi pada data yang sudah tercetak
  di surat, dengan **nama pemohon disamarkan** (mis. "BUDI S.").
- Permintaan surat yang masuk lewat API **dapat memuat NIK**; isi badan
  permintaan **tidak dicatat** ke log. Log hanya mencatat endpoint, alamat
  pengirim, dan kode jawaban.

### 6.4 Data IDM Kemendesa

Halaman IDM mengambil data resmi dari `idm.kemendesa.go.id` untuk kode desa
yang dipilih. Permintaan hanya memuat **kode desa dan tahun** — tidak ada data
warga. Bila sertifikat HTTPS situs pusat bermasalah, aplikasi dapat mengulang
permintaan lewat HTTP tanpa enkripsi dan menandainya di layar.

### 6.5 Pemeriksaan pembaruan

Aplikasi dapat memeriksa rilis terbaru melalui **GitHub Releases** atau berkas
`version.json` HTTPS. Permintaan hanya memuat versi aplikasi dan alamat IP
pengunduh (sebagaimana setiap koneksi internet). Berkas pembaruan diverifikasi
SHA-256 sebelum dijalankan.

### 6.6 Pengumuman aplikasi

Aplikasi menanyakan `GET /pengumuman` secara berkala (±60 detik) untuk
menampilkan pemberitahuan. Permintaan tidak memuat kode desa maupun data
warga; server hanya melihat alamat IP seperti permintaan HTTP biasa.

---

## 7. Pihak ketiga dan transfer internasional

| Pihak ketiga | Keperluan | Data | Lokasi pemrosesan |
|---|---|---|---|
| Midtrans | gateway pembayaran | data transaksi dan instrumen pembayaran sesuai kebijakan Midtrans | Indonesia |
| Penyedia basis data & hosting server lisensi (mis. Supabase/PostgreSQL) | menyimpan data langganan | tabel pelanggan, langganan, tagihan, pembayaran, perangkat | sesuai wilayah yang dipilih operator; dapat di luar Indonesia |
| Google | Drive/Sheets/Forms opsional | berkas cadangan, formulir, token OAuth | global |
| Meta | WhatsApp Cloud API opsional | nomor dan isi pesan warga | global |
| Kemendesa | data IDM | kode desa dan tahun | Indonesia |
| GitHub | rilis dan pembaruan | versi aplikasi, alamat IP | global |

Bila data dialihkan ke luar Indonesia, kami mengupayakan dasar pengalihan yang
sah sesuai UU No. 27 Tahun 2022 tentang Pelindungan Data Pribadi dan ketentuan
penyedia masing-masing. Kami tidak menjual data kepada pihak mana pun.

---

## 8. Masa simpan dan penghapusan

- **Di komputer desa:** data disimpan sampai desa menghapusnya melalui
  aplikasi, menghapus berkas ekspor, atau menghapus instalasi. Penghapusan
  warga/surat di aplikasi tidak otomatis menghapus salinan di berkas ekspor,
  cetakan, atau cadangan yang sudah dibuat.
- **Di server lisensi:** data langganan, tagihan, dan pembayaran disimpan
  selama langganan berjalan dan setelahnya sepanjang diperlukan untuk
  pembukuan, pajak, dan penyelesaian sengketa. **Belum ada penghapusan
  otomatis**; ini disengaja agar riwayat pembayaran tidak hilang tanpa
  persetujuan.
- **Permintaan penghapusan** data langganan/perangkat dapat dikirim ke
  [sumberjayadev@gmail.com](mailto:sumberjayadev@gmail.com) dan akan
  ditindaklanjuti paling lambat **30 hari kerja**, kecuali data yang wajib
  dipertahankan oleh hukum (mis. bukti pembayaran).
- **Cadangan Google Drive:** maksimum 30 berkas per kelompok, dipangkas
  otomatis oleh aplikasi; penghapusan penuh dilakukan dari akun Google desa.
- **Log:** log lokal berada di folder aplikasi dan dapat dihapus operator.
  Log server mengikuti masa retensi penyedia hosting.

---

## 9. Hak subjek data

Menurut UU No. 27 Tahun 2022, subjek data berhak:

1. mendapatkan informasi tentang pemrosesan dan identitas pengendali;
2. mengakses dan memperoleh salinan data pribadinya;
3. meminta pembetulan data yang tidak akurat;
4. meminta penghapusan atau pemusnahan data;
5. menarik kembali persetujuan;
6. mengajukan keberatan atas pemrosesan yang berdampak hukum;
7. menunda atau membatasi pemrosesan;
8. memperoleh dan memindahkan datanya dalam format yang lazim;
9. menggugat dan menerima ganti rugi atas pelanggaran.

Karena desa adalah pengendali, permintaan tersebut diajukan **kepada desa**.
Kami membantu desa dengan menyediakan fitur ekspor/impor, penghapusan data,
dan riwayat aktivitas. Untuk data yang kami simpan sendiri (bagian 4), ajukan
langsung ke kontak kami.

Bila terjadi pelanggaran pelindungan data pribadi yang berdampak pada subjek
data, kami memberi tahu desa dan pihak terdampak paling lambat **3 × 24 jam**
sesuai ketentuan UU PDP, disertai keterangan data yang terpengaruh dan langkah
penanganannya.

---

## 10. Keamanan

| Lapis | Pengamanan |
|---|---|
| Database desa | SQLCipher (AES-256); kunci disimpan DPAPI per akun Windows |
| Kata sandi akun | PBKDF2-HMAC-SHA256, 120.000 iterasi, salt acak 16 byte; kunci 3× gagal selama 5 menit |
| Sesi | layar kunci otomatis setelah diam (bawaan 15 menit, dapat diatur) |
| Berkas akun lama | `Login.dat` dienkripsi DPAPI |
| Lisensi | token ES256 diverifikasi dengan kunci publik yang ditanam; cache dimodifikasi = dianggap tidak aktif |
| Server lisensi | isolasi per-desa (Row Level Security), role terbatas, identitas operator dengan izin terbatas, HTTPS, pembatas laju |
| API Desa | bawaannya hanya localhost, kunci API ber-cakupan, batas 64 KB dan pembatas laju |
| Pembaruan | unduhan diverifikasi SHA-256 sebelum dijalankan |

Yang perlu dijaga desa: akun Windows, kata sandi aplikasi, perangkat yang
dipakai, akun Google yang disambungkan, dan token WhatsApp. Beberapa token
(mis. OAuth Google dan WhatsApp) tersimpan sebagai berkas di profil pengguna
tanpa enkripsi tambahan; membatasi akses fisik dan akun Windows adalah
lapisan pertahanannya.

Kerentanan keamanan dilaporkan melalui **GitHub Security Advisories**, bukan
issue publik — lihat [SECURITY.md](../SECURITY.md).

---

## 11. Anak dan data pribadi spesifik

Data warga dapat memuat data anak (mis. dalam Kartu Keluarga, akta, atau surat
keterangan) dan data pribadi spesifik seperti agama. Data itu diproses oleh
desa untuk tugas pemerintahan dan pelayanan publik, bukan oleh kami. Aplikasi
tidak mengirimkannya ke server lisensi atau pihak ketiga, kecuali operator
sendiri mengaktifkan integrasi opsional dan memasukkan data tersebut.

---

## 12. Cookie dan analitik

Aplikasi desktop tidak memakai cookie. Halaman `checkout` dan `admin` di
server lisensi adalah halaman statis tanpa cookie analitik, tanpa piksel
pelacak, dan tanpa layanan analitik pihak ketiga. Halaman pembayaran Midtrans
dapat memakai cookie sesuai kebijakan Midtrans.

---

## 13. Perubahan kebijakan

Kebijakan ini dapat diperbarui bila fitur atau alur data berubah. Versi dan
tanggal berlaku selalu tercantum di awal dokumen; perubahan material
diberitahukan melalui pengumuman di aplikasi dan/atau surel terdaftar paling
lambat **30 hari** sebelum berlaku. Versi lama dapat diminta melalui kontak
kami.

---

## 14. Kontak dan pengaduan

- Surel: [sumberjayadev@gmail.com](mailto:sumberjayadev@gmail.com)
- Repositori: [github.com/inomixstudio/SuDesApp](https://github.com/inomixstudio/SuDesApp)
- Perusahaan: PT Duta Publik — unit Sumberjaya Dev, [dutapublik.com](https://dutapublik.com)

Kami berupaya membalas dalam **7 hari kerja**. Bila jawaban kami belum
memuaskan, Anda dapat menyampaikan pengaduan kepada otoritas pengawasan
pelindungan data pribadi yang berwenang di Indonesia.

---

## 15. Catatan untuk desa sebagai pengendali

1. Beri tahu warga bahwa data mereka dicatat untuk pelayanan administrasi dan
   jelaskan masa simpannya.
2. Batasi akun aplikasi sesuai peran; jangan memakai satu akun bersama.
3. Aktifkan layar kunci dan jaga akun Windows komputer kantor.
4. Tentukan siapa yang boleh mengekspor, mencetak, dan mencadangkan data.
5. Hapus data yang tidak lagi diperlukan dan pangkas berkas ekspor lama.
6. Bila membutuhkan perjanjian pemrosesan data (DPA) tertulis, ajukan melalui
   kontak di bagian 14.

Dokumen terkait: [Syarat dan Ketentuan](syarat-ketentuan.md) ·
[Kebijakan Pengguna dan Peran](policy-peran-pengguna.md) ·
[Kebijakan Tutup Buku & Retensi](policy-tutup-buku-dan-retensi.md) ·
[Langganan dan Lisensi](langganan-dan-lisensi.md)
