<#
.SYNOPSIS
    Pemeriksa rilis SuDesApp - satu perintah untuk seluruh invarian yang
    selama ini diverifikasi manual sebelum/di sela-sela penerbitan rilis.

.DESCRIPTION
    Lima tahap, berurutan dari yang paling murah ke yang paling mahal:

      1. Parse - seluruh berkas Tools/*.ps1 wajib lolos parse PowerShell.
      2. Build - dotnet build SuDesApp.slnx wajib 0 warning / 0 error.
      3. Skema & benih SQL - desa.db.sql tidak boleh menyuntik warga contoh;
         database segar wajib berisi 0 warga dan >= 10 jenis surat; pembersih
         contoh (DatabaseInitializer.BersihkanWargaContohAsync) wajib
         menghapus tepat baris contoh dan menyisakan data pengguna. Uji
         perilaku memakai Python (py/python/python3) - bila tidak ada,
         tahap perilaku dilewati dan pemeriksaan teks tetap berjalan.
      4. Pengemasan contoh - SuDesApp.Wpf.csproj wajib memuat glob
         ..\Templates\** dan ..\Bawaan\**; folder Bawaan/TemplateSurat wajib
         berisi JSON contoh yang SEGAR dengan katalog C# (diperiksa lewat
         Tools/BawaanGen); jumlah berkas Templates dilaporkan (ambang bisa
         dipasang lewat -MinimalTemplate); ketidakcocokan dengan isi
         artifacts/publish diperingatkan (folder publish bisa lebih tua
         dari working tree).
      5. Aset - SHA-256 artefak lokal (zip, patch.json, installer) wajib
         cocok dengan yang tercatat di artifacts/RELEASE.md; bila rilis sudah
         terbit di GitHub, digest aset di GitHub wajib cocok dengan berkas
         lokal. Bisa dilewati offline dengan -TanpaGitHub.

    Keluar dengan kode 0 hanya bila tidak ada GAGAL; LEWATI tidak
    menggagalkan (dicatat di ringkasan).

.PARAMETER MinimalTemplate
    Ambang minimum berkas PDF di folder Templates. Default 0 = hanya
    melaporkan; folder Templates kosong dilewati (bukan gagal), karena
    pendekatan contoh template bisa berubah.

.PARAMETER TanpaGitHub
    Lewati bagian GitHub pada tahap 5 (mode offline). Pemeriksaan lokal
    (RELEASE.md + SHA-256 berkas) tetap berjalan.

.PARAMETER Versi
    Versi rilis yang diharapkan (mis. 2.5.5). Tanpa parameter ini, versi
    dideteksi dari patch.json, nama zip, dan RELEASE.md - ketiganya wajib
    konsisten satu sama lain dan dengan AssemblyVersion di csproj.

.PARAMETER Repo
    Repositori GitHub pembanding. Default inomixstudio/SuDesApp.

.EXAMPLE
    powershell -File Tools/check-rilis.ps1

.EXAMPLE
    powershell -File Tools/check-rilis.ps1 -MinimalTemplate 8 -TanpaGitHub
#>
[CmdletBinding()]
param(
    [int]$MinimalTemplate = 0,
    [switch]$TanpaGitHub,
    [string]$Versi,
    [string]$Repo = 'inomixstudio/SuDesApp'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$script:jumlahLulus = 0
$script:jumlahGagal = 0
$script:jumlahLewat = 0

function Tahap       { param([string]$Judul) Write-Host ''; Write-Host ('== ' + $Judul + ' ==') -ForegroundColor Cyan }
function Info        { param([string]$Teks) Write-Host ('   ' + $Teks) }
function OK          { param([string]$Teks) $script:jumlahLulus++; Write-Host ('   [OK] ' + $Teks) -ForegroundColor Green }
function Gagal       { param([string]$Teks) $script:jumlahGagal++; Write-Host ('   [GAGAL] ' + $Teks) -ForegroundColor Red }
function Lewat       { param([string]$Teks) $script:jumlahLewat++; Write-Host ('   [LEWAT] ' + $Teks) -ForegroundColor Yellow }
function Peringatan  { param([string]$Teks) Write-Host ('   [PERINGATAN] ' + $Teks) -ForegroundColor Yellow }
function HitungSha256 { param([string]$Path) (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function CariPython {
    $kandidat = @(
        @{ Nama = 'py';      Arg = @('-3') },
        @{ Nama = 'python';  Arg = @() },
        @{ Nama = 'python3'; Arg = @() }
    )
    foreach ($k in $kandidat) {
        $cmd = Get-Command $k.Nama -ErrorAction SilentlyContinue
        if (-not $cmd) { continue }
        $src = $cmd.Source
        $argVersi = @($k.Arg) + @('--version')
        $v = ''
        try { $v = (& $src @argVersi | Out-String) } catch { continue }
        if ($v -match 'Python\s+\d+\.\d+') {
            return @{ Exe = $src; Arg = $k.Arg }
        }
    }
    return $null
}

# Skrip uji perilaku database (tahap 3). Pola DELETE diambil langsung dari
# DatabaseInitializer.cs sehingga pemeriksaan tidak meniru SQL dua kali.
$pySkrip = @'
import io, re, sqlite3, sys

sql_path, cs_path = sys.argv[1], sys.argv[2]


def gagal(pesan, kode):
    print("GAGAL: " + pesan)
    sys.exit(kode)


with io.open(sql_path, "r", encoding="utf-8-sig", errors="replace") as f:
    sql = f.read()
with io.open(cs_path, "r", encoding="utf-8-sig", errors="replace") as f:
    cs = f.read()

m = re.search(r"DELETE FROM Warga\s+WHERE NIK = '([^']*)'\s+AND Nama = '([^']*)'", cs)
if not m:
    gagal("pola DELETE pembersih tidak ditemukan di DatabaseInitializer.cs", 2)
nik, nama = m.group(1), m.group(2)
hapus_sql = m.group(0)

try:
    db = sqlite3.connect(":memory:")
    db.executescript(sql)
    n_warga = db.execute('SELECT COUNT(*) FROM "Warga"').fetchone()[0]
    n_jenis = db.execute('SELECT COUNT(*) FROM "JenisSurat"').fetchone()[0]
except Exception as exc:
    gagal("menjalankan desa.db.sql: %s" % exc, 3)

if n_warga != 0:
    gagal("database segar berisi %d warga, diharapkan 0" % n_warga, 4)
if n_jenis < 10:
    gagal("jenis surat hanya %d, diharapkan >= 10" % n_jenis, 5)
print("OK: database segar - 0 warga, %d jenis surat" % n_jenis)

try:
    db.execute('INSERT INTO "Warga" (NIK, Nama) VALUES (?, ?)', (nik, nama))
    db.execute('INSERT INTO "Warga" (NIK, Nama) VALUES (?, ?)', ("9999999999999999", nama))
    db.execute('INSERT INTO "Warga" (NIK, Nama) VALUES (?, ?)', ("8888888888888888", "WARGA PENGGUNA"))
    terhapus = db.execute(hapus_sql).rowcount
    sisa = db.execute('SELECT NIK, Nama FROM "Warga"').fetchall()
except Exception as exc:
    gagal("uji pembersih: %s" % exc, 6)

if terhapus != 1:
    gagal("pembersih menghapus %d baris, diharapkan tepat 1" % terhapus, 7)
if len(sisa) != 2:
    gagal("sisa warga %d, diharapkan 2 (data pengguna harus utuh): %r" % (len(sisa), sisa), 8)
for n, d in sisa:
    if n == nik:
        gagal("baris contoh NIK %s masih tersisa" % nik, 9)
print("OK: pembersih menghapus tepat 1 baris contoh; %d baris data pengguna utuh" % len(sisa))
'@

# ================= TAHAP 1/5: PARSE =================
Tahap 'TAHAP 1/5: Parse seluruh skrip Tools/*.ps1'
$berkasPs1 = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name)
if ($berkasPs1.Count -eq 0) {
    Gagal 'tidak ada berkas Tools/*.ps1'
} else {
    $lolos = 0
    foreach ($f in $berkasPs1) {
        $tokens = $null; $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
        if ($errors -and $errors.Count -gt 0) {
            $e = $errors[0]
            Gagal ('{0} baris {1}: {2}' -f $f.Name, $e.Extent.StartLineNumber, $e.Message)
        } else {
            $lolos++
        }
    }
    if ($lolos -eq $berkasPs1.Count) { OK ('{0} berkas lolos parse' -f $lolos) }
}

# ================= TAHAP 2/5: BUILD =================
Tahap 'TAHAP 2/5: Build solusi (0 warning / 0 error)'
$slnx = Join-Path $repoRoot 'SuDesApp.slnx'
if (-not (Test-Path -LiteralPath $slnx)) {
    Gagal 'SuDesApp.slnx tidak ditemukan'
} else {
    $keluaran = (& dotnet build SuDesApp.slnx --nologo -v minimal | Out-String)
    $kode = $LASTEXITCODE
    if ($kode -ne 0) {
        Gagal ('dotnet build gagal (exit {0})' -f $kode)
        foreach ($baris in (($keluaran -split "`r?`n") | Select-Object -Last 25)) {
            if ($baris.Trim()) { Write-Host ('        ' + $baris) -ForegroundColor Red }
        }
    } else {
        $jmlWarning = $null; $jmlError = $null
        if ($keluaran -match '(\d+)\s+Warning\(s\)') { $jmlWarning = [int]$Matches[1] }
        if ($keluaran -match '(\d+)\s+Error\(s\)')   { $jmlError = [int]$Matches[1] }
        if (($jmlWarning -gt 0) -or ($jmlError -gt 0)) {
            Gagal ('build bersih exit 0 tetapi ringkasan menunjuk {0} warning / {1} error' -f $jmlWarning, $jmlError)
        } else {
            if ($null -ne $jmlWarning) { OK ('build bersih: {0} warning, {1} error' -f $jmlWarning, $jmlError) }
            else { OK 'build bersih (exit 0; ringkasan MSBuild tak tercetak - TreatWarningsAsErrors menjadikan warning = error)' }
        }
    }
}

# ================= TAHAP 3/5: BENIH SQL & PERILAKU =================
Tahap 'TAHAP 3/5: Benih SQL & perilaku pembersih contoh'
$sqlPath = Join-Path $repoRoot 'desa.db.sql'
$csPath = Join-Path $repoRoot 'SuDesApp.Core\Configuration\DatabaseInitializer.cs'
$sqlAda = Test-Path -LiteralPath $sqlPath
$csAda = Test-Path -LiteralPath $csPath
if (-not $sqlAda) { Gagal 'desa.db.sql tidak ditemukan' }
if (-not $csAda)  { Gagal 'SuDesApp.Core/Configuration/DatabaseInitializer.cs tidak ditemukan' }

if ($sqlAda -and $csAda) {
    # 1) Teks: seed tidak boleh menyuntik warga contoh (baris komentar dikecualikan)
    $sqlTeks = Get-Content -LiteralPath $sqlPath -Raw
    $barisSuntik = @()
    $nomorBaris = 0
    foreach ($baris in ($sqlTeks -split "`r?`n")) {
        $nomorBaris++
        $b = $baris.TrimStart()
        if ($b.StartsWith('--')) { continue }
        if ($b -match '(?i)\bINSERT\b' -and $b -match '1234567890123456|NAMA ORANG TUA') {
            $barisSuntik += $nomorBaris
        }
    }
    if ($barisSuntik.Count -gt 0) {
        Gagal ('desa.db.sql masih menyuntik warga contoh pada baris ' + ($barisSuntik -join ', '))
    } else {
        OK 'desa.db.sql tidak menyuntik warga contoh'
    }

    # 2) Teks: pembersih contoh ada dan dipanggil saat startup
    $csTeks = Get-Content -LiteralPath $csPath -Raw
    if ($csTeks -notmatch 'BersihkanWargaContohAsync') {
        Gagal 'BersihkanWargaContohAsync tidak ada di DatabaseInitializer.cs'
    } elseif ($csTeks -notmatch 'await BersihkanWargaContohAsync') {
        Gagal 'BersihkanWargaContohAsync tidak dipanggil saat startup'
    } else {
        OK 'pembersih contoh didefinisikan dan dipanggil saat startup'
    }

    # 3) Perilaku sqlite memakai Python; bila tak ada Python, dilewati
    $py = CariPython
    if (-not $py) {
        Lewat 'Python tidak tersedia - uji perilaku database dilewati (pemeriksaan teks tetap berjalan)'
    } else {
        $pyFile = Join-Path ([IO.Path]::GetTempPath()) ('sudes-cek-benih-' + [guid]::NewGuid().ToString('N') + '.py')
        try {
            Set-Content -LiteralPath $pyFile -Value $pySkrip -Encoding UTF8
            $exe = $py.Exe
            $argumen = @($py.Arg) + @($pyFile, $sqlPath, $csPath)
            $keluaranPy = (& $exe @argumen | Out-String)
            $kodePy = $LASTEXITCODE
            foreach ($barisPy in ($keluaranPy -split "`r?`n")) {
                $t = $barisPy.Trim()
                if (-not $t) { continue }
                if ($t.StartsWith('GAGAL')) { Write-Host ('        ' + $t) -ForegroundColor Red }
                else { Info $t }
            }
            if ($kodePy -ne 0) { Gagal ('uji perilaku sqlite gagal (exit {0})' -f $kodePy) }
            else { OK 'uji perilaku sqlite lulus' }
        } catch {
            Gagal ('gagal menjalankan uji Python: ' + $_.Exception.Message)
        } finally {
            Remove-Item -LiteralPath $pyFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# ================= TAHAP 4/5: PENGEMASAN CONTOH =================
Tahap 'TAHAP 4/5: Pengemasan contoh (Templates & Bawaan)'
$csprojPath = Join-Path $repoRoot 'SuDesApp.Wpf\SuDesApp.Wpf.csproj'
$csprojAda = Test-Path -LiteralPath $csprojPath
$csprojTeks = ''
if ($csprojAda) {
    $csprojTeks = Get-Content -LiteralPath $csprojPath -Raw
    if ($csprojTeks -match '\.\.+\\+Templates\\+\*\*') { OK 'glob ..\Templates\** ada di SuDesApp.Wpf.csproj' }
    else { Gagal 'glob ..\Templates\** hilang dari SuDesApp.Wpf.csproj' }
} else {
    Gagal 'SuDesApp.Wpf/SuDesApp.Wpf.csproj tidak ditemukan'
}

$tplDir = Join-Path $repoRoot 'Templates'
$tplKerja = @()
if (Test-Path -LiteralPath $tplDir) { $tplKerja = @(Get-ChildItem -LiteralPath $tplDir -Recurse -File -Filter '*.pdf') }
$jumlahKerja = $tplKerja.Count

$pubDir = Join-Path $repoRoot 'artifacts\publish\Templates'
$pubAda = Test-Path -LiteralPath $pubDir
$jumlahPub = 0
if ($pubAda) { $jumlahPub = @(Get-ChildItem -LiteralPath $pubDir -Recurse -File -Filter '*.pdf').Count }

$teksPub = 'tidak ada'
if ($pubAda) { $teksPub = [string]$jumlahPub }
Info ('folder Templates: {0} berkas PDF; artifacts/publish/Templates: {1}' -f $jumlahKerja, $teksPub)

if ($MinimalTemplate -gt 0) {
    if ($jumlahKerja -lt $MinimalTemplate) {
        Gagal ('jumlah template {0} < -MinimalTemplate {1}' -f $jumlahKerja, $MinimalTemplate)
    } else {
        OK ('jumlah template {0} >= ambang {1}' -f $jumlahKerja, $MinimalTemplate)
    }
} elseif ($jumlahKerja -eq 0) {
    Lewat 'folder Templates kosong - pemeriksaan jumlah template dilewati (pakai -MinimalTemplate bila perlu ambang)'
} else {
    OK ('jumlah template tercatat: {0} berkas PDF' -f $jumlahKerja)
}

if (-not $pubAda) {
    Info 'artifacts/publish/Templates tidak ada - pembanding isi publish dilewati (folder publish bisa lebih tua)'
} elseif (($jumlahPub -eq $jumlahKerja) -and ($jumlahKerja -gt 0)) {
    $namaKerja = @($tplKerja | ForEach-Object Name | Sort-Object)
    $namaPub = @(Get-ChildItem -LiteralPath $pubDir -Recurse -File -Filter '*.pdf' | ForEach-Object Name | Sort-Object)
    $selisih = @(Compare-Object -ReferenceObject $namaKerja -DifferenceObject $namaPub)
    if ($selisih.Count -eq 0) { OK 'isi artifacts/publish/Templates cocok dengan folder Templates' }
    else { Peringatan ('nama template publish berbeda dari working tree: ' + (($selisih | ForEach-Object { $_.SideIndicator + ' ' + $_.InputObject }) -join ', ')) }
} elseif ($jumlahKerja -eq 0) {
    Peringatan ('publish/Templates berisi {0} PDF sementara folder Templates kosong - folder publish lebih tua dari working tree' -f $jumlahPub)
} else {
    Peringatan ('jumlah template publish ({0}) tidak sama dengan working tree ({1}) - folder publish lebih tua' -f $jumlahPub, $jumlahKerja)
}

# Contoh bawaan (folder Bawaan): JSON milik aplikasi — disemai sekali ke database
# dengan penanda (BawaanStore), sehingga hapusan pengguna menempel dan contoh
# baru dari tambalan tetap disiapkan.
if ($csprojAda) {
    if ($csprojTeks -match '\.\.+\\+Bawaan\\+\*\*') { OK 'glob ..\Bawaan\** ada di SuDesApp.Wpf.csproj' }
    else { Gagal 'glob ..\Bawaan\** hilang dari SuDesApp.Wpf.csproj' }
}

$bawaanDir = Join-Path $repoRoot 'Bawaan\TemplateSurat'
$bawaanJson = @()
if (Test-Path -LiteralPath $bawaanDir) {
    $bawaanJson = @(Get-ChildItem -LiteralPath $bawaanDir -File -Filter '*.json')
}
Info ('folder Bawaan/TemplateSurat: {0} berkas JSON' -f $bawaanJson.Count)
if ($bawaanJson.Count -eq 0) {
    Gagal 'Bawaan/TemplateSurat kosong - JSON contoh bawaan wajib terkirim'
} else {
    OK ('contoh bawaan terkirim: {0} berkas JSON' -f $bawaanJson.Count)
}

# Kesegaran: JSON wajib sama dengan katalog C# (satu-satunya sumber kebenaran).
$genProj = Join-Path $repoRoot 'Tools\BawaanGen\BawaanGen.csproj'
if (-not (Test-Path -LiteralPath $genProj)) {
    Gagal 'Tools/BawaanGen tidak ditemukan - pemeriksa kesegaran JSON contoh hilang'
} else {
    $keluaranGen = (& dotnet run --project $genProj --nologo -- --cek | Out-String)
    $kodeGen = $LASTEXITCODE
    foreach ($barisGen in ($keluaranGen -split "`r?`n")) {
        $t = $barisGen.Trim()
        if (-not $t) { continue }
        if ($t.StartsWith('GAGAL')) { Write-Host ('        ' + $t) -ForegroundColor Red }
        else { Info $t }
    }
    if ($kodeGen -ne 0) {
        Gagal ('JSON contoh basi/rusak dengan katalog C# (exit {0}) - jalankan: dotnet run --project Tools/BawaanGen' -f $kodeGen)
    } else {
        OK 'JSON contoh segar dengan katalog C#'
    }
}

# Pembanding publish (folder publish bisa lebih tua dari working tree).
$pubBawaan = Join-Path $repoRoot 'artifacts\publish\Bawaan\TemplateSurat'
if (Test-Path -LiteralPath $pubBawaan) {
    $jumlahPubBawaan = @(Get-ChildItem -LiteralPath $pubBawaan -File -Filter '*.json').Count
    if ($jumlahPubBawaan -eq $bawaanJson.Count) {
        OK 'artifacts/publish/Bawaan/TemplateSurat cocok dengan working tree'
    } else {
        Peringatan ('jumlah JSON bawaan di publish ({0}) beda dengan working tree ({1}) - publish lebih tua' -f $jumlahPubBawaan, $bawaanJson.Count)
    }
} else {
    Info 'artifacts/publish/Bawaan/TemplateSurat tidak ada - pembanding publish dilewati'
}

# ================= TAHAP 5/5: ASET & DIGEST =================
Tahap 'TAHAP 5/5: Aset rilis & digest (SHA-256)'
$artDir = Join-Path $repoRoot 'artifacts'
$list = @()
$zip = $null
$patchPath = Join-Path $artDir 'patch.json'
$installerDir = Join-Path $artDir 'installer'
$relPath = Join-Path $artDir 'RELEASE.md'

if (-not (Test-Path -LiteralPath $artDir)) {
    Lewat 'folder artifacts/ belum ada - seluruh pemeriksaan aset dilewati'
} else {
    $zip = Get-ChildItem -Path (Join-Path $artDir '*.zip') -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
    if ($zip) { $list += [pscustomobject]@{ Nama = $zip.Name; Path = $zip.FullName; Sha = $null } }
    if (Test-Path -LiteralPath $patchPath) { $list += [pscustomobject]@{ Nama = 'patch.json'; Path = $patchPath; Sha = $null } }
    if (Test-Path -LiteralPath $installerDir) {
        foreach ($exeFile in @(Get-ChildItem -LiteralPath $installerDir -File -Filter '*.exe')) {
            $list += [pscustomobject]@{ Nama = $exeFile.Name; Path = $exeFile.FullName; Sha = $null }
        }
    }

    if ($list.Count -eq 0) {
        Lewat 'belum ada artefak (zip/patch.json/installer) di artifacts/ - jalankan rilis dulu'
    } else {
        foreach ($a in $list) {
            $a.Sha = HitungSha256 $a.Path
            Info ('SHA-256 {0}: {1}' -f $a.Nama, $a.Sha)
        }
    }
}

# --- Konsistensi versi antar sumber ---
$versiDari = @{}
if ((Test-Path -LiteralPath $patchPath) -and $list.Count -gt 0) {
    try {
        $pj = Get-Content -LiteralPath $patchPath -Raw | ConvertFrom-Json
        if ($pj.versi) { $versiDari['patch.json'] = [string]$pj.versi }
    } catch { Peringatan ('patch.json tidak terbaca: ' + $_.Exception.Message) }
}
if ($zip -and $zip.Name -match '^SuDesApp_([0-9]+(?:\.[0-9]+)+)_win-x64\.zip$') { $versiDari['zip'] = $Matches[1] }
if ((Test-Path -LiteralPath $relPath) -and $versiDari.Count -lt 3) {
    $relTeksAwal = Get-Content -LiteralPath $relPath -Raw
    if ($relTeksAwal -match 'Rilis SuDesApp\s+([0-9]+(?:\.[0-9]+)+)') { $versiDari['RELEASE.md'] = $Matches[1] }
}
$nilaiUnik = @($versiDari.Values | Sort-Object -Unique)
if ($nilaiUnik.Count -gt 1) {
    Gagal ('versi artefak tidak konsisten: ' + (($versiDari.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ', '))
} elseif ($nilaiUnik.Count -eq 1) {
    OK ('versi artefak konsisten: {0}' -f $nilaiUnik[0])
    if ($Versi -and ($Versi -ne $nilaiUnik[0])) { Gagal ('-Versi {0} berbeda dari versi artefak ({1})' -f $Versi, $nilaiUnik[0]) }
} elseif ($Versi) {
    Info ('versi hanya dari parameter: {0}' -f $Versi)
} elseif ($list.Count -gt 0) {
    Lewat 'versi tak bisa dideteksi dari artefak lokal'
}
$versiAkhir = $Versi
if ((-not $versiAkhir) -and ($nilaiUnik.Count -eq 1)) { $versiAkhir = $nilaiUnik[0] }

# --- Konsistensi dengan AssemblyVersion csproj ---
if ($csprojAda -and $csprojTeks -match '<AssemblyVersion>([^<]+)</AssemblyVersion>') {
    $vAss = $Matches[1]
    $potong = @($vAss -split '\.')
    $vAss3 = $vAss
    if ($potong.Count -ge 3) { $vAss3 = ($potong[0..2] -join '.') }
    if ($versiAkhir) {
        if ($vAss3 -eq $versiAkhir) { OK ('AssemblyVersion {0} cocok dengan versi rilis' -f $vAss) }
        else { Gagal ('AssemblyVersion {0} tidak cocok dengan versi rilis {1}' -f $vAss, $versiAkhir) }
    } else { Info ('AssemblyVersion tercatat: {0} (versi rilis tak diketahui - tak bisa dibandingkan)' -f $vAss) }
} elseif ($csprojAda) {
    Peringatan '<AssemblyVersion> tidak ditemukan di SuDesApp.Wpf.csproj'
}

# --- Pembanding lokal: RELEASE.md ---
$relAda = Test-Path -LiteralPath $relPath
$relTeks = ''
if ($relAda) { $relTeks = Get-Content -LiteralPath $relPath -Raw }
$sudahDicocokkan = @{}
if (-not $relAda) {
    if ($list.Count -gt 0) { Lewat 'artifacts/RELEASE.md tidak ada - pembanding digest lokal dilewati' }
} else {
    $rxRel = [regex]'SHA-256 `([^`]+)`: `([0-9a-fA-F]{64})`'
    $jumlahCatatan = 0
    foreach ($m in $rxRel.Matches($relTeks)) {
        $jumlahCatatan++
        $namaCatat = $m.Groups[1].Value
        $shaCatat = $m.Groups[2].Value.ToLowerInvariant()
        $target = $list | Where-Object { $_.Nama -eq $namaCatat } | Select-Object -First 1
        if (-not $target) {
            $kandidat = @((Join-Path $artDir $namaCatat), (Join-Path $installerDir $namaCatat)) |
                Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if ($kandidat) { $target = [pscustomobject]@{ Nama = $namaCatat; Path = $kandidat; Sha = (HitungSha256 $kandidat) } }
        }
        if (-not $target) {
            Lewat ('RELEASE.md mencatat {0} tetapi berkasnya tidak ada lokal' -f $namaCatat)
            continue
        }
        $sudahDicocokkan[$namaCatat] = $true
        if ($target.Sha -eq $shaCatat) { OK ('RELEASE.md: SHA-256 {0} cocok' -f $namaCatat) }
        else { Gagal ('RELEASE.md: SHA-256 {0} TIDAK cocok (lokal {1} vs tercatat {2})' -f $namaCatat, $target.Sha, $shaCatat) }
    }
    if ($jumlahCatatan -eq 0) { Peringatan 'RELEASE.md tidak memuat baris SHA-256 sama sekali' }
}
foreach ($a in $list) {
    if (-not $sudahDicocokkan[$a.Nama]) {
        Lewat ('{0} tidak tercatat di RELEASE.md - andalkan digest GitHub' -f $a.Nama)
    }
}

# --- Pembanding GitHub: digest aset ---
if ($TanpaGitHub) {
    Lewat 'pemeriksaan GitHub dilewati (-TanpaGitHub)'
} elseif ($list.Count -eq 0) {
    Lewat 'tidak ada artefak lokal untuk dibandingkan dengan GitHub'
} elseif (-not $versiAkhir) {
    Lewat 'versi tidak diketahui - pemeriksaan digest GitHub dilewati'
} else {
    $tag = 'v' + $versiAkhir
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $h = @{ 'User-Agent' = 'check-rilis'; 'Accept' = 'application/vnd.github+json' }
        if ($env:GITHUB_TOKEN) { $h['Authorization'] = 'Bearer ' + $env:GITHUB_TOKEN }
        $uriRilis = 'https://api.github.com/repos/{0}/releases?per_page=100' -f $Repo
        $semuaRilis = Invoke-RestMethod -Uri $uriRilis -Headers $h
        # Perbaikan PS 5.1: balasan larik bisa terbungkus satu larik bersarang
        $semuaRilis = @($semuaRilis | ForEach-Object { $_ })
        $rilis = $semuaRilis |
            Where-Object { ($_.tag_name -eq $tag) -and (-not $_.draft) } |
            Sort-Object created_at -Descending |
            Select-Object -First 1
        if (-not $rilis) {
            Lewat ('rilis {0} belum terbit di GitHub (atau masih draft)' -f $tag)
        } else {
            $peta = @{}
            foreach ($aset in $rilis.assets) { $peta[$aset.name] = $aset }
            foreach ($a in $list) {
                $aset = $peta[$a.Nama]
                if (-not $aset) {
                    Lewat ('aset {0} belum ada di rilis {1}' -f $a.Nama, $tag)
                    continue
                }
                $ukuranLokal = (Get-Item -LiteralPath $a.Path).Length
                if ($aset.digest) {
                    $shaGitHub = ($aset.digest -replace '^sha256:', '')
                    if ($shaGitHub.ToLowerInvariant() -eq $a.Sha) {
                        OK ('digest GitHub {0} cocok' -f $a.Nama)
                    } else {
                        Gagal ('digest GitHub {0} TIDAK cocok (GitHub {1} vs lokal {2})' -f $a.Nama, $shaGitHub, $a.Sha)
                    }
                } elseif ($aset.size -eq $ukuranLokal) {
                    Lewat ('digest GitHub {0} tidak tersedia - pembanding hanya ukuran byte (cocok)' -f $a.Nama)
                } else {
                    Gagal ('ukuran aset {0} di GitHub ({1}) != lokal ({2}) dan digest tidak tersedia' -f $a.Nama, $aset.size, $ukuranLokal)
                }
            }
        }
    } catch {
        Lewat ('GitHub tidak terjangkau / permintaan gagal: {0}' -f $_.Exception.Message)
    }
}

# ================= RINGKASAN =================
Write-Host ''
$pesan = 'RINGKASAN: {0} lulus, {1} gagal, {2} dilewati (LEWATI tidak menggagalkan)' -f `
    $script:jumlahLulus, $script:jumlahGagal, $script:jumlahLewat
if ($script:jumlahGagal -gt 0) {
    Write-Host $pesan -ForegroundColor Red
    exit 1
}
Write-Host $pesan -ForegroundColor Green
exit 0
