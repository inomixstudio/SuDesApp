<#
.SYNOPSIS
    Pemeriksa kesehatan file resource SuDesApp - untuk CI maupun lokal.

.DESCRIPTION
    Mendeteksi tiga jenis masalah resource (pengalaman nyata repo ini:
    stub 0 byte, folder hantu "Template", font & PNG tak terpakai):

      [GAGAL] MISSING     - file literal yang dirujuk csproj tidak ada di disk.
      [GAGAL] DEADGLOB    - pola glob csproj tidak mencocokkan file apa pun.
      [PERINGATAN] ORPHAN - file resource di disk tidak dirujuk csproj maupun kode
                            (menjadi error bila switch -FailOnOrphan diberikan).

    Scope scan orphan: folder Resources, Templates, Scripts, Configuration, Data.
    Pengecualian yang disengaja ditulis di Tools/resource-check-ignore.txt
    (satu pola per baris relatif root, wildcard * boleh, '#' = komentar).

.USAGE
    pwsh -File Tools/check-resources.ps1 [-FailOnOrphan]
#>
[CmdletBinding()]
param(
    [switch]$FailOnOrphan
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$sep  = [IO.Path]::DirectorySeparatorChar
$esep = [regex]::Escape($sep)

$script:hardErrors = 0
$script:warnCount  = 0
function Write-Problem([string]$m) { $script:hardErrors++; Write-Host "  [GAGAL] $m" -ForegroundColor Red }
function Write-Warn2([string]$m)   { $script:warnCount++;  Write-Host "  [PERINGATAN] $m" -ForegroundColor Yellow }
function To-Rel([string]$full) { $full.Substring($repoRoot.Length).TrimStart($sep).Replace($sep, '/') }

# ---------- 1. Kumpulkan elemen Include/Remove dari semua csproj ----------
$rxInclude = '<(?:None|Content|Resource|EmbeddedResource) [^>]*?Include="([^"]+)"'
$rxRemove  = '<(?:None|Content|Resource|EmbeddedResource) [^>]*?Remove="([^"]+)"'

$includeLiterals = @()   # path absolut hasil gabungan folder csproj + nilai Include
$includeGlobs    = @()   # @{"Base"=folder csproj; "Glob"=pola}
$removeLiterals  = @()

$csprojFiles = @(Get-ChildItem -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch "$esep(bin|obj)$esep" })
if ($csprojFiles.Count -eq 0) { Write-Host 'Tidak ada .csproj ditemukan' -ForegroundColor Red; exit 1 }

foreach ($proj in $csprojFiles) {
    $text = Get-Content -LiteralPath $proj.FullName -Raw
    $base = $proj.DirectoryName
    foreach ($m in [regex]::Matches($text, $rxInclude)) {
        $v = $m.Groups[1].Value
        if ($v.Contains('*')) { $includeGlobs += @{ Base = $base; Glob = $v } }
        else { $includeLiterals += [IO.Path]::Combine($base, $v) }
    }
    foreach ($m in [regex]::Matches($text, $rxRemove)) {
        $v = $m.Groups[1].Value
        if (-not $v.Contains('*')) { $removeLiterals += [IO.Path]::Combine($base, $v) }
    }
}

# ---------- 2. Ekspansi glob ala MSBuild ----------
function Expand-Glob([string]$baseDir, [string]$glob) {
    # Mendukung pola yang dipakai csproj: "dir{sep}**", "dir{sep}**{sep}*.ext", "dir{sep}*.ext"
    $norm = $glob.Replace('/', $sep)
    $full = if ([IO.Path]::IsPathRooted($norm)) { $norm } else { [IO.Path]::Combine($baseDir, $norm) }

    $idx = $norm.IndexOf('*')
    if ($idx -ge 0 -and $norm.Substring($idx).StartsWith('**')) {
        # Rekursif
        $anchor = $norm.Substring(0, $idx).TrimEnd($sep)
        $anchorFull = if ($anchor) { [IO.Path]::GetFullPath([IO.Path]::Combine($baseDir, $anchor)) } else { [IO.Path]::GetFullPath($baseDir) }
        if (-not (Test-Path -LiteralPath $anchorFull -PathType Container)) { return @() }
        $leaf = $norm.Substring($idx + 2).TrimStart($sep)
        if (-not $leaf) { $leaf = '*' }
        $pattern = Join-Path $anchorFull $leaf
        return @(Get-ChildItem -LiteralPath $anchorFull -Recurse -File | Where-Object { $_.FullName -like $pattern } | ForEach-Object { $_.FullName })
    }

    if ($idx -ge 0) {
        # Wildcard tunggal di leaf
        $dir  = [IO.Path]::GetFullPath((Split-Path -Parent $full))
        $leaf = Split-Path -Leaf $full
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return @() }
        return @(Get-ChildItem -LiteralPath $dir -File -Filter $leaf | ForEach-Object { $_.FullName })
    }

    return @($full)
}

Write-Host ''
Write-Host '== Pemeriksaan resource SuDesApp ==' -ForegroundColor Cyan
Write-Host ("csproj diperiksa: " + $csprojFiles.Count)

# ---------- 3. Literal Include harus ada di disk ----------
$resolved = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$missing  = 0
foreach ($p in $includeLiterals) {
    if (Test-Path -LiteralPath $p -PathType Leaf) {
        [void]$resolved.Add((Resolve-Path -LiteralPath $p).Path)
    } else {
        $missing++
        Write-Problem ("MISSING - dirujuk csproj tapi tidak ada di disk: " + (To-Rel $p))
    }
}
Write-Host ("  literal Include: " + $includeLiterals.Count + " diperiksa, hilang: " + $missing)

# ---------- 4. Glob harus mencocokkan minimal satu file ----------
$deadGlob = 0
foreach ($g in $includeGlobs) {
    $files = Expand-Glob -baseDir $g.Base -glob $g.Glob
    if ($files.Count -eq 0) {
        $deadGlob++
        Write-Problem ("DEADGLOB - glob tidak mencocokkan file apa pun: " + $g.Glob)
    } else {
        foreach ($f in $files) { [void]$resolved.Add($f) }
    }
}
Write-Host ("  glob Include: " + $includeGlobs.Count + " diperiksa, mati: " + $deadGlob)

# ---------- 5. Remove literal ke file yang tidak ada = peringatan ----------
foreach ($p in $removeLiterals) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {
        Write-Warn2 ("REMOVE-MISSING - <None Remove> menunjuk file yang tidak ada: " + (To-Rel $p))
    }
}

# ---------- 6. File resource di disk yang tak dirujuk (orphan) ----------
# Catatan: folder Scripts dihapus beserta menu Alat Perbaikan — seluruh skrip eksternal tak dipakai lagi.
$scopeDirs = @('Resources', 'Templates', 'Configuration', 'Data') |
    ForEach-Object { Join-Path $repoRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ }
$diskFiles = @($scopeDirs | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File } | ForEach-Object { $_.FullName })

$ignorePatterns = @()
$ignoreFile = Join-Path $repoRoot 'Tools/resource-check-ignore.txt'
if (Test-Path -LiteralPath $ignoreFile) {
    $ignorePatterns = @(Get-Content -LiteralPath $ignoreFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

$sourceFiles = @(Get-ChildItem -Path $repoRoot -Recurse -File -Include *.cs, *.xaml, *.json, *.ps1, *.py, *.csproj |
    Where-Object { $_.FullName -notmatch "$esep(bin|obj)$esep" })
$sourcePaths = @($sourceFiles | ForEach-Object { $_.FullName })

$orphans = 0
foreach ($f in $diskFiles) {
    $rel = To-Rel $f
    $ignored = $false
    foreach ($pat in $ignorePatterns) { if ($rel -like $pat) { $ignored = $true; break } }
    if ($ignored) { continue }
    if ($resolved.Contains($f)) { continue }

    $base = [IO.Path]::GetFileName($f)
    $hit = Select-String -Path $sourcePaths -Pattern $base -SimpleMatch -List -ErrorAction SilentlyContinue
    if ($hit) { continue }

    $orphans++
    Write-Warn2 ("ORPHAN - tidak dirujuk csproj maupun kode: " + $rel)
}
Write-Host ("  file disk di scope: " + $diskFiles.Count + ", orphan: " + $orphans + ", pola whitelist: " + $ignorePatterns.Count)

# ---------- Ringkasan ----------
Write-Host ''
Write-Host ("Ringkasan: " + $script:hardErrors + " error keras, " + $script:warnCount + " peringatan")
if ($script:hardErrors -gt 0) { Write-Host 'HASIL: GAGAL' -ForegroundColor Red; exit 1 }
if ($FailOnOrphan -and $script:warnCount -gt 0) { Write-Host 'HASIL: GAGAL (FailOnOrphan)' -ForegroundColor Red; exit 1 }
Write-Host 'HASIL: LOLOS' -ForegroundColor Green
exit 0
