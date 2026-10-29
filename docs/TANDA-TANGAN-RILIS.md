# Tanda Tangan Authenticode untuk Rilis

Panduan **pemelihara rilis**. Menjelaskan apa yang ditandatangani, dari mana
sertifikatnya datang, cara mengisi secret CI, dan cara memeriksa hasilnya.

## Mengapa

Windows SmartScreen memeriksa tanda tangan Authenticode pada `SuDesApp.exe` dan
installer sebelum pengguna menjalankannya. Tanpa tanda tangan:

- pengguna baru melihat "Windows protected your PC" dan harus menekan
  **More info → Run anyway**;
- nama penerbit di properti berkas kosong, sehingga aplikasi yang dijual
  terlihat tidak resmi;
- setiap rilis baru memulai reputasi dari nol.

Tanda tangan tidak menghapus peringatan seketika. Sertifikat baru membangun
reputasi SmartScreen dari waktu ke waktu; sertifikat **EV** biasanya langsung
dipercaya. Yang penting: tandatangani sejak rilis pertama dan pakai sertifikat
yang sama untuk semua rilis.

## Apa yang ditandatangani

| Berkas | Ditandatangani | Alasan |
|---|---|---|
| `SuDesApp.exe` (di publish, zip portable, dan payload installer) | ya | inilah yang dijalankan pengguna dan yang diperiksa SmartScreen |
| `SuDesApp_<versi>_Setup.exe` | ya | diunduh dan dijalankan langsung dari halaman rilis |
| DLL aplikasi (`SuDesApp.dll`, `SuDesApp.Core.dll`, `SudesPdf*.dll`) | tidak | Windows tidak memeriksa tanda tangan DLL saat pemuatan; menandatanganinya menambah byte baru pada setiap rilis (timestamp) sehingga pembaruan kecil ikut membengkak |

Tanda tangan dilakukan **sebelum** zip dan tambalan dibuat, jadi exe yang sama
ada di zip portable, installer, dan patch.

## Sertifikat

Yang dibutuhkan adalah **code signing certificate** dari otoritas sertifikat
publik (bukan sertifikat self-signed). Pilih OV bila cukup; EV memberi reputasi
SmartScreen lebih cepat dengan biaya lebih tinggi. Sertifikat diterbitkan atas
nama badan hukum, jadi siapkan dokumen perusahaan saat memesan.

Ekspor sebagai `.pfx` **beserta kunci privat**. Berkas ini dan sandinya adalah
identitas penerbit: siapa pun yang memegangnya bisa menerbitkan pembaruan yang
diterima Windows sebagai SuDesApp.

- Jangan pernah commit `.pfx`/`.p12`/`.pem` (sudah dikecualikan `.gitignore`).
- Simpan sandi di pengelola sandi, bukan di berkas repo.
- Bila bocor: cabut sertifikat di otoritas penerbit, terbitkan yang baru, ganti
  secret.

## Rilis CI (`.github/workflows/release.yml`)

Tambahkan empat secret di **Settings → Secrets and variables → Actions**:

| Secret | Isi |
|---|---|
| `WINDOWS_SIGNING_PFX_BASE64` | isi berkas `.pfx` dalam base64 (satu baris) |
| `WINDOWS_SIGNING_PFX_PASSWORD` | sandi `.pfx` |
| `LISENSI_URL_SERVER` | alamat HTTPS server lisensi produksi |
| `LISENSI_KUNCI_PUBLIK` | kunci publik ES256 server lisensi |

Base64 dari PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\kunci\sudesapp.pfx')) |
    Set-Clipboard
```

lalu tempel ke nilai secret. Jangan menaruh hasil base64 atau sandinya di chat,
berkas repo, atau variabel workflow biasa.

Workflow menolak berjalan tanpa `WINDOWS_SIGNING_PFX_BASE64` — aset yang tidak
ditandatangani tidak boleh terbit tanpa disadari. Untuk uji pipeline sebelum
sertifikat dibeli, jalankan **Run workflow** dengan `tanpa_tanda_tangan=true`;
status "TIDAK ditandatangani" ikut tercatat di `RELEASE.md`.

## Rilis lokal (`Tools/publish-release.ps1`)

Skrip yang sama dipakai `Tools/rilis-sekali-jalan.ps1`. Sertifikat dibaca
berurutan: parameter, lalu variabel lingkungan.

```powershell
# .pfx
$env:SUDESAPP_PFX_PATH = 'C:\kunci\sudesapp.pfx'
$env:SUDESAPP_PFX_PASSWORD = '<sandi>'
./Tools/rilis-sekali-jalan.ps1 2.8.1 -Peningkatan "..."

# atau langsung ke publish-release
./Tools/publish-release.ps1 -Version 2.8.1 -SertifikatPfx 'C:\kunci\sudesapp.pfx' `
    -SandiSertifikat '<sandi>'

# sertifikat sudah terpasang di penyimpanan Windows (mis. token USB)
$env:SUDESAPP_SIGN_THUMBPRINT = '<thumbprint>'
./Tools/publish-release.ps1 -Version 2.8.1
```

Tanpa sertifikat sama sekali, rilis lokal tetap dibuat, tetapi mencetak
peringatan dan menulis `Authenticode: TIDAK ditandatangani ...` ke
`artifacts/RELEASE.md`. Bila sertifikat ada tetapi `signtool` gagal (salah sandi,
sertifikat kedaluwarsa, timestamp tidak terjangkau), publish **berhenti** —
tidak ada aset setengah bertanda tangan.

## Memeriksa hasil

```powershell
Get-AuthenticodeSignature artifacts/publish/SuDesApp.exe |
    Select-Object Status, StatusMessage,
        @{ n = 'Penerbit'; e = { $_.SignerCertificate.Subject } }

signtool verify /pa /all artifacts/installer/SuDesApp_2.8.1_Setup.exe
```

`Status` harus `Valid` dan penerbit harus nama badan hukum pemilik sertifikat.
`signtool` tersedia di PATH bila Windows SDK terpasang; `Tools/Tanda-Tangani-Rilis.ps1`
juga mencarinya di `%ProgramFiles(x86)%\Windows Kits\10\bin\<versi>\x64`.

Di komputer pengguna, penerbit tampil di tab **Digital Signatures** properti
berkas dan di kotak SmartScreen/UAC.

## Masa berlaku dan pembaruan

Timestamp (bawaan `http://timestamp.digicert.com`) membuat tanda tangan tetap
sah setelah sertifikat kedaluwarsa, tetapi **penandatanganan baru** gagal.
Perpanjang sertifikat sebelum kedaluwarsa, lalu ganti
`WINDOWS_SIGNING_PFX_BASE64` dan sandinya. Jangan mengganti sertifikat di tengah
siklus rilis bila tidak perlu: reputasi SmartScreen terikat pada sertifikat.

## Berkas terkait

- `Tools/Tanda-Tangani-Rilis.ps1` — satu-satunya tempat `signtool` dipanggil.
- `Tools/publish-release.ps1` — memanggilnya untuk exe dan installer.
- `.github/workflows/release.yml` — menyediakan sertifikat dari secret.
