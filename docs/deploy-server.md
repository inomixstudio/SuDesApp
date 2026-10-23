# Deploy server lisensi SuDesApp

Server lisensi hanya menjadi tempat pembayaran/langganan — data desa tetap di
SQLite lokal di komputer desa. Server bisa dihosting di layanan murah (Railway,
Render, Fly.io) atau VPS. Prasyarat mutlak: **HTTPS**.

## Apa yang di-deploy

`SuDesApp.Server` (minimal API .NET 10) + Supabase Postgres + Midtrans Snap.
Tidak ada file statis aplikasi desktop di sini; hanya endpoint API + halaman
`/checkout`.

## Kredensial yang wajib disiapkan

| Kredensial | Dari mana | Dipakai untuk |
|---|---|---|
| `SUPABASE_HOST` + `SUPABASE_DB_USER` (`sudes_server.<project-ref>`) + `SUPABASE_DB_PASSWORD` | Role dibuat oleh `schema.sql`; kata sandinya Anda setel sekali (`ALTER ROLE ...`) | Koneksi aplikasi — **RLS berlaku di sini** |
| `SUPABASE_MIGRATE_PASSWORD` (opsional) | Project settings → Database → Connection string | Hanya untuk menerapkan `schema.sql` saat start |
| `MIDTRANS_SERVER_KEY` | Dashboard Midtrans → Settings → Access Keys | Membuat Snap, verifikasi webhook, & menanyakan status transaksi (rekonsiliasi) |
| `SUDES_ADMIN_OPERATORS__0__ID/NAME/TOKEN` | ID dan nama operator; token acak pribadi minimal 32 byte | Kredensial operator pertama; `TOKEN` disetel sebagai rahasia |
| `SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0` | Satu izin per indeks | Izin operator, mis. `customers.read`; tambah operator dengan indeks `1`, `2`, dan seterusnya |
| `LISENSI_KEY_PATH` | Di-generate server saat pertama start | Kunci penandatangan token lisensi |

### Menyiapkan konfigurasi lokal Supabase Session Pooler

Di Windows, jalankan `Tools/Set-KonfigurasiSupabaseLokal.ps1`. Helper meminta
hostname **persis** dari Supabase Dashboard → Connect → Session pooler, project
ref, dan password role aplikasi secara tersembunyi. Ia membuat
`SuDesApp.Server/appsettings.lokal.json`, yang diabaikan Git, dikecualikan dari
output/publish, dan ACL-nya dibatasi ke akun Windows saat ini. Jangan menyalin
berkas ini ke repositori atau image deployment.

Gunakan port **5432** (Session Pooler), SSL `Require`, dan username
`sudes_server.<project-ref>`. Jangan menebak hostname pooler dari region; salin
nilai host yang ditampilkan Dashboard. Password yang diminta adalah password
role `sudes_server` yang dibuat oleh `schema.sql`, bukan password database
`postgres`. Untuk penggunaan paling aman, jalankan `schema.sql` sekali lewat
SQL Editor sebagai pemilik database dan jawab `N` pada prompt migrasi otomatis;
server hanya memakai role aplikasi terbatas ketika berjalan.

Saat server mulai, ia memeriksa bahwa role koneksi bukan superuser, bukan
pemilik tabel, tidak memiliki `BYPASSRLS`, RLS aktif pada tujuh tabel, dan
fungsi pencarian lintas-tenant yang diperlukan tersedia. Startup harus mencatat
`Isolasi RLS terverifikasi` sebelum server melayani permintaan. Jika verifikasi
gagal, server berhenti dan tidak boleh dipakai. Pesan itu membuktikan konfigurasi
role/skema dasar. Tes `SkemaPostgresTests` juga memeriksa struktur kebijakan
dan migrasi secara offline; tes itu tidak menggantikan verifikasi startup pada
database Supabase yang sebenarnya.

Jika kredensial pernah ditempel di chat, log, atau berkas yang dibagikan,
rotasikan password database dan token operator di sisi Supabase/server sebelum
dipakai lagi. Menghapusnya dari berkas lokal tidak membatalkan kredensial lama.

### Mengelola skema lewat Supabase CLI (opsional)

CLI Supabase **tidak wajib**: server sudah menerapkan `schema.sql` sendiri saat
start (skemanya idempoten), atau operator menjalankannya sekali lewat SQL Editor.
CLI berguna untuk melihat perbedaan skema, menariknya, atau menerapkannya dari
baris perintah.

```bash
npm install -g supabase            # CLI
supabase init                       # membuat folder supabase/ di repo
supabase login                      # butuh personal access token (sbp_…)
supabase link --project-ref <ref> --password '<password database>'
supabase db push                    # terapkan migrasi di supabase/migrations/
```

> **Token CLI bukan kunci publishable.** `supabase login` hanya menerima
> personal access token (`sbp_…`) dari Dashboard → Account → Access Tokens.
> Kunci publishable/anon (`sb_publishable_…`) hanya berlaku sebagai role
> `anon` di PostgREST — dan role itulah yang haknya dicabut oleh `schema.sql`.
> Memberikannya ke server lisensi justru membuka data desa; kunci publishable
> tidak pernah dipakai server.

Belum punya token CLI? Skema tetap bisa diterapkan tanpa `link`:

```bash
supabase db push --db-url "postgresql://postgres.<ref>:<password>@<host-pooler>:5432/postgres?sslmode=require"
```

Migrasi di `supabase/migrations/` adalah **salinan** `SuDesApp.Server/schema.sql`
— server menjalankan berkas itu sendiri, CLI menjalankan salinannya. Keduanya
wajib sama persis: `python Tools/sinkron-migrasi-supabase.py` menulis ulang
salinan itu, dan uji `MigrasiSupabaseTests` gagal kalau keduanya menyimpang.

Kredensial CLI disimpan di `supabase/.temp/` (diabaikan Git). Jangan menulis
kata sandi database di berkas yang ikut repo, dan rotasi password itu bila
pernah bocor.

### Cara cepat mencoba di komputer sendiri (bukan produksi)

Paling mudah lewat profil Visual Studio sudah tersedia (`http://localhost:5000`,
`ASPNETCORE_ENVIRONMENT=Development`) — atau dari terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 \
  dotnet run --project SuDesApp.Server --no-launch-profile
```

`Development` wajib di sini: tanpa `MIDTRANS_SERVER_KEY`, server sengaja
menolak start di luar Development. Di mode itu tagihan dibuat dengan tautan
contoh, tetapi **aktivasi, trial, katalog /paket, dan pengumuman tetap
berjalan sungguhan** terhadap database.

Uji cepat dari terminal — kedua baris ini harus menjawab sebelum aplikasi
desktop dibuka:

```bash
curl http://localhost:5000/        # {"status":"SuDesApp lisensi server", ...}
curl http://localhost:5000/paket   # [{"kode":"bulanan", ...}]
```

Bila `/` menjawab tetapi `/paket` tidak, masalahnya di sisi server (bukan di
aplikasi). Bila keduanya kosong, server belum berjalan: periksa log startup —
server sengaja berhenti, bukan diam-diam melayani, bila kredensial Supabase
kosong, RLS tidak terverifikasi, atau (di luar Development) ServerKey Midtrans
kosong. Di aplikasi, buka **Langganan → Server Lisensi**, isi
`http://localhost:5000`, tekan **Simpan & Uji** sampai chip berbunyi
**Terhubung**.

## Environment variables

Semua nilai environment **menang atas** `appsettings*.json`. Kosongkan
`appsettings.lokal.json` sebelum deploy, atau jangan ikutkan berkas itu.

| Variabel | Wajib? | Contoh |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | ya | `Production` |
| `SUPABASE_HOST` | ya | hostname persis dari Dashboard → Connect (Session pooler); indeks host tidak bisa ditebak dari region |
| `SUPABASE_DB_PASSWORD` | ya | (kata sandi role `sudes_server`) |
| `SUPABASE_DB_USER` | tidak | `sudes_server.jotnrhjzyslyaozzawwq` (default `sudes_server`) |
| `SUPABASE_MIGRATE_USER` | tidak | `postgres.jotnrhjzyslyaozzawwq` (default `postgres`) |
| `SUPABASE_MIGRATE_PASSWORD` | tidak | (kata sandi role `postgres`) — hanya bila server boleh menerapkan skema |
| `SUPABASE_SSLMODE` | tidak | `Require` (default) |
| `MIDTRANS_SERVER_KEY` | ya di produksi | lihat dashboard Midtrans |
| `MIDTRANS_SANDBOX` | ya | `false` untuk produksi, `true` saat uji |
| `MIDTRANS_NOTIFICATION_URL` | tidak | URL **publik** server yang menerima notifikasi transaksi uji (header `X-Override-Notification`); boleh ditulis `https://host` atau `https://host/webhook-pembayaran` |
| `SUDES_ADMIN_OPERATORS__0__ID` | ya untuk admin | ID stabil, mis. `operator-utama` |
| `SUDES_ADMIN_OPERATORS__0__NAME` | ya untuk admin | Nama operator yang tampil dan dicatat di audit |
| `SUDES_ADMIN_OPERATORS__0__TOKEN` | ya untuk admin | Token pribadi acak minimal 32 byte; simpan sebagai secret deployment |
| `SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0` | ya untuk admin | Izin bernama; daftar izin ada di `docs/langganan-dan-lisensi.md` |
| `LISENSI_KEY_PATH` | ya | path berisi kunci. Bila kosong/tidak diisi, memakai `keys/lisensi-signing.key` relatif ke **ContentRoot** server (folder proyek saat `dotnet run`). Nilai kosong (`""`) dan spasi diperlakukan sama dengan tidak diisi — placeholder `"KeyPath": ""` di `appsettings.json` tidak lagi membuat server gagal start. |
| `ASPNETCORE_URLS` | tidak | `http://+:8080` (default image) |
| `SUDES_TRUSTED_PROXY_IPS` | tidak | IP proxy tepercaya, dipisahkan koma (mis. `127.0.0.1,10.0.0.5`) |
| `SUDES_TRUSTED_PROXY_NETWORKS` | tidak | CIDR proxy tepercaya, dipisahkan koma (mis. `10.0.0.0/24`) |

Header `X-Forwarded-For` dan `X-Forwarded-Proto` hanya diterima dari alamat
atau jaringan yang dicantumkan di dua variabel proxy tersebut. Jangan
mencantumkan jaringan publik luas; proxy harus menimpa header forward dari
klien sebelum meneruskannya. Tanpa allowlist, server memakai alamat peer
langsung untuk pembatasan laju dan menolak HTTP. HTTPS harus berhenti di proxy
tepercaya atau langsung di server. Pengecualian HTTP hanya untuk `/healthz`
dan loopback dalam Development.

Setiap operator harus punya ID dan token unik. Atur variabel berindeks melalui
`SUDES_ADMIN_OPERATORS__0__...`; simpan hanya `TOKEN` di secret manager. Untuk
operator berikutnya gunakan indeks `1`, lalu `2`. Token lama `SUDES_ADMIN_TOKEN`
tidak lagi diterima. Setiap permintaan API admin menyertakan `X-Admin-Operator`
dan `Authorization: Bearer <token>`; gunakan hanya izin minimum yang diperlukan.

> **Audit 2026-10-22:** variabel `SUDES_ADMIN_OPERATORS__<indeks>__*` sekarang
> dipetakan eksplisit oleh `Program.cs` ke `Lisensi:AdminOperators:<indeks>:*`.
> Sebelumnya provider environment menghasilkan kunci
> `SUDES_ADMIN_OPERATORS:*`, sehingga registry operator kosong dan semua
> endpoint admin membalas 503 walaupun env sudah benar.

### Uji lokal tanpa Supabase

Dengan `ASPNETCORE_ENVIRONMENT=Development` dan kredensial Supabase kosong,
server memakai penyimpanan memori (langganan, pengumuman, audit). Ini hanya
untuk menguji aplikasi, `/admin`, dan alur lisensi di komputer sendiri — data
hilang saat server berhenti dan mode ini tidak aktif di Production. Contoh:

```bash
ASPNETCORE_ENVIRONMENT=Development \
SUDES_ADMIN_OPERATORS__0__ID=operator-lokal \
SUDES_ADMIN_OPERATORS__0__NAME="Operator Lokal" \
SUDES_ADMIN_OPERATORS__0__TOKEN="<token-acak-minimal-32-byte>" \
SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0=session.read \
SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1=customers.read \
dotnet run --project SuDesApp.Server
```

Buka `http://localhost:5000/admin`, lalu masuk dengan ID/token di atas. Aplikasi
desa yang menunjuk `http://localhost:5000` akan melihat chip **Terhubung**.

Tanpa `MIDTRANS_SERVER_KEY` di luar mode Development, server **menolak start** —
webhook tanpa verifikasi signature akan membiarkan siapa pun memperpanjang
langganan gratis.

Tanpa `SUPABASE_MIGRATE_PASSWORD`, server tidak mengubah skema sama sekali;
jalankan `schema.sql` sekali di SQL Editor (lihat bagian berikut).

### Rekonsiliasi pembayaran (backstop webhook)

Webhook tetap jalur utama. Rekonsiliasi adalah jaring pengaman bila notifikasi
Midtrans tidak pernah sampai (URL webhook berubah, server sempat mati, kiriman
hilang): server menanyakan status order yang belum lunas langsung ke Midtrans,
lalu menyelesaikannya memakai **aturan yang sama persis** dengan webhook.

| Variabel | Bawaan | Arti |
|---|---|---|
| `REKONSILIASI_INTERVAL_MENIT` | `15` | Jarak antar putaran. `0` = penjadwalan latar mati (endpoint manual tetap bisa dipakai). |
| `REKONSILIASI_JEDA_AWAL_MENIT` | `1` | Tunggu sebelum putaran pertama. |
| `REKONSILIASI_BATAS` | `25` | Maksimum tagihan diperiksa per putaran. |
| `REKONSILIASI_MINIMAL_UMUR_MENIT` | `10` | Tagihan yang lebih baru dari ini dilewati (pembeli masih di halaman pembayaran). |
| `REKONSILIASI_MAKSIMAL_UMUR_HARI` | `30` | Tagihan yang lebih tua dari ini diabaikan — sudah mati, dan bila ikut diperiksa ia akan menghabiskan kuota batch sehingga tagihan baru tidak pernah dicek. |

Penjadwalan latar **hanya menyala bila `MIDTRANS_SERVER_KEY` terisi**: tanpa
kunci itu status transaksi tidak bisa ditanyakan, jadi server mencatat alasannya
di log start dan tidak menjadwalkan apa pun.

Jalankan sekali secara manual (mis. setelah memperbaiki URL webhook):

```bash
curl -X POST https://<host>/admin/rekonsiliasi \
     -H "Authorization: Bearer $SUDES_ADMIN_OPERATOR_TOKEN" \
     -H "X-Admin-Operator: $SUDES_ADMIN_OPERATOR_ID"
```

Balasannya berisi hitungan `diperiksa`, `diselesaikan`, `sudahDiselesaikan`,
`belumBayar`, `perluDiperiksa`, `tidakPasti`, plus catatan per order. Website
Midtrans TIDAK perlu diubah untuk ini — endpoint status memakai kredensial
server yang sama seperti pembuatan tagihan.

Untuk membebaskan kuota perangkat desa (aktivasi ditolak karena penuh — mis.
pelanggan mengganti komputer), buka **`https://<host>/admin`** di peramban
(masukkan ID dan token pribadi operator sekali per tab). Halaman itu juga bisa mendaftarkan,
mengubah, dan menghapus desa, serta menampilkan riwayat pembayaran per desa
(dapat diunduh CSV). Setara lewat `/admin/pelanggan*`, `/admin/ekspor/*.csv`,
atau `Tools/Admin-Lisensi.ps1`. Lihat
[langganan-dan-lisensi.md](langganan-dan-lisensi.md#pusat-administer-kelola-desa--perangkat).

### Midtrans di produksi (hasil uji 2026-10-22)

Server lisensi memakai **hanya `MIDTRANS_SERVER_KEY`**. `ClientKey` dan
`Merchant ID` tidak dipakai server sama sekali: Snap dibuka lewat
`redirect_url` di peramban, bukan lewat widget JavaScript di dalam aplikasi —
jadi menambahkannya ke konfigurasi tidak ada efek apa pun, dan menambahkannya
ke repo berarti membocorkan kredensial merchant tanpa alasan.

| Kunci | Environment | Arti |
|---|---|---|
| `SB-Mid-server-…` | sandbox | `MIDTRANS_SANDBOX=true` (atau kosongkan → bawaan sandbox) |
| `Mid-server-…` | production | `MIDTRANS_SANDBOX=false` **wajib** — kunci sandbox ditolak host production, dan sebaliknya |

`MIDTRANS_SANDBOX` yang bernilai **kosong** dianggap "belum diisi", bukan
"production": platform deploy kerap menyuntik variabel ada tetapi kosong, dan
tanpa aturan itu server diam-diam bertanya status ke host sandbox sehingga
tidak ada pembayaran produksi yang pernah cocok.

Dua hasil yang tidak terduga dari uji langsung ke Midtrans production:

1. **Order Snap yang belum dibuka pembeli tidak muncul di Core API.**
   `GET https://api.midtrans.com/v2/{order_id}/status` membalas
   `{"status_code":"404","status_message":"Transaction doesn't exist."}`
   untuk order Snap yang masih freshly dibuat — dan balasannya **HTTP 200**,
   bukan 404. Halaman pembayarannya sendiri membalas 200 (order-nya
   benar-benar ada di akun merchant), hanya belum punya catatan pembayaran.
   Karena itu server membaca `transaction_status` di dalam body: jawaban
   tanpa field itu berarti "tidak bisa dipastikan" (bukan "transaksi
   ditemukan"), dan rekonsiliasi menghitungnya `tidakPasti` (gagal tertutup),
   **bukan** "pembeli gagal bayar". Tagihan seperti itu baru bisa dipastikan
   setelah pembeli benar-benar memilih metode pembayaran, jadi satu putaran
   rekonsiliasi terlalu dini tidak akan pernah menyelesaikannya.
2. **Notifikasi Midtrans memakai `gross_amount` bertulis `"150000.00"`.**
   Verifikasi signature memakai string itu **persis seperti dikirim** —
   `SHA512(order_id + status_code + gross_amount + server_key)` — bukan
   angka yang di-parse ulang ke format lain.

Verifikasi signature tetap menolak hal yang harus ditolak: tanpa signature →
403, signature sah tapi order tak dikenal → 404, signature sah tapi nominal
tidak cocok → 400, dan webhook berulang → `duplikat` tanpa tambahan hari.

### Halaman uji pembayaran (`/checkout-uji`)

Halaman operator untuk **membuktikan alur pembayaran dari ujung ke ujung**:
buat transaksi Snap NYATA di akun merchant yang sedang dipakai, bayar di
halaman Midtrans, lalu lihat notifikasi aslinya masuk ke server ini —
signature-nya sah, server membalas 200, dan masa akses desa bertambah.

| Endpoint | Kegunaan |
|---|---|
| `GET /checkout-uji` | Halaman web (statis; tidak ada kredensial tertanam di HTML) |
| `GET /admin/uji-pembayaran` | Keadaan jalur uji: lingkungan gateway, arah notifikasi, ServerKey terisi? |
| `POST /admin/uji-pembayaran` | Buat transaksi uji untuk satu desa uji |
| `GET /admin/uji-pembayaran/{orderId}` | Keadaan satu transaksi: status di gateway + jejak notifikasi |
| `GET /admin/uji-pembayaran/notifikasi` | Notifikasi webhook terakhir yang masuk (jejak di memori server) |

Semuanya di bawah `/admin/*`, jadi butuh operator dengan izin
**`payments.test`**. Izin itu **terpisah** dari `payments.record`:
`payments.record` menambah hak mencatat pembayaran manual, sedangkan
`payments.test` MEMBUAT transaksi sungguhan di akun merchant — jadi tidak
boleh ikut dibuka ke operator yang cuma berhak mencatat pembayaran.

Dua hal yang wajib disiapkan supaya notifikasi benar-benar tertangkap:

1. **Server harus bisa dijangkau Midtrans** lewat HTTPS publik. Untuk uji di
   komputer sendiri, tunnel (mis. `cloudflared`) cukup — tunnel meneruskan
   ke `http://127.0.0.1:<port>` lokal.
2. **`MIDTRANS_NOTIFICATION_URL`** diisi dengan URL publik itu. Transaksi uji
   lalu memakai header `X-Override-Notification`, jadi notifikasinya dikirim
   ke server tempat operator membuktikan alurnya — **tanpa mengubah
   "Payment Notification URL" di dashboard akun** (yang berlaku untuk semua
   transaksi, termasuk pelanggan sungguhan). Env itu boleh ditulis
   `https://host` maupun `https://host/webhook-pembayaran`; path webhook
   otomatis ditambahkan.

Bila env itu kosong, notifikasi transaksi uji mengikuti URL dashboard akun.
Halaman uji menunjukkannya sebagai peringatan, supaya operator tahu kenapa
notifikasinya tidak pernah masuk.

> **Uji di production memakai uang sungguhan.** Halaman ini membuat
> transaksi Snap di akun merchant yang aktif. Pilih paket terkecil, pakai
> desa uji, lalu **hapus desa uji** setelah selesai (tombol "Hapus desa uji"
> di halaman, atau `DELETE /admin/pelanggan/{kode}?konfirmasi={kode}`) supaya
> tagihan & pembayarannya tidak tertinggal di database.

### Verifikasi menyeluruh setelah deploy

Setelah server hidup dengan Supabase & Midtrans sungguhan,
`Tools/uji-e2e-server-lisensi.sh` menjalankan seluruh pemeriksaan lewat HTTP —
endpoint publik, gerbang token & izin admin, data desa, aktivasi + tanda tangan
token, batas perangkat, pembukuan manual & bukti transfer, transaksi Snap
**nyata**, semua pintu penolakan webhook, rekonsiliasi, ekspor, pengumuman —
lalu menghapus jejak ujinya (desa uji dan pengumuman uji).

```bash
BASE_URL=https://lisensi.example.com OPERATOR_TOKEN=... \
  Tools/uji-e2e-server-lisensi.sh
```

Skrip itu **idempoten**: kode desa ujinya tetap, sisa run sebelumnya dibersihkan
lebih dulu, pembersihan akhir berjalan walau uji gagal, dan jumlah desa setelah
putaran kembali seperti semula. Yang tetap tinggal hanya order Snap **belum
dibayar** di dashboard Midtrans (tidak ada API publik untuk menghapusnya) —
skrip mencetak order id-nya. Butuh izin operator yang lengkap; lihat header
berkasnya.

## Isolasi antar-desa (tenant_id + RLS)

Satu desa = satu tenant (`tenant_id` = `pelanggan.id`). Kelima tabel tenant
memakai Row Level Security dengan predicate `tenant_id = (SELECT tenant_aktif())`,
dan `tenant_aktif()` membaca GUC `app.tenant_id` yang **disetel server per
transaksi** dari desa yang sudah diverifikasi — tidak pernah dari klien.
Tanpa GUC: nol baris (gagal tertutup). Tabel `pengumuman` juga ber-RLS, tetapi
kebijakannya beda: baca publik untuk semua role, tulis hanya `sudes_server`
(hak tulis `anon`/`authenticated` dicabut oleh `schema.sql`).

Yang wajib diingat:

1. **Server tidak boleh memakai role `postgres`.** Pemilik tabel melewati RLS,
jadi isolasinya hilang tanpa peringatan apa pun. Server memeriksa hal ini saat
start dan **menolak start** bila role koneksinya superuser/BYPASSRLS atau
pemilik tabel — termasuk bila RLS belum aktif atau fungsi pencariannya belum ada.

   > **Saat memperbarui server:** jalankan `SuDesApp.Server/schema.sql` **sebelum**
   > mengganti biner. Pemeriksaan start kini mencakup `pengumuman` (6/6 tabel).
   > Instalasi lama yang skemanya belum dijalankan ulang akan berhenti dengan
   > pesan `RLS aktif di 5/6 tabel` — itu disengaja: lebih baik server mati
   > daripada menerima tulisan lewat kunci `anon`. Aman dijalankan berulang kali.
2. **Setel kata sandi role aplikasi sekali** (kata sandi tidak ditulis di berkas
apa pun di repo):

   ```sql
   ALTER ROLE sudes_server PASSWORD '<kata sandi acak panjang>';
   ```

   Cukup setel kata sandinya — mengubah atribut role (LOGIN/BYPASSRLS) bisa
   ditolak platform. Lakukan **sebelum** start pertama: pooler Supabase butuh
   waktu sesaat untuk menerima kata sandi baru, dan bila start pertama masih
   mengeluh soal login, cukup jalankan lagi sebentar kemudian.

   Lalu pakai di `SUPABASE_DB_USER=sudes_server.<project-ref>` +
   `SUPABASE_DB_PASSWORD=<kata sandi tadi>`.
3. **Hanya dua pencarian yang lintas-tenant**, keduanya by credential:
`dir_cari_pelanggan(kode)` (kode aktivasi itu sendiri kredensialnya) dan
`dir_cari_tagihan(order_id)` (dipanggil webhook hanya setelah signature SHA512
Midtrans sah). Keduanya `SECURITY DEFINER`, `EXECUTE`-nya dicabut dari PUBLIC
dan hanya diberikan ke `sudes_server`.

## Kunci penandatangan (paling penting untuk diingat)

Alur token: server menandatangani token ECDSA P-256, aplikasi desktop
memverifikasi dengan kunci **publik** yang ditanam di dalam binary-nya. Karena
itu:

- Kunci **privat** dibuat otomatis saat server pertama kali jalan di
  `LISENSI_KEY_PATH`. Simpan di volume yang **persisten** (volume Railway/Render,
  atau folder di VPS). Kalau container diganti tanpa volume, kunci baru dibuat →
  semua token lama tidak valid → semua desa terlihat "baca-saja".
- Salin `https://<host>/kunci-publik` (SPKI base64url) ke
  `Lisensi:KunciPublik` di `appsettings.json` aplikasi **sebelum rilis client**.
- Kunci privat tidak pernah dikirim ke aplikasi.

## Langkah deploy

### Opsi A — Railway (paling singkat)

1. Buat template `Railway` dari repo (atau `railway init` lalu `railway up`).
   Buildpack otomatis mendeteksi `Dockerfile`.
2. Tambahkan semua environment variables di atas.
3. Pasang volume di `/lisensi` (untuk kunci penandatangan).
4. Buka domain, pastikan HTTPS aktif (Railway menyediakan `*.up.railway.app`).
5. Di dashboard Midtrans: Settings → Configuration → set **Payment Notification
   URL** = `https://<host>/webhook-pembayaran`. Simpan (cek kotak "receive
   notification after payment made via payment gateway").
6. Catat `LISENSI_KEY_PATH` yang dibuat server (isi volume `/lisensi/keys`).

### Opsi B — Render

1. New → Web Service → pilih repo, bahasa `Docker`.
2. Set env vars (di atas) + `LISENSI_KEY_PATH=/lisensi/keys/lisensi-signing.key`.
3. Add a persisted Disk at `/lisensi` (Render "Disks" feature).
4. Domain: Render memberi `*.onrender.com` HTTPS.
5. Set Payment Notification URL Midtrans ke `https://<host>/webhook-pembayaran`.

### Opsi C — VPS

1. Build image: `docker build -t sudesapp-server .`
2. Jalankan dengan volume:

   ```bash
   docker run -d --restart=unless-stopped \
     -p 127.0.0.1:8080:8080 \
     -e ASPNETCORE_ENVIRONMENT=Production \
     -e SUPABASE_HOST=... \
     -e SUPABASE_DB_USER=sudes_server.<project-ref> -e SUPABASE_DB_PASSWORD=... \
     -e MIDTRANS_SERVER_KEY=... -e MIDTRANS_SANDBOX=false \
     -e SUDES_ADMIN_OPERATORS__0__ID=operator-utama \
     -e SUDES_ADMIN_OPERATORS__0__NAME="Operator Utama" \
     -e SUDES_ADMIN_OPERATORS__0__TOKEN=... \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0=customers.read \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1=customers.create \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__2=customers.update \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__3=customers.delete \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__4=devices.read \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__5=devices.manage \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__6=payments.read \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__7=payments.record \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__8=payments.export \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__9=payments.reconcile \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__10=payments.test \
     -e SUDES_ADMIN_OPERATORS__0__PERMISSIONS__11=session.read \
     -v /srv/sudes/lisensi:/lisensi \
     sudesapp-server
   ```

   Bind ke loopback supaya klien hanya bisa masuk melalui reverse proxy HTTPS.
   Tambahkan `-e SUPABASE_MIGRATE_USER=postgres.<project-ref> -e
   SUPABASE_MIGRATE_PASSWORD=...` hanya bila container ini yang boleh
   menerapkan `schema.sql` saat start.

3. Pasang HTTPS: Caddy (`Caddyfile` cepat) atau Nginx + certbot, reverse proxy
   ke `localhost:8080`. Webhook Midtrans hanya menerima HTTPS dan pelanggan
   memakai HTTPS juga.
4. Set Payment Notification URL Midtrans ke `https://domain/webhook-pembayaran`.

## Langkah setelah deploy

1. **Verifikasi health**: `curl https://<host>/` harus balas
   `{"status":"SuDesApp lisensi server", ...}`. Log start juga mencetak
   `Isolasi RLS terverifikasi: role 'sudes_server' ... 6/6 tabel ber-RLS`
   (lima tabel tenant + `pengumuman`).
2. **Verifikasi isolasi RLS** (sekali, di SQL Editor). Buat dua desa uji, lalu
   periksa bahwa satu tenant tidak bisa melihat baris tenant lain:

   ```sql
   -- Ganti <id-A> / <id-B> dengan id dari dua baris pelanggan.
   BEGIN;
   SELECT set_config('app.tenant_id', '<id-A>', true);
   SELECT count(*) FROM tagihan;                     -- hanya milik A
   SELECT count(*) FROM tagihan WHERE tenant_id = '<id-B>';   -- harus 0
   -- Tulis lintas-tenant harus DITOLAK (ERROR: new row violates row-level security policy)
   INSERT INTO tagihan (order_id, pelanggan_id, tenant_id, paket, nominal, hari)
   VALUES ('uji-rls-1', '<id-B>', '<id-B>', 'bulanan', 50000, 31);
   ROLLBACK;
   ```

   Untuk mencoba sebagai role aplikasi, `SET ROLE sudes_server;` lebih dulu —
   sebagai `postgres` (pemilik tabel) RLS memang dilewati, dan itulah sebabnya
   server tidak boleh memakai role itu.
2. **Isi public key di aplikasi** (lihat bagian *Kunci penandatangan*).
3. **Set `Lisensi:UrlServer`** di `SuDesApp.Wpf/appsettings.json` (file yang
   ditanam ke hasil build) ke `https://<host>` — bukan `localhost`.
4. **Uji end-to-end** dengan pembayaran sungguhan kecil, lalu pastikan webhook
   masuk: cek log server / tabel `pembayaran`; verifikasi status kegiatan
   pembelian tidak dapat dipalsukan.
5. **Putar kata sandi DB Supabase** — selama pengembangan, kata sandi pernah
   tertulis di chat; ganti via Dashboard → Settings → Database → Reset password.
6. Halaman checkout: `https://<host>/checkout` — bisa di-link dari menu
   "Langganan" di aplikasi atau dibuka langsung (lihat catatan bawah).
7. **   Saat memperbarui server ini**: jalankan ulang `schema.sql` (aman diulang)
   atau set `SUPABASE_MIGRATE_PASSWORD` supaya server menerapkannya sendiri.
   Rekonsiliasi memakai fungsi `dir_tagihan_belum_dibayar` yang baru ada di
   versi skema ini; bila fungsinya belum ada, log akan menyebutkannya dan
   rekonsiliasi dilewati — webhook tetap berjalan seperti biasa.
8. **Cek rekonsiliasi sekali** setelah deploy: `POST /admin/rekonsiliasi`
   dengan operator berizin `payments.reconcile` harus membalas hitungan (mis. `diperiksa: 0`).

## Catatan keamanan

- **Role aplikasi terbatas + RLS.** Server memakai `sudes_server` (bukan
  pemilik tabel, tanpa BYPASSRLS) dan menolak start bila role koneksinya
  istimewa atau RLS belum aktif. Tanpa ini, satu bug query bisa menyentuh data
  desa lain.
- **HTTPS mutlak.** Tanpa HTTPS, kode aktivasi & token lisensi bisa dicuri
  dengan menguping jaringan; webhook Midtrans pun butuh HTTPS.
- Webhook `POST /webhook-pembayaran` wajib HTTPS dan sudah fail-closed
  (signature SHA512 Midtrans). Jangan nonaktifkan verifikasi.
- API `/admin/*` menolak 503 bila belum ada operator terkonfigurasi, 401 untuk
  identitas/token yang salah, dan 403 bila operator tidak memiliki izin rute.
  Audit terstruktur mencatat ID operator, izin, rute, hasil, dan trace ID per
  permintaan. Endpoint rekonsiliasi mensyaratkan `payments.reconcile` karena
  membaca daftar tagihan SEMUA desa.
- Rekonsiliasi **tidak pernah** memberi hak akses atas dasar dugaan: status yang
  tidak bisa dipastikan (404 / HTTP gagal / jaringan mati) dihitung `tidakPasti`
  dan tagihannya dibiarkan. Nominal di luar tagihan dan status pembayaran yang
  belum berduit juga tidak memperpanjang — aturannya satu tempat dengan webhook.
- Rate limiting ada secara bawaan pada `/aktivasi` (20/menit/IP) dan
  `/buat-tagihan` (5/menit/IP).
- Server hanya menangani urusan langganan — tidak ada data surat desa yang lewat
  server ini.
