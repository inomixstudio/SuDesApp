<#
.SYNOPSIS
    Rilis SuDesApp sekali-jalan: naikkan versi, commit, tag, publish, installer,
    tambalan, patch.json - lalu opsional unggah ke GitHub Releases.

.DESCRIPTION
    Satu perintah untuk seluruh urutan rilis. Tiap tahap memanggil skrip yang
    sudah ada, jadi hasilnya sama dengan menjalankannya berurutan:

      1. Tools/rilis.ps1 - prasyarat, naik AssemblyVersion/FileVersion +
         entri Catatan Rilis, gerbang dotnet build, commit chore(release),
         tag anotasi vX.Y.Z, push ke origin;
      2. Tools/publish-release.ps1 - publish win-x64 self-contained, installer
         Inno Setup, pembaruan kecil (tambalan + patch.json), catatan rilis,
         daftar aset UNGGAH.txt;
      3. (opsional) unggah aset ke GitHub Releases dengan -Unggah.

    Bila -VersiLama kosong, skrip ini membaca AssemblyVersion di csproj sebelum
    dinaikkan. Zip pembanding (SuDesApp_<versi>_win-x64.zip) diambil dari
    artifacts/ bila ada, atau diunduh otomatis dari rilis GitHub dengan
    verifikasi sha256 - pembaruan kecil tetap terbentuk dari komputer mana pun.
    Hanya bila rilis pembanding tidak ada sama sekali, patch.json jatuh ke
    jenis "besar" dan aplikasi terpasang memakai installer penuh.

.PARAMETER Versi
    Versi baru format x.y.z (posisi 0). Awalan "v" boleh ikut ditulis.

.PARAMETER Fitur
    Diteruskan ke rilis.ps1 - isi bagian "Fitur Baru" pada entri catatan rilis.
    Beberapa butir boleh dipisah koma.

.PARAMETER Peningkatan
    Diteruskan ke rilis.ps1 - isi bagian "Peningkatan".

.PARAMETER Perbaikan
    Diteruskan ke rilis.ps1 - isi bagian "Perbaikan Bug".

.PARAMETER CatatanBerkas
    Diteruskan ke rilis.ps1 - berkas teks berbutir untuk catatan rilis.

.PARAMETER VersiLama
    Versi rilis sebelumnya untuk membuat pembaruan kecil. Kosong = versi yang
    sedang ada di csproj. Zip pembandingnya diambil dari artifacts/ bila ada,
    atau diunduh otomatis dari rilis GitHub bila belum ada.

.PARAMETER Cabang
    Cabang tempat rilis dibuat. Kosong = cabang yang sedang aktif, jadi rilis
    dari cabang kerja (mis. upgrade-dotnet-10) tidak perlu pindah ke main.

.PARAMETER Unggah
    Setelah aset siap, unggah ke GitHub Releases (draft -> ganti aset bernama
    sama -> terbitkan) lewat publish-release.ps1 -Unggah. Tidak boleh digabung
    dengan -UjiCoba.

.PARAMETER LewatiInstaller
    Diteruskan ke publish-release.ps1 - installer Inno Setup tidak dibuat.

.PARAMETER Besar
    Diteruskan ke publish-release.ps1 - paksa patch.json berjenis "besar".

.PARAMETER IzinkanTanpaKredensial
    Diteruskan ke publish-release.ps1 - izinkan publish tanpa kredensial Google
    tertanam (harus disengaja; lihat check-kredensial-google.ps1).

.PARAMETER TanpaDorong
    rilis.ps1 membuat commit dan tag lokal, tetapi tidak mendorong ke origin.
    Tidak boleh digabung dengan -Unggah: rilis GitHub menuntut tag sudah ada
    di origin.

.PARAMETER UjiCoba
    Latihan aman: rilis.ps1 -UjiCoba (tidak ada commit/tag/push) + publish lokal
    + tidak mengunggah apa pun. Berkas versi dikembalikan rilis.ps1 seperti semula.

.EXAMPLE
    ./Tools/rilis-sekali-jalan.ps1 2.5.6 -Peningkatan "Tabel register lebih rapi." -Unggah

.EXAMPLE
    ./Tools/rilis-sekali-jalan.ps1 2.5.6 -CatatanBerkas artifacts/catatan-rilis.txt

.EXAMPLE
    ./Tools/rilis-sekali-jalan.ps1 9.9.9 -UjiCoba
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Versi,

    [string[]]$Fitur = @(),
    [string[]]$Peningkatan = @(),
    [string[]]$Perbaikan = @(),
    [string]$CatatanBerkas = '',
    [string]$VersiLama = '',
    # Versi lain yang ikut dilayani tambalan (mis. 2.8.0,2.9.0 saat pembandingnya
    # versi tertua). Diteruskan ke publish-release.ps1 -DariVersi.
    [string[]]$DariVersi = @(),
    [string]$Cabang = '',
    [switch]$Unggah,
    [switch]$LewatiInstaller,
    [switch]$Besar,
    [switch]$IzinkanTanpaKredensial,
    [switch]$TanpaDorong,
    [switch]$UjiCoba
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Tahap  { param([string]$Teks) Write-Host ''; Write-Host ('== ' + $Teks + ' ==') -ForegroundColor Cyan }
function Info   { param([string]$Teks) Write-Host ('   ' + $Teks) }
function Sukses { param([string]$Teks) Write-Host ('   ' + $Teks) -ForegroundColor Green }
function Ingat  { param([string]$Teks) Write-Host ('   ' + $Teks) -ForegroundColor Yellow }

function GitRepo {
    param([string[]]$Arg)
    $keluaran = & git -c ('safe.directory=' + $repoRoot) @Arg 2>$null
    return [pscustomobject]@{
        Keluaran = (($keluaran | Out-String).Trim())
        Kode     = $LASTEXITCODE
    }
}

# ---------- prasyarat ----------
$Versi = $Versi.Trim().TrimStart('v', 'V')
if ($Versi -notmatch '^\d+\.\d+\.\d+$') {
    throw ("Versi tidak valid: '" + $Versi + "' (harus x.y.z, mis. 2.5.6).")
}
if ($UjiCoba -and $Unggah) {
    throw 'Pilih salah satu: -UjiCoba (latihan lokal, tidak mengunggah) atau -Unggah (terbitkan ke GitHub Releases).'
}
if ($TanpaDorong -and $Unggah) {
    throw 'Jangan gabungkan -TanpaDorong dengan -Unggah: rilis GitHub menuntut tag v<versi> sudah ada di origin.'
}
if ($CatatanBerkas -and ($Fitur.Count + $Peningkatan.Count + $Perbaikan.Count) -gt 0) {
    throw 'Pilih salah satu cara mengisi catatan rilis: -CatatanBerkas atau -Fitur/-Peningkatan/-Perbaikan.'
}
foreach ($skrip in @('rilis.ps1', 'publish-release.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $skrip))) {
        throw ('Skrip tidak ditemukan: ' + (Join-Path $PSScriptRoot $skrip))
    }
}

# Cabang: kosong = cabang yang sedang aktif. rilis.ps1 tetap memeriksa bahwa
# cabang aktif sama persis dengan yang dipakai, jadi rilis dari cabang kerja
# tidak lagi menuntut pindah ke main lebih dulu.
if (-not $Cabang) {
    $cekCabang = GitRepo -Arg @('rev-parse', '--abbrev-ref', 'HEAD')
    if ($cekCabang.Kode -ne 0 -or -not $cekCabang.Keluaran -or $cekCabang.Keluaran -eq 'HEAD') {
        throw 'Cabang aktif tidak bisa dideteksi - isi -Cabang (mis. -Cabang main).'
    }
    $Cabang = $cekCabang.Keluaran
}
Info ('Cabang rilis   : ' + $Cabang)

# ---------- pembanding pembaruan kecil ----------
# Versi lama = versi yang sekarang ada di csproj, dibaca SEBELUM rilis.ps1
# menaikkannya. Zip portable versi itu di artifacts/ dipakai sebagai pembanding.
$versiCsproj = ''
$berkasCsproj = Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj'
if (Test-Path -LiteralPath $berkasCsproj) {
    $isiCsproj = [System.IO.File]::ReadAllText($berkasCsproj)
    if ($isiCsproj -match '<AssemblyVersion>([^<]+)</AssemblyVersion>') {
        $bagian = $Matches[1].Split('.')
        if ($bagian.Length -ge 3) { $versiCsproj = ($bagian[0..2] -join '.') }
    }
}

if (-not $VersiLama) {
    $VersiLama = $versiCsproj
} else {
    $VersiLama = $VersiLama.Trim().TrimStart('v', 'V')
}
if ($VersiLama -eq $Versi) { $VersiLama = '' }

$pakaiPembaruan = $false
if ($VersiLama) {
    $zipLama = Join-Path $repoRoot ('artifacts/SuDesApp_{0}_win-x64.zip' -f $VersiLama)
    $pakaiPembaruan = $true
    if (Test-Path -LiteralPath $zipLama) {
        Info ('Pembaruan kecil : ' + $VersiLama + ' -> ' + $Versi + '   (pembanding ' + (Split-Path -Leaf $zipLama) + ')')
    } else {
        # Zip belum ada di mesin ini: publish-release.ps1 mengunduhnya sendiri
        # dari rilis GitHub (sekalian menjadi cache) - tidak perlu unduh manual.
        Info ('Pembaruan kecil : ' + $VersiLama + ' -> ' + $Versi + '   (zip pembanding diunduh dari rilis GitHub)')
    }
} else {
    Info 'Pembaruan kecil : dilewati - patch.json berjenis "besar".'
}

# ---------- Tahap 1/3: versi + commit + tag + push ----------
Tahap ('Tahap 1/3 - naik versi, commit, tag, push (Tools/rilis.ps1; cabang ' + $Cabang + ')')
$argRilis = @{ Versi = $Versi; Cabang = $Cabang }
if ($Fitur.Count)       { $argRilis['Fitur'] = $Fitur }
if ($Peningkatan.Count) { $argRilis['Peningkatan'] = $Peningkatan }
if ($Perbaikan.Count)   { $argRilis['Perbaikan'] = $Perbaikan }
if ($CatatanBerkas)     { $argRilis['CatatanBerkas'] = $CatatanBerkas }
if ($TanpaDorong)       { $argRilis['TanpaDorong'] = $true }
if ($UjiCoba)           { $argRilis['UjiCoba'] = $true }
& (Join-Path $PSScriptRoot 'rilis.ps1') @argRilis
if ($LASTEXITCODE -ne 0) { throw ('Tahap rilis.ps1 gagal (exit ' + $LASTEXITCODE + ') - publish tidak dijalankan.') }

# ---------- Tahap 2/3: publish + installer + tambalan + patch.json ----------
Tahap 'Tahap 2/3 - publish, installer, tambalan, patch.json (Tools/publish-release.ps1)'
$argPublish = @{ Version = $Versi }
if ($pakaiPembaruan)            { $argPublish['VersiLama'] = $VersiLama }
if ($pakaiPembaruan -and $DariVersi.Count) { $argPublish['DariVersi'] = $DariVersi }
if ($LewatiInstaller)           { $argPublish['SkipInstaller'] = $true }
if ($Besar)                     { $argPublish['Besar'] = $true }
if ($IzinkanTanpaKredensial)    { $argPublish['IzinkanTanpaKredensial'] = $true }
if ($UjiCoba)                   { $argPublish['LewatiPemeriksaan'] = $true }
if ($Unggah -and -not $UjiCoba) { $argPublish['Unggah'] = $true }
& (Join-Path $PSScriptRoot 'publish-release.ps1') @argPublish
if ($LASTEXITCODE -ne 0) { throw ('Tahap publish-release.ps1 gagal (exit ' + $LASTEXITCODE + ').') }

# ---------- Tahap 3/3: penutup ----------
Tahap 'Tahap 3/3 - penutup'
if ($UjiCoba) {
    Ingat ('Mode -UjiCoba: tidak ada commit, tag, push, atau unggahan untuk versi ' + $Versi + '.')
    Info 'Aset latihan ada di artifacts/ (publish, zip portable, installer, patch.json) dan boleh dibuang.'
    Info 'Jalankan ulang tanpa -UjiCoba untuk menerbitkan rilis sungguhan.'
} elseif ($Unggah) {
    Sukses ('Rilis v' + $Versi + ' terbit: https://github.com/inomixstudio/SuDesApp/releases/tag/v' + $Versi)
} else {
    Info ('Aset siap diunggah - daftarnya: ' + (Join-Path $repoRoot 'artifacts/UNGGAH.txt'))
    Info ('Halaman unggah: https://github.com/inomixstudio/SuDesApp/releases/new?tag=v' + $Versi)
    Info 'Jalankan ulang rilis dengan -Unggah bila ingin skrip mengunggahnya sendiri.'
}
