# Data Perangkat Desa: Tabel Struktur, SK, Ekspor & Cetak

Referensi fitur halaman **Data Perangkat Desa**. Halaman ini memuat struktur
pemerintahan desa beserta lembaga desa lain: daftar jabatan tiap bagian tetap
dan tiap jabatan selalu punya baris — termasuk yang belum ada orangnya —
sehingga seluruh struktur terlihat utuh dari yang terisi sampai yang masih
kosong.

Halaman punya **12 bagian yang ditampilkan bergantian**, dipilih lewat tombol di
bilah pemilih bagian yang tersusun dalam **dua baris berlabel**:

- **Pemerintahan Desa** — **Perangkat Desa** (bawaan saat halaman dibuka),
  **RT/RW**, **BPD**, **Staf Desa**, **Operator Desa**;
- **Lembaga & Kader** — **LINMAS**, **Posyandu**, **Kader KPM**, **Kader
  Stunting**, **PKK**, **LPM**, dan **Karang Taruna**.

Kedua belas tabel tidak diperlihatkan sekaligus. Tiap tombol memakai ikon
bagiannya dan punya tooltip keterangan; tombol bagian yang sedang tampil
ditandai warna utama, tombol satunya tetap gaya garis. Tombolnya dibangkitkan
dari daftar bagian di view model (`BarisBagianPemerintahan` /
`BarisBagianLembaga` — nama + ikon + perintah + tooltip dalam satu tempat),
sehingga menambah bagian baru cukup menambah satu entri data. Ikon chip judul
halaman juga ikut berganti mengikuti bagian yang sedang tampil.

Tabelnya **tampilan baca-saja**; pengisian data diri lewat tombol **Tambah
Orang** (form). Di bawah pemilih bagian ada **bilah aksi** dengan **Tambah
Orang** di sisi kiri — berlaku untuk semua bagian, form otomatis memakai jabatan
bagian yang sedang tampil — dan **Formulir Pendataan**, **Ekspor Excel**, serta
**Cetak PDF** di sisi kanan. Tidak ada kotak pencarian, filter, ataupun tombol
**Muat Ulang** — data dibaca sekali saat halaman dibuka serta disegarkan
setiap kali data disimpan atau dihapus.

**Seluruh isi halaman mengikuti bagian yang sedang tampil** — pilihan jabatan
form, ekspor, cetak, dan pilihan jabatan panel SK —
sebab tiap kelompok punya cara penerbitan SK yang berbeda, jadi isi satu bagian
tidak dicampur dengan bagian lain.

## Ruang lingkup: seluruh bagian halaman

Bagian **PERANGKAT DESA** menampilkan, dari atas ke bawah, jabatan resmi
desa:

| # | Jabatan |
|---|---------|
| 1 | Kepala Desa |
| 2 | Sekretaris Desa |
| 3 | Kaur Keuangan |
| 4 | Kaur Perencanaan |
| 5 | Kaur Umum dan Tata Usaha |
| 6 | Kasi Pemerintahan |
| 7 | Kasi Pelayanan |
| 8 | Kasi Kesejahteraan |
| 9 | Kepala Dusun (KADUS) |

Daftar ini ada di `JabatanPerangkat.Resmi` (`SuDesApp.Core/Data/Models/PerangkatDesaData.cs`).

Bagian lain memakai daftar jabatannya sendiri:

| Bagian | Jabatan |
|--------|---------|
| RT/RW | Ketua RT, Ketua RW |
| BPD | Ketua, Wakil Ketua, Sekretaris, Anggota BPD |
| LINMAS | LINMAS |
| Posyandu | Kader Posyandu |
| Kader KPM | Kader KPM |
| Kader Stunting | Kader Stunting |
| Staf Desa | Staf |
| Operator Desa | Operator Desa |
| PKK | Ketua PKK, Pengurus PKK |
| LPM | Ketua LPM, Pengurus LPM |
| Karang Taruna | Ketua, Wakil Ketua, Sekretaris, Bendahara, Pengurus Karang Taruna |

- **BPD tidak ada bendaharanya**: susunan BPD hanya Ketua, Wakil Ketua,
  Sekretaris, dan Anggota — urusan keuangan ditangani sekretaris. Jabatan
  `BENDAHARA BPD` dibuang dari daftar dan pilihan form; baris lama berjabatan
  itu dipetakan ke `ANGGOTA BPD` lewat `JabatanPerangkat.NamaLama` supaya tetap
  tampil di bagian BPD.
- **Wakil Kepala Desa tidak ada**: Kepala Desa tidak mempunyai wakil, jadi
  jabatan itu dibuang dari daftar resmi maupun pilihan form. Data lamanya tetap
  sah dan tersimpan di database (masih dikenali `JabatanPerangkat.Semua` /
  `Kelompok`), hanya tidak ditampilkan di halaman ini.
- Konstanta jabatan kelima bernama **`KAUR UMUM DAN TATA USAHA`**. Nama lama
  `KAUR UMUM` dan `KAUR TATA USAHA DAN UMUM` dipetakan ke nama baru lewat
  `JabatanPerangkat.NamaLama` (`Normalisasi`) — termasuk migrasi data lama yang
  dijalankan repository, sehingga baris lama tampil pada jabatan yang benar.
- **Jabatan lama di luar daftar bagian** (`KEPALA SEKRETARIAT`, `KAUR
  PEMASARAN`, `KEPALA SEKSI`, `LAINNYA`) tetap sah untuk data lama dan tetap
  tersimpan di tabel `PerangkatDesa`, tetapi tidak ditampilkan pada bagian
  mana pun — karena itu tidak ikut ringkasan, ekspor, maupun cetak.
- **LPM dan LPMD satu lembaga**, jadi tidak ada bagian LPMD terpisah. Nama
  jabatan lama `KETUA LPMD` dan `PENGURUS LPMD` dipetakan ke `KETUA LPM`
  dan `PENGURUS LPM` lewat `JabatanPerangkat.NamaLama`; migrasinya dijalankan
  repository setiap aplikasi dibuka, sehingga data LPMD lama otomatis tampil
  pada bagian LPM tanpa perlu diedit ulang.
- Nama jabatan lama tetap diizinkan untuk data lama, hanya tidak muncul di
  tabel maupun pilihan form.

## Tabel: satu baris per orang, baca-saja

Kolom tabel di layar: **Jabatan, Nama / NIK, Wilayah, Masa Jabatan, Status,
Aksi**. Kedelapan belas kolom berkas "DATA PERANGKAT" tetap lengkap, tetapi
dibaca dan dicetak lewat **form**, **Ekspor Excel**, dan **Cetak PDF** — bukan
dipadatkan ke dalam tabel yang harus digeser ke kiri-kanan. Tabel tetap dapat
digeser mendatar pada jendela sempit, dan kepala kolom ikut bergeser bersama
isinya.

- Tiap jabatan selalu mendapat baris. Jabatan yang belum punya orang tampil
  sebagai **baris kosong penanda** (petunjuk *belum terisi*).
- **Jabatan yang boleh dipegang lebih dari satu orang tampil sebagai satu
  baris per orang** — misalnya KADUS: *Kadus Kalenjaya*, *Kadus Belendung 01*,
  *Kadus Belendung 02* masing-masing satu baris, dan kolom **Wilayah**
  menunjukkan dusunnya. Urutan dalam satu jabatan: yang masih memegang
  jabatan didahulukan, lalu menurut nama.
- **Tgl. Pengangkatan** memakai kolom `TanggalSK` (format `dd-MM-yyyy`);
  kolom **No. SK** memakai `NomorSK`; sel kosong dicetak `-`.
- Tabel tidak punya sel isian. **Isian data diri dilakukan lewat form**: tombol
  pada baris (atau klik ganda pada baris) membuka form lengkap — baris kosong
  membuka mode tambah dengan jabatannya sudah terpilih — dan tombol **Tambah
  Orang** di bilah aksi membuka form dengan jabatan awal bagian itu. Tombol
  **Hapus** hanya ada pada baris yang sudah berisi orang.
- Pilihan jabatan pada form **dibatasi jabatan bagian tempat form dibuka**
  (`DaftarJabatanForm` ← `JabatanBagian`; tanpa ketik bebas), sehingga tambah
  orang tidak pernah menyatu dengan jabatan bagian lain dan orang yang disimpan
  selalu tampil di tabel bagian itu. Form memuat seluruh kolom berkas
  "DATA PERANGKAT" dengan **urutan kelompok blanko pendataan**: A Identitas
  (nama, jabatan, NIK, NIP, jenis kelamin, tempat & tanggal lahir, agama,
  no. telp./HP, WhatsApp), B Pendidikan (pendidikan, tahun lulus, no. ijasah),
  C Pengangkatan & Rekomendasi (nomor & tanggal rekomendasi, nomor & tanggal
  SK, masa jabatan mulai/selesai, status, catatan), dan D Wilayah & Keuangan
  (dusun/RT/RW, unit, **nomor rekening BJB**, **BPJS Kesehatan**) — **isian
  Alamat sudah dihapus** karena keterangan wilayah sudah diwakili Dusun dan
  RT/RW; alamat data lama tetap tersimpan di database dan dibawa apa adanya
  saat data diubah.
  **Agama dipilih dari daftar** (sama dengan form Data Warga) dan **seluruh
  isian tanggal berupa TextBox `dd-mm-yyyy`** — bukan pemilih tanggal; teks
  tanggal yang tidak dikenali ditolak saat Simpan dengan pesan berisi label
  isiannya. Untuk mengumpulkan isian itu di lapangan, tombol **Formulir
  Pendataan** mencetak blanko 18 kolom yang sama (lihat bagian Ekspor Excel
  dan Cetak PDF).

Aturan lama tetap berlaku dan dijaga `PerangkatDesaService`: **satu jabatan
pada satu wilayah hanya boleh satu orang** (dihitung dari kombinasi jabatan +
Dusun/RT/RW pada status AKTIF/MENUNGGU SK). Praktiknya: KADUS kedua untuk
satu desa diperbolehkan asal **Dusun-nya diisi berbeda** (mis. `Belendung 01`
dan `Belendung 02`) — kalau Dusun/keduanya kosong atau kembar, simpan ditolak
dengan pesan penjelas bahwa jabatan di wilayah itu sudah ada orang lain.

## Bagian RT/RW dan bagian lembaga lain

Bagian **RT/RW** dan **10 bagian lembaga** (BPD, LINMAS, Posyandu, Kader KPM,
Kader Stunting, Staf Desa, Operator Desa, PKK, LPM, Karang Taruna)
memakai tampilan dan isian **sama persis** dengan bagian perangkat desa —
bahkan berbagi kepala tabel dan template baris yang sama:

- **Satu tabel, satu kepala bagian, dan satu templat baris** dipakai seluruh
  dua belas bagian; memilih bagian hanya mengganti isi `ItemsAktif` dan angka
  ringkasannya, tanpa membaca ulang database.
- Kepala bagian menampilkan judul kelompok, petunjuk singkat, dan tiga angka:
  **ORANG**, **JABATAN**, dan **BELUM TERISI**. Penambahan data memakai tombol
  **Tambah Orang** pada bilah aksi.
- Tabel memakai kolom yang sama untuk semua bagian: tombol **SK / Ubah /
  Hapus**, satu orang satu baris, dan jabatan yang belum ada orangnya tetap
  punya baris penanda *belum terisi*.
- **Form isian, validasi, dan tombol SK berlaku untuk semua
  bagian**, tetapi **ekspor dan cetak hanya memuat bagian yang sedang tampil**
  — tiap kelompok punya cara penerbitan SK berbeda, jadi daftarnya tidak
  dicampur. Kolom **Jabatan** tetap membedakan kelompoknya.
- **Tata letak tahan jendela sempit**: judul &amp; subjudul halaman membungkus
  di kolom bintang, label pemilih bagian duduk di atas baris tombol (bukan di
  kolom kiri yang memakan lebar), tombol aksi kanan berada di `WrapPanel`
  sehingga turun ke baris kedua alih-alih terpotong, dan tabel memiliki lebar
  minimum 880 px yang digeser mendatar bersama kepalanya.
- **Nama panjang dipotong elipsis dengan tooltip** pada kolom Nama supaya
  baris tidak melebar; kolom Jabatan dan Wilayah membungkus teks.

## Pesan keadaan

- Halaman ini **tanpa saringan**: seluruh baris bagian yang sedang tampil
  selalu tampil apa adanya.
- **Kerangka jabatan tidak pernah disembunyikan.** Bagian yang belum berisi
  orang tetap menampilkan baris penanda *belum terisi* beserta angka 0, supaya
  operator melihat jabatan apa saja yang harus diisi. Dulu panel keadaan kosong
  menutupi tabel ini dan judulnya selalu "perangkat desa", bahkan ketika
  bagian aktifnya BPD atau Posyandu.
- Petunjuk pada kepala bagian mengikuti keadaan: bagian kosong menawarkan
  pilih baris jabatan atau **Tambah Orang**; bagian berisi mengingatkan klik
  ganda untuk mengubah data dan tombol SK untuk surat keputusan.
- Baris pesan di bawah bilah aksi kini **hanya untuk hasil aksi** (simpan,
  ekspor, cetak, hapus) dan menghilang saat tidak ada pesan; ringkasan
  keterisian pindah ke kepala bagian supaya tidak ada dua ringkasan berbeda.

## Tombol dan panel SK per jabatan

Setiap baris — **termasuk baris yang belum ada orangnya** — punya tombol **SK**
di kolom AKSI (ikon pratinjau). Tombol itu membuka **panel Surat Keputusan**
dengan jabatan baris tersebut sudah terpilih:

- **Jabatan & jenis SK** — jabatan bisa diganti langsung dari panel, tetapi
  pilihannya **dibatasi jabatan bagian tempat panel dibuka**
  (`PilihanJabatanSk` ← `JabatanBagian`); jenis SK: Pengangkatan,
  Pemberhentian, atau Penetapan (untuk kelompok yang punya judul penetapan,
  mis. Kader KPM).
- **Isian dokumen** — nomor SK, tanggal SK, nama, NIK, wilayah, masa jabatan
  mulai/selesai, serta alasan (untuk SK pemberhentian). Isian diambil dari data
  orang pada jabatan itu, jadi tidak perlu diketik ulang. **Semua isian tanggal
  (form maupun panel SK) memakai TextBox biasa berformat `dd-mm-yyyy`, bukan
  pemilih tanggal**; teks yang tidak dikenali ditolak dengan pesan jelas
  sebelum dokumen dibuat. Aturan bacaan tanggalnya dipusatkan di
  `SuDesApp.Wpf/Mvvm/TanggalIsian.cs` (pola `dd-mm-yyyy`, titik, garis miring,
  dan `yyyy-MM-dd`, dengan pesan galat seragam) dan dipakai juga oleh **Isi
  Template Surat**, **Register Surat & Register NTCR** (tanggal filter diperiksa
  saat kotaknya ditinggalkan), serta **Permohonan Rekening Koran**.
- **Mode contoh** — baris yang belum ada orangnya membuka panel dengan isian
  kosong; SK-nya dicetak sebagai contoh (nama dan tanggal digariskan
  titik-titik) supaya bentuk dokumen bisa dilihat lebih dulu.
- **Pratinjau SK** — menyusun PDF lewat `SkPerangkatGenerator` lalu membukanya
  di pratinjau SudesPdf; dari sana operator bisa mencetak atau menyimpan.
- **SK bersama** — centang *SK bersama* untuk membuat satu SK bagi semua orang
  pada jabatan itu dengan lampiran daftar nama (lihat bagian di bawah).
- **Kelompok tanpa template** — bila kelompok terpilih belum punya template
  bawaan, panel tidak menampilkan daftar dasar hukum (tidak ada template yang
  dirujuk) melainkan kotak peringatan berisi `SkCatatanTemplate` yang
  menjelaskan keterbatasan itu; saat ini seluruh bagian halaman sudah punya
  template, jadi kotak itu hanya cadangan.

Pengecualian **Kepala Desa dan BPD**: SK keduanya diterbitkan **Bupati**
(Kepala Desa lewat SKD, anggota BPD lewat penetapan Bupati), sehingga aplikasi
tidak menerbitkan dokumennya — tombol **Pratinjau SK** disembunyikan dan
panelnya hanya menampilkan bagian pengarsipan berkas. Pemeriksaannya ada di
`SkJabatanAksiKatalog.SumberJabatan` (jabatan sendiri ATAU kelompok
`Sumber = Bupati`). Karena itu, menambah orang sebagai **Kepala Desa atau
anggota BPD wajib melampirkan berkas PDF SK Bupati saat menyimpan** (lihat
bagian “SK saat menyimpan data” di bawah).

**Arsip berkas SK.** Setiap panel punya bagian "Berkas SK":

- **Arsipkan SK Bupati** hanya tampil untuk jabatan yang SK-nya diterbitkan
  Bupati (Kepala Desa/BPD): dialog pilih berkas PDF, salinan disimpan ke
  folder arsip `%LOCALAPPDATA%\SuDesApp\BerkasSkPerangkat` (lihat
  `SuDesApp.Core/Utilities/BerkasSkPerangkat.cs`), lalu namanya disimpan di
  kolom `BerkasSK` milik data orang itu. Berkas wajib PDF. Tombol unggah ini
  disembunyikan untuk jabatan lain sebab SK mereka dibuat aplikasi otomatis
  saat menyimpan data — operator tidak lagi diminta melampirkan berkas PDF.
- **Buka Berkas** menampilkan PDF terarsip (SK Bupati unggahan maupun SK jadi
  hasil aplikasi) pada pratinjau yang sama. Bila berkasnya sudah tidak ada,
  panel memberi pesan untuk melampirkan ulang.
- Menghapus data perangkat ikut menghapus berkas SK terarsipnya.

### SK saat menyimpan data (form data diri)

Cara menerbitkan SK mengikuti siapa yang menerbitkan SK jabatan tersebut
(`SkJabatanAksiKatalog.SumberJabatan`), berlaku pada tombol **Simpan** form
tambah/ubah:

- **Jabatan dengan SK Bupati (Kepala Desa & anggota BPD) wajib melampirkan
  berkas PDF SK saat menyimpan.** Form menampilkan bagian **Berkas PDF SK
  Bupati \*** berisi nama berkas terpilih beserta tombol **Lampirkan SK**
  (dialog pilih PDF) dan **Buka Berkas**; tanpa lampiran, simpan ditolak dengan
  pesan penjelas. Pada mode tambah, berkas disalin ke folder arsip tepat setelah
  baris tersimpan (baris baru belum punya ID saat berkas dipilih) lalu namanya
  ditulis ke kolom `BerkasSK`. Berkas lama yang masih ada tidak perlu dipilih
  ulang saat data diedit.
- **Jabatan lain (SK diterbitkan Kepala Desa) tidak menampilkan pilihan berkas
  lampiran sama sekali** — form hanya menampilkan keterangan bahwa SK dibuat
  otomatis. Saat disimpan, aplikasi **langsung membuat SK jadi** (bukan mode
  contoh: `Contoh = false`, tanda tangan memakai nama Kepala Desa dari data
  desa) memakai data form: nomor & tanggal SK (tanggal kosong → hari ini),
  nama, NIK, wilayah, masa jabatan, jenis SK mengikuti status (BERHENTI →
  pemberhentian), template per jabatan, penanda diktum, dan butir Memperhatikan
  kelompok. PDF-nya ditulis ke folder arsip dengan nama tetap per baris
  (`SK_<id>_SK-Jadi.pdf`, ditimpa tiap penyimpanan) dan namanya disimpan di
  kolom `BerkasSK`; berkas unggahan operator tidak pernah ikut terhapus
  (`BerkasSkPerangkat.SimpanHasilOtomatis` / `AdalahHasilOtomatis`). Kelompok
  tanpa template (LAINNYA) dilewati tanpa galat.
- **Linmas, Posyandu & Kader KPM: satu SK bersama untuk semua anggota.**
  Ketiga kelompok (`Bersama = true` pada `SkPerangkatKatalog` — LINMAS,
  POSYANDU, KADER KPM) tidak membuat SK per orang: saat salah satu anggota
  disimpan, aplikasi membuat **satu SK dengan lampiran daftar
  seluruh anggota jabatan itu** (yang masih menjabat didahulukan; kolom
  pribadi diambil dari Data Warga lewat NIK oleh
  `SkPerangkatLampiranService.SusunAsync`). Satu berkas PDF ditautkan ke
  kolom `BerkasSK` **setiap anggota** — baris anggota lain ikut diperbarui,
  dan berkas otomatis lama baru dibuang setelah seluruh baris berpindah.
  Pengelompokan lampiran: **LINMAS satu daftar tanpa judul blok** (kecuali
  operator mengisi Unit); **POSYANDU dikelompokkan per posyandu mengikuti
  dusun** — isian **Unit** operator didahulukan (mis. `POSYANDU SAKURA I`),
  dan bila kosong nama bloknya diturunkan dari dusun (`POSYANDU <Dusun>`),
  sehingga jumlah posyandu = jumlah Kepala Dusun; **KPM satu daftar** tanpa
  unit dengan **jenis SK otomatis Penetapan** (sesuai contoh SK KPM desa) dan
  tabel lampiran yang menambahkan kolom **NIK** serta **NO. TELEPON/HP**
  (`JudulKolomLampiran(..., nikDanTelepon)` — hanya kelompok KPM).
  Nomor & tanggal SK diambil
  dari form baris yang disimpan; nama & NIK dicetak dari lampiran, bukan dari
  satu orang.
- Bila baris berhasil tersimpan tetapi langkah berkasnya gagal (mis. berkas SK
  sedang terbuka di pratinjau), data tetap tersimpan, form berpindah ke mode
  ubah, dan pesan galat meminta menekan **Simpan** sekali lagi — penyimpanan
  berikutnya memperbarui baris yang sama, bukan membuat ganda.
- Tombol **SK** per baris dan panel SK tetap tersedia: dipakai untuk pratinjau,
  cetak ulang, SK bersama berlampiran, dan pengarsipan SK Bupati.

### SK bersama: satu SK untuk semua orang pada satu jabatan

Mode **SK bersama** di panel membuat **satu SK dan satu nomor untuk semua orang
pada jabatan terpilih sekaligus**, dengan daftar namanya dicetak pada **halaman
Lampiran** — bentuk yang lazim dipakai SK Linmas/Posyandu/KPM, dan tetap berguna
untuk jabatan yang dipegang beberapa orang (mis. KADUS per dusun, Ketua RT/RW):

- **Menyalakan mode**: centang *SK bersama — satu SK untuk semua orang pada
  jabatan ini*. Aplikasi langsung menyusun daftar nama dari data jabatan itu:
  **orang yang masih memegang jabatan** (AKTIF/MENUNGGU SK) didahulukan; bila
  jabatan itu tidak punya pemegang sama sekali, seluruh orangnya dipakai
  sebagai cadangan dan panel menjelaskannya.
- **Kelompok SK bersama terkunci**: **LINMAS, POSYANDU, dan KADER KPM** selalu
  dibuka dalam mode bersama — pilihannya menyala otomatis dan **tidak bisa
  dimatikan** (`SkWajibBersama` pada `PerangkatDesaViewModel`), sebab kelompok
  ini memang tidak pernah diterbitkan sebagai SK per orang. Jabatan yang belum
  punya orang tetap bisa dipratinjau sebagai contoh kosong; jabatan yang sudah
  punya orang baru bisa dipratinjau setelah daftar namanya termuat.
- **Data pribadi dari Data Warga**: pendidikan, pekerjaan, agama, golongan
  darah, status kawin, dan alamat diambil dari tabel Warga lewat NIK
  (`SkPerangkatLampiranService`, satu kali baca untuk semua NIK). NIK yang
  tidak ada di Data Warga tidak menggagalkan SK — kolomnya dicetak `-`.
- **Daftar di panel**: tiap nama punya isian **Peran** (mis. Ketua, Anggota;
  baris Posyandu diawali *Anggota*, sesuai kolom Jabatan pada contoh SK-nya)
  dan **Unit** (mis. `POSYANDU SAKURA I`) yang boleh disunting; baris yang tidak
  ikut dihapus, dan **Muat Ulang Daftar** menyusun ulang dari data jabatan.
  Unit yang diisi membuat lampiran dikelompokkan per unit dengan nomor
  sendiri-sendiri; unit kosong dicetak sebagai satu tabel.
- **Hasil PDF**: diktum pertama menjadi *"Mengangkat/Memberhentikan/Menetapkan
  nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini …"*,
  pertimbangan tidak lagi menyebut satu nama (SK pemberhentian memakai butir
  berlampiran), dan halaman kedua memuat tabel **NO, NAMA, JABATAN, TEMPAT &
  TGL. LAHIR, PENDIDIKAN, PEKERJAAN, ALAMAT, AGAMA, GOL. DARAH, STATUS KAWIN**
  — kolom pribadi hanya dicetak bila ada isinya; judul daftarnya memakai judul
  bawaan kelompok (`SkKelompokTemplate.JudulLampiran`) seperti contoh SK desa —
  *DAFTAR NAMA KADER POSYANDU DAN KADER POKJANAL DESA …* (Posyandu) dan
  *DAFTAR PENGANGKATAN ANGGOTA SATUAN PERLINDUNGAN MASYARAKAT (LINMAS) DESA …*
  (Linmas) — sedangkan kelompok lain memakai *DAFTAR {judul keputusan}*.
- **SK yang diterbitkan Bupati (Kepala Desa & BPD) dikecualikan** dari mode SK
  bersama karena hanya diarsipkan.

Berkas: model lampiran `SuDesApp.Core/Data/Models/SkLampiranData.cs`
(`BarisLampiranSk`, `SkPerangkatLampiran.Kelompokkan`), layanan
`SuDesApp.Core/Services/SkPerangkatLampiranService.cs`, baris panel
`SuDesApp.Wpf/ViewModels/LampiranBarisViewModel.cs`, dan pencetak lampiran di
`SkPerangkatGenerator` (`JudulKolomLampiran`, `SusunLampiran`).

### Contoh SK per jabatan (sesuai aturan yang berlaku)

Setiap jabatan perangkat desa punya **template SK sendiri**, bukan satu
template seragam. Susunannya mengikuti **naskah dinas desa** (Perbup Tata
Naskah Dinas, mis. Perbup 74/2020) dan ketentuan pengangkatan/pemberhentian
perangkat desa — judul "PENGANGKATAN/PEMBERHENTIAN {JABATAN}", butir
Menimbang a–c, blok Memperhatikan/Mengingat, diktum KESATU–KETIGA, dan
tembusan — lalu dirinci per jabatan menurut tugas pokok masing-masing
(Permendagri 84/2015 tentang SOTK Pemerintah Desa):

| Jabatan | Isi khusus pada SK-nya |
|---------|------------------------|
| Kepala Desa | Jalur pemilihan (berita acara pemilihan, laporan ke Bupati); sumber SK Bupati |
| Sekretaris Desa | Administrasi pemerintahan desa & pelayanan administratif |
| Kaur Keuangan | Urusan keuangan, penatausahaan & pelaporan keuangan desa |
| Kaur Perencanaan | RPJMDes/RKPDes, data dan evaluasi pembangunan |
| Kaur Umum dan Tata Usaha | Surat-menyurat, kearsipan, perlengkapan, administrasi umum & kepegawaian |
| Kasi Pemerintahan | Ketenteraman/ketertiban, administrasi kependudukan, politik & pemerintahan |
| Kasi Pelayanan | Pelayanan masyarakat, sosialisasi & penyuluhan program desa |
| Kasi Kesejahteraan | Ekonomi, sosial, agama, pendidikan, kesehatan, KB |
| Kepala Dusun | Pemerintahan, pembangunan, pembinaan & pemberdayaan wilayah dusun |

Yang berbeda antar jabatan:

- **Menimbang** memakai tugas pokok jabatannya; SK **Pemberhentian** memakai
  butirnya sendiri (termasuk konsultasi kepada Camat sesuai Permendagri
  83/2015), tidak lagi memakai pertimbangan pengangkatan.
- **Memperhatikan** mengikuti contoh SK perangkat desa: pengangkatan memakai
  **Berita Acara Hasil Penjaringan dan Penyaringan Calon Perangkat Desa** +
  **Surat Rekomendasi Camat**; pemberhentian memakai **surat pengunduran diri**
  + **surat rekomendasi Camat**.
- **Mengingat** memuat UU 6/2014 tentang Desa, UU 23/2014 tentang Pemerintahan
  Daerah (terakhir diubah UU 9/2015), **PP 16/2026 tentang Peraturan
  Pelaksanaan UU Desa** — pengganti PP 43/2014 beserta perubahannya yang
  sudah dicabut — Permendagri 83/2015 jo. 67/2017, Permendagri 84/2015,
  Perda SOTK `{KABUPATEN}`, dan Perdes SOTK: daftar baku naskah dinas desa
  menurut aturan yang berlaku. Aturan yang sudah dicabut **tidak** lagi
  dicantumkan supaya dasar hukum SK selalu relevan dengan kenyataan.
- **Diktum KETIGA** menyebut jabatan yang bersangkutan: kewajiban melaksanakan
  tugas sesuai SOTK dan melaporkan hasilnya kepada Kepala Desa.

**Kelompok lain juga punya template sendiri** di `SkPerangkatKatalog._perKelompok`:
PERANGKAT DESA, RT/RW, BPD, LINMAS, POSYANDU, KADER KPM, KADER STUNTING,
STAF DESA, OPERATOR DESA, PKK, LPM, dan KARANG TARUNA. Tiga yang baru
ditambahkan (Staf Desa, Operator Desa, dan Karang Taruna) memakai dasar hukum
masing-masing: SOTK Pemerintah Desa untuk staf/operator dan PP 87/2007 tentang
Pemberdayaan Karang Taruna untuk Karang Taruna — sedangkan LPM memakai
Permendagri 18/2018 **tentang Lembaga Kemasyarakatan Desa dan Lembaga Adat
Desa** (judul aslinya; LPM termasuk lembaga kemasyarakatan desa). BPD memakai
Permendagri 110/2016 tentang Badan Permusyawaratan Desa. Hanya kelompok
**LAINNYA** yang sengaja tanpa template.

### Contoh SK desa: Linmas, Posyandu & KPM

Template **LINMAS** dan **POSYANDU** disusun mendekati contoh SK desa
pemakai, bukan sekadar template umum:

- **LINMAS** — judul *PENGANGKATAN KEANGGOTAAN SATUAN PERLINDUNGAN
  MASYARAKAT (LINMAS) DESA …* (pemberhentian memakai bentuk sama);
  Menimbang tiga butir penyegaran keanggotaan Linmas; Memperhatikan
  merujuk keputusan Kepala Desa tentang pengangkatan anggota LINMAS;
  Mengingat 12 butir mengikuti contoh **tetapi memakai aturan yang masih
  berlaku**: UU 14/1950, UU 2/2002, UU 3/2002, UU 10/2004, lalu **UU 23/2014
  tentang Pemerintahan Daerah**, **UU 1/2022 tentang Hubungan Keuangan
  Pusat–Daerah**, dan **PP 16/2026** (menggantikan UU 32/2004, UU 33/2004,
  dan PP 72/2005 yang sudah tidak dipakai), Kepres 56/1972, Kesepakatan
  Bersama Mendagri–Kapolri 119/1572/SJ/2002, Keputusan Mendagri 36/1979,
  Perbup, Perdes; diktum KETIGA = kewajiban pelatihan, piket, pembinaan,
  dan Siskamling.
- **POSYANDU** — judul *PENGANGKATAN KADER POSYANDU DAN KADER POKJANAL
  DESA …* (menyebut Pokjanal sesuai contoh walau aplikasi belum punya
  jabatan sendiri untuk itu); Menimbang kesehatan ibu & anak melalui
  Posyandu; Mengingat **UU 17/2023 tentang Kesehatan** (UU 23/2002 sudah
  dicabut), UU 6/2014, PP 16/2026, Permendagri 18/2018, Permendagri 19/2011,
  **Permendagri 13/2024 tentang Pos Pelayanan Terpadu**, dan Permendagri
  1/2013 s.d. Perda Kabupaten dan Perdes (Permendagri 5/2007 yang dahulu
  dipakai sudah tidak berlaku); diktum KETIGA = tugas pokok dan fungsinya.
- **KADER KPM** — judul penetapan *PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM)
  KONVERGENSI PENCEGAHAN STUNTING DESA …* (tahun anggaran pada contoh sengaja
  tidak dikunci); Menimbang dua butir seperti contoh (pelaksanaan Konvergensi
  Pencegahan Stunting → penetapan SK Kepala Desa); Mengingat sembilan butir
  mengikuti contoh (UU 6/2014, **PP 16/2026** — dahulu PP 43/2014,
  Permendagri 114/2014, Perpres
  83/2017, Perpres 72/2021, Perbup percepatan penurunan stunting, Perda
  Kabupaten tentang Desa, hasil musyawarah desa, Perdes); diktum memakai
  bentuk *“Mengangkat … sebagai Kader Pembangunan Manusia (KPM) Desa …”*
  (`KetentuanPenetapan` untuk satu orang dan
  `KetentuanPenetapanLampiran` untuk SK bersama) tanpa klausul masa jabatan.
- **Anggota Pokjanal** dicatat sebagai kader dengan isian **Unit**
  `POKJANAL DESA`, sehingga menjadi blok tersendiri pada lampiran lalu
  disusul blok Posyandu tiap dusun — persis susunan contoh SK desa.
- Butir diktum lampiran SK bersama memakai
  `SkKelompokTemplate.KetentuanPengangkatanLampiran` (LINMAS & POSYANDU) atau
  `KetentuanPenetapanLampiran` (KPM) bila ada, selain itu butir baku katalog.
- **Linmas & Posyandu selalu SK bersama** (`Bersama = true`): panel SK
  menyalakan mode bersama otomatis dan mengunci pilihannya, sehingga satu
  dokumen memuat seluruh anggotanya dengan lampiran daftar nama — sama seperti
  file contoh SK desa; judul daftar lampirannya pun mengikuti file contoh
  (`SkKelompokTemplate.JudulLampiran`).

Panel SK **tidak lagi** menyimpan dokumen rujukan contoh SK per jabatan
(folder `ContohSkPerangkat`, tombol *Lampirkan Contoh*/*Buka Dokumen*/*Contoh
dari Aplikasi*/*Hapus*, beserta utilitas `ContohSkPerangkat.cs`) — bagian itu
dihapus agar panel fokus pada penerbitan dan pengarsipan SK. Yang tersisa:
baris yang belum ada orangnya tetap bisa dicetak sebagai **contoh** (nama dan
tanggal digariskan titik-titik), jadi bentuk dokumen masih bisa dilihat tanpa
berkas rujukan.

Template per jabatan ada di `SkPerangkatKatalog` (`CariJabatan`, `Untuk`,
`JabatanBerTemplate`); kelompok lain (RT/RW, Linmas, Posyandu, BPD, kader)
memakai template kelompoknya. Generator memakai template per jabatan lebih dulu
lewat `SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok)`, dan panel SK
menampilkan catatan template serta daftar dasar hukum jabatan yang sedang
dipilih. Kartu/urutan jabatan panel ada di
`SuDesApp.Core/Data/Models/SkJabatanAksiKatalog.cs`
(`UrutanJabatanPanel()` = `JabatanPerangkat.Resmi`).

### Susunan PDF SK sesuai contoh (garuda & kop naskah)

`SkPerangkatGenerator` menyusun halaman SK mengikuti contoh SK desa
pemakai sekaligus format naskah dinas Perbup 74/2020:

1. **Gambar Garuda Pancasila** rata tengah di puncak halaman, di atas segalanya
   (±71 × 77 pt, meniru ukuran pada contoh). Gambar ditanam sebagai embedded
   resource `SuDesApp.Core/Resources/GarudaPancasila.png` dan diambil lewat
   `SuDesApp.Core/GeneratorPdf/GambarBawaan.cs`; bila resource tidak ada,
   gambar dilewati agar dokumen tetap terbentuk.
2. **Blok kepala naskah** tiga baris rata tengah, tebal: `KEPALA DESA <desa>`,
   `KECAMATAN <kec>`, `KABUPATEN <kab>` (huruf kapital).
3. **`KEPUTUSAN KEPALA DESA <desa>`** (tebal) + baris `NOMOR : …`, lalu
   **`TENTANG`** (tebal) + judul SK (14 pt, tebal, tanpa bergaris bawah).
4. **Baris penetap** `KEPALA DESA <desa>,` rata tengah; contoh SK tidak memuat
   kalimat pembuka, jadi baris ini langsung diikuti Menimbang.
5. **`MEMUTUSKAN :`** (tebal, rata tengah), baris **`MENETAPKAN :`** berisi
   judul (seperti contoh), lalu diktum: penanda pertama (PERTAMA/KESATU) berisi
   kalimat keputusan, **KEDUA** = kewajiban pelaksanaan, **KETIGA** = ketentuan
   berlaku — pada contoh, ketentuan berlaku selalu menjadi diktum terakhir.
6. **Blok tanda tangan**: kolom kiri `Tembusan`, kolom kanan `Ditetapkan di :`
   + `Pada tanggal :` (label berjajar sebelum titik dua, bukan "{desa},
   {tanggal}"), lalu `KEPALA DESA <desa>` rata tengah kolom dan nama
   penandatangan tebal bergaris bawah; diakhiri `1 (satu) lembar Lampiran`
   bila ada lampiran.
7. **Halaman lampiran**: kepala berlabel rata kiri — baris `Lampiran Keputusan
   Kepala Desa <desa>`, `Nomor :`, `Tanggal :`, `Tentang :` — lalu judul
   `DAFTAR …` rata tengah (tebal) beserta tabel nama per unit.

**Tata letak & tipografi**:

- **Huruf Times** di seluruh dokumen dengan **metrik bawaan PDF**
  (`PdfStandardTimes` di `SudesPdf.Core`): tidak ada berkas font yang didaftarkan
  atau dibundel, dan lebar tiap huruf tetap sama di komputer mana pun.
- Teks isi **11 pt** dan judul SK **13 pt** tebal, dengan tinggi baris rapat
  (`LineHeight 1.05`) sehingga naskah tidak boros halaman.
- **Ukuran halaman mengikuti Pengaturan Cetak** — A4 atau F4, portrait,
  margin 36 pt di keempat sisi — bukan ukuran yang dipaku di generator, sama
  seperti dokumen lain aplikasi.
- **Satu halaman utuh**: blok `MEMUTUSKAN` + diktum + blok tanda tangan
  dibungkus `ShowEntire()` sehingga tidak pernah terbelah ke halaman
  berikutnya; dulu diktum terpotong dan blok tanda tangan pindah halaman.
  Butir Menimbang/Mengingat masih boleh mengalir bila memang panjang.
- Nilai kosong tetap dicetak bergaris titik-titik, dan SK jadi
  menandatangani dengan nama Kepala Desa dari Pengaturan Surat.

## Menu SOTK Desa

Satu menu setelah **Perangkat Desa** memuat dua dokumen yang saling terkait:
**SOTK Desa** dan **Perdes SOTK**. Keduanya memakai data desa yang sudah tersimpan
dan bermuara ke **Buku Perdes**. Katalog **contoh Perdes** tampil di halaman
**PERDES** dan membuka naskah berhalaman yang dapat disunting — contoh tidak lagi
membuka berkas Word di luar aplikasi.

Saat sebuah contoh dibuka, aplikasi memuat **data naskah berhalaman** dari
`Templates/Perdes/*.json` lalu mengganti identitas contoh dengan identitas dari
Pengaturan Surat: nama desa, kecamatan, kabupaten/kota (termasuk gelar
Bupati/Wali Kota dan kutipan peraturan daerah) menggantikan teks contoh
Sumberjaya/Tempuran/Karawang. Tidak ada dokumen Word yang dibuka atau disalin;
sumber contoh tidak pernah diubah karena setiap formulir bekerja pada data
halaman di memori. Naskah yang tersimpan sudah sesuai desa pengguna.

### SOTK Desa (`SotkDesaView`)

- PDF SOTK memakai kop surat desa dari Pengaturan Surat (logo, identitas pemerintah kabupaten/kota, kecamatan, desa, alamat, dan garis kop), lalu judul dan isi ringkasan organisasi. Tabel menampilkan data perangkat aktif; jabatan yang belum terisi diberi tanda `-`. Judul pasal diratakan ke tengah.
- Susunan, jumlah, dan nomenklatur jabatan mengikuti Peraturan Bupati/Wali Kota dan klasifikasi Desa yang berlaku; template tidak mengunci seluruh desa pada satu struktur yang sama. Perdes SOTK merupakan dasar hukum organisasi yang mengikat.

### Perdes SOTK di halaman SOTK Desa

- PDF Perdes memakai lambang Garuda dan sistematika contoh Perdes pada lampiran tata naskah dinas desa. Naskah memuat nomor/tahun, Menimbang, Mengingat, kesepakatan BPD dan Kepala Desa, diktum Menetapkan, pasal, penetapan, dan pengundangan. Judul pasal rata tengah. Nama anggota BPD ditarik dari daftar BPD aktif di menu Perangkat Desa; susunan perangkat aktual dicetak sebagai lampiran.
- Tanggal penetapan berasal dari input Perdes. Tanggal pengundangan dan nomor Lembaran Desa disediakan sebagai ruang isian setelah proses pengundangan. Blok Sekretaris Desa dan nomor Lembaran Desa rata kiri.
- Panel memuat nomor, tanggal (`dd-mm-yyyy`), dan rujukan Perda/Perbup/Perwali setempat. **Simpan Draf** menyimpan isian per database desa; **Simpan ke Buku Perdes** menerbitkan naskah sebagai baris PERDES beserta PDF-nya, sehingga bisa dibuka, disunting metadatanya, dan dihapus dari halaman PERDES seperti peraturan desa lain. Tautan arsip disimpan bersama draf agar penyimpanan ulang memperbarui baris yang sama, bukan membuat duplikat. Tempat penetapan mengambil nama desa dari Pengaturan Surat.
- Panel **Buku Perdes** menunjukkan apakah naskah sudah tertaut ke arsip, nomor dan tanggalnya, serta menyediakan tombol **Buka Buku Perdes**. Bila baris arsip dihapus dari menu PERDES, status berubah dan penyimpanan berikutnya menerbitkan baris baru.
- Mengingat memuat UU Desa beserta perubahannya, peraturan pelaksana yang relevan, Permendagri 84/2015 dan 111/2014, serta rujukan daerah yang dimasukkan operator. Rujukan daerah wajib dipastikan berlaku pada wilayah masing-masing.
- Sistematika mengikuti contoh Perdes dalam lampiran Tata Naskah Dinas Pemerintah Desa yang tersedia pada `docs/regulasi/perbup74-lampiran.txt`. Perda/Perbup tiap daerah dapat mengatur format atau struktur lebih khusus.
Rujukan nasional PP 16/2026 tercantum pada [JDIH BPK](https://peraturan.bpk.go.id/Details/349409/pp-no-16-tahun-2026).
Rujukan daerah harus dipastikan operator pada JDIH kabupaten/kota masing-masing;
aplikasi tidak menganggap satu Perbup berlaku untuk seluruh Indonesia.

### Contoh Perdes dan halaman naskah

- Katalog contoh berada di kartu **CONTOH PERDES** pada halaman **PERDES**
  (`ContohPerdesCatalog`). Contoh yang tersedia: PADes, APBDes, BPD, kewenangan
  berdasarkan hak asal-usul, kewenangan lokal berskala desa, dan kewenangan
  yang ditugaskan. Isinya berasal dari `Templates/Perdes/*.json`, bukan berkas
  Word.
- Memilih contoh membuka **formulir tambah PERDES** dengan isian awal nomor,
  tanggal, tentang, keterangan, dan halaman naskah yang dapat disunting. Setiap
  halaman dimulai di lembar PDF baru; isi yang panjang mengalir ke lembar
  berikutnya. Tombol **Tambah Halaman** menyisipkan bagian baru, sedangkan Naik,
  Turun, dan Hapus Halaman mengatur urutannya. **Pratinjau PDF** merender naskah
  yang sedang diisi tanpa meninggalkan formulir. Nama halaman hanya menjadi label
  daftar; teks yang tercetak ada di kotak Isi halaman.
- Nomor, tahun, dan tanggal pada kepala surat serta penutup diambil dari kolom
  formulir saat pratinjau/simpan, termasuk tahun pada baris Lembaran Desa.
  Halaman lampiran (mis. Keputusan BPD pada contoh PADes) tidak ikut diubah
  karena nomor dan tanggalnya milik dokumen lampiran.
- Saat Simpan, `NaskahPerdesGenerator` (SudesPdf) menulis PDF ke arsip dan
  menyimpan sumber halaman di berkas `<nama-pdf>.naskah.json` di sampingnya.
  Saat data dibuka kembali, sumber itu dimuat sehingga halaman dapat disunting
  lagi; PDF lama tetap ada sampai penggantiannya berhasil ditulis. Menghapus
  data PERDES juga menghapus PDF dan sumber naskahnya.
- Baris arsip tetap memakai skema `DataKeputusan` (`JenisKeputusan`, `Nomor`,
  `Tanggal`, `Tentang`, `Keterangan`, `FileWord`); kolom `FileWord` kini menunjuk
  PDF hasil generator, bukan dokumen Word.
- Katalog tidak menetapkan contoh sebagai peraturan yang berlaku; nomor,
  tanggal, dasar hukum, dan isi wajib diperiksa operator.

Generator dan model SOTK ada di
`SuDesApp.Core/GeneratorPdf/SotkPerdesGenerator.cs` (`SotkDesaData`,
`PerdesSotkData`, `SotkDesaGenerator`, `PerdesSotkGenerator`, beserta helper
`NaskahDesa` untuk lambang Garuda, judul bab, pasal, dan blok penetapan).
Naskah Perdes dari contoh memakai
`SuDesApp.Core/GeneratorPdf/NaskahPerdesGenerator.cs` dengan model
`NaskahPerdes`/`NaskahPerdesHalaman`, pemuat contoh dan penyimpanan sampingan di
`NaskahPerdesBerkas.cs`. View model dan halaman SOTK ada di
`SuDesApp.Wpf/ViewModels/SotkDesaViewModel.cs` dan
`SuDesApp.Wpf/Views/SotkDesaView.xaml`; katalog contoh di
`SuDesApp.Wpf/ViewModels/ContohPerdesCatalog.cs`; penyunting halaman di
`SuDesApp.Wpf/ViewModels/InputKeputusanViewModel.cs`; dan penyimpanan draf
beserta tautan arsip di `SuDesApp.Wpf/Services/PerdesSotkStore.cs`. Isian Perdes
dan SOTK tampil bersama di halaman **SOTK Desa** yang dibuka dari menu DATA DESA;
katalog contoh Perdes tampil di halaman **PERDES**.

## Ringkasan kepala bagian

Halaman ini tidak memakai deretan kartu KPI di atas pemilih bagian; angka
ringkasannya menempel pada konteksnya, yaitu **kepala bagian yang sedang
tampil**:

- Chip **ORANG** — jumlah baris berisi orang pada bagian itu.
- Chip **JABATAN** — jumlah jabatan resmi bagian itu (`JabatanBagian`).
- Chip **BELUM TERISI** — jabatan pada bagian itu yang belum ada orangnya.

Angka yang sama tampil sebagai bilangan kecil di ujung tiap tombol pemilih
bagian, sehingga kelompok yang belum terisi terlihat tanpa membukanya. Angka
ini dihitung dari koleksi tabel yang sama dengan yang ditampilkan
(`TabelAktif`), bukan dari penghitung terpisah, jadi tidak bisa berbeda dari
isi tabel.

Rincian per jabatan tetap tersedia di tabel: setiap jabatan tanpa penghuni
punya barisnya sendiri bertanda **belum terisi**.

## Ekspor Excel dan Cetak PDF

**Ekspor Excel** (`EksporExcelCommand`) — **hanya bagian yang sedang tampil**:

- Bagian kosong diperiksa lebih dulu: bila tidak ada satu pun baris orang,
  muncul pesan dan berkas tidak dibuat.
- Dialog simpan bawaan Windows (`.xlsx`) berjudul *Ekspor Data <Bagian>*, nama
  berkas `Perangkat_Desa_<Bagian>_waktu.xlsx` (bagian Perangkat Desa tanpa
  akhiran `<Bagian>`), dan lembar kerja Excel-nya memakai judul dokumen bagian
  itu (mis. `DAFTAR KETUA RT DAN KETUA RW`).
- Baris yang diekspor = **baris berisi orang saja pada bagian itu** (baris
  penanda ikut dilewati), kolomnya **persis 18 kolom berkas "DATA PERANGKAT"
  contoh pemakai** dengan urutan sama: `DESA`, `NAMA PERANGKAT`, `JABATAN`,
  `JENIS KELAMIN`, `TEMPAT LAHIR`, `TGL. LAHIR`, `NIK`, `AGAMA`,
  `NO. TELPON`, `NOMOR REKOMENDASI`, `TGL. REKOMENDASI`,
  `NO SK PENGANGKATAN`, `TGL SK PENGANGKATAN`, `PENDIDIKAN`, `TAHUN LULUS`,
  `NO. IJASAH`, `NOREK BJB`, `BPJS KES`. Kolom `DESA` diisi nama desa dari
  Pengaturan Surat; kolom `JABATAN` bernomor urut per jabatan berbeda (mis.
  `1. Kepala Desa`, `8. Kepala Dusun` untuk lima Kepala Dusun) meniru berkas
  contoh; nilai kosong ditulis `-`.
- Ditulis dengan SudesExPlus (pustaka .xlsx milik aplikasi sendiri); NIK/rekening
  sebagai teks supaya angka awal nol tidak hilang; header diberi latar biru tebal.

**Cetak PDF** (`CetakPdfCommand`) — **hanya bagian yang sedang tampil**:

- Menghasilkan PDF A4/F4 **landscape** (mengikuti Pengaturan Cetak) berisi kop
  surat desa, judul sesuai bagian, tanggal cetak + ringkasan (jumlah orang +
  jabatan belum terisi), **tabel 18 kolom yang sama persis dengan ekspor
  Excel/berkas contoh** (teks 7pt supaya muat), dan blok tanda tangan Kepala
  Desa.
- Judul dokumen per bagian (`JudulDokumenAktif`):

  | Bagian | Judul |
  |--------|-------|
  | Perangkat Desa | DAFTAR PERANGKAT DESA |
  | RT/RW | DAFTAR KETUA RT DAN KETUA RW |
  | BPD, LINMAS, Posyandu, Kader KPM, Kader Stunting, Staf Desa, Operator Desa, PKK, LPM, Karang Taruna | DAFTAR &lt;NAMA BAGIAN&gt; |

- Isi tabel = **hanya baris orang yang tercatat pada bagian itu** (baris
  jabatan kosong tidak dicetak sebagai baris — jumlahnya tampil pada ringkasan
  di atas); tidak menyambung dengan bagian lain.
- Berkas ditulis ke folder `TempPDF` dengan akhiran nama bagian
  (`PerangkatDesa_<Bagian>_waktu.pdf`, ikut dibersihkan `TempPdfCleanup`), lalu
  dibuka pada **pratinjau SudesPdf** yang dipakai seluruh dokumen aplikasi — dari
  pratinjau operator bisa mencetak atau menyimpan seperti biasa.
- Generator: `SuDesApp.Core/GeneratorPdf/StrukturPerangkatGenerator.cs`
  (mesin SudesPdf, kop memakai `KopSurat.BarisKop`, terdaftar di DI sebagai
  `StrukturPerangkatGenerator`).

**Formulir Pendataan** (`CetakFormulirCommand`) — tombol pada bilah aksi
bagian Perangkat Desa, untuk **menarik data perangkat baru** di lapangan:

- Menghasilkan blanko **portrait** (A4/F4 mengikuti Pengaturan Cetak) satu
  halaman berisi kop surat desa, judul *FORMULIR PENDATAAN DATA PERANGKAT
  DESA*, petunjuk pengisian singkat, dan **18 isian yang sama persis dengan
  kolom berkas "DATA PERANGKAT"/ekspor Excel** — dikelompokkan agar mudah
  diisi: identitas (DESA s.d. NO. TELPON), pendidikan (PENDIDIKAN, TAHUN
  LULUS, NO. IJASAH), pengangkatan/rekomendasi (NOMOR REKOMENDASI s.d. TGL SK
  PENGANGKATAN), serta keuangan & kesehatan (NOREK BJB, BPJS KES).
- Isian yang belum ada dicetak garis titik-titik, kolom `DESA` sudah terisi
  nama desa dari Pengaturan Surat, dan tiap baris diberi petunjuk kecil (mis.
  format tanggal `dd-mm-yyyy`, 16 digit NIK). Blok tanda tangan: kiri *Yang
  memberi data* (kosong untuk ditandatangani) dan kanan Kepala Desa dengan
  nama dari Pengaturan Surat.
- Berkas ditulis ke folder `TempPDF` (`FormulirPerangkat_waktu.pdf`) lalu
  dibuka pada pratinjau SudesPdf, sehingga operator bisa memperbanyak dan
  mencetaknya seperti dokumen lain.
- Generator: `SuDesApp.Core/GeneratorPdf/FormulirPerangkatGenerator.cs`
  (`FormulirPerangkatData`/`FormulirPerangkatGenerator`, terdaftar di DI).

## Yang tidak ada di halaman ini

- **Pencarian teks**, tombol **Muat Ulang**, filter **Jabatan** dan
  **Wilayah**, **Filter Status**, serta tombol **Cari** (penerap saringan).
- **Wakil Kepala Desa**, jabatan lama di luar daftar bagian (`KEPALA
  SEKRETARIAT`, `KAUR PEMASARAN`, `KEPALA SEKSI`, `LAINNYA`), beserta data
  yang tidak masuk bagian mana pun — semuanya tetap di database.
- Panel **SK per kelompok** (BPD, Linmas, Posyandu, dst.) untuk daftar
  anggotanya masing-masing — halaman ini memakai **panel SK per jabatan**,
  termasuk **SK bersama** berlampiran bagi jabatan yang dipegang beberapa
  orang.

## Catatan teknis

- Daftar jabatan resmi & pemetaan nama lama:
  `SuDesApp.Core/Data/Models/PerangkatDesaData.cs` (`JabatanPerangkat.Resmi`,
  `AdalahResmi`, `Normalisasi`, `NamaLama`). Entity `PerangkatDesa` memuat
  kolom baru `NomorRekening`, `NomorBPJS`, serta turunan `ToolTipAksiSk`.
- Skema & repository: `SuDesApp.Core/Data/Repositories/PerangkatDesaRepository.cs`
  — kolom baru ikut di `KolomSelect`, INSERT, UPDATE, `Ddl.CreateTable`, dan
  `KolomTambahan` (untuk database lama).
- Baris tabel: `SuDesApp.Wpf/ViewModels/BarisPerangkatViewModel.cs` —
  pembungkus satu baris (jabatan, data diri, nomor SK/tanggal pengangkatan,
  pendidikan, rekening, BPJS, wilayah, status, penanda baris kosong, dan
  `ToolTipAksiSk`).
- Logika halaman: `SuDesApp.Wpf/ViewModels/PerangkatDesaViewModel.cs` —
  `LoadAsync` membaca seluruh data sekali ke salinan `_semua`;
  `SusunBaris` mengisi **seluruh** tabel bagian (`IsiTabel` per bagian) dari
  salinan itu, lalu `SusunBarisBagian` memperbarui angka pada tombol pemilih
  bagian dan `SegarkanRingkasanBagian` menyegarkan chip kepala bagian;
  `BukaFormDariBarisAsync` membuka form tambah/ubah;
  `EksporExcelAsync`/`CetakPdfAsync` melayani ekspor dan cetak; sedangkan
  `BukaPanelSkAsync`/`PratinjauSkAsync`/`ArsipkanSkAsync` melayani panel SK.
- Pemilih bagian: enum `BagianPerangkat` (12 nilai) + pasangan
  `Tampilkan<Bagian>Command` + koleksi `Items<Bagian>`; `PilihBagian` mengganti
  tabel yang tampil (`ItemsAktif` → `TabelAktif`), judul kepala bagian, dan
  angkanya, tanpa membaca ulang database sebab seluruh tabel sudah ada di
  memori. Di XAML hanya ada **satu** tabel dan **satu** kepala bagian; dulu
  tiap kelompok dibungkus `StackPanel` ber-`Visibility` sendiri.
  Tombolnya adalah dua `ItemsControl` yang menyalin `BarisBagianPemerintahan`
  dan `BarisBagianLembaga` (class `BarisBagian`: `Bagian`, `Judul`, `Ikon`,
  `Keterangan`, `Jumlah`, `Perintah`) dengan satu templat
  `TombolBagianItem` dan satu gaya `TombolBagian`: penanda tempel
  `u:BagianAktif.Bagian` dibandingkan dengan `NamaBagianAktifEnum` lewat
  `BagianSamaConverter` (`SuDesApp.Wpf/Views/BagianSamaConverter.cs`) untuk
  menyalakan warna tombol aktif, dan chip judul mengikuti `IkonBagianAktif` —
  tanpa satu gaya/properti per bagian.
- `Jumlah` pada `BarisBagian` dihitung dari daftar jabatan bagian yang sama
  (`JabatanBagian`) dan salinan `_semua`, jadi tidak perlu dua belas field
  penghitung per kelompok.
- SK bersama disusun di `PerangkatDesaViewModel` (`SkBersama`,
  `SkWajibBersama`, `SkBersamaDiubah`, `SkLampiran`,
  `MuatLampiranBersamaAsync`, `CalonLampiran`, `SegarkanLampiranCommand`):
  daftar orang dari jabatan terpilih dibungkus menjadi `LampiranSumber` lalu
  dilengkapi `SkPerangkatLampiranService.SusunAsync`; saat pratinjau,
  `PratinjauSkAsync` mengisi `SkPerangkatIsi.Lampiran` sehingga generator
  mencetak halaman lampirannya. `LampiranBarisViewModel` menyimpan peran/unit
  yang disunting operator beserta tombol hapus per baris. Kelompok
  `Bersama = true` (Linmas, Posyandu, Kader KPM) membuka panel dengan mode
  bersama terkunci; peran awal Posyandu *Anggota* dan unit lampirannya dihitung
  `PeranLampiran`/`UnitLampiran` — sama dengan SK jadi yang dibuat saat data
  disimpan.
- Pembatas bagian memakai `TabelAktif` (tabel bagian aktif): dipakai tabel
  yang tampil, chip ringkasan (`JumlahOrang/JumlahJabatan/JumlahKosong`),
  ekspor Excel, dan cetak PDF, sehingga tiap bagian berdiri sendiri.
  `JabatanBagian` menurunkan daftar jabatan satu bagian — dipakai
  `DaftarJabatanForm` (form), `PilihanJabatanSk` (panel SK), dan angka pada
  tombol pemilih bagian.
- Panel SK memakai `PilihanJabatanSk` (= daftar jabatan bagian tempat panel
  dibuka) supaya combo jabatan selalu sebagian dan tidak kosong; `SkAdaTemplate`
  memutus apakah blok dasar hukum tampil atau diganti kotak peringatan, dan
  `SkDariBupati` (lewat `SkJabatanAksiKatalog.SumberJabatan`) menyembunyikan
  tombol pratinjau sekaligus mengubah teks arsip untuk Kepala Desa & BPD.
- SK saat menyimpan: `SimpanAsync` memeriksa `FormSkWajib` (jabatan SK Bupati →
  wajib lampiran; pilihan berkas disimpan di `_formBerkasSkSumber` dan diarsipkan
  lewat `BerkasSkPerangkat.Arsipkan` setelah baris punya ID) lalu memanggil
  `BuatSkJadiAsync` untuk jabatan lain (`FormSkOtomatis`/`FormSkPesanOtomatis`
  pada form; tombol unggah pada panel disembunyikan lewat trigger
  `SkDariBupati`). Turunan form disegarkan `SegarkanFormSk`.
- Generator `SkPerangkatGenerator` mengisi placeholder `{JABATAN}`, `{NAMA}`,
  `{NIK}`, `{DESA}`, `{KECAMATAN}`, `{KABUPATEN}`, `{WILAYAH}`, `{NOMOR}`,
  `{MULAI}`, `{SELESAI}`, `{TANGGAL}`, `{ALASAN}`, dan `{PENANDA}`; nilai yang
  kosong dicetak bergaris titik-titik.
- `BooleanToVisibilityConverter` bawaan WPF mengabaikan `ConverterParameter`,
  jadi jangan memakai pola `ConverterParameter=Collapsed` untuk membalik nilai;
  untuk membalik pakai `InverseBoolToVisibilityConverter` (`InvBoolToVis` di
  `PerangkatDesaView.xaml`, dipakai panel SK). Pesan hasil aksi memakai
  `StringToVisibilityConverter` (`StringToVis`) supaya barisnya kolaps saat
  tidak ada pesan.
