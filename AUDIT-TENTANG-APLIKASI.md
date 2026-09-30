# Audit: Halaman "Tentang Aplikasi" & Alur Pembaruan

Catatan internal (akar repo, **bukan** `docs/`) — dijalankan 29 September 2026 atas
permintaan pemelihara. Setiap temuan bertingkat **Tinggi/Sedang/Rendah**, dan yang sudah
dikerjakan ditandai **BERES** beserta buktinya. Nomor baris mengikuti keadaan **sesudah**
perbaikan.

Halaman ini adalah satu-satunya tempat pengguna membaca identitas aplikasi (nama, versi,
pengembang, lokasi berkas) dan sering dikutip saat melapor masalah — karena itu
"kelihatan benar" saja tidak cukup: isinya harus berasal dari sumber yang sama dengan yang
dipakai aplikasi untuk bekerja.

## Ringkasan

| # | Temuan | Tingkat | Status |
|---|--------|---------|--------|
| C1 | Versi di Tentang tampil `v2.5.4.0` sementara status bar/footer `v2.5.4` — satu aplikasi, dua bentuk versi | Sedang | BERES |
| C2 | Identitas ditulis dua kali: `AboutViewModel` membaca atribut assembly sendiri dan tidak memakai `IdentitasAplikasi` | Sedang | BERES |
| C3 | `AssemblyTitle` dibaca dari assembly tetapi **tidak pernah ditampilkan** (0 binding di XAML) | Rendah | BERES |
| C4 | Halaman Pembaruan menampilkan `Versi Saat Ini: 2.5.4.0` — bentuk versi ketiga yang berbeda lagi | Rendah | BERES |
| C5 | Daftar fitur ketinggalan kemampuan yang sudah rilis: Verifikasi Surat, lampiran daftar nama SK perangkat, data perangkat per kelompok jabatan, halaman Dokumentasi, dan enkripsi database | Sedang | BERES |
| C6 | Daftar teknologi tidak akurat: SQLite tanpa SQLCipher, `Pdfium` bukan nama paketnya, Google Forms API (dipakai formulir daring) tak disebut | Rendah | BERES |
| C7 | "Lokasi data" hanya menunjuk folder aplikasi; berkas milik pengguna di `%LOCALAPPDATA%\SuDesApp` tidak disebut sama sekali | Sedang | BERES |
| C8 | URL repo (dan surel, website, donasi) ditulis langsung di XAML 4 kali, padahal `AppConfig.githubRepo` sudah ada — tautan bisa menyimpang dari sumber pembaruan | Sedang | BERES |
| C9 | **Kode verifikasi surat belum sampai ke kertas**: dibuat & disimpan, tetapi tidak dicetak generator mana pun dan QR-nya belum pernah dibuat | Sedang | **belum** |
| C10 | Tidak ada uji untuk isi halaman Tentang — sebab C1–C8 lolos tanpa terdeteksi | Sedang | BERES |
| C11 | Sisa build dari sesi verifikasi (160 MB, tidak ter-ignore) tampil di `git status` | Rendah | BERES |
| C12 | Ganti nama properti view model **tidak** menghasilkan galat build di WPF — binding lama yang tertinggal membuat bagian itu tampil kosong tanpa suara (terjadi saat perbaikan C3: `BagianDetailLengkapView` masih memakai `AssemblyDescription`) | Tinggi (kelas bug) | BERES + penjaga |

---

## 1. Identitas & versi (C1–C4)

| Kode | Temuan | Bukti |
|------|--------|-------|
| C1 | `AssemblyVersion` di-`StringFormat='v{0}'` sehingga tercetak `v2.5.4.0`, sedangkan `IdentitasAplikasi.VersiDenganPrefiks` — yang dipakai status bar, footer Catatan Rilis, dan jendela login — membuang komponen `.0` menjadi `v2.5.4`. Versi berbeda di dua tempat membuat laporan pengguna sulit dicocokkan dengan rilis. | `AboutView.xaml` (dulu `{Binding AssemblyVersion, StringFormat='v{0}'}`), `IdentitasAplikasi.cs:43,57` |
| C2 | Dokumen `IdentitasAplikasi` menyebut dirinya "satu sumber kebenaran", tetapi justru halaman Tentang tidak memakainya: `AboutViewModel` membaca `AssemblyProductAttribute`, `AssemblyCompanyAttribute`, `AssemblyCopyrightAttribute`, dan `AssemblyTitleAttribute` sendiri lewat `Assembly.GetExecutingAssembly()`. | `AboutViewModel.cs:37-49` (sekarang dari `IdentitasAplikasi`) |
| C3 | `public string AssemblyTitle` diisi dari atribut assembly, tetapi tidak ada satu pun binding `AssemblyTitle` di XAML — properti mati. | `AboutViewModel.cs` (properti sudah dihapus) |
| C4 | `_currentVersionText = $"Versi Saat Ini: {GetCurrentVersion()}"` menampilkan objek `Version` apa adanya → `2.5.4.0`. `GetCurrentVersion()` tetap dipakai untuk **membandingkan** versi (butuh empat komponen), jadi hanya tampilannya yang diseragamkan. | `PembaruanViewModel.cs:115` |

**Perbaikan:** identitas satu pintu — `IdentitasAplikasi` ditambah `VersiLengkap`
(`IdentitasAplikasi.cs:36`) dan `AboutViewModel` memakai `NamaAplikasi`, `VersiTampil`
(`v2.5.4`), `VersiBuild` (`2.5.4.0`), `Pengembang`, `HakCipta` dari sana. Nomor build
tetap tersedia sebagai baris terpisah **"Nomor build"** untuk laporan/diagnosa
(`BagianDetailAplikasiView.xaml:72`), sehingga tidak ada informasi yang hilang dan tidak ada
lagi versi yang tampil berbeda bentuk.

## 2. Isi halaman (C5–C6)

| Kode | Temuan | Bukti |
|------|--------|-------|
| C5 | Daftar fitur berhenti pada keadaan beberapa rilis lalu. Yang belum tercantum: (a) **Verifikasi Surat** — menu dan layanannya sudah ada; (b) **lampiran tabel daftar nama SK perangkat** multi-orang dengan kolom dari data Warga lewat NIK dan pengelompokan per unit Posyandu; (c) **data perangkat desa per kelompok jabatan**; (d) halaman **Dokumentasi** bawaan; (e) **enkripsi database** (SQLCipher + kunci DPAPI) — properti privasi yang justru penting bagi aplikasi desa dan sama sekali tidak disebut. | `MainWindowViewModel.cs:859-861` (menu Verifikasi Surat), `SkPerangkatGenerator.cs` (lampiran), `EnkripsiDatabase.cs` |
| C6 | Tiga entri teknologi tidak akurat: `SQLite` tanpa menyebut SQLCipher padahal paketnya `SQLitePCLRaw.bundle_e_sqlcipher`; `"Pdfium"` padahal paketnya `PdfiumViewer.Updated`; Google Forms API dipakai untuk formulir daring tetapi tidak tercantum. | `SuDesApp.Core.csproj:33-34`, `SuDesApp.Wpf.csproj:40-41` |

**Perbaikan:** daftar fitur 18 → 23 butir dan daftar teknologi 11 → 12 butir
(`AboutViewModel.cs:183-250`). Deskripsi panjang juga menyebut verifikasi surat dan
lampiran SK per unit. Jumlah di lencana hero ikut sendiri karena dihitung dari koleksi.

## 3. Lokasi berkas (C7)

| Kode | Temuan | Bukti |
|------|--------|-------|
| C7 | Baris "Lokasi data" menampilkan `AppDomain.CurrentDomain.BaseDirectory` — benar untuk `desa.db` (connection string `Data Source=desa.db` relatif terhadap folder aplikasi, dan installer memasang ke `{localappdata}\Programs\SuDesApp`), tetapi **menyesatkan sebagai janji "data"**: preferensi, kunci database, token Google, dan riwayat pembaruan tidak ada di situ. Pengguna yang mencadangkan folder aplikasi akan mengira seluruh datanya ikut tersalin. | `appsettings.json:3`, `installer/SuDesApp.iss:22`, `KunciDatabase.cs:31-35`, `AppPreferenceStore.cs:26`, `RiwayatPembaruanStore.cs:55` |

**Perbaikan:** dua baris terpisah — **"Folder aplikasi"** (program + `desa.db`) dan
**"Preferensi & kunci"** (`%LOCALAPPDATA%\SuDesApp`) — masing-masing dengan tooltip yang
menyebut isinya (`BagianDetailAplikasiView.xaml:118-136`).

## 4. Tautan (C8)

| Kode | Temuan | Bukti |
|------|--------|-------|
| C8 | `https://github.com/inomixstudio/SudesApp` ditulis langsung sebagai `Tag`, `ToolTip`, dan teks kartu; surel `sumberjayadev@gmail.com`, `https://desa-sumberjaya.com`, dan `https://saweria.co/arieino` pun sama. Padahal `AppConfig.githubRepo` sudah tersedia dan dipakai `UpdateService` untuk memeriksa pembaruan: tautan di halaman bisa menunjuk repo yang berbeda dari tempat aplikasi mengunduh berkas pembaruan (mis. pada pemasangan khusus atau fork). | `AboutViewModel.cs:113-134,170` (sekarang dari `appConfig?.GithubRepo` + properti tautan), `BagianDukunganView.xaml`, `UpdateService.cs:47-48` |

**Perbaikan:** seluruh alamat datang dari view model. Repositori dibaca dari
`AppConfig.githubRepo` (sumber yang sama dengan pemeriksa pembaruan), dan kartu/baris repo
otomatis disembunyikan lewat `HasRepositori` bila `githubRepo` kosong — bukan kartu yang
diam-diam tidak berbunyi.

## 5. Yang belum: kode verifikasi belum sampai ke kertas (C9)

Rantai verifikasi surat saat ini lengkap **di dalam** aplikasi:

1. `SuratRepository.cs:799` membuat `KodeVerifikasiSurat.BuatKode()` (`SD-XXXX-XXXX`) dan
   `:800` menghitung cap SHA-256 data inti surat saat terbit;
2. `KodeVerifikasiSurat.UraiMasukan`/`BuatPayload` (`:86,121`) menyediakan payload untuk
   kode batang dua dimensi, dan `QrCodeSurat.BuatPng` (`:23`) bisa membuat gambarnya;
3. halaman **Verifikasi Surat** (`MainWindowViewModel.cs:861`) dan
   `VerifikasiSuratService` bisa memeriksa kode serta menandai surat yang isinya berubah
   setelah terbit.

Yang **tidak ada**: perantara ke kertasnya.

| Bukti | Keadaan |
|-------|---------|
| `grep KodeVerifikasi SuDesApp.Core/GeneratorPdf` | **0 hasil** — tidak ada generator surat yang mencetak kodenya di kaki surat, padahal komentar `SuratData.cs:52` menyebut "dicetak di kaki surat" dan komentar menu di `MainWindowViewModel.cs:859` menyebut "kode yang tercetak di kaki surat". |
| `grep -rl KodeVerifikasi SuDesApp.Wpf` | **0 hasil** — kodenya bahkan tidak ditampilkan di halaman register/pratinjau. |
| `grep -rn BuatPng` | hanya definisi (`QrCodeSurat.cs:23`) — **tidak ada pemanggil**, sehingga paket `QRCoder` ikut terdistribusi tanpa dipakai. |

Akibatnya operator tidak punya cara membaca kode dari surat yang sudah dicetak; satu-satunya
jalan adalah membuka arsip dan memeriksa kode di database. Ini kesenjangan **fitur**, bukan
kesalahan tampilan, jadi sisa pekerjaannya: cetak kode (teks dan/atau QR dari
`BuatPayload`) di kaki surat pada generator, lalu tampilkan kode di panel/pratinjau register
supaya bisa diperiksa tanpa mencetak.

## 6. Uji penjaga (C10, C12) & kebersihan repo (C11)

| Kode | Temuan | Bukti |
|------|--------|-------|
| C10 | Tidak ada uji yang menyentuh halaman Tentang; itulah sebab C1–C8 (termasuk versi yang berbeda bentuk dan properti mati) lolos berbulan-bulan. | `tests/SuDesApp.Core.Tests/TentangAplikasiTests.cs` (baru, 9 uji) |
| C11 | `SuDesApp.Core/binverifikasi-build 2Debug/` dan `SuDesApp.Wpf/binverifikasi-build 2Debug/` (±160 MB) adalah sisa build verifikasi yang jalurnya terpotong spasi; namanya tidak tertangkap pola `.gitignore` (`bin/`, `*-verify/`), sehingga muncul sebagai `??` di `git status` dan bisa ikut ter-`git add -A`. | sudah dihapus; `git status` bersih dari keduanya |
| C12 | Penamaan ulang properti di view model lolos dari compiler WPF: hanya binding-nya yang salah, dan binding salah **tidak** melempar apa pun — bagian itu tampil kosong. Saat C3 dikerjakan (`AssemblyDescription` → `DeskripsiLengkap`), `BagianDetailLengkapView.xaml` sempat tertinggal dan Deskripsi panjang akan hilang dari halaman tanpa satu pun galat build. | `BagianDetailLengkapView.xaml:30` (sudah diperbaiki) |

Isi `TentangAplikasiTests.cs`:

1. `AboutViewModel` memakai lima acuan `IdentitasAplikasi.*` dan tidak menyebut empat
   atribut assembly (mencegah duplikasi identitas hidup lagi);
2. `IdentitasAplikasi.cs` adalah **satu-satunya** berkas di `SuDesApp.Wpf` yang membaca
   `AssemblyProductAttribute` (pemindaian seluruh `.cs`, `obj/` dan `bin/` dikecualikan);
3. hero & bagian Detail memakai `VersiTampil`/`VersiBuild`, bukan `AssemblyVersion`;
4. XAML Tentang tidak memuat alamat apa pun (`github.com`, `saweria.co`, `mailto:`,
   `desa-sumberjaya.com`) — tautan wajib dari view model;
5. `appsettings.json` → `AppConfig.githubRepo` wajib terisi dan berbentuk `owner/repo`;
6. `githubRepo` dibandingkan dengan remote GitHub di `.git/config` bila ada (mencegah
   konfigurasi pembaruan menunjuk repo yang bukan repo aplikasi ini);
7. daftar fitur wajib menyebut kelima kemampuan pada C5;
8. setiap tautan teknologi wajib `https` dan sah (regex blok `Teknologi`, karena daftar
   "Bagian" memakai bentuk `new(...)` yang sama dengan argumen keterangan);
9. **(C12)** setiap akar `{Binding …}` di seluruh tampilan Tentang wajib ada sebagai
   anggota publik di `AboutViewModel.cs` (berkas itu memuat `AboutViewModel`,
   `AboutBagianViewModel`, dan `TeknologiItem`; `DataContext` dikecualikan karena binding
   relatif). Uji ini dijalankan sekali dengan binding `AssemblyDescription` dikembalikan
   manual untuk memastikan ia benar-benar **gagal** — uji penjaga yang tidak bisa gagal
   tidak menjaga apa pun.

## Verifikasi

| Pemeriksaan | Hasil |
|-------------|-------|
| `dotnet build SuDesApp.Wpf/SuDesApp.Wpf.csproj` (Debug & Release) | 0 error, 0 warning |
| `dotnet test tests/SuDesApp.Core.Tests` (Debug & Release) | **465/465** hijau (456 → 465) |
| `Tools/check-resources.ps1` | LOLOS (7 include literal, 5 glob, 0 orphan) |
| `Tools/check-kontras-wcag.ps1` | LOLOS (63 kunci × 7 tema — tema Kontras Tinggi diuji pada ambang AAA; warna tetap di luar tema) |
| Verifikasi tautan baru | `https://www.zetetic.net/sqlcipher/` → 200; `https://developers.google.com/forms/api` → 200 (alih ke `/workspace/forms/api`) |

## Catatan keadaan repo (bukan bagian audit ini)

`git status` saat audit menunjukkan pekerjaan lain yang belum di-commit dan **tidak
disentuh** oleh audit ini: tiga berkas `UbahSandi*` (ViewModel + View) terhapus di working
tree, sementara `SuDesApp.Core/Services/KataSandiPengguna.cs` dan
`tests/SuDesApp.Core.Tests/KeamananLapisanDataTests.cs` masih untracked. Aplikasi tetap
build dan seluruh uji hijau dengan keadaan itu. Jangan menambahkan berkas secara borongan
(`git add -A`) sebelum keadaan itu diputuskan pemiliknya.

---

## Addendum 30 September 2026 — audit ulang bersama halaman Catatan Rilis

Pemelihara meminta audit **Catatan Rilis** disertakan. Pemeriksaan ulang ternyata
menemukan bahwa tiga perbaikan yang sudah tertanda **BERES** di atas ternyata **tidak ada
di berkas kerja** (kemungkinan tertimpa saat penggabungan pengerjaan): C4, C6, dan C7.
Ketiganya dikerjakan ulang, ditambah dua temuan baru (D1, D2).

| Kode | Keadaan saat audit ulang | Tindakan |
|------|--------------------------|----------|
| C1–C3 | Sesuai dokumen — hero & Detail memakai `VersiTampil`/`VersiBuild`; identitas dari `IdentitasAplikasi` | — |
| C4 | **Kambuh**: `PembaruanViewModel.cs:111` masih `$"Versi Saat Ini: {GetCurrentVersion()}"` → `2.5.4.0` | Dikerjakan ulang: tampilan memakai `IdentitasAplikasi.VersiDenganPrefiks`; `GetCurrentVersion()` tetap untuk **membandingkan** versi |
| C5 | Sesuai dokumen (23 butir fitur) | — |
| C6 | **Kambuh**: teknologi masih `SQLite` (tanpa SQLCipher), `Pdfium`, tanpa Google Forms API | Dikerjakan ulang: `SQLite + SQLCipher` (zetetic.net), `PdfiumViewer`, + `Google Forms API` |
| C7 | **Kambuh**: satu baris "Lokasi data" (`BaseDirectory`) tanpa `%LOCALAPPDATA%\SuDesApp` | Dikerjakan ulang: dua baris **"Folder aplikasi"** + **"Preferensi & kunci"** (`LokasiPreferensi` dari `LocalApplicationData`) dengan tooltip isi masing-masing |
| C9 | Masih terbuka (kesenjangan fitur — kode verifikasi belum dicetak di kaki surat) | Tetap dicatat terbuka |
| C10–C12 | Sesuai dokumen | — |
| **D1** | **Kartu versi terbaru Catatan Rilis tertinggal di 2.5.3** padahal AssemblyVersion csproj sudah **2.5.4.0** sejak commit `8c033b5` — halaman memakai `MaksKartu = 3`, jadi rilis terbaru tak pernah tampil | Entri **Versi 2.5.4 (30 September 2026)** disusun: API Desa (menu baru, kunci ber-cakupan, verifikasi surat via HTTP), lokasi berkas database dapat dipilih (Pengaturan → Database Desa, dengan pemeriksaan berkas + konfirmasi), peningkatan halaman Tentang, dan perbaikan halaman kosong/tinggi baris tabel; `IsLatest` bergeser ke 2.5.4 |
| **D2** | Daftar fitur Tentang tidak menyebut **enkripsi database SQLCipher** (sudah dirilis jauh sebelumnya) maupun **API Desa** | Keduanya ditambahkan ke daftar fitur (23 → 25 butir) dan paragraf deskripsi detail lengkap |

Penjaga baru: `tests/SuDesApp.Core.Tests/CatatanRilisDanTentangTests.cs` — 8 uji berbasis
sumber: (1) entri pertama Catatan Rilis wajib bernomor sama dengan `AssemblyVersion` csproj
dan berstatus terbaru; (2) tepat satu kartu terbaru, di paling atas; (3) format
"Versi x.y.z (tanggal)" pada seluruh entri; (4) entri terbaru mencatat fitur rilisnya;
(5) daftar fitur Tentang menyebut SQLCipher, DPAPI, dan API Desa; (6) blok teknologi
menyebut SQLCipher/PdfiumViewer/Google Forms API dan tidak menghidupkan kembali nama lama;
(7) Detail aplikasi membedakan "Folder aplikasi" dan "Preferensi & kunci";
(8) "Versi Saat Ini" halaman Pembaruan memakai bentuk status bar.

Verifikasi: `dotnet build SuDesApp.slnx` 0 error 0 warning; `dotnet test` **489/489**
hijau (481 → 489). Semua perubahan belum di-commit (mengikuti kebijakan sesi).
