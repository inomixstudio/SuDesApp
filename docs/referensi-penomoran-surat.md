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
| Dasar hukum utama | Permendagri 54/2009 (Tata Naskah Dinas Pemda), Permendagri 47/2016 (Administrasi Pemerintahan Desa), Perda Karawang 4/2019 tentang Desa |

> **Catatan**: Perbup 377/2023 tentang Tata Naskah Dinas hanya berlaku untuk
> lingkungan **Pemerintah Kabupaten** (mencabut Perbup 23/2017), bukan untuk
> Pemerintah Desa. Untuk desa, rujukannya tetap **Perbup 74/2020**.

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

**Yang dapat disimpulkan dari ketentuan + lampiran:**

1. Pola nomor desa: `KLASIFIKASI / NOMOR URUT / KODE JENIS-DESA / TAHUN`.
   Aplikasi menerapkan segmen ketiga sebagai `Ds` dan keempat sebagai tahun —
   konsisten dengan pola resmi di atas.
2. **Semua surat keterangan desa memakai satu kode klasifikasi: 470.**
   Perbup tidak memperkenalkan kode 474/474.2/474.3/474.4/510/570/593.2 untuk
   naskah desa. Kode 474.x yang pernah dipakai aplikasi berasal dari klasifikasi
   kependudukan/pernikahan lama dan **tidak** ada dalam regulasi baku naskah
   dinas desa Karawang.
3. Karena satu kode, nomor urut berjalan **satu deret bersama** untuk seluruh
   surat keterangan desa (tanggal penerbitan menentukan urutannya).
4. **Pengecualian yang tetap dipertahankan aplikasi** (dasarnya di luar Perbup):
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

## 4. Pemetaan Format: Lama → Baru

| Jenis Surat | Kode Jenis | Format Lama | Format Baru (Perbup 74/2020) |
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

1. **`SuDesApp.Core/Configuration/JenisSuratConfig.json`** — satu-satunya sumber
   format (`NomorFormat`) + flag pengelompokan (`IsSharedNumbering`,
   `IsKeteranganDesa`). Sudah diperbarui: 13 jenis surat keterangan desa kini
   `470/…` dengan `IsSharedNumbering: true` dan `IsKeteranganDesa: true`.
2. **Deret bersama dihitung otomatis** dari flag `IsSharedNumbering`
   (`JenisSuratRepository.GetSharedNumberingGroupAsync` → `GetNextDocumentNumberAsync`),
   tanpa melihat awalan — jadi perpindahan kode tidak memutus urutan deret baru.
3. **Deret per-awalan** untuk jenis non-bersama (NTCR, Rekening Koran, Template)
   dihitung per kode awalan (`PenomoranSuratService.AmbilAwalan`).
4. **Filter register** "Surat Keterangan Desa" (`GROUP_KETERANGAN_DESA`) kini
   mengikutkan 13 jenis tersebut secara konsisten.
5. **Kustomisasi pengguna tetap dihormati**: nilai pada Pengaturan Aplikasi
   (disimpan `PenomoranOverrideStore`, mis. `{"SKD_UMUM": "471/{0:D3}/Ds/{2:yyyy}"}`)
   selalu menimpa nilai bawaan berkas JSON — desa yang kebijakan kecamatannya
   berbeda dapat menyesuaikan sendiri tanpa mengubah kode.
6. **Nomor lama tidak diubah**: perubahan hanya berlaku untuk surat baru; nomor
   surat yang sudah terbit di register tetap apa adanya (unik dan terkunci saat edit).

---

## 6. Verifikasi Ulang (bila regulasi berubah)

1. Cek versi terbaru di JDIH: https://jdih.karawangkab.go.id (cari "tata naskah
   dinas pemerintah desa").
2. Bandingkan contoh `NOMOR : …` pada lampiran format (bagian 3 di atas).
3. Perbarui `NomorFormat`/flag di `JenisSuratConfig.json` — cukup satu berkas.
4. Jalankan `dotnet test tests/SuDesApp.Core.Tests` (test penomoran otomatis
   akan mengunci format `470/…` melalui fixture NTCR/regex terkait).
