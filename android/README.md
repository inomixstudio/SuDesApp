# SuDesApp Desa (Android)

Aplikasi Android untuk operator desa yang sedang di lapangan.

| Menu | Peran |
|---|---|
| **Buat Surat** | Formulir dinamis untuk surat biasa; hasilnya PDF |
| **Buat NTCR** | Formulir persyaratan pernikahan N1–N6 dan N8; hasilnya PDF |
| **Register Surat** | Daftar PDF yang pernah dibuat di perangkat ini, bisa dicetak ulang atau dibagikan |
| **Daftar Hadir** | Buat + cetak PDF daftar hadir, tidak disimpan |
| **Pengaturan** | Salinan kop surat & penomoran dari desktop, status sinkronisasi, kirim surat tertunda, keluar perangkat |
| **Sinkron** | Pasang perangkat ke Supabase (pindai QR dari desktop) |

## Alur surat

1. Formulir diisi → **Cetak & Simpan PDF**.
2. PDF langsung ditawarkan ke dialog cetak Android.
3. Salinan PDF tersimpan di `Downloads/SuDesApp` (Android 11+) atau folder
   `Download/SuDesApp` (Android 10 ke bawah).
4. Isian surat ikut ke **antrean Supabase** — desktop mengimpornya ke register
   dan memberikan nomor resmi (nomor Android hanya placeholder, boleh dobel).

Bila perangkat belum dipasang atau jaringan gagal, langkah 4 tidak hilang:
isian disimpan di `files/antrean-tertunda/`, dicoba lagi setiap menu utama
dibuka, dan bisa dikirim manual lewat tombol **Kirim surat tertunda** di
halaman Pengaturan. PDF tetap tersimpan, jadi surat tidak pernah hilang.

## Bentuk PDF sama dengan desktop

`SuratPdf.kt` memakai angka yang sama dengan sisi desktop:

| Aspek | Sumber di desktop |
|---|---|
| Margin kiri/kanan 45 pt, margin atas/bawah & kerapatan | `TataLetakHalamanSurat`, `KerapatanSurat` |
| Ukuran kertas A4 (595,28 × 841,89 pt) / F4 (607,5 × 935,4 pt) | `PengaturanCetak` |
| Kop 5 baris + garis ganda 0,8 pt / 1,5 pt | `KopSurat` |
| Judul 14 pt tebal bergaris bawah + `NOMOR : …` | `SuratRenderer.JudulDanNomor` |
| Tabel isian rasio kolom 160 : 10 : 340 | `SuratRenderer.TabelFormulir` |
| Blok tanda tangan dua kolom | `SuratRenderer.TandaTangan` |
| Font Times New Roman | `Resources/Fonts/times*.ttf` (ikut di `assets/fonts/`) |

Isi badan surat tiap jenis dibaca dari `badan` pada skema formulir
— blok `paragraf`, `tabel`, `paragrafCampur`, `daftar`, dan `kustom` — supaya
teks dan urutannya identik dengan generator desktop.

## Skema formulir dan kop

Desktop adalah sumber tunggal. Saat perangkat menarik pengaturan (login, menu
utama dibuka, atau tombol *Sinkronkan sekarang*), Android juga menarik
`skema_formulir` dan menyimpannya di `files/skema-cache.json`. Salinan itu
dipakai lebih dulu; bila belum ada atau rusak, aplikasi memakai
`assets/skema-formulir.json` yang ikut di APK. Jadi perubahan formulir di
desktop berlaku tanpa memasang APK baru.

## Membangun dan menjalankan

Toolchain: JDK 17/21, Android SDK (compileSdk 35). Gradle **memakai wrapper**
di folder ini, jadi tidak perlu Gradle global:

```
cd android
./gradlew assembleDebug        # Windows: gradlew.bat assembleDebug
```

`local.properties` berisi `sdk.dir` per mesin — sengaja tidak di-commit.

Kunci Supabase dibaca dari `supabase.properties` (juga tidak di-commit) saat
build:

```properties
SUPABASE_URL=https://<proyek>.supabase.co
SUPABASE_ANON_KEY=<kunci publik>
```

Bila berkas itu tidak ada, build tetap berhasil tetapi aplikasi tidak bisa
memasang perangkat (URL/kunci kosong). Untuk rilis, isi nilainya dari
Supabase Dashboard → *Project Settings* → *API*.

Memasang ke emulator/HP yang terhubung:

```
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n dev.sumberjaya.sudes.android/.LoginActivity
```

## Struktur

```
app/src/main/
  assets/
    skema-formulir.json   definisi formulir + tata letak badan (salinan dari SuDesApp.Core/Configuration)
    fonts/times*.ttf      Times New Roman — sama dengan yang dipakai desktop
  java/dev/sumberjaya/sudes/android/
    LoginActivity.kt      pasang perangkat (QR: URL + kode 4 angka)
    MainActivity.kt       menu utama, tarik pengaturan, kirim surat tertunda
    Ui.kt                 pembantu dp + inset status/navigation bar
    BuatSuratActivity.kt  formulir dinamis → PDF → cetak + simpan + antre
    SuratPdf.kt           tata letak PDF (meniru desktop), termasuk Kop dan FontSet
    AlamatSusun.kt        alamat lengkap penduduk (meniru AlamatFormatter)
    MuatLogo.kt           pemuat logo kop dari berkas
    AntreanSurat.kt       kirim isian ke antrean Supabase + simpan surat tertunda
    AntreanBaris.kt       model baris antrean untuk tampilan status
    SupabaseKlien.kt      klien Auth + PostgREST (tanpa SDK)
    SupabaseSesi.kt       sesi perangkat (token, surel, kata sandi perangkat)
    PdfAksi.kt            simpan ke Downloads, cetak, bagikan
    DaftarSurat.kt        daftar PDF tersimpan
    RegisterSuratActivity.kt  register PDF di perangkat
    DaftarHadirActivity.kt    PDF daftar hadir + cetak
    PengaturanActivity.kt     kop surat, status sinkron, kirim tertunda, keluar
    PengaturanSurat.kt        identitas kop & nomor urut
    PengaturanSinkron.kt      tarik kop, pola nomor, dan skema dari desktop
    SkemaFormulir.kt      model skema + pemuat lokal (cache desktop / aset APK)
```

Alur sinkronisasi lengkap (pairing, RLS, batas 10 surat, percobaan ulang, dan
implikasi privasi) ada di [`../docs/sinkronisasi-android.md`](../docs/sinkronisasi-android.md).
