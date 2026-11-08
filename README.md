# SuDesApp — Sistem Surat Desa

Aplikasi surat desa lintas perangkat: desktop Windows (WPF, .NET 10), backend
API, server lisensi, dan pendamping Android (Kotlin) dengan sinkron Supabase.
Fitur inti: pembuatan surat resmi dengan penomoran sesuai perbup, register &
agenda digital, data penduduk lengkap dengan mutasi, sampai alur persetujuan
opsional (ajukan → verifikasi → tanda tangan) per surat. Masa uji coba 30 hari
aktif otomatis; setelah habis cetak PDF/Excel dan sinkron Android dikunci
kecuali Daftar Hadir, dan popup mengarahkan ke AKTIVASI LISENSI atau hubungi
developer.

## Fitur utama

- **Buat & cetak surat** — 20+ jenis surat bawaan (SKTM, SKU, domisili, kematian,
  NTCR, rekening koran, …) plus Template Surat sendiri lewat wizard atau impor
  berkas Word; penomoran otomatis mengikuti Perbup (rujukan di
  `docs/referensi-penomoran-surat.md`). Tombol **Pratinjau** bisa ditekan sebelum
  form diisi: SudesPdf menampilkan kerangka surat, dengan kolom kosong tercetak
  sebagai titik-titik dan tanpa menyimpan apa pun ke register.
- **Logo kabupaten otomatis** — lambang 514 kabupaten/kota se-Indonesia tersedia
  lengkap; begitu kabupaten dipilih di Pengaturan Surat, lambang kop surat
  terpasang otomatis tanpa unggah manual. Kode Kemendagri disimpan di database
  sehingga logo tetap konsisten setelah restart (lihat
  [`Resources/LogosKabupaten/README.md`](Resources/LogosKabupaten/README.md)).
- **Register & agenda** — register surat, register NTCR, buku agenda surat
  masuk/keluar, arsip keputusan/perdes, lengkap dengan ekspor Excel/CSV/JSON/PDF.
- **Data warga** — penduduk, Kartu Keluarga, mutasi (aktif/baru/pindah/meninggal),
  impor & ekspor massal, laporan rekapitulasi kependudukan (PDF + Excel).
- **Alur persetujuan opsional** — per surat: ajukan → verifikasi → tanda tangan
  (atau tolak), dengan label cetak status dan lampiran scan surat bertanda
  tangan. Surat tanpa alur tetap terbit langsung seperti biasa
  (lihat `docs/alur-persetujuan-surat.md`).
- **QR verifikasi** — setiap surat terbit membawa kode + QR di kaki surat;
  pihak luar (bank/instansi) bisa memeriksa keasliannya.
- **API desa** — endpoint HTTP lokal untuk sistem luar membaca statistik, data
  perangkat desa, status persetujuan, rekap laporan bulanan, dan memeriksa
  keaslian surat (`docs/api-desa.md`).
- **Backend untuk klien luar** — `SuDesApp.Api` (ASP.NET Core) membuka data desa
  lewat HTTP untuk aplikasi mobile/browser: masuk memakai akun desktop, data
  warga, register surat, statistik, dan kelola pengguna
  (`docs/backend-web.md`). Kanal WhatsApp dan Google Form/Sheet sudah dihapus;
  login Google kini hanya untuk pencadangan ke Google Drive.
- **Sinkron Android** — aplikasi Android native (Kotlin di `android/`) menarik
  kop surat dan pola nomor dari desktop via Supabase, membuat surat dan PDF di
  perangkat, lalu mengantrekan di `antrean_surat` untuk diimpor desktop.
  Perangkat dipasangkan via QR atau kode 4 angka.
- **Lisensi** — trial 30 hari otomatis tanpa form; setelah habis cetak PDF/Excel
  dan sinkron Android dikunci (Daftar Hadir tetap boleh), popup mengarahkan ke
  AKTIVASI LISENSI atau hubungi developer
  (`docs/langganan-dan-lisensi.md`).
- **Multi-pengguna** — akun dengan peran & izin, kunci akun setelah percobaan
  login gagal, dan halaman **Pengguna & Peran** untuk membuat akun,
  mengganti peran/status, mereset kata sandi, dan menghapus akun
  (`docs/policy-peran-pengguna.md`).
- **Kuitansi lisensi** — setiap pembayaran bisa dicetak sebagai kuitansi dari
  pusat administer (nomor, nominal + terbilang, status lunas, masa aktif) untuk
  pertanggungjawaban anggaran desa (lihat `docs/langganan-dan-lisensi.md`).
- **Keamanan data** — pencadangan database ke Google Drive (otomatis saat
  ditutup) + ekspor/impor manual, tutup buku tahunan
  (`docs/policy-tutup-buku-dan-retensi.md`), riwayat aktivitas yang bisa diekspor
  ke Excel.

## Kebutuhan

- Windows 10/11 dengan **.NET 11 SDK** (build) atau runtime .NET 10 (menjalankan).
  Versi SDK dikunci lewat `global.json` ke **11.0.100-rc.3** sehingga build developer dan
  CI (GitHub Actions) selalu memakai SDK yang sama; bila mesin Anda belum punya
  versi itu, pasang dulu dari https://dotnet.microsoft.com/download.
- Microsoft Word opsional — hanya untuk konversi berkas `.doc` lama dan impor
  template.

## Build

Build juga menjalankan analyzer **SUDES0001**: setiap `catch` kosong wajib
berisi komentar alasan di dalam bloknya. Catch kosong tanpa komentar alasan
ditolak sebagai error, sehingga build gagal. Contoh yang diterima:
`catch { /* berkas sementara sudah tidak ada */ }`. Analyzer adalah dependensi
build saja dan tidak ikut ke installer atau hasil publish.

```bash
# Library inti (TreatWarningsAsErrors aktif)
dotnet build SuDesApp.Core/SuDesApp.Core.csproj --nologo -v q

# Aplikasi WPF
dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj --nologo -v q

# Uji otomatis (termasuk integritas token lisensi)
dotnet test SuDesApp.Tests/SuDesApp.Tests.csproj --nologo -v q

# Backend web (ASP.NET Core)
dotnet build SuDesApp.Api/SuDesApp.Api.csproj --nologo -v q
```

Jalankan backend untuk menguji klien mobile/browser:

```bash
dotnet run --project SuDesApp.Api --urls http://127.0.0.1:5188
```

Endpoint-nya didokumentasikan di [`docs/backend-web.md`](docs/backend-web.md).

GitHub Actions menjalankan restore, build Release, dan uji otomatis pada Windows
untuk setiap pull request dan push ke `master` (`.github/workflows/ci.yml`). Cakupan
uji saat ini mencakup tanda tangan token lisensi, termasuk kompatibilitas verifikasi
server dan aplikasi desktop. Workflow publikasi `.github/workflows/release.yml`
ikut disertakan dan menandatangani aset dengan Authenticode — lihat
[`docs/TANDA-TANGAN-RILIS.md`](docs/TANDA-TANGAN-RILIS.md). Workflow
`.github/workflows/rilis-otomatis.yml` menjalankan seluruh urutan rilis dari satu
tombol: menaikkan versi aplikasi, menjalankan gerbang build dan uji E2E, membuat
tag anotasi, lalu memanggil `release.yml` untuk publish, installer, dan unggah
aset.
Untuk pemeriksaan kontras tema, jalankan `Tools/check-kontras-wcag.ps1` secara
terpisah (lihat [`docs/tema-dan-warna.md`](docs/tema-dan-warna.md)). Skrip
`Tools/publish-release.ps1` dan `Tools/rilis-sekali-jalan.ps1` menyiapkan aset
rilis lokal; jalankan pemeriksaan rilis dan tinjau aset sebelum membagikan
installer.

## Atribusi commit

Pesan commit jangan memuat atribusi agen: baris `Co-Authored-By` bernama
Codebuff, teks `Generated with Codebuff`, atau alamat `noreply@codebuff.com`.
Atribusi semacam itu pernah masuk ke 37 commit dan harus ditulis ulang dengan
force-push agar tidak muncul di daftar kontributor GitHub.

Untuk memeriksa sejarah commit sebelum membagikan branch, pakai
`Tools/check-atribusi-commit.sh`:

```bash
bash Tools/check-atribusi-commit.sh "origin/master..HEAD"
```

Skrip itu hanya memeriksa pesan commit, bukan isi berkas.

## Struktur repositori

| Folder | Isi |
|---|---|
| `SuDesApp.Core/` | Logika bisnis: data (SQLite/Dapper), layanan, generator surat di atas mesin PDF sendiri (SudesPdf), API desa, integrasi Google Drive |
| `SuDesApp.Wpf/` | Aplikasi WPF: ViewModel, View, tema, navigasi |
| `SuDesApp.Api/` | Backend HTTP (ASP.NET Core) di atas `Core` untuk klien mobile/browser — akun, data warga, register surat (`docs/backend-web.md`) |
| `SudesPdf.Core/` | Mesin PDF aplikasi sendiri: penulis dokumen, penata letak surat, pembaca, dan penggambar halaman — tanpa pustaka PDF pihak ketiga |
| `SudesPdf/` | Lapisan WPF untuk mesin tadi: pratinjau halaman, pembungkus dokumen, dan cetak |
| `SudesExPlus/` | Pustaka .xlsx milik aplikasi sendiri (baca/tulis, gaya, rumus) pengganti EPPlus |
| `SuDesApp.Server/` | Server lisensi (ASP.NET Core) — token bertanda tangan, katalog, tagihan, dan pusat administrasi |
| `android/` | Pendamping Android (Kotlin): buat surat & PDF lokal, antrean via Supabase; desktop adalah otoritas kop & nomor |
| `supabase/` | Migrasi & skema Supabase untuk sinkron, perangkat, dan pairing |
| `docs/` | Dokumentasi kebijakan & API (tautan di bawah) |
| `Bawaan/`, `Templates/`, `Resources/`, `installer/`, `Tools/` | Contoh bawaan aplikasi (JSON), template formulir, sumber daya, installer, alat bantu |

## Lisensi

Kode sumber SuDesApp dilisensikan **MIT** — lihat [`LICENSE`](LICENSE).
Penggunaan aplikasi untuk pemerintahan desa mengikuti ketentuan langganan dan
lisensi pada [`docs/langganan-dan-lisensi.md`](docs/langganan-dan-lisensi.md)
serta [`docs/syarat-ketentuan.md`](docs/syarat-ketentuan.md). Nama, logo, dan
identitas Sumberjaya Dev/PT Duta Publik tidak ikut dilisensikan untuk dipakai
sebagai merek produk turunan.

## Dokumentasi

- [`docs/api-desa.md`](docs/api-desa.md) — API desa lokal di komputer kantor
- [`docs/backend-web.md`](docs/backend-web.md) — backend `SuDesApp.Api` untuk klien mobile/browser
- [`docs/alur-persetujuan-surat.md`](docs/alur-persetujuan-surat.md) — alur persetujuan opsional
- [`docs/langganan-dan-lisensi.md`](docs/langganan-dan-lisensi.md) — langganan berbayar & lisensi bertanda tangan
- [`docs/deploy-server.md`](docs/deploy-server.md) — deploy server lisensi (Railway/Render/VPS)
- [`docs/policy-peran-pengguna.md`](docs/policy-peran-pengguna.md) — peran & izin pengguna
- [`docs/policy-tutup-buku-dan-retensi.md`](docs/policy-tutup-buku-dan-retensi.md) — tutup buku tahunan & retensi
- [`docs/kebijakan-privasi.md`](docs/kebijakan-privasi.md) — kebijakan privasi: data lokal vs data server
- [`docs/syarat-ketentuan.md`](docs/syarat-ketentuan.md) — syarat & ketentuan layanan (lisensi, pembayaran, tanggung jawab)
- [`docs/tema-dan-warna.md`](docs/tema-dan-warna.md) — aturan warna & tema (agar semua bagian ikut tema)
- [`docs/arsitektur-pdf-surat.md`](docs/arsitektur-pdf-surat.md) — struktur lapisan pembuatan PDF surat (kop, renderer, generator)
- [`docs/referensi-penomoran-surat.md`](docs/referensi-penomoran-surat.md) — acuan penomoran
- [`docs/PEMBARUAN-TAMBALAN.md`](docs/PEMBARUAN-TAMBALAN.md) — mekanisme pembaruan kecil
