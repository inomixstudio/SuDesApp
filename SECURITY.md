# Keamanan — SuDesApp

## Melaporkan kerentanan

Laporkan lewat **GitHub Security Advisories** (tab Security → Report a
vulnerability), bukan issue publik. Sertakan:

- versi SuDesApp dan versi Windows,
- langkah reproduksi,
- dampak yang kamu perkirakan (mis. data mana yang bisa dibaca atau diubah).

Kami akan membalas dalam 7 hari kerja.

## Jangan pernah melakukan ini

Beberapa hal terlihat seperti pintasan tetapi merusak hak pengguna. Kalau
temuan Anda mengarah ke salah satunya, laporkan, jangan exploited:

- **Jangan pernah meng-commit berkas rahasia apa pun.** Kredensial OAuth
  Google, kredensial Supabase, dan kunci privat penandatangan lisensi berada di
  daftar ignore repo. Kalau bocor, saya rotasi — jadi laporkan agar bisa
  dirotasi sebelum disalahgunakan.
- **Jangan pernah mengakses `desa.db` milik orang lain.** Data desa disimpan
  terenkripsi di komputer masing-masing pengguna; tidak ada yang tersimpan di
  server kecuali masa aktif langganan.
- **Jangan menyalahgunakan endpoint admin.** Endpoint `/admin/*` dilindungi
  kredensial operator dan izin terbatas, hanya untuk operator.

## Ringkasan desain keamanan

Bagian ini menjelaskan keputusan yang sudah diambil, supaya laporan Anda
menjumpai jawaban yang sudah benar, sehingga Anda bisa fokus pada celah
yang belum tertutup.

### Verifikasi tanda tangan — selalu gagal tertutup

- **Webhook Midtrans** (`SuDesApp.Server/Services/MidtransService.cs`)
  merekonstruksi `SHA512(order_id + status_code + gross_amount + ServerKey)`
  dan membandingkannya dengan waktu-tetap. Tanpa `ServerKey`, server
  **menolak start** di luar mode Development.
- **Webhook WhatsApp Meta** (`SuDesApp.Core/WhatsApp/MetaWebhookListener.cs`)
  memverifikasi `X-Hub-Signature-256` dengan `HMAC-SHA256`. Bila app secret
  kosong, listener **menolak semua request dengan 503** — bukan meneruskannya.
  (Sebelumnya jalur ini hanya memverifikasi bila secret terisi, sehingga
  praktis selalu terbuka.)

### Kunci lisensi

- Penandatanganan memakai ECDSA P-256.
- Kunci privat hanya ada di server dan **tidak pernah** keluar dari sana.
- Kunci publik ditanam ke aplikasi sebagai akar kepercayaan
  (`Lisensi:KunciPublik` di `appsettings.json`) dan diisi **sebelum rilis**.
  Kalau kosong, aplikasi melakukan *trust on first use* — lihat
  `docs/deploy-server.md` bagian "Kunci penandatangan".
- Server menolak memakai `appsettings.lokal.json` untuk menetapkan
  `Lisensi:KeyPath`: environment variable menang, supaya kunci bisa dipetakan
  ke volume persisten dan tidak berubah tiap container restart.

### Rahasia

| Rahasia | Lokasi | Dibuat |
|---|---|---|
| Kredensial OAuth Google | `GoogleClientCredentialsBawaan.lokal.cs` (terlokal, tidak terlacak) + DPAPI di `%LOCALAPPDATA%` |saat build lokal, atau diisi teknisi lewat Pengaturan → Kredensial Google |
| Kredensial Supabase | `appsettings.lokal.json` (terlokal, tidak terlacak) atau environment variable | saat deploy |
| Kunci privat lisensi | `SuDesApp.Server/keys/` (terlokal, tidak terlacak) atau env `LISENSI_KEY_PATH` | saat deploy |

Berkas `appsettings.lokal.json` sengaja dikecualikan dari output publish
(`<Content Remove>` di `.csproj`) dan dari build context Docker
(`.dockerignore`), supaya kredensial tidak ikut terbawa ke dalam image.

### Kata sandi pengguna

PBKDF2-HMAC-SHA256, 120.000 iterasi, salt acak 16 byte per akun, jumlah
iterasi ikut disimpan per akun (agar bisa menaikkan biaya di masa depan tanpa
membuat akun lama tidak terbaca), dan perbandingan memakai
`CryptographicOperations.FixedTimeEquals`.

### Data desa

Database desa (`desa.db`) terenkripsi dan **tidak pernah** dikirim ke server.
Server lisensi hanya menyimpan masa aktif, nama desa, dan kontak — cukup untuk
menagih langganan. Lihat `docs/langganan-dan-lisensi.md`.
