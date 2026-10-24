# Setup Menu Langganan dan Pusat Administrasi dengan Supabase

Dokumen ini menjelaskan bagaimana menu Langganan dan Pusat Administrasi di SuDesApp terhubung langsung ke database Supabase untuk mengelola lisensi dan administrasi perangkat.

## Arsitektur Koneksi

### Menu Langganan (LanggananView)
- **View**: `SuDesApp.Wpf/Views/LanggananView.xaml`
- **ViewModel**: `SuDesApp.Wpf/ViewModels/LanggananViewModel.cs`
- **Service**: `SuDesApp.Core/Services/LanggananKlien.cs`, `LisensiManager.cs`
- **Endpoint Server**: `/aktivasi`, `/aktifkan-trial`, `/buat-tagihan`, `/paket`, `/pengumuman`

**Alur Kerja**:
1. Pengguna membuka halaman Langganan
2. ViewModel memuat status langganan dari cache lokal (offline-ready) menggunakan `LisensiManager`
3. Di latar belakang, ViewModel menghubungi server lisensi untuk:
   - Verifikasi kode aktivasi (`POST /aktivasi`)
   - Cek status langganan
   - Memuat katalog harga (`GET /paket`)
   - Membuat tagihan pembayaran (`POST /buat-tagihan`) — dua jalur: **berlangganan awal** (belum punya kode → kode aktivasi baru diterbitkan otomatis) atau **perpanjangan** (kode sudah dimiliki)
4. Server menghubungi Supabase untuk menyimpan/membaca:
   - Status pelanggan (kolom `pelanggan`)
   - Riwayat pembayaran (kolom `pembayaran`)
   - Perangkat desa (kolom `perangkat`)

### Pusat Administrasi (PusatAdministrasiView)
- **View**: `SuDesApp.Wpf/Views/PusatAdministrasiView.xaml`
- **CodeBehind**: `SuDesApp.Wpf/Views/PusatAdministrasiView.xaml.cs`
- **Service Server**: `SuDesApp.Server/LayananAdmin.cs`
- **Endpoint Server**: `/admin/*` (semua endpoint admin)

**Alur Kerja**:
1. Pengguna membuka Pusat Administrasi
2. View menggunakan WebView2 untuk menampilkan halaman admin dari server
3. Halaman admin dimuat dari `GET /admin` (server mengembalikan `wwwroot/admin.html`)
4. JavaScript di halaman admin melakukan panggilan ke endpoint `/admin/*` dengan token operator:
   - `GET /admin/sesi` - Verifikasi sesi operator
   - `GET /admin/pelanggan` - Daftar semua desa
   - `GET /admin/pelanggan/{kode}/perangkat` - Perangkat per desa
   - `GET /admin/pelanggan/{kode}/pembayaran` - Riwayat pembayaran
   - `POST /admin/pelanggan/{kode}/tandai-lunas` - Catat pembayaran manual
   - Dan banyak lagi

## Prasyarat: Konfigurasi Supabase

Sebelum menggunakan kedua menu ini, server harus terhubung ke Supabase:

### 1. Setup Supabase Project
```bash
# 1. Buat project di https://supabase.com
# 2. Salin informasi koneksi dari Dashboard → Connect → Session Pooler
# 3. Catat:
#    - Host (session pooler)
#    - Project Ref
#    - Password untuk role postgres
```

### 2. Setup Database Lokal (untuk pengembangan)

Edit atau buat file `SuDesApp.Server/appsettings.lokal.json`:

```json
{
  "Supabase": {
    "Host": "aws-0-ap-northeast-2.pooler.supabase.com",     // Dari Dashboard → Connect
    "Port": 5432,
    "User": "sudes_server.jotnrhjzyslyaozzawwq",             // Role aplikasi (ganti PROJECT_REF)
    "DbPassword": "PASSWORD_ROLE_SUDES_SERVER",              // Password yang Anda setel
    "SslMode": "Require",
    "MigrateUser": "postgres.jotnrhjzyslyaozzawwq",          // Untuk menerapkan schema (opsional)
    "MigratePassword": "PASSWORD_ROLE_POSTGRES"
  },
  "Midtrans": {
    "ServerKey": "Mid-server-XXXXXX",
    "UseSandbox": true
  },
  "Lisensi": {
    "AdminToken": "TOKEN_ADMIN_ACAK_MINIMAL_32_BYTE"
  }
}
```

### 3. Jalankan Schema Migration

Server akan **otomatis** menjalankan `SuDesApp.Server/schema.sql` pada startup jika:
- `SUPABASE_MIGRATE_PASSWORD` diisi (credentials pemilik database)
- Skema belum lengkap

Atau jalankan manual di Supabase SQL Editor:
```bash
# 1. Buka Supabase Dashboard → SQL Editor
# 2. Buat query baru, salin isi SuDesApp.Server/schema.sql
# 3. Jalankan
# 4. Setelah selesai, set password role aplikasi:
ALTER ROLE sudes_server PASSWORD 'PASSWORD_ACAK_YANG_KUAT';
```

### 4. Setup Operator Admin

Tambahkan operator minimal satu orang di environment variable atau `appsettings.lokal.json`:

```json
{
  "Lisensi": {
    "AdminOperators": [
      {
        "Id": "operator1",
        "Name": "Nama Operator",
        "Token": "RANDOM_TOKEN_MINIMAL_32_BYTE_ATAU_LEBIH",
        "Permissions": [
          "customers.read",
          "customers.write",
          "devices.write",
          "payments.read",
          "payments.write",
          "payments.reconcile",
          "announcements.read",
          "announcements.write"
        ]
      }
    ]
  }
}
```

Atau via environment variable (di container/deployment):
```bash
SUDES_ADMIN_OPERATORS__0__ID=operator1
SUDES_ADMIN_OPERATORS__0__NAME=Nama Operator
SUDES_ADMIN_OPERATORS__0__TOKEN=TOKEN_ACAK_MINIMAL_32_BYTE
SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0=customers.read
SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1=customers.write
# ... dan seterusnya
```

## Menjalankan Aplikasi

### Mode Pengembangan (Development)

```bash
# 1. Pastikan appsettings.lokal.json sudah diisi dengan credential Supabase
# 2. Jalankan server
cd SuDesApp.Server
dotnet run

# 3. Di terminal lain, jalankan aplikasi WPF
# Visual Studio: Set SuDesApp.Wpf sebagai startup project dan Run
```

Server akan:
1. Membaca credential dari `appsettings.lokal.json`
2. Membangun koneksi string Supabase (Session Pooler)
3. Menjalankan `schema.sql` jika belum ada
4. Verifikasi RLS aktif dan role terbatas
5. Mulai melayani endpoint

Aplikasi WPF akan:
1. Menampilkan halaman Langganan (untuk cek status lisensi)
2. Memungkinkan user membuka Pusat Administrasi (WebView2 ke `GET /admin`)

### Mode Produksi (Production)

Di container atau VPS:

```bash
ASPNETCORE_ENVIRONMENT=Production \
SUPABASE_HOST=aws-0-ap-northeast-2.pooler.supabase.com \
SUPABASE_DB_USER=sudes_server.jotnrhjzyslyaozzawwq \
SUPABASE_DB_PASSWORD=PASSWORD_ROLE \
SUPABASE_MIGRATE_PASSWORD=PASSWORD_POSTGRES \
SUPABASE_SSLMODE=Require \
MIDTRANS_SERVER_KEY=Mid-server-XXXX \
SUDES_ADMIN_OPERATORS__0__ID=operator1 \
SUDES_ADMIN_OPERATORS__0__NAME="Nama Operator" \
SUDES_ADMIN_OPERATORS__0__TOKEN="TOKEN_ACAK" \
SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0=customers.read \
LISENSI_KEY_PATH=/persist/lisensi-signing.key \
dotnet run --project SuDesApp.Server
```

## Endpoint yang Digunakan

### Langganan (Public + Activation)

| Endpoint | Metode | Deskripsi | Autentikasi |
|---|---|---|---|
| `/aktivasi` | POST | Aktivasi desa dengan kode | Kode Aktivasi |
| `/aktifkan-trial` | POST | Mulai trial 14 hari | Kode Aktivasi |
| `/buat-tagihan` | POST | Buat tagihan Midtrans — **berlangganan awal** (`kodeAktivasi` kosong + `namaDesa` → desa terdaftar & kode baru terbit, balasan memuat `kode_aktivasi`) atau **perpanjangan** (kode harus dikenal) | Berlangganan awal tanpa kode; perpanjangan dengan Kode Aktivasi |
| `/paket` | GET | Daftar paket & harga | Publik |
| `/pengumuman` | GET | Pengumuman broadcast | Publik |

### Administrasi (Operator-only)

| Endpoint | Metode | Deskripsi |
|---|---|---|
| `/admin` | GET | Halaman web admin |
| `/admin/sesi` | GET | Info operator saat ini |
| `/admin/pelanggan` | GET | Daftar semua desa |
| `/admin/pelanggan` | POST | Daftarkan desa baru |
| `/admin/pelanggan/{kode}` | GET | Info desa detail |
| `/admin/pelanggan/{kode}/perangkat` | GET | Daftar perangkat desa |
| `/admin/pelanggan/{kode}/pembayaran` | GET | Riwayat pembayaran |
| `/admin/pelanggan/{kode}/tandai-lunas` | POST | Catat pembayaran manual |
| `/admin/pelanggan/{kode}/batas` | POST | Ubah batas perangkat |
| `/admin/pengumuman` | GET/POST | Kelola pengumuman |
| `/admin/rekonsiliasi` | POST | Jalankan rekonsiliasi pembayaran |

Semua endpoint admin memerlukan header `Authorization: Bearer TOKEN_OPERATOR`.

## Alur Langganan: Berlangganan Awal vs Perpanjangan

Dua jalur pembayaran di menu Langganan berbeda sesuai kepemilikan kode aktivasi — operator perlu tahu bahwa **kode justru terbit dari berlangganan awal, bukan dipakai untuk memulainya**:

| | Berlangganan awal | Perpanjangan |
|---|---|---|
| Kondisi | Desa **belum punya** kode aktivasi | Desa **sudah punya** kode aktivasi |
| Isian pengguna | Nama desa (otomatis dari Pengaturan Surat) | Kode aktivasi |
| Hasil `POST /buat-tagihan` | Desa terdaftar + **kode baru diterbitkan otomatis** + tagihan dibuat | Tagihan dibuat atas kode yang sudah dikenal |
| Balasan server | Medan `kode_aktivasi` berisi kode baru (`null` pada perpanjangan) | `kode_aktivasi: null` |
| Setelah pembayaran lunas | Langganan desa baru aktif (webhook/rekonsiliasi) | Langganan berlanjut |

### Berlangganan awal (belum punya kode)

1. Pengguna menekan **Bayar** tanpa mengisi kode. Aplikasi mengambil nama desa dari profil (Pengaturan Surat) lalu mengirim `POST /buat-tagihan` dengan `kodeAktivasi: ""`, `namaDesa` terisi (maksimum 120 karakter), dan `paket` pilihan.
2. Server mendaftarkan desa baru lewat `DaftarkanDenganKodeBaruAsync`, menerbitkan kode aktivasi unik `SUDES-XXXX-XXXX` (dicek ke database, dengan pembatasan percobaan), mencatat log `TagihanBerlanggananAwal` (Warning), lalu membuat tagihan. Harga & durasi tetap dari katalog server — klien tidak pernah menentukan nominal.
3. Pengguna melihat kode barunya di dua tempat:
   - Halaman checkout publik menampilkannya menonjol: **"KODE AKTIVASI ANDA (baru diterbitkan)"** dengan perintah menyimpannya.
   - Aplikasi WPF menyimpannya langsung ke preferensi lokal dan menampilkannya di kotak Kode Aktivasi dengan banner bahwa kode aktif setelah pembayaran lunas.
4. **Pendaftaran tidak memberi akses**: langganan desa baru masih kosong dan hanya menyala setelah pembayaran lunas lewat webhook/rekonsiliasi — kode bisa terbit lebih dulu tanpa celah akses gratis.

### Perpanjangan (sudah punya kode)

1. Pengguna mengisi kode aktivasi yang sudah dimiliki lalu menekan Bayar (`kodeAktivasi` terisi).
2. Kode harus dikenal server; bila tidak → **404** `"Kode aktivasi tidak dikenal."` dan gateway pembayaran tidak dipanggil.
3. Setelah lunas, langganan lama diperpanjang lewat `PerpanjangAsync` yang sama dengan webhook — aturan idempotensi & perpanjangan tetap satu salinan.

### Galat & log

| Keadaan | HTTP | Keterangan |
|---|---|---|
| Kode & nama desa sama-sama kosong | 400 | Pesan menjelaskan dua jalur di atas |
| Nama desa > 120 karakter | 400 | Batas penamaan desa |
| Pendaftaran desa baru gagal | 500 | Log `TagihanBerlanggananAwalGagal` (Error) |
| Kode perpanjangan tidak dikenal | 404 | Tanpa panggilan gateway |

Untuk operator Pusat Administrasi: desa hasil berlangganan awal langsung terlihat di `GET /admin/pelanggan` dengan langganan masih kosong; pembayaran manual bisa dicatat via `POST /admin/pelanggan/{kode}/tandai-lunas` setelah uang diterima.

## Troubleshooting

### "Supabase belum dikonfigurasi"
- Periksa file `SuDesApp.Server/appsettings.lokal.json` sudah ada
- Pastikan `Supabase.Host` dan `Supabase.DbPassword` tidak kosong
- Jalankan server di Mode Development jika credential belum siap (server akan gunakan in-memory store)

### "Role aplikasi tidak bisa login"
- Verifikasi password role `sudes_server` di Supabase SQL Editor
- Sunting `appsettings.lokal.json` dan match password-nya
- Jalankan: `ALTER ROLE sudes_server PASSWORD 'PASSWORD_BARU'` di SQL Editor

### "Isolasi RLS belum lengkap"
- Jalankan `schema.sql` di Supabase SQL Editor terlebih dahulu
- Atau isi `SUPABASE_MIGRATE_PASSWORD` dan biarkan server menjalankannya otomatis

### Menu Langganan tidak menampilkan halaman admin
- Pastikan server lisensi berjalan di alamat yang benar
- Di halaman Langganan, isi "Alamat Server Lisensi" dengan URL server (mis. `https://lisensi.contoh.com`)
- Klik "Cek Status" untuk verifikasi koneksi
- Jika ada WebView2 error, gunakan tombol "Buka di Browser" sebagai fallback

### Pusat Administrasi menampilkan halaman kosong
- Periksa browser DevTools (F12 di admin.html) untuk error JavaScript
- Verifikasi token operator di appsettings.lokal.json / env variable
- Cek endpoint `/admin/sesi` mengembalikan info operator dengan benar

## Diagram Alur Data

```
┌──────────────────────────────────────────────────────────────┐
│                    Aplikasi WPF Desa                          │
├──────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌─────────────────────┐      ┌─────────────────────┐        │
│  │  Langganan View     │      │  Pusat Admin View   │        │
│  │  - Status LIsensi   │      │  - WebView2         │        │
│  │  - Cek Status       │      │  - Admin HTML       │        │
│  │  - Buat Tagihan     │      │  - Kelola Perangkat │        │
│  └──────────┬──────────┘      └──────────┬──────────┘        │
│             │                            │                    │
│  ViewModel  │                   CodeBehind │                  │
│  • LisensiManager              • WebView2.Navigate()         │
│  • LanggananKlien              • JavaScript API calls         │
│             │                            │                    │
│             └────────────────┬───────────┘                    │
│                              │                                │
└──────────────────────────────┼────────────────────────────────┘
                               │ HTTPS
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                  SuDesApp.Server (Lisensi)                    │
├──────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌─────────────────┐                  ┌─────────────────┐   │
│  │  PublicEndpoint │                  │  AdminEndpoint  │   │
│  │  • /aktivasi    │                  │  • /admin       │   │
│  │  • /paket       │                  │  • /admin/sesi  │   │
│  │  • /buat-tag... │                  │  • /admin/pel..│   │
│  └────────┬────────┘                  └────────┬────────┘   │
│           │                                    │              │
│  ┌────────┴───────────────────┬────────────────┴─────────┐   │
│  │                            │                          │   │
│  ▼                            ▼                          ▼   │
│ ┌─────────────────┐  ┌────────────────┐  ┌──────────────────┐
│ │ LayananAktivasi │  │ LayananTagihan │  │ LayananAdmin     │
│ │ • CekAktivasi   │  │ • Buat Invoice │  │ • Kelola Perangkat
│ │ • SetupRLS      │  │ • Verifikasi  │  │ • Kelola Pembayaran
│ └────────┬────────┘  └────────┬───────┘  └────────┬─────────┘
│          │                    │                   │
└──────────┼────────────────────┼───────────────────┼──────────┘
           │                    │                   │
           └────────────────────┼───────────────────┘
                                │ Npgsql
                                ▼
                    ┌────────────────────────┐
                    │  Supabase Postgres     │
                    ├────────────────────────┤
                    │ • pelanggan (RLS)      │
                    │ • langganan (RLS)      │
                    │ • tagihan (RLS)        │
                    │ • pembayaran (RLS)     │
                    │ • perangkat (RLS)      │
                    │ • pengumuman (publik)  │
                    │ • audit_admin (append) │
                    └────────────────────────┘
```

## Referensi File

- **Server**: `SuDesApp.Server/Program.cs` - Inisialisasi endpoint & Supabase
- **Database**: `SuDesApp.Server/schema.sql` - Skema, RLS, fungsi
- **Store**: `SuDesApp.Server/SupabaseStore.cs` - Repository untuk Supabase
- **Admin Service**: `SuDesApp.Server/LayananAdmin.cs` - Logika administrasi
- **WPF ViewModel**: `SuDesApp.Wpf/ViewModels/LanggananViewModel.cs`
- **WPF View**: `SuDesApp.Wpf/Views/LanggananView.xaml`, `PusatAdministrasiView.xaml`

## Keamanan

- **RLS (Row Level Security)**: Setiap desa (tenant) hanya melihat datanya sendiri melalui `app.tenant_id` GUC
- **Role Terbatas**: Aplikasi memakai role `sudes_server` (bukan superuser), dengan RLS aktif
- **Token Operator**: Admin harus provide token pribadi di header `Authorization: Bearer`
- **Idempotensi Pembayaran**: Order ID unik mencegah duplikasi pembayaran di webhook
- **HTTPS Wajib**: Semua komunikasi lokal & cloud harus encrypted
