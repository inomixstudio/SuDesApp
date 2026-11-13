# Build & Uji Guidelines

## Build
- Jangan menambahkan `-f net10.0` pada `dotnet test`: proyek uji hanya punya target `net11.0-windows`, hasilnya galat NETSDK1005 yang menyesalkan (seolah restore belum jalan).
- Analyzer SUDES0001 (dipasang lewat `Directory.Build.props`) menggagalkan build bila ada blok `catch` kosong tanpa komentar alasan di dalamnya.

## Uji
- Perintah `dotnet test ... -v q` ala README menyembunyikan keluaran `ITestOutputHelper`; tambahkan `--logger "console;verbosity=detailed"` lalu saring dengan grep bila butuh angka dari tes.
- Uji xunit berjalan PARALEL dalam satu proses, jadi `Environment.SetEnvironmentVariable` di satu uji mengganggu uji lain yang membaca env yang sama. `MidtransService` menerima parameter `serverKey` eksplisit (konstruktor 4 argumen) supaya uji signature tidak bergantung pada env global — pakai itu, jangan andalkan `Midtrans:ServerKey` dari config. Akar flaky `JejakWebhookTests.Notifikasi_lunas_tercatat_sebagai_diterima` (dulu 403 saat suite penuh, lolos sendirian) adalah ini.
- `JejakWebhookTests` dan `LayananUjiPembayaranTests` keduanya memakai kunci uji `kunci-uji-midtrans`.
