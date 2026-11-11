# Backend Web `SuDesApp.Api` — untuk klien mobile/browser

Dokumen ini menjelaskan **`SuDesApp.Api`**: backend HTTP (ASP.NET Core) yang
membuka data desa untuk aplikasi di luar desktop, misalnya aplikasi mobile/Android
atau browser. Seluruh logika bisnis tetap di `SuDesApp.Core`; proyek ini hanya
membungkusnya sebagai HTTP di atas **database `desa.db` yang sama** dengan aplikasi
desktop.

> Beda dengan **API Desa** ([`api-desa.md`](api-desa.md)): API Desa adalah listener
> HTTP lokal yang menyala bersama aplikasi desktop di komputer kantor desa, dan
> hanya membuka angka agregat + verifikasi surat dengan kunci API. Backend ini
> adalah proses server terpisah yang bisa dihosting sendiri dan melayani akun,
> data warga, serta register surat untuk klien dengan sesi login.

---

## 1. Ringkasan

| Topik | Kebijakan | Status |
|---|---|---|
| Runtime | ASP.NET Core di atas `SuDesApp.Core` | Aktif |
| Basis data | `desa.db` (SQLite) yang sama dengan desktop; skema dibuat/dimigrasi saat start | Aktif |
| Autentikasi | Sesi token 12 jam (`POST /sesi`), dikirim sebagai `Authorization: Bearer` | Aktif |
| Otorisasi | `SessionContext.Wajib` di Core; endpoint kelola pengguna menuntut `KelolaPengguna` (Administrator) | Aktif |
| Data pribadi | `/warga` dan `/surat` berisi NIK, No. KK, nomor HP — **wajib sesi** | Aktif |
| Data agregat | `/statistik`, `/perangkat-desa`, `/rekap-bulanan` hanya angka/nama jabatan publik | Aktif |
| HTTPS | Tidak ada bawaan; pasang reverse proxy HTTPS di depan untuk produksi | **[TENTUKAN]** |
| CORS | Diyalankan hanya pada `Development` (semua origin) | Aktif (dev) |

---

## 2. Menjalankan

```bash
dotnet run --project SuDesApp.Api --urls http://127.0.0.1:5188
```

Saat start, backend membuka `desa.db` (lokasi mengikuti `appsettings.json` /
konfigurasi `AppConfig`) dan menjalankan `IDatabaseInitializer.InitializeAsync()`
supaya tabel tersedia. Bila berkas database tidak bisa dibuka, proses berhenti
dengan pesan galat — bukan menjawab 500 di setiap endpoint.

Uji cepat:

```bash
curl http://127.0.0.1:5188/api/health
curl http://127.0.0.1:5188/api/versi
```

---

## 3. Endpoint

Semua jalur di bawah akar server. Jalur `/sudes/api/v1/*` sengaja mengikuti
prefix yang sama dengan API Desa agar kontrak sesi/statistik konsisten.

### 3.1 Kesehatan & versi (publik)

| Metode | Jalur | Keterangan |
|---|---|---|
| GET | `/api/health` | `{ status, waktu }` — untuk health check. |
| GET | `/api/versi` | `{ core, versi }` — versi assembly `SuDesApp.Core`. |

### 3.2 Data agregat (publik)

| Metode | Jalur | Keterangan |
|---|---|---|
| GET | `/sudes/api/v1/statistik` | Jumlah warga, kartu keluarga, perangkat desa, surat per status & per status persetujuan. Tidak memuat NIK/alamat/HP. |
| GET | `/sudes/api/v1/perangkat-desa?hanyaAktif=true` | Nama jabatan, pejabat, wilayah, masa jabatan. |
| GET | `/sudes/api/v1/rekap-bulanan?tahun=YYYY` | Rekap surat per bulan per status persetujuan; `bulan` selalu 12 baris. |
| GET | `/sudes/api/v1/jenis-surat` | Daftar jenis surat yang bisa diminta (katalog Core). |
| GET | `/sudes/api/v1/menu` | Menu aplikasi (pilihan surat + blanko NTCR). Tidak memuat data pribadi. |

Contoh:

```bash
curl http://127.0.0.1:5188/sudes/api/v1/statistik
curl "http://127.0.0.1:5188/sudes/api/v1/rekap-bulanan?tahun=2026"
```

### 3.3 Data warga & register surat (wajib sesi)

Kedua endpoint ini mengembalikan **data pribadi** (NIK, No. KK, nomor HP,
alamat). Tanpa sesi yang sah, keduanya menjawab **401**.

| Metode | Jalur | Keterangan |
|---|---|---|
| GET | `/sudes/api/v1/warga?cari=&status=&halaman=&ukuran=` | Tabel warga terpaginasi (mis. 25/baris). |
| GET | `/sudes/api/v1/surat?cari=&status=&tahun=&jenis=&halaman=&ukuran=` | Register surat (NTCR dikecualikan; ada register NTCR sendiri di desktop). |

Contoh:

```bash
curl http://127.0.0.1:5188/sudes/api/v1/warga?halaman=1\&ukuran=25 \
  -H "Authorization: Bearer <token sesi>"
```

Izin per-peran **tidak** diperiksa di endpoint ini — sama seperti halaman
desktop, semua peran yang bisa masuk aplikasi boleh membaca data warga. Yang
dibatasi adalah perubahannya.

### 3.4 Sesi (akun desktop)

| Metode | Jalur | Keterangan |
|---|---|---|
| POST | `/sudes/api/v1/sesi` | Menukar `username` + `kata_sandi` dengan token 12 jam. |
| GET | `/sudes/api/v1/sesi/siapa` | Identitas sesi yang tokennya dikirim. |
| DELETE | `/sudes/api/v1/sesi` | Mengakhiri sesi. |
| POST | `/sudes/api/v1/sesi/awal` | Membuat akun Administrator pertama — **hanya loopback** dan hanya selama tabel `Pengguna` masih kosong. |

Badan `POST /sesi`:

```json
{ "username": "operator", "kata_sandi": "rahasia" }
```

Balasan:

```json
{
  "sukses": true,
  "token": "…",
  "berlakuSampai": "2026-11-06T21:00:00",
  "pengguna": { "username": "operator", "nama": "Nama Tampilan", "peran": "Operator" }
}
```

Aturan yang sama dengan layar login desktop: kata sandi PBKDF2, akun terkunci
5 menit setelah 3 percobaan gagal, pesan gagal disamarkan. Database tanpa akun
membalas **403** dengan pesan "Belum ada akun di database ini.".

### 3.5 Kelola pengguna (wajib sesi + `KelolaPengguna`)

Hanya Administrator. Tanpa sesi → **401**; sesi dengan peran lain → **403**.

| Metode | Jalur | Keterangan |
|---|---|---|
| GET | `/sudes/api/v1/pengguna` | Daftar akun (tanpa hash/salt). |
| POST | `/sudes/api/v1/pengguna` | Buat akun. |
| PUT | `/sudes/api/v1/pengguna/{id}` | Ubah nama/peran/status aktif. |
| POST | `/sudes/api/v1/pengguna/{id}/kata-sandi` | Reset kata sandi. |
| DELETE | `/sudes/api/v1/pengguna/{id}` | Hapus akun. |

Badan tulis memakai `username`, `nama`, `peran`, `kata_sandi`, `aktif`.
Bila peran/status aktif berubah atau kata sandi direset, **semua token sesi
akun itu langsung diputus** — wewenang baru berlaku tanpa menunggu sesi 12 jam
habis.

---

## 4. Batas & risiko yang perlu diputusakan

- **HTTPS wajib untuk produksi.** Backend tidak mengenkripsi transport; pasang
  reverse proxy (nginx/Caddy) atau host dengan TLS.
- **Data pribadi di `/warga` dan `/surat`.** Selama backend dapat dijangkau
  orang yang memegang sesi, data warga bisa dibaca. Batasi jangkauan jaringan
  (firewall/VPN) dan pertimbangkan verifikasi peran di endpoint itu.
- **Basis data bersama.** Desktop dan backend membaca `desa.db` yang sama. Dua
  proses yang membuka SQLite yang sama bisa saling mengunci; kalau backend
  berjalan terus-menerus, jalankan desktop dan backend pada berkas database
  yang sama dengan hati-hati (mis. satu proses sebagai pemilik data).
- **Belum ada dokumen deploy.** `Dockerfile` di repo ini adalah untuk server
  lisensi, bukan backend ini. Untuk produksi, siapkan image .NET 11 + volume
  `desa.db` + reverse proxy HTTPS.

---

## 5. Kapan memakai yang mana

Tiga permukaan HTTP di repo ini punya tanggung jawab berbeda — jangan
tertukar saat mengembangkan:

| Kebutuhan | Pakai | Bentuk proses | Kontrak |
|---|---|---|---|
| Komputer kantor desa: angka agregat + verifikasi surat untuk dashboard/pihak ketiga | **`ApiListener`** (`SuDesApp.Core/Api/`) | Menyala **in-process** bersama aplikasi desktop WPF | `/sudes/api/v1/*` dengan kunci API (`ApiKunci`) |
| Klien mobile/Android/browser: sesi login, data warga, register surat, kelola pengguna | **`SuDesApp.Api`** | Proses server **terpisah** (ASP.NET Core), bisa dihosting sendiri | `/sudes/api/v1/*` dengan token sesi (`Authorization: Bearer`) |
| Lisensi langganan, aktivasi perangkat, sinkronisasi Supabase | **`SuDesApp.Server`** | Proses server terpisah (dipublish pipeline rilis) | Endpoint lisensi/sinkron sendiri |

Prefix `/sudes/api/v1` sengaja **dibuat sama** antara `ApiListener` dan
`SuDesApp.Api` supaya kontrak statistik/sesi tidak bercabang; yang membedakan
hanya mekanisme autentikasi (kunci API vs sesi) dan cakupan datanya.

### Status penerbitan

- **`SuDesApp.Server`** ikut pipeline rilis: tag `vX.Y.Z` memicu
  `.github/workflows/release.yml` yang menerbitkan server + installer desktop.
- **`SuDesApp.Api`** saat ini **belum** dipublish pipeline mana pun — dirujuk
  uji `SuDesApp.Tests/Api/` dan dijalankan manual
  (`dotnet run --project SuDesApp.Api`). Saat klien mobile/browser mulai
  diproduksikan, tambahkan job publish terpisah (mis. container image) —
  jangan digabung ke rilis desktop karena siklus hidupnya berbeda.
