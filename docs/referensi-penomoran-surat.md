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
   lampiran memakai kode awal `470`. Ini mendukung penggunaan `470` pada
   contoh-contoh tersebut; lampiran tidak menetapkan satu format lengkap yang
   seragam untuk semua jenis surat pada tabel berikut.
2. Pasal 8 huruf b menyatakan sekretariat memberi nomor, tanggal, dan stempel
   setelah surat ditandatangani. Pasal ini tidak menetapkan bahwa nomor urut
   semua jenis surat keterangan harus memakai satu deret bersama.
3. Format aplikasi `470/{0:D3}/Ds/{2:yyyy}` menafsirkan ruang kosong pada
   contoh lampiran sebagai nomor urut, kode `Ds`, dan tahun. Kode `Ds`, urutan
   segmen yang persis, lebar tiga digit, dan deret bersama adalah pilihan
   implementasi yang konsisten dengan contoh, tetapi **tidak semuanya
   ditentukan eksplisit** dalam contoh tersebut. Operator tetap perlu memastikan
   format final dengan sekretariat/kecamatan dan ketentuan yang berlaku.
4. Perbup 74/2020 sendiri tidak cukup untuk menyatakan kode lain (misalnya
   `474.x`, `510`, `570`, atau `593.2`) dilarang atau berlaku. Kode-kode
   khusus memerlukan dasar sektoral/daerah tersendiri. **Pengecualian yang
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

Tabel berikut mencatat perubahan konfigurasi aplikasi, bukan daftar format yang
seluruhnya diperintahkan Perbup. Bukti langsung dalam lampiran mendukung kode
470 untuk contoh surat keterangan yang disebut pada §3. Untuk jenis lain,
penggunaan 470 dan penggabungan urutannya di bawah ini adalah kebijakan bawaan
aplikasi yang perlu ditinjau operator; jangan menyebutnya kewajiban eksplisit
Perbup 74/2020.

| Jenis Surat | Kode Jenis | Format Lama | Format bawaan aplikasi (perlu peninjauan) |
|---|---|---|---|
| SKD Umum | SKD | 470 (deret bersama) | 470 (deret bersama) — *tetap* |
| Domisili Warga | DOM_WRG | 470 (deret bersama) | 470 — *tetap* |
| Domisili Instansi | DOM_INS | 470 (deret bersama) | 470 — *tetap* |
| SKU (Surat Ket. Usaha) | SKU | **510** (deret sendiri) | **470 (deret bersama)** |
| Pengantar SKCK | SKCK | **474** (deret bersama) | **470 (deret bersama)** |
| Izin Suami/Orang Tua | IZIN | **474.4** (deret sendiri) | **470 (deret bersama)** |
| Ket. Garapan Sawah | GRP_SAW | **593.2** (deret sendiri) | **470 (deret bersama)** |
| Surat Kematian | KEM | **570** (deret sendiri) | **470 (deret bersama)** |
| SKTM | SKTM | 470 (deret bersama) | 470 — *tetap* |
| Surat Beda Data | BEDANAMA | 470 (deret bersama) | 470 — *tetap* |
| Surat Kenal Lahir | KENAL_LAHIR | **474.2** (deret bersama) | **470 (deret bersama)** |
| Ket. Ahli Waris | AHLI_WARIS | **474.2** (deret bersama) | **470 (deret bersama)** |
| Ijin Tinggal Sementara | IJT | 470 (deret bersama) | 470 — *tetap* |
| Permohonan Rekening Koran | REKKOR | 130 (deret sendiri) | 130 — *tetap* (di luar skop surat keterangan) |
| Surat dari Template | TMPL | TMPL (deret sendiri) | TMPL — *tetap* (penanda aplikasi) |
| NTCR N1–N6 | N1T–N6T | 474.3 (deret bersama) | 474.3 — *tetap* (blanko Kepdirjen 473/2020) |
| NTCR N8 (Numpang Nikah) | N8T | 474.2 (deret sendiri) | 474.2 — *tetap* |

**Contoh hasil penerbitan setelah perubahan:**

- Surat Keterangan Domisili: `470/015/Ds/2026`
- Surat Kematian (setelah SKD 014 pada hari yang sama): `470/015/Ds/2026`
  — satu deret, tidak lagi `570/003/Ds/2026`
- Blanko NTCR N1: `474.3/006/Ds/2026` (tetap)

---

## 5. Dampak Implementasi di SuDesApp

1. **`SuDesApp.Core/Configuration/JenisSuratConfig.json`** adalah sumber
   format (`NomorFormat`) dan flag pengelompokan (`IsSharedNumbering`,
   `IsKeteranganDesa`). Tiga belas jenis saat ini memakai pola 470 dan deret
   bersama. Contoh lampiran Perbup mendukung 470 untuk beberapa surat keterangan,
   tetapi tidak membuktikan semua tiga belas jenis harus memakai kode dan deret
   yang sama; operator Karawang perlu mengonfirmasi cakupan ini.
2. **Deret bersama adalah kebijakan aplikasi**, dihitung otomatis dari flag `IsSharedNumbering`
   (`JenisSuratRepository.GetSharedNumberingGroupAsync` → `GetNextDocumentNumberAsync`),
   tanpa melihat awalan — jadi perpindahan kode tidak memutus urutan deret baru.
3. **Deret per-awalan** untuk jenis non-bersama (NTCR, Rekening Koran, Template)
   dihitung per kode awalan (`PenomoranSuratService.AmbilAwalan`).
4. **Filter register** "Surat Keterangan Desa" (`GROUP_KETERANGAN_DESA`) kini
   mengikutkan 13 jenis tersebut secara konsisten.
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
   dinas pemerintah desa").
2. Bandingkan contoh `NOMOR : …` pada lampiran format (bagian 3 di atas).
3. Perbarui `NomorFormat`/flag di `JenisSuratConfig.json` — cukup satu berkas.

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
