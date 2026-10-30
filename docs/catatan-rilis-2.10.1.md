# SuDesApp 2.10.1 — 31 Oktober 2026

Rilis tambalan untuk 2.10.0: memperbaiki letak tanda tangan pada permohonan
rekening koran dan membuat pratinjau/cetak PDF jauh lebih ringan. Tidak ada
perubahan format data, jadi `desa.db`, `Templates`, dan arsip surat lama dipakai
apa adanya.

## Perbaikan

- **Tanda tangan Kepala Desa pada rekening koran berada di kolom kanan.**
  Sebelumnya kotak blok tanda tangan memang diletakkan di sisi kanan, tetapi
  isinya ditata selebar halaman sehingga jabatan dan nama tercetak di tengah
  kertas. Kini keduanya rata tengah di dalam kolom kanan, baik pada pratinjau
  maupun cetak ulang surat lama. Dijaga uji yang mengukur rentang tinta dua baris
  terakhir halaman.

## Peningkatan

- **Pratinjau PDF lebih ringan.** Gambar kop dan pindaian besar ditanam pada
  kerapatan cetak yang dipakai (300 dpi) alih-alih seluruh pikselnya, setiap
  berkas gambar hanya diurai sekali per dokumen, dan saat halaman dilukis ke
  layar hanya bagian gambar yang tampak yang dibaca — pindaian 1507×2050 piksel
  yang tampil sekitar 70 piksel tidak lagi dikonversi penuh ke RGBA per halaman.
- **Zoom tidak lagi berkedip.** Kerangka halaman dipakai ulang saat memperbesar
  atau memperkecil tampilan; bitmap lama tetap terpasang sampai hasil render baru
  siap, jadi panel tidak kosong sekejap.
- **Satu jalur untuk semua pratinjau.** Seluruh alur “susun surat, simpan berkas
  sementara, buka pratinjau” memakai satu penulis berkas; pratinjau selalu berada
  di folder `TempPDF` aplikasi dan ikut dibersihkan saat aplikasi dijalankan.
- **Kop pratinjau memakai mesin tata letak surat.** Kop pada pratinjau disusun
  oleh mesin yang sama dengan surat, bukan tata letak kedua di XAML.

Angka pendukung, diukur pada build Release: render satu halaman surat turun dari
sekitar 180 ms menjadi sekitar 60 ms, dan berkas PDF surat berisi kop menjadi
lebih kecil. Hasil tampilan halaman dibandingkan piksel demi piksel pada empat
belas halaman contoh sebelum rilis ini diterbitkan.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.10.0 | pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.10.0 | installer penuh 2.10.1 | ± 100 MB |

Versi di bawah 2.10.0 **tidak perlu** memasang 2.10.0 lebih dulu: installer
2.10.1 sudah memuat seluruh isi 2.10.0 beserta perbaikan pada versi ini. Aplikasi
memilih jalur ini sendiri — tambalan hanya dipasang bila versi terpasang sama
dengan versi asal tambalan (`dariVersi` pada `patch.json`), sedangkan versi lain
diarahkan ke installer penuh. Jalur yang sama dipakai bila folder aplikasi tidak
dapat ditulis atau tambalan gagal dipasang.

Aplikasi versi 2.9.0 ke atas mengunduh paketnya sendiri di latar belakang setelah
pengguna masuk; versi yang lebih lama belum punya unduhan latar, jadi paket
diunduh manual dari halaman rilis dan dijalankan seperti biasa.

## Aset

| Berkas | Keterangan |
|---|---|
| `SuDesApp_2.10.1_Setup.exe` | Installer Windows x64: memasang ke `%LocalAppData%\Programs\SuDesApp` tanpa UAC, membuat pintasan Start Menu, dan menyediakan pencopotan lewat *Apps & features* |
| `SuDesApp_2.10.1_Setup.exe.sha256` | Sidik jari SHA-256 installer |
| `SuDesApp_2.10.1_win-x64_portable.zip` | Aplikasi desktop Windows x64, self-contained .NET 10 (tanpa instalasi runtime) |
| `SuDesApp_2.10.1_win-x64_portable.zip.sha256` | Sidik jari SHA-256 paket portable |
| `patch.json` + `patch-2.10.1.zip` | Pembaruan kecil untuk aplikasi 2.10.0 yang sudah terpasang |

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi
baru, dan `Templates` tidak dihapus saat pencopotan. Tanpa instalasi, ekstrak ZIP
lalu jalankan `SuDesApp.exe`.

## Verifikasi unduhan

Setiap aset installer dan zip portable disertai berkas `.sha256` senilai isinya.
Periksa di Windows dengan
`Get-FileHash .\SuDesApp_2.10.1_Setup.exe -Algorithm SHA256`, atau di Linux/macOS
dengan `sha256sum -c SuDesApp_2.10.1_Setup.exe.sha256`. Aplikasi juga memverifikasi
SHA-256 paket sebelum memasangnya.

## Persyaratan sistem

- Windows 10 versi 1607 atau lebih baru, atau Windows 11, arsitektur x64.
- Tidak perlu memasang .NET: kedua paket membawa runtime .NET 10 sendiri.
- Ruang disk sekitar 410 MB untuk berkas program, ditambah ruang untuk data desa.
- Pemasangan memakai `%LocalAppData%\Programs\SuDesApp`, jadi tidak menuntut hak
  administrator.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur:
  <https://github.com/inomixstudio/SuDesApp/issues>
- Panduan pembaruan dan tambalan: `docs/PEMBARUAN-TAMBALAN.md`
- Tanda tangan dan verifikasi rilis: `docs/TANDA-TANGAN-RILIS.md`
