[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$SkipInstaller,

    # Pembaruan kecil (opsional): isi versi rilis sebelumnya yang hasil publish-nya
    # tersedia di -PublishLama, maka skrip ini sekaligus membuat patch.json +
    # patch-<versi>.zip untuk diunggah bersama installer.
    [string]$VersiLama,
    [string]$PublishLama,
    [string]$Ringkasan,
    [switch]$Besar
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versi tidak valid: '$Version' (harus x.y.z, mis. 2.2.0)" }
$assemblyVersion = (($Version.Split('.') + @('0'))[0..3] -join '.')

$publishDir   = Join-Path $repoRoot 'artifacts/publish'
$installerDir = Join-Path $repoRoot 'artifacts/installer'
$zipPath      = Join-Path $repoRoot ("artifacts/SuDesApp_{0}_win-x64.zip" -f $Version)

Write-Host ''
Write-Host "== Publish SuDesApp $Version ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish (Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal (exit $LASTEXITCODE)" }

if (-not (Test-Path (Join-Path $publishDir 'SuDesApp.exe'))) { throw "SuDesApp.exe tidak ditemukan di hasil publish" }

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host ("Portable  : " + $zipPath) -ForegroundColor Green

$setup = $null
if (-not $SkipInstaller) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }

    if ($iscc) {
        New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
        & $iscc "/DAppVersion=$Version" (Join-Path $repoRoot 'installer/SuDesApp.iss')
        if ($LASTEXITCODE -ne 0) { throw "ISCC gagal (exit $LASTEXITCODE)" }
        $setup = Get-ChildItem -LiteralPath $installerDir -Filter ("SuDesApp_{0}_Setup.exe" -f $Version) -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $setup) { throw "Installer tidak terbentuk di $installerDir" }
    } else {
        Write-Host 'Inno Setup (ISCC.exe) tidak ditemukan.' -ForegroundColor Yellow
        Write-Host 'Install gratis dari https://jrsoftware.org/isdl.php lalu jalankan ulang skrip ini.' -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '== Ringkasan ==' -ForegroundColor Cyan
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
$lines += ('Portable: `{0}`' -f (Split-Path -Leaf $zipPath))
Write-Host ("Portable  : " + $zipPath)

$manifestPatch = Join-Path $repoRoot 'artifacts/patch.json'
if (Test-Path -LiteralPath $manifestPatch) {
    $isiPatch = Get-Content -LiteralPath $manifestPatch -Raw | ConvertFrom-Json
    $lines += ('Pembaruan kecil: `{0}` (dari {1}, {2} berkas)' -f $isiPatch.versi, ($isiPatch.dariVersi -join ', '), $isiPatch.berkas.Count)
    if ($isiPatch.jenis -eq 'kecil') { $lines += ('Tambalan: `{0}`' -f $isiPatch.berkasPatch) }
    Write-Host ("Tambalan  : " + $manifestPatch) -ForegroundColor Green
}

# Pembaruan kecil: bandingkan hasil publish ini dengan hasil publish rilis lama.
if ($VersiLama) {
    if (-not $PublishLama) { throw "-PublishLama (folder hasil publish $VersiLama) wajib diisi bila -VersiLama dipakai" }
    $ringkasanArg = @()
    if ($Ringkasan) { $ringkasanArg = @('-Ringkasan', $Ringkasan) }
    $besarArg = @()
    if ($Besar) { $besarArg = @('-Besar') }

    & (Join-Path $PSScriptRoot 'Buat-Tambalan.ps1') `
        -Versi $Version -VersiLama $VersiLama `
        -PublishBaru $publishDir -PublishLama $PublishLama `
        -Keluaran (Join-Path $repoRoot 'artifacts') @ringkasanArg @besarArg
    if ($LASTEXITCODE -ne 0) { throw "Pembuatan tambalan gagal (exit $LASTEXITCODE)" }
}

$releaseNotes = Join-Path $repoRoot 'artifacts/RELEASE.md'
Set-Content -LiteralPath $releaseNotes -Value $lines -Encoding UTF8
Write-Host ("Catatan   : " + $releaseNotes)
Write-Host ''
Write-Host ("Upload manual: https://github.com/inomixstudio/SudesApp/releases/new  (tag v$Version)") -ForegroundColor Green
