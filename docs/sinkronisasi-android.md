# Sinkronisasi Android → Desktop

Aplikasi Android membuat surat di lapangan: PDF jadi dan tersimpan **di
perangkat**, sedangkan isian suratnya masuk **antrean Supabase**. Operator
desktop lalu menekan **Sinkron** di halaman *Sinkron Android* — sinkronisasi
**tidak berjalan otomatis** — untuk mengunduh antrean, mengimpornya ke SQLite,
dan mengarsipkan baris yang sudah masuk.

```
Android ──insert──▶ antrean_surat (Supabase, maks 10)
   │                    │
   └─ PDF di Downloads  Desktop ──select (tombol Sinkron)──┘
        (tidak dikirim)     ├─ tulis berkas XLSX (jejak audit)
                            ├─ impor: SuratRepository.AddSuratAsync
                            │     nomor dobel → nomor baru; NIK ada → pakai warga itu
                            ├─ status surat dinaikkan ke Active
                            ├─ catat sinkron_log
                            └─ ack → baris dihapus
```

## Yang disimpan di perangkat Android

| Data | Tempat | Ikut dikirim? |
|---|---|---|
| PDF surat | `Downloads/SuDesApp` | tidak |
| Daftar surat (register lokal) | preferensi `sudes_register` | tidak |
| Kop surat, pola nomor, logo | preferensi `sudes_surat` + `cache/kop/logo.png` | tidak |
| Skema formulir terbitan desktop | `files/skema-cache.json` (cadangan: aset APK) | tidak |
| Isian surat yang menunggu sinkron | `antrean_surat` (Supabase) atau `files/antrean-tertunda` bila gagal | ya |
| Sesi perangkat (token, surel, kata sandi perangkat) | preferensi `sudes_sesi` | tidak |

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
kata_sandi)` yang membuat akun Auth + klaim + baris perangkat, dan mengembalikan
`perangkat_android.id`. Pencabutan: `public.cabut_perangkat_android(id)`.

**Isi QR** hanya dua baris: URL proyek Supabase dan **kode 4 angka**. Surel dan
kata sandi perangkat tidak pernah tampil di layar maupun di QR; Android
menukar kode itu lewat `rpc/ambil_pasangan`, lalu masuk sendiri dengan
`grant_type=password`. Kunci publik tetap diambil dari APK.

Sambungan dibuat dengan **dua cara, pindai lebih dulu**: kamera terbuka sendiri
saat layar pasang muncul. Bila kamera tidak bisa dipakai (izin ditolak, kamera
rusak), kode 4 angka yang sama bisa diketik di kartu bawah layar. Sesudah
tersambung, perangkat langsung menarik kop, pola nomor, dan skema formulir
sebelum menu dibuka — bukan menunggu menu pertama kali dibuka.

Hanya **satu perangkat aktif per desa**. Setelah perangkat baru dipasang,
desktop memanggil `nonaktifkan_perangkat_lain(tenant, perangkat_baru)` sehingga
perangkat lama tidak lagi aktif (antreannya tetap bisa ditarik sebelum
ditinggalkan). Perangkat nonaktif juga tidak bisa menambah surat ke antrean
karena policy insert memakai `perangkat_saya_aktif()`. Migrasinya:
`20261110000001_perangkat_tunggal_dan_status_online.sql`.

Perangkat melapor lewat `rpc/tandai_perangkat_online()` (paling sering sekali
per menit). Desktop memakai `terakhir_online` untuk menampilkan chip
"Android terhubung: <nama>" di status bar — hidup sejak aplikasi dijalankan,
tanpa membuka halaman Sinkron.

Kode berlaku **10 menit**, hanya bisa dipakai sekali, dan dibatasi **10
percobaan per 15 menit per alamat IP** (`pasangan_percobaan`, migrasi
`20261110000000_batas_percobaan_pasangan.sql`). Tanpa batas itu, empat angka
hanya berarti 10.000 kemungkinan bagi siapa pun yang memegang kunci anon.

Di perangkat, rute yang ditempuh: `LoginActivity` memindai QR (atau menerima
kode yang diketik) → ambil URL dari QR bila ada (menimpa nilai di APK) dan
kode → `ambil_pasangan` → login → simpan sesi → tarik kop, pola nomor, dan
skema formulir → lapor online.

> **Catatan:** `auth.users` memerlukan kolom token (`confirmation_token`,
> `recovery_token`, `email_change_token_*`, `reauthentication_token`,
> `phone_change_token`, `email_change`) bernilai **string kosong**, bukan
> `NULL` — GoTrue menolak login kalau `NULL`. Lihat supabase/auth#1940.

## Percobaan ulang dan batas antrean

Impor satu baris bisa gagal (isian tidak lengkap, NIK tidak valid). Aturannya:

1. Baris `menunggu` selalu diimpor.
2. Baris `gagal` **dicoba lagi** pada putaran sinkron berikutnya selama
   `batas_coba` belum mencapai 3 (`SinkronSuratService.PerluDiimpor`).
3. Pada kegagalan ketiga, alasan ditulis ke `sinkron_log`, lalu baris
   **dihapus** supaya tidak menempati slot batas 10 selamanya.
4. Operator dapat mengirim ulang surat dari Android; baris baru akan dibuat.

Karena jumlah baris dibatasi 10 per desa (trigger `batasi_antrean_surat`),
langkah 3 penting: tanpa itu sepuluh baris gagal membuat perangkat berhenti
bisa mengirim.

## Bila jaringan gagal di perangkat

Isian yang gagal masuk antrean **tidak dibuang**. Android menyimpannya di
`files/antrean-tertunda/` dan mencoba lagi setiap menu utama dibuka atau saat
operator menekan *Kirim surat tertunda* di halaman Pengaturan. Halaman itu juga
menampilkan jumlah surat yang menunggu di Supabase dan yang masih tertunda di
perangkat.

## Nomor surat

Nomor Android hanyalah **placeholder** di PDF draf. Nomor resmi diberikan
desktop saat impor (`AddSuratAsync` membangkitkan ulang nomor yang kosong atau
sudah dipakai). PDF resmi dibuat desktop dari register, sehingga bisa dicetak
ulang. Karena desktop yang menomori, nomor dobel antar-perangkat bukan masalah.

## Skema formulir dan kop

Desktop adalah sumber tunggal dua hal ini; Android tidak mengetik ulang:

| Terbitan desktop | Tabel | Dipakai Android |
|---|---|---|
| Kop, logo, pola nomor, `urutTerakhir` | `pengaturan_desa` | ditarik saat login, saat menu dibuka, dan saat halaman Pengaturan dibuka |
| Definisi formulir per jenis surat | `skema_formulir` | ditarik saat tarikan yang sama, disimpan di `files/skema-cache.json` |

Skema lokal itu yang membuat perubahan formulir di desktop berlaku tanpa APK
baru; bila belum pernah ditarik (atau salinannya rusak), Android memakai
`assets/skema-formulir.json` yang ikut di APK.

## Implikasi privasi

Antrean Supabase memuat data penduduk (NIK, nama, alamat). Pernyataan "data
tetap di desktop" karena itu diberi pengecualian eksplisit di
`docs/kebijakan-privasi.md` bagian 6.7 dan `docs/syarat-ketentuan.md` bagian 7,
termasuk cara mematikannya (cabut perangkat) dan batas penyimpanannya.

## Modul desktop (WPF/Core)

| Berkas | Peran |
|---|---|
| `SuDesApp.Core/Supabase/SupabasePengaturan.cs` | Koneksi Supabase: preferensi operator, lalu `Supabase:*` di appsettings; kata sandi lewat DPAPI |
| `SuDesApp.Core/Sinkron/SkemaFormulir.cs` | Skema formulir + pemetaan isian → `SuratData` |
| `SuDesApp.Core/Sinkron/ImporAntreanSurat.cs` | Satu baris antrean → register (nomor & NIK dinormalkan) |
| `SuDesApp.Core/Sinkron/SinkronSuratService.cs` | Satu putaran: tarik → `.xlsx` → impor → ack |
| `SuDesApp.Core/Sinkron/PerangkaianPerangkat.cs` | Buat/cabut perangkat + kata sandi acak |
| `SuDesApp.Wpf/Views/SinkronView.xaml` | Halaman Sinkron Android (menu PENGATURAN) |

Alur terverifikasi end-to-end: dua surat Android dengan placeholder nomor
yang sama diimpor; nomor pertama dipertahankan, nomor kedua dibangkitkan ulang
(`470/001/Ds/2026`), NIK berbeda menjadi dua warga, status **Active**, antrean
kosong, jejak `sinkron_log` terisi.

Catatan: `AddSuratAsync` menulis status lewat query `InsertSurat` yang tidak
memuat kolom `Status`, jadi surat baru mendarat sebagai `Draft`. Impor
menaikkan statusnya ke `Active` lewat `UpdateStatusAsync` — surat Android
dianggap sudah terbit.
