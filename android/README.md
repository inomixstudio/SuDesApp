# SuDesApp Desa (Android)

Aplikasi Android untuk operator desa yang sedang di lapangan. Tiga peran:

| Menu | Peran |
|---|---|
| **Buat Surat** | Formulir dinamis untuk 20 jenis surat biasa; hasilnya PDF |
| **Buat NTCR** | Formulir persyaratan pernikahan N1–N6 dan N8; hasilnya PDF |
| **Register Surat** | Daftar PDF yang pernah dibuat di perangkat ini, bisa dicetak ulang atau dibagikan |
| **Daftar Hadir** | Buat + cetak PDF daftar hadir, tidak disimpan |

## Tidak mengirim data ke mana pun

Aplikasi ini **tidak punya akun, tidak butuh internet, dan tidak mengirim data ke
layanan mana pun**. Isian surat hanya dijadikan PDF di perangkat:

1. Formulir diisi → **Cetak & Simpan PDF**.
2. PDF langsung ditawarkan ke dialog cetak Android.
3. Salinan PDF tersimpan di `Downloads/SuDesApp` (Android 11+) atau folder
   `Download/SuDesApp` (Android 10 ke bawah).

Surat berpindah ke register desktop lewat PDF yang dicetak — sama seperti
surat yang dibuat di desktop lalu di-scan. Data warga tidak meninggalkan
perangkat.

Konsekuensinya: definisi formulir ikut di dalam APK (`assets/skema-formulir.json`),
jadi bila skema di desktop berubah, APK harus dibangun ulang.

## Kop surat dan penomoran

Menu **Pengaturan** mengatur:

- Nama desa, kecamatan, kabupaten → jadi kop tiga baris.
- Awalan nomor (mis. `470`) dan nomor urut terakhir → dilanjutkan dari angka
  itu, format `470/001/Ds/2026`. Samakan angka terakhir dengan register
  desktop supaya nomor tidak lompat.

Semua nilai ini hanya disimpan di perangkat.

## Membangun

Toolchain: JDK 17+/21, Android SDK (compileSdk 35), Gradle 8.11+.

```
gradle assembleDebug
```

`local.properties` berisi `sdk.dir` per mesin — sengaja tidak di-commit.

## Struktur

```
app/src/main/
  assets/skema-formulir.json   definisi formulir (sumber: SuDesApp.Core/Configuration)
  java/dev/sumberjaya/sudes/android/
    MainActivity.kt        menu utama (Buat Surat / Buat NTCR / Register / Daftar Hadir / Pengaturan)
    BuatSuratActivity.kt   formulir dinamis → PDF → cetak + simpan
    SuratPdf.kt            tata letak PDF surat
    PdfAksi.kt             simpan ke Downloads, cetak, bagikan
    DaftarSurat.kt         daftar PDF tersimpan
    RegisterSuratActivity.kt  register PDF di perangkat
    DaftarHadirActivity.kt    PDF daftar hadir + cetak
    PengaturanActivity.kt     kop surat + penomoran
    PengaturanSurat.kt        identitas kop & nomor urut
    SkemaFormulir.kt       model + pemuat skema lokal
```
