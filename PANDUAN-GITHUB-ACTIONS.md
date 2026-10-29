# Panduan: Membereskan GitHub Actions yang gagal instan, lalu menerbitkan rilis

> ## PENTING (per 13 Oktober 2026) - sebagian tidak berlaku lagi
>
> Panduan ini ditulis untuk Pascadar **v2.5.4**. Beberapa bagian sudah tidak
> akurat setelah workflow diperbaiki; baca cetaknya sebelum mengikuti.
>
> **Yang sudah berubah:**
>
> - **Rilis lewat tag sudah tidak gagal di gerbang kredensial Google.**
>   GitHub Actions memang tidak pernah punya `GoogleClientCredentialsBawaan.lokal.cs`
>   (berkas itu sengaja tidak terlacak), jadi gerbang itu kini hanya berjalan untuk
>   jalur manual — bukan berarti tag otomatis ditolak.
> - **CI sekarang menjalankan build dan test.** Job `build-test`
>   beserta gerbang "proyek inti harus terlacak", pengunggahan hasil uji, dan
>   pemeriksaan tautan markdown.
> - **Rilis sekarang bergerbang CI dan konfigurasi lisensi.** `release.yml`
>   menolak terbit bila `LISENSI_KUNCI_PUBLIK` kosong atau URL server bukan HTTPS.
> - **Rilis ditandatangani Authenticode.** `release.yml` menolak terbit tanpa
>   sertifikat (`WINDOWS_SIGNING_PFX_BASE64`); langkah dan secret-nya ada di
>   [`docs/TANDA-TANGAN-RILIS.md`](docs/TANDA-TANGAN-RILIS.md).
> - **Bagian "Cara A / Cara B / Cara C" sudah usang.** Rilis berjalan lewat
>   `Tools/rilis-sekali-jalan.ps1 <versi>`. Jalur cadangan manual di bawah
>   tetap berlaku.
>
> **Yang masih berlaku:** Langkah 0 (baca anotasi), diagnosis masalah akun, dan
> Jalur cadangan — rilis lokal tanpa Actions.

Berlaku untuk repo **inomixstudio/SudesApp**. Ditulis untuk gejala yang kita alami:
**semua** workflow (CI maupun Release) gagal dalam ~3 detik, job-nya tidak menjalankan
satu pun langkah. Gejala seperti itu hampir selalu masalah **akun** (penagihan/budget
atau izin Actions), bukan isi workflow-nya.

> Panduan ini adalah catatan operasional internal, bukan panduan pengguna akhir.
> Isinya soal alamat GitHub dan alur rilis internal, bukan cara memakai
> aplikasi desa. Berkasnya tetap di akar repo karena dirujuk dari `docs/`.
>
> Perlu merilis **sekarang** tanpa menunggu Actions? Langsung ke bagian **Jalur
> cadangan — rilis lokal tanpa Actions** di bawah; installer dan `patch.json` bisa
> dibuat dan diunggah dari komputer ini.

---

## Langkah 0 — Baca pesan anotasinya (1 menit, jangan dilewati)

1. Buka https://github.com/inomixstudio/SudesApp/actions
2. Klik run yang merah (biasanya pada commit terakhir atau tag `v2.5.4`).
3. Lihat kotak anotasi di bagian atas halaman run — pesannya menentukan langkah mana
   yang perlu:

| Pesan yang muncul | Penyebab | Langkah |
|---|---|---|
| "...recent account payments have failed or your spending limit needs to be increased. Please check the 'Billing & plans' section..." | Penagihan / limit kuota | **1** |
| "...an Actions budget is preventing further use" | Budget Actions menghentikan pemakaian | **1** |
| "Actions is disabled for this repository" / "Workflows aren't being run on this repository" | Izin Actions dimatikan | **2** |
| Tidak ada anotasi apa pun, hanya gagal 3 detik | Cek izin baca Actions + status GitHub | **2**, lalu **6** |

---

## Langkah 1 — Billing & budget Actions (penyebab paling sering)

1. Buka **https://github.com/settings/billing** (avatar → *Settings* → *Billing and
   licensing*). Sementara itu, rilis bisa tetap diterbitkan lewat jalur lokal di bawah.
2. Pastikan **Payment information** sah: kartu belum kedaluwarsa dan tidak ada
   pembayaran gagal.
3. Buka **Budgets and alerts** → **https://github.com/settings/billing/budgets**,
   lalu cari entri untuk produk **Actions**:
   - Ada budget dengan batas **$0**, atau bertanda *Stop usage when budget limit is
     reached* → naikkan batasnya (mis. $5) atau matikan penghentian penggunaannya.
   - Belum ada budget Actions → klik **New budget**, pilih produk Actions, beri batas
     kecil (mis. $5).
   - Catatan: UI baru GitHub mengganti istilah lama "spending limit" dengan "budgets".
     **Batas $0 berarti Actions dilarang berjalan sama sekali** — inilah yang membuat
     job gagal sebelum langkah pertama.
4. Periksa juga **Actions** di halaman *Usage*: repo **privat** memakai jatah menit
   berbayar, sedangkan repo **publik** gratis 2.000 menit/bulan. Bila kuota bulan ini
   habis, tunggu reset tanggal 1 atau naikkan budget.
5. Setelah disimpan, **tunggu sekitar satu menit** — perubahan penagihan tidak langsung
   aktif.

---

## Langkah 2 — Izin Actions untuk repo

1. Buka **https://github.com/inomixstudio/SudesApp/settings/actions**
   (*Settings* → *Actions* → *General*).
2. Bagian **Actions permissions**: pilih **Allow all actions and reusable workflows**.
   Workflow repo ini memakai `actions/checkout@v4` dan `actions/setup-dotnet@v4`
   (aset rilis diunggah oleh `Tools/publish-release.ps1`, bukan action pihak ketiga).
3. Kalau memilih **Allow select actions**, centang juga **Allow actions created by
   GitHub** dan **Allow Marketplace actions by verified creators**, supaya kedua action
   di atas tidak ikut diblokir.
4. Bagian **Workflow permissions**: pilih **Read and write permissions** — workflow
   Release butuh itu untuk membuat GitHub Release dan mengunggah aset.
5. Bila repo berada di bawah **organisasi**, periksa juga kebijakan tingkat organisasi
   (Organization → *Settings* → *Actions* → *General*): kebijakan organisasi menimpa
   pengaturan repo, jadi repo bisa terlihat "sudah benar" tetapi tetap diblokir di sana.

---

## Langkah 3 — Secret rilis dan hal lain yang perlu diketahui

- **Secret yang wajib untuk rilis:** `WINDOWS_SIGNING_PFX_BASE64`,
  `WINDOWS_SIGNING_PFX_PASSWORD`, `LISENSI_URL_SERVER`, dan
  `LISENSI_KUNCI_PUBLIK` — lihat
  [`docs/TANDA-TANGAN-RILIS.md`](docs/TANDA-TANGAN-RILIS.md). Kredensial Google
  **tidak ikut** pada paket hasil CI: berkas rahasia build
  (`SuDesApp.Core/Utilities/GoogleClientCredentialsBawaan.lokal.cs`) sengaja tidak ada di
  repo, sehingga paket CI hanya memuat placeholder dan **tidak bisa menyambungkan akun
  Google** (pencadangan ke Google Drive) di komputer pengguna.
- Karena itu workflow Release membuat paket CI dengan `-IzinkanTanpaKredensial`:
  asetnya tetap terbit, tetapi status **"Kredensial Google TIDAK tertanam"** ikut
  tertulis di `RELEASE.md` dan teknisi mengisinya di Pengaturan Aplikasi. Untuk rilis
  yang fitur Google-nya langsung siap dipakai, bangun asetnya di komputer yang memiliki
  berkas rahasia itu — lihat **Jalur cadangan** di bawah.
- Jangan pernah menaruh `GoogleClientCredentialsBawaan.lokal.cs` atau token apa pun ke
  Secrets repo.
- `choco install innosetup` di workflow Release berjalan memakai Chocolatey bawaan
  runner dan tidak butuh kredensial.
- Anda hanya perlu peran **Write** (atau Admin) di repo untuk membuat tag dan rilis.

---

## Langkah 4 — Jalankan ulang rilis v2.5.4

Pilih **salah satu** cara berikut, jangan dua-duanya bersamaan.

### Cara A — jalankan ulang run yang gagal (paling cepat)

1. Buka https://github.com/inomixstudio/SudesApp/actions/workflows/release.yml
2. Klik run merah pada tag **v2.5.4** (commit `8c033b5`).
3. **Re-run all jobs** (kanan atas) → konfirmasi **Re-run all jobs**.
4. Catatan penting: run yang diulang memakai berkas workflow **dari commit yang di-tag**.
   Commit `v2.5.4` belum memuat gerbang verifikasi CI yang baru ditambahkan, jadi rilis
   ini terbit **tanpa** gerbang verifikasi CI lebih dulu. Hasil rilisnya tetap sah.

### Cara B — jalankan manual dari branch main (memakai workflow terbaru)

1. Buka https://github.com/inomixstudio/SudesApp/actions/workflows/release.yml
2. **Run workflow** (kanan) → branch **main**, **Version: `2.5.4`** → **Run workflow**.
3. Bila perubahan workflow (gerbang verifikasi + cache) sudah di-commit di `main`, job
   **Verifikasi (build)** berjalan lebih dulu; rilis hanya terbit kalau
   job itu hijau.
4. Karena tag `v2.5.4` sudah ada, GitHub memakai tag itu (tidak memindahkannya). Bila
   release untuk versi ini sudah pernah ada, asetnya diperbarui, bukan dibuat ganda.

### Cara C — agar tag juga melewati gerbang CI (untuk rilis berikutnya)

Commit dulu perubahan `.github/workflows/*`, lalu buat tag **baru** (mis. `v2.5.5`) dan
push:

```bash
git push origin main
git tag -a v2.5.5 -m "SuDesApp 2.5.5"
git push origin v2.5.5
```

Sejak commit itu, tag apa pun yang di-push akan gagal lebih awal bila build
merah — persis tujuan gerbang verifikasi.

Skrip `Tools/rilis.ps1` mengerjakan seluruh urutan itu sekaligus: naikkan versi, jalankan
gerbang build, commit, tag, lalu push.

```powershell
# Latihan aman: tidak ada commit/tag/push, berkas versi dikembalikan
./Tools/rilis.ps1 2.5.5 -UjiCoba

# Rilis sungguhan + butir catatan rilis (boleh beberapa butir, dipisah koma)
./Tools/rilis.ps1 2.5.5 -Peningkatan "Tabel register kini punya tinggi baris pilihan."

# Catatan rilis panjang: tulis di berkas berbutir (bagian diawali "#")
./Tools/rilis.ps1 2.5.5 -CatatanBerkas artifacts/catatan-rilis.txt
```

Skrip menolak jalan bila working tree kotor atau cabang aktif bukan cabang rilis
(default `main`; sebutkan `-Cabang upgrade-dotnet-10` bila rilis dibuat dari cabang
kerja), menuntut versi
baru lebih tinggi dari versi sekarang, dan berhenti tanpa commit bila build gagal.
Versi assembly **dan** entri Catatan Rilis versi baru diisi bersamaan supaya kartu
versi terbaru di halaman Catatan Rilis selalu cocok dengan `AssemblyVersion` di csproj.
Tambahkan `-TanpaDorong` bila commit dan tag ingin
dibuat lokal dulu, lalu push sendiri.

---

## Rilis sekali-jalan (satu perintah)

`Tools/rilis-sekali-jalan.ps1` menggabungkan seluruh urutan rilis — naik versi, commit,
tag, push, publish, installer, tambalan, `patch.json` — lalu opsional mengunggahnya.
Skrip ini memanggil `rilis.ps1` dan `publish-release.ps1` yang sama, jadi hasilnya
identik dengan menjalankannya berurutan.

```powershell
# Satu perintah: versi + commit + tag + push + publish + patch.json
./Tools/rilis-sekali-jalan.ps1 2.5.6 -Peningkatan "Tabel register lebih rapi."

# Sekaligus unggah ke GitHub Releases (butuh token — lihat bagian Unggah di bawah)
./Tools/rilis-sekali-jalan.ps1 2.5.6 -CatatanBerkas artifacts/catatan-rilis.txt -Unggah

# Latihan lengkap: tidak ada commit/tag/push dan tidak ada unggahan
./Tools/rilis-sekali-jalan.ps1 2.5.6 -UjiCoba
```

Versi rilis sebelumnya dideteksi dari `AssemblyVersion` di csproj sebelum dinaikkan;
zip pembandingnya diambil dari `artifacts/` bila ada, atau diunduh otomatis dari rilis
GitHub sehingga pembaruan kecil terbentuk dari komputer mana pun tanpa `-VersiLama`
(hanya bila rilis pembanding tidak ada sama sekali, `patch.json` jatuh ke jenis
"besar" — aplikasi terpasang memakai installer penuh). Cabang rilis diambil dari
cabang yang sedang aktif, jadi rilis dari cabang kerja tidak perlu pindah ke `main`
lebih dulu.

---

## Jalur cadangan — rilis lokal tanpa Actions (`Tools/publish-release.ps1`)

Selama Actions belum bisa berjalan, rilis tetap bisa dibuat dan diunggah dari komputer
ini. Skrip `Tools/publish-release.ps1` mengerjakan urutan yang sama dengan
`release.yml`: publish self-contained win-x64 → jaring pengaman `docs/` → zip portable →
installer Inno Setup → tambalan (bila ada rilis sebelumnya) → `patch.json` → catatan
rilis berisi SHA-256 → daftar aset untuk diunggah manual (atau unggah otomatis).

**Prasyarat**

- **.NET 10 SDK** di PATH — wajib; skrip berhenti lebih awal bila tidak ada.
- **Inno Setup 6** untuk installer: `winget install -e --id JRSoftware.InnoSetup`
  (atau https://jrsoftware.org/isdl.php). Tanpa itu skrip tetap berjalan dan hanya
  memberi peringatan — installer tidak terbentuk, jadi pakai `-SkipInstaller` bila
  installer memang belum diperlukan.

### 1. Bangun asetnya

```powershell
# Rilis penuh: zip portable + installer + patch.json (+ tambalan bila rilis lama ada)
./Tools/publish-release.ps1 -Version 2.5.5 -VersiLama 2.5.4

# Tanpa installer (mis. Inno Setup belum terpasang)
./Tools/publish-release.ps1 -Version 2.5.5 -SkipInstaller

# Dengan catatan rilis yang muncul di notifikasi pengguna
./Tools/publish-release.ps1 -Version 2.5.5 -VersiLama 2.5.4 -Ringkasan artifacts/RINGKASAN.txt
```

Bila `-VersiLama` diberi **tanpa** `-PublishLama`, skrip memakai zip portable rilis
lama di folder `artifacts/` (mis. `artifacts/SuDesApp_2.5.4_win-x64.zip`) sebagai
pembanding; **bila zip itu belum ada, skrip mengunduhnya sendiri dari rilis GitHub**
(token dari `GITHUB_TOKEN`/Git Credential Manager) dan memverifikasi sha256-nya
terhadap digest aset, lalu menyimpannya di `artifacts/` sebagai cache. Ringkasan
notifikasi juga dibuat otomatis dari judul commit sejak tag rilis lama
(`artifacts/RINGKASAN.txt`) — sama seperti jalur CI.

Skrip memperingatkan bila masih ada perubahan yang belum di-commit: aset yang dibangun
tidak akan sama dengan isi tag, sedangkan aplikasi membandingkan versi lewat tag itu.
`-LewatiPemeriksaan` hanya untuk uji coba.

Skrip juga memeriksa **kredensial OAuth Google**: hasil publish yang tidak memuat
kredensial tertanam akan **menghentikan** proses sebelum zip/installer dibuat, supaya
tidak ada lagi installer yang terbit dalam keadaan fitur Google-nya tidak bisa
disambungkan. Jalankan
kapan pun untuk memeriksa paket yang sudah jadi:

```powershell
./Tools/check-kredensial-google.ps1                    # memeriksa artifacts/publish
./Tools/check-kredensial-google.ps1 -Folder <folder>   # folder publish lain
```

Bila rilis tanpa kredensial Google memang disengaja, tambahkan `-IzinkanTanpaKredensial`
pada `publish-release.ps1` (statusnya ikut tertulis di `artifacts/RELEASE.md`). Untuk
paket yang sudah terpasang tanpa kredensial, koneksi Google tetap bisa dihidupkan di
komputer pengguna lewat **Pengaturan Aplikasi → Kredensial Google** → isi Client ID &
Client Secret → **Simpan Kredensial** → **Uji Koneksi** (tersimpan terenkripsi DPAPI per
pengguna Windows).

### 2. Hasilnya (semuanya di `artifacts/`, folder ini gitignored)

| Berkas | Isi |
|---|---|
| `SuDesApp_<versi>_win-x64.zip` | aplikasi portable (± 90 MB) |
| `SuDesApp_<versi>_Setup.exe` | installer Inno Setup — tidak ada bila `-SkipInstaller` |
| `patch.json` | manifest pembaruan; **wajib diunggah**, aplikasi terpasang membacanya |
| `patch-<versi>.zip` | hanya berkas yang berubah (bila jenisnya "kecil") |
| `RELEASE.md` | catatan rilis + SHA-256 installer |
| `UNGGAH.txt` | daftar aset, ukuran, SHA-256, dan tautan halaman rilis |

### 3. Unggah

Manual (paling aman, tanpa token di komputer):

1. Buka tautan `releases/new?tag=v<versi>` yang tercetak di akhir keluaran skrip.
2. Seret semua berkas yang tercantum di `artifacts/UNGGAH.txt` — **jangan lupa
   `patch.json`**; tanpa berkas itu aplikasi terpasang menganggap rilis tidak punya
   keterangan pembaruan dan meminta installer penuh.
3. Salin isi `artifacts/RELEASE.md` ke badan rilis, lalu **Publish release**.

Otomatis lewat REST API (tidak butuh `gh`):

```powershell
$env:GITHUB_TOKEN = '<token dengan scope repo>'
./Tools/publish-release.ps1 -Version 2.5.5 -VersiLama 2.5.4 -Unggah
```

Skrip membuat rilis sebagai **draft**, mengunggah seluruh aset, baru menerbitkannya —
jadi pengguna tidak pernah melihat rilis setengah terunggah. Aset bernama sama
diganti, bukan ditolak (GitHub menolak nama kembar dengan galat 422). Tag `v<versi>`
harus sudah ada di origin; skrip menolak mengunggah bila belum, karena GitHub akan
membuat tag itu dari cabang default dan rilis menunjuk commit yang salah. Token scope
`repo` dibuat di
https://github.com/settings/tokens dan hanya disimpan di variabel lingkungan —
jangan pernah ditulis ke berkas repo. Bila `GITHUB_TOKEN` tidak diset, skrip memakai
kredensial `github.com` yang tersimpan di Git Credential Manager — token yang sama
dengan yang dipakai `git push`.

### 4. Setelah Actions pulih

Keduanya boleh dipakai bergantian: jalur lokal untuk merilis cepat (atau saat Actions
bermasalah), `release.yml` untuk rilis dari tag. Hasil asetnya sama, jadi
pengguna tidak merasakan bedanya.

---

## Langkah 5 — Tanda berhasil

- Halaman run berisi langkah-langkah nyata (bukan gagal 3 detik tanpa langkah).
- Release terbit di https://github.com/inomixstudio/SudesApp/releases dengan aset
  `SuDesApp_2.5.4_Setup.exe`, `SuDesApp_2.5.4_win-x64.zip`, dan `patch.json`.
- Catatan rilis memuat **SHA-256** installer (dipakai aplikasi memverifikasi unduhan).
- Unduh installernya sekali untuk memastikan berkas benar-benar bisa dipasang.

---

## Langkah 6 — Bila masih gagal

1. **Baca anotasi run** dan tempelkan pesannya saat meminta bantuan — pesan itu
   menentukan langkah berikutnya, bukan "workflow gagal".
2. Cek https://www.githubstatus.com bila banyak repo gagal berbarengan.
3. Jangan menghapus dan memasang ulang workflow: berkasnya sudah benar (build
   lolos di komputer lokal: 0 error 0 warning). Fokus pada pesan dari
   GitHub.
