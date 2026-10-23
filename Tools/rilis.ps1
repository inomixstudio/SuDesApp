<#
.SYNOPSIS
    Menaikkan versi SuDesApp dan menerbitkan rilis lewat git (alur "Cara C").

.DESCRIPTION
    Menjalankan urutan rilis di PANDUAN-GITHUB-ACTIONS.md:

      1. memeriksa prasyarat: dotnet, cabang rilis (default main; ganti dengan
         -Cabang bila rilis dibuat dari cabang kerja), working tree bersih, versi
         lebih tinggi dari versi sekarang, dan tag belum dipakai;
      2. menaikkan AssemblyVersion + FileVersion di SuDesApp.Wpf/SuDesApp.Wpf.csproj
         (sumber versi aplikasi) sekaligus menyisipkan entri Catatan Rilis baru
         untuk versi itu - kartu terbaru
         di halaman Catatan Rilis sama persis dengan versi csproj;
      3. menjalankan gerbang verifikasi: dotnet build solusi - gagal berarti
         berhenti tanpa commit dan kedua berkas dikembalikan ke isi semula;
      4. commit chore(release) berisi perubahan versi + catatan rilis;
      5. membuat tag anotasi vX.Y.Z;
      6. mendorong commit ke origin/main lalu mendorong tag vX.Y.Z.

    Tag yang didorong memicu .github/workflows/release.yml (publish win-x64 +
    installer). Bila Actions belum bisa berjalan, aset rilis tetap bisa dibuat
    lokal: Tools/publish-release.ps1 -Version X.Y.Z

.PARAMETER Versi
    Versi baru format x.y.z (mis. 2.5.5). Awalan "v" boleh ikut ditulis.

.PARAMETER Fitur
    Isi bagian "Fitur Baru" pada entri catatan rilis versi baru. Satu parameter
    boleh memuat beberapa butir, dipisah koma:
        -Fitur "API Desa membuka endpoint baru", "Tema keempat ditambahkan"

.PARAMETER Peningkatan
    Isi bagian "Peningkatan" pada entri catatan rilis versi baru.

.PARAMETER Perbaikan
    Isi bagian "Perbaikan Bug" pada entri catatan rilis versi baru.

.PARAMETER CatatanBerkas
    Berkas teks berisi butir catatan rilis - lebih nyaman untuk catatan panjang
    atau bila skrip dipanggil dari luar PowerShell. Format:

        # Fitur Baru
        Butir pertama.
        Butir kedua.
        # Peningkatan
        Butir.

    Baris yang diawali "#" membuka bagian (kata "fitur", "peningkatan", dan
    "perbaikan"/"bug" dikenali bebas); baris lain menjadi butir. Tanda "-" atau
    "*" di awal baris boleh dipakai sebagai penanda butir dan ikut dibuang.
    Butir yang ditulis sebelum bagian pertama dianggap "Peningkatan".

.PARAMETER Cabang
    Cabang tempat rilis dibuat (default "main"). Bila rilis dibuat dari cabang
    kerja - mis. upgrade-dotnet-10 - sebutkan di sini supaya gerbang cabang
    memeriksa cabang yang benar:
        ./Tools/rilis.ps1 2.5.6 -Cabang upgrade-dotnet-10 -Peningkatan "..."

.PARAMETER UjiCoba
    Latihan aman: semua diperiksa dan gerbang build tetap dijalankan, tetapi tidak
    ada commit, tag, atau push - kedua berkas dikembalikan ke isi semula.
    Working tree boleh kotor karena tidak ada yang dikomit.

.PARAMETER TanpaDorong
    Commit dan tag tetap dibuat lokal, tetapi tidak didorong ke origin.

.EXAMPLE
    ./Tools/rilis.ps1 2.5.5 -Peningkatan "Tabel Data Warga memakai tinggi baris pilihan operator."

.EXAMPLE
    ./Tools/rilis.ps1 -Versi 2.5.5 -Fitur "API Desa: endpoint ringkasan baru." -Perbaikan "Kontras tema Pink diperbaiki." -UjiCoba

.EXAMPLE
    ./Tools/rilis.ps1 2.5.5 -CatatanBerkas artifacts/catatan-rilis.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Versi,

    [string[]]$Fitur = @(),
    [string[]]$Peningkatan = @(),
    [string[]]$Perbaikan = @(),
    [string]$CatatanBerkas = '',

    # Cabang tempat rilis dibuat (default main). Bila rilis dibuat dari cabang
    # kerja (mis. upgrade-dotnet-10), sebutkan di sini - gerbang cabang akan
    # memeriksa cabang itu, bukan main.
    [string]$Cabang = 'main',

    [switch]$UjiCoba,

    [switch]$TanpaDorong
)

$ErrorActionPreference = 'Stop'

$script:repoRoot   = Split-Path -Parent $PSScriptRoot
$script:csprojWpf  = Join-Path $script:repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj'
$script:catatanVm  = Join-Path $script:repoRoot 'SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs'
$script:sln        = Join-Path $script:repoRoot 'SuDesApp.slnx'
$script:cabang     = $Cabang.Trim()
if (-not $script:cabang) { $script:cabang = 'main' }

Set-Location $script:repoRoot

function Tahap  { param([string]$Teks) Write-Host ''; Write-Host ('== ' + $Teks + ' ==') -ForegroundColor Cyan }
function Info   { param([string]$Teks) Write-Host ('   ' + $Teks) }
function Sukses { param([string]$Teks) Write-Host ('   ' + $Teks) -ForegroundColor Green }
function Ingat  { param([string]$Teks) Write-Host ('   ' + $Teks) -ForegroundColor Yellow }

# -c safe.directory: git di Windows menolak repo milik akun/drive lain; flag ini
# hanya berlaku untuk pemanggilan ini, tidak mengubah konfigurasi git pengguna.
function JalankanGit {
    param([string[]]$Arg)
    & git -c ('safe.directory=' + $script:repoRoot) @Arg
    if ($LASTEXITCODE -ne 0) {
        throw ('git ' + ($Arg -join ' ') + ' gagal (exit ' + $LASTEXITCODE + ').')
    }
}

function GitTenang {
    param([string[]]$Arg)
    $keluaran = & git -c ('safe.directory=' + $script:repoRoot) @Arg 2>$null
    return [pscustomobject]@{
        Keluaran = (($keluaran | Out-String).Trim())
        Kode     = $LASTEXITCODE
    }
}

# Membaca/menulis dengan encoding yang sama seperti berkas aslinya: PowerShell 5.1
# membaca berkas UTF-8 tanpa BOM sebagai ANSI, sehingga karakter seperti (c) dan
# tanda pisah panjang ikut rusak bila berkas ditulis ulang lewat Get-Content.
function BacaBerkas {
    param([string]$Jalur)
    $bytes = [System.IO.File]::ReadAllBytes($Jalur)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    return [pscustomobject]@{
        Teks = [System.IO.File]::ReadAllText($Jalur)
        Bom  = $bom
    }
}

function TulisBerkas {
    param([string]$Jalur, [string]$Teks, [bool]$Bom)
    $encoder = New-Object System.Text.UTF8Encoding($Bom)
    [System.IO.File]::WriteAllText($Jalur, $Teks, $encoder)
}

# Membaca berkas catatan rilis berbutir menjadi tiga daftar butir.
function BacaCatatanBerkas {
    param([string]$Jalur)
    $isi = (BacaBerkas $Jalur).Teks
    $fitur = @()
    $peningkatan = @()
    $perbaikan = @()
    $sasaran = 'peningkatan'

    foreach ($baris in ($isi -split "\r?\n")) {
        $teks = $baris.Trim()
        if ($teks.Length -eq 0) { continue }

        if ($teks.StartsWith('#')) {
            $judul = $teks.TrimStart('#').Trim().ToLowerInvariant()
            if ($judul.Contains('fitur'))          { $sasaran = 'fitur' }
            elseif ($judul.Contains('perbaik') -or $judul.Contains('bug')) { $sasaran = 'perbaikan' }
            elseif ($judul.Contains('peningkat'))  { $sasaran = 'peningkatan' }
            else {
                $sasaran = 'peningkatan'
                Ingat ('Bagian "' + $teks + '" tidak dikenali - butirnya masuk bagian Peningkatan.')
            }
            continue
        }

        if ($teks.StartsWith('-') -or $teks.StartsWith('*')) { $teks = $teks.Substring(1).Trim() }
        if ($teks.Length -eq 0) { continue }

        if ($sasaran -eq 'fitur')          { $fitur += $teks }
        elseif ($sasaran -eq 'perbaikan')  { $perbaikan += $teks }
        else                               { $peningkatan += $teks }
    }

    return [pscustomobject]@{
        Fitur       = $fitur
        Peningkatan = $peningkatan
        Perbaikan   = $perbaikan
    }
}

function KembalikanBerkas {
    if ($script:isiCsproj) {
        TulisBerkas -Jalur $script:csprojWpf -Teks $script:isiCsproj -Bom $script:bomCsproj
        TulisBerkas -Jalur $script:catatanVm -Teks $script:isiCatatan -Bom $script:bomCatatan
        Ingat ('Berkas versi + catatan rilis dikembalikan ke isi semula (' + $script:versiSekarang + ').')
    }
}

function TeksPunyaCSharp {
    param([string]$Teks)
    $t = $Teks -replace '\\', '\\'
    $t = $t -replace '"', '\"'
    $t = $t -replace "\r?\n", ' '
    return $t.Trim()
}

# Menambahkan satu sub-bagian ("Fitur Baru"/"Peningkatan"/"Perbaikan Bug") ke daftar baris entri.
function TambahBagian {
    param(
        [System.Collections.Generic.List[string]]$Ke,
        [string]$Judul,
        [string[]]$Butir,
        [bool]$Koma
    )
    $Ke.Add($script:i24 + 'new CatatanRilisSubSection("' + (TeksPunyaCSharp $Judul) + '", new List<string>')
    $Ke.Add($script:i24 + '{')
    for ($i = 0; $i -lt $Butir.Count; $i++) {
        $tanda = ','
        if ($i -eq $Butir.Count - 1) { $tanda = '' }
        $Ke.Add($script:i28 + '"' + (TeksPunyaCSharp $Butir[$i]) + '"' + $tanda)
    }
    $penutup = '})'
    if ($Koma) { $penutup = '}),' }
    $Ke.Add($script:i24 + $penutup)
}

Tahap 'Tahap 1/7 - prasyarat'
$Versi = $Versi.Trim().TrimStart('v', 'V')
if ($Versi -notmatch '^\d+\.\d+\.\d+$') {
    throw ("Versi tidak valid: '" + $Versi + "' (harus x.y.z, mis. 2.5.5).")
}
$versiAssembly = $Versi + '.0'
$tagRilis      = 'v' + $Versi

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet tidak ditemukan di PATH - pasang .NET 10 SDK dulu.'
}
if (-not (Test-Path -LiteralPath $script:csprojWpf)) {
    throw ('Berkas versi tidak ditemukan: ' + $script:csprojWpf)
}
if (-not (Test-Path -LiteralPath $script:catatanVm)) {
    throw ('Berkas catatan rilis tidak ditemukan: ' + $script:catatanVm)
}
if (-not (Test-Path -LiteralPath $script:sln)) {
    throw ('Solusi tidak ditemukan: ' + $script:sln)
}

$cekRepo = GitTenang -Arg @('rev-parse', '--is-inside-work-tree')
if ($cekRepo.Kode -ne 0 -or $cekRepo.Keluaran -ne 'true') {
    throw 'Folder ini bukan repositori git yang dikenali.'
}

$cabangSekarang = (GitTenang -Arg @('rev-parse', '--abbrev-ref', 'HEAD')).Keluaran
if ($cabangSekarang -ne $script:cabang) {
    throw ("Skrip rilis harus dijalankan di cabang '" + $script:cabang + "' (sekarang '" + $cabangSekarang + "'). Pindah cabang dulu, atau sebutkan cabang yang benar lewat -Cabang.")
}

$kotor = (GitTenang -Arg @('status', '--porcelain')).Keluaran
if ($kotor.Length -gt 0) {
    if ($UjiCoba) {
        Ingat 'Working tree kotor - diabaikan karena mode -UjiCoba (tidak ada yang dikomit).'
        Info $kotor
    } else {
        $pesan = 'Working tree tidak bersih - bereskan dulu (commit atau simpan perubahan):'
        foreach ($baris in ($kotor -split "`n")) {
            if ($baris.Trim().Length -gt 0) {
                $pesan += [Environment]::NewLine + '     ' + $baris.TrimEnd()
            }
        }
        throw $pesan
    }
}

$tagLokal = GitTenang -Arg @('rev-parse', '--verify', '--quiet', ('refs/tags/' + $tagRilis))
if ($tagLokal.Kode -eq 0) {
    throw ('Tag ' + $tagRilis + ' sudah ada di repo lokal - pilih nomor versi lain atau hapus tag lama.')
}
if (-not $TanpaDorong) {
    $tagRemote = GitTenang -Arg @('ls-remote', '--tags', 'origin', $tagRilis)
    if ($tagRemote.Kode -ne 0) {
        Ingat 'Tidak bisa memeriksa tag di origin (jaringan?) - push nanti akan menolak bila tag sudah ada.'
    } elseif ($tagRemote.Keluaran.Length -gt 0) {
        throw ('Tag ' + $tagRilis + ' sudah ada di origin - pilih nomor versi lain.')
    }
}

# ---------- baca isi asli, hitung perubahan (gagal = tidak ada berkas yang disentuh) ----------

$berkasCsproj = BacaBerkas $script:csprojWpf
$script:bomCsproj = $berkasCsproj.Bom
$script:isiCsproj = $berkasCsproj.Teks

if ($script:isiCsproj -notmatch '<AssemblyVersion>([^<]+)</AssemblyVersion>') {
    throw ('<AssemblyVersion> tidak ditemukan di ' + $script:csprojWpf + '.')
}
$script:versiSekarang = $Matches[1]
$bagianVersi = $script:versiSekarang.Split('.')
$versiSekarangSingkat = ($bagianVersi[0..2] -join '.')
if ($bagianVersi.Length -ge 4 -and $bagianVersi[3] -ne '0') {
    $versiSekarangSingkat = ($bagianVersi -join '.')
}

if (-not $UjiCoba) {
    if ([version]$versiAssembly -le [version]$script:versiSekarang) {
        throw ('Versi baru ' + $versiAssembly + ' harus lebih tinggi dari versi sekarang ' + $script:versiSekarang + '.')
    }
}

$berkasCatatan = BacaBerkas $script:catatanVm
$script:bomCatatan = $berkasCatatan.Bom
$script:isiCatatan = $berkasCatatan.Teks

$polaEntriTerbaru = [regex]::new('new CatatanRilisEntry\(\s*"Versi (?<versi>[\d.]+) \((?<tanggal>[^"]+)\)",\s*true,')
$cocokTerbaru = $polaEntriTerbaru.Match($script:isiCatatan)
if (-not $cocokTerbaru.Success) {
    throw ('Entri catatan rilis terbaru (IsLatest = true) tidak ditemukan di ' + $script:catatanVm + '.')
}
if ($cocokTerbaru.Groups['versi'].Value -ne $versiSekarangSingkat) {
    throw ('Entri catatan rilis terbaru menyebut versi ' + $cocokTerbaru.Groups['versi'].Value +
           ' padahal csproj memakai ' + $versiSekarangSingkat + ' - rapikan dulu kartu versi terbarunya.')
}

if ($CatatanBerkas) {
    if ($Fitur.Count + $Peningkatan.Count + $Perbaikan.Count -gt 0) {
        throw 'Pilih salah satu cara mengisi catatan rilis: -CatatanBerkas atau -Fitur/-Peningkatan/-Perbaikan.'
    }
    if (-not (Test-Path -LiteralPath $CatatanBerkas)) {
        throw ('Berkas catatan rilis tidak ditemukan: ' + $CatatanBerkas)
    }
    $hasilCatatan = BacaCatatanBerkas $CatatanBerkas
    $Fitur       = @($hasilCatatan.Fitur)
    $Peningkatan = @($hasilCatatan.Peningkatan)
    $Perbaikan   = @($hasilCatatan.Perbaikan)
    if ($Fitur.Count + $Peningkatan.Count + $Perbaikan.Count -eq 0) {
        throw ('Berkas catatan rilis ' + $CatatanBerkas + ' tidak memuat butir apa pun.')
    }
    Info ('Catatan rilis dibaca dari: ' + $CatatanBerkas)
}

if ($Fitur)       { $Fitur       = @($Fitur       | Where-Object { $_.Trim().Length -gt 0 }) }
if ($Peningkatan) { $Peningkatan = @($Peningkatan | Where-Object { $_.Trim().Length -gt 0 }) }
if ($Perbaikan)   { $Perbaikan   = @($Perbaikan   | Where-Object { $_.Trim().Length -gt 0 }) }
if ($Fitur.Count + $Peningkatan.Count + $Perbaikan.Count -eq 0) {
    Ingat 'Tidak ada -Fitur/-Peningkatan/-Perbaikan: entri catatan rilis diisi butir pemeliharaan umum.'
    $Peningkatan = @('Pemeliharaan rilis: pembaruan versi dan perbaikan internal.')
}

$namaBulan = @('Januari', 'Februari', 'Maret', 'April', 'Mei', 'Juni', 'Juli', 'Agustus', 'September', 'Oktober', 'November', 'Desember')
$sekarang = Get-Date
$tanggalRilis = $sekarang.Day.ToString() + ' ' + $namaBulan[$sekarang.Month - 1] + ' ' + $sekarang.Year.ToString()
$judulEntri = 'Versi ' + $Versi + ' (' + $tanggalRilis + ')'

Info ('Versi sekarang : ' + $script:versiSekarang)
Info ('Versi baru     : ' + $versiAssembly + '   (tag ' + $tagRilis + ')')
Info ('Catatan rilis  : ' + $judulEntri)

Info ('Butir catatan : ' + $Fitur.Count + ' fitur, ' + $Peningkatan.Count + ' peningkatan, ' + $Perbaikan.Count + ' perbaikan')

# ---------- Tahap 2: tulis versi + entri catatan rilis ----------

Tahap 'Tahap 2/7 - naikkan versi dan sisipkan entri catatan rilis'

$isiCsprojBaru = $script:isiCsproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', ('<AssemblyVersion>' + $versiAssembly + '</AssemblyVersion>')
$isiCsprojBaru = $isiCsprojBaru -replace '<FileVersion>[^<]+</FileVersion>', ('<FileVersion>' + $versiAssembly + '</FileVersion>')
if ($isiCsprojBaru -eq $script:isiCsproj) {
    throw 'Baris AssemblyVersion/FileVersion tidak berubah - periksa berkas versi.'
}

$nl = "`n"
if ($script:isiCatatan.Contains("`r`n")) { $nl = "`r`n" }

$script:i16 = '                '
$script:i20 = '                    '
$script:i24 = '                        '
$script:i28 = '                            '

$sisaBagian = 0
if ($Fitur.Count -gt 0)       { $sisaBagian++ }
if ($Peningkatan.Count -gt 0) { $sisaBagian++ }
if ($Perbaikan.Count -gt 0)   { $sisaBagian++ }

$barisEntri = New-Object System.Collections.Generic.List[string]
# Baris pertama tanpa indentasi: titik sisip sudah berada setelah 16 spasi milik entri lama.
$barisEntri.Add('new CatatanRilisEntry(')
$barisEntri.Add($script:i20 + '"' + $judulEntri + '",')
$barisEntri.Add($script:i20 + 'true,')
$barisEntri.Add($script:i20 + 'new List<CatatanRilisSubSection>')
$barisEntri.Add($script:i20 + '{')
if ($Fitur.Count -gt 0)       { $sisaBagian--; TambahBagian -Ke $barisEntri -Judul 'Fitur Baru'   -Butir $Fitur       -Koma ($sisaBagian -gt 0) }
if ($Peningkatan.Count -gt 0) { $sisaBagian--; TambahBagian -Ke $barisEntri -Judul 'Peningkatan'   -Butir $Peningkatan -Koma ($sisaBagian -gt 0) }
if ($Perbaikan.Count -gt 0)   { $sisaBagian--; TambahBagian -Ke $barisEntri -Judul 'Perbaikan Bug' -Butir $Perbaikan   -Koma ($sisaBagian -gt 0) }
$barisEntri.Add($script:i20 + '}),')
$blokEntri = ($barisEntri -join $nl)

# Kartu lama berhenti menjadi "terbaru", lalu entri baru disisipkan di atasnya.
$blokLama = $cocokTerbaru.Value -replace 'true,', 'false,'
$isiCatatanBaru = $script:isiCatatan.Substring(0, $cocokTerbaru.Index) + $blokLama +
                  $script:isiCatatan.Substring($cocokTerbaru.Index + $cocokTerbaru.Length)
$awalEntri = $isiCatatanBaru.IndexOf('new CatatanRilisEntry(')
if ($awalEntri -lt 0) { throw 'Titik sisip entri catatan rilis tidak ditemukan.' }
$isiCatatanBaru = $isiCatatanBaru.Insert($awalEntri, $blokEntri + $nl + $script:i16)

TulisBerkas -Jalur $script:csprojWpf -Teks $isiCsprojBaru -Bom $script:bomCsproj
Sukses ('AssemblyVersion/FileVersion -> ' + $versiAssembly)
TulisBerkas -Jalur $script:catatanVm -Teks $isiCatatanBaru -Bom $script:bomCatatan
Sukses ('Entri catatan rilis "' + $judulEntri + '" disisipkan; kartu versi sebelumnya tidak lagi ditandai terbaru.')

# ---------- Tahap 3: gerbang build ----------

Tahap 'Tahap 3/7 - gerbang build (dotnet build SuDesApp.slnx)'
& dotnet build $script:sln --nologo -v q
if ($LASTEXITCODE -ne 0) {
    KembalikanBerkas
    throw ('Build gagal (exit ' + $LASTEXITCODE + ') - tidak ada commit dibuat.')
}
Sukses 'Build bersih.'

if ($UjiCoba) {
    Tahap 'Tahap 5/7..7/7 - dilewati (mode -UjiCoba)'
    KembalikanBerkas
    Info 'Yang akan dijalankan pada rilis sungguhan:'
    Info 'git add SuDesApp.Wpf/SuDesApp.Wpf.csproj SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs'
    Info ('git commit -m "chore(release): naikkan versi aplikasi ke ' + $Versi + '" ...')
    Info ('git tag -a ' + $tagRilis + ' -m "SuDesApp ' + $Versi + '"')
    Info ('git push origin ' + $script:cabang)
    Info ('git push origin ' + $tagRilis)
    Tahap 'Selesai - uji coba'
    Sukses 'Prasyarat dan kedua gerbang lolos. Jalankan tanpa -UjiCoba untuk menerbitkan rilis.'
    return
}

$judul   = 'chore(release): naikkan versi aplikasi ke ' + $Versi
$badan   = 'AssemblyVersion/FileVersion dinaikkan dan entri "Catatan Rilis" versi baru disisipkan.' + [Environment]::NewLine +
           'Keduanya dijaga tetap konsisten satu sama lain.'

Tahap 'Tahap 5/7 - commit rilis'
JalankanGit -Arg @('add', 'SuDesApp.Wpf/SuDesApp.Wpf.csproj', 'SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs')
JalankanGit -Arg @('commit', '-m', $judul, '-m', $badan)
Sukses ('Commit dibuat: ' + $judul)

Tahap 'Tahap 6/7 - tag rilis'
JalankanGit -Arg @('tag', '-a', $tagRilis, '-m', ('SuDesApp ' + $Versi))
Sukses ('Tag anotasi ' + $tagRilis + ' dibuat.')

Tahap 'Tahap 7/7 - dorong ke origin'
if ($TanpaDorong) {
    Ingat 'TanpaDorong: commit dan tag tinggal di komputer ini. Dorong manual dengan:'
    Info ('git push origin ' + $script:cabang)
    Info ('git push origin ' + $tagRilis)
} else {
    try {
        JalankanGit -Arg @('push', 'origin', $script:cabang)
        Sukses ('Commit terkirim ke origin/' + $script:cabang + '.')
    } catch {
        Ingat 'Push commit gagal. Commit dan tag sudah terbentuk lokal; setelah masalah beres:'
        Info ('git push origin ' + $script:cabang)
        Info ('git push origin ' + $tagRilis)
        Ingat ('Untuk membatalkan commit lokal: git reset --soft HEAD~1   lalu   git tag -d ' + $tagRilis)
        throw
    }
    try {
        JalankanGit -Arg @('push', 'origin', $tagRilis)
        Sukses ('Tag ' + $tagRilis + ' terkirim - workflow release.yml berjalan di GitHub Actions.')
    } catch {
        Ingat 'Push tag gagal. Ulangi dorongan tag dengan:'
        Info ('git push origin ' + $tagRilis)
        throw
    }
}

Tahap 'Selesai'
Info ('Rilis ' + $Versi + ' terkirim. Pantau workflow di https://github.com/inomixstudio/SudesApp/actions')
Info ('Bila Actions belum bisa berjalan, buat aset rilis lokal: ./Tools/publish-release.ps1 -Version ' + $Versi)
