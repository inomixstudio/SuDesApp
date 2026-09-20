# ============================================================================
#  Buat paket pembaruan kecil (tambalan) untuk SuDesApp.
#
#  Membandingkan dua hasil `dotnet publish` (lama & baru), lalu:
#    - berkas yang berubah saja dikemas menjadi `patch-<versi>.zip`
#    - daftar berkas + SHA-256 ditulis ke `patch.json`
#
#  Aplikasi yang sudah terpasang membaca `patch.json` dari halaman rilis GitHub:
#  bila versi terpasang sama dengan -VersiLama dan patch berjenis "kecil", aplikasi
#  mengunduh hanya zip tambalan (jauh lebih kecil dari installer) lalu mengganti
#  berkas tersebut. Rilis berjenis "besar" tetap meminta installer penuh.
#
#  Contoh pemakaian:
#    ./Tools/Buat-Tambalan.ps1 -Versi 2.5.1 -VersiLama 2.5.0 `
#        -PublishBaru artifacts/publish -PublishLama artifacts/publish-lama `
#        -Ringkasan artifacts/RINGKASAN.txt -Keluaran artifacts
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Versi,
    [Parameter(Mandatory = $true)][string]$VersiLama,
    [Parameter(Mandatory = $true)][string]$PublishBaru,
    [Parameter(Mandatory = $true)][string]$PublishLama,
    [string]$Ringkasan,
    [string]$Keluaran = 'artifacts',
    [switch]$Besar,
    [int]$BatasBerkasPersen = 40,
    [long]$BatasUkuranMB = 60
)

$ErrorActionPreference = 'Stop'

$Versi = $Versi.Trim().TrimStart('v', 'V')
$VersiLama = $VersiLama.Trim().TrimStart('v', 'V')
if ($Versi -notmatch '^\d+\.\d+\.\d+$') { throw "Versi tidak valid: '$Versi' (harus x.y.z)" }

if (-not (Test-Path -LiteralPath $PublishBaru)) { throw "Folder publish baru tidak ada: $PublishBaru" }
if (-not (Test-Path -LiteralPath $PublishLama)) { throw "Folder publish lama tidak ada: $PublishLama" }
New-Item -ItemType Directory -Force -Path $Keluaran | Out-Null

# Berkas data/pengaturan pengguna tidak pernah ikut diganti pembaruan. Daftar ini
# harus sama dengan PatchUpdateService.Dilindungi di aplikasi.
$awalanDilindungi = @('database/', 'templates/', 'template/', 'temppdf/', 'temp/', 'output/', 'logs/', 'backup/')
$namaDilindungi = @('appsettings.json', 'penomoran-surat.json', 'template-bawaan.json', 'pengaturan-cetak.json', 'desa.db')
$ekstensiDilindungi = @('.db', '.db-wal', '.db-shm', '.log', '.bak', '.sqlite')

function Jalur-Relatif([string]$akar, [string]$penuh) {
    $akarPenuh = [System.IO.Path]::GetFullPath($akar).TrimEnd('\', '/')
    $rel = [System.IO.Path]::GetFullPath($penuh).Substring($akarPenuh.Length).TrimStart('\', '/')
    return ($rel -replace '\\', '/')
}

function Dilindungi([string]$relatif) {
    $r = $relatif.ToLowerInvariant()
    foreach ($awalan in $awalanDilindungi) { if ($r.StartsWith($awalan)) { return $true } }
    $nama = ($r -split '/')[-1]
    if ($namaDilindungi -contains $nama) { return $true }
    foreach ($ekstensi in $ekstensiDilindungi) { if ($nama.EndsWith($ekstensi)) { return $true } }
    return $false
}

function Peta-Berkas([string]$akar) {
    $peta = @{}
    Get-ChildItem -LiteralPath $akar -Recurse -File | ForEach-Object {
        $rel = Jalur-Relatif $akar $_.FullName
        $peta[$rel] = @{
            Ukuran = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    return $peta
}

Write-Host ''
Write-Host "== Buat tambalan SuDesApp $VersiLama -> $Versi ==" -ForegroundColor Cyan

$berkasBaru = Peta-Berkas $PublishBaru
$berkasLama = Peta-Berkas $PublishLama

Write-Host ("Berkas publish baru: {0} berkas" -f $berkasBaru.Count)
Write-Host ("Berkas publish lama: {0} berkas" -f $berkasLama.Count)

$berubah = @()
$dihapus = @()
foreach ($rel in $berkasBaru.Keys) {
    $baru = $berkasBaru[$rel]
    $adaLama = $berkasLama.ContainsKey($rel)
    if (-not $adaLama -or $berkasLama[$rel].Sha256 -ne $baru.Sha256) {
        $berubah += [pscustomobject]@{ Path = $rel; Sha256 = $baru.Sha256; Ukuran = $baru.Ukuran }
    }
}
foreach ($rel in $berkasLama.Keys) {
    if (-not $berkasBaru.ContainsKey($rel)) { $dihapus += $rel }
}

$terlindungi = @($berubah | Where-Object { Dilindungi $_.Path })
$berubah = @($berubah | Where-Object { -not (Dilindungi $_.Path) })
if ($terlindungi.Count -gt 0) {
    Write-Host ("Dilewati (data pengguna): {0} berkas" -f $terlindungi.Count) -ForegroundColor DarkYellow
}

# Penjumlahan dihitung manual: nilai peta-berkas berbentuk hashtable sehingga
# Measure-Object -Property tidak bisa membacanya.
$byteBerubah = 0
foreach ($item in $berubah) { $byteBerubah += $item.Ukuran }
$byteTotal = 0
foreach ($rel in $berkasBaru.Keys) { $byteTotal += $berkasBaru[$rel].Ukuran }

$persenBerkas = if ($berkasBaru.Count -gt 0) { [math]::Round(($berubah.Count * 100.0) / $berkasBaru.Count, 1) } else { 0 }
$mbBerubah = [math]::Round($byteBerubah / 1MB, 2)

Write-Host ("Berubah : {0} berkas ({1}%) - {2} MB" -f $berubah.Count, $persenBerkas, $mbBerubah)
Write-Host ("Dihapus : {0} berkas" -f $dihapus.Count)

# Rilis dengan perubahan besar dikirim lewat installer penuh saja: lebih sedikit
# risiko daripada mengganti ratusan berkas runtime.
$jenis = 'kecil'
$alasanBesar = @()
if ($Besar) { $jenis = 'besar'; $alasanBesar += 'ditandai manual' }
if ($persenBerkas -gt $BatasBerkasPersen) { $jenis = 'besar'; $alasanBesar += "$persenBerkas% berkas berubah" }
if ($mbBerubah -gt $BatasUkuranMB) { $jenis = 'besar'; $alasanBesar += "$mbBerubah MB berubah" }
if ($berubah.Count -eq 0) { $jenis = 'besar'; $alasanBesar += 'tidak ada berkas berubah' }

# Ringkasan perbaikan (satu baris per poin) untuk notifikasi di aplikasi.
$daftarRingkasan = @()
if ($Ringkasan -and (Test-Path -LiteralPath $Ringkasan)) {
    $daftarRingkasan = @(Get-Content -LiteralPath $Ringkasan |
        ForEach-Object { $_.Trim().TrimStart('-', '*', ' ') } |
        Where-Object { $_.Length -gt 0 } |
        Select-Object -First 25)
}

$namaPatch = "patch-$Versi.zip"
$pathPatch = Join-Path $Keluaran $namaPatch
$hashPatch = $null

if ($jenis -eq 'kecil') {
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("SuDesApp-patch-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    try {
        foreach ($item in $berubah) {
            $sumber = Join-Path $PublishBaru ($item.Path -replace '/', '\')
            $tujuan = Join-Path $staging ($item.Path -replace '/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $tujuan) | Out-Null
            Copy-Item -LiteralPath $sumber -Destination $tujuan -Force
        }

        if (Test-Path -LiteralPath $pathPatch) { Remove-Item -LiteralPath $pathPatch -Force }
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $pathPatch -CompressionLevel Optimal
        $hashPatch = (Get-FileHash -LiteralPath $pathPatch -Algorithm SHA256).Hash.ToLowerInvariant()
        $ukuranPatch = (Get-Item -LiteralPath $pathPatch).Length
        Write-Host ("Tambalan: {0} ({1} MB)" -f $pathPatch, [math]::Round($ukuranPatch / 1MB, 2)) -ForegroundColor Green
    }
    finally {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else {
    if (Test-Path -LiteralPath $pathPatch) { Remove-Item -LiteralPath $pathPatch -Force }
    Write-Host ("Rilis besar ({0}) - installer penuh yang dipakai." -f ($alasanBesar -join ', ')) -ForegroundColor Yellow
}

# Daftar disusun lebih dulu sebagai variabel: ekspresi if di dalam hashtable bisa
# kehilangan bentuk array-nya saat diserialkan (penting: satu berkas saja harus tetap
# menjadi array JSON).
$daftarBerkasJson = @()
$daftarHapusJson = @()
if ($jenis -eq 'kecil') {
    $daftarBerkasJson = @($berubah | Sort-Object Path | ForEach-Object {
        [ordered]@{ path = $_.Path; sha256 = $_.Sha256; ukuran = $_.Ukuran }
    })
    $daftarHapusJson = @($dihapus | Sort-Object)
}

$manifest = [ordered]@{
    versi = $Versi
    dariVersi = @($VersiLama)
    jenis = $jenis
    ringkasan = $daftarRingkasan
    berkasPatch = $(if ($jenis -eq 'kecil') { $namaPatch } else { '' })
    sha256Patch = $(if ($hashPatch) { $hashPatch } else { '' })
    berkas = $daftarBerkasJson
    berkasDihapus = $daftarHapusJson
    tanggal = (Get-Date).ToString('s')
    catatan = $(if ($jenis -eq 'kecil') { "Tambalan $VersiLama -> $Versi ($($berubah.Count) berkas)" } else { "Pembaruan besar: $($alasanBesar -join ', ')" })
}

$pathManifest = Join-Path $Keluaran 'patch.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $pathManifest -Encoding UTF8
Write-Host ("Manifest: {0}" -f $pathManifest) -ForegroundColor Green

Write-Host ''
Write-Host 'Ringkasan untuk diunggah ke halaman rilis:' -ForegroundColor Cyan
Write-Host ("  patch.json      : {0}" -f $pathManifest)
if ($jenis -eq 'kecil') { Write-Host ("  {0} : {1}" -f $namaPatch, $pathPatch) }
Write-Host '  (unggah keduanya sebagai aset rilis bersama installer & zip portable)'

if ($env:GITHUB_ENV) {
    "SUDESAPP_JENIS_TAMBALAN=$jenis" | Out-File -FilePath $env:GITHUB_ENV -Append
    "SUDESAPP_NAMA_TAMBALAN=$namaPatch" | Out-File -FilePath $env:GITHUB_ENV -Append
}

exit 0
