[CmdletBinding()]
param(
    # Folder keluaran aset wizard (relatif terhadap repo).
    [string]$FolderAset = '',
    # Sumber logo aplikasi.
    [string]$Logo = ''
)

# ============================================================================
#  Aset wizard Inno Setup bertema SuDesApp.
#
#  Inno Setup 6 menampilkan dua gambar pada wizard modern:
#    - WizardImageFile     : gambar besar di sisi kanan halaman Welcome/Finish
#                            (ukuran bawaan 164x314).
#    - WizardSmallImageFile: gambar kecil di sisi kiri halaman lain (55x58).
#
#  Wizard bukan kotak WPF, jadi radius 14 dan font aplikasi tidak bisa dipakai
#  apa adanya; yang bisa disesuaikan adalah Warna, ikon, gambar, dan font
#  dialog ([LangOptions] DialogFontName). Aset ini membuat header hijau
#  emerald + lencana logo yang sama dengan splash/login aplikasi.
#
#  BMP 24-bit dipakai karena paling kompatibel dengan semua versi Inno Setup.
# ============================================================================

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $FolderAset) { $FolderAset = Join-Path $repoRoot 'installer\Assets' }
if (-not $Logo) { $Logo = Join-Path $repoRoot 'Resources\AppIcon-256.png' }

$primer  = [System.Drawing.Color]::FromArgb(0xFF, 0x16, 0x65, 0x34)
$sekunder = [System.Drawing.Color]::FromArgb(0xFF, 0x14, 0x53, 0x2D)
$putih   = [System.Drawing.Color]::FromArgb(0xFF, 0xFF, 0xFF, 0xFF)

$FolderAset = [IO.Path]::GetFullPath($FolderAset)
$Logo = [IO.Path]::GetFullPath($Logo)
if (-not (Test-Path -LiteralPath $Logo -PathType Leaf)) { throw "Logo tidak ditemukan: $Logo" }
New-Item -ItemType Directory -Force -Path $FolderAset | Out-Null

function Buat-Dasar([int]$Lebar, [int]$Tinggi) {
    $bmp = New-Object System.Drawing.Bitmap($Lebar, $Tinggi, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle(0, 0, $Lebar, $Tinggi)),
        $primer, $sekunder, [System.Drawing.Drawing2D.LinearGradientMode]::Diagonal)
    $g.FillRectangle($brush, 0, 0, $Lebar, $Tinggi)
    return @{ Bmp = $bmp; G = $g }
}

function Gambar-Lencana($State, [int]$Lebar, [int]$Tinggi, [int]$Diameter) {
    $bmp = $State['Bmp']
    $g = $State['G']

    $x = [int](($Lebar - $Diameter) / 2)
    $y = [int](($Tinggi - $Diameter) / 2)

    # Lencana putih lembut (alpha 240) seperti splash/login.
    $lencana = [System.Drawing.Color]::FromArgb(0xF2, 0xFF, 0xFF, 0xFF)
    $brushLencana = New-Object System.Drawing.SolidBrush($lencana)
    $g.FillEllipse($brushLencana, $x, $y, $Diameter, $Diameter)

    # Ikon aplikasi di dalam lencana, menyisakan bingkai.
    $sisi = [int]($Diameter * 0.72)
    $ix = [int]($x + ($Diameter - $sisi) / 2)
    $iy = [int]($y + ($Diameter - $sisi) / 2)

    $ikon = [System.Drawing.Image]::FromFile($Logo)
    try {
        $g.DrawImage($ikon, $ix, $iy, $sisi, $sisi)
    } finally {
        $ikon.Dispose()
    }
}

function Simpan([System.Drawing.Bitmap]$Bmp, [string]$Jalur) {
    $Bmp.Save($Jalur, [System.Drawing.Imaging.ImageFormat]::Bmp)
    Write-Host ("Aset: {0} ({1:N0} byte)" -f $Jalur, (Get-Item -LiteralPath $Jalur).Length)
}

# --- WizardImageFile: 164x314 -------------------------------------------------
$lebar = 164
$tinggi = 314
$state = Buat-Dasar $lebar $tinggi
Gambar-Lencana $state $lebar $tinggi 118
Simpan $state['Bmp'] (Join-Path $FolderAset 'WizardImage.bmp')
$state['G'].Dispose()
$state['Bmp'].Dispose()

# --- WizardSmallImageFile: 55x58 ---------------------------------------------
$lebar = 55
$tinggi = 58
$state = Buat-Dasar $lebar $tinggi
Gambar-Lencana $state $lebar $tinggi 46
Simpan $state['Bmp'] (Join-Path $FolderAset 'WizardSmallImage.bmp')
$state['G'].Dispose()
$state['Bmp'].Dispose()

Write-Host 'Selesai.'
