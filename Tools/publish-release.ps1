[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$SkipInstaller,

    # Tuntut installer benar-benar terbentuk. Tanpa saklar ini, ISCC.exe yang
    # tidak ditemukan hanya memunculkan peringatan dan rilis tetap terbit tanpa
    # installer (perilaku lama). Alur CI memakai saklar ini supaya rilis yang
    # kehilangan installer menggagalkan job, bukan lolos diam-diam.
    [switch]$WajibInstaller,

    # Paket yang memang tanpa kredensial Google (mis. uji internal) tetap boleh dibuat,
    # tetapi harus disebutkan terus terang di sini — gerbang kredensial Google di
    # bawah akan menolak publish yang tanpa kredensial tertanam.
    [switch]$IzinkanTanpaKredensial,

    # Tanda tangan Authenticode. Kosong = pakai env SUDESAPP_PFX_PATH /
    # SUDESAPP_PFX_PASSWORD / SUDESAPP_SIGN_THUMBPRINT (jalur CI), atau rilis
    # tetap dibuat tanpa tanda tangan dengan status yang ditulis terus terang.
    [string]$SertifikatPfx,
    [string]$SandiSertifikat,
    [string]$SidikJariSertifikat,

    # Pembaruan kecil (opsional): isi versi rilis sebelumnya, maka skrip membuat
    # patch.json + patch-<versi>.zip dari perbandingan hasil publish. Pembandingnya
    # diambil dari -PublishLama, dari zip portable rilis lama di artifacts/
    # (SuDesApp_<versiLama>_win-x64.zip), atau bila keduanya kosong - diunduh
    # sendiri dari rilis GitHub (dengan verifikasi sha256 terhadap digest aset).
    # Beberapa versi dipisah koma: tiap pembanding disatukan sehingga satu
    # tambalan juga melayani versi-versi yang lebih tua (lihat -DariVersi).
    [string[]]$VersiLama,
    [string[]]$PublishLama,

    # Versi yang boleh memasang tambalan ini. Bawaannya hanya versi yang benar-benar
    # dibandingkan; versi yang lebih tua pemilik paketnya harus dinyatakan sendiri.
    [string[]]$DariVersi,
    [string]$Ringkasan,
    [switch]$Besar,

    # Jangan berhenti walau working tree masih kotor (hanya untuk uji coba).
    [switch]$LewatiPemeriksaan,

    # Unggah langsung ke GitHub Releases setelah aset siap: rilis dicari/dibuat
    # sebagai draft, aset bernama sama diganti, lalu rilis diterbitkan
    # (draft=false, make_latest=true). Token diambil dari env GITHUB_TOKEN /
    # GH_TOKEN, atau dari kredensial github.com yang tersimpan di Git Credential
    # Manager (token yang sama dipakai push).
    [switch]$Unggah,

    # Repo GitHub tujuan unggah (pemilik/nama).
    [string]$Repo = 'inomixstudio/SuDesApp'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# -VersiLama/-DariVersi boleh datang sebagai satu string berkoma (input workflow).
$VersiLama = @($VersiLama | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().TrimStart('v', 'V') } | Where-Object { $_.Length -gt 0 })
$DariVersi = @($DariVersi | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().TrimStart('v', 'V') } | Where-Object { $_.Length -gt 0 })
$PublishLama = @($PublishLama | Where-Object { $_ })
if ($DariVersi.Count -gt 0 -and $VersiLama.Count -eq 0) {
    # Tanpa pembanding tidak ada isi berkas lama untuk dibandingkan; daftar versi
    # asal saja tidak cukup, jadi jangan diam-diam mengklaim cakupan itu.
    Write-Host '-DariVersi diabaikan: isi tambalan hanya bisa dihitung dari -VersiLama.' -ForegroundColor Yellow
    $DariVersi = @()
}

# Jangan pernah menghasilkan installer yang diam-diam menunjuk server lokal,
# HTTP, atau akar kunci yang tidak cocok dengan server aktivasi produksi.
# Pemeriksaan dijalankan sebelum folder output publish lama disentuh.
$appSettingsPath = Join-Path $repoRoot 'appsettings.json'
if (-not (Test-Path -LiteralPath $appSettingsPath)) {
    throw 'Publish dibatalkan: appsettings.json tidak ditemukan.'
}
$appSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
$urlLisensi = [string]$appSettings.Lisensi.UrlServer
$kunciPublik = [string]$appSettings.Lisensi.KunciPublik
$uriLisensi = $null
if (-not [Uri]::TryCreate($urlLisensi, [UriKind]::Absolute, [ref]$uriLisensi)) {
    throw 'Publish dibatalkan: Lisensi:UrlServer harus berupa URL absolut server produksi.'
}
if ($uriLisensi.Scheme -cne 'https' -or $uriLisensi.IsLoopback -or
    $uriLisensi.Host.EndsWith('.local', [StringComparison]::OrdinalIgnoreCase) -or
    $uriLisensi.UserInfo -or $uriLisensi.Query -or $uriLisensi.Fragment -or
    $uriLisensi.AbsolutePath.TrimEnd('/') -ne '') {
    throw 'Publish dibatalkan: Lisensi:UrlServer harus HTTPS dan tidak boleh mengarah ke loopback atau host .local.'
}
if ([string]::IsNullOrWhiteSpace($kunciPublik)) {
    throw 'Publish dibatalkan: Lisensi:KunciPublik harus berisi kunci ES256 server produksi.'
}

$urlKunci = $urlLisensi.TrimEnd('/') + '/kunci-publik'
try {
    $jawabanKunci = Invoke-RestMethod -Uri $urlKunci -Method Get -TimeoutSec 10 `
        -MaximumRedirection 0 -ErrorAction Stop
} catch {
    throw "Publish dibatalkan: server lisensi tidak dapat diverifikasi di $urlKunci. Pastikan server produksi aktif."
}
if ([string]$jawabanKunci.algoritma -cne 'ES256' -or
    [string]$jawabanKunci.kunci -cne $kunciPublik.Trim()) {
    throw 'Publish dibatalkan: Lisensi:KunciPublik tidak cocok dengan kunci ES256 yang disajikan server lisensi.'
}
Write-Host ('Server lisensi produksi terverifikasi: ' + $uriLisensi.GetLeftPart([UriPartial]::Authority)) -ForegroundColor Green

$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi tidak valid: '$Version' (harus x.y.z, mis. 2.2.0)" }
$assemblyVersion = (($Version.Split('.') + @('0'))[0..3] -join '.')

$publishDir    = Join-Path $repoRoot 'artifacts/publish'
$installerDir  = Join-Path $repoRoot 'artifacts/installer'
$zipPath       = Join-Path $repoRoot ("artifacts/SuDesApp_{0}_win-x64.zip" -f $Version)
$manifestPatch = Join-Path $repoRoot 'artifacts/patch.json'
$unggahTxt     = Join-Path $repoRoot 'artifacts/UNGGAH.txt'
$halamanRilis  = 'https://github.com/inomixstudio/SuDesApp/releases/new?tag=v' + $Version

# ---------- pembantu GitHub ----------
# Didefinisikan di atas supaya bisa dipakai blok tambalan (unduh pembanding)
# maupun blok unggah di bawah - PowerShell menjalankan skrip dari atas ke bawah.

# Token GitHub: env GITHUB_TOKEN / GH_TOKEN lebih dulu, lalu kredensial
# github.com yang tersimpan di Git Credential Manager (token yang sama dipakai
# push - tidak perlu menyalin token ke berkas mana pun).
function AmbilTokenGitHub {
    if ($env:GITHUB_TOKEN -and $env:GITHUB_TOKEN.Trim().Length -gt 0) { return $env:GITHUB_TOKEN.Trim() }
    if ($env:GH_TOKEN -and $env:GH_TOKEN.Trim().Length -gt 0) { return $env:GH_TOKEN.Trim() }

    $interaktifLama = $env:GCM_INTERACTIVE
    $promptLama     = $env:GIT_TERMINAL_PROMPT
    $env:GCM_INTERACTIVE = 'never'
    $env:GIT_TERMINAL_PROMPT = '0'
    try {
        $isian = ("protocol=https`nhost=github.com`n`n" | & git credential fill 2>$null)
        foreach ($baris in $isian) {
            if ($baris -match '^password=(.+)$') { return $Matches[1].Trim() }
        }
    } catch {
    } finally {
        $env:GCM_INTERACTIVE = $interaktifLama
        $env:GIT_TERMINAL_PROMPT = $promptLama
    }
    return ''
}

# Panggilan REST API GitHub dengan pesan galat yang bisa dibaca (kode HTTP +
# badan balasan GitHub) - dipakai untuk unduh pembanding maupun unggah aset.
function KirimApiGitHub {
    param([string]$Metode, [string]$Uri, [hashtable]$Kepala, [string]$Json, [string]$Berkas)
    $arg = @{ Method = $Metode; Uri = $Uri; Headers = $Kepala; ErrorAction = 'Stop' }
    if ($Json)   { $arg['Body']   = $Json;   $arg['ContentType'] = 'application/json; charset=utf-8' }
    if ($Berkas) { $arg['InFile'] = $Berkas; $arg['ContentType'] = 'application/octet-stream' }
    try {
        $hasil = Invoke-RestMethod @arg
        # PS 5.1: balasan JSON berupa larik kembali sebagai SATU objek larik, dan
        # `return` meneruskannya utuh sehingga pemanggil mendapat larik bersarang
        # (filter tag tidak akan pernah cocok). Lewatkan lewat pipeline supaya
        # pemanggil selalu memperoleh daftar datar.
        return $hasil | ForEach-Object { $_ }
    } catch {
        $galat = $_
        $kode = '(?)'
        try { $kode = [int]$galat.Exception.Response.StatusCode } catch { }
        $rinci = ''
        try {
            $aliran = $galat.Exception.Response.GetResponseStream()
            $rinci = (New-Object System.IO.StreamReader($aliran)).ReadToEnd()
        } catch { }
        throw ('GitHub API ' + $Metode + ' ' + $Uri + ' gagal (HTTP ' + $kode + '): ' + $galat.Exception.Message + ' ' + $rinci)
    }
}

# Tulis berkas sisi .sha256 berformat `sha256sum -c`: dua spasi antara digest dan
# nama, akhir baris LF (bukan CRLF Windows). Berkas ini ikut diunggah sebagai
# aset rilis supaya pengguna bisa memeriksa unduhan tanpa menyalin nilai dari
# catatan rilis, dan `sha256sum -c` di Linux/macOS langsung berhasil.
function TulisSidikJari {
    param([string]$Berkas)
    $hash = (Get-FileHash -LiteralPath $Berkas -Algorithm SHA256).Hash.ToLowerInvariant()
    $jalur = $Berkas + '.sha256'
    $baris = "{0}  {1}`n" -f $hash, (Split-Path -Leaf $Berkas)
    [IO.File]::WriteAllText($jalur, $baris, [Text.Encoding]::ASCII)
    return $jalur
}

# Unduh zip portable rilis lama dari GitHub sebagai pembanding tambalan - supaya
# pembaruan kecil bisa dibuat dari komputer mana pun tanpa unduhan manual.
# Hasil disimpan di jalur canonic artifacts/SuDesApp_<versi>_win-x64.zip sekaligus
# menjadi cache untuk unggahan berikutnya.
function UnduhZipRilisLama {
    param([string]$Versi, [string]$Tujuan)

    $tag = 'v' + $Versi
    Write-Host ("Mengunduh zip portable {0} dari rilis GitHub..." -f $tag) -ForegroundColor Cyan

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $api = 'https://api.github.com/repos/' + $Repo
    $kepala = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'SuDesApp-publish-release' }
    $token = AmbilTokenGitHub
    if ($token) { $kepala['Authorization'] = 'Bearer ' + $token }

    # Cari lewat daftar, bukan endpoint releases/tags/{tag} - endpoint itu tidak
    # mengembalikan rilis draft (balas 404).
    $daftar = @(KirimApiGitHub -Metode 'Get' -Uri ($api + '/releases?per_page=50') -Kepala $kepala)
    $rilis = $daftar | Where-Object { $_.tag_name -eq $tag -and -not $_.draft } | Select-Object -First 1
    if (-not $rilis) { throw ("rilis {0} tidak ditemukan di GitHub (atau masih draft)" -f $tag) }

    # Nama zip portable tidak seragam antar rilis: skrip ini mengunggah
    # SuDesApp_<versi>_win-x64.zip, sedangkan rilis 2.10.0 memakai akhiran
    # _portable. Tanpa menerima keduanya, pembanding tidak ditemukan, tambalan
    # dilewati diam-diam, dan rilis hanya keluar sebagai installer penuh.
    $aset = $null
    foreach ($nama in @(("SuDesApp_{0}_win-x64.zip" -f $Versi),
                        ("SuDesApp_{0}_win-x64_portable.zip" -f $Versi))) {
        $aset = @($rilis.assets) | Where-Object { $_.name -eq $nama } | Select-Object -First 1
        if ($aset) { break }
    }
    if (-not $aset) {
        $aset = @($rilis.assets) | Where-Object { $_.name -like 'SuDesApp_*_win-x64*.zip' } | Select-Object -First 1
    }
    if (-not $aset) { throw ("rilis {0} tidak memuat zip portable SuDesApp_*_win-x64*.zip" -f $tag) }

    # Dua jalur unduh: URL unduh langsung tanpa header (paling tahan redirect untuk
    # repo publik), bila gagal memakai endpoint API asset + token (repo privat).
    $lamaProg = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    $galat = $null
    try {
        try {
            Invoke-WebRequest -Uri $aset.browser_download_url -OutFile $Tujuan -UseBasicParsing -TimeoutSec 600
        } catch {
            if ($token) {
                $kepalaAsset = @{
                    Accept        = 'application/octet-stream'
                    'User-Agent'  = 'SuDesApp-publish-release'
                    Authorization = 'Bearer ' + $token
                }
                Invoke-RestMethod -Uri $aset.url -Headers $kepalaAsset -OutFile $Tujuan -TimeoutSec 600
            } else {
                throw
            }
        }
    } catch {
        $galat = $_
    } finally {
        $ProgressPreference = $lamaProg
    }
    if ($galat) {
        # Berkas sebagian jangan ditinggalkan - unggahan berikutnya akan salah
        # menganggapnya zip utuh.
        if (Test-Path -LiteralPath $Tujuan) { Remove-Item -LiteralPath $Tujuan -Force -ErrorAction SilentlyContinue }
        throw ('unduhan zip pembanding gagal: ' + $galat.Exception.Message)
    }

    # Verifikasi sha256 terhadap digest yang dicatat GitHub (bila tersedia).
    if ($aset.digest -and $aset.digest.StartsWith('sha256:')) {
        $harap = $aset.digest.Substring(7)
        $dapat = (Get-FileHash -LiteralPath $Tujuan -Algorithm SHA256).Hash.ToLower()
        if ($dapat -ne $harap) {
            Remove-Item -LiteralPath $Tujuan -Force
            throw ("sha256 zip hasil unduhan tidak cocok dengan digest rilis ({0})" -f $harap)
        }
    } else {
        Write-Host '   digest aset tidak tersedia - sha256 tidak bisa diverifikasi.' -ForegroundColor Yellow
    }

    $ukuran = (Get-Item -LiteralPath $Tujuan).Length
    Write-Host ("   terunduh: {0}  ({1:N0} byte, sha256 cocok dengan rilis)" -f (Split-Path -Leaf $Tujuan), $ukuran) -ForegroundColor Green
}

# Ringkasan pembaruan kecil: judul commit sejak tag rilis lama - saluran yang sama
# dengan langkah "Paket pembaruan kecil" di release.yml. Berkas hasilnya
# dikembalikan; string kosong bila tidak ada judul yang bisa diambil.
function BuatRingkasanOtomatis {
    param([string]$VersiLama)

    $keluaran = Join-Path $repoRoot 'artifacts/RINGKASAN.txt'
    $tagLama  = 'v' + $VersiLama
    $adaTag = & git -c ('safe.directory=' + $repoRoot) rev-parse -q --verify ('refs/tags/' + $tagLama) 2>$null
    if (-not $adaTag) {
        # Tag lama tidak ada lokal (repo shallow / mesin baru) - ambil dulu seperti CI.
        & git -c ('safe.directory=' + $repoRoot) fetch --tags --quiet 2>$null
        $adaTag = & git -c ('safe.directory=' + $repoRoot) rev-parse -q --verify ('refs/tags/' + $tagLama) 2>$null
    }

    $judul = @()
    if ($adaTag) {
        $judul = @(& git -c ('safe.directory=' + $repoRoot) log --pretty=format:%s ('{0}..HEAD' -f $tagLama) 2>$null)
    } else {
        $judul = @(& git -c ('safe.directory=' + $repoRoot) log -n 15 --pretty=format:%s 2>$null)
    }
    $judul = @($judul | Where-Object { $_ -and $_.Trim().Length -gt 0 } | Select-Object -First 20)
    if ($judul.Count -eq 0) { return '' }

    $judul | Set-Content -LiteralPath $keluaran -Encoding UTF8
    Write-Host ("Ringkasan   : {0} ({1} butir sejak {2})" -f (Split-Path -Leaf $keluaran), $judul.Count, $tagLama)
    return $keluaran
}

Write-Host ''
Write-Host "== Publish SuDesApp $Version ==" -ForegroundColor Cyan

# Aset yang dibangun saat working tree kotor tidak akan sama dengan isi tag yang
# dipakai aplikasi membandingkan versi — ingatkan terus terang.
if (-not $LewatiPemeriksaan) {
    try {
        $kotor = (& git -c ('safe.directory=' + $repoRoot) status --porcelain 2>$null | Out-String).Trim()
    } catch {
        $kotor = ''
    }
    if ($kotor.Length -gt 0) {
        Write-Host 'Peringatan: masih ada perubahan yang belum di-commit — aset yang dibangun tidak akan sama dengan isi tag.' -ForegroundColor Yellow
        Write-Host 'Commit dulu, atau pakai -LewatiPemeriksaan bila memang uji coba.' -ForegroundColor Yellow
    }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish (Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal (exit $LASTEXITCODE)" }

if (-not (Test-Path (Join-Path $publishDir 'SuDesApp.exe'))) { throw "SuDesApp.exe tidak ditemukan di hasil publish" }

# Gerbang kredensial Google: hasil publish yang tidak memuat kredensial OAuth
# tertanam akan membuat fitur Google (Formulir/Sheet/Drive) tidak bisa menyambung
# setelah dipasang (versi sebelumnya sempat
# terbit begitu karena dibangun tanpa berkas rahasia build). Pemeriksaan ini
# memastikan aset rilis tidak pernah keluar dalam keadaan seperti itu.
$statusKredensial = 'Kredensial Google tertanam - koneksi akun Google siap dipakai.'
$cekArg = @{}
if ($IzinkanTanpaKredensial) { $cekArg['IzinkanTanpaKredensial'] = $true }
& (Join-Path $PSScriptRoot 'check-kredensial-google.ps1') -Folder $publishDir @cekArg
if ($LASTEXITCODE -ne 0) {
    throw "Publish dibatalkan: hasil publish tidak memuat kredensial OAuth Google, sehingga aplikasi terpasang tidak bisa menyambungkan akun Google (Formulir/Sheet/Drive). Bangun aset rilis di komputer yang memiliki berkas rahasia build Utilities/GoogleClientCredentialsBawaan.lokal.cs, atau tambahkan -IzinkanTanpaKredensial bila rilis tanpa kredensial Google memang disengaja."
}
if ($IzinkanTanpaKredensial) {
    $statusKredensial = 'Kredensial Google TIDAK tertanam - teknisi mengisinya di Pengaturan Aplikasi -> Kredensial Google pada tiap komputer.'
}

# ---------- tanda tangan Authenticode (executable) ----------
# Tanpa tanda tangan, setiap pengguna baru melihat peringatan SmartScreen dan
# nama penerbit tidak tampil di properti berkas. Dijalankan SEBELUM zip dan
# tambalan dibuat supaya semua jalur distribusi membawa exe yang sama.
$sertifikatTersedia = [bool]($SertifikatPfx -or $SidikJariSertifikat -or
    $env:SUDESAPP_PFX_PATH -or $env:SUDESAPP_SIGN_THUMBPRINT)
$argTandaTangan = @{}
if ($SertifikatPfx)       { $argTandaTangan['Pfx'] = $SertifikatPfx }
if ($SandiSertifikat)     { $argTandaTangan['Sandi'] = $SandiSertifikat }
if ($SidikJariSertifikat) { $argTandaTangan['SidikJari'] = $SidikJariSertifikat }
$ditandatangani = @()

if ($sertifikatTersedia) {
    & (Join-Path $PSScriptRoot 'Tanda-Tangani-Rilis.ps1') -Berkas (Join-Path $publishDir 'SuDesApp.exe') @argTandaTangan
    if ($LASTEXITCODE -ne 0) { throw "Publish dibatalkan: tanda tangan SuDesApp.exe gagal (exit $LASTEXITCODE)." }
    $ditandatangani += 'SuDesApp.exe'
} else {
    Write-Host 'Peringatan: sertifikat tanda tangan tidak tersedia - aset rilis TIDAK ditandatangani.' -ForegroundColor Yellow
    Write-Host 'Tanpa Authenticode, Windows SmartScreen memperingatkan setiap pengguna baru.' -ForegroundColor Yellow
    Write-Host 'Isi -SertifikatPfx/-SandiSertifikat atau env SUDESAPP_PFX_PATH/SUDESAPP_PFX_PASSWORD (lihat docs/TANDA-TANGAN-RILIS.md).' -ForegroundColor Yellow
}

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host ("Portable  : " + $zipPath) -ForegroundColor Green

$setup = $null
if (-not $SkipInstaller) {
    # Cari folder "Inno Setup <versi>" apa pun, bukan hanya 6: paket choco dan
    # pemasang resmi bisa memakai folder bernomor lain, dan jalur tetap yang
    # ditulis tangan membuat installer diam-diam tidak dibuat setelah pembaruan.
    $iscc = $null
    foreach ($akar in @("${env:ProgramFiles(x86)}", $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if (-not $akar) { continue }
        $kandidat = @(Get-ChildItem -Path (Join-Path $akar 'Inno Setup*\ISCC.exe') -ErrorAction SilentlyContinue)
        if ($kandidat.Count -gt 0) {
            $iscc = ($kandidat | Sort-Object FullName -Descending | Select-Object -First 1).FullName
            break
        }
    }
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }

    if ($iscc) {
        New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
        # VersionInfoVersion di skrip Inno hanya menerima angka (x.y.z[.w]);
        # prarilis seperti 2.7.1-rc1 memakai bentuk numeriknya.
        $numericParts = @((($Version -replace '-.*$', '').Split('.')) + @('0', '0', '0'))
        $appVersionNumeric = $numericParts[0..3] -join '.'
        & $iscc "/DAppVersion=$Version" "/DAppVersionNumeric=$appVersionNumeric" (Join-Path $repoRoot 'installer/SuDesApp.iss')
        if ($LASTEXITCODE -ne 0) { throw "ISCC gagal (exit $LASTEXITCODE)" }
        $setup = Get-ChildItem -LiteralPath $installerDir -Filter ("SuDesApp_{0}_Setup.exe" -f $Version) -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $setup) { throw "Installer tidak terbentuk di $installerDir" }
        if ($sertifikatTersedia) {
            & (Join-Path $PSScriptRoot 'Tanda-Tangani-Rilis.ps1') -Berkas $setup.FullName @argTandaTangan
            if ($LASTEXITCODE -ne 0) { throw "Publish dibatalkan: tanda tangan installer gagal (exit $LASTEXITCODE)." }
            $ditandatangani += $setup.Name
        }
    } elseif ($WajibInstaller) {
        throw ('Installer wajib dibuat, tetapi ISCC.exe tidak ditemukan. Pasang Inno Setup 6 ' +
               '(https://jrsoftware.org/isdl.php) atau, bila memang tidak ingin menerbitkan installer, ' +
               'jalankan tanpa -WajibInstaller.')
    } else {
        Write-Host 'Inno Setup (ISCC.exe) tidak ditemukan.' -ForegroundColor Yellow
        Write-Host 'Install gratis dari https://jrsoftware.org/isdl.php lalu jalankan ulang skrip ini.' -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '== Ringkasan ==' -ForegroundColor Cyan
if ($ditandatangani.Count -gt 0) {
    $statusTandaTangan = 'Authenticode: ' + ($ditandatangani -join ', ') + ' ditandatangani.'
} else {
    $statusTandaTangan = 'Authenticode: TIDAK ditandatangani - pengguna baru akan melihat peringatan SmartScreen.'
}
$lines = @("Rilis SuDesApp $Version.", "")
if ($setup) {
    $hash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLower()
    Write-Host ("Installer : " + $setup.FullName)
    Write-Host ("SHA-256   : " + $hash)
    $lines += ('Installer: `{0}`' -f $setup.Name)
    $lines += ('SHA-256 `{0}`: `{1}`' -f $setup.Name, $hash)
} else {
    Write-Host 'Installer : (tidak dibuat)'
}
$zipName = Split-Path -Leaf $zipPath
$lines += ('Portable: `{0}`' -f $zipName)
if ($setup) {
    $lines += ('Sidik jari: `{0}.sha256`, `{1}.sha256`' -f $setup.Name, $zipName)
} else {
    $lines += ('Sidik jari: `{0}.sha256`' -f $zipName)
}
$lines += ''
$lines += $statusKredensial
$lines += $statusTandaTangan
Write-Host ("Portable  : " + $zipPath)
Write-Host ("Kredensial: " + $statusKredensial)
Write-Host ("Tanda tgn : " + $statusTandaTangan)

# ---------- tambalan (pembaruan kecil) ----------
# Bandingkan hasil publish ini dengan hasil publish rilis lama. Pembanding bisa
# diberikan lewat -PublishLama, dipakai dari zip portable rilis lama di artifacts/
# bila ada, atau diunduh dari rilis GitHub bila belum ada di mesin ini.
# Setiap pembanding dikumpulkan dulu; yang gagal (zip tak ada / unduhan gagal)
# dilewati sendirian, bukan membatalkan seluruh tambalan.
$pembanding = @()
if ($PublishLama.Count -gt 0 -and $PublishLama.Count -ne $VersiLama.Count) {
    throw ('-PublishLama ({0}) harus sejumlah -VersiLama ({1}).' -f $PublishLama.Count, $VersiLama.Count)
}
for ($i = 0; $i -lt $VersiLama.Count; $i++) {
    $versiBanding = $VersiLama[$i]
    $folderBanding = if ($PublishLama.Count -gt 0) { $PublishLama[$i] } else { '' }
    if (-not $folderBanding) {
        $zipLama = Join-Path $repoRoot ("artifacts/SuDesApp_{0}_win-x64.zip" -f $versiBanding)
        if (-not (Test-Path -LiteralPath $zipLama)) {
            try {
                UnduhZipRilisLama -Versi $versiBanding -Tujuan $zipLama
            } catch {
                # Kegagalan lingkungan (jaringan/token/rilis tak ada) tidak boleh
                # mematikan rilis: pembanding ini dilewati, tambalan menyusut.
                Write-Host ('Pembanding {0} tidak tersedia: {1}' -f $versiBanding, $_.Exception.Message) -ForegroundColor Yellow
                continue
            }
        }
        $folderBanding = Join-Path $repoRoot ("artifacts/publish-lama-{0}" -f $versiBanding)
        if (Test-Path -LiteralPath $folderBanding) { Remove-Item -LiteralPath $folderBanding -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zipLama, $folderBanding)
        Write-Host ("Pembanding: {0} diekstrak ke {1}" -f $zipLama, $folderBanding)
    }
    $pembanding += @{ Versi = $versiBanding; Folder = $folderBanding }
}

if ($VersiLama.Count -gt 0 -and $pembanding.Count -eq 0) {
    Write-Host 'Tambalan dilewati - patch.json akan berjenis "besar" (installer penuh).' -ForegroundColor Yellow
}

if ($pembanding.Count -gt 0) {
    # Ringkasan otomatis dari judul commit sejak tag pembanding pertama (bila
    # -Ringkasan tidak diberi) - dipakai Buat-Tambalan dan, bila tambalan gagal,
    # badan patch.json fallback.
    if (-not $Ringkasan) {
        $Ringkasan = BuatRingkasanOtomatis -VersiLama $pembanding[0].Versi
    }
    # Hashtable splat: -Ringkasan/-Besar harus diteruskan sebagai argumen bernama.
    # Array splat memposisikan argumen dan path RINGKASAN.txt mendarat di parameter
    # int lain (BatasBerkasPersen).
    $argTambalan = @{
        Versi       = $Version
        VersiLama   = @($pembanding | ForEach-Object { $_.Versi })
        PublishBaru = $publishDir
        PublishLama = @($pembanding | ForEach-Object { $_.Folder })
        Keluaran    = (Join-Path $repoRoot 'artifacts')
    }
    if ($DariVersi.Count -gt 0) { $argTambalan['DariVersi'] = $DariVersi }
    if ($Ringkasan) { $argTambalan['Ringkasan'] = $Ringkasan }
    if ($Besar)     { $argTambalan['Besar'] = $true }

    & (Join-Path $PSScriptRoot 'Buat-Tambalan.ps1') @argTambalan
    if ($LASTEXITCODE -ne 0) { throw "Pembuatan tambalan gagal (exit $LASTEXITCODE)" }
}

# ---------- patch.json selalu tersedia untuk versi ini ----------
# Aplikasi terpasang membaca patch.json dari halaman rilis; tanpa berkas itu ia
# meminta installer penuh. Bila belum ada patch.json untuk versi ini (mis. tanpa
# pembanding), tulis manifest "besar" sebagai jaring pengaman — sama seperti
# langkah "Pastikan patch.json tersedia" di .github/workflows/release.yml.
$isiPatch = $null
if (Test-Path -LiteralPath $manifestPatch) {
    $isiPatch = Get-Content -LiteralPath $manifestPatch -Raw | ConvertFrom-Json
    if ($isiPatch.versi -ne $Version) { $isiPatch = $null }
}
if (-not $isiPatch) {
    $ringkasanFallback = @()
    if ($Ringkasan -and (Test-Path -LiteralPath $Ringkasan)) {
        $ringkasanFallback = @(Get-Content -LiteralPath $Ringkasan |
            ForEach-Object { $_.Trim().TrimStart('-', '*', ' ') } |
            Where-Object { $_.Length -gt 0 } |
            Select-Object -First 25)
    }
    $fallback = [ordered]@{
        versi         = $Version
        dariVersi     = @()
        jenis         = 'besar'
        ringkasan     = $ringkasanFallback
        berkasPatch   = ''
        sha256Patch   = ''
        berkas        = @()
        berkasDihapus = @()
        catatan       = 'Rilis ini memakai installer penuh.'
    }
    $fallback | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPatch -Encoding UTF8
    Write-Host ("patch.json fallback (jenis besar) ditulis: {0}" -f $manifestPatch) -ForegroundColor Yellow
    $isiPatch = Get-Content -LiteralPath $manifestPatch -Raw | ConvertFrom-Json
}
$lines += ('Pembaruan: `{0}` jenis {1} (dari {2}, {3} berkas)' -f $isiPatch.versi, $isiPatch.jenis, ($isiPatch.dariVersi -join ', '), @($isiPatch.berkas).Count)
if ($isiPatch.jenis -eq 'kecil') { $lines += ('Tambalan: `{0}`' -f $isiPatch.berkasPatch) }
Write-Host ("Tambalan  : " + $manifestPatch) -ForegroundColor Green

# ---------- catatan rilis ----------
# Tautkan catatan rilis versi ini (fitur, perbaikan, cara memasang) supaya halaman
# rilis tidak hanya berisi daftar aset.
$berkasCatatan = Join-Path $repoRoot ("docs/catatan-rilis-{0}.md" -f $Version)
if (Test-Path -LiteralPath $berkasCatatan) {
    $lines += ('Catatan rilis: https://github.com/{0}/blob/v{1}/docs/catatan-rilis-{1}.md' -f $Repo, $Version)
}
$releaseNotes = Join-Path $repoRoot 'artifacts/RELEASE.md'
Set-Content -LiteralPath $releaseNotes -Value $lines -Encoding UTF8
Write-Host ("Catatan   : " + $releaseNotes)

# Badan rilis di GitHub: catatan rilis versi ini lalu daftar aset. Halaman rilis
# yang hanya berisi daftar aset menyulitkan pengguna melihat apa yang berubah.
$badanRilis = (Get-Content -LiteralPath $releaseNotes -Raw -Encoding UTF8).Trim()
if (Test-Path -LiteralPath $berkasCatatan) {
    # -Encoding UTF8 wajib: catatan rilis ditulis tanpa BOM, dan PS 5.1 membaca
    # berkas tanpa BOM sebagai cp1252 sehingga tanda pisah/tanda kutip Rusak
    # menjadi mojibake di halaman rilis GitHub.
    $badanRilis = (Get-Content -LiteralPath $berkasCatatan -Raw -Encoding UTF8).Trim() + "`n`n## Sidik jari aset`n`n" + $badanRilis
}

# ---------- daftar aset untuk diunggah ----------
$daftarAset = @()
if ($setup) {
    $daftarAset += $setup.FullName
    $daftarAset += (TulisSidikJari -Berkas $setup.FullName)
}
$daftarAset += $zipPath
$daftarAset += (TulisSidikJari -Berkas $zipPath)
$daftarAset += $manifestPatch
$patchZip = Join-Path $repoRoot ("artifacts/patch-{0}.zip" -f $Version)
if (Test-Path -LiteralPath $patchZip) { $daftarAset += $patchZip }

$barisUnggah = @(
    "Daftar aset rilis SuDesApp $Version - unggah ke:",
    $halamanRilis,
    ""
)
foreach ($berkas in $daftarAset) {
    $item = Get-Item -LiteralPath $berkas
    $hash = (Get-FileHash -LiteralPath $berkas -Algorithm SHA256).Hash.ToLower()
    $barisUnggah += ('- {0}  ({1:N0} byte)  sha256: {2}' -f $item.Name, $item.Length, $hash)
}
$barisUnggah += ''
$barisUnggah += 'patch.json wajib ikut diunggah - tanpa berkas itu aplikasi terpasang meminta installer penuh.'
Set-Content -LiteralPath $unggahTxt -Value $barisUnggah -Encoding UTF8
Write-Host ("Daftar    : " + $unggahTxt)

Write-Host ''
# ---------- unggah ke GitHub Releases (opsional) ----------
# Memakai REST API langsung supaya tidak butuh gh CLI. Urutannya: pastikan tag
# sudah ada di origin -> cari rilis v<versi> (atau buat sebagai draft) -> ganti
# aset bernama sama -> unggah semua aset -> terbitkan. Rilis draft baru terlihat
# pengguna setelah seluruh aset lengkap. Fungsi AmbilTokenGitHub/KirimApiGitHub
# sudah didefinisikan di bagian atas skrip.
if ($Unggah) {
    Write-Host ''
    Write-Host '== Unggah ke GitHub Releases ==' -ForegroundColor Cyan

    $tagRilis = 'v' + $Version

    # Tag harus sudah ada di origin. Tanpa itu GitHub membuat tag dari cabang
    # default, sehingga rilis menunjuk commit yang salah - lebih baik berhenti.
    $tagDiOrigin = (& git -c ('safe.directory=' + $repoRoot) ls-remote --tags origin ('refs/tags/' + $tagRilis) 2>$null | Out-String).Trim()
    if (-not $tagDiOrigin) {
        throw ('Tag ' + $tagRilis + ' belum ada di origin. Dorong dulu (git push origin ' + $tagRilis + ') - tanpa itu GitHub membuat tag dari cabang default dan rilis menunjuk commit yang salah.')
    }

    $token = AmbilTokenGitHub
    if (-not $token) {
        throw ('Token GitHub tidak ditemukan. Set GITHUB_TOKEN (PAT scope repo) atau pastikan Git Credential Manager ' +
               'menyimpan kredensial github.com (git config --get credential.helper).')
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $kepala = @{
        Authorization = 'Bearer ' + $token
        Accept        = 'application/vnd.github+json'
        'User-Agent'  = 'SuDesApp-publish-release'
    }
    $api = 'https://api.github.com/repos/' + $Repo

    # Cari lewat daftar rilis, bukan "releases/tags/{tag}": endpoint tag TIDAK
    # mengembalikan rilis draft (balas 404), sehingga draft yang sudah ada bisa
    # terlihat "tidak ada" dan justru dibuat ulang. Daftar ber-token memuat draft.
    $daftarRilis = @(KirimApiGitHub -Metode 'Get' -Uri ($api + '/releases?per_page=100') -Kepala $kepala)
    $rilis = $daftarRilis | Where-Object { $_.tag_name -eq $tagRilis } | Select-Object -First 1

    $catatanRilis = $badanRilis

    if ($rilis) {
        Write-Host ('Rilis ' + $tagRilis + ' sudah ada (' + $rilis.html_url + ', draft=' + $rilis.draft + ') - aset akan diganti.')
        if (-not $rilis.draft) {
            # Jadikan draft dulu: selama aset lama diganti, pengguna umum tidak
            # melihat rilis berisi aset lengkap yang setengah terganti.
            $badan = @{ draft = $true } | ConvertTo-Json
            $rilis = KirimApiGitHub -Metode 'Patch' -Uri ($api + '/releases/' + $rilis.id) -Kepala $kepala -Json $badan
            Write-Host '   rilis disetel ke draft sementara - penggantian aset berjalan tertutup.'
        }
    } else {
        $badan = @{
            tag_name = $tagRilis
            name     = 'SuDesApp ' + $Version
            body     = $catatanRilis
            draft    = $true
        } | ConvertTo-Json
        $rilis = KirimApiGitHub -Metode 'Post' -Uri ($api + '/releases') -Kepala $kepala -Json $badan
        Write-Host ('Draft rilis dibuat: ' + $rilis.html_url)
    }

    # GitHub menolak nama aset kembar (422) - ganti aset lama yang namanya sama
    # supaya skrip ini bisa dijalankan ulang dengan aman.
    foreach ($berkas in $daftarAset) {
        $namaAset = Split-Path -Leaf $berkas
        foreach ($aset in @($rilis.assets)) {
            if ($aset.name -eq $namaAset) {
                KirimApiGitHub -Metode 'Delete' -Uri ($api + '/releases/assets/' + $aset.id) -Kepala $kepala | Out-Null
                Write-Host ('   aset lama diganti: ' + $namaAset)
            }
        }
    }

    $unggahKe = [string]$rilis.upload_url
    $unggahKe = $unggahKe.Substring(0, $unggahKe.IndexOf('{'))
    foreach ($berkas in $daftarAset) {
        $namaAset = Split-Path -Leaf $berkas
        $uri = $unggahKe + '?name=' + [uri]::EscapeDataString($namaAset)
        KirimApiGitHub -Metode 'Post' -Uri $uri -Kepala $kepala -Berkas $berkas | Out-Null
        Write-Host ('   terunggah: ' + $namaAset) -ForegroundColor Green
    }

    $badan = @{ draft = $false; make_latest = 'true'; body = $catatanRilis } | ConvertTo-Json
    $rilis = KirimApiGitHub -Metode 'Patch' -Uri ($api + '/releases/' + $rilis.id) -Kepala $kepala -Json $badan
    Write-Host ''
    Write-Host ('Rilis terbit: ' + $rilis.html_url) -ForegroundColor Green
    Write-Host 'Aset yang terpasang:'
    foreach ($aset in @($rilis.assets)) {
        Write-Host ('   - {0}  ({1:N0} byte)' -f $aset.name, $aset.size)
    }
} else {
    Write-Host ('Upload manual: ' + $halamanRilis) -ForegroundColor Green
}
