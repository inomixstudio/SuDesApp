# Sinkronisasi Android → Desktop

Aplikasi Android (**klien tipis**, online-only, tidak menyimpan data) membuat
surat saat aplikasi desktop mati. Surat masuk **antrean Supabase**. Saat
desktop menyala, layanan sinkron mengunduh antrean, mengimpornya ke SQLite,
dan mengarsipkan baris yang sudah masuk.

```
Android ──insert──▶ antrean_surat (Supabase, maks 10)
                        │
Desktop ──select───────┘
   ├─ tulis berkas XLSX (jejak audit)
   ├─ impor: SuratRepository.AddSuratAsync
   │     nomor dobel → nomor baru; NIK ada → ambil data warga
   ├─ buat PDF resmi (SudesPdf)
   ├─ catat sinkron_log
   └─ ack → baris dihapus (retensi 7 hari)
```

## Bentang Supabase

Proyek: `jotnrhjzyslyaozzawwq` (region `ap-northeast-2`). Kredensial **tidak**
disimpan di repo; lihat `SuDesApp.Server/appsettings.lokal.json`.

Migrasi: `supabase/migrations/20261107000000_skema_sinkron.sql` (idempoten).

### Tabel

| Tabel | Isi |
|---|---|
| `perangkat_android` | Perangkat yang dipasangkan desktop, tertaut ke `auth.users` |
| `antrean_surat` | Surat menunggu sinkron. `data_json` memuat `{"tanggal", "fields"}` |
| `sinkron_log` | Jejak impor per baris antrean (idempotensi) |
| `skema_formulir` | Definisi bidang formulir per jenis surat, diterbitkan desktop |

`tenant_id` sama dengan `pelanggan.id` pada skema lisensi.

### Identitas dan RLS

Android memakai Supabase Auth (email/kata sandi) dengan klaim `app_metadata`:

| Klaim | Arti |
|---|---|
| `tenant_id` | id desa |
| `id_pengguna` | id pengguna desktop |
| `id_perangkat` | `perangkat_android.id` |
| `peran` | `android` |

Desktop masuk sebagai role `postgres` (pemilik tabel), jadi RLS tidak
membatasinya. Android dibatasi RLS:

* `antrean_surat` — boleh `insert`/`select` hanya untuk baris yang
  `tenant_id` dan `id_perangkat`-nya cocok dengan JWT.
* `perangkat_android` — boleh `select` hanya perangkatnya sendiri.
* `skema_formulir` — boleh `select` untuk `tenant_id`-nya.

### Batas 10 surat

Trigger `trg_batasi_antrean_surat` menolak `insert` ke-11 per `tenant_id`
(`security definer`, supaya tidak bisa diloloskan klien). Pesan:
`Antrean sinkronisasi penuh (10 surat). Sinkronkan aplikasi desktop dulu.`

### Pairing

Desktop memanggil `public.pasang_perangkat_android(tenant, pengguna, nama,
kata_sandi)` yang membuat akum Auth + klaim + baris perangkat, dan mengembalikan
`perangkat_android.id`. QR memuat URL Supabase, email, dan kata sandi perangkat.
Pencabutan: `public.cabut_perangkat_android(id)`.

> **Catatan:** `auth.users` memerlukan kolom token (`confirmation_token`,
> `recovery_token`, `email_change_token_*`, `reauthentication_token`,
> `phone_change_token`, `email_change`) bernilai **string kosong**, bukan
> `NULL` — GoTrue menolak login kalau `NULL`. Lihat supabase/auth#1940.

## Nomor surat

Nomor Android hanyalah **placeholder** di PDF draf. Nomor resmi diberikan
desktop saat impor (`AddSuratAsync` membangkitkan ulang nomor yang kosong atau
sudah dipakai). PDF resmi dibuat desktop dari register, sehingga bisa dicetak
ulang.

## Implikasi privasi

Antrean Supabase memuat data penduduk (NIK, nama, alamat). Ini mengubah
pernyataan "data tetap di desktop" pada `docs/kebijakan-privasi.md` dan
`docs/syarat-ketentuan.md` — keduanya **harus diperbarui** sebelum fitur ini
dipakai.
