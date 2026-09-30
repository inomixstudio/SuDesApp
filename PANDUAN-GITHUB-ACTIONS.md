# Panduan: Membereskan GitHub Actions yang gagal instan, lalu menerbitkan rilis v2.5.4

Berlaku untuk repo **inomixstudio/SudesApp**. Ditulis untuk gejala yang kita alami:
**semua** workflow (CI maupun Release) gagal dalam ~3 detik, job-nya tidak menjalankan
satu pun langkah. Gejala seperti itu hampir selalu masalah **akun** (penagihan/budget
atau izin Actions), bukan isi workflow-nya.

> Panduan ini adalah catatan operasional internal. Berkasnya sengaja diletakkan di akar
> repo (bukan di `docs/`) karena seluruh isi `docs/` kini ikut ter-bundle ke aplikasi
> yang dipasang pengguna desa.
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
   Workflow repo ini memakai `actions/checkout@v4`, `actions/setup-dotnet@v4`, dan
   `softprops/action-gh-release@v2`.
3. Kalau memilih **Allow select actions**, centang juga **Allow actions created by
   GitHub** dan **Allow Marketplace actions by verified creators**, supaya ketiga action
   di atas tidak ikut diblokir.
4. Bagian **Workflow permissions**: pilih **Read and write permissions** — workflow
   Release butuh itu untuk membuat GitHub Release dan mengunggah aset.
5. Bila repo berada di bawah **organisasi**, periksa juga kebijakan tingkat organisasi
   (Organization → *Settings* → *Actions* → *General*): kebijakan organisasi menimpa
   pengaturan repo, jadi repo bisa terlihat "sudah benar" tetapi tetap diblokir di sana.

---

## Langkah 3 — Hal yang tidak perlu dikhawatirkan

- Tidak ada Secrets/Variables yang wajib diisi untuk rilis. Kredensial Google **tidak**
  dipakai rilis.
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
   ini terbit **tanpa** uji otomatis lebih dulu. Hasil rilisnya tetap sah.

### Cara B — jalankan manual dari branch main (memakai workflow terbaru)

1. Buka https://github.com/inomixstudio/SudesApp/actions/workflows/release.yml
2. **Run workflow** (kanan) → branch **main**, **Version: `2.5.4`** → **Run workflow**.
3. Bila perubahan workflow (gerbang verifikasi + cache) sudah di-commit di `main`, job
   **Verifikasi (build + uji unit)** berjalan lebih dulu; rilis hanya terbit kalau
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

Sejak commit itu, tag apa pun yang di-push akan gagal lebih awal bila build atau uji
merah — persis tujuan gerbang verifikasi.

---

## Jalur cadangan — rilis lokal tanpa Actions (`Tools/publish-release.ps1`)

Selama Actions belum bisa berjalan, rilis tetap bisa dibuat dan diunggah dari komputer
ini. Skrip `Tools/publish-release.ps1` mengerjakan urutan yang sama dengan
`release.yml`: publish self-contained win-x64 → jaring pengaman `docs/` → zip portable →
installer Inno Setup → tambalan (bila ada rilis sebelumnya) → `patch.json` → catatan
rilis berisi SHA-256 → daftar aset untuk diunggah manual (atau unggah otomatis).

**Prasyarat**

- **.NET 8 SDK** di PATH — wajib; skrip berhenti lebih awal bila tidak ada.
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

Bila `-VersiLama` diberi **tanpa** `-PublishLama`, skrip otomatis memakai zip portable
rilis lama di folder `artifacts/` (mis. `artifacts/SuDesApp_2.5.4_win-x64.zip`) sebagai
pembanding; unduh dulu zip itu dari halaman Releases bila belum ada.

Skrip memperingatkan bila masih ada perubahan yang belum di-commit: aset yang dibangun
tidak akan sama dengan isi tag, sedangkan aplikasi membandingkan versi lewat tag itu.
`-LewatiPemeriksaan` hanya untuk uji coba.

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
dibuat otomatis pada commit saat ini bila belum ada. Token scope `repo` dibuat di
https://github.com/settings/tokens dan hanya disimpan di variabel lingkungan —
jangan pernah ditulis ke berkas repo.

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
3. Jangan menghapus dan memasang ulang workflow: berkasnya sudah benar (build + uji
   lolos di komputer lokal: 0 error 0 warning, 429 uji hijau). Fokus pada pesan dari
   GitHub.
