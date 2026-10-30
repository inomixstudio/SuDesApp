# Audit perubahan lokal sebelum upload ke GitHub

> **Dokumen historis (20 Oktober 2026).** Isi di bawah menggambarkan keadaan
> working tree saat audit itu dibuat, bukan keadaan repo sekarang. Sejak
> 30 Oktober 2026 branch default adalah `master`; perubahan sudah di-commit,
> artefak build/probe sudah dihapus, dan rilis 2.10.0 diterbitkan. Bagian
> rekomendasi di bawah disimpan hanya sebagai catatan proses.

Status diperiksa pada branch `upgrade-dotnet-10` (nama branch saat itu).

## Ringkasan working tree

- Total entri perubahan Git: **2.997**
- Berkas terlacak berubah: **203**
- Berkas terlacak dihapus: **7**
- Berkas tak terlacak: **2.787**
- Branch lokal berada **6 commit di depan** `origin/upgrade-dotnet-10`.
- Working tree memuat kode, tes, dokumentasi, data/aset, hasil build dan percobaan installer. Karena itu, jangan gunakan `git add -A` atau push branch saat ini sebelum meninjau dan memilih kelompok file.

## Kelompok yang perlu dikecualikan dari commit sumber

Bukti di status Git menunjukkan direktori/berkas berikut adalah hasil build, cache, backup, atau percobaan lokal:

- `_idm-build-check/` — keluaran publish/build lengkap, termasuk DLL/PDB, runtime native, dan salinan docs/data.
- `SuDesApp.Core/.build-google-auth/`, `SuDesApp.Server/.build-google-auth/`, `SuDesApp.Tests/.build-google-auth/`, `SuDesApp.Wpf/.build-google-auth/` — keluaran build dengan dependensi dan aset runtime.
- `installer/SuDesApp-Advanced-SetupFiles/` — MSI hasil build installer.
- `installer/SuDesApp-Advanced-v24-cache/` — cache installer; satu CAB terdeteksi sekitar **311 MB**.
- `installer/SuDesApp-Advanced-v24-backups/`, `installer/_edit-probe-backups/`, `installer/_folder-probe-backups/` — backup/probe.
- `installer/_cli-probe.aip`, `installer/_edit-probe.aip`, `installer/_folder-probe.aip` — berkas probe.
- `Tools/__pycache__/` — cache Python.
- `.uji-render/` — tangkapan/hasil render visual; perlu keputusan apakah ini deliverable atau hanya bahan uji.

Daftar ini tidak dihapus atau diubah oleh audit.

## Kandidat sumber yang perlu pemeriksaan isi sebelum staging

- `SuDesApp.Core/`, `SuDesApp.Server/`, `SuDesApp.Wpf/`, `SuDesApp.Tests/`: perubahan kode dan tes lokal. Beberapa mungkin sesuai dengan lima commit yang belum di-push, tetapi perlu ditinjau karena terdapat banyak modifikasi dan file baru.
- `Data/Wilayah/wilayah.tsv.gz`, `Resources/LogosKabupaten/`, `Resources/Fonts/`, dan logo perusahaan: tampak seperti aset/data aplikasi; validasi sumber, lisensi, kelengkapan, dan integrasi build sebelum commit.
- `docs/`, `Templates/`, `Bawaan/`, `README.md`, file proyek, workflow, installer script: dokumentasi/configuration yang berpotensi merupakan deliverable, tetap perlu pemeriksaan diff.
- `installer/*.aip`: proyek konfigurasi Advanced Installer; pastikan yang dimaksud adalah konfigurasi sumber utama, bukan probe/backup. Paket MSI/CAB/cache tidak semestinya ikut sebagai file sumber biasa.

## Pemeriksaan nama berkas terkait rahasia

Penyaringan nama berkas perubahan tidak menemukan `.env`, private key, sertifikat, atau `appsettings.*.lokal.json` aktual. `SuDesApp.Server/appsettings.lokal.template.json` tampak sebagai template berdasarkan namanya, tetapi isinya belum diaudit untuk memastikan tidak mengandung kredensial nyata. Nama file saja tidak membuktikan file aman untuk publikasi.

## Rekomendasi langkah upload

1. Review diff seluruh 203 file terlacak berubah dan keputusan atas 7 file terhapus.
2. Tinjau isi file baru yang relevan, terutama template konfigurasi dan sumber/atribusi aset.
3. Pilih sumber, tes, dokumentasi, data/aset deliverable secara eksplisit; keluarkan build/cache/backup/probe.
4. Jalankan build dan test yang sesuai pada kumpulan perubahan yang dipilih.
5. Stage daftar file eksplisit, review staged diff, kemudian commit dan push ke branch review terpisah sebelum merge.

Audit ini bersifat inventaris dan rekomendasi saja; tidak ada file proyek yang di-stage, di-commit, dihapus, atau di-push.

## Keputusan audit ini (2026-10-20)

- **Di-commit**: seluruh perubahan berkas terlacak (kode, tes, docs, workflow,
  `installer/SuDesApp.iss`), berkas baru di `SuDesApp.Core/`, `SuDesApp.Server/`,
  `SuDesApp.Tests/`, `SuDesApp.Wpf/`, `docs/`, `Tools/`, `Templates/`,
  `SuDesApp.Server/appsettings.lokal.template.json` (placeholder, tanpa kredensial),
  serta aset/data yang dipakai build: `Resources/` (termasuk 514 lambang dengan
  `sumber.json`) dan `Data/Wilayah/wilayah.tsv.gz`.
- **Dikecualikan dan masuk `.gitignore`**: `_idm-build-check/`,
  `**/.build-google-auth/`, cache/backup/probe installer, `installer/*.aip`
  (dibuat ulang oleh `Tools/Buat-Installer-Advanced.ps1`), `Tools/__pycache__/`,
  dan `.uji-render/`.
- **Pemeriksaan rahasia**: tidak ada kredensial nyata pada kandidat commit;
  `appsettings.lokal.template.json` hanya berisi placeholder, dan
  `appsettings.lokal.json`, `sudesapp.json`, kunci privat tetap terabaikan.
- **Verifikasi**: `dotnet build SuDesApp.slnx` 0 error (70 warning analyzer) dan
  `dotnet test` 720 lulus, 0 gagal.
