# Panduan Kontribusi — SuDesApp

Terima kasih telah ingin berkontribusi. SuDesApp dipakai pemerintah desa untuk
pekerjaan administrasi sehari-hari, jadi perubahan kecil pun perlu menjaga
data, hasil cetak, dan alur kerja yang sudah berjalan.

Dengan berkontribusi, Anda menyatakan menyetujui
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) dan melisensikan kontribusi Anda
dengan lisensi MIT yang sama seperti proyek ini.

## Sebelum mulai

- Cari dulu di [issue](https://github.com/inomixstudio/SuDesApp/issues) dan
  pull request yang ada agar tidak mengerjakan hal yang sama.
- Untuk perubahan besar atau desain baru, buka issue diskusi lebih dulu.
- **Jangan melaporkan kerentanan keamanan lewat issue publik.** Ikuti
  [`SECURITY.md`](SECURITY.md).
- Jangan menyertakan data warga asli, NIK, nomor KK, alamat, atau isi surat
  nyata dalam issue, commit, maupun berkas uji.

## Lingkungan pengembangan

Prasyarat:

- Windows 10/11 64-bit,
- **.NET 11 SDK 11.0.100** (dikunci `global.json`),
- untuk Android: **JDK 21** dan Android SDK,
- PowerShell 7 (`pwsh`) untuk skrip alat.

```bash
git clone https://github.com/inomixstudio/SuDesApp.git
cd SuDesApp
dotnet restore SuDesApp.slnx
```

## Build dan uji

```bash
# Seluruh solusi (Release, peringatan diperlakukan sebagai galat)
dotnet build SuDesApp.slnx --configuration Release

# Uji otomatis
dotnet test SuDesApp.Tests/SuDesApp.Tests.csproj --logger "console;verbosity=detailed"

# Uji paritas formulir Android ↔ desktop
dotnet test --filter "FullyQualifiedName~ParitasBuatSurat"

# Uji lisensi (wajib setelah menyentuh area lisensi)
dotnet test --filter "FullyQualifiedName~Licensing"

# Audit kontras tema
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/check-kontras-wcag.ps1

# Android
cd android
./gradlew assembleDebug
./gradlew testDebugUnitTest
```

Build menjalankan analyzer **SUDES0001**: `catch` kosong wajib berisi komentar
alasan di dalam bloknya, misalnya
`catch { /* berkas sementara sudah tidak ada */ }`.

## Konvensi yang wajib diikuti

### Branch dan commit

- Branch default adalah **`master`**; pull request diarahkan ke sana.
- Pesan commit berbahasa Indonesia dengan awalan konvensional:
  `feat:`, `fix:`, `refactor:`, `perf:`, `docs:`, `test:`, `build:`, `ci:`,
  `chore:`.
- Badan commit berupa **satu paragraf** yang menjelaskan alasan perubahan,
  bukan daftar ulang berkas yang disentuh.
- **Jangan menambahkan atribusi agen** seperti `Co-Authored-By` bernama
  Codebuff, teks `Generated with ...`, atau alamat `noreply@...`. Periksa
  sebelum membuka PR:

  ```bash
  bash Tools/check-atribusi-commit.sh "origin/master..HEAD"
  ```

### Rahasia dan berkas lokal

Jangan pernah meng-commit:

- `appsettings.lokal.json`, `GoogleClientCredentialsBawaan.lokal.cs`,
- kunci privat lisensi (`SuDesApp.Server/keys/`),
- kredensial Supabase, token OAuth, atau kata sandi apa pun,
- `desa.db` atau data warga nyata,
- hasil build (`bin/`, `obj/`, `artifacts/`).

Gunakan environment variable atau berkas lokal yang sudah di-ignore.

### Paritas Android ↔ desktop

Formulir dan PDF Android dibangun dari salinan skema yang sama:

- `SuDesApp.Core/Configuration/SkemaFormulir.json`
- `android/app/src/main/assets/skema-formulir.json`

Mengubah salah satu tanpa yang lain membuat hasil cetak berbeda diam-diam.
Jalankan `dotnet test --filter "FullyQualifiedName~ParitasBuatSurat"` setelah
mengubah skema, generator PDF, atau formulir. Aturan penamaan kunci dan pola
tanggal dijelaskan di [`AGENTS.md`](AGENTS.md).

Bila menambah atau memindahkan section pada form desktop, jalankan:

```bash
python Tools/turunkan-grup.py --tulis
```

Perintah itu juga memperbarui `Configuration/BidangFormDesktop.json`.

### Area terkunci

Hubungan server lisensi (`PenentuAlamatLisensi`, `ServerLisensiLokal`,
`LanggananViewModel.Tindakan.cs`, `App.Startup.cs`, dan `Lisensi:UrlServer`)
dijaga kontrak uji. Jangan mengubahnya tanpa menjalankan
`dotnet test --filter "FullyQualifiedName~Licensing"` dan menjelaskan alasannya
di commit. Bila berkas bertanda read-only, buka kuncinya lebih dulu — jangan
menimpa galat izin.

### Tampilan dan PDF

- Semua warna, ukuran huruf, dan jarak harus memakai token tema; jangan
  menulis warna langsung di XAML. Lihat
  [`docs/tema-dan-warna.md`](docs/tema-dan-warna.md).
- Perubahan tata letak PDF harus menjaga hasil cetak A4 dan F4; lihat
  [`docs/arsitektur-pdf-surat.md`](docs/arsitektur-pdf-surat.md) dan
  [`.agents/pdf-render.md`](.agents/pdf-render.md).
- Sertakan uji untuk logika baru. Uji yang menyentuh berkas atau preferensi
  statis harus dijalankan berurutan agar tidak flaky.

## Daftar periksa pull request

- [ ] Perubahan dijelaskan: masalah, pendekatan, dan dampaknya.
- [ ] `dotnet build SuDesApp.slnx --configuration Release` lolos tanpa
      peringatan.
- [ ] `dotnet test` lolos (sebutkan bila ada uji yang sengaja dilewati dan
      alasannya).
- [ ] Uji paritas Android/desktop dijalankan bila skema atau PDF berubah.
- [ ] Tidak ada rahasia, data warga asli, atau berkas build yang ikut.
- [ ] Dokumentasi diperbarui bila perilaku, konfigurasi, atau alur berubah.
- [ ] Perubahan UI mengikuti token tema dan lolos audit kontras.
- [ ] Pesan commit tidak memuat atribusi agen.

## Menambah dokumentasi

Dokumentasi kebijakan, arsitektur, dan catatan rilis berada di [`docs/`](docs).
Tambahkan tautan ke indeks [`docs/README.md`](docs/README.md) bila membuat
dokumen baru, dan tulis dalam bahasa Indonesia dengan istilah teknis yang
konsisten.

## Pertanyaan

Buka [issue](https://github.com/inomixstudio/SuDesApp/issues) dengan template
yang sesuai atau hubungi <sumberjayadev@gmail.com>. Untuk pertanyaan keamanan,
gunakan jalur privat di [`SECURITY.md`](SECURITY.md).
