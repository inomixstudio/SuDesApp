# Kebijakan Keamanan — SuDesApp

Terima kasih telah membantu menjaga keamanan SuDesApp dan data desa yang
memakainya. Dokumen ini menjelaskan cara melaporkan kerentanan, cakupan
penelitian yang diizinkan, dan keputusan desain keamanan yang sudah ada.

## Versi yang didukung

Perbaikan keamanan hanya diterbitkan untuk rilis terbaru pada cabang `master`.

| Versi | Didukung |
|---|---|
| 2.15.x (terbaru) | Ya |
| < 2.15 | Tidak — perbarui ke rilis terbaru |

Versi desktop dan Android harus sama karena sinkronisasi bergerbang versi.
Selalu uji laporan pada rilis terbaru sebelum mengirimkannya.

## Melaporkan kerentanan

**Jangan buka issue publik untuk kerentanan.** Gunakan salah satu jalur privat:

1. **GitHub Security Advisories (disarankan)** —
   <https://github.com/inomixstudio/SuDesApp/security/advisories/new>
2. **Surel** — <sumberjayadev@gmail.com> dengan subjek
   `[KEAMANAN] SuDesApp <ringkasan singkat>`

Sertakan sebanyak mungkin informasi berikut:

- versi SuDesApp dan platform (Windows/Android/server lisensi/API),
- versi Windows atau Android,
- langkah reproduksi yang dapat diulang,
- dampak yang diperkirakan (data apa yang dapat dibaca, diubah, atau
  diekspos),
- bukti konsep secukupnya (tangkapan layar, permintaan HTTP, atau log), dan
- apakah temuan memerlukan akses lokal, akun pengguna, atau akses jaringan.

**Jangan menyertakan data pribadi warga asli.** Gunakan data uji atau
anonimkan NIK, nomor Kartu Keluarga, alamat, dan isi surat.

### Waktu tanggap

| Tahap | Target |
|---|---|
| Konfirmasi penerimaan | 3 hari kerja |
| Penilaian awal dan rencana perbaikan | 7 hari kerja |
| Perbaikan | Sesuai tingkat keparahan; kerentanan kritis diutamakan |
| Pengungkapan | Dikoordinasikan setelah perbaikan tersedia |

Kami akan memberi tahu bila laporan berada di luar cakupan atau membutuhkan
informasi tambahan.

## Cakupan

Dalam cakupan:

- aplikasi desktop WPF (`SuDesApp.Wpf`, `SuDesApp.Core`),
- server lisensi (`SuDesApp.Server`) dan pusat administrasi,
- backend HTTP (`SuDesApp.Api`) dan API desa lokal,
- aplikasi Android (`android/`),
- skema Supabase, kebijakan RLS, dan alur sinkronisasi perangkat,
- skrip build, rilis, dan workflow GitHub Actions di repositori ini.

Di luar cakupan:

- layanan pihak ketiga (GitHub, Supabase, Google, penyedia hosting) — laporkan
  langsung kepada penyedianya,
- versi yang sudah tidak didukung,
- rekayasa sosial, phishing, dan serangan fisik,
- denial-of-service terhadap instalasi milik sendiri atau server bersama,
- temuan dari pemindai otomatis tanpa bukti dampak yang dapat dieksploitasi,
- masalah yang memerlukan perangkat sudah disusupi, akses administrator
  Windows, atau kredensial operator yang sudah bocor,
- ketidaksesuaian konfigurasi pada deployment yang tidak mengikuti
  [`docs/deploy-server.md`](docs/deploy-server.md).

## Safe harbor

Kami menghargai penelitian keamanan yang dilakukan dengan iktikad baik. Selama
Anda:

- hanya menguji instalasi milik sendiri atau yang Anda punya izin tertulis
  untuk mengujinya,
- tidak mengakses, menyalin, atau mengubah data desa/warga milik pihak lain,
- tidak mengganggu layanan, menghapus data, atau memasang malware,
- tidak memeras, memperjualbelikan, atau mempublikasikan temuan sebelum
  perbaikan tersedia, dan
- mematuhi hukum yang berlaku,

kami tidak akan menempuh tindakan hukum terhadap penelitian tersebut. Laporan
yang memenuhi ketentuan ini akan dicatat kontribusinya bila Anda menghendaki.

## Jangan pernah melakukan ini

Beberapa tindakan terlihat seperti pintasan, tetapi merusak hak pengguna dan
tidak termasuk penelitian yang diizinkan:

- **Jangan meng-commit rahasia apa pun.** Kredensial OAuth Google, kredensial
  Supabase, dan kunci privat penandatangan lisensi berada di berkas lokal yang
  di-ignore. Bila menemukan rahasia yang bocor, laporkan secara privat agar
  dapat dirotasi sebelum disalahgunakan.
- **Jangan mengakses `desa.db` milik orang lain.** Database desa disimpan di
  komputer masing-masing pengguna dan tidak dikirim ke server.
- **Jangan menyalahgunakan endpoint admin.** Endpoint `/admin/*` dilindungi
  kredensial operator dan izin terbatas; jangan mencoba memakainya tanpa
  otorisasi.
- **Jangan menguji dengan data warga asli.** Gunakan data sintetis atau
  anonimkan seluruh data pribadi.

## Ringkasan desain keamanan

Bagian ini menjelaskan keputusan yang sudah diambil agar laporan Anda langsung
menuju celah yang belum tertutup.

### Tidak ada notifikasi pembayaran otomatis

Gateway pembayaran (Midtrans Snap) dan endpoint notifikasinya
(`/webhook-pembayaran`, `/checkout`, `/admin/rekonsiliasi`,
`/admin/uji-pembayaran*`) sudah dihapus. Satu-satunya jalan memperpanjang
langganan adalah pembukuan manual oleh operator bertoken
(`POST /admin/pelanggan/{kode}/tandai-lunas`), sehingga tidak ada permintaan
masuk yang dapat mengklaim “transaksi ini sudah lunas” tanpa kredensial.

Penjaga lama “server menolak start bila `ServerKey` kosong” tidak diperlukan
lagi karena ancaman yang dijaganya tidak punya jalan masuk. Jangan menambah
endpoint pembayaran otomatis tanpa mengembalikan penjaga setaranya.

### Kunci lisensi

- Penandatanganan memakai **ECDSA P-256**.
- Kunci privat hanya ada di server dan **tidak pernah** keluar dari sana.
- Kunci publik ditanam ke aplikasi sebagai akar kepercayaan
  (`Lisensi:KunciPublik` di `appsettings.json`) dan diisi **sebelum rilis**.
  Bila kosong, aplikasi melakukan *trust on first use* — lihat
  [`docs/deploy-server.md`](docs/deploy-server.md) bagian “Kunci penandatangan”.
- Server menolak memakai `appsettings.lokal.json` untuk menetapkan
  `Lisensi:KeyPath`: environment variable menang, sehingga kunci dapat
  dipetakan ke volume persisten dan tidak berubah setiap container restart.

### Rahasia

| Rahasia | Lokasi | Dibuat |
|---|---|---|
| Kredensial OAuth Google | `GoogleClientCredentialsBawaan.lokal.cs` (terlokal, tidak terlacak) — ikut semua konfigurasi build; login Google aktif otomatis dari installer | saat build lokal |
| Kredensial Supabase | `appsettings.lokal.json` (terlokal, tidak terlacak) atau environment variable | saat deploy |
| Kunci privat lisensi | `SuDesApp.Server/keys/` (terlokal, tidak terlacak) atau env `LISENSI_KEY_PATH` | saat deploy |

Berkas `appsettings.lokal.json` dikecualikan dari output publish
(`<Content Remove>` di `.csproj`) dan dari build context Docker
(`.dockerignore`) agar kredensial tidak terbawa ke dalam image.

### Kata sandi pengguna

PBKDF2-HMAC-SHA256, 120.000 iterasi, salt acak 16 byte per akun, jumlah iterasi
disimpan per akun (agar biaya dapat dinaikkan tanpa membuat akun lama tidak
terbaca), dan perbandingan memakai `CryptographicOperations.FixedTimeEquals`.

### Data desa

Database desa (`desa.db`) berada di komputer desa dan **tidak pernah** dikirim
ke server. Database yang dibuat versi ini berupa **SQLite biasa tanpa
enkripsi** — lindungi lewat izin folder dan akun Windows. Database terenkripsi
SQLCipher dari versi lama tetap dibuka memakai berkas kuncinya
(`DesaKunciDb.bin`, DPAPI akun Windows); aplikasi tidak membuat database
terenkripsi baru.

Server lisensi hanya menyimpan masa aktif, nama desa, dan kontak — cukup untuk
menagih langganan. Lihat
[`docs/langganan-dan-lisensi.md`](docs/langganan-dan-lisensi.md) dan
[`docs/kebijakan-privasi.md`](docs/kebijakan-privasi.md).

## Pemindaian otomatis di repositori

Repositori ini menjalankan:

- **Dependabot** untuk NuGet, Gradle, dan GitHub Actions,
- **Secret scanning** dan **push protection** GitHub,
- **Gitleaks** pada setiap push dan pull request,
- **GitHub Security Advisories** untuk pelaporan privat.

Laporan dari pemindai otomatis tanpa bukti dampak tidak perlu dikirim sebagai
advisory; cukup buka issue biasa bila menurut Anda ada konfigurasi yang salah.
