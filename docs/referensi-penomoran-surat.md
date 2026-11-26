# Referensi Penomoran Surat Desa — Perbup Kab. Karawang No. 74 Tahun 2020

Dokumen ini adalah acuan implementasi penomoran surat pada SuDesApp
(`SuDesApp.Core/Configuration/JenisSuratConfig.json`) sesuai peraturan yang
berlaku untuk **tingkat desa di Kabupaten Karawang**.

---

## 1. Identitas Regulasi

| Butir | Keterangan |
|---|---|
| Jenis | Peraturan Bupati (Perbup) |
| Nomor | **74 Tahun 2020** (Berita Daerah 2020/Nomor 76) |
| Judul | Tata Naskah Dinas Pemerintah Desa di Kabupaten Karawang |
| Ditetapkan/Diundangkan | 3 Desember 2020 |
| Status | **Berlaku** (perubahan terbaru Perbup 74/2025 adalah urusan organisasi Dinas Pendidikan, bukan tata naskah desa) |
| Rujukan yang disebut Perbup | Permendagri 54/2009 (sudah dicabut), Permendagri 47/2016, Perda Karawang 4/2019 tentang Desa |

> **Catatan**: Perbup 377/2023 tentang Tata Naskah Dinas hanya berlaku untuk
> lingkungan **Pemerintah Kabupaten** (mencabut Perbup 23/2017), bukan untuk
> Pemerintah Desa. Untuk desa, rujukannya tetap **Perbup 74/2020**.

> **Pembaruan rujukan nasional**: Permendagri 54/2009 telah dicabut oleh
> Permendagri 1/2023 tentang Tata Naskah Dinas di Lingkungan Pemerintah Daerah.
> Permendagri 1/2023 mengatur tata naskah dinas pemerintah daerah; jangan
> menganggapnya sendiri sebagai pengganti Perbup khusus tata naskah pemerintah
> desa. Status Perbup desa tetap perlu diperiksa di JDIH kabupaten terkait.

---

## 2. Sumber Resmi & Bukti (PDF)

1. **PDF resmi (terlampir di repo ini)** — batang tubuh + lampiran 28 format
   naskah dinas, diunduh dari JDIH Kab. Karawang:
   `docs/regulasi/Perbup-74-2020-Lampiran-Tata-Naskah-Dinas-Desa.pdf`
2. **Perbup Karawang Nomor 28 Tahun 2019 tentang Pedoman Klasifikasi Arsip di
   Lingkungan Pemerintah Kabupaten Karawang** — sumber kode awal (kelas
   klasifikasi) nomor surat. Lampiran resminya 94 halaman:
   <https://jdih.karawangkab.go.id/document/download/122/lampiran>
   (unduhan 26 November 2026, berkas `2019pb32150028.pdf`). Petikan kelas yang
   dipakai aplikasi disimpan di `docs/regulasi/perbup28-2019-klasifikasi-terpilih.txt`.
   Kelas yang dipakai
   aplikasi: `130 Pemerintah Kabupaten`, `300 Keamanan dan Ketertiban Umum` →
   `330 Keamanan` → `331 Kepolisian`, `460 Sosial` → `465 Kesejahteraan Sosial`
   (`465.3 Masyarakat Miskin: Raskin, BLT`), `470 Kependudukan dan Catatan
   Sipil` → `471 Pendaftaran Penduduk`, `472 Pencatatan Sipil`, `510 Perdagangan`
   (`510.41 Penerbitan SIUP`), `518 Koperasi` (`518.3 UMKM`, `518.31 Pembinaan
   Usaha Mikro`), `520 Pertanian` → `521 Tanaman Pangan` (`521.21 Sawah`,
   `521.43 Persawahan`), `580 Perbankan Moneter`.
2. Halaman dokumen JDIH Kab. Karawang (flipbook):
   https://jdih.karawangkab.go.id/document/peraturan-bupati/eyJpdiI6IktUT1Jvbm9sTUw3Z2E0aFkrVytDVXc9PSIsInZhbHVlIjoiWllXcXNtbWxBbS9LWUhKaWMyMFJxZz09IiwibWFjIjoiYTIxZTlhNjdkMjgwYTcyNzUxYjdlZWFkN2FkODZjMGQ5YTMwN2NmYTk3NzExYjQwYzc4NzBiN2JkOGIwZTRhYiIsInRhZyI6IiJ9?view=flipbook
3. Metadata resmi: https://peraturan.bpk.go.id/Details/289498/perbup-kab-karawang-no-74-tahun-2020
4. Salinan layanan: https://www.klaussa.com/peraturan/perbup-no-74-tahun-2020-kabupaten-karawang-nomor-74-tahun-2020-tentang-tata-naskah-dinas-pemerintah

---

## 3. Ketentuan Penomoran dalam Perbup 74/2020

**Pasal 8 huruf b (Pengelolaan surat keluar):**

> "surat keluar yang telah ditandatangani oleh pejabat yang berwenang
> **diberi nomor, tanggal dan stempel oleh sekretariat**"

**Lampiran — format nomor pada contoh naskah dinas (kutipan persis dari PDF):**

| Naskah | Kutipan format nomor |
|---|---|
| 8.a Surat Keterangan Umum | `NOMOR : 470/....../.............../......` |
| 8.b Surat Keterangan Kelahiran | `Nomor: 470/.../......../....` |
| 8.c Surat Keterangan Kematian | `No. 470/..../ ......... /20..` |
| 8.e Surat Keterangan Ahli Waris | `Nomor : 470/...../................./...........` |

**Batas kesimpulan yang didukung dokumen:**

1. Contoh surat keterangan umum, kelahiran, kematian, dan ahli waris pada
   lampiran memakai kode awal `470`. Angka itu **bukan** kode "semua surat
   desa": pada Perbup 28/2019, `470` adalah kelas *Kependudukan dan Catatan
   Sipil*, dan keempat contoh tersebut memang urusan kelas itu. Kelas lain
   punya kodenya sendiri, jadi memakai `470` untuk surat usaha, pertanian,
   kesejahteraan sosial, atau kepolisian membuat nomor surat tidak cocok
   dengan klasifikasi arsipnya sendiri.
2. Pasal 8 huruf b menyatakan sekretariat memberi nomor, tanggal, dan stempel
   setelah surat ditandatangani. Pasal ini tidak menetapkan bahwa nomor urut
   semua jenis surat keterangan harus memakai satu deret bersama.
3. Format aplikasi `470/{0:D3}/Ds/{2:yyyy}` menafsirkan ruang kosong pada
   contoh lampiran sebagai nomor urut, kode `Ds`, dan tahun. Kode `Ds`, urutan
   segmen yang persis, lebar tiga digit, dan deret bersama adalah pilihan
   implementasi yang konsisten dengan contoh, tetapi **tidak semuanya
   ditentukan eksplisit** dalam contoh tersebut. Operator tetap perlu memastikan
   format final dengan sekretariat/kecamatan dan ketentuan yang berlaku.
4. Perbup 74/2020 saja tidak cukup untuk menentukan kode tiap jenis surat;
   ia hanya menunjukkan contoh. Sejak 26 November 2026 bawaan aplikasi
   mengambil kelasnya dari **Perbup 28/2019** (lihat §2). Yang masih
   penafsiran ditandai "penafsiran" pada tabel §4. **Pengecualian yang
   dipakai aplikasi** saat ini:
   - Blanko NTCR **N1–N6** → `474.3/xxx/Ds/tahun` (satu deret bersama) —
     Mengikuti Lampiran Keputusan Dirjen Bimas Islam No. 473/2020; aplikasi
     menerbitkan blanko bersama pengantar desa.
   - Surat Keterangan Numpang Nikah **N8** → `474.2/xxx/Ds/tahun` (deret sendiri).
   - Permohonan Rekening Koran → `130/xxx/Ds/tahun` (surat keluar desa ke bank,
     klasifikasi keuangan/berkas — deret sendiri).
   - Surat dari Template Surat buatan pengguna → `TMPL/xxx/Ds/tahun`
     (penanda internal aplikasi; pengguna bebas mengubah awalannya per template).
5. Kertas Folio/F4 (215×330 mm) untuk surat-menyurat (Pasal 11) — sudah sesuai
   dengan tata letak PDF aplikasi.

---

## 4. Pemetaan format aplikasi dan batas dasar hukumnya

Tabel berikut adalah bawaan aplikasi. Kolom "Kelas Karawang" diambil dari
Perbup 28/2019 (kode klasifikasi arsip), kolom "Dasar" menyebut dari mana kelas
itu ditarik untuk jenis surat tersebut. Yang tidak didukung contoh lampiran
maupun kelas eksplisit ditandai **penafsiran** dan tetap perlu dikonfirmasi
sekretariat/kecamatan. Format lengkapnya (`xxx/{0:D3}/Ds/{yyyy}`) tetap tafsir
aplikasi atas ruang titik-titik pada contoh Perbup 74/2020 — lihat §3 butir 3.

| Jenis Surat | Kode Jenis | Kelas Karawang (Perbup 28/2019) | Dasar |
|---|---|---|---|
| Domisili Warga | DOM_WRG | `470` | Contoh lampiran 8.a Perbup 74/2020 (SK umum) — kelas 470 Kependudukan dan Catatan Sipil |
| Surat Kematian | KEM | `470` | Contoh lampiran 8.c Perbup 74/2020; rinciannya `472.12` Kematian |
| Surat Kenal Lahir | KENAL_LAHIR | `470` | Contoh lampiran 8.b Perbup 74/2020; rinciannya `472.11` Kelahiran |
| Keterangan Ahli Waris | AHLI_WARIS | `470` | Contoh lampiran 8.e Perbup 74/2020 |
| Ket. Belum Terbit / Sudah Terbit | SD | `470` | Pola SK umum 8.a; surat ini menerangkan arsip keterangan desa |
| Surat Beda Data | BEDANAMA | `471` | `471.11` Biodata Penduduk/Data Pribadi |
| Pengantar Adminduk | PDK | `471` | `471.13` KTP, `471.14` Kartu Keluarga |
| Surat Keterangan Pindah | SK_PINDAH | `471` | `471.21` Perpindahan Penduduk WNI |
| Ijin Tinggal Sementara | IJT | `471` | `471.23` Perpindahan Penduduk WNA dan WNI tinggal sementara |
| SKD Belum Menikah (KUA) | SKD | `472` | `472.2` Perkawinan, Perceraian dan Advokasi |
| Izin Suami/Orang Tua | IZIN | `472` | `472.2` Perkawinan (izin orang tua dipakai pada pencatatan nikah) |
| SKTM | SKTM | `465` | `465.3` Masyarakat Miskin: Raskin, BLT |
| SK Tidak Punya Rumah | SPR | `465` | `465` Kesejahteraan Sosial (keterangan untuk bantuan sosial) |
| Keterangan Penghasilan | PENGHASILAN | `465` | **penafsiran** — keterangan penghasilan tidak punya kode sendiri di lampiran |
| SKU (Surat Ket. Usaha) | SKU | `518` | `518.3` UMKM → `518.311` Pembinaan Usaha Mikro |
| Domisili Instansi/Lembaga | DOM_INS | `510` | `510.41` Penerbitan SIUP (domisili badan usaha/instansi) |
| Ket. Garapan Sawah | GRP_SAW | `521` | `521.21` Padi → `521.211` Sawah, dan `521.431` Persawahan (di bawah `520` Pertanian) |
| Pengantar SKCK | SKCK | `331` | `331` Kepolisian (di bawah `330` Keamanan) — SKCK diterbitkan Kepolisian, desa mengantar |
| Izin Keramaian | IKR | `330` | `330` Keamanan (ketertiban kegiatan/keramaian warga) |
| Permohonan Rekening Koran | REKKOR | `580` | `580` Perbankan Moneter. `130` di Karawang = Pemerintah Kabupaten, bukan keuangan |
| Blanko NTCR N1–N6 | N1T–N6T | `474.3` | Pengecualian: mengikuti blanko Kepdirjen Bimas Islam No. 473/2020, bukan kelas Karawang (`474` di Karawang = Perkembangan Penduduk) |
| NTCR N8 (Numpang Nikah) | N8T | `474.2` | Pengecualian: blanko Kepdirjen Bimas Islam No. 473/2020 |
| Surat dari Template | TMPL | `TMPL` | Penanda internal aplikasi; awalan bebas diubah per template |

**Contoh hasil penerbitan dengan bawaan baru:**

- Surat Keterangan Domisili: `470/015/Ds/2026` (tetap, satu deret dengan surat
  keterangan kependudukan lain)
- SKTM setelah domisili pada hari yang sama: `465/001/Ds/2026` — kelasnya sendiri,
  urutannya sendiri
- Izin Keramaian: `330/001/Ds/2026`; Pengantar SKCK: `331/001/Ds/2026`
- Blanko NTCR N1: `474.3/006/Ds/2026` (tetap)

---

## 5. Dampak Implementasi di SuDesApp

1. **`SuDesApp.Core/Configuration/JenisSuratConfig.json`** adalah sumber
   format (`NomorFormat`) dan flag pengelompokan (`IsSharedNumbering`,
   `IsKeteranganDesa`). Lima jenis kependudukan/capil memakai `470` (yang
   memang dicontohkan Perbup 74/2020); sisanya mengikuti kelas Perbup 28/2019
   (§4). Operator Karawang tetap perlu mengonfirmasi pemetaan yang ditandai
   penafsiran. Setiap entri
   juga memuat teks `AcuanNomor` — ringkasan acuan nyata (regulasi daerah atau
   blanko resmi) yang ditampilkan langsung pada kartu jenis surat di halaman
   Pengaturan Nomor Surat, diambil dari dokumen ini.
2. **Satu deret per kelas.** Cabang `IsSharedNumbering=true`
   (`JenisSuratRepository.GetSharedNumberingGroupAsync` → `GetNextDocumentNumberAsync`)
   menghitung nomor tertinggi dengan awalan yang sama di antara jenis-jenis dalam
   grup, jadi `470`, `471`, `472`, `465` masing-masing punya register sendiri.
   **Wajib `true` untuk jenis yang berbagi awalan**: cabang `false` menghitung per
   `KodeJenis`, sehingga dua jenis berawalan sama sama-sama mulai 001 dan nomornya
   bentrok — bug yang dulu menimpa blanko N1–N6. Dijaga
   `PenomoranKodeKarawangTests.Jenis_seawalan_ikut_deret_bersama_supaya_nomor_tidak_ganda`.
3. **Deret per-awalan** dipakai jenis yang berdiri sendiri (Template Surat) dan
   dihitung dari kode awalan (`PenomoranSuratService.AmbilAwalan`).
4. **Filter register** "Surat Keterangan Desa" (`GROUP_KETERANGAN_DESA`) tidak
   berubah: ia dikelompokkan oleh `IsKeteranganDesa`, bukan oleh kode awalan, jadi
   surat berkode `465`/`521`/`330` tetap masuk daftar yang sama.
5. **Kustomisasi pengguna tetap dihormati**: Pengaturan Nomor Surat menerima pola
   nomor lengkap, bukan hanya awalan. Setelah kode Kemendagri kabupaten/kota
   tersimpan pada Pengaturan Surat, pola lokal dipisahkan menurut kode daerah;
   sebelum kode itu tersedia, aplikasi memakai profil database lama. Setiap
   penyimpanan mencadangkan pola sebelumnya. Penanda `{0:D3}` adalah nomor urut;
   `{1}` mengisi kode jenis surat dan `{2:yyyy}` mengisi tahun.
6. **Format bawaan bukan aturan nasional.** Bukti Karawang mendukung awalan 470
   pada contoh yang tercantum di §3, bukan otomatis untuk semua jenis surat.
   Kode wilayah saja tidak cukup untuk menyatakan pola atau deret sudah sesuai;
   minta operator memeriksa aturan setempat dan dasar sektoral sebelum mengubah
   atau mengesahkan format.
7. **Template mencatat wilayah, rujukan, tanggal berlaku, dan versi format**.
   Contoh bawaan ditandai sebagai contoh sampai operator meninjau aturan lokal.
   Setiap perubahan definisi menyimpan snapshot versi sebelumnya; surat yang
   diterbitkan menyimpan salinan definisi yang dipakai saat itu.
8. **Nomor dan surat lama tidak diubah**: perubahan hanya berlaku untuk surat
   baru; isi surat yang sudah masuk register tetap memakai snapshot saat diterbitkan.

---

## 6. Verifikasi Ulang (bila regulasi berubah)

1. Cek versi terbaru di JDIH: https://jdih.karawangkab.go.id (cari "tata naskah
   dinas pemerintah desa" **dan** "klasifikasi arsip" — Perbup 28/2019 adalah
   sumber kode awal, Perbup 74/2020 sumber format naskah).
2. Bandingkan contoh `NOMOR : …` pada lampiran format (bagian 3 di atas) dan nama
   kelas pada lampiran Perbup 28/2019.
3. Perbarui `NomorFormat`/flag di `JenisSuratConfig.json` — cukup satu berkas,
   lalu jalankan `dotnet test --filter PenomoranKodeKarawangTests`.

---

## 7. Batas cakupan untuk template tambahan

Perbup 74/2020 mengatur tata naskah dinas Pemerintah Desa **Kabupaten
Karawang**. Lampiran angka 8.a menyediakan format *Surat Keterangan Umum*
dengan nomor `470/....../.............../......`; lampiran angka 10 menyediakan
format *Surat Izin Kepala Desa* dengan nomor yang masih berupa titik-titik.
Perbup ini tidak menetapkan secara khusus persyaratan penerbitan surat
keterangan penghasilan, domisili usaha, atau izin keramaian. Karena itu:

- Format nomor `471`, `472`, atau `476` pada contoh template tambahan bukan
  kode yang dapat dibuktikan dari contoh lampiran tersebut. Jangan menganggapnya
  sebagai klasifikasi resmi hanya karena dipakai di template.
- Surat izin pada lampiran adalah *Surat Izin Kepala Desa* dengan dasar dan
  tujuan yang diisi pada surat. Itu tidak membuktikan bahwa desa berwenang
  menerbitkan izin keramaian atau menggantikan persetujuan Kepolisian maupun
  instansi teknis.
- Gunakan format umum `470` hanya ketika surat memang diterbitkan sebagai
  Surat Keterangan Umum menurut pola Karawang. Untuk surat khusus, kewenangan,
  syarat, dan kode/deret nomornya harus mengikuti aturan sektoral serta
  kebijakan pencatatan kabupaten yang berlaku. Template aplikasi tidak
  menetapkan sendiri syarat hukum itu.

Status dan sumber diverifikasi 20 Oktober 2026 melalui metadata JDIH BPK
(menyatakan Perbup 74/2020 berstatus berlaku) dan lampiran Perbup yang disalin
di `docs/regulasi/perbup74-lampiran.txt` / PDF resmi yang ditautkan pada bagian 2.
Aturan Karawang tidak dapat diterapkan otomatis ke desa di kabupaten lain;
masing-masing daerah perlu sumber JDIH kabupaten/provinsinya.

## 8. Pembaruan rujukan nasional

- [Permendagri Nomor 1 Tahun 2023 — Tata Naskah Dinas di Lingkungan Pemerintah Daerah](https://peraturan.bpk.go.id/Search?keywords=Permendagri%20Nomor%201%20Tahun%202023%20Tata%20Naskah%20Dinas%20di%20Lingkungan%20Pemerintah%20Daerah)
  berstatus berlaku pada JDIH BPK dan secara eksplisit mencabut Permendagri
  Nomor 54 Tahun 2009. Lingkup judulnya Pemerintah Daerah, jadi penerapannya
  pada tata naskah Pemerintah Desa harus dibaca bersama aturan desa yang khusus.
- [Metadata Perbup Karawang Nomor 74 Tahun 2020 di JDIH BPK](https://peraturan.bpk.go.id/Details/289498/perbup-kab-karawang-no-74-tahun-2020)
  mencatat status berlaku. JDIH Kabupaten Karawang menyediakan salinan batang
  tubuh dan lampiran yang digunakan untuk acuan lokal pada dokumen ini.
- Tidak ada satu Perbup atau Perda tata naskah yang berlaku untuk semua desa
  Indonesia. Untuk kabupaten/kota lain, rujukan yang benar adalah Perbup/Perwali
  dan perubahan/pencabutannya pada JDIH daerah tersebut. Hingga katalog sumber
  per daerah diverifikasi, status antarmuka untuk daerah lain harus tetap
  **belum diverifikasi**, bukan mengadopsi aturan Karawang.

## 9. Kabupaten Bekasi

JDIH Kabupaten Bekasi mencatat **Peraturan Bupati Bekasi Nomor 215 Tahun
2022 tentang Tata Naskah Dinas Pemerintah Desa** dengan status **Berlaku**.
Ruang lingkupnya secara eksplisit Pemerintah Desa, sehingga dapat menjadi
rujukan dokumen untuk Kabupaten Bekasi; jangan tertukar dengan Perbup 37/2018
dan Perbup 233/2022 tentang tata naskah di lingkungan Pemerintah Kabupaten.

- Detail dan status pada JDIH Kabupaten Bekasi:
  https://jdih.bekasikab.go.id/page/info/produk/9879
- PDF resmi yang dibundel aplikasi:
  `docs/regulasi/Perbup-215-2022-Bekasi-Tata-Naskah-Dinas-Desa.pdf`
- Tautan unduh JDIH:
  https://jdih.bekasikab.go.id/media/jdih/law-product-1-jdih/9879

PDF JDIH berupa pindaian. Setelah pemeriksaan visual lampiran bagian A angka 6–8
(halaman PDF 30–37), awalan 470 terverifikasi pada contoh surat keterangan umum,
kelahiran, kematian, dan ahli waris. Lampiran tidak menetapkan bahwa semua jenis
surat memakai 470, dan tidak menetapkan kode Ds, nomor urut tiga digit, atau satu
deret bersama. Rincian kutipan dan batas kesimpulan ada di
docs/referensi-penomoran-surat-bekasi.md. Pengaturan Nomor Surat tetap memberi
peringatan agar operator memeriksa unsur format dan ketentuan sektoral sebelum
mengesahkan pola lengkap.

Penelusuran JDIH Kabupaten Bandung, Bogor, Garut, dan Subang pada 21 Oktober
2026 belum memastikan adanya Perbup/Perwali yang secara eksplisit mengatur tata
naskah Pemerintah Desa beserta pola penomoran yang berlaku. Hasil tentang tata
naskah dinas Pemerintah Kabupaten tidak dipakai sebagai pengganti. Untuk daerah
tersebut, dokumentasi aplikasi harus tetap menyatakan file regulasi desa belum
tersedia sampai sumber dan cakupannya terverifikasi.

## 10. Android: pola nomor diterbitkan desktop

Android tidak punya katalog penomoran sendiri. Saat sinkron, desktop menerbitkan
`pengaturan_desa.nomor` berisi `format` (peta `NamaJenis` → `NomorFormat` dari
`JenisSuratConfig.json`, termasuk penyesuaian yang diubah operator di halaman
Pengaturan Nomor Surat), `urutTerakhir`, dan `tahun`. `PengaturanSinkron.kt`
menyimpan peta itu sebagai `PengaturanSurat.formatNomor`, lalu
`nomorBerikutnya(idJenis, ...)` mengambil pola untuk `idJenis` skema formulir —
kosakata kuncinya sama dengan `NamaJenis` katalog, jadi `SKD_UMUM` di HP adalah
`SKD_UMUM` di desktop.

Artinya kelas klasifikasi Karawang di §4 berlaku di kedua platform tanpa menyalin
angka. Kalau sebuah jenis surat tidak ikut diterbitkan (misal jenis baru ditambahkan
di skema tapi lupa didaftarkan di katalog), Android **jatuh ke awalan global**
`awalanNomor` bawaan `470` — nomor tetap terbentuk, hanya bentuknya tidak sama
dengan register desktop. Penjaganya `SuDesApp.Tests/Sinkron/ParitasNomorAndroidDesktopTests.cs`:
setiap kunci `SkemaFormulir.json` wajib punya pola di katalog, kunci katalog dan
skema harus satu himpunan, dan kontrak nama `nomor.format` di kedua sisi tidak boleh
berubah tanpa disadari.

Nomor urut lokal Android yang lebih tinggi selalu menang atas `urutTerakhir` hasil
sinkron: menarik ulang pengaturan tidak boleh membuat nomor surat mundur. Deret
resmi tetap milik register desktop; angka di HP dipakai untuk draf/penomoran awal
sebelum surat disahkan di desktop.
