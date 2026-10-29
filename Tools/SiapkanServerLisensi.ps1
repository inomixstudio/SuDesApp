# Jaminan koneksi server lisensi (menu Langganan + Pusat Administrasi).
#
# Masalah yang berulang dan ditangani otomatis skrip ini:
#   1. Instans server LAMA menempati 127.0.0.1:5110 dengan konfigurasi
#      lama (appsettings.lokal.json tidak pernah di-reload), sehingga
#      token operator ditolak (401) meski file sudah diedit.
#   2. Preferensi %LOCALAPPDATA%\SuDesApp\login_prefs.json buah
#      halaman Langganan (kunci lisensi_url_server) menimpa alamat
#      appsettings ketika keduanya loopback dan berbeda - aplikasi
#      memanggil alamat salah.
#
# Sejak aplikasi memegang siklus hidup server lisensi lokal (halaman
# Langganan: nyalakan bersama aplikasi, matikan saat keluar/logoff), skrip
# ini hanya untuk MEMERIKSA/MEMPERBAIKI koneksi secara manual - mis. saat
# server sudah berjalan tetapi token operator ditolak.
#
# Pemakaian (bila perlu memeriksa, dan setiap kali koneksi hilang):
#   powershell -ExecutionPolicy Bypass -File Tools\SiapkanServerLisensi.ps1
#
# Keluar kode 0 = koneksinya terbukti hidup end-to-end.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$serverDir = Join-Path $root 'SuDesApp.Server'
$port = 5110
$baseUrl = "http://127.0.0.1:$port"

function Write-Gagal($pesan) {
    Write-Host "  GAGAL: $pesan" -ForegroundColor Red
    exit 1
}

# ---- 1. Alamat server menurut appsettings.json aplikasi WPF ----
$konfig = Get-Content (Join-Path $root 'appsettings.json') -Raw | ConvertFrom-Json
$alamatKonfig = [string]$konfig.Lisensi.UrlServer

# ---- 2. Bersihkan preferensi alamat yang usang ----
$prefPath = Join-Path $env:LOCALAPPDATA 'SuDesApp\login_prefs.json'
if (Test-Path $prefPath) {
    $baris = @(Get-Content $prefPath -ErrorAction SilentlyContinue)
    $prefUrl = $null
    foreach ($l in $baris) {
        if ($l -match '^\s*lisensi_url_server\s*=\s*(.+)$') { $prefUrl = $Matches[1].Trim() }
    }
    if ($prefUrl -and ($prefUrl -ne $alamatKonfig)) {
        # Hanya SENTUH preferensi saat keduanya loopback dan berbeda:
        # preferensi memang boleh menunjuk server produksi (https),
        # itu tidak pernah dibersihkan otomatis.
        $prefLoopback = $prefUrl -match '^http://(localhost|127\.0\.0\.1)'
        $konfigLoopback = $alamatKonfig -match '^http://(localhost|127\.0\.0\.1)'
        if ($prefLoopback -and $konfigLoopback) {
            $tersisa = $baris | Where-Object { $_ -notmatch '^\s*lisensi_url_server\s*=' }
            Set-Content -Path $prefPath -Value $tersisa -Encoding UTF8
            Write-Host ("  Dibersihkan: preferensi alamat usang '{0}' dihapus, aplikasi memakai config ({1})." -f $prefUrl, $alamatKonfig) -ForegroundColor Yellow
        }
    }
}

# ---- 3. Operasi port: identifikasi pemegang 5110 ----
function Get-PemegangPort {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
}

# ---- 4. Keadaan server yang berjalan saat ini ----
function Test-Server([bool]$cekOperator) {
    try {
        $h = Invoke-RestMethod -Uri "$baseUrl/healthz" -TimeoutSec 4
        if ($h.status -ne 'sehat') { return 'tidak-sehat' }
        if (-not $cekOperator) { return 'sehat' }
        $lokal = Get-Content (Join-Path $serverDir 'appsettings.lokal.json') -Raw -ErrorAction Stop | ConvertFrom-Json
        $op = $lokal.Lisensi.AdminOperators | Select-Object -First 1
        if (-not $op -or -not $op.Token) { return 'tanpa-operator' }
        $sesi = Invoke-RestMethod -Uri "$baseUrl/admin/sesi" -Headers @{
            'X-Admin-Operator' = $op.Id
            'Authorization'    = "Bearer $($op.Token)"
        } -TimeoutSec 8
        if ($sesi.id) { return 'sehat-auth' } else { return 'tidak-sehat' }
    } catch { return 'tak-dijangkau' }
}

function Matikan-YangMemegangPort {
    foreach ($procId in @(Get-PemegangPort)) {
        $info = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($info) {
            Write-Host ("  Menghentikan instans lama: {0} (PID {1})." -f $info.ProcessName, $procId) -ForegroundColor Yellow
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
    if (Get-PemegangPort) { Start-Sleep -Seconds 2 }
}

# ---- 5. Putuskan: sehat sekarang, atau restart paksa ----
$keadaan = Test-Server $true
if ($keadaan -ne 'sehat-auth') {
    if ($keadaan -eq 'tak-dijangkau') {
        Write-Host 'Server lisensi belum berjalan. Menjalankan…'
    } else {
        Write-Host ("Instans berjalan BUKAN konfigurasi terbaru (keadaan: {0}). Dihentikan dan dijalankan ulang…" -f $keadaan) -ForegroundColor Yellow
        Matikan-YangMemegangPort
    }

    $log = Join-Path $env:TEMP 'sudes-server-lisensi.log'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    Start-Process -FilePath 'dotnet' -ArgumentList 'run' -WorkingDirectory $serverDir `
        -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    # Wait dotnet start up to 60 seconds.
    $siap = $false
    for ($i = 0; $i -lt 30 -and -not $siap; $i++) {
        Start-Sleep -Seconds 2
        $siap = ((Test-Server $false) -eq 'sehat')
    }
    if (-not $siap) {
        Matikan-YangMemegangPort
        Write-Gagal ("Server tidak bisa dibangunkan dalam 60 detik. Lihat log: {0}" -f $log)
    }
    $keadaan = Test-Server $true
}

# ---- 6. Verifikasi koneksinya end-to-end ----
if ($keadaan -ne 'sehat-auth') {
    Matikan-YangMemegangPort
    Write-Gagal 'Auth operator tetap ditolak setelah restart. Periksa appsettings.lokal.json (Lisensi:AdminOperators).'
}

$kunciServer = (Invoke-RestMethod -Uri "$baseUrl/kunci-publik" -TimeoutSec 8).kunci
$kunciAplikasi = [string]$konfig.Lisensi.KunciPublik
$paket = (Invoke-RestMethod -Uri "$baseUrl/paket" -TimeoutSec 8).Count

Write-Host ''
Write-Host "KONEKSI SERVER LISENSI TERVERIFIKASI  ($baseUrl)" -ForegroundColor Green
Write-Host "  /healthz      : sehat"
Write-Host "  /kunci-publik : $kunciServer"
if ($kunciServer -ne $kunciAplikasi) {
    Write-Gagal 'KunciPublik di appsettings.json TIDAK sama dengan /kunci-publik server. Aktivasi lisensi akan gagal.'
}
Write-Host "  kunci = appsettings   : cocok"
Write-Host "  /paket                : $paket paket"
Write-Host ''
Write-Host 'Menu Langganan dan Pusat Administrasi siap dipakai di aplikasi.'
