# Sumber formulir per kabupaten

Daftar halaman unduhan formulir administrasi kependudukan **tidak ditanam di
kode**. Aplikasi membacanya dari berkas JSON:

1. **Berkas aktif (yang dipakai aplikasi):**
   ```
   %LOCALAPPDATA%\SuDesApp\sumber-formulir-daerah.json
   ```
2. **Berkas bawaan yang ikut terpasang bersama aplikasi:**
   ```
   <folder aplikasi>\Bawaan\sumber-formulir-daerah.json
   ```

Berkas aktif dibuat otomatis dari berkas bawaan saat aplikasi pertama kali
dibuka. Karena letaknya di profil pengguna (bukan folder instalasi), suntingan
operator **tidak tertimpa** saat aplikasi diperbarui. Berkas bawaan dipakai
sebagai cadangan bila berkas aktif belum ada atau rusak.

Berkas memiliki `Versi`. Bila berkas aktif masih memakai versi lama, daerah baru
dari berkas bawaan yang belum ada akan disisipkan dan versi berkas dinaikkan.
Entri yang sudah ada — termasuk URL yang sudah diperbaiki operator — tidak
diubah.

Untuk memperbarui daftar tanpa merilis ulang aplikasi, sunting berkas aktif
lalu buka **Pengaturan Formulir** dan tekan **Segarkan**. Perubahan langsung
dipakai — tidak perlu merilis ulang aplikasi. Berkas bawaan juga boleh diganti
lewat tambalan pembaruan; isi berkas aktif yang sudah ada tidak ditimpa, kecuali
penyisipan entri baru dari bawaan versi lebih tinggi seperti dijelaskan di atas.

## Format

```json
{
  "Versi": 2,
  "Keterangan": "…",
  "Sumber": [
    {
      "Nama": "Kabupaten Karawang",
      "KodeKabupaten": "32.15",
      "Url": "https://infosadaradmindukkarawang.id",
      "UnduhanOtomatis": true,
      "Catatan": "INSAN Karawang — formulir F-1.01 dan blanko surat desa."
    }
  ]
}
```

| Field | Wajib | Keterangan |
|---|---|---|
| `Versi` | tidak | Versi skema berkas. Naikkan saat menambah daerah bawaan baru agar berkas aktif lama ikut diperbarui. |
| `Nama` | ya | Nama lengkap daerah, mis. `Kabupaten Bandung` / `Kota Sukabumi`. |
| `KodeKabupaten` | ya | Kode Kemendagri (`32.15`). Dipakai lebih dulu saat mencocokkan pilihan Pengaturan Surat. |
| `Url` | ya | Halaman resmi unduhan formulir; harus `http://` atau `https://`. |
| `UnduhanOtomatis` | tidak | Hanya berlaku untuk host `infosadaradmindukkarawang.id`. Daerah lain dianggap unduh manual. |
| `Catatan` | tidak | Keterangan singkat isi halaman sumber. |

Nama field tidak peka huruf besar/kecil; `nama`, `kodeKabupaten`, dan seterusnya
juga diterima.

## Perilaku

- **Kabupaten cocok** dan `UnduhanOtomatis` benar → tombol *Unduh dari Sumber*
  mengambil formulir langsung dari INSAN Karawang.
- **Kabupaten cocok, unduh manual** → tombol *Buka Situs Formulir* membuka
  halaman resmi; berkas PDF-nya ditambahkan lewat tombol *Tambah*.
- **Kabupaten tidak ada di berkas** → halaman menjelaskan bahwa formulir harus
  ditambahkan manual memakai blanko Dukcapil setempat.
- **Kabupaten belum dipilih** pada Pengaturan Surat → tombol *Atur di Pengaturan
  Surat* ditampilkan.

## Kesalahan yang aman

- Berkas aktif hilang → dibuat ulang dari berkas bawaan di folder aplikasi.
- Berkas bawaan hilang/rusak → daftar darurat berisi Kabupaten Karawang dipakai
  agar unduhan otomatis tetap tersedia; alasannya dicatat di log aplikasi.
- JSON rusak, `Sumber` kosong, atau semua entri tidak sah → daftar bawaan dipakai
  dan alasannya dicatat di log aplikasi; halaman tetap bisa dipakai.
- Entri tanpa nama/kode/URL, URL bukan http(s), atau kode ganda → entri itu
  dilewati, entri lain tetap dipakai.
- `UnduhanOtomatis: true` pada host selain INSAN Karawang → diabaikan (aplikasi
  belum punya pembaca halaman untuk host tersebut).

## Menambah daerah baru

Tambahkan satu objek ke dalam `Sumber`, isi nama, kode Kemendagri, dan URL
halaman unduhan resmi daerah tersebut, lalu tekan **Segarkan** di halaman
Pengaturan Formulir. Tidak ada perubahan kode atau rilis baru yang diperlukan.
