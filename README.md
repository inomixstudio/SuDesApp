# SuDesApp — Sistem Surat Desa

Aplikasi desktop Windows (WPF) untuk administrasi surat-menyurat desa: pembuatan
surat resmi dengan penomoran sesuai perbup, register & agenda digital, data
penduduk lengkap dengan mutasi, layanan permintaan surat lewat website/API desa, sampai
alur persetujuan (ajukan → verifikasi → tanda tangan) yang opsional per surat.

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
- **Layanan online** — warga mengisi sendiri **Formulir Warga** di website
  (`/layanan-online`, tanpa akun); permintaan langsung masuk antrean operator,
  sama dengan kiriman sistem desa lewat API desa. Kanal WhatsApp dan Google
  Form/Sheet sudah dihapus; login Google kini hanya untuk pencadangan ke
  Google Drive.
- **Data warga & register surat di browser** — halaman `/modul/warga` dan
  `/modul/register-surat` membaca database desa yang sama lewat backend:
  pencarian, penyaring status/tahun/jenis, dan paginasi memakai kueri Core yang
  juga dipakai desktop, jadi jumlah dan urutannya tidak bisa berbeda. Keduanya
  masih baca-saja sampai formulir tulisnya diporting.
- **Multi-pengguna** — akun dengan peran & izin, kunci akun setelah percobaan
  login gagal, dan halaman **Pengguna & Peran** di web untuk membuat akun,
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

- Windows 10/11 dengan **.NET 10 SDK** (build) atau runtime .NET 10 (menjalankan).
  Versi SDK dikunci lewat `global.json` ke **10.0.401** sehingga build developer dan
  CI (GitHub Actions) selalu memakai SDK yang sama; bila mesin Anda belum punya
  10.0.401, pasang dulu lewat `winget install Microsoft.DotNet.SDK.10` atau https://dotnet.microsoft.com/download.
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

# Proyek lintas platform (desktop + Android + browser)
dotnet build SuDesApp.Api/SuDesApp.Api.csproj --nologo -v q
dotnet build SuDesApp.Web/SuDesApp.Web.csproj --nologo -v q
dotnet build SuDesApp.Maui/SuDesApp.Maui.csproj -f net10.0-windows10.0.19041.0 --nologo -v q
dotnet build SuDesApp.Maui/SuDesApp.Maui.csproj -f net10.0-android --nologo -v q
```

Jalankan backend dan web lalu buka `http://127.0.0.1:5189`:

```bash
dotnet run --project SuDesApp.Api --urls http://127.0.0.1:5188
dotnet run --project SuDesApp.Web --urls http://127.0.0.1:5189
```

GitHub Actions menjalankan restore, build Release, dan uji otomatis pada Windows
untuk setiap pull request dan push ke `main` (`.github/workflows/ci.yml`). Cakupan
uji saat ini mencakup tanda tangan token lisensi, termasuk kompatibilitas verifikasi
server dan aplikasi desktop. Workflow publikasi `.github/workflows/release.yml`
ikut disertakan dan menandatangani aset dengan Authenticode — lihat
[`docs/TANDA-TANGAN-RILIS.md`](docs/TANDA-TANGAN-RILIS.md).
Untuk pemeriksaan kontras tema, jalankan `Tools/check-kontras-wcag.ps1` secara
terpisah (lihat [`docs/tema-dan-warna.md`](docs/tema-dan-warna.md)). Skrip
`Tools/publish-release.ps1` dan `Tools/rilis-sekali-jalan.ps1` menyiapkan aset
rilis lokal; jalankan pemeriksaan rilis dan tinjau aset sebelum membagikan
installer.

## Hook commit

Repo menyertakan `.githooks/commit-msg` yang menolak pesan commit berisi
atribusi agen: baris `Co-Authored-By` bernama Codebuff, teks
`Generated with Codebuff`, atau alamat `noreply@codebuff.com`. Aktifkan sekali
per clone:

```bash
git config core.hooksPath .githooks
```

Hook hanya memeriksa pesan commit, bukan isi berkas. Bila benar-benar perlu
dilewati, pakai `git commit --no-verify`. Alasannya ada di komentar berkas
hook: atribusi semacam itu pernah masuk ke 37 commit dan harus ditulis ulang
dengan force-push agar tidak muncul di daftar kontributor GitHub.

Aturan yang sama diperiksa lagi di CI lewat job **Audit atribusi commit**
(`Tools/check-atribusi-commit.sh`). Jadi pull request tetap gagal meski hook
di clone penulis belum diaktifkan atau commit dibuat dengan `--no-verify`.

## Struktur repositori

| Folder | Isi |
|---|---|
| `SuDesApp.Core/` | Logika bisnis: data (SQLite/Dapper), layanan, generator surat di atas mesin PDF sendiri (SudesPdf), API desa, integrasi Google Drive |
| `SuDesApp.Wpf/` | Aplikasi WPF: ViewModel, View, tema, navigasi |
| `SuDesApp.Api/` | Backend web (ASP.NET Core) di atas `Core` — sumber data tunggal untuk desktop, Android, dan browser |
| `SuDesApp.Ui/` | Komponen Blazor bersama (net10.0): layout, menu utama, halaman modul — dipakai ketiga platform |
| `SuDesApp.Maui/` | Host MAUI Blazor Hybrid: desktop Windows dan Android |
| `SuDesApp.Web/` | Host Blazor Web untuk browser (render server) |
| `SudesPdf.Core/` | Mesin PDF aplikasi sendiri: penulis dokumen, penata letak surat, pembaca, dan penggambar halaman — tanpa pustaka PDF pihak ketiga |
| `SudesPdf/` | Lapisan WPF untuk mesin tadi: pratinjau halaman, pembungkus dokumen, dan cetak |
| `SudesExPlus/` | Pustaka .xlsx milik aplikasi sendiri (baca/tulis, gaya, rumus) pengganti EPPlus |
| `docs/` | Dokumentasi kebijakan & API (tautan di bawah) |
| `Bawaan/`, `Templates/`, `Resources/`, `installer/`, `Tools/` | Contoh bawaan aplikasi (JSON), template formulir, sumber daya, installer, alat bantu |

## Dokumentasi

- [`docs/api-desa.md`](docs/api-desa.md) — API desa untuk sistem luar
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
