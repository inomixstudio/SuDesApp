# Build & Uji Guidelines

## Build
- Jangan menambahkan `-f net10.0` pada `dotnet test`: proyek uji hanya punya target `net10.0-windows`, hasilnya galat NETSDK1005 yang menyesalkan (seolah restore belum jalan).
- Analyzer SUDES0001 (dipasang lewat `Directory.Build.props`) menggagalkan build bila ada blok `catch` kosong tanpa komentar alasan di dalamnya.

## Uji
- Perintah `dotnet test ... -v q` ala README menyembunyikan keluaran `ITestOutputHelper`; tambahkan `--logger "console;verbosity=detailed"` lalu saring dengan grep bila butuh angka dari tes.
- `JejakWebhookTests.Notifikasi_lunas_tercatat_sebagai_diterima` kadang gagal saat seluruh suite berjalan, tetapi lolos bila difilter sendiri — flaky karena berkas jejak sementara bersama, bukan kemunduran kode.
