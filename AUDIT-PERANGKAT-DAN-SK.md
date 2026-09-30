# Audit: Menu Data Perangkat Desa & Template SK Perangkat

Catatan internal (akar repo, **bukan** `docs/`) — dijalankan 29 September 2026 atas
permintaan pemelihara. Berisi hasil pemeriksaan menu perangkat, panel/generator SK, dan
perbandingannya dengan tiga SK asli Desa Sumberjaya, Kecamatan Tempuran, Kabupaten
Karawang yang dikirim pemelihara.

> **Data pribadi:** ketiga berkas contoh berada **di luar repo** (`D:\SK`) dan memuat
> NIK, alamat, nomor HP, serta tanggal lahir warga. Jangan pernah menyalinnya ke repo,
> lampiran rilis, atau `docs/`. Audit ini hanya mengutip **strukturnya**.

## Ringkasan

| # | Temuan | Tingkat |
|---|--------|---------|
| A1 | Sidebar tidak lagi punya jalan satu klik ke daftar **semua** kelompok perangkat | Sedang |
| B1 | Judul SK per kelompok (`JudulPengangkatan`/`JudulPemberhentian`) **tidak pernah dicetak**; blok "TENTANG" hilang dari PDF | Tinggi |
| B2 | Typo `ANGOTA` pada judul kelompok LINMAS (tersembunyi karena B1) | Rendah (nyata) |
| B3 | Nomor/tanggal SK dari panel **tidak disimpan** ke data perangkat; status "MENUNGGU SK" tak pernah berpindah | Tinggi |
| B4 | Kelompok tanpa template (LAINNYA) diam-diam memakai template PERANGKAT DESA | Tinggi |
| B5 | Tidak ada lampiran daftar nama, Memperhatikan, Tembusan, dan diktum ketiga | Tinggi (fitur) |
| B6 | Jenis "Penetapan" belum ada (SK KPM memakai penetapan, bukan pengangkatan) | Sedang |
| B9 | Tidak ada uji otomatis untuk katalog/generator SK | Sedang |

> **Status 29 September 2026:** B1, B2, B4, dan B9 sudah beres (P1 butir 1–2 + uji
> penjaga), dan **B7, B8, serta bagian lampiran dari B5 sudah beres** (lampiran
> daftar nama multi-orang dengan join data Warga + pengelompokan unit Posyandu —
> lihat §6a). Sisa: B3, B6, Memperhatikan/Tembusan, dan diktum ketiga.

---

## 1. Menu perangkat (sidebar)

| Kode | Temuan | Bukti |
|------|--------|-------|
| A1 | Akordeon "Data Perangkat Desa" punya 10 anak, semuanya membuka halaman **tersaring satu kelompok**. Tidak ada lagi satu klik untuk melihat seluruh perangkat seperti pada menu tombol tunggal sebelumnya. Halaman masih bisa menampilkan semuanya lewat dropdown **Kelompok** di halamannya (pilihan kosong), tapi jalur itu tidak lagi terlihat dari sidebar. | `MainWindowViewModel.cs:898-928` (loop 10 kelompok), `PerangkatDesaViewModel.cs:205-206` (pilihan kosong ada) |
| A2 | Dropdown **Kelompok** memakai `string.Empty` sebagai pilihan "semua", sehingga tampil sebagai baris kosong tanpa label — pola ini sama dengan filter status/wilayah, konsisten tetapi tidak menjelaskan diri. | `PerangkatDesaViewModel.cs:205-206`, `Views/PerangkatDesaView.xaml:130-142` |
| A3 | Anak akordeon tidak diberi `Izin` masing-masing; penyaringan izin hanya berjalan pada item top-level, dan judul seksi. Aman sekarang karena seluruh akordeon ikut terbuang bila `KelolaPerangkat` tidak dimiliki, tapi menambah anak dengan izin berbeda nanti **tidak akan tersaring**. | `MainWindowViewModel.cs:1038-1047` |
| A4 | Deep-link notifikasi/tujuan halaman `"PERANGKAT"` tetap membuka halaman tanpa filter — perubahan menu tidak merusaknya. | `MainWindowViewModel.cs:1135` |
| A5 | "SK / Keputusan" di bawah *Surat Peraturan* adalah **fitur SK kedua** (arsip keputusan/perdes: `DataKeputusan`, `KeputusanPeraturanGenerator`, `KeputusanView`) dengan field `Nomor`, `Tanggal`, `Tentang`. Tidak bertabrakan secara teknis, tetapi dua tempat berbeda menyandang nama "SK" berpotensi membingungkan operator. | `Core/GeneratorPdf/KeputusanPeraturanGenerator.cs`, `Views/KeputusanView.xaml:34` |

## 2. Panel & generator SK perangkat

| Kode | Temuan | Bukti |
|------|--------|-------|
| B1 | Generator hanya mencetak `"SURAT KEPUTUSAN KEPALA DESA"` + `NOMOR …`, lalu langsung `Menimbang`/`Mengingat`. Judul template per kelompok (mis. *PENGANGKATAN KETUA LPM DESA X*) dan **blok "TENTANG …"** tidak pernah tercetak. Kata `TENTANG` tidak ada di seluruh generator. Akibatnya `JudulPengangkatan`/`JudulPemberhentian` adalah properti mati: tidak direferensikan di Core, Wpf, maupun test. | `SkPerangkatGenerator.cs:131-176` (tidak ada TENTANG), `SkPerangkatData.cs:63-67` (properti didefinisikan) |
| B2 | Judul kelompok LINMAS berbunyi `"PENGANGKATAN ANGOTA {JABATAN} DESA {DESA}"` → seharusnya **ANGGOTA**. Belum berdampak karena B1, tapi akan langsung tercetak begitu B1 diperbaiki. | `SkPerangkatData.cs:154-155` |
| B3 | Setelah `CetakSkAsync` selesai, PDF dibuka sebagai pratinjau dan panel ditutup — `SkNomor`/`SkTanggal` tidak pernah ditulis balik ke data perangkat, padahal form data perangkat sudah punya `FormNomorSk`/`FormTanggalSk`. Operator harus mengetik ulang nomor SK, dan status **MENUNGGU SK** tidak pernah berpindah ke **AKTIF** otomatis. | `PerangkatDesaViewModel.cs:706-783`, `PerangkatDesaData.cs` (`NomorSK`, `TanggalSK`, `StatusPerangkat.MenungguSK`) |
| B4 | Bila orang yang dibuka punya kelompok `LAINNYA` (tanpa template SK), panel **diam-diam** memakai `PilihanSkKelompok.First()` = PERANGKAT DESA, sehingga bisa tercetak SK dengan dasar hukum kelompok yang salah. Tidak ada peringatan. | `PerangkatDesaViewModel.cs:682-684`, `668` |
| B5 | Bentuk diktum tetap: hanya `KESATU` + `KEDUA` dengan teks `KEDUA` baku. Tidak ada `Memperhatikan`, tidak ada `TEMBUSAN`, dan tidak ada diktum ketiga (kewajiban) seperti pada contoh LINMAS. | `SkPerangkatGenerator.cs:160-179` |
| B6 | Jenis SK hanya `Pengangkatan` dan `Pemberhentian`. SK KPM di contoh berjudul **"PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM)"**, yang tidak terwakili. | `SkPerangkatData.cs:9-13` |
| B7 | Generator tidak pernah membuat tabel: tidak ada `Table` pada `SkPerangkatGenerator`, jadi **lampiran daftar nama tidak mungkin** dicetak. Untuk Linmas 10 orang hari ini harus dicetak 10 PDF terpisah. | `SkPerangkatGenerator.cs` (tanpa `Table`) |
| B8 | Model perangkat tidak menyimpan Pekerjaan/Agama/GolonganDarah/StatusPerkawinan, padahal kolom-kolom itu diminta lampiran contoh. Semuanya sudah ada di tabel **Warga**, bisa diambil lewat NIK. | `PerangkatDesaData.cs` (`PerangkatDesa`), `SuratData.cs:481-542`, `WargaRepository.cs` |
| B9 | Tidak ada uji untuk katalog/generator SK (hanya `PerangkatDesaServiceTests.cs`). Itulah sebab B1 dan B2 lolos tanpa terdeteksi. | `tests/SuDesApp.Core.Tests/` |

## 3. Perbandingan bentuk dokumen: tiga SK asli vs aplikasi

| Bagian | Contoh asli (Sumberjaya) | Aplikasi | Selisih |
|--------|--------------------------|----------|---------|
| Kop | 3–4 baris `PEMERINTAH … KABUPATEN / KECAMATAN / DESA` + alamat + garis ganda | `KopSurat.BarisKop` yang sama dengan surat lain | setara |
| Judul | `KEPUTUSAN/SURAT KEPUTUSAN KEPALA DESA …` + `NOMOR …` + **`TENTANG …`** | `SURAT KEPUTUSAN KEPALA DESA` + `NOMOR …` | **B1: TENTANG hilang** |
| Menimbang | a, b, c (KPM 2 butir; LINMAS 3 butir) | ada, 3 butir, khusus pengangkatan | pemberhentian tanpa Menimbang |
| Mengingat | 7 (POSYANDU), 12 (LINMAS), **42 (KPM)** butir | 3–5 butir rapi per kelompok | lebih rapi, tapi belum ada penjaga duplikat |
| Memperhatikan | ada (LINMAS: hasil kesepakatan/keputusan sebelumnya; KPM: hasil musyawarah desa) | tidak ada | kurang |
| Diktum | `KESATU/KEDUA/KETIGA` (POSYANDU) atau `PERTAMA/KEDUA/KETIGA` (LINMAS) | `KESATU` + `KEDUA` baku | jumlah diktum tetap |
| Tembusan | 9 baris (Kesbang Linmas, Camat, Kapolsek, Danramil, BPD, LPMD, Babinkamtibmas, Babinsa, yang bersangkutan) | tidak ada | kurang |
| Lampiran | **ada di ketiga SK**: tabel 6–8 kolom, 1–30 nama, kadang dikelompokkan per unit (Pokjanal + Posyandu Sakura I–V) | tidak ada | **kesenjangan terbesar** |
| Tanda tangan | `Ditetapkan di` / `Pada tanggal` / `KEPALA DESA …` + nama | sama, plus baris kecil `(nama tanda tangan)` | setara |
| Tanggal | dapat **berbeda** antara badan SK dan lampiran (LINMAS: 03 vs 02 Januari 2024); POSYANDU: judul berkas 2024 tetapi isi 2026 | satu tanggal untuk SK | peluang: satu sumber tanggal + peringatan bila lampiran ≠ badan SK |

## 4. Kolom lampiran yang diminta contoh vs data yang tersedia

| Kolom pada contoh | Sumber di aplikasi |
|-------------------|--------------------|
| NAMA (sesuai KTP), NIK | `PerangkatDesa.Nama`, `.NIK` |
| TEMPAT & TGL LAHIR | `PerangkatDesa.TempatLahir` + `.TanggalLahir` |
| ALAMAT | `PerangkatDesa.Alamat`/`Dusun`/`RT`/`RW`, atau `Warga.AlamatDetail` |
| PENDIDIKAN | `PerangkatDesa.Pendidikan` |
| NO. TELEPON/HP | `PerangkatDesa.NomorHP` |
| JABATAN | `PerangkatDesa.Jabatan` |
| PEKERJAAN | **`Warga.Pekerjaan`** (join lewat NIK) |
| AGAMA | **`Warga.Agama`** |
| GOL DARAH | **`Warga.GolonganDarah`** |
| STATUS (perkawinan) | **`Warga.StatusPerkawinan`** |
| POSYANDU / unit | **tidak ada di model mana pun** — perlu atribut/penugasan unit |

## 5. Prioritas perbaikan

> **Status 29 September 2026:** P1 butir 1–2 sudah dikerjakan — blok **TENTANG**
> kini dicetak dari judul tiap kelompok (kapital, placeholder terisi), typo
> `ANGOTA` → `ANGGOTA` diperbaiki, dan kelompok tanpa template ditolak dengan
> penjelasan (tombol SK tidak lagi diam-diam memakai template PERANGKAT DESA).
> Uji penjaganya di `tests/SuDesApp.Core.Tests/SkPerangkatKatalogTests.cs`
> (katalog lengkap, judul tercetak tanpa sisa placeholder, PDF contoh tetap
> terbuat). Butir 3 dan seluruh P2/P3 belum dikerjakan.

**P1 — murah dan langsung benar**

1. ~~Cetak blok **TENTANG** dari `JudulPengangkatan`/`JudulPemberhentian` di
   `SkPerangkatGenerator.Isi` (setelah nomor), lalu perbaiki typo `ANGOTA` → `ANGGOTA` (B1, B2).~~ **selesai**
2. ~~Nonaktifkan tombol **SK** (atau beri pesan jelas) untuk kelompok tanpa template,
   jangan diam-diam memakai PERANGKAT DESA (B4).~~ **selesai**
3. Tambahkan anak pertama **"Semua Kelompok"** pada akordeon perangkat yang membuka
   halaman tanpa filter (A1).

**P2 — alur kerja**

4. Simpan nomor/tanggal SK kembali ke data perangkat setelah cetak, dan tawarkan
   perpindahan status `MENUNGGU SK` → `AKTIF` (B3).
5. Tambah uji: judul template benar-benar tercetak, tidak ada placeholder `{…}` tersisa,
   kelompok tanpa template ditolak, dan tiap kelompok di luar LAINNYA punya template (B9, B2).
6. Tambah jenis **Penetapan** di samping Pengangkatan/Pemberhentian (B6).

**P3 — fitur**

7. ~~Lampiran daftar nama multi-orang (tabel) dengan kolom yang diisi dari data perangkat
   dan **join ke Warga lewat NIK**, plus atribut unit untuk Posyandu/Pokjanal (B5, B7, B8).~~ **selesai** — lihat butir 7 di bawah.
8. Blok **Memperhatikan** dan **TEMBUSAN** yang bisa disunting per SK (B5).
9. Setelah SK tercetak, tawarkan pencatatan otomatis ke register **SK / Keputusan**
   supaya nomor, tanggal, dan "Tentang" muncul di register (A5).

### 6a. Lampiran daftar nama (selesai 29 September 2026)

Dikerjakan sebagai P3 butir 7 atas permintaan pemelihara, menyelesaikan **B5 (sebagian),
B7, dan B8**:

- Panel SK punya bagian **Lampiran daftar nama (opsional)**: muat seluruh anggota kelompok
  yang masih memegang jabatan, tambah per orang lewat NIK warga, sunting **Peran** dan
  **Unit** tiap baris, atau buang barisnya. Selama lampiran kosong, SK tetap satu orang.
- Diktum **KESATU** berubah menjadi kalimat lampiran (*"Mengangkat nama-nama sebagaimana
  tercantum dalam Lampiran …"*), dan Menimbang pengangkatan tidak lagi menyebut satu nama
  — penggantian "saudara/i {NAMA}" menjadi rujukan lampiran.
- Halaman lampiran terpisah tanpa kop: blok *Lampiran Keputusan Kepala Desa …* + nomor +
  tanggal, judul `DAFTAR …`, tabel nama bergaris, lalu tanda tangan Kepala Desa.
- Kolom dari tabel **Warga** lewat NIK (satu query untuk semua NIK): pekerjaan, agama,
  golongan darah, status perkawinan, alamat, tempat & tanggal lahir, pendidikan. B8
  diselesaikan tanpa menambah kolom pribadi ke tabel `PerangkatDesa` — datanya tetap satu
  sumber di tabel Warga.
- B7: generator kini memakai `Table` QuestPDF, dan lebar kolomnya diuji supaya judul kolom
  tidak terpotong di tengah kata (bukan sekadar "PDF-nya terbuat").
- Unit Posyandu/Pokjanal: kolom baru **Unit** pada tabel `PerangkatDesa` (migrasi otomatis),
  dipakai mengelompokkan baris menjadi blok `UNIT POSYANDU SAKURA I` dengan penomoran yang
  dimulai lagi dari 1 di tiap unit.
- Uji penjaganya di `tests/SuDesApp.Core.Tests/SkPerangkatLampiranTests.cs` (15 uji),
  termasuk membaca **teks yang benar-benar tercetak per halaman** lewat `GenerateSvg()`.
- Sisa yang belum dikerjakan dari B5: blok **Memperhatikan**, **TEMBUSAN**, dan diktum
  ketiga; serta penjaga selisih tanggal badan-lampiran (bagian 6).

## 6. Catatan kualitas data pada contoh asli (peluang pencegahan di aplikasi)

- SK KPM memuat **42 butir Mengingat** dengan banyak duplikat (mis. Permendagri 114/2014,
  Perbup 8/2019, Perbup 33/2019, Permendagri 110/2016, Perda 8/2016 masing-masing muncul
  dua kali) dan penomoran yang tidak lengkap di sebagian butir.
- SK LINMAS: badan SK bertanggal **03 Januari 2024**, lampirannya **02 Januari 2024**.
- SK POSYANDU: nama berkas "2024", isi memakai tahun **2026** pada nomor dan tanggal.
- Peluang: penjaga duplikat dasar hukum, satu sumber tanggal untuk badan + lampiran, dan
  peringatan bila tahun pada nomor SK ≠ tahun tanggal SK.

## 7. Berkas terkait

- `SuDesApp.Wpf/ViewModels/MainWindowViewModel.cs` — akordeon perangkat, penyaringan izin, deep-link.
- `SuDesApp.Wpf/ViewModels/PerangkatDesaViewModel.cs` — panel SK (`BukaPanelSkAsync`, `CetakSkAsync`), filter kelompok.
- `SuDesApp.Wpf/Views/PerangkatDesaView.xaml` — tabel, panel SK, tombol `SK`/`Contoh SK`.
- `SuDesApp.Core/Data/Models/SkPerangkatData.cs` — katalog template + isian SK.
- `SuDesApp.Core/GeneratorPdf/SkPerangkatGenerator.cs` — penyusun PDF SK.
- `SuDesApp.Core/Data/Models/PerangkatDesaData.cs` — jabatan, kelompok, status, model orang.
- `docs/perangkat-desa-dan-sk.md` — dokumentasi yang dibundel ke aplikasi (sudah memuat bagian lampiran).
- `SuDesApp.Core/Data/Models/SkLampiranData.cs`, `SuDesApp.Core/Services/SkPerangkatLampiranService.cs` — lampiran daftar nama dan pengelompokan unit.
- `tests/SuDesApp.Core.Tests/SkPerangkatLampiranTests.cs` — uji lampiran (termasuk teks tercetak per halaman).

## 8. Panel "SK per Jabatan" (selesai 30 September 2026)

Dikerjakan atas permintaan pemelihara: **akses SK dipisah per jabatan**, dengan tombol
aksi per jabatan yang sekaligus menampilkan SK yang siap dicetak/dipratinjau.
Contoh struktur SK per jabatan mengikuti referensi ciptadesa.com (sudah dijkstrak ke
katalog kelompok §3), **kecuali SK Kepala Desa** yang berupa berkas PDF dari Bupati (SKD).

- Berkas baru `SuDesApp.Core/Data/Models/SkJabatanAksiKatalog.cs`: urutan 11 kartu
  (Kepala Desa, Sekretaris Desa, Kaur Keuangan, Kaur Perencanaan, Kaur Tata Usaha dan
  Umum, Kasi Pelayanan, Kasi Pemerintahan, Kasi Kesejahteraan, Operator Desa, Staf,
  Jabatan Lainnya), hitungan orang/aktif/menunggu SK per jabatan, dan **sumber SK per
  jabatan** — Kepala Desa = Bupati, sisanya = Kepala Desa. Perbedaan penting dengan
  `SumberSkPerangkat` pada template kelompok: Bupati di sana menandai seluruh kelompok
  BPD; di sini hanya jabatan Kepala Desa, sebab SK Sekretaris/Kaur/Kasi tetap
  diterbitkan Kepala Desa.
- Halaman: panel kartu aksi di atas tabel (bisa ditutup lewat tombol ✕ yang
  mengembalikan filter ke semua kelompok). Klik kartu = saring daftar ke jabatan itu
  **dan** buka panel SK terisi — mode cetak/pratinjau untuk perangkat, mode arsip SKD
  untuk Kepala Desa (tooltip tombol pada tiap baris ikut menjelaskan).
- Panel SK punya properti `SkDiterbitkanBupati` (baris Kepala Desa **atau** kelompok
  BPD → mode arsip); pesan arsipnya membedakan Kepala Desa (SKD Bupati) dari BPD
  (SK bersama seluruh anggota).
- Panel disembunyikan otomatis bila daftar sedang difilter satu kelompok — kartu
  kelompok lain berhitung nol dan akan menyesatkan bila tetap tampil.
- Uji penjaga: `tests/SuDesApp.Core.Tests/PerangkatSkPerJabatanTests.cs` (9 uji):
  susunan 11 jabatan sesuai permintaan, kartu tetap dibuat pada pemasangan baru,
  hitungan per status, pemetaan sumber SK, label aksi tiga mode, penanda arsip SKD,
  dan penjaga pengikatan XAML↔VM (pelajaran C12).
