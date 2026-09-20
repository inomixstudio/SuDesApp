# Pembaruan Aplikasi: Kecil (Tambalan) vs Besar (Installer)

Panduan singkat untuk **pemelihara/pengembang** SuDesApp. Sisi pengguna dijelaskan di
menu **Pembaruan** dan halaman **Catatan Rilis** di dalam aplikasi.

## Ringkasan

| | Pembaruan kecil (tambalan) | Pembaruan besar (installer) |
|---|---|---|
| Isi | hanya berkas yang berubah | seluruh aplikasi + runtime .NET |
| Ukuran unduhan | ratusan KB – beberapa MB | ± 100 MB |
| Cara pasang | berkas diganti otomatis, aplikasi dibuka ulang | installer Inno Setup dijalankan |
| Yang bisa diubah | surat, teks, tampilan, logika aplikasi | apa pun (termasuk dependensi/runtime baru) |
| Syarat | versi terpasang = versi asal tambalan (`dariVersi`) | selalu bisa |

Aplikasi memilih sendiri: bila rilis menyertakan `patch.json`, versi terpasang cocok,
dan folder aplikasi bisa ditulis → pembaruan kecil. Selain itu → installer penuh.

## Alur otomatis (GitHub Actions)

1. Naikkan versi di `SuDesApp.Wpf/SuDesApp.Wpf.csproj`, `CatatanRilisViewModel`,
   halaman **Tentang**, dan footer login (kebiasaan proyek ini).
2. Commit, lalu buat tag: `git tag v2.5.1 && git push origin v2.5.1`
   (atau jalankan workflow **Release** secara manual dengan mengisi versi).
3. Workflow `.github/workflows/release.yml` akan:
   - `dotnet publish` self-contained win-x64;
   - membuat zip portable + installer Inno Setup;
   - mengunduh zip portable rilis sebelumnya, mengekstraknya, lalu membandingkan
     berkas (SHA-256) dengan hasil publish baru;
   - menulis `patch.json` + `patch-<versi>.zip` berisi **hanya berkas yang berubah**;
   - mengunggah `SuDesApp_<versi>_Setup.exe`, zip portable, `patch.json`, dan
     `patch-<versi>.zip` sebagai aset rilis.

Bila langkah tambalan dilewati (belum ada rilis sebelumnya, atau gagal), rilis tetap
terbit dan aplikasi otomatis memakai installer penuh — tidak ada pengguna yang
tertahan.

## Alur manual (tanpa CI)

```powershell
# 1. Siapkan hasil publish lama DAN baru
dotnet publish SuDesApp.Wpf/SuDesApp.Wpf.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=2.5.1 -p:AssemblyVersion=2.5.1.0 -p:FileVersion=2.5.1.0 -o artifacts/publish

# artifacts/publish-lama/ = hasil ekstraksi zip portable rilis 2.5.0

# 2. Buat tambalan (patch.json + patch-2.5.1.zip)
./Tools/Buat-Tambalan.ps1 -Versi 2.5.1 -VersiLama 2.5.0 `
    -PublishBaru artifacts/publish -PublishLama artifacts/publish-lama `
    -Ringkasan artifacts/RINGKASAN.txt -Keluaran artifacts

# 3. Unggah ke halaman rilis GitHub bersama installer & zip portable
```

`publish-release.ps1` bisa sekaligus membuat tambalan:

```powershell
./Tools/publish-release.ps1 -Version 2.5.1 -VersiLama 2.5.0 `
    -PublishLama artifacts/publish-lama -Ringkasan artifacts/RINGKASAN.txt
```

`-Ringkasan <berkas>` berisi satu baris per perbaikan; teks inilah yang muncul di
notifikasi pengguna. Bila dikosongkan, aplikasi memakai catatan rilis GitHub.

### Kapan dipaksa jadi pembaruan besar

- `-Besar` (manual), atau
- lebih dari 40% berkas berubah (`-BatasBerkasPersen`), atau
- lebih dari 60 MB berubah (`-BatasUkuranMB`), atau
- tidak ada berkas yang berubah.

Ambang itu bisa disetel saat memanggil skrip. Untuk perubahan arsitektur
(dependensi baru, .NET baru, basis data baru) sebaiknya selalu pakai `-Besar`.

## Isi `patch.json`

```json
{
  "versi": "2.5.1",
  "dariVersi": ["2.5.0"],
  "jenis": "kecil",
  "ringkasan": ["Perbaikan cetak surat template", "Rapikan menu utama"],
  "berkasPatch": "patch-2.5.1.zip",
  "sha256Patch": "…64 digit…",
  "berkas": [ { "path": "SuDesApp.Core.dll", "sha256": "…", "ukuran": 812345 } ],
  "berkasDihapus": ["berkas-lama.dll"],
  "tanggal": "2026-09-19T10:00:00",
  "catatan": "Tambalan 2.5.0 -> 2.5.1 (3 berkas)"
}
```

`dariVersi` memuat versi yang boleh memakai tambalan ini. Pengguna yang melompati versi
(mis. dari 2.4.3 langsung ke 2.5.1) diarahkan ke installer penuh agar isi berkasnya
pasti cocok. Bila ingin melayani lompatan versi, buat tambalan dengan
`-VersiLama 2.4.3` (diff terhadap rilis yang lebih tua).

## Pengamanan yang berlaku di aplikasi

- ZIP tambalan diverifikasi **SHA-256** sebelum diekstrak (memakai digest resmi GitHub
  bila ada, atau `sha256Patch`), lalu **setiap berkas** diverifikasi terhadap `berkas[].sha256`.
- Jalur berkas ditolak bila absolut atau keluar dari folder aplikasi (`..`), dan entri
  zip diperiksa satu per satu.
- **Data pengguna tidak pernah diganti**: `appsettings.json`, `Database/`, `Templates/`,
  `TempPDF/`, `Output/`, `logs/`, `*.db`, `*.log`, `penomoran-surat.json`,
  `template-bawaan.json`, `pengaturan-cetak.json`, dan `Resources/sudesapp.json`.
- Folder aplikasi harus bisa ditulis; bila tidak, aplikasi memakai installer penuh.
- Penerapan hanya berjalan setelah aplikasi ditutup (skrip penerap menunggu proses
  aplikasi benar-benar berakhir), lalu aplikasi dibuka kembali otomatis.
- Hasil penerapan ditulis ke `%LOCALAPPDATA%\SuDesApp\hasil-tambalan.json` dan
  dilaporkan lewat notifikasi saat aplikasi dibuka lagi — berhasil maupun gagal.
