[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serverDir = Join-Path $PSScriptRoot '..\SuDesApp.Server'
$serverDir = [System.IO.Path]::GetFullPath($serverDir)
$target = Join-Path $serverDir 'appsettings.lokal.json'

Write-Host 'Isi host persis dari Supabase Dashboard > Connect > Session pooler.'
$hostPooler = (Read-Host 'Session pooler host').Trim()
$projectRef = (Read-Host 'Project ref (huruf/angka)').Trim()
$dbPasswordSecure = Read-Host 'Password role sudes_server (bukan password postgres)' -AsSecureString
$migrateChoice = Read-Host 'Simpan password postgres untuk migrasi otomatis? (y/N)'

if ([string]::IsNullOrWhiteSpace($hostPooler) -or
    $hostPooler -match '://' -or $hostPooler -match '/') {
    throw 'Host harus nama host saja (tanpa https://, port, atau path).'
}
if ($projectRef -notmatch '^[a-z0-9]+$') { throw 'Project ref hanya boleh berisi huruf kecil dan angka.' }
if ($dbPasswordSecure.Length -lt 20) { throw 'Gunakan password role sudes_server yang acak dan minimal 20 karakter.' }

$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($dbPasswordSecure)
try { $dbPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }

$migratePassword = ''
if ($migrateChoice -match '^(y|ya)$') {
    Write-Warning 'Password postgres memberi hak DDL luas. Pakai hanya untuk migrasi lokal lalu hapus dari berkas.'
    $migrateSecure = Read-Host 'Password role migrasi postgres' -AsSecureString
    $ptrMigrate = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($migrateSecure)
    try { $migratePassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptrMigrate) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptrMigrate) }
}

$config = [ordered]@{
    Supabase = [ordered]@{
        Host = $hostPooler
        Port = 5432
        User = "sudes_server.$projectRef"
        DbPassword = $dbPassword
        SslMode = 'Require'
        MigrateUser = "postgres.$projectRef"
        MigratePassword = $migratePassword
    }
}
$json = $config | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($target, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

# Restrict the local file to the current Windows account. It is also ignored by Git
# and excluded from build/publish output by the project file.
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $target /inheritance:r /grant:r "$identity`:(R,W)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Konfigurasi tertulis, tetapi ACL berkas tidak berhasil dibatasi.' }

$dbPassword = $null
$migratePassword = $null
$json = $null
Write-Host 'Konfigurasi lokal tersimpan. Tidak ada nilai rahasia yang ditampilkan.'
Write-Host 'Jalankan: dotnet run --project SuDesApp.Server --no-launch-profile'
Write-Host 'Startup server harus mencatat bahwa isolasi RLS terverifikasi sebelum menerima trafik.'
