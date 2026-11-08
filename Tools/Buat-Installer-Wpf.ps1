[CmdletBinding()]
param(
    [string]$Versi = '2.12.0',
    [string]$Rku = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$publishDir = Join-Path $repoRoot "artifacts/publish-$Versi"
$payloadZip = Join-Path $repoRoot "artifacts/payload-$Versi.zip"
$outDir = Join-Path $repoRoot "artifacts/installer-wpf"
$setupName = "SuDesApp_${Versi}_Setup.exe"

Write-Host "== Publish SuDesApp $Versi =="
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish (Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') --configuration Release -r $Rku --self-contained true -p:Version=$Versi -p:AssemblyVersion="$Versi.0" -p:FileVersion="$Versi.0" -p:DebugType=none -p:DebugSymbols=false --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal" }
if (-not (Test-Path (Join-Path $publishDir 'SuDesApp.exe'))) { throw "SuDesApp.exe tidak ditemukan" }

Write-Host "== ZIP payload =="
Remove-Item -Force $payloadZip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $payloadZip
Copy-Item -LiteralPath $payloadZip -Destination (Join-Path $repoRoot 'SuDesApp.Installer/Payload.zip') -Force
Write-Host "Payload: $payloadZip"

Write-Host "== Build installer WPF (single-file) =="
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
dotnet publish (Join-Path $repoRoot 'SuDesApp.Installer/SuDesApp.Installer.csproj') --configuration Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false --output $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish installer gagal" }

$exe = Join-Path $outDir 'SuDesAppSetup.exe'
if (-not (Test-Path $exe)) { throw "SuDesAppSetup.exe tidak ditemukan" }
$target = Join-Path $outDir $setupName
Copy-Item -LiteralPath $exe -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$target.sha256", "$hash  $setupName`n", [Text.Encoding]::ASCII)
Write-Host "Installer: $target" -ForegroundColor Green
Remove-Item -Force (Join-Path $repoRoot 'SuDesApp.Installer/Payload.zip') -ErrorAction SilentlyContinue
