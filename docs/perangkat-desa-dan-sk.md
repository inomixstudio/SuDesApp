# Data Perangkat Desa: Kelompok Jabatan & Surat Keputusan (SK)

Referensi fitur pengelompokan jabatan dan pencetakan SK di halaman
**Data Perangkat Desa**. Dasar hukum di bawah disusun dari regulasi nasional
yang umum dirujuk contoh SK desa (ciptadesa.com, JDIH kabupaten); operator
tetap disarankan menyesuaikan dengan Perbup/Perdes setempat sebelum
menetapkan.

## Kelompok jabatan

| # | Kelompok | Jabatan |
|---|----------|---------|
| 1 | PERANGKAT DESA | Kepala Desa, Wakil Kepala Desa, Sekretaris Desa, Kaur Keuangan, Kaur Perencanaan, Kaur Tata Usaha dan Umum, Kasi Pemerintahan, Kasi Pelayanan, Kasi Kesejahteraan, Kadus, Operator Desa, Staf |
| 2 | RT/RW | Ketua RT, Ketua RW |
| 3 | LINMAS | Linmas |
| 4 | LPM | Ketua LPM |
| 5 | PKK | Ketua PKK, Pengurus PKK |
| 6 | POSYANDU | Kader Posyandu |
| 7 | BPD | Ketua, Wakil Ketua, Sekretaris, Bendahara, Anggota BPD |
| 8 | KADER STUNTING | Kader Stunting |
| 9 | KADER KPM | Kader Pembangunan Manusia (KPM) |
| — | LAINNYA | Jabatan lama di luar daftar (tidak muncul di dropdown baru) |

Nama lama yang otomatis dipindahkan saat aplikasi dibuka (migrasi di
`DatabaseInitializer`): KAUR UMUM → KAUR TATA USAHA DAN UMUM, KAUR PELAYANAN →
KASI PELAYANAN, STAF SEKRETARIAT → STAF, KEPALA LKD → KETUA LPM.

## Template SK per kelompok

Tombol **SK** pada tiap baris membuka panel isian; tombol **Contoh SK** di
header mencetak SK kosong (garis titik-titik). Hasilnya PDF A4 dengan kop
surat desa yang sama dengan dokumen lain, dengan bentuk baku: judul, nomor,
blok **TENTANG** berisi judul kelompok (mis. *PENGANGKATAN ANGGOTA LINMAS DESA
SUMBERJAYA*), lalu Menimbang, Mengingat, dan diktum KESATU/KEDUA.

| Kelompok | Dasar hukum pada butir Mengingat |
|----------|----------------------------------|
| PERANGKAT DESA | UU 6/2014; PP 43/2014; Permendagri 83/2015 jo. Permendagri 67/2017; SOTK desa |
| RT/RW | UU 6/2014; PP 43/2014; Perdes RT/RW |
| LINMAS | UU 23/2014; UU 6/2014; Perbup linmas; Perdes pembinaan linmas |
| LPM | UU 6/2014; PP 43/2014; Permendagri 18/2018; Perdes LPM |
| PKK | UU 6/2014; Perpres 99/2017 (PPKK); Permendagri 36/2020; Perdes TP-PKK |
| POSYANDU | UU 36/2009 (Kesehatan); UU 52/2009 (KKB); UU 6/2014; Perbup & Perdes posyandu |
| BPD | UU 6/2014; PP 43/2014; Permendagri 112/2014 |
| KADER STUNTING | UU 6/2014; Perpres 72/2021 (RAN-PASTI); Perdes kader stunting |
| KADER KPM | UU 6/2014; Perpres 72/2021; Perpres 83/2017; Perdes kader |

Kelompok LAINNYA sengaja tidak diberi template SK karena dasar hukumnya
bergantung jabatan spesifik desa. Karena itu tombol **SK** untuk orang dengan
kelompok tersebut **ditolak dengan penjelasan** — bukan diam-diam dicetak memakai
template PERANGKAT DESA, yang akan menghasilkan judul dan dasar hukum yang salah.
Untuk jabatan seperti itu pakai menu **Surat Peraturan → SK / Keputusan**.

## Lampiran daftar nama (SK banyak orang)

Satu SK sering menetapkan puluhan orang sekaligus (anggota Linmas, kader
Posyandu se-Desa, pengurus PKK). Panel SK punya bagian **Lampiran daftar nama
(opsional)**: selama lampiran kosong, SK tetap dicetak untuk satu orang seperti
biasa; begitu lampiran berisi, diktum KESATU berubah menjadi *"Mengangkat
nama-nama sebagaimana tercantum dalam Lampiran …"* dan tabel namanya dicetak di
halaman terpisah di belakang SK.

Cara mengisi:

1. **Muat Anggota Kelompok** — mengisi lampiran dengan seluruh anggota kelompok
   terpilih (mis. Posyandu) yang masih memegang jabatan, urut data perangkat.
2. **Tambah** dari NIK warga — untuk orang yang belum tercatat di Data Perangkat
   Desa (mis. kader bantu). Namanya dan seluruh kolom pribadinya diambil dari
   halaman Data Warga.
3. Kolom **Peran** dan **Unit** tiap baris bisa disunting langsung, atau baris
   dibuang dengan tombol **Hapus**.

Kolom yang tercetak pada tabel lampiran:

| Kolom | Isi |
|-------|-----|
| NO | nomor baris, dimulai lagi dari 1 pada setiap unit |
| NAMA | nama lengkap |
| JABATAN | peran dalam unit (mis. Ketua, Sekretaris); hanya muncul bila perannya tidak sama semua |
| TEMPAT & TGL. LAHIR | dari data Warga (NIK), cadangan dari data perangkat |
| PENDIDIKAN, PEKERJAAN, ALAMAT | dari data Warga (NIK); pendidikan bercadangan ke data perangkat |
| AGAMA, GOL. DARAH, STATUS KAWIN | dari data Warga (NIK) |

Aturannya: hanya kolom **NO** dan **NAMA** yang selalu dicetak. Kolom lain
hanya muncul bila memang ada isinya — data warga sering tidak memuat golongan
darah, dan kolom penuh tanda hubung lebih buruk daripada kolom yang tidak ada.
Kolom **JABATAN** tampil begitu peran barisnya berbeda-beda, sehingga daftar
Linmas yang semuanya "LINMAS" tidak mengulang satu kata di setiap baris.

### Pengelompokan per unit Posyandu

Kolom **Unit** pada baris lampiran (dan pada form Data Perangkat) mengelompokkan
nama. Baris dengan unit yang sama dicetak dalam satu blok berjudul
**UNIT <nama unit>** — mis. `UNIT POSYANDU SAKURA I` — dan nomor barisnya mulai
lagi dari 1 di setiap unit, sama seperti lembar lampiran SK Posyandu. Baris yang
unitnya kosong dikumpulkan pada blok tanpa judul (daftar biasa, mis. anggota
Linmas se-Desa).

NIK yang tidak ditemukan di Data Warga tidak menggagalkan SK: barisnya tetap
dicetak, hanya kolom pribadinya berisi tanda hubung, dan panel memberi tahu
berapa orang yang datanya belum lengkap. Lampiran selalu dikosongkan bila
kelompok SK diganti, sebab daftar nama satu kelompok tidak cocok dengan judul
kelompok lain.

Catatan yang belum ada: SK contoh (tombol **Contoh SK**) tidak memuat halaman
lampiran, dan lampiran belum memuat blok **Tembusan**.

## Catatan teknis

- Model/katalog: `SuDesApp.Core/Data/Models/SkPerangkatData.cs` (placeholder
  `{NAMA}`, `{NIK}`, `{JABATAN}`, `{DESA}`, `{WILAYAH}`, `{NOMOR}`, `{MULAI}`,
  `{SELESAI}`, `{TANGGAL}`, `{ALASAN}`).
- Generator PDF: `SuDesApp.Core/GeneratorPdf/SkPerangkatGenerator.cs`
  (`BangunDokumen` untuk halaman badan + lampiran; `JudulKolomLampiran` menentukan
  kolom tabel yang benar-benar dicetak).
- Lampiran: model `Data/Models/SkLampiranData.cs` (baris + pengelompokan unit),
  penyusun `Services/SkPerangkatLampiranService.cs` (join data Warga lewat NIK,
  satu query untuk semua NIK), kolom **Unit** di tabel `PerangkatDesa`.
- Panel & perintah: `PerangkatDesaViewModel` (BukaPanelSk, CetakSk,
  MuatAnggotaLampiran, TambahLampiran) dan `PerangkatDesaView.xaml`.
- Uji: `tests/SuDesApp.Core.Tests/SkPerangkatLampiranTests.cs` membaca teks yang
  benar-benar tercetak per halaman lewat `GenerateSvg()` QuestPDF, sehingga judul
  kolom yang terpotong atau halaman lampiran yang hilang langsung ketahuan.
