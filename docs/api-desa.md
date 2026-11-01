# API Desa — SuDesApp

Dokumen ini menjelaskan **API Desa**: endpoint HTTP lokal di komputer kantor
desa yang membuka data keluaran (angka agregat, perangkat desa), memeriksa
keaslian surat, serta melayani **aplikasi pendamping HP** (masuk sesi akun
desktop dan membaca pengaturan surat supaya hasil cetak sama persis).

> Status dokumen: **berlaku sebagai default implementasi** (sesuai kode saat
> ini). API mati secara bawaan dan hanya melayani `localhost` sampai operator
> menyalakannya di halaman **API Desa** (seksi **BANTUAN** di sidebar, tepat
> di atas Catatan Rilis).

---

## 1. Ringkasan

| Topik | Kebijakan | Status |
|---|---|---|
| Transport | HTTP pada `HttpListener` (tanpa dependensi baru) | Aktif |
| Alamat bawaan | `http://localhost:8790/sudes/api/v1` | Aktif |
| Versioning | Prefix memuat versi (`/v1`) | Aktif |
| Autentikasi | Kunci API pada header `X-Api-Key` atau `Authorization: Bearer` | Aktif |
| Sesi aplikasi | `POST /sesi` menukar akun desktop dengan token 12 jam; endpoint sesi/pengaturan mewajibkan token di header `Authorization: Bearer` | Aktif |
| Cakupan kunci | Kunci utama `Penuh`; kunci tambahan `Agregat` (baca statistik & perangkat). Kategori `Sesi` dan `Pengaturan` hanya dibuka kunci `Penuh` | Aktif |
| Verifikasi surat | `GET /verifikasi/{kode}` — satu-satunya endpoint tanpa kunci API | Aktif |
| Penyimpanan kunci | DPAPI CurrentUser di `%LOCALAPPDATA%\SuDesApp\ApiKunci.bin` | Aktif |
| HTTPS | Tidak ada (localhost); jaringan lokal biasanya perlu tambahan | **[TENTUKAN]** |
| Data pribadi warga | Tidak diekspos (tidak ada NIK, alamat, nomor HP) | Aktif |
| Pembatasan laju | 60 permintaan/menit per alamat pengirim (bawaan) | Aktif |
| Batas badan | 64 KB | Aktif |
| Pencatatan | Log aplikasi + Riwayat Aktivitas (tanpa isi badan) | Aktif |

---

## 2. Mengaktifkan API (langkah demi langkah)

Halaman **API Desa** menampilkan panduan yang sama di bagian CARA MENJALANKAN.
Ringkasnya:

1. Buka menu **API Desa** (seksi **BANTUAN** di sidebar).
2. Centang **Aktifkan API**.
3. Tekan **Buat Kunci Baru**, lalu **Salin** kuncinya. Kunci hanya bisa dibaca
   oleh akun Windows yang sedang memakai aplikasi.
4. Tekan **Simpan** (pojok kanan atas). Listener menyala; kotak status di atas
   berubah menjadi **"API aktif dan melayani permintaan"** dan alamat portnya
   tampil di bawahnya. Bila gagal, kotak itu menampilkan alasan lengkapnya
   (mis. port terpakai, atau kunci belum dibuat).
5. Tekan **Uji Koneksi** untuk memastikan listener menjawab dengan kunci itu.

Kalau langkah 4 gagal karena port terpakai, ganti port (1025–65535; bawaan
`8790`) lalu Simpan lagi. Listener membaca ulang port, mode jaringan, dan
daftar IP setiap kali pengaturan disimpan.

### Dipakai dari HP (aplikasi pendamping)

Aplikasi HP (SuDesApp Android) terhubung lewat API ini. Di komputer kantor:

1. Centang **Izinkan akses dari jaringan**.
2. Isi **IP yang diizinkan** dengan alamat IP HP di jaringan lokal (mis.
   `192.168.1.20`). Tanpa minimal satu IP, API tidak mau menyala dalam mode
   jaringan.
3. Tekan **Simpan**. Windows biasanya menuntut aplikasi dijalankan sebagai
   administrator untuk membuka `http://+:port/`; bila muncul galat port,
   jalankan aplikasi sebagai administrator atau minta IT kantor menambahkan
   izin URL (`netsh http add urlacl`).
4. Di HP: buka aplikasi, tekan **Sambungkan**, isi alamat komputer kantor
   (mis. `http://192.168.1.5:8790`) dan **kunci API** dari halaman ini, lalu
   masuk memakai akun desktop (nama pengguna & kata sandi yang sama).

Setelah tersambung, HP menarik **pengaturan surat** (kop & pejabat penanda
tangan) dari desktop lewat `GET /pengaturan`, jadi hasil cetak dari HP
mengikuti Pengaturan Surat yang sudah diisi di desktop — tidak perlu diisi
ulang di HP.

### Kunci cakupan untuk sistem luar

Kunci utama berwenang penuh. Untuk pihak luar (dashboard kecamatan, website
desa) buat **kunci cakupan** di kotak **KUNCI CAKUPAN** halaman API:

1. Pilih cakupan: **Agregat** (baca statistik, status, perangkat desa).
2. Isi nama pemegang (opsional) lalu tekan **Buat Kunci Cakupan**.
3. **Salin** nilainya dari daftar, lalu serahkan ke sistem luar.
4. Tekan **Hapus** pada barisnya bila kunci sudah tidak dipakai — pemegang
   langsung ditolak pada permintaan berikutnya (listener membaca daftar kunci
   di setiap permintaan, tanpa perlu restart).

Kategori `Sesi` dan `Pengaturan` sengaja hanya dibuka untuk kunci utama
(`Penuh`): kunci Agregat tidak bisa memakai akun operator. Kunci lama
bercakupan `Permintaan` (alur surat online yang sudah dihentikan) tetap
terbaca, tetapi tidak membuka endpoint mana pun.

Listener dinyalakan otomatis setiap aplikasi dibuka selama pengaturannya masih
aktif. Mengganti kunci, mengganti port, atau mematikan API akan menyalakan ulang
listener.

### Bila dibuka ke jaringan

Centang **Izinkan akses dari jaringan** hanya bila API harus dipakai dari
komputer lain di jaringan lokal yang sama. HTTP tidak mengenkripsi isi maupun
kunci — untuk jaringan yang tidak dipercaya, gunakan SSH tunnel atau reverse
proxy HTTPS di depan aplikasi.

---

## 3. Bentuk Respons

Semua respons memakai amplop yang sama:

```json
{
  "sukses": true,
  "pesan": "Permintaan masuk antrean operator.",
  "data": { }
}
```

Gagal memakai `sukses: false`, `kode` berisi kode galat (snake_case huruf
kecil, mis. `api_key_salah`), dan `data` tidak dikirim. Pada respons berhasil
`kode` juga tidak dikirim — pemanggil cukup memeriksa `sukses`.

---

## 4. Endpoint

Semua jalur diawali `/sudes/api/v1`.

| Metode | Jalur | Keterangan |
|---|---|---|
| GET | `/status` | Cek versi API dan waktu server. Endpoint paling ringan. |
| GET | `/statistik` | Angka agregat: warga, kartu keluarga, perangkat desa, surat per status. |
| GET | `/perangkat-desa` | Nama jabatan, pejabat, wilayah, dan masa jabatan (hanya yang aktif). |
| GET | `/verifikasi/{kode}` | Periksa keaslian surat dari kode/kaki QR yang tercetak di surat. **Tanpa kunci API.** |
| POST | `/sesi` | Masuk memakai akun desktop; menerima token sesi 12 jam. |
| GET | `/sesi/siapa` | Identitas sesi yang tokennya dikirim (nama, peran, waktu berakhir). |
| DELETE | `/sesi` | Akhiri sesi yang sedang berjalan. |
| GET | `/pengaturan` | Pengaturan surat desktop (kop & pejabat penanda tangan) untuk aplikasi pendamping. |
| GET | `/rekap-bulanan` | Rekap surat per bulan per status persetujuan untuk satu tahun (`?tahun=YYYY`). |

### 4.1 `GET /status`

```json
{
  "sukses": true,
  "data": {
    "aplikasi": "SuDesApp",
    "versi": "2.10.1.0",
    "waktu": "2026-11-01 09:34:12"
  }
}
```

### 4.2 `GET /statistik`

Berisi `totalWarga`, `wargaAktif`, `wargaLakiLaki`, `wargaPerempuan`,
`jumlahKartuKeluarga`, `wargaPerRt`, angka perangkat desa
(`perangkatTotal`, `perangkatAktif`, `perangkatSelesai`,
`jabatanIntiKosong`), `suratTotal` + `suratPerStatus` +
`suratPerStatusPersetujuan`, serta `permintaanTotal` + `permintaanPerStatus` +
`permintaanBelumDibaca`. Tidak ada kolom yang bisa mengidentifikasi warga.

`suratPerStatusPersetujuan` menghitung surat per status alur persetujuan:
kunci `DIAJUKAN`, `DIVERIFIKASI`, `TERBIT`, `DITOLAK`, dan `TANPA_ALUR`
(surat yang tidak melewati alur, termasuk draf). Kunci selalu terisi penuh
walau nilainya nol, supaya dashboard sistem luar cukup membaca nilai tanpa
menangani kunci yang muncul-hilang. Angka `DIAJUKAN`/`DIVERIFIKASI` adalah
antrean yang menunggu tindakan pejabat desa.

### 4.3 `POST /sesi`

Menukar kredensial akun desktop dengan token sesi. Dipakai aplikasi pendamping
(HP) supaya tidak menyimpan kata sandi.

```json
{
  "username": "operator",
  "kata_sandi": "rahasia"
}
```

```json
{
  "sukses": true,
  "data": {
    "token": "…",
    "berlakuSampai": "2026-11-01T21:00:00",
    "username": "operator",
    "nama": "Nama Tampilan",
    "peran": "Operator"
  }
}
```

- Kunci API tetap wajib dikirim (header `X-Api-Key`) — sesi adalah lapis
  kedua, bukan pengganti kunci.
- Sesi berlaku **12 jam** dan hilang saat aplikasi desktop ditutup; tidak
  disimpan ke mana pun, jadi restart aplikasi menggugurkan semua token.
- Belum ada akun di komputer dibalas `401` `sesi_gagal` dengan pesan
  "Belum ada akun di komputer ini. Buat akun dari aplikasi desktop lebih dulu."
- Kata sandi salah juga `401` `sesi_gagal`.

### 4.4 `GET /sesi/siapa`

Mem-balas identitas pemegang token: `username`, `nama`, `peran`,
`berlakuSampai`, dan token yang sama. Dipakai aplikasi pendamping untuk
memulihkan keadaan setelah dibuka ulang selama token masih berlaku.

### 4.5 `DELETE /sesi`

Mengakhiri sesi pemegang token (balasan `{ "diakhiri": true }`). Token lalu
tidak bisa dipakai lagi.

### 4.6 `GET /pengaturan`

Mengambil Pengaturan Surat desktop untuk aplikasi pendamping: `namaDesa`,
`kecamatan`, `kabupaten`, `jenisKabupaten`, `alamat`, `kodepos`, `email`,
`kepalaDesa`, `nipKepalaDesa`, `jenisLembaga`, beserta `diambilPada`.
Baca-saja — sumber kebenarannya tetap desktop. Database yang belum diisi tetap
dijawab (dengan string kosong), jadi aplikasi pendamping bisa menampilkan
peringatan seperti pratinjau desktop.

### 4.7 `GET /verifikasi/{kode}`

Setiap surat yang terbit membawa kode verifikasi dan kode QR di kakinya
(bentuk kodenya seperti `SD-7K3M-9QX2`). Endpoint ini memeriksa kode tersebut —
dipakai bank/instansi yang memegang surat, **tanpa kunci API**. Isinya sengaja
dibatasi pada data yang sudah tercetak di surat; tidak ada NIK, alamat, atau
nomor HP.

```json
{
  "sukses": true,
  "pesan": "Surat sah: kode terdaftar di arsip desa dan isinya masih sama dengan saat diterbitkan.",
  "data": {
    "kode": "SD-7K3M-9QX2",
    "sah": true,
    "nomorSurat": "470/012/Ds/2026",
    "jenisSurat": "SKTM",
    "tanggalSurat": "2026-09-25T00:00:00",
    "pemohonTersamar": "BUDI S.",
    "dataBerubah": false,
    "statusPersetujuan": "TERBIT"
  }
}
```

- Masukan boleh kode apa adanya (`sd7k3m9qx2`), kode bertanda hubung, atau
  seluruh isi QR (`SUDES|SD-7K3M-9QX2|A1B2C3D4E5`).
- `sah` bernilai `true` hanya bila kode terdaftar **dan** cap dokumen masih
  sama — artinya isi surat tidak berubah setelah diterbitkan.
- Kode yang tidak terdaftar dibalas `404` dengan kode galat
  `kode_tidak_dikenal`.
- Nama pemohon dikembalikan dalam bentuk tersamar (mis. `BUDI S.`), cukup
  untuk mencocokkan dengan nama yang tercetak di surat tanpa menyalin nama
  lengkap ke sistem luar.
- `statusPersetujuan` memuat status alur persetujuan surat: `DIAJUKAN`,
  `DIVERIFIKASI`, `TERBIT`, `DITOLAK`, atau `TANPA_ALUR` (surat tidak
  melewati alur). Untuk surat dalam alur, nilainya sama dengan label yang
  tercetak di kertas surat; field ini selalu terisi sehingga sistem luar cukup
  membaca satu nilai tanpa null-check.

### 4.8 `GET /rekap-bulanan`

Rekap laporan bulanan: jumlah surat per bulan untuk satu tahun, dirinci per
status persetujuan. Dipakai dashboard sistem luar memantau tren arsip surat
sepanjang tahun tanpa perlu memanggil satu per satu.

Query string (opsional):

| Parameter | Bawaan | Keterangan |
|---|---|---|
| `tahun` | tahun berjalan | Tahun rekap, angka 1900–2100. Nilai di luar rentang dibalas `400`. |

```json
{
  "sukses": true,
  "data": {
    "tahun": 2026,
    "dibuatPada": "2026-11-01T09:30:00",
    "jumlahSuratTotal": 148,
    "suratPerStatusPersetujuan": {
      "DIAJUKAN": 2,
      "DIVERIFIKASI": 1,
      "TERBIT": 120,
      "DITOLAK": 3,
      "TANPA_ALUR": 22
    },
    "bulan": [
      { "bulan": "2026-01", "jumlahSurat": 12, "perStatusPersetujuan": { "DIAJUKAN": 0, "DIVERIFIKASI": 0, "TERBIT": 10, "DITOLAK": 1, "TANPA_ALUR": 1 } },
      { "bulan": "2026-02", "jumlahSurat": 9, "perStatusPersetujuan": { "DIAJUKAN": 0, "DIVERIFIKASI": 0, "TERBIT": 8, "DITOLAK": 0, "TANPA_ALUR": 1 } }
    ]
  }
}
```

- `bulan` selalu memuat **12 baris** Januari–Desember; bulan tanpa surat
  tetap muncul dengan nol, supaya tren setahun penuh terbaca langsung.
- `perStatusPersetujuan` memakai kunci yang sama dengan `/statistik`
  (`DIAJUKAN`, `DIVERIFIKASI`, `TERBIT`, `DITOLAK`, `TANPA_ALUR`) dan selalu
  terisi penuh walau nol.
- Hitungan mencakup seluruh baris surat (termasuk draf) — konsisten dengan
  statistik bulanan yang tampil di aplikasi.
- Hanya angka agregat: tidak ada nomor surat, nama, maupun data warga.

---

## 5. Autentikasi

```
X-Api-Key: <kunci>
```

atau

```
Authorization: Bearer <kunci>
```

Kunci dibandingkan dengan perbandingan waktu-tetap. Tanpa kunci yang cocok, seluruh
permintaan ditolak dan jawabannya `401`.

Kunci punya **cakupan** (lihat bagian 2): kunci `Penuh` (kunci utama) boleh ke
semua endpoint berkunci, kunci `Agregat` hanya ke `/status`, `/statistik`,
`/perangkat-desa`, dan `/rekap-bulanan`. Kategori `Sesi` (`POST /sesi`,
`GET /sesi/siapa`, `DELETE /sesi`) dan `Pengaturan` (`GET /pengaturan`)
hanya dibuka kunci `Penuh`. Kunci sah di luar cakupannya menjawab `403`
`cakupan_tidak_cukup`.

Endpoint sesi/pengaturan mewajibkan **dua** lapis: kunci API pada
`X-Api-Key`, lalu token sesi pada header terpisah:

```
Authorization: Bearer <token sesi>
```

Header `Authorization` dipakai untuk token sesi; header `X-Api-Key` dipakai
untuk kunci. Bila keduanya diperlukan, kirim keduanya di header yang berbeda.

Pengecualian: `GET /verifikasi/{kode}` boleh dipanggil tanpa kunci (lihat 4.7).
Pembatas laju tetap berlaku untuk endpoint ini.

---

## 6. Kode Galat

| HTTP | Kode | Arti |
|---|---|---|
| 400 | `permintaan_tidak_sah` | Badan permintaan tidak terbaca atau parameter tidak sah (mis. `?tahun=abc`) |
| 401 | `api_key_salah` | Kunci API tidak dikirim atau tidak cocok |
| 401 | `sesi_diperlukan` | Endpoint bertanda sesi dipanggil tanpa token yang sah |
| 401 | `sesi_gagal` | Nama pengguna/kata sandi salah, atau belum ada akun di komputer ini |
| 403 | `cakupan_tidak_cukup` | Kunci sah, tetapi cakupannya tidak mencakup endpoint ini |
| 404 | `endpoint_tidak_ada` | Jalur atau kode permintaan tidak dikenal |
| 404 | `kode_tidak_dikenal` | Kode verifikasi surat tidak ada di arsip desa |
| 405 | `metode_tidak_diizinkan` | Metode tidak sesuai; lihat header `Allow` |
| 413 | `badan_terlalu_besar` | Badan melebihi 64 KB |
| 429 | `terlalu_banyak_permintaan` | Melewati batas laju; lihat header `Retry-After` |
| 500 | `gagal_di_server` | Kesalahan tak terduga di aplikasi |
| 503 | `api_key_kosong` | Kunci API belum dibuat di komputer ini |

---

## 7. Contoh Pemakaian

```bash
# Status layanan — cek paling ringan
curl -H "X-Api-Key: <kunci>" http://localhost:8790/sudes/api/v1/status

# Statistik desa
curl -H "X-Api-Key: <kunci>" http://localhost:8790/sudes/api/v1/statistik

# Daftar perangkat desa aktif
curl -H "X-Api-Key: <kunci>" http://localhost:8790/sudes/api/v1/perangkat-desa

# Rekap laporan bulanan setahun (bawaan: tahun berjalan)
curl -H "X-Api-Key: <kunci>" "http://localhost:8790/sudes/api/v1/rekap-bulanan?tahun=2026"

# Verifikasi keaslian surat (tanpa kunci API)
curl http://localhost:8790/sudes/api/v1/verifikasi/SD-7K3M-9QX2

# Masuk sesi (aplikasi pendamping)
curl -X POST http://localhost:8790/sudes/api/v1/sesi \
  -H "X-Api-Key: <kunci>" \
  -H "Content-Type: application/json" \
  -d '{"username":"operator","kata_sandi":"rahasia"}'

# Pengaturan surat (butuh kunci + token sesi)
curl http://localhost:8790/sudes/api/v1/pengaturan \
  -H "X-Api-Key: <kunci>" \
  -H "Authorization: Bearer <token sesi>"
```

---

## 8. Batas yang Belum Ditentukan

- **[TENTUKAN]** HTTPS dan Reverse proxy untuk akses dari luar jaringan desa.
- **[TENTUKAN]** Cakupan kunci API per peran pengguna aplikasi (P3-11): cakupan
  per kunci sudah aktif, tetapi belum terikat ke peran pengguna aplikasi.
- **[TENTUKAN]** Retensi dan penghapusan data (P3-15).
