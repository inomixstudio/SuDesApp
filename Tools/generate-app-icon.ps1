# =====================================================================
#  SuDesApp - Generator Ikon Aplikasi (Versi 3.0)
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
#  DESAIN v3 (viewBox 1024, diskalakan ke tiap ukuran):
#    1. Ubin rounded-square, gradient emerald lebih dalam + cincin tepi
#       ("machined") + kilap kaca diagonal
#    2. Amplop putih dengan gradien halus, flap V & saku
#    3. Segel lilin marun gradien + ring emas ganda bertuliskan "SD"
#    4. Sparkle kecil kanan-atas
# ---------------------------------------------------------------------
function New-IconVisual([double]$size) {
    $vis = New-Object System.Windows.Media.DrawingVisual
    $ctx = $vis.RenderOpen()

    # 1a. Latar gradient emerald yang lebih dalam (mulai segar, berakhir pekat)
    $bg = New-Object System.Windows.Media.LinearGradientBrush
    $bg.StartPoint = [System.Windows.Point]::new(0, 0)
    $bg.EndPoint   = [System.Windows.Point]::new(1, 1)
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x14,0xB8,0x84), 0.0))
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x06,0xA1,0x6C), 0.55))
    $bg.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x04,0x78,0x57), 1.0))

    $radius = [Math]::Round($size * 0.225)
    $ctx.DrawRoundedRectangle($bg, $null, [System.Windows.Rect]::new(0, 0, $size, $size), $radius, $radius)

    # Klip seluruh seni ke bidang membulat agar kilap tidak "bocor" ke luar.
    $clip = [System.Windows.Media.RectangleGeometry]::new(
        [System.Windows.Rect]::new(0, 0, $size, $size), $radius, $radius)
    $ctx.PushClip($clip)

    # 1b. Kilap kaca utama (atas)
    $hl = New-Object System.Windows.Media.LinearGradientBrush
    $hl.StartPoint = [System.Windows.Point]::new(0, 0)
    $hl.EndPoint   = [System.Windows.Point]::new(0, 1)
    $hl.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromArgb(0x4D,0xFF,0xFF,0xFF), 0.0))
    $hl.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromArgb(0x00,0xFF,0xFF,0xFF), 1.0))
    $ctx.DrawRoundedRectangle($hl, $null,
        [System.Windows.Rect]::new($size*0.03, $size*0.03, $size*0.94, $size*0.46),
        $radius*0.9, $radius*0.9)

    # 1c. Cincin tepi halus ("machined"): sorot terang lembut di dalam + bayang
    #     gelap tipis di sana — memberi dimensi tanpa gambar tambahan.
    $edgeHighlightPen = [System.Windows.Media.Pen]::new(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x26,0xFF,0xFF,0xFF)), $size*0.010)
    $ctx.DrawRoundedRectangle($null, $edgeHighlightPen,
        [System.Windows.Rect]::new($size*0.05, $size*0.05, $size*0.90, $size*0.90), $radius*0.80, $radius*0.80)
    $edgeShadePen = [System.Windows.Media.Pen]::new(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x2E,0x00,0x22,0x11)), $size*0.016)
    $ctx.DrawRoundedRectangle($null, $edgeShadePen,
        [System.Windows.Rect]::new($size*0.075, $size*0.075, $size*0.85, $size*0.85), $radius*0.68, $radius*0.68)

    # 2. Amplop
    $envX = $size*0.15; $envY = $size*0.24
    $envW = $size*0.70; $envH = $size*0.52
    $envRound = [Math]::Round($size*0.06)
    $lineColor = [System.Windows.Media.Color]::FromRgb(0xC9,0xE8,0xDA)

    # 2a. Bayangan lembut amplop (terangkat dari ubin)
    $envShadow = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x30,0x00,0x33,0x22))
    $ctx.DrawRoundedRectangle($envShadow, $null,
        [System.Windows.Rect]::new($envX + $size*0.012, $envY + $size*0.016, $envW, $envH), $envRound, $envRound)

    # 2b. Badan amplop: gradien vertikal halus putih → hijau-pudar
    $envFill = New-Object System.Windows.Media.LinearGradientBrush
    $envFill.StartPoint = [System.Windows.Point]::new(0, 0)
    $envFill.EndPoint   = [System.Windows.Point]::new(0, 1)
    $envFill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0xFF,0xFF,0xFF), 0.0))
    $envFill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0xEC,0xF7,0xF1), 1.0))
    $ctx.DrawRoundedRectangle($envFill, $null,
        [System.Windows.Rect]::new($envX, $envY, $envW, $envH), $envRound, $envRound)

    # 2c. Flap V dari tepi atas ke tengah bawah
    $cx = $envX + $envW/2
    $flapGeo = [System.Windows.Media.Geometry]::Parse(
        ("M {0},{1} L {2},{3} L {4},{5} Z" -f `
            ($envX + $size*0.01), ($envY + $size*0.02),
            $cx,                    ($envY + $envH*0.60),
            ($envX + $envW - $size*0.01), ($envY + $size*0.02)))
    $flapFill = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xE4,0xF6,0xED))
    $flapPen  = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.018)
    $flapPen.StartLineCap = 'Round'; $flapPen.EndLineCap = 'Round'; $flapPen.LineJoin = 'Round'
    $ctx.DrawGeometry($flapFill, $flapPen, $flapGeo)

    # 2d. Saku bawah: dua diagonal ke tengah + lipatan dasar (garis tipis)
    $pocketPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.015)
    $pocketPen.StartLineCap = 'Round'; $pocketPen.EndLineCap = 'Round'
    $pocket = [System.Windows.Media.Geometry]::Parse(
        ("M {0},{1} L {2},{3} M {4},{5} L {6},{7} M {8},{9} L {10},{11}" -f `
            ($envX + $size*0.012), ($envY + $envH - $size*0.02),
            $cx,                    ($envY + $envH*0.42),
            ($envX + $envW - $size*0.012), ($envY + $envH - $size*0.02),
            $cx,                    ($envY + $envH*0.42),
            ($envX + $size*0.012),  ($envY + $envH - $size*0.02),
            ($envX + $envW - $size*0.012), ($envY + $envH - $size*0.02)))
    $ctx.DrawGeometry($null, $pocketPen, $pocket)

    # 2e. Bingkai amplop
    $bodyPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new($lineColor), $size*0.020)
    $ctx.DrawRoundedRectangle($null, $bodyPen,
        [System.Windows.Rect]::new($envX, $envY, $envW, $envH), $envRound, $envRound)

    # 3. Segel lilin (wax seal) di tengah bawah amplop — gradien marun yang lebih hidup
    $sealR  = $size*0.145
    $sealCX = $cx
    $sealCY = $envY + $envH*0.99

    # 3a. Bayangan segel
    $shadow = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x38,0x00,0x00,0x00))
    $ctx.DrawEllipse($shadow, $null,
        [System.Windows.Point]::new($sealCX + $size*0.008, $sealCY + $size*0.012), $sealR, $sealR)

    # 3b. Badan segel gradien marun
    $sealFill = New-Object System.Windows.Media.LinearGradientBrush
    $sealFill.StartPoint = [System.Windows.Point]::new(0, 0)
    $sealFill.EndPoint   = [System.Windows.Point]::new(0, 1)
    $sealFill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0xC6,0x28,0x28), 0.0))
    $sealFill.GradientStops.Add([System.Windows.Media.GradientStop]::new([System.Windows.Media.Color]::FromRgb(0x96,0x1C,0x1C), 1.0))
    $sealRim  = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0x7F,0x1D,0x1D)), $size*0.012)
    $ctx.DrawEllipse($sealFill, $sealRim,
        [System.Windows.Point]::new($sealCX, $sealCY), $sealR, $sealR)

    # 3c. Ring emas ganda (luar + cincin tipis dalam)
    $goldPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xF5,0xC0,0x6B)), $size*0.010)
    $ctx.DrawEllipse($null, $goldPen,
        [System.Windows.Point]::new($sealCX, $sealCY), $sealR*0.78, $sealR*0.78)
    $goldThinPen = [System.Windows.Media.Pen]::new([System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0xCC,0xF5,0xC0,0x6B)), $size*0.006)
    $ctx.DrawEllipse($null, $goldThinPen,
        [System.Windows.Point]::new($sealCX, $sealCY), $sealR*0.56, $sealR*0.56)

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
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x9A,0xFF,0xFF,0xFF)),
        $null,
        [System.Windows.Point]::new($size*0.82, $size*0.15),
        $size*0.026, $size*0.026)
    $ctx.DrawEllipse(
        [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(0x5C,0xFF,0xFF,0xFF)),
        $null,
        [System.Windows.Point]::new($size*0.90, $size*0.25),
        $size*0.014, $size*0.014)

    $ctx.Pop()   # lepas klip membulat
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
Write-Host "  Desain: tile emerald gradient + cincin machined + amplop + segel lilin 'SD' (vector, multi-size, v3)."
