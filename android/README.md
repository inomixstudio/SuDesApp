# SuDesApp Android

Aplikasi Android untuk operator desa yang sedang di lapangan. Tiga menu:

| Menu | Peran |
|---|---|
| **Buat Surat Baru** | Formulir dinamis dari skema yang diterbitkan desktop; isian masuk antrean Supabase |
| **Register Surat** | Daftar surat yang dibuat di perangkat ini + status sinkron |
| **Daftar Hadir** | Buat + cetak PDF, **tidak** disimpan |

Aplikasi ini **online-only** dan **tidak menyimpan data di perangkat** — hanya
identitas perangkat (surel, kata sandi, token Supabase) yang disimpan untuk
membuktikan diri ke Supabase.

## Menghubungkan ke Supabase

1. Di aplikasi desktop, buka **Pengaturan → Sinkron Android**.
2. Simpan koneksi (host pooler, tenant, kata sandi DB) dan terbitkan skema formulir.
3. Pasang perangkat: masukkan nama, lalu pindai QR yang muncul.
4. QR berisi URL Supabase, kunci publik, surel, dan kata sandi perangkat.

Bila QR tidak bisa dipindai, ketik manual keempat nilai tersebut di halaman
yang sama.

## Sinkronisasi

Surat yang dibuat di Android masuk tabel `antrean_surat` (maks 10). Saat
aplikasi desktop menjalankan sinkronisasi, surat diimpor ke register desktop —
nomor dobel dibangkitkan ulang, NIK yang sudah ada memakai data warga yang ada —
lalu baris antrean dihapus dan jejaknya masuk `sinkron_log`.

PDF yang dicetak dari Android adalah **draf**; PDF resmi dibuat desktop dari
register (bisa dicetak ulang).

## Membangun

Toolchain: JDK 17+/21, Android SDK (compileSdk 35), Gradle 8.11+.

```
gradle assembleDebug
```

`local.properties` berisi `sdk.dir` per mesin — sengaja tidak di-commit.

## Struktur

```
app/src/main/java/dev/sumberjaya/sudes/android/
  LoginActivity.kt      pasang perangkat (QR / manual)
  MainActivity.kt       menu utama + status antrean
  BuatSuratActivity.kt  formulir dinamis + kirim antrean
  RegisterSuratActivity.kt  daftar antrean milik perangkat
  DaftarHadirActivity.kt    PDF daftar hadir + cetak
  SupabaseKlien.kt      REST client (Auth + PostgREST)
  SupabaseSesi.kt       identitas perangkat
  SkemaFormulir.kt      model skema dari desktop
```
