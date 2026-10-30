# ============================================================================
#  Siapkan paket pembanding tambalan dari sebuah tag rilis.
#
#  Tambalan hanya bisa dibangun bila isi folder publish versi asal diketahui.
#  Rilis lama kadang tidak menyediakan zip portable (mis. v2.7.2 hanya punya
#  tag), sedangkan publish-release.ps1 hanya bisa mengunduh yang memang
#  terlampir di halaman rilis. Skrip ini membangun zip itu dari tag setempat,
#  dengan pengaturan publish yang sama, sehingga tambalan bisa dibuat ulang di
#  mesin mana pun yang punya repo ini.
#
#  Hasilnya: artifacts/publish-lama-<versi>/ dan
#            artifacts/SuDesApp_<versi>_win-x64.zip (nama yang dicari
#            publish-release.ps1 saat -VersiLama dipakai).
#
#  Contoh:
#    ./Tools/siapkan-pembanding.ps1 -Versi 2.7.2
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Versi,
    # Biarkan folder publish hasil build (bila ingin diperiksa manual atau
    # langsung dipakai sebagai -PublishLama).
    [switch]$SimpanPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$Versi = $Versi.Trim().TrimStart('v', 'V')
if ($Versi -notmatch '^\d+\.\d+\.\d+$') { throw "Versi tidak valid: '$Versi' (harus x.y.z)" }
$tag = 'v' + $Versi

& git -c ('safe.directory=' + $repoRoot) rev-parse --verify --quiet ("refs/tags/" + $tag) | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Tag $tag tidak ada di repo ini. Ambil dulu tag-nya (git fetch --tags) atau pilih versi lain."
}

# Worktree di folder sementara OS: hasil checkout tag penuh tidak boleh mengotori
# working tree repo utama yang sedang dipakai rilis.
$sementara = Join-Path ([System.IO.Path]::GetTempPath()) ("sudesapp-src-" + $Versi)
$publish = Join-Path $repoRoot ("artifacts/publish-lama-" + $Versi)
$zip = Join-Path $repoRoot ("artifacts/SuDesApp_{0}_win-x64.zip" -f $Versi)
$assemblyVersion = (($Versi.Split('.') + @('0'))[0..3] -join '.')

if (Test-Path -LiteralPath $sementara) { & git worktree remove --force $sementara | Out-Null }
& git -c ('safe.directory=' + $repoRoot) worktree add --detach $sementara $tag
if ($LASTEXITCODE -ne 0) { throw "Gagal membuat worktree untuk $tag." }

try {
    Write-Host ("== Publish pembanding {0} ({1}) ==" -f $Versi, $tag) -ForegroundColor Cyan
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    & dotnet publish (Join-Path $sementara 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Versi -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish gagal untuk pembanding.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'SuDesApp.exe'))) {
        throw 'SuDesApp.exe tidak ditemukan di hasil publish pembanding.'
    }
}
finally {
    & git -c ('safe.directory=' + $repoRoot) worktree remove --force $sementara 2>$null | Out-Null
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $zip) | Out-Null
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("Pembanding : {0}" -f $zip) -ForegroundColor Green
Write-Host ("SHA-256    : {0}" -f $hash)
if ($SimpanPublish) {
    Write-Host ("Publish    : {0}" -f $publish)
} else {
    Remove-Item -LiteralPath $publish -Recurse -Force
}

exit 0
