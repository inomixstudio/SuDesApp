# Rencana: Langganan 2 Jalur + Aktivasi Manual + Buat Kode Aktivasi

## Hasil eksplorasi (kondisi saat ini)
- **Halaman Langganan (WPF)** sudah punya: jalur otomatis (tombol "Bayar / Perpanjang" → tagihan Midtrans), panel QRIS manual (scan & bayar, tanpa langkah hubungi admin), dan kartu "Kode Aktivasi" (Simpan & Cek).
- **Pusat Administrasi** = WebView2 ke halaman `admin.html` di server lisensi. Halaman itu sudah punya "Daftarkan desa baru" (kode diketik manual oleh operator) dan "Tandai lunas & perpanjang" per desa (harus buka desa dulu).

## 1. Halaman Langganan — dua jalur yang jelas (WPF)
**`SuDesApp.Wpf/ViewModels/LanggananViewModel.cs`**
- Konstanta baru: `NomorWaAdminTampil = "0851 1166 9951"` + `NomorWaAdminWa = "6285111669951"` (format wa.me), satu sumber seperti pola `QrisNmid`.
- Perintah baru `BukaWaAdminCommand`: membuka `https://wa.me/6285111669951?text=…` dengan pesan terisi otomatis dari `QrisReferensi` (kode aktivasi · paket · nominal · nama penerima) + kalimat bukti pembayaran. Gagal buka peramban → nomor & link ditampilkan di banner (tidak pernah diam).
- Helper statis pembangun tautan WA (URL-encoded) agar bisa diuji.
- Kalimat pengarah QRIS diperbarui menyebut langkah WhatsApp.

**`SuDesApp.Wpf/Views/LanggananView.xaml`**
- Kartu "Paket Langganan" disusun ulang menjadi dua sub-bagian berlabel:
  - **Jalur 1 — Otomatis**: tombol Trial + "Bayar / Perpanjang" (perintah lama, tidak berubah perilaku).
  - **Jalur 2 — Manual**: tombol "Tampilkan QRIS (scan & bayar)".
- Panel QRIS diberi langkah bernomor: (1) scan & bayar nominal persis, (2) **tombol "Hubungi Admin via WhatsApp"** dengan nomor tampil, (3) tunggu admin menandai lunas → Cek Status.
- Kartu "Kode Aktivasi" dinamai ulang **"Aktivasi Manual"** dengan deskripsi: masukkan kode yang diberikan admin setelah pembayaran manual dikonfirmasi via WhatsApp.
- Kartu "Cara Memperpanjang" diperbarui mencerminkan dua jalur.

## 2. Pusat Administrasi (server lisensi) — aktivasi manual & buat kode
**Server**
- File baru `GeneratorKodeAktivasi.cs`: format `SUDES-XXXX-XXXX`, alfabet tanpa karakter rancu (tanpa 0/O/1/I), sumber acak bisa disuntik untuk uji.
- `LayananAdmin.DaftarkanDenganKodeBaruAsync(…)`: buat kode → cek unik via `CariByKodeAsync` → daftarkan; log audit Warning seperti pendaftaran lain.
- Endpoint baru `POST /admin/kode-aktivasi` (Program.cs) + record `BuatKodeAktivasiRequest(NamaDesa, Kontak, MaxPerangkat)` (AdminRequests.cs); didaftarkan di `AdminEndpointCatalog` dengan izin `CustomersCreate`.
- Aktivasi manual memakai endpoint lama `POST /admin/pelanggan/{kode}/tandai-lunas` (tidak perlu endpoint baru).

**`SuDesApp.Server/wwwroot/admin.html`**
- Kartu baru **"Aktivasi Manual"**: kolom kode aktivasi + pilihan paket + nominal opsional + catatan → tandai lunas & perpanjang, tanpa harus membuka desa dulu. Hasil menampilkan order id, nominal, dan masa aktif baru.
- Kartu pendaftaran (diberi judul "Buat Kode Aktivasi"): tombol **"Buat kode otomatis & daftarkan"** — isi nama desa → server menghasilkan kode unik → kode tampil dengan tombol salin. Mengetik kode manual tetap tersedia.

## 3. Uji (`SuDesApp.Tests/Licensing`)
- Uji generator kode: format, alfabet tanpa rancu, keunikan (injeksi acak).
- Uji pendaftaran dengan kode otomatis via `LayananAdmin` + repo memori.
- Uji pembangun tautan WhatsApp (nomor & encoding benar).

## 4. Verifikasi
- `dotnet build` solusi; `dotnet test` (test baru + test lisensi lama untuk regresi).

Tidak ada perubahan skema database, tidak ada perubahan perilaku jalur otomatis/trial yang ada.