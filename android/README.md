# SuDesApp Desa (Android)

Aplikasi Android untuk operator desa yang sedang di lapangan.

| Menu | Peran |
|---|---|
| **Buat Surat** | Formulir dinamis untuk 20 jenis surat biasa; hasilnya PDF |
| **Buat NTCR** | Formulir persyaratan pernikahan N1–N6 dan N8; hasilnya PDF |
| **Register Surat** | Daftar PDF yang pernah dibuat di perangkat ini, bisa dicetak ulang atau dibagikan |
| **Daftar Hadir** | Buat + cetak PDF daftar hadir, tidak disimpan |
| **Pengaturan** | Identitas kop surat (seperti Pengaturan Surat di desktop) + penomoran |
| **Sinkron** | Pasang perangkat ke Supabase (pindai QR dari desktop atau ketik manual) |

## Alur surat

1. Formulir diisi → **Cetak & Simpan PDF**.
2. PDF langsung ditawarkan ke dialog cetak Android.
3. Salinan PDF tersimpan di `Downloads/SuDesApp` (Android 11+) atau folder
   `Download/SuDesApp` (Android 10 ke bawah).
4. Isian surat ikut ke **antrean Supabase** — desktop mengimpornya ke register
   dan memberikan nomor resmi (nomor Android hanya placeholder, boleh dobel).

Bila perangkat belum dipasang atau jaringan gagal, langkah 4 dilewati; PDF
tetap tersimpan, jadi surat tidak hilang.

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

Isi badan surat tiap jenis dibaca dari `badan` pada `assets/skema-formulir.json`
— blok `paragraf`, `tabel`, `paragrafCampur`, `daftar`, dan `kustom` — supaya
teks dan urutannya identik dengan generator desktop.

## Membangun

Toolchain: JDK 17+/21, Android SDK (compileSdk 35), Gradle 8.11+.

```
gradle assembleDebug
```

`local.properties` berisi `sdk.dir` per mesin — sengaja tidak di-commit.

## Struktur

```
app/src/main/
  assets/
    skema-formulir.json   definisi formulir + tata letak badan (sumber: SuDesApp.Core/Configuration)
    fonts/times*.ttf      Times New Roman — sama dengan yang dipakai desktop
  java/dev/sumberjaya/sudes/android/
    MainActivity.kt        menu utama
    LoginActivity.kt       pasang perangkat (QR / manual)
    BuatSuratActivity.kt   formulir dinamis → PDF → cetak + simpan + antre
    SuratPdf.kt            tata letak PDF (meniru desktop)
    Kop.kt                 isi baris kop & jabatan pejabat
    AlamatSusun.kt         alamat lengkap penduduk (meniru AlamatFormatter)
    FontSet.kt             pemuat Times New Roman
    AntreanSurat.kt        kirim isian surat ke antrean Supabase
    SupabaseKlien.kt       klien Auth + PostgREST
    SupabaseSesi.kt        sesi perangkat
    PdfAksi.kt             simpan ke Downloads, cetak, bagikan
    DaftarSurat.kt         daftar PDF tersimpan
    RegisterSuratActivity.kt  register PDF di perangkat
    DaftarHadirActivity.kt    PDF daftar hadir + cetak
    PengaturanActivity.kt     kop surat + penomoran
    PengaturanSurat.kt        identitas kop & nomor urut
    SkemaFormulir.kt       model + pemuat skema lokal
```
