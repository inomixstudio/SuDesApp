<#
    Pemeriksa kredensial OAuth Google yang tertanam pada hasil build/publish.

    LATAR
    Kredensial klien OAuth hidup di SuDesApp.Core/Utilities/GoogleClientCredentialsBawaan.lokal.cs
    yang SENGAJA tidak ikut git. Bila berkas itu tidak ada saat build (mis. build di
    CI), yang ter-compile adalah placeholder "GANTI_CLIENT_ID"/"GANTI_CLIENT_SECRET" —
    aplikasi hasil install lalu TIDAK bisa menyambungkan akun Google ke fitur layanan
    (Formulir/Sheet/Drive) karena klien OAuth tidak aktif. Login aplikasi sendiri TIDAK
    terpengaruh: layar login memakai akun aplikasi, bukan akun Google.

    Skrip ini membaca assembly hasil build dan memastikan:
      1. ada kredensial asli (string "apps.googleusercontent.com" tertanam), dan
      2. placeholder pengganti TIDAK ikut ter-compile.

    DIPAKAI OLEH
      - Tools/publish-release.ps1 (gerbang sebelum zip/installer dibuat)
      - .github/workflows/release.yml (gerbang sebelum installer CI dibuat)

    Pemakaian:
        ./Tools/check-kredensial-google.ps1                       # artifacts/publish
        ./Tools/check-kredensial-google.ps1 -Folder <dir-publish>
        ./Tools/check-kredensial-google.ps1 -AssemblyPath <file.dll>
        ./Tools/check-kredensial-google.ps1 -IzinkanTanpaKredensial # peringatan saja

    Keluaran: 0 = kredensial ada (atau diizinkan tanpa kredensial), 1 = gagal.
#>
[CmdletBinding()]
param(
    [string]$Folder = 'artifacts/publish',
    [string]$AssemblyName = 'SuDesApp.Core.dll',
    [string]$AssemblyPath,
    [switch]$IzinkanTanpaKredensial
)

$ErrorActionPreference = 'Stop'

function Write-Baris([string]$teks, [string]$warna = 'Gray') {
    Write-Host $teks -ForegroundColor $warna
}

$path = $null
if ($AssemblyPath) {
    if (Test-Path -LiteralPath $AssemblyPath) { $path = (Resolve-Path -LiteralPath $AssemblyPath).Path }
} else {
    $kandidat = Join-Path $Folder $AssemblyName
    if (Test-Path -LiteralPath $kandidat) {
        $path = (Resolve-Path -LiteralPath $kandidat).Path
    } else {
        # Folder publish bisa punya struktur berbeda; cari assembly-nya sekali.
        $ketemu = Get-ChildItem -LiteralPath $Folder -Filter $AssemblyName -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($ketemu) { $path = $ketemu.FullName }
    }
}

Write-Baris ''
Write-Baris '== Pemeriksaan kredensial OAuth Google ==' 'Cyan'

if (-not $path) {
    Write-Baris ("  Assembly tidak ditemukan: " + (Join-Path $Folder $AssemblyName)) 'Red'
    Write-Baris '  Jalankan publish lebih dulu, atau arahkan -AssemblyPath ke SuDesApp.Core.dll hasil build.' 'Yellow'
    exit 1
}

Write-Baris ("  Assembly diperiksa: " + $path)

# String di assembly .NET tersimpan sebagai UTF-16LE — baca byte lalu ubah ke
# teks Unicode agar pencarian tidak bergantung pada 'grep' yang berbeda antar OS.
# Penting: string pada metadata bisa mulai di offset byte GANJIL, sehingga isi
# assembly dibaca dua kali (dari byte 0 dan dari byte 1) lalu digabung.
$byte = [System.IO.File]::ReadAllBytes($path)
$teks = [System.Text.Encoding]::Unicode.GetString($byte)
if ($byte.Length -gt 1) {
    $teks += [System.Text.Encoding]::Unicode.GetString($byte, 1, $byte.Length - 1)
}
$punyaKredensial = $teks.Contains('apps.googleusercontent.com')
$punyaPlaceholder = $teks.Contains('GANTI_CLIENT_ID') -or $teks.Contains('GANTI_CLIENT_SECRET')

Write-Baris ("  Kredensial asli tertanam : " + $(if ($punyaKredensial) { 'YA' } else { 'TIDAK' }))
Write-Baris ("  Placeholder ikut ter-compile : " + $(if ($punyaPlaceholder) { 'YA' } else { 'TIDAK' }))

$lolos = $punyaKredensial -and -not $punyaPlaceholder

if ($lolos) {
    Write-Baris 'HASIL: LOLOS - paket ini bisa menyambungkan akun Google di komputer mana pun.' 'Green'
    exit 0
}

$penjelasan = @(
    'Paket ini dibangun TANPA kredensial OAuth tertanam, sehingga halaman Login Google',
    '(menu samping) di aplikasi terpasang akan menampilkan "Login Google belum dikonfigurasi".',
    '',
    'Cara memperbaiki:',
    '  1. Bangun aset rilis di komputer yang memiliki berkas rahasia build',
    '     SuDesApp.Core/Utilities/GoogleClientCredentialsBawaan.lokal.cs',
    '     (mis. ./Tools/publish-release.ps1 -Version <versi> -VersiLama <versi lama>), atau',
    '  2. setelah terpasang, isi Client ID & Client Secret pada halaman',
    '     Pengaturan Aplikasi -> Kredensial Google, lalu klik Uji Koneksi.'
)

if ($IzinkanTanpaKredensial) {
    Write-Baris 'PERINGATAN: kredensial OAuth Google tidak tertanam, tetapi rilis dilanjutkan' 'Yellow'
    Write-Baris '            karena -IzinkanTanpaKredensial dipakai.' 'Yellow'
    $penjelasan | ForEach-Object { Write-Baris ('  ' + $_) 'Yellow' }
    Write-Baris 'HASIL: DILANJUTKAN TANPA KREDENSIAL' 'Yellow'
    exit 0
}

Write-Baris 'HASIL: GAGAL - kredensial OAuth Google tidak tertanam pada paket ini.' 'Red'
$penjelasan | ForEach-Object { Write-Baris ('  ' + $_) 'Yellow' }
exit 1
