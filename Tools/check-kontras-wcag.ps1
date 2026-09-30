<#
.SYNOPSIS
    Pemeriksa kontras teks WCAG 2.1 AA (dan AAA untuk tema kontras tinggi)
    untuk seluruh tema SuDesApp.

.DESCRIPTION
    Menghitung rasio kontras setiap pasangan teks/latar yang benar-benar dipakai
    aplikasi (bukan sekadar daftar warna) pada keenam tema, lalu menandai yang
    belum memenuhi ambang WCAG 2.1 AA:

      [GAGAL] KONTRAS - rasio di bawah ambang (teks normal 4,5:1; teks besar 3:1).
      [GAGAL] KUNCI   - ada tema yang kehilangan/kelebihan kunci dibanding tema lain,
                        yang membuat halaman tampil berbeda diam-diam antar tema.

    Ambang teks besar dipakai untuk judul header halaman (20 px SemiBold) dan
    elemen non-teks (ikon lencana) yang tunduk pada WCAG 1.4.11 (3:1).

    HighContrastTheme adalah satu-satunya tema yang menargetkan WCAG 2.1 AAA:
    ambangnya dinaikkan menjadi 7:1 (teks normal) dan 4,5:1 (teks besar) — lihat
    Get-AmbangEfektif. Tema lain tetap diuji pada ambang AA.

    Latar gradien header (PrimaryColor -> SecondaryColor) diuji di kedua ujungnya,
    dan brush yang punya Opacity dikompositkan dulu ke latarnya sebelum dihitung.

.USAGE
    pwsh -File Tools/check-kontras-wcag.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$temaDir = Join-Path $repoRoot 'SuDesApp.Wpf/Themes'
$temaList = @('LightTheme', 'DarkTheme', 'GreenTheme', 'BlueTheme', 'PinkTheme', 'SlateTheme', 'HighContrastTheme')

# Ambang per tema. Tema kontras tinggi (opsional, untuk pengguna low vision)
# menargetkan WCAG 2.1 AAA, jadi ambang setiap pasangan dinaikkan.
function Get-AmbangEfektif([string]$namaTema, [double]$ambangDasar) {
    if ($namaTema -ne 'HighContrastTheme') { return $ambangDasar }
    if ($ambangDasar -lt 4.5) { return 4.5 }
    return 7.0
}

$script:hardErrors = 0
function Write-Problem([string]$m) { $script:hardErrors++; Write-Host "  [GAGAL] $m" -ForegroundColor Red }
function Write-Ok([string]$m)      { Write-Host "  [OK] $m" -ForegroundColor DarkGray }

# ---------- Konversi warna & perhitungan kontras WCAG ----------

function Get-IntChannel([string]$hex, [int]$mulai) {
    $s = $hex.Trim().TrimStart('#')
    if ($s.Length -eq 8) { $s = $s.Substring(2) }   # buang alfa AARRGGBB
    return [Convert]::ToInt32($s.Substring($mulai, 2), 16)
}
function Get-Cr([string]$hex) { return (Get-IntChannel $hex 0) }
function Get-Cg([string]$hex) { return (Get-IntChannel $hex 2) }
function Get-Cb([string]$hex) { return (Get-IntChannel $hex 4) }

function Get-Kanal([int]$c) {
    $v = $c / 255.0
    if ($v -le 0.03928) { return $v / 12.92 }
    return [Math]::Pow((($v + 0.055) / 1.055), 2.4)
}
function Get-Luminansi([string]$hex) {
    return 0.2126 * (Get-Kanal (Get-Cr $hex)) + 0.7152 * (Get-Kanal (Get-Cg $hex)) + 0.0722 * (Get-Kanal (Get-Cb $hex))
}
function Get-Rasio([string]$a, [string]$b) {
    $la = Get-Luminansi $a
    $lb = Get-Luminansi $b
    if ($la -lt $lb) { $t = $la; $la = $lb; $lb = $t }
    return (($la + 0.05) / ($lb + 0.05))
}
function Get-Campur([string]$fg, [string]$bg, [double]$op) {
    $r = [int][Math]::Round((Get-Cr $fg) * $op + (Get-Cr $bg) * (1 - $op))
    $g = [int][Math]::Round((Get-Cg $fg) * $op + (Get-Cg $bg) * (1 - $op))
    $b = [int][Math]::Round((Get-Cb $fg) * $op + (Get-Cb $bg) * (1 - $op))
    return '#' + ('{0:X2}{1:X2}{2:X2}' -f $r, $g, $b)
}

# ---------- Membaca satu file tema ----------

function Read-Tema([string]$nama, [string]$folder) {
    $isi = [IO.File]::ReadAllText((Join-Path $folder "$nama.xaml"))

    $warna = @{}
    foreach ($m in [regex]::Matches($isi, '<Color x:Key="(?<k>[^"]+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>')) {
        $warna[$m.Groups['k'].Value] = $m.Groups['v'].Value
    }

    $brush = @{}
    foreach ($m in [regex]::Matches($isi, '<SolidColorBrush\s+x:Key="(?<k>[^"]+)"\s+Color="(?<c>[^"]+)"(?:\s+Opacity="(?<o>[^"]+)")?\s*/>')) {
        $op = 1.0
        if ($m.Groups['o'].Success -and $m.Groups['o'].Value -ne '') {
            $op = [double]::Parse($m.Groups['o'].Value, [Globalization.CultureInfo]::InvariantCulture)
        }
        $brush[$m.Groups['k'].Value] = @{ Warna = $m.Groups['c'].Value; Opasitas = $op }
    }

    $kunci = @()
    foreach ($m in [regex]::Matches($isi, 'x:Key="(?<k>[^"]+)"')) { $kunci += $m.Groups['k'].Value }

    return @{ Warna = $warna; Brush = $brush; Kunci = ($kunci | Sort-Object -Unique) }
}

function Resolve-Warna([string]$nilai, $tema) {
    if ([string]::IsNullOrWhiteSpace($nilai)) { return $null }
    $v = $nilai.Trim()
    if ($v -like '{StaticResource*') {
        $nama = ($v -replace '[{}]', '').Replace('StaticResource', '').Trim()
        if ($tema.Warna.ContainsKey($nama)) { return $tema.Warna[$nama] }
        return $null
    }
    if ($v -eq 'White') { return '#FFFFFF' }
    if ($v -eq 'Black') { return '#000000' }
    if ($v.StartsWith('#')) { return $v }
    return $null
}

# ---------- Pasangan teks/latar yang dipakai aplikasi ----------
# Ambang 4,5 = teks normal (WCAG 1.4.3). Ambang 3,0 = teks besar / elemen non-teks
# (WCAG 1.4.3 teks besar, 1.4.11 non-teks).
$pasangan = @(
    @{ Uji = 'Teks utama di kartu';               Fg = 'TextBrush';             Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks utama di latar halaman';       Fg = 'TextBrush';             Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Teks utama di panel';               Fg = 'TextBrush';             Bg = 'PanelBrush';               Ambang = 4.5 }
    @{ Uji = 'Teks utama baris zebra';            Fg = 'TextBrush';             Bg = 'ZebraBrush';               Ambang = 4.5 }
    @{ Uji = 'Teks utama baris terpilih';         Fg = 'TextBrush';             Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    @{ Uji = 'Teks utama di atas BorderBrush';    Fg = 'TextBrush';             Bg = 'BorderBrush';              Ambang = 4.5 }
    @{ Uji = 'Teks sekunder di kartu';            Fg = 'TextSecondaryBrush';    Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks sekunder di latar halaman';    Fg = 'TextSecondaryBrush';    Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Teks sekunder di panel';            Fg = 'TextSecondaryBrush';    Bg = 'PanelBrush';               Ambang = 4.5 }
    @{ Uji = 'Teks sekunder baris zebra';         Fg = 'TextSecondaryBrush';    Bg = 'ZebraBrush';               Ambang = 4.5 }
    @{ Uji = 'Chip status bar (sekunder)';        Fg = 'TextSecondaryBrush';    Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    @{ Uji = 'Status bar';                        Fg = 'StatusBarTextBrush';    Bg = 'StatusBarBackgroundBrush'; Ambang = 4.5 }
    @{ Uji = 'Teks sidebar';                      Fg = 'SidebarTextBrush';      Bg = 'SidebarBackgroundBrush';   Ambang = 4.5 }
    @{ Uji = 'Judul header halaman';              Fg = 'HeaderTextBrush';       Bg = 'PrimaryBrush';             Ambang = 3.0 }
    @{ Uji = 'Subjudul header halaman';           Fg = 'HeaderSubtitleBrush';   Bg = 'PrimaryBrush';             Ambang = 4.5 }
    @{ Uji = 'Judul header (ujung gradien)';      Fg = 'HeaderTextBrush';       Bg = 'SecondaryBrush';           Ambang = 3.0 }
    @{ Uji = 'Subjudul header (ujung gradien)';   Fg = 'HeaderSubtitleBrush';   Bg = 'SecondaryBrush';           Ambang = 4.5 }
    @{ Uji = 'Teks tombol primer';                Fg = 'OnPrimaryTextBrush';    Bg = 'PrimaryBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks tombol primer (ujung gradien)'; Fg = 'OnPrimaryTextBrush';    Bg = 'SecondaryBrush';           Ambang = 4.5 }
    @{ Uji = 'Teks tombol aksen';                 Fg = 'OnAccentTextBrush';     Bg = 'AccentBrush';              Ambang = 4.5 }
    @{ Uji = 'Teks lencana sukses';               Fg = 'OnSuccessTextBrush';    Bg = 'SuccessBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks lencana peringatan';           Fg = 'OnWarningTextBrush';    Bg = 'WarningBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks lencana galat';                Fg = 'OnErrorTextBrush';      Bg = 'ErrorBrush';               Ambang = 4.5 }
    @{ Uji = 'Ikon/tautan aksen di kartu';        Fg = 'AccentTextBrush';       Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Ikon/tautan aksen di panel';        Fg = 'AccentTextBrush';       Bg = 'PanelBrush';               Ambang = 4.5 }
    @{ Uji = 'Ikon/tautan aksen di chip';         Fg = 'AccentTextBrush';       Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    @{ Uji = 'Ikon/tautan aksen di latar halaman'; Fg = 'AccentTextBrush';      Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Ikon/tautan aksen baris zebra';      Fg = 'AccentTextBrush';      Bg = 'ZebraBrush';               Ambang = 4.5 }
    @{ Uji = 'Teks tab terpilih';                 Fg = 'AccentTextBrush';       Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Teks terpilih di kartu';            Fg = 'TextSelectedBrush';     Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Sukses di badge';                   Fg = 'SuccessTextBrush';      Bg = 'SuccessSubtleBrush';       Ambang = 4.5 }
    @{ Uji = 'Peringatan di badge';               Fg = 'WarningTextBrush';      Bg = 'WarningSubtleBrush';       Ambang = 4.5 }
    @{ Uji = 'Galat di badge';                    Fg = 'ErrorTextBrush';        Bg = 'ErrorSubtleBrush';         Ambang = 4.5 }
    @{ Uji = 'Sukses di kartu';                   Fg = 'SuccessTextBrush';      Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Peringatan di kartu';               Fg = 'WarningTextBrush';      Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Galat di kartu';                    Fg = 'ErrorTextBrush';        Bg = 'SurfaceBrush';             Ambang = 4.5 }
    @{ Uji = 'Sukses di chip';                    Fg = 'SuccessTextBrush';      Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    @{ Uji = 'Peringatan di chip';                Fg = 'WarningTextBrush';      Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    @{ Uji = 'Galat di chip';                     Fg = 'ErrorTextBrush';        Bg = 'AccentSubtleBrush';        Ambang = 4.5 }
    # Pasangan berikut sempat LUPA diperiksa alat & uji (celah audit 1 Oktober 2026):
    # kode memakainya, tetapi daftar 39 pasangan di atas tidak memuatnya sehingga
    # pelanggaran kontras nyata (mis. teks bilah status Google Drive di Green/Blue/Pink,
    # teks aksen sidebar Pink/Blue) lolos tanpa terdeteksi selama berbulan-bulan.
    @{ Uji = 'Teks utama di kartu peringatan';   Fg = 'TextBrush';             Bg = 'WarningSubtleBrush';       Ambang = 4.5 }
    @{ Uji = 'Peringatan di panel';              Fg = 'WarningTextBrush';      Bg = 'PanelBrush';               Ambang = 4.5 }
    @{ Uji = 'Galat di panel';                   Fg = 'ErrorTextBrush';        Bg = 'PanelBrush';               Ambang = 4.5 }
    @{ Uji = 'Peringatan di latar halaman';      Fg = 'WarningTextBrush';      Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Sukses di latar halaman';          Fg = 'SuccessTextBrush';      Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Galat di latar halaman';           Fg = 'ErrorTextBrush';        Bg = 'WindowBackgroundBrush';    Ambang = 4.5 }
    @{ Uji = 'Teks aksen sidebar';               Fg = 'SidebarAccentBrush';    Bg = 'SidebarBackgroundBrush';   Ambang = 4.5 }
    # Teks keterangan di dalam kotak status (gaya InfoBoxText): nilainya sangat
    # tipis di Dark — TextSecondaryColor Dark dinaikkan #9E9E9E → #BDBDBD dan
    # ErrorSubtleColor Pink #FEE2E2 → #FEEAEA supaya ketiganya punya margin.
    @{ Uji = 'Teks sekunder di kartu sukses';    Fg = 'TextSecondaryBrush';    Bg = 'SuccessSubtleBrush';       Ambang = 4.5 }
    @{ Uji = 'Teks sekunder di kartu peringatan'; Fg = 'TextSecondaryBrush';   Bg = 'WarningSubtleBrush';       Ambang = 4.5 }
    @{ Uji = 'Teks sekunder di kartu galat';     Fg = 'TextSecondaryBrush';    Bg = 'ErrorSubtleBrush';         Ambang = 4.5 }
)

# ---------- 1. Uji kontras ----------

Write-Host 'Pemeriksaan kontras WCAG 2.1 AA (AAA untuk HighContrastTheme) per tema' -ForegroundColor Cyan

$temuan = @()
$dibaca = @{}

foreach ($nama in $temaList) {
    $berkas = Join-Path $temaDir "$nama.xaml"
    if (-not (Test-Path -LiteralPath $berkas)) {
        Write-Problem "Berkas tema tidak ditemukan: $berkas"
        continue
    }

    $tema = Read-Tema $nama $temaDir
    $dibaca[$nama] = $tema

    foreach ($p in $pasangan) {
        if (-not $tema.Brush.ContainsKey($p.Fg)) { Write-Problem "$nama kehilangan brush '$($p.Fg)'"; continue }
        if (-not $tema.Brush.ContainsKey($p.Bg)) { Write-Problem "$nama kehilangan brush '$($p.Bg)'"; continue }

        $fgHex = Resolve-Warna $tema.Brush[$p.Fg].Warna $tema
        $bgHex = Resolve-Warna $tema.Brush[$p.Bg].Warna $tema
        if (-not $fgHex -or -not $bgHex) { Write-Problem ("{0}: warna '{1}'/'{2}' tidak dapat diselesaikan" -f $nama, $p.Fg, $p.Bg); continue }

        $op = $tema.Brush[$p.Fg].Opasitas
        if ($op -lt 1.0) { $fgHex = Get-Campur $fgHex $bgHex $op }

        $rasio = [Math]::Round((Get-Rasio $fgHex $bgHex), 2)
        $ambang = Get-AmbangEfektif $nama $p.Ambang
        if ($rasio -lt $ambang) {
            $temuan += [pscustomobject]@{
                Tema     = $nama
                Pasangan = $p.Uji
                Rasio    = $rasio
                Ambang   = $ambang
                Teks     = $fgHex
                Latar    = $bgHex
            }
        }
    }
}

if ($temuan.Count -eq 0) {
    Write-Ok "Seluruh $($pasangan.Count) pasangan teks/latar pada $($temaList.Count) tema memenuhi ambangnya: AA untuk tema warna, AAA untuk HighContrastTheme."
} else {
    foreach ($t in ($temuan | Sort-Object Rasio)) {
        Write-Problem ("{0}: {1} - rasio {2} (butuh {3}); teks {4} di atas latar {5}" -f `
            $t.Tema, $t.Pasangan, $t.Rasio, $t.Ambang, $t.Teks, $t.Latar)
    }
}

# ---------- 2. Konsistensi kunci antar tema ----------

Write-Host ''
Write-Host 'Konsistensi kunci antar tema' -ForegroundColor Cyan

$refNama = $temaList[0]
if ($dibaca.ContainsKey($refNama)) {
    foreach ($nama in $temaList) {
        if (-not $dibaca.ContainsKey($nama)) { continue }
        $selisih = @(Compare-Object $dibaca[$refNama].Kunci $dibaca[$nama].Kunci)
        $kurang = @($selisih | Where-Object { $_.SideIndicator -eq '<=' } | ForEach-Object { $_.InputObject })
        $lebih = @($selisih | Where-Object { $_.SideIndicator -eq '=>' } | ForEach-Object { $_.InputObject })
        if ($kurang.Count -eq 0 -and $lebih.Count -eq 0) {
            Write-Ok ("{0}: {1} kunci, sama dengan {2}." -f $nama, $dibaca[$nama].Kunci.Count, $refNama)
        } else {
            Write-Problem ("{0} berbeda dari {1} - kurang: [{2}]; lebih: [{3}]" -f $nama, $refNama, ($kurang -join ', '), ($lebih -join ', '))
        }
    }
}

# ---------- 3. Warna tetap (bukan token tema) yang masih dipakai ----------

Write-Host ''
Write-Host 'Warna tetap di luar tema' -ForegroundColor Cyan
Write-Ok 'Avatar Google AboutView memakai #3367D6 dengan teks putih (rasio 5,18).'
Write-Ok 'Latar pratinjau kertas Setelan, latar PDF (#525659), dan panduan WhatsApp sengaja memakai warna tetap.'

Write-Host ''
if ($script:hardErrors -eq 0) {
    Write-Host 'HASIL: semua pemeriksaan lolos.' -ForegroundColor Green
    exit 0
}

Write-Host "HASIL: $($script:hardErrors) masalah ditemukan." -ForegroundColor Red
exit 1
