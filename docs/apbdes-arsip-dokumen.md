# APBDES: arsip dokumen anggaran desa

Halaman **APBDES** bukan aplikasi akuntansi dan tidak menghitung keuangan desa.
Angka resmi APBDes dibuat dan ditetapkan di **Siskeudes**; yang dikerjakan
aplikasi ini adalah menyimpan dokumennya, menampilkan isinya, menyunting apa
yang perlu, lalu menyimpannya kembali.

## Tahapan dokumen

Satu tahun anggaran bisa memuat beberapa dokumen, masing-masing dengan tahapannya:

| Tahapan di aplikasi | Dokumen di desa |
|---|---|
| Rancangan APBDes | Rancangan Perdes tentang APB Desa + lampirannya |
| APBDes Awal | Perdes tentang APB Desa dan Perkades Penjabaran APB Desa |
| APBDes Perubahan (ke-1, ke-2, …) | Perdes tentang Perubahan APB Desa + Penjabaran Perubahan |
| APBDes Pertanggungjawaban (LPJ) | Perdes tentang Laporan Pertanggungjawaban Realisasi APB Desa + lampirannya |
| Dokumen Pelaksanaan (DPA/DPPA/DPAL) | DPA/DPPA/DPAL, RAK, RAB |
| Laporan Realisasi / Semester | Laporan pelaksanaan APB Desa dan laporan realisasi kegiatan |
| Lainnya | Dokumen lain yang perlu diarsipkan bersama tahun anggaran ini |

Catatan aturan: istilah resmi pada Permendagri 20/2018 adalah **Perubahan APB
Desa**; perubahan pada keadaan normal **satu kali dalam satu tahun anggaran**,
dengan pengecualian keadaan luar biasa. Karena itu nomor urut "Perubahan ke-2,
ke-3, …" di aplikasi hanya **penanda berkas**, bukan nama tahapan resmi. Dokumen
Perdes perubahan sebaiknya dipisahkan dari Perkades penjabaran perubahannya.

## Tahun anggaran

Daftar tahun di bagian atas halaman selalu memuat **tahun berjalan menurut jam
komputer**, ditambah tahun sebelum dan sesudahnya (untuk menyiapkan anggaran
tahun berikutnya) serta seluruh tahun yang sudah punya dokumen. Tahun berjalan
diberi tanda "tahun berjalan" dan diletakkan paling atas, sehingga tidak perlu
ditebak dari daftar angka.

Daftar itu disusun ulang setiap kali arsip dibaca — saat halaman dibuka, saat
tombol **Muat Ulang** ditekan, dan setelah impor yang mengubah tahun dokumen.
Tahun yang sedang dibuka tidak pernah hilang dari daftar. Aplikasi yang
dibiarkan terbuka melewati pergantian tahun karena itu tetap mendapat tahun
baru tanpa perlu ditutup lebih dulu.

## Berkas sumber

- **Impor dari Word** (`.docx`, `.doc` lama lewat konversi Microsoft Word):
  berkas dibaca, teksnya ditampilkan di halaman, dan baris
  `kode rekening + uraian + anggaran (+ realisasi)` dikenali menjadi tabel
  rincian yang bisa langsung disunting.
- **Lampirkan Berkas** (PDF/Excel/apa pun hasil cetak Siskeudes): berkas
  disalin apa adanya tanpa dibaca isinya.

Kedua jalur menyalin berkas ke `%LocalAppData%\SuDesApp\Apbdes\`, sehingga arsip
tetap utuh walaupun berkas aslinya dipindah atau dihapus. Daftar dokumen dan
rinciannya disimpan di `%LocalAppData%\SuDesApp\apbdes.json`.

### Melihat berkas lampiran

- **Pratinjau**: lampiran **PDF** dibuka di halaman pratinjau PDF aplikasi
  (mesin PDFium yang sama dengan IDM, Agenda Surat, dan Daftar Hadir), lengkap
  dengan tombol Cetak dan Ekspor. Berkasnya dibaca dari folder arsip, bukan dari
  folder PDF sementara, jadi tidak ikut terhapus pembersih dokumen temp.
- **Buka berkas**: membuka lampiran dengan aplikasi luar. Ini jalur untuk
  **Word** dan **Excel** (juga PDF bila diinginkan), karena aplikasi tidak
  memiliki mesin render untuk kedua format itu — karena itu tombol Pratinjau
  nonaktif bila lampirannya bukan PDF, dan tooltip-nya mengarahkan ke Buka berkas.

## Angka pada tabel

Kolom rincian disalin apa adanya dari dokumen: kode rekening, uraian, anggaran,
realisasi, dan keterangan. Kolom **Lebih/(Kurang)** dan **%** dihitung dari
baris yang tampil, mengikuti bentuk laporan realisasi resmi. Angka rupiah
diterima dalam gaya Indonesia (`1.234.567`, `1.234.567,00`) maupun gaya lain
(`1,234,567.00`).

Data dari versi lama aplikasi (daftar pos datar) tetap dibaca: dipindahkan
menjadi satu dokumen **APBDes Awal** per tahun dengan kode rekening sederhana
(4 Pendapatan, 5 Belanja, 6 Pembiayaan) dan catatan bahwa itu bukan salinan
berkas Siskeudes.

## Batas yang disengaja

- Tidak menghitung ulang atau menetapkan anggaran; penetapan tetap di Siskeudes
  dan Perdes.
- Tidak membuat berkas Word dari tabel rincian (belum ada penulis `.docx`).
- Pembacaan Word bergantung pada bentuk berkas: tabel Word terbaca paling akurat;
  berkas yang tabelnya sudah menjadi teks masih dibaca dari jaraknya, dan
  hasilnya diberi peringatan untuk diperiksa.
