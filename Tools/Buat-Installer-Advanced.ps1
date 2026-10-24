[CmdletBinding()]
param(
    [string]$Versi,
    [string]$FolderPublish,
    [string]$FolderOutput = 'artifacts/installer-ai-final',
    [switch]$LewatiPemeriksaanKredensial
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$cli = 'C:\Program Files (x86)\Caphyon\Advanced Installer 24.1\bin\x86\AdvancedInstaller.com'
$project = Join-Path $repoRoot 'installer/SuDesApp-Advanced-v24.aip'
$publishDir = if ($FolderPublish) { (Resolve-Path -LiteralPath $FolderPublish).Path } else { Join-Path $repoRoot 'artifacts/publish-ai-final' }
$outputDir = Join-Path $repoRoot $FolderOutput

if (-not (Test-Path -LiteralPath $cli)) { throw 'Advanced Installer 24.1 CLI tidak ditemukan.' }
if (-not $Versi) {
    $versiMatch = Select-String -Path (Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') -Pattern '<AssemblyVersion>(\d+\.\d+\.\d+)\.\d+</AssemblyVersion>'
    if (-not $versiMatch) { throw 'AssemblyVersion aplikasi tidak dapat ditentukan.' }
    $Versi = $versiMatch.Matches[0].Groups[1].Value
}
if ($Versi -notmatch '^\d+\.\d+\.\d+$') { throw "Versi tidak valid: $Versi" }

foreach ($path in @($project, $outputDir)) {
    if (Test-Path -LiteralPath $path) { throw "Jalur keluaran sudah ada; skrip tidak akan menimpa data: $path" }
}
if ($FolderPublish -and -not (Test-Path -LiteralPath $publishDir)) { throw "Folder publish tidak ditemukan: $publishDir" }

New-Item -ItemType Directory -Path $outputDir | Out-Null

try {
    if (-not $FolderPublish) {
        if (Test-Path -LiteralPath $publishDir) { throw "Folder publish sudah ada; tentukan -FolderPublish agar tidak menimpanya: $publishDir" }
        New-Item -ItemType Directory -Path $publishDir | Out-Null
        & dotnet publish (Join-Path $repoRoot 'SuDesApp.Wpf/SuDesApp.Wpf.csproj') `
            --configuration Release -r win-x64 --self-contained true `
            -p:Version=$Versi -p:AssemblyVersion="$Versi.0" -p:FileVersion="$Versi.0" `
            --output $publishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal (exit $LASTEXITCODE)." }
    }
    if (-not (Test-Path (Join-Path $publishDir 'SuDesApp.exe'))) { throw 'SuDesApp.exe tidak ada pada hasil publish.' }

    $cekArg = @{ Folder = $publishDir }
    if ($LewatiPemeriksaanKredensial) { $cekArg['IzinkanTanpaKredensial'] = $true }
    & (Join-Path $PSScriptRoot 'check-kredensial-google.ps1') @cekArg
    if ($LASTEXITCODE -ne 0) { throw 'Pemeriksaan kredensial Google pada hasil publish gagal.' }

    & $cli /newproject $project -type enterprise -lang en
    if ($LASTEXITCODE -ne 0) { throw 'Advanced Installer gagal membuat proyek.' }

    $folderName = Split-Path -Leaf $publishDir
    $commands = @(
        ,@('/SetVersion', $Versi),
        ,@('/SetProperty', 'ProductName=SuDesApp'),
        ,@('/SetProperty', 'Manufacturer=Sumberjaya Dev.'),
        ,@('/SetProperty', 'ARPURLINFOABOUT=https://github.com/inomixstudio/SuDesApp'),
        ,@('/SetProperty', 'ALLUSERS=2'),
        ,@('/SetProperty', 'MSIINSTALLPERUSER=1'),
        ,@('/SetAppdir', '-buildname', 'DefaultBuild', '-path', '[LocalAppDataFolder]Programs\SuDesApp'),
        ,@('/SetOutputType', 'ExeInside', '-buildname', 'DefaultBuild'),
        ,@('/SetOutputLocation', '-buildname', 'DefaultBuild', '-path', $outputDir),
        ,@('/SetPackageName', "SuDesApp_${Versi}.exe", '-exename', 'SuDesApp'),
        ,@('/AddFolder', 'APPDIR', $publishDir),
        ,@('/SetFolderOperations', 'APPDIR', '-overwrite_behavior', 'DoNotOverwrite', '-remove_folder', 'no'),
        ,@('/NewShortcut', '-name', 'SuDesApp', '-dir', 'ProgramMenuFolder', '-target', 'SuDesApp.exe'),
        ,@('/NewShortcut', '-name', 'SuDesApp', '-dir', 'DesktopFolder', '-target', 'SuDesApp.exe')
    )
    foreach ($command in $commands) {
        & $cli /edit $project @command
        if ($LASTEXITCODE -ne 0) { throw "Perintah proyek Advanced Installer gagal: $($command -join ' ')" }
    }

    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $payloadRows = $projectXml.SelectNodes('//COMPONENT[@cid="caphyon.advinst.msicomp.MsiFilesComponent"]/ROW')
    $publishFiles = @(Get-ChildItem -LiteralPath $publishDir -File -Recurse)
    $adaExe = @($payloadRows | Where-Object { $_.File -eq 'SuDesApp.exe' }).Count -gt 0
    if ($payloadRows.Count -ne $publishFiles.Count -or -not $adaExe) {
        throw "Payload Advanced Installer tidak lengkap (sumber=$($publishFiles.Count), proyek=$($payloadRows.Count), SuDesApp.exe=$adaExe)."
    }

    & $cli /build $project
    if ($LASTEXITCODE -ne 0) { throw 'Build paket Advanced Installer gagal.' }
    $setup = Join-Path $outputDir "SuDesApp_${Versi}.exe"
    if (-not (Test-Path -LiteralPath $setup)) { throw "Paket installer tidak ditemukan: $setup" }
    $targetSetup = Join-Path $outputDir "SuDesApp_${Versi}_Setup.exe"
    Copy-Item -LiteralPath $setup -Destination $targetSetup
    if ((Get-Item -LiteralPath $targetSetup).Length -lt 50MB) { throw 'Ukuran installer terlalu kecil; kemungkinan payload tidak terkemas.' }
    $setup = $targetSetup
    Write-Host "Installer dibuat: $setup" -ForegroundColor Green
}
catch {
    throw
}
