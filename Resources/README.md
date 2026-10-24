# Aset `Resources/`

Folder ini menampung gambar yang **ikut terbuntel** ke instalasi pengguna.
Keduanya wajib punya penjagaan asal-usul:

| Kelompok | Isi | Katalog asal-usul | Uji penjagaan |
|---|---|---|---|
| Lambang kabupaten/kota | `LogosKabupaten/` (514 kode) | `LogosKabupaten/sumber.json` | `LogoKabupatenSumberTests` |
| Ikon & logo umum | ikon aplikasi, `logo.png`, `Qris-Pembayaran.png`, logo & ikon Sumberjaya Dev / Duta Publik | [`sumber-aset.json`](sumber-aset.json) | `SumberAsetGambarTests` |

## Kenapa penting

Gambar paling gampang ditukar diam-diam: `Qris-Pembayaran.png` yang tertukar
mengalirkan pembayaran desa ke merchant lain lewat aplikasi bank, dan ikon
aplikasi bisa terganti tanpa disadari. Karena itu setiap gambar langsung di
folder ini wajib punya entri di `sumber-aset.json` — SHA-256, penggunaan,
jenis sumber (`generasi-skrip`/`aset-perusahaan`/`aset-internal`), dan catatan
asal-usul — dan uji menolak: gambar tanpa entri, hash yang tidak cocok isi
berkas, entri tanpa berkas atau duplikat, serta aset terbuntel yang tidak
terdaftar (tepat sekali) di `SuDesApp.Wpf.csproj`.

## Alat pendamping

```bash
py -3 Tools/verifikasi-asal-usul.py                 # verifikasi semua aset
py -3 Tools/verifikasi-asal-usul.py --perbarui      # muat ulang SHA-256 isi
```

`--perbarui` untuk momen yang disengaja — regenerasi ikon lewat
`Tools/generate-app-icon.ps1` atau penggantian merek — dan hanya mengganti
hash; catatan asal-usul tetap wajib terisi.

Menambah/mengganti aset: simpan berkas (nama saja, tanpa subfolder), tambah
entri katalog, daftarkan di `SuDesApp.Wpf.csproj`, lalu `dotnet test`.
Untuk QRIS ada penjagaan yang lebih dalam lagi: NMID diuraikan dari payload
EMVCo gambar yang benar-benar terbuntel (`QrisPembayaranTests`).
