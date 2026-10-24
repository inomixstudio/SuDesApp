# LogosKabupaten — lambang kabupaten/kota untuk kop surat

Folder ini menyimpan **lambang kabupaten/kota** yang bisa dipilih sebagai
**alternatif** logo kop surat di halaman **Pengaturan Surat → Cetak & Logo**.

## Penting: ini pilihan, bukan penggantian

Logo desa yang Anda unggah sendiri tetap menjadi **logo utama**. Lambang
kabupaten hanya dipakai bila Anda secara tegas memilihnya lewat opsi
**"Sumber logo → Lambang kabupaten/kota"**. Aplikasi tidak pernah mengganti
logo desa Anda secara diam-diam.

## Status: 514 dari 514 kabupaten/kota (lengkap)

Semua kabupaten/kota di `wilayah.tsv` memiliki lambang; setiap berkas punya
entri `sumber.json` yang menyebut berkas acuan Wikimedia Commons (P94/P158 di
Wikidata, atau parameter `lambang` infobox artikel Wikipedia) beserta SHA-1
dan SHA-256 isinya. Kelengkapan ini dijaga uji `SemuaKabupatenKota_PunyaLambang`
di `LogoKabupatenSumberTests` — angkanya bukan sekadar teks yang bisa basi.

Daftar yang **belum** tersedia selalu dibuat ulang oleh skrip
[`LOGO_YANG_HILANG.txt`](LOGO_YANG_HILANG.txt) — saat ini kosong, dan bila
sebuah kode masuk daftar itu lagi, uji cakupan akan gagal.

## Sumber & verifikasi (penting sebelum menambah berkas)

Setiap berkas **wajib** punya entri di [`sumber.json`](sumber.json): nama
berkas acuan di Wikimedia Commons, URL aslinya, dan SHA-256 isinya. Uji
`LogoKabupatenSumberTests` gagal bila ada berkas tanpa entri, hash tidak cocok,
dua berkas untuk satu kode, atau kode yang tidak ada di data wilayah.

Alasannya nyata. Skrip pengunduh lama **menebak**: judul artikel ditentukan dari
nama, lalu gambar dipilih dari daftar gambar artikel berdasarkan kata
"lambang". Cara itu pernah memasang lambang **provinsi** untuk kabupaten di
Aceh, dan penelusuran isi folder ini menemukan dua kesalahan yang tidak
terlihat dari aplikasi sama sekali:

- `32.75` Kota Bekasi berisi **logo DPRD Kota Bekasi**, bukan lambang kota;
- `32.16` Kabupaten Bekasi berisi lambang **Kota** Bekasi (nama berkas
  "Coat of arms of Bekasi" ambigu — kategori Commons-nya "Bekasi (city)").

Selain itu `32.04`, `32.17`, `32.18`, dan `32.71` belum ada, padahal README
lama mengklaim Jawa Barat lengkap. Semuanya sudah diperbaiki.

### Cara memeriksa

```bash
py -3 Tools/verifikasi-logo-kabupaten.py              # berkas yang ada
py -3 Tools/verifikasi-logo-kabupaten.py --semua       # 514 kabupaten/kota
py -3 Tools/verifikasi-logo-kabupaten.py --perbaiki    # ganti yang salah/hilang + tulis sumber.json
```

Alat itu memakai tiga sumber terstruktur, bukan tebakan:

1. artikel `id.wikipedia` → QID Wikidata **dan** parameter `lambang` infobox;
2. Wikidata **P94** (coat of arms) atau **P158** (seal) milik daerah itu;
3. Commons → SHA-1 isi, URL, dan thumbnail berkas acuan.

Berkas dinilai cocok bila byte-nya identik dengan acuan, bentuknya sama dengan
render acuan (dHash), atau SHA-1-nya dikenali Commons sebagai berkas yang
namanya jelas menyebut lambang daerah itu. Kalau nama berkasnya ambigu
(mis. "Coat of arms of Bekasi"), kategori Commons diperiksa untuk memastikan
kabupaten vs kota. `--perbaiki` mengganti berkas yang salah dengan berkas
acuan yang sama dengan yang dipakai artikel/Wikidata.

Berkas yang isinya tidak dikenal Commons dan bentuknya tidak mirip acuan
dianggap **SALAH** — laporan menyebut berkas apa yang sebenarnya terpasang,
jadi tidak ada lagi "kelihatannya benar".

### Batas yang jujur

- Sebagian besar berkas adalah **render 960 px** dari berkas acuan (ditandai
  `cocok_byte: false` di `sumber.json`, dengan SHA-1 acuan tetap dicatat);
  hanya sebagian kecil yang byte-nya identik dengan berkas aslinya. Ini
  dinyatakan apa adanya, bukan dianggap "identik".
- Dua lambang masih di bawah anjuran 256 px karena sumbernya memang tidak lebih
  besar: `12.73` Sibolga 189×200 dan `13.10` Dharmasraya 192×226. Keduanya
  sudah dicari di Commons (kategori resminya), tidak ada versi lebih besar.
- Bila suatu saat `upload.wikimedia.org` membalas 429 (pemblokiran IP sesudah
  unduhan massal), skrip otomatis memakai `commons.wikimedia.org/w/thumb.php`
  yang dilayani host wiki; hasil unduhan juga dicache di folder sementara
  (`SuDesApp-lambang-cache`) agar menjalankan ulang tidak membebani Wikimedia.
- Verifikasi berjalan saat menambah berkas/uji, bukan saat aplikasi dijalankan.

## Cara menaruh berkas

Beri nama berkas = **kode Kemendagri tingkat kabupaten/kota**, ditaruh di
folder ini. Contoh:

| Kabupaten/Kota        | Kode  | Nama berkas      |
|-----------------------|-------|------------------|
| Kabupaten Karawang    | 32.15 | `32.15.png`      |
| Kota Bogor            | 32.71 | `32.71.png`      |
| Kabupaten Garut       | 32.14 | `32.14.png`      |

Kode kabupaten/kota adalah **dua bagian pertama** kode wilayah lengkap desa
(contoh desa Sumberjaya `32.15.20.2011` → kabupatennya `32.15`).

Format gambar yang diterima: **.png, .jpg, .jpeg, .bmp, .gif** (disarankan PNG
latar-transparan, sisi ≥ 256 px supaya tidak pecah saat dicetak). Satu kode
hanya boleh punya **satu** berkas — dua berkas membuat lambang yang tercetak
bergantung urutan pencarian, dan uji akan menolaknya.

Folder lain yang juga dibaca (dan didahulukan bila sama):
`%LocalAppData%\SuDesApp\LogosKabupaten` — untuk tambahan tanpa menyentuh
folder instalasi.

## Persistensi kode di database

Sejak versi 2.6.0, kode Kemendagri kabupaten **disimpan permanen** di
database pada kolom `KodeKabupaten` (tabel `InfoDesa`). Ini berarti:

- Saat aplikasi di-restart, lambang kabupaten dipulihkan secara
  **deterministik** dari kode — bukan dari pencocokan nama yang bisa ambigu
  (mis. Kabupaten Bandung `32.04` vs Kota Bandung `32.73`).
- Bila nama kabupaten ambigu saat halaman Pengaturan Surat dibuka, sistem
  otomatis memilih kabupaten yang benar berdasarkan kode tersimpan.
- Database lama (sebelum 2.6.0) mendapat kolom baru otomatis via migrasi
  `ALTER TABLE`; data lama tetap sah, kolomnya tinggal diisi ulang saat
  pengguna memilih kabupaten berikutnya.

## Perilaku

- Selama sebuah kabupaten **belum punya** berkas di sini, pilihan otomatisnya
  tidak muncul dan kop tetap memakai logo desa seperti biasa.
- Begitu berkasnya ada, pilihan lambangnya muncul di daftar, dan — bila Anda
  belum pernah mengunggah logo sendiri — otomatis terpasang saat kabupaten
  dipilih.
- Bila berkasnya dihapus setelah dipilih, aplikasi jatuh kembali ke logo
  pengguna/bawaan, tidak pernah mencetak kop tanpa logo.

## Menambah kabupaten baru di luar Jawa Barat

1. Cari kode Kemendagri kabupaten/kota tersebut (mis. di
   [wikipedia.org](https://id.wikipedia.org/wiki/Daftar_kabupaten_di_Indonesia)).
2. Unduh lambang resminya — sumber yang disarankan: Wikimedia Commons.
3. Simpan sebagai `{kode}.png` di folder ini.
4. **Wajib**: jalankan `py -3 Tools/verifikasi-logo-kabupaten.py --kode
   {kode} --perbaiki`. Tanpa langkah ini berkas tidak punya entri
   `sumber.json` dan uji akan gagal — memang begitu seharusnya, karena berkas
   tanpa asal-usul tidak bisa dibedakan dari lambang daerah lain.
5. Selesai — aplikasi otomatis menemukan berkas baru tanpa perlu konfigurasi.

Skrip Python `Tools/download-logo-kabupaten-v2.py` masih tersedia untuk
pengunduhan massal dari Wikipedia/Wikimedia Commons, tetapi hasilnya **tidak
boleh dipercaya begitu saja** — skrip itu menebak dari daftar gambar artikel.
Selalu jalankan verifikasi sesudahnya:

```bash
python Tools/download-logo-kabupaten-v2.py              # semua yang belum ada
python Tools/download-logo-kabupaten-v2.py --ulang      # hanya yang tercatat belum ada
python Tools/download-logo-kabupaten-v2.py --kode 11.01,32.73
py -3 Tools/verifikasi-logo-kabupaten.py --perbaiki     # wajib setelah unduh
```

Catatan penting untuk yang menjalankan ulang:

- **Unduhan sengaja lambat** (jeda 0,8 detik antar berkas dan 1,5 detik antar
  batch API). Wikipedia membalas `429 Terlalu Banyak Permintaan` bila terlalu
  cepat.
- **Isi berkas tidak selalu PNG:** sebagian lambang di Wikipedia adalah SVG
  (dirender lewat endpoint thumbnail) atau JPEG yang dibiarkan apa adanya dengan
  ekstensi `.png`. Itu aman — `LogoKabupaten.Cari` mengenali
  `.png/.jpg/.jpeg/.bmp/.gif` dan WPF membaca gambar berdasarkan **isi** berkas,
  bukan ekstensinya.
- **`LOGO_YANG_HILANG.txt` adalah daftar kode yang belum punya berkas** (dibuat
  dari data wilayah), jadi mode `--ulang` bekerja tanpa perlu mengingat kode
  mana yang kurang.
