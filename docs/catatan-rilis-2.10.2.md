# SuDesApp 2.10.2 — 2 November 2026

Pembaruan untuk meningkatkan tampilan dan kemudahan penggunaan menu APBDes, Perdes, dan Aktivasi Lisensi. Tidak ada perubahan format data, jadi `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Perbaikan

- **Menu APBDes lebih ringkas.** Kartu ringkasan APBDes diperkecil dan disusun dalam satu baris agar tampilan lebih padat dan tidak membuat menu bawah terasa sempit.
- **Menu Perdes lebih praktis.** Kartu contoh Perdes dibuat lebih ringkas, ikon keadaan kosong pada tabel arsip Perdes diperbaiki agar tidak menutupi isi tabel, serta formulir Perdes kini mendukung melampirkan berkas Word/PDF seperti pada SK dan Perkades.
- **Menu Aktivasi Lisensi disederhanakan.** Tombol "Bayar/Berlangganan (Full Service)" disembunyikan pada kartu status langganan karena aktivasi kini dijalankan secara manual.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.10.1, 2.10.0, 2.9.0, 2.8.0, 2.7.2 | pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.7.2 | installer penuh 2.10.2 | ± 100 MB |

## Aset

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.2_Setup.exe` | Installer Windows x64 |
| `SuDesApp_2.10.2_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.2_win-x64.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 |
| `SuDesApp_2.10.2_win-x64.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.2.zip` | Pembaruan kecil untuk aplikasi yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru.
