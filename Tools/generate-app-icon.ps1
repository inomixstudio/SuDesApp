# =====================================================================
#  SuDesApp - Generator Ikon Aplikasi (Versi 2.0)
#  Merancang ikon aplikasi Surat Desa sebagai vector art (WPF) lalu
#  merendernya ke multi-size .ico (256/64/48/32/24/16 px) + PNG preview.
#
#  Pemakaian:  powershell -ExecutionPolicy Bypass -File Tools\generate-app-icon.ps1
#  Output   :  Resources\AppIcon.ico + Resources\AppIcon-256.png
#              + Tools\icon-preview\icon-256/32/16.png
# =====================================================================

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root    = Split-Path -Parent $PSScriptRoot
$resDir  = Join-Path $root 'Resources'
$icoPath = Join-Path $resDir 'AppIcon.ico'
$png256  = Join-Path $resDir 'AppIcon-256.png'
$previewDir = Join-Path $PSScriptRoot 'icon-preview'
New-Item -ItemType Directory -Force -Path $resDir, $previewDir | Out-Null

# ---------------------------------------------------------------------
#  DESAIN (viewBox 1024, diskalakan ke tiap ukuran):
#    1. Tile rounded-square, gradient emerald diagonal + glass highlight
#    2. Amplop putih dengan lipatan V (flap)
#    3. Segel lilin marun + ring emas bertuliskan "SD"
#    4. Sparkle kecil kanan-atas
# ---------------------------------------------------------------------
function New-IconVisual([double]$size) {
    $vis = New-Object System.Windows.Media.DrawingVisual
    $ctx = $vis.RenderOpen()

    # 1a. Latar gradient emerald
    $bg = New-Object System.Windows.Media.LinearGradientBrush
    $bg.StartPoint = [System.Windows.Point]::new(0, 0)
    $bg.EndPoint   = [System.Windows.Point]::new(1, 1)
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x10,0xB9,0x81), 0.0))
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x05,0x9F,0x69), 0.55))
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x04,0x78,0x56), 1.0))

    $radius = [Math]::Round($size * 0.225)
    $ctx.DrawRoundedRectangle($bg, $null, [System.Windows.Rect]::new(0, 0, $size, $size), $radius, $radius)

    # 1b. Glass highlight atas
    $hl = New-Object System.Windows.Media.LinearGradientBrush
    $hl.StartPoint = [System.Windows.Point]::new(0, 0)
    $hl.EndPoint   = [System.Windows.Point]::new(0, 1)
    $hl.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromArgb(0x5A,0xFF,0xFF,0xFF), 0.0))
    $hl.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromArgb(0x00,0xFF,0xFF,0xFF), 1.0))
    $ctx.DrawRoundedRectangle($hl, $null,
        [System.Windows.Rect]::new($size*0.03, $size*0.03, $size*0.94, $size*0.44),
        $radius*0.95, $radius*0.95)

    # 2. Amplop
    $envX = $size*0.16; $envY = $size*0.22
    $envW = $size*0.68; $envH = $size*0.50
    $envRound = [Math]::Round($size*0.06)
    $lineColor = [System.Windows.Media.Color]::FromRgb(0xCB,0xE9,0xDC)

    # 2a. Bayangan lembut amplop (memberi kesan terangkat dari tile)
    $envShadow = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x3A,0x00,0x33,0x22))
    $ctx.DrawRoundedRectangle($envShadow, $null,
        [System.Windows.Rect]::new($envX + $size*0.012, $envY + $size*0.018, $envW, $envH), $envRound, $envRound)

    # 2b. Badan amplop (fill putih lembut)
    $envFill = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xF7,0xFC,0xFA))
    $ctx.DrawRoundedRectangle($envFill, $null,
        [System.Windows.Rect]::new($envX, $envY, $envW, $envH), $envRound, $envRound)

    # 2b. Flap V dari tepi atas ke tengah bawah
    $flapGeo = [System.Windows.Media.Geometry]::Parse(
        ("M {0},{1} L {2},{3} L {4},{5} Z" -f `
            ($envX + $size*0.005), ($envY + $size*0.012),
            ($envX + $envW/2),      ($envY + $envH*0.62),
            ($envX + $envW - $size*0.005), ($envY + $size*0.012)))
    $flapFill = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xE6,0xF7,0xEF))
    $flapPen  = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.020)
    $flapPen.StartLineCap = 'Round'; $flapPen.EndLineCap = 'Round'; $flapPen.LineJoin = 'Round'
    $ctx.DrawGeometry($flapFill, $flapPen, $flapGeo)

    # 2c. Garis diagonal saku bawah (kiri & kanan dari bawah-flap ke sudut)
    $pocketPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.016)
    $pocketPen.StartLineCap = 'Round'; $pocketPen.EndLineCap = 'Round'
    $pocket = [System.Windows.Media.Geometry]::Parse(
        ("M {0},{1} L {2},{3} L {4},{5} M {6},{7} L {8},{9}" -f `
            ($envX + $size*0.01),  ($envY + $envH - $size*0.02),
            ($envX + $envW/2),     ($envY + $envH*0.62),
            ($envX + $envW - $size*0.01), ($envY + $envH - $size*0.02),
            ($envX + $size*0.01),  ($envY + $envH - $size*0.02),
            ($envX + $envW - $size*0.01), ($envY + $envH - $size*0.02)))
    $ctx.DrawGeometry($null, $pocketPen, $pocket)

    # 2d. Bingkai amplop
    $bodyPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.022)
    $ctx.DrawRoundedRectangle($null, $bodyPen,
        [System.Windows.Rect]::new($envX, $envY, $envW, $envH), $envRound, $envRound)

    # 3. Segel lilin (wax seal) di bawah tengah amplop
    $sealR  = $size*0.150
    $sealCX = $envX + $envW/2
    $sealCY = $envY + $envH*0.97

    # 3a. Bayangan segel
    $shadow = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x40,0x00,0x00,0x00))
    $ctx.DrawEllipse($shadow, $null,
        [System.Windows.Point]::new($sealCX + $size*0.008, $sealCY + $size*0.012), $sealR, $sealR)

    # 3b. Badan segel marun
    $sealFill = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xB9,0x1C,0x1C))
    $sealRim  = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0x7F,0x1D,0x1D)), $size*0.014)
    $ctx.DrawEllipse($sealFill, $sealRim,
        [System.Windows.Point]::new($sealCX, $sealCY), $sealR, $sealR)

    # 3c. Ring emas
    $goldPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xF5,0xC0,0x6B)), $size*0.010)
    $ctx.DrawEllipse($null, $goldPen,
        [System.Windows.Point]::new($sealCX, $sealCY), $sealR*0.78, $sealR*0.78)

    # 3d. Teks "SD" pada segel
    # 3d. Teks "SD" pada segel — hanya untuk ukuran >= 32px (di bawah itu
    #     teks tinggal coretan tidak terbaca; segel polos tetap jelas).
    if ($size -ge 32) {
    # Typeface default (1-arg ctor) lalu ganti bobot jadi Bold — menghindari
    # keanehan PowerShell ([System.Windows.FontStyles]::Bold bisa bernilai null).
    $base = New-Object System.Windows.Media.Typeface "Segoe UI"
    $typeface = New-Object System.Windows.Media.Typeface -ArgumentList (
        (New-Object System.Windows.Media.FontFamily "Segoe UI"),
        $base.Style,
        [System.Windows.FontWeights]::Bold,
        $base.Stretch)
    $tf = [System.Windows.Media.FormattedText]::new(
        "SD",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Windows.FlowDirection]::LeftToRight,
        $typeface,
        $size*0.115,
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xFF,0xF7,0xED)),
        1.25)
    $tf.TextAlignment = [System.Windows.TextAlignment]::Center
    # Nudge vertikal kecil: Height memuat ruang descender, tanpa nudge teks tampak
    # sedikit terlalu tinggi di lingkaran segel.
    $ctx.DrawText($tf, [System.Windows.Point]::new($sealCX - $tf.Width/2, $sealCY - $tf.Height/2 + $size*0.008))
    }

    # 4. Sparkle kanan-atas (dua titik, besar + kecil)
    $ctx.DrawEllipse(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0xB4,0xFF,0xFF,0xFF)),
        $null,
        [System.Windows.Point]::new($size*0.82, $size*0.14),
        $size*0.026, $size*0.026)
    $ctx.DrawEllipse(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x78,0xFF,0xFF,0xFF)),
        $null,
        [System.Windows.Point]::new($size*0.90, $size*0.24),
        $size*0.014, $size*0.014)

    $ctx.Close()
    return $vis
}

# ---------------------------------------------------------------------
#  Render tiap ukuran -> PNG in-memory -> rakit file .ICO
# ---------------------------------------------------------------------
$sizes  = @(256, 64, 48, 32, 24, 16)
$frames = @{}

foreach ($s in $sizes) {
    $vis = New-IconVisual -size $s
    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($vis)

    $enc = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = [System.IO.MemoryStream]::new()
    $enc.Save($ms)
    $frames[$s] = $ms.ToArray()

    if ($s -in 256, 32, 16) {
        [System.IO.File]::WriteAllBytes((Join-Path $previewDir ("icon-{0}.png" -f $s)), $frames[$s])
    }
}

# PNG 256 penuh untuk dokumentasi/README
[System.IO.File]::WriteAllBytes($png256, $frames[256])

# --- Konversi frame PNG -> DIB (BMP 32bit bottom-up + AND mask) -------------
# Compiler C# (Win32 resource) menolak frame PNG di dalam ICO; harus format
# BMP klasik inilah yang diterima csc maupun Explorer lama.
# Dikerjakan inline (tanpa function) supaya byte[] tidak ter-unroll oleh pipeline.
$dibs = @{}
foreach ($s in $sizes) {
    $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
        [System.IO.MemoryStream]::new($frames[$s]),
        [System.Windows.Media.Imaging.BitmapCreateOptions]::None,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $src = $decoder.Frames[0]

    $stride = $s * 4
    $pixels = New-Object byte[] ($stride * $s)
    $src.CopyPixels($pixels, $stride, 0)

    $dib = [System.IO.MemoryStream]::new()
    $dbw = [System.IO.BinaryWriter]::new($dib)

    # BITMAPINFOHEADER
    $dbw.Write([UInt32]40)                    # biSize
    $dbw.Write([Int32]$s)                     # biWidth
    $dbw.Write([Int32]($s * 2))               # biHeight = gambar + mask
    $dbw.Write([UInt16]1)                     # biPlanes
    $dbw.Write([UInt16]32)                    # biBitCount
    $dbw.Write([UInt32]0)                     # biCompression = BI_RGB
    $dbw.Write([UInt32]($stride * $s))        # biSizeImage
    $dbw.Write([Int32]0); $dbw.Write([Int32]0)          # ppm
    $dbw.Write([UInt32]0); $dbw.Write([UInt32]0)        # clrUsed/Important

    # Pixel bottom-up (BGRA32; channel alpha menangani transparansi)
    for ($y = $s - 1; $y -ge 0; $y--) {
        $dbw.Write($pixels, $y * $stride, $stride)
    }

    # AND mask 1bpp nol (transparansi diserahkan ke channel alpha)
    $maskStride = [int][Math]::Ceiling([Math]::Ceiling($s / 8.0) / 4.0) * 4
    $zeroRow = New-Object byte[] $maskStride
    for ($y = 0; $y -lt $s; $y++) { $dbw.Write($zeroRow) }

    $dbw.Flush()
    $dibs[$s] = $dib.ToArray()      # assignment langsung: byte[] utuh
    $dbw.Close()
}

# --- Rakit ICO: ICONDIR + ICONDIRENTRY[] + data DIB ---
$icoStream = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($icoStream)

$bw.Write([UInt16]0)                       # reserved
$bw.Write([UInt16]1)                       # type = icon
$bw.Write([UInt16]$sizes.Count)            # jumlah gambar

$offset = 6 + 16*$sizes.Count   # ICONDIR = 6 byte (2+2+2), BUKAN 16!
$entries = @()
foreach ($s in $sizes) {
    $len = $dibs[$s].Length
    $entries += ,@($s, $len, $offset)
    $offset += $len
}

foreach ($e in $entries) {
    $s = $e[0]
    $byte = if ($s -ge 256) { 0 } else { $s }   # 256 ditulis sebagai 0
    $bw.Write([Byte]$byte)     # width
    $bw.Write([Byte]$byte)     # height
    $bw.Write([Byte]0)         # color count
    $bw.Write([Byte]0)         # reserved
    $bw.Write([UInt16]1)       # planes
    $bw.Write([UInt16]32)      # bit count
    $bw.Write([UInt32]$e[1])   # ukuran data
    $bw.Write([UInt32]$e[2])   # offset data
}

foreach ($s in $sizes) {
    $bw.Write($dibs[$s])
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())
$bw.Close()

Write-Host ""
Write-Host "  Ikon SuDesApp berhasil dibuat:" -ForegroundColor Green
Write-Host ("    - {0}" -f $icoPath)
Write-Host ("    - {0}" -f $png256)

# --- Susun ulang preview.html dari template + ikon terbaru (data URI) -------
$templatePath = Join-Path $previewDir 'preview.template.html'
$previewPath  = Join-Path $previewDir    'preview.html'
if (Test-Path $templatePath) {
    $html = [System.IO.File]::ReadAllText($templatePath)
    $html = $html.Replace('%%ICON256%%', ('data:image/png;base64,{0}' -f [Convert]::ToBase64String($frames[256])))
    $html = $html.Replace('%%ICON32%%',  ('data:image/png;base64,{0}' -f [Convert]::ToBase64String($frames[32])))
    $html = $html.Replace('%%ICON16%%',  ('data:image/png;base64,{0}' -f [Convert]::ToBase64String($frames[16])))
    [System.IO.File]::WriteAllText($previewPath, $html)
    Write-Host ("    - preview: {0}" -f $previewPath)
}

Write-Host ""
Write-Host "  Desain: tile emerald gradient + amplop + segel lilin 'SD' (vector, multi-size)."
