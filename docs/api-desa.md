# API Desa — SuDesApp

Dokumen ini menjelaskan **API Desa**: endpoint HTTP lokal yang membuka data
keluaran (angka agregat, perangkat desa) dan **menerima permintaan surat** dari
sistem luar ke antrean operator.

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
| Cakupan kunci | Kunci utama `Penuh`; kunci tambahan `Agregat` (baca statistik & perangkat) atau `Permintaan` (surat online) — dikelola di kotak **KUNCI CAKUPAN** halaman API | Aktif |
| Verifikasi surat | `GET /verifikasi/{kode}` — satu-satunya endpoint tanpa kunci API | Aktif |
| Penyimpanan kunci | DPAPI CurrentUser di `%LOCALAPPDATA%\SuDesApp\ApiKunci.bin` | Aktif |
| HTTPS | Tidak ada (localhost); jaringan lokal biasanya perlu tambahan | **[TENTUKAN]** |
| Data pribadi warga | Tidak diekspos (tidak ada NIK, alamat, nomor HP) | Aktif |
| Pembatasan laju | 60 permintaan/menit per alamat pengirim (bawaan) | Aktif |
| Batas badan | 64 KB | Aktif |
| Pencatatan | Log aplikasi + Riwayat Aktivitas (tanpa isi badan) | Aktif |
| Peran pengguna | Berlaku di aplikasi (lihat `policy-peran-pengguna.md`); kunci API terikat **cakupan**, bukan peran pengguna | **[TENTUKAN]** (P3-11; cakupan per kunci sudah Aktif) |

---

## 2. Mengaktifkan API

1. Buka menu **API Desa** (seksi **BANTUAN** di sidebar, di atas Catatan Rilis).
2. Centang **Aktifkan API**.
3. Tekan **Buat Kunci Baru**, lalu **Salin** kuncinya. Kunci hanya bisa dibaca
   oleh akun Windows yang sedang memakai aplikasi.
4. Simpan port bila perlu (1025–65535; bawaan `8790`).
5. Tekan **Uji Koneksi** untuk memastikan listener menjawab.

### Kunci cakupan untuk sistem luar

Kunci utama berwenang penuh. Untuk pihak luar (dashboard kecamatan, website
desa) buat **kunci cakupan** di kotak **KUNCI CAKUPAN** halaman API:

1. Pilih cakupan: **Agregat** (baca statistik, status, perangkat desa) atau
   **Permintaan** (kirim & pantau permintaan surat online).
2. Isi nama pemegang (opsional) lalu tekan **Buat Kunci Cakupan**.
3. **Salin** nilainya dari daftar, lalu serahkan ke sistem luar.
4. Tekan **Hapus** pada barisnya bila kunci sudah tidak dipakai — pemegang
   langsung ditolak pada permintaan berikutnya (listener membaca daftar kunci
   di setiap permintaan, tanpa perlu restart).

Kunci cakupan tidak bisa membuka endpoint di luar cakupannya, dan
meregenerasi kunci utama tidak mematikan kunci cakupan (dan sebaliknya).

Listener dinyalakan otomatis setiap aplikasi dibuka selama pengaturannya masih
aktif. Mengganti kunci, mengganti port, atau mematikan API akan menyalakan ulang
listener.

### Bila dibuka ke jaringan

Centang **Izinkan akses dari jaringan** hanya bila API harus dipakai dari
komputer lain di jaringan lokal yang sama. Windows biasanya menuntut aplikasi dijalankan
sebagai administrator untuk membuka `http://+:port/`, dan HTTP tidak
mengenkripsi isi maupun kunci — untuk jaringan yang tidak dipercaya, gunakan
SSH tunnel atau reverse proxy HTTPS di depan aplikasi.

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
| GET | `/status` | Cek versi API dan antrean menunggu operator. Endpoint paling ringan. |
| GET | `/statistik` | Angka agregat: warga, kartu keluarga, perangkat desa, surat per status, permintaan per status. |
| GET | `/perangkat-desa` | Nama jabatan, pejabat, wilayah, dan masa jabatan (hanya yang aktif). |
| GET | `/jenis-surat` | Nama jenis surat yang bisa diminta lewat API. |
| POST | `/permintaan` | Mengirim permintaan surat ke antrean operator dengan status `BARU`. |
| GET | `/permintaan/{kode}` | Status satu permintaan, mis. `PMT-2026-0001`. |
| GET | `/verifikasi/{kode}` | Periksa keaslian surat dari kode/kaki QR yang tercetak di surat. **Tanpa kunci API.** |
| GET | `/rekap-bulanan` | Rekap surat per bulan per status persetujuan untuk satu tahun (`?tahun=YYYY`). |

### 4.1 `GET /status`

```json
{
  "sukses": true,
  "data": {
    "aplikasi": "SuDesApp",
    "versi": "2.5.4.0",
    "waktu": "2026-09-27 10:30:00",
    "antrean": { "BARU": 2, "DIPROSES": 0, "SELESAI": 12, "DITOLAK": 1 },
    "belumDibaca": 2
  }
}
```

### 4.2 `GET /statistik`

Berisi `totalWarga`, `wargaAktif`, `wargaLakiLaki`, `wargaPerempuan`,
`jumlahKartuKeluarga`, `wargaPerRt`, angka perangkat desa
(`perangkatTotal`, `perangkatAktif`, `perangkatSelesai`,
`jabatanIntiKosong`), `suratTotal` + `suratPerStatus` +
`suratPerStatusPersetujuan`, serta `permintaanTotal` +
`permintaanPerStatus` + `permintaanBelumDibaca`. Tidak ada kolom yang bisa
mengidentifikasi warga.

`suratPerStatusPersetujuan` menghitung surat per status alur persetujuan:
kunci `DIAJUKAN`, `DIVERIFIKASI`, `TERBIT`, `DITOLAK`, dan `TANPA_ALUR`
(surat yang tidak melewati alur, termasuk draf). Kunci selalu terisi penuh
walau nilainya nol, supaya dashboard sistem luar cukup membaca nilai tanpa
menangani kunci yang muncul-hilang. Angka `DIAJUKAN`/`DIVERIFIKASI` adalah
antrean yang menunggu tindakan pejabat desa.

### 4.3 `POST /permintaan`

```json
{
  "referensi": "WEB-2026-0001",
  "nama_warga": "Budi Santoso",
  "nik": "3204010101800001",
  "no_hp": "081234567890",
  "jenis_surat": "SKTM",
  "pesan": "SKTM\nNama: Budi Santoso",
  "data": { "keterluan": "Beasiswa" }
}
```

- `referensi` — opsional. Kalau diisi, pengiriman ulang memakai referensi sama
  **tidak** menghasilkan permintaan kedua; responsnya `200` dengan
  `sudahAda: true`. Ini yang membuat integrasi boleh mencoba ulang dengan aman.
- `jenis_surat` — boleh diisi dengan nama dari `GET /jenis-surat`, atau dikosongkan
  bila jenis surat ditulis pada baris pertama `pesan` (format yang sama dengan
  WhatsApp).
- Jenis surat yang hanya bisa dibuat offline (`Katalog Offline`) **ditolak**.
- NIK hanya boleh angka; karakter selain angka dibuang.
- Respons berhasil: `201` untuk permintaan baru, `200` untuk permintaan yang
  sudah ada, dengan `data.kode` berisi kode permintaan.

### 4.4 `GET /permintaan/{kode}`

```json
{
  "sukses": true,
  "data": {
    "kode": "PMT-2026-0001",
    "status": "BARU",
    "sumber": "API",
    "jenis_surat": "SKTM",
    "tanggal": "2026-09-27T10:30:00",
    "catatan": null,
    "adaSurat": false
  }
}
```

Status yang mungkin: `BARU`, `DIPROSES`, `SELESAI`, `DITOLAK`,
`PERLU_PERBAIKAN`. `adaSurat` bernilai `true` setelah permintaan disetujui
operator dan suratnya sudah terbit.

### 4.5 `GET /verifikasi/{kode}`

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

### 4.6 `GET /rekap-bulanan`

Rekap laporan bulanan: jumlah surat per bulan untuk satu tahun, dirinci per
status persetujuan. Dipakai dashboard sistem luar memantau tren arsip surat
sepanjang tahun tanpa perlu memanggil satu per satu.

Query string (keduanya opsional):

| Parameter | Bawaan | Keterangan |
|---|---|---|
| `tahun` | tahun berjalan | Tahun rekap, angka 1900–2100. Nilai di luar rentang dibalas `400`. |

```json
{
  "sukses": true,
  "data": {
    "tahun": 2026,
    "dibuatPada": "2026-09-27T10:30:00",
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
`/perangkat-desa`, `/jenis-surat`, `/rekap-bulanan`, dan kunci `Permintaan`
hanya ke `POST /permintaan` serta `GET /permintaan/{kode}`. Kunci sah di luar
cakupannya menjawab `403` `cakupan_tidak_cukup`.

Pengecualian: `GET /verifikasi/{kode}` boleh dipanggil tanpa kunci (lihat 4.5).
Pembatas laju tetap berlaku untuk endpoint ini.

---

## 6. Kode Galat

| HTTP | Kode | Arti |
|---|---|---|
| 400 | `permintaan_tidak_sah` | Badan permintaan tidak terbaca, jenis surat tidak dikenali, atau parameter tidak sah (mis. `?tahun=abc`) |
| 401 | `api_key_salah` | Kunci API tidak dikirim atau tidak cocok |
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
# Statistik desa
curl -H "X-Api-Key: <kunci>" http://localhost:8790/sudes/api/v1/statistik

# Kirim permintaan surat
curl -X POST http://localhost:8790/sudes/api/v1/permintaan \
  -H "X-Api-Key: <kunci>" \
  -H "Content-Type: application/json" \
  -d '{"referensi":"WEB-2026-0001","nama_warga":"Budi Santoso","nik":"3204010101800001","jenis_surat":"SKTM"}'

# Periksa status permintaan
curl -H "X-Api-Key: <kunci>" http://localhost:8790/sudes/api/v1/permintaan/PMT-2026-0001

# Rekap laporan bulanan setahun (bawaan: tahun berjalan)
curl -H "X-Api-Key: <kunci>" "http://localhost:8790/sudes/api/v1/rekap-bulanan?tahun=2026"

# Verifikasi keaslian surat (tanpa kunci API)
curl http://localhost:8790/sudes/api/v1/verifikasi/SD-7K3M-9QX2
```

---

## 8. Batas yang Belum Ditentukan

- **[TENTUKAN]** HTTPS dan Reverse proxy untuk akses dari luar jaringan desa.
- **[TENTUKAN]** Cakupan kunci API per peran (P3-11): kunci bercakupan
  (`Penuh`/`Agregat`/`Permintaan`) sudah aktif dan bisa dibuat/dihapus di
  halaman API, tetapi belum terikat ke peran pengguna aplikasi. Langkah
  berikutnya bila diperlukan: mencocokkan peran pengguna dengan cakupan kunci
  tanpa mengubah endpoint lama.
- **[TENTUKAN]** Retensi dan penghapusan data (P3-15).
