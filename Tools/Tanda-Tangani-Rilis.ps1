<#
.SYNOPSIS
    Menandatangani berkas rilis SuDesApp dengan Authenticode + timestamp.

.DESCRIPTION
    Satu jalur tanda tangan untuk rilis lokal (Tools/publish-release.ps1) dan
    rilis CI (.github/workflows/release.yml). Tanpa tanda tangan Authenticode,
    Windows SmartScreen memperingatkan setiap pengguna baru dan nama penerbit
    tidak muncul di properti berkas - merugikan produk yang dijual.

    Sumber sertifikat, berurutan:
      1. -Pfx + -Sandi (berkas .pfx berisi kunci privat);
      2. env SUDESAPP_PFX_PATH + SUDESAPP_PFX_PASSWORD (dipakai CI);
      3. -SidikJari atau env SUDESAPP_SIGN_THUMBPRINT (sertifikat di
         penyimpanan CurrentUser\My, mis. dari token USB).

    Setiap berkas diverifikasi dengan `signtool verify /pa` sesudah
    ditandatangani. Tanda tangan yang tidak lolos verifikasi dianggap gagal:
    aset seperti itu tetap memicu peringatan di komputer pengguna, jadi lebih
    baik rilis berhenti daripada menerbitkan berkas yang tampak bertanda tangan.

.PARAMETER Berkas
    Berkas .exe/.dll yang ditandatangani. Wajib ada.

.PARAMETER Pfx
    Jalur berkas .pfx.

.PARAMETER Sandi
    Sandi berkas .pfx.

.PARAMETER SidikJari
    Thumbprint sertifikat di penyimpanan CurrentUser\My (tanpa berkas .pfx).

.PARAMETER UrlTimestamp
    Server timestamp RFC 3161. Timestamp membuat tanda tangan tetap sah setelah
    sertifikat kedaluwarsa. Bawaan: http://timestamp.digicert.com.

.EXAMPLE
    ./Tools/Tanda-Tangani-Rilis.ps1 -Berkas artifacts/publish/SuDesApp.exe `
        -Pfx C:\kunci\sudesapp.pfx -Sandi 'sandi-pfx'

.EXAMPLE
    $env:SUDESAPP_SIGN_THUMBPRINT = 'A1B2C3...'
    ./Tools/Tanda-Tangani-Rilis.ps1 -Berkas artifacts/installer/SuDesApp_2.9.0_Setup.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Berkas,

    [string]$Pfx,
    [string]$Sandi,
    [string]$SidikJari,
    [string]$UrlTimestamp = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

# signtool tidak ikut runtime .NET; ia datang dari Windows SDK. Urutan: PATH,
# lalu versi Windows Kits terbaru yang memasang x64\signtool.exe.
function CariSigntool {
    $dariPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($dariPath) { return $dariPath.Source }

    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kits) {
        $kandidat = Get-ChildItem -LiteralPath $kits -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $versi = [version]'0.0'
            if ([version]::TryParse($_.Name, [ref]$versi)) {
                [pscustomobject]@{ Versi = $versi; Jalur = (Join-Path $_.FullName 'x64\signtool.exe') }
            }
        } | Sort-Object -Property Versi -Descending |
            Where-Object { Test-Path -LiteralPath $_.Jalur } |
            Select-Object -First 1
        if ($kandidat) { return $kandidat.Jalur }
    }

    throw ('signtool.exe tidak ditemukan. Pasang "Windows SDK Signing Tools" dari Windows SDK, ' +
           'atau jalankan di runner windows-latest yang sudah memuatnya.')
}

if (-not $Pfx) { $Pfx = [string]$env:SUDESAPP_PFX_PATH }
if (-not $Sandi -and $env:SUDESAPP_PFX_PASSWORD) { $Sandi = [string]$env:SUDESAPP_PFX_PASSWORD }
if (-not $SidikJari) { $SidikJari = [string]$env:SUDESAPP_SIGN_THUMBPRINT }

if (-not $Pfx -and -not $SidikJari) {
    throw ('Sertifikat tanda tangan tidak diberikan. Isi -Pfx + -Sandi (atau env SUDESAPP_PFX_PATH + ' +
           'SUDESAPP_PFX_PASSWORD), atau -SidikJari (env SUDESAPP_SIGN_THUMBPRINT).')
}
if ($Pfx) {
    if (-not (Test-Path -LiteralPath $Pfx -PathType Leaf)) { throw ("Berkas .pfx tidak ditemukan: " + $Pfx) }
    $Pfx = (Resolve-Path -LiteralPath $Pfx).Path
    if (-not $Sandi) {
        Write-Host 'Peringatan: -Sandi kosong - signtool akan meminta sandi .pfx secara interaktif.' -ForegroundColor Yellow
    }
}

$daftar = @()
foreach ($jalur in $Berkas) {
    if (-not (Test-Path -LiteralPath $jalur -PathType Leaf)) { throw ("Berkas yang akan ditandatangani tidak ada: " + $jalur) }
    $daftar += (Resolve-Path -LiteralPath $jalur).Path
}

$signtool = CariSigntool

$argSign = @('sign', '/fd', 'sha256', '/tr', $UrlTimestamp, '/td', 'sha256', '/a')
if ($Pfx) {
    $argSign += @('/f', $Pfx)
    if ($Sandi) { $argSign += @('/p', $Sandi) }
} else {
    $argSign += @('/sha1', $SidikJari.Trim())
    Write-Host ('Sertifikat : thumbprint ' + $SidikJari.Trim())
}
if ($Pfx) { Write-Host ('Sertifikat : ' + (Split-Path -Leaf $Pfx)) }
Write-Host ('Timestamp  : ' + $UrlTimestamp)

# PS 5.1 mengubah setiap baris stderr proses native menjadi ErrorRecord yang,
# dengan $ErrorActionPreference = 'Stop', berhenti sebelum pesan galat buatan
# skrip sempat tercetak. Jalankan signtool dengan preferensi 'Continue' dan
# kembalikan teksnya supaya kegagalan menjelaskan sebabnya.
function JalankanSigntool {
    param([string[]]$Argumen)

    $preferensiLama = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $keluaran = & $signtool @Argumen 2>&1
        $kode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $preferensiLama
    }

    $teks = ($keluaran | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { [string]$_ }
    }) -join "`n"

    return [pscustomobject]@{ Kode = $kode; Teks = $teks.Trim() }
}

# Variabel loop sengaja bukan $berkas: parameter [string[]]$Berkas memberi
# batasan tipe pada nama itu, sehingga nilai string tunggal berubah menjadi
# larik dan pesan galat mencetak "System.String[]" alih-alih jalur berkas.
foreach ($jalurBerkas in $daftar) {
    $nama = Split-Path -Leaf $jalurBerkas
    Write-Host ("Menandatangani {0} ..." -f $nama)

    $hasil = JalankanSigntool -Argumen ($argSign + $jalurBerkas)
    if ($hasil.Kode -ne 0) {
        throw ("Tanda tangan gagal untuk {0} (signtool exit {1}):`n{2}" -f $jalurBerkas, $hasil.Kode, $hasil.Teks)
    }

    # /pa memakai kebijakan Authenticode untuk executable: rantai sertifikat
    # harus dipercaya komputer ini. Sertifikat uji yang tidak dipercaya sengaja
    # ditolak - aset seperti itu tetap memicu peringatan di komputer pengguna.
    $verifikasi = JalankanSigntool -Argumen (@('verify', '/pa', '/all', $jalurBerkas))
    if ($verifikasi.Kode -ne 0) {
        throw ("Tanda tangan {0} tidak lolos verifikasi Authenticode (signtool exit {1}):`n{2}" -f `
            $jalurBerkas, $verifikasi.Kode, $verifikasi.Teks)
    }

    Write-Host ("   terverifikasi: {0}" -f $nama) -ForegroundColor Green
}
