# Policy Tutup Buku & Retensi Arsip Tahunan — SuDesApp

Dokumen ini menjelaskan perilaku **tutup buku tahunan**, **arsip register**,
dan **retensi** pada SuDesApp. Tujuannya agar operator punya aturan main yang
jelas sebelum dipakai di lapangan, dan agar keputusan untuk mengubah policy
nanti selalu punya dasar yang jelas.

> Status dokumen: **berlaku sebagai default implementasi** (sudah sesuai kode
> saat ini). Bagian yang masih menunggu keputusan produk ditandai
> **[TENTUKAN]** dan sengaja **belum** diimplementasikan.

---

## 1. Ringkasan Keputusan

| Topik | Kebijakan | Status |
|---|---|---|
| Tutup buku | Menandai tahun sebagai *selesai* (status jadi `TERTUTUP`) | Aktif |
| Penghapusan data saat tutup | **Tidak ada** — tutup buku tidak menghapus apa pun | Aktif |
| Pembatalan tutup | Bisa dibuka kembali kapan saja, tidak menghapus data | Aktif |
| Nomor hilang | Peringatan, **tidak** menghalangi penutupan | Aktif |
| Nomor ganda / tidak terbaca / tahun beda | Blocking; perlu diperbaiki atau ditutup dengan paksa | Aktif |
| Surat baru setelah tahun ditutup | Tetap boleh terbit (tutup buku bersifat advisory) | Aktif |
| Auto-hapus arsip lama | **Tidak ada** | Aktif (kebijakan aman) |
| Retensi berkala | Tidak ditetapkan | **[TENTUKAN]** |
| Hard lock penerbitan | Tidak ada | **[TENTUKAN]** |

---

## 2. Apa Itu Tutup BukuTahunan

Tutup buku tahunan adalah **tanda administratif** bahwa penomoran surat
sudah final untuk satu tahun. Status setiap tahun disimpan di tabel
`TutupBukuTahun` dengan dua nilai:

- `TERBUKA` — tahun masih berjalan; operator boleh menerbitkan surat.
- `TERTUTUP` — tahun dianggap selesai; hanya arsip dan verifikasi yang bisa dibuka.

Penomoran sudah ter-scope per tahun sejak awal: penerbitan selalu mengambil
nomor yang berakhiran `/Ds/<tahun>`, sehingga deret tahun baru mulai dari
`001` tanpa perlu proses reset manual. Tutup buku **tidak mengubah**
mekanisme itu.

### 2.1 Yang Terjadi Saat Buku Ditutup

1. Penomoran tahun tersebut diverifikasi lebih dulu (lihat §3).
2. Jumlah surat dan rekap deret disimpan sebagai **snapshot** di kolom
   `Snapshot` (bentuk JSON) sebagai bukti kondisi saat ditutup.
3. Status menjadi `TERTUTUP`, beserta `TanggalTutup`, `DitutupOleh`,
   `Dipaksa`, dan `Catatan`.
4. **Tidak ada** baris surat yang berubah, dipindah, atau dihapus.

### 2.2 Yang Terjadi Saat Buku Dibuka Kembali

1. Status kembali menjadi `TERBUKA`.
2. Snapshot dan metadata penutupan dibersihkan agar tidak menyesatkan.
3. Jumlah surat dan deret tetap disimpan sebagai rekap.
4. **Tidak ada** data surat yang dihapus. Penomoran tahun tersebut kembali
   bisa menerbitkan nomor seperti biasa.

Karena itu, tutup buku selalu **reversible** dan **non-destruktif**.

---

## 3. Verifikasi Penomoran

Setiap deret nomor dihitung per tahun, dengan dua lapis pemeriksaan, peringatan dan blocking:

| Temuan | Level | Keterangan |
|---|---|---|
| Nomor hilang di tengah deret | Peringatan | Nomor yang dilewati saat pencetakannya; tidak menghalangi |
| Deret tidak mulai dari `001` | Peringatan | Penomoran dimulai dari angka lain |
| Nomor sama muncul lebih dari sekali | **Blocking** | Indikasi salah input atau duplikasi |
| Nomor kosong / tidak bisa dibaca | **Blocking** | Format nomor tidak sesuai pola |
| Tahun pada nomor ≠ tahun tanggal surat | **Blocking** | Nomor berasal dari tahun lain |

Nomor dengan tahun yang tidak cocok dicatat blocking tetapi **tidak dihitung**
dalam statistik deret tahun tersebut, agar deret yang diaudit tetap bersih.

### 3.1 Menutup dengan Paksa

Jika masih ada temuan blocking, operator dapat menutup buku dengan paksa.
Ketentuan:

- Harus mencentang **tutup dengan paksa**.
- **Wajib** menyertakan catatan alasan supaya auditor bisa menelusuri kenapa
  buku ditutup dalam kondisi bermasalah. Kewajiban ini divalidasi di antarmuka;
  service hanya menerima dan menyimpan catatannya.
- Penutupan paksa dicatat pada snapshot sebagai `Dipaksa = true`.

Penutupan tanpa paksa ditolak bila masih ada temuan blocking. Tutup buku
dengan paksa **tetap tidak menghapus atau memperbaiki** data; ia hanya
mencatat bahwa operator sudah meninjau kondisi penomorannya.

---

## 4. Arsip Register Tahunan

Arsip dibuat per tahun dalam dua format, keduanya memuat status tutup buku:

- **Excel** — tiga sheet: `Ringkasan` (status, jumlah surat, jumlah deret),
  `Register` (daftar surat), `Deret Nomor` (rekap per kode).
- **PDF** — layout landscape dengan header status buku, tabel register, dan
  halaman catatan penomoran bila ada catatan.

Nama berkas mengikuti pola baku `Register Surat Desa <tahun>.<ekstensi>`,
misalnya `Register Surat Desa 2025.xlsx` dan `Register Surat Desa 2025.pdf`.

Arsip bersifat **tambahan** (read-only). Membuat arsip tidak mengubah data
apa pun dan aman diulang kapan saja.

---

## 5. Retensi

### 5.1 Kebijakan Default (Aktif)

- **Tidak ada auto-hapus.** Arsip dan data surat tidak pernah dihapus oleh
  sistem secara otomatis, berapa pun umurnya.
- **Tidak ada auto-retensi.** Tidak ada proses yang memindahkan atau
  mengarsipkan data ke media lain secara otomatis.
- Penyimpanan sepenuhnya lokal di SQLite.
- Backup dan pemindahan storage adalah tanggung jawab operator/pengelola,
  dan belum diatur oleh aplikasi.

### 5.2 Mengapa Tidak Ada Auto-Hapus

Ketentuan yang berlaku (Perbup 74/2020) mengatur format dan tata naskah
dinas, bukan masa simpan berkas. Karena tidak ada ketentuan retensi yang
jelas, pilihan aman adalah **menyimpan semua** dan menyerahkan keputusan
penghapusan ke manusia. Menghapus otomatis berisiko menghilangkan bukti yang
diwajibkan untuk pemeriksaan.

### 5.3 Alasan Tutup Buku Belum Jadi Hard Lock

Tutup buku pada versi ini bersifat **advisory** (penegasian administratif).
Surat tetap bisa diterbitkan pada tahun yang sudah ditutup, karena:

- Belum ada aturan tegas yang melarang penerbitan setelah tutup buku.
- Hard lock bisa menghambat operasional desa yang masih butuh mencetak
  surat pada tahun berjalan yang belum sempat ditutup.
- Tidak ada mekanisme pemulihan yang aman bila hard lock salah aktif.

Dengan model advisory, kalau memang diperlukan hard lock di masa depan,
penanganannya cukup di satu tempat: pengecekan status pada alur
penerbitan surat, dengan pesan yang jelas dan opsi force yang tercatat.

---

## 6. Hal yang Belum Diputuskan — [TENTUKAN]

Hal berikut **tidak** diimplementasikan dan membutuhkan keputusan produk
sebelum diubah:

1. **Masa retensi berkas.** Berapa lama arsip wajib disimpan sebelum boleh
   dihapus, dan siapa yang berwenang menghapus.
2. **Auto-retensi.** Apakah sistem boleh otomatis memindahkan arsip lama ke
   media penyimpanan lain.
3. **Hard lock.** Apakah tahun yang sudah `TERTUTUP` harus benar-benar
   melarang penerbitan surat baru, atau cukup bersifat advisory seperti
   sekarang.
4. **Peran dan hak akses.** Siapa yang boleh menutup, membuka, atau menutup
   dengan paksa buku tahunan.

Sampai keputusan diambil, default di §1–§5 yang berlaku tetap yang aman:
**non-destruktif, reversible, advisory, tanpa auto-hapus**.

---

## 7. Rujukan

- `docs/referensi-penomoran-surat.md` — acuan format penomoran.
- `docs/regulasi/Perbup-74-2020-Lampiran-Tata-Naskah-Dinas-Desa.pdf` —
  peraturan beserta lampiran format.
- `SuDesApp.Core/Services/VerifikasiPenomoranService.cs` — aturan verifikasi.
- `SuDesApp.Core/Services/TutupBukuTahunService.cs` — aturan tutup/buka.
- `SuDesApp.Core/Services/ArsipRegisterTahunanService.cs` — pembuatan arsip.
