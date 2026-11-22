## Ringkasan

Jelaskan masalah yang diselesaikan dan pendekatan yang dipakai. Sebutkan issue
yang terkait bila ada (`Closes #...`).

## Jenis perubahan

- [ ] Perbaikan bug
- [ ] Fitur baru
- [ ] Refaktor atau performa
- [ ] Dokumentasi
- [ ] Build, CI, atau rilis
- [ ] Perubahan Android

## Cara menguji

Tuliskan langkah yang sudah dicoba dan hasilnya. Sertakan perintah uji yang
dijalankan.

```bash
dotnet build SuDesApp.slnx --configuration Release
dotnet test SuDesApp.Tests/SuDesApp.Tests.csproj
```

## Daftar periksa

- [ ] Build lolos tanpa peringatan baru.
- [ ] Uji otomatis lolos; uji yang dilewati dijelaskan alasannya.
- [ ] Uji paritas Android ↔ desktop dijalankan bila skema, formulir, atau PDF
      berubah.
- [ ] Tidak ada rahasia, data warga asli, `desa.db`, atau hasil build yang ikut.
- [ ] Dokumentasi dan tautan diperbarui bila perilaku atau konfigurasi berubah.
- [ ] Perubahan UI memakai token tema dan lolos audit kontras.
- [ ] Pesan commit tidak memuat atribusi agen.

## Tangkapan layar

Untuk perubahan tampilan, sertakan tangkapan layar sebelum dan sesudah dengan
data uji (bukan data warga asli).
