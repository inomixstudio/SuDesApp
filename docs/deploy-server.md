# Deploy server lisensi SuDesApp

Server lisensi hanya menjadi tempat pembayaran/langganan — data desa tetap di
SQLite lokal di komputer desa. Server bisa dihosting di layanan murah (Railway,
Render, Fly.io) atau VPS. Prasyarat mutlak: **HTTPS**.

## Apa yang di-deploy

`SuDesApp.Server` (minimal API .NET 11) + Supabase Postgres.
Tidak ada file statis aplikasi desktop di sini; hanya endpoint API + halaman
admin `/admin`.

## Kredensial yang wajib disiapkan

| Kredensial | Dari mana | Dipakai untuk |
|---|---|---|
| `SUPABASE_HOST` + `SUPABASE_DB_USER` (`sudes_server.<project-ref>`) + `SUPABASE_DB_PASSWORD` | Role dibuat oleh `schema.sql`; kata sandinya Anda setel sekali (`ALTER ROLE ...`) | Koneksi aplikasi — **RLS berlaku di sini** |
| `SUPABASE_MIGRATE_PASSWORD` (opsional) | Project settings → Database → Connection string | Hanya untuk menerapkan `schema.sql` saat start |
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

Paling mudah: nyalakan pilihan **Jalankan server lisensi di komputer ini** di
halaman Langganan — aplikasi yang menyalakan, memakai, dan mematikannya
(lihat [langganan-dan-lisensi.md](langganan-dan-lisensi.md#server-lisensi-lokal-yang-dijalankan-aplikasi)).
Bila perlu menjalankannya sendiri (debugging), profil Visual Studio sudah memakai
`http://127.0.0.1:5110` dengan `ASPNETCORE_ENVIRONMENT=Development` — atau dari
terminal:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5110 \
  dotnet run --project SuDesApp.Server --no-launch-profile
```

`Development` tidak lagi wajib di sini: sejak gateway pembayaran dihapus, server
melayan tanpa kredensial pembayaran apa pun. Aktivasi, trial, katalog `/paket`,
dan pengumuman berjalan sungguhan terhadap database.

Uji cepat dari terminal — kedua baris ini harus menjawab sebelum aplikasi
desktop dibuka:

```bash
curl http://127.0.0.1:5110/        # {"status":"SuDesApp lisensi server", ...}
curl http://127.0.0.1:5110/paket   # [{"kode":"bulanan", ...}]
```

Bila `/` menjawab tetapi `/paket` tidak, masalahnya di sisi server (bukan di
aplikasi). Bila keduanya kosong, server belum berjalan: periksa log startup —
di luar Development server sengaja berhenti, bukan diam-diam melayani, bila
kredensial Supabase kosong atau RLS tidak terverifikasi. Di aplikasi, buka
**Langganan → Server Lisensi**, isi
`http://127.0.0.1:5110`, tekan **Simpan & Uji** sampai chip berbunyi
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

Buka `http://127.0.0.1:5110/admin`, lalu masuk dengan ID/token di atas — atau
tekan **Buka Pusat Administrasi** di halaman Langganan, yang membuka alamat yang
sama di dalam aplikasi. Aplikasi desa yang menunjuk `http://127.0.0.1:5110` akan
melihat chip **Terhubung**.

Tanpa `SUPABASE_MIGRATE_PASSWORD`, server tidak mengubah skema sama sekali;
jalankan `schema.sql` sekali di SQL Editor (lihat bagian berikut).

### Pusat Administrasi (kuota perangkat & riwayat pembayaran)

Untuk membebaskan kuota perangkat desa (aktivasi ditolak karena penuh — mis.
pelanggan mengganti komputer), buka **`https://<host>/admin`** di peramban
(masukkan ID dan token pribadi operator sekali per tab). Halaman itu juga bisa
mendaftarkan, mengubah, dan menghapus desa, mencatat pembayaran manual
(QRIS statis/transfer) beserta lampiran bukti transfernya, menampilkan riwayat
pembayaran per desa (dapat diunduh CSV), dan mencetak kuitansi. Setara lewat
`/admin/pelanggan*`, `/admin/ekspor/*.csv`, atau `Tools/Admin-Lisensi.ps1`. Lihat
[langganan-dan-lisensi.md](langganan-dan-lisensi.md#pusat-administer-kelola-desa--perangkat).

Pembayaran TIDAK berjalan sendiri: tidak ada notifikasi otomatis dari gateway
mana pun. Hak akses hanya bertambah kalau operator menekan "Tandai lunas" di
halaman itu, jadi alur deploy tidak perlu lagi mendaftarkan URL callback ke
penyedia pembayaran apa pun.

### Verifikasi menyeluruh setelah deploy

Setelah server hidup dengan Supabase sungguhan,
`Tools/uji-e2e-server-lisensi.sh` menjalankan seluruh pemeriksaan lewat HTTP —
endpoint publik, gerbang token & izin admin, data desa, aktivasi + tanda tangan
token, batas perangkat, `/buat-tagihan` (dipastikan tidak lagi menghasilkan medan
gateway), pembukuan manual & bukti transfer, ekspor, pengumuman — lalu menghapus
jejak ujinya (desa uji dan pengumuman uji).

```bash
BASE_URL=https://lisensi.example.com OPERATOR_TOKEN=... \
  Tools/uji-e2e-server-lisensi.sh
```

Skrip itu **idempoten**: kode desa ujinya tetap, sisa run sebelumnya dibersihkan
lebih dulu, pembersihan akhir berjalan walau uji gagal, dan jumlah desa setelah
putaran kembali seperti semula. Tagihan dan pembayaran yang dibuat uji ikut
terhapus bersama desanya. Butuh izin operator yang lengkap; lihat header
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
`dir_cari_tagihan(order_id)` (dipakai pembukuan manual operator untuk mengaitkan
order dengan desa). Keduanya `SECURITY DEFINER`, `EXECUTE`-nya dicabut dari PUBLIC
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
5. Catat `LISENSI_KEY_PATH` yang dibuat server (isi volume `/lisensi/keys`).

### Opsi B — Render

1. New → Web Service → pilih repo, bahasa `Docker`.
2. Set env vars (di atas) + `LISENSI_KEY_PATH=/lisensi/keys/lisensi-signing.key`.
3. Add a persisted Disk at `/lisensi` (Render "Disks" feature).
4. Domain: Render memberi `*.onrender.com` HTTPS.

### Opsi C — VPS

1. Build image: `docker build -t sudesapp-server .`
2. Jalankan dengan volume:

   ```bash
   docker run -d --restart=unless-stopped \
     -p 127.0.0.1:8080:8080 \
     -e ASPNETCORE_ENVIRONMENT=Production \
     -e SUPABASE_HOST=... \
     -e SUPABASE_DB_USER=sudes_server.<project-ref> -e SUPABASE_DB_PASSWORD=... \
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
   ke `localhost:8080`. Aplikasi desktop dan pelanggan juga wajib memakai HTTPS.

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
6. **Saat memperbarui server ini**: jalankan ulang `schema.sql` (aman diulang)
   atau set `SUPABASE_MIGRATE_PASSWORD` supaya server menerapkannya sendiri.

## Catatan keamanan

- **Role aplikasi terbatas + RLS.** Server memakai `sudes_server` (bukan
  pemilik tabel, tanpa BYPASSRLS) dan menolak start bila role koneksinya
  istimewa atau RLS belum aktif. Tanpa ini, satu bug query bisa menyentuh data
  desa lain.
- **HTTPS mutlak.** Tanpa HTTPS, kode aktivasi & token lisensi bisa dicuri
  dengan menguping jaringan.
- API `/admin/*` menolak 503 bila belum ada operator terkonfigurasi, 401 untuk
  identitas/token yang salah, dan 403 bila operator tidak memiliki izin rute.
  Audit terstruktur mencatat ID operator, izin, rute, hasil, dan trace ID per
  permintaan.
- Rate limiting ada secara bawaan pada `/aktivasi` (20/menit/IP) dan
  `/buat-tagihan` (5/menit/IP).
- Server hanya menangani urusan langganan — tidak ada data surat desa yang lewat
  server ini.
