# ============================================================================
#  Pusat administer lisensi SuDesApp — kelola perangkat per desa.
#
#  MEMBUNGKUS endpoint /admin/* supaya operator tidak perlu menulis curl.
#  Inilah jalan pemulihan saat aktivasi sebuat desa ditolak karena kuota
#  perangkat penuh ("Batas N perangkat tercapai ... Hapus perangkat lain di
#  pusat administer") — mis. pelanggan mengganti komputer.
#
#  Kredensial operator dibaca dari -OperatorId / -Token atau environment
#  SUDES_ADMIN_OPERATOR_ID / SUDES_ADMIN_OPERATOR_TOKEN.
#
#  Contoh:
#    $env:SUDES_ADMIN_OPERATOR_ID = 'operator-dukungan'
#    $env:SUDES_ADMIN_OPERATOR_TOKEN = '<token pribadi>'
#    ./Tools/Admin-Lisensi.ps1 -Aksi pelanggan
#    ./Tools/Admin-Lisensi.ps1 -Aksi perangkat   -Kode SUDES-0001
#    ./Tools/Admin-Lisensi.ps1 -Aksi hapus       -Kode SUDES-0001 -Perangkat pc-1
#    ./Tools/Admin-Lisensi.ps1 -Aksi reset       -Kode SUDES-0001
#    ./Tools/Admin-Lisensi.ps1 -Aksi batas       -Kode SUDES-0001 -Batas 3
#    ./Tools/Admin-Lisensi.ps1 -Aksi pembayaran  -Kode SUDES-0001
#    ./Tools/Admin-Lisensi.ps1 -Aksi daftar      -Kode SUDES-0002 -NamaDesa 'Desa Baru' -Batas 3
#    ./Tools/Admin-Lisensi.ps1 -Aksi ubah        -Kode SUDES-0001 -NamaDesa 'Desa Sukamaju' -Kontak '0812...'
#    ./Tools/Admin-Lisensi.ps1 -Aksi hapus-desa  -Kode SUDES-0002
#    ./Tools/Admin-Lisensi.ps1 -Aksi hapus-paksa -Kode SUDES-0001 -Konfirmasi SUDES-0001
#    ./Tools/Admin-Lisensi.ps1 -Aksi ekspor-desa
#    ./Tools/Admin-Lisensi.ps1 -Aksi ekspor-bayar -Kode SUDES-0001
#    ./Tools/Admin-Lisensi.ps1 -Aksi rekonsiliasi
# ============================================================================

[CmdletBinding()]
param(
    [string]$Server = 'http://localhost:5000',
    [string]$OperatorId = $env:SUDES_ADMIN_OPERATOR_ID,
    [string]$Token = $env:SUDES_ADMIN_OPERATOR_TOKEN,
    [ValidateSet('pelanggan', 'perangkat', 'hapus', 'reset', 'batas', 'pembayaran',
        'daftar', 'ubah', 'hapus-desa', 'hapus-paksa', 'ekspor-desa', 'ekspor-bayar',
        'rekonsiliasi')]
    [string]$Aksi = 'pelanggan',
    [string]$Kode,
    [string]$Perangkat,
    [string]$NamaDesa,
    [string]$Kontak,
    [string]$Konfirmasi,
    [int]$Batas
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OperatorId) -or [string]::IsNullOrWhiteSpace($Token)) {
    Write-Host 'ID operator/token belum diisi. Set SUDES_ADMIN_OPERATOR_ID dan SUDES_ADMIN_OPERATOR_TOKEN.' -ForegroundColor Red
    exit 1
}

if ($Aksi -in @('perangkat', 'hapus', 'reset', 'batas', 'pembayaran', 'ubah', 'hapus-desa') -and
    [string]::IsNullOrWhiteSpace($Kode)) {
    Write-Host ("Aksi '{0}' butuh -Kode <kode aktivasi>." -f $Aksi) -ForegroundColor Red
    exit 1
}
if ($Aksi -in @('daftar', 'ubah') -and [string]::IsNullOrWhiteSpace($NamaDesa)) {
    Write-Host ("Aksi '{0}' butuh -NamaDesa <nama desa>." -f $Aksi) -ForegroundColor Red
    exit 1
}
if ($Aksi -eq 'hapus' -and [string]::IsNullOrWhiteSpace($Perangkat)) {
    Write-Host 'Aksi hapus butuh -Perangkat <id>. Pakai -Aksi reset untuk menghapus semua.' -ForegroundColor Red
    exit 1
}
if ($Aksi -eq 'batas' -and $Batas -lt 1) {
    Write-Host 'Aksi batas butuh -Batas <1..100>.' -ForegroundColor Red
    exit 1
}

# Pesan galat dari server (bentuk { pesan } / { title }) dibaca dari body
# respons, bukan hanya dari teks exception PowerShell.
function Ambil-PesanGalat($galat) {
    $resp = $galat.Exception.Response
    if ($null -eq $resp) { return $galat.Exception.Message }
    try {
        $stream = $resp.GetResponseStream()
        if ($null -eq $stream) { return $galat.Exception.Message }
        $reader = New-Object System.IO.StreamReader($stream)
        $teks = $reader.ReadToEnd()
        $reader.Dispose()
        if ($teks) {
            try {
                $obj = $teks | ConvertFrom-Json
                if ($obj.pesan) { return $obj.pesan }
                if ($obj.detail) { return $obj.detail }
                return $teks
            }
            catch { return $teks }
        }
    }
    catch { }
    return $galat.Exception.Message
}

function Panggil([string]$Metode, [string]$Jalur, $Body) {
    $uri = $Server.TrimEnd('/') + $Jalur
    $header = @{ Authorization = "Bearer $Token"; 'X-Admin-Operator' = $OperatorId }
    try {
        if ($null -ne $Body) {
            return Invoke-RestMethod -Method $Metode -Uri $uri -Headers $header `
                -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Compress)
        }
        return Invoke-RestMethod -Method $Metode -Uri $uri -Headers $header
    }
    catch {
        Write-Host ("Gagal: {0}" -f (Ambil-PesanGalat $_)) -ForegroundColor Red
        exit 1
    }
}

# Unduh berkas CSV mentah. TIDAK lewat Invoke-RestMethod: responsnya bukan JSON
# (teks CSV + BOM), dan ConvertFrom-Json akan menganggapnya galat.
function Unduh-Csv([string]$Jalur, [string]$NamaBerkas) {
    $uri = $Server.TrimEnd('/') + $Jalur
    $header = @{ Authorization = "Bearer $Token"; 'X-Admin-Operator' = $OperatorId }
    try {
        Invoke-WebRequest -Method Get -Uri $uri -Headers $header -OutFile $NamaBerkas `
            -UseBasicParsing | Out-Null
    }
    catch {
        Write-Host ("Gagal mengunduh: {0}" -f (Ambil-PesanGalat $_)) -ForegroundColor Red
        if (Test-Path $NamaBerkas) { Remove-Item $NamaBerkas -Force }
        exit 1
    }
    if (-not (Test-Path $NamaBerkas)) {
        Write-Host 'Gagal mengunduh: berkas tidak tersimpan.' -ForegroundColor Red
        exit 1
    }
    $ukuran = (Get-Item $NamaBerkas).Length
    if ($ukuran -eq 0) {
        Write-Host 'Gagal mengunduh: berkas kosong.' -ForegroundColor Red
        exit 1
    }
    Write-Host ("Tersimpan: {0} ({1} byte)" -f $NamaBerkas, $ukuran) -ForegroundColor Green
}

$kodeAman = if ($Kode) { [uri]::EscapeDataString($Kode) } else { '' }

switch ($Aksi) {
    'pelanggan' {
        $hasil = Panggil 'GET' '/admin/pelanggan'
        Write-Host ("Desa terdaftar: {0}" -f $hasil.jumlah) -ForegroundColor Cyan
        $hasil.pelanggan | Format-Table `
            @{ L = 'Kode'; E = { $_.kodeAktivasi } },
            @{ L = 'Desa'; E = { $_.namaDesa } },
            @{ L = 'Perangkat'; E = { "$($_.jumlahPerangkat)/$($_.maxPerangkat)" } },
            @{ L = 'Penuh'; E = { if ($_.penuh) { 'YA' } else { '' } } },
            @{ L = 'Paket'; E = { $_.paket } },
            @{ L = 'BerlakuSampai'; E = { $_.berlakuSampai } } -AutoSize
    }
    'perangkat' {
        $hasil = Panggil 'GET' "/admin/pelanggan/$kodeAman/perangkat"
        Write-Host ("Perangkat desa '{0}': {1}" -f $Kode, $hasil.jumlah) -ForegroundColor Cyan
        if ($hasil.jumlah -gt 0) {
            $hasil.perangkat | Format-Table `
                @{ L = 'IdPerangkat'; E = { $_.id } },
                @{ L = 'TerakhirLihat'; E = { $_.terakhirLihat } } -AutoSize
        }
    }
    'hapus' {
        $idAman = [uri]::EscapeDataString($Perangkat)
        $hasil = Panggil 'DELETE' "/admin/pelanggan/$kodeAman/perangkat/$idAman"
        Write-Host ("{0} (dihapus: {1})" -f $hasil.pesan, $hasil.dihapus) -ForegroundColor Green
    }
    'reset' {
        $hasil = Panggil 'DELETE' "/admin/pelanggan/$kodeAman/perangkat"
        Write-Host ("{0} (dihapus: {1})" -f $hasil.pesan, $hasil.dihapus) -ForegroundColor Green
    }
    'batas' {
        $hasil = Panggil 'POST' "/admin/pelanggan/$kodeAman/batas" @{ batas = $Batas }
        Write-Host ("Batas perangkat desa '{0}' sekarang {1}." -f $hasil.kode, $hasil.maxPerangkat) -ForegroundColor Green
    }
    'pembayaran' {
        $hasil = Panggil 'GET' "/admin/pelanggan/$kodeAman/pembayaran"
        Write-Host ("Riwayat pembayaran desa '{0}': {1} (total {2} rupiah)" -f $Kode, $hasil.jumlah, $hasil.total) -ForegroundColor Cyan
        if ($hasil.jumlah -gt 0) {
            $hasil.pembayaran | Format-Table `
                @{ L = 'OrderId'; E = { $_.orderId } },
                @{ L = 'Paket'; E = { $_.paket } },
                @{ L = 'Nominal'; E = { $_.nominal } },
                @{ L = 'Status'; E = { $_.status } },
                @{ L = 'Dicatat'; E = { $_.dicatat } } -AutoSize
        }
    }
    'daftar' {
        $maks = if ($Batas -ge 1) { $Batas } else { 2 }
        $hasil = Panggil 'POST' '/admin/pelanggan' @{
            kodeAktivasi = $Kode; namaDesa = $NamaDesa; kontak = $Kontak; maxPerangkat = $maks
        }
        Write-Host ("Desa '{0}' ({1}) terdaftar. Langganan aktif setelah pembayaran pertama masuk." -f `
            $hasil.namaDesa, $hasil.kodeAktivasi) -ForegroundColor Green
    }
    'ubah' {
        $hasil = Panggil 'PUT' "/admin/pelanggan/$kodeAman" @{ namaDesa = $NamaDesa; kontak = $Kontak }
        Write-Host ("{0} ({1})" -f $hasil.pesan, $Kode) -ForegroundColor Green
    }
    'hapus-desa' {
        $hasil = Panggil 'DELETE' "/admin/pelanggan/$kodeAman"
        Write-Host $hasil.pesan -ForegroundColor Green
    }
    'hapus-paksa' {
        # Penghapusan paksa ikut membuang jejak uang (tagihan & pembayaran), jadi
        # konfirmasinya harus kode aktivasi yang sama — bukan sekadar kata "iya".
        # Server juga memeriksa keduanya identik; ini hanya agar pesan galatnya
        # muncul sebelum permintaan jaringan dikirim.
        if ([string]::IsNullOrWhiteSpace($Konfirmasi)) {
            Write-Host ('Aksi hapus-paksa butuh -Konfirmasi <kode aktivasi> ' +
                'karena tagihan & pembayaran desa akan ikut terhapus.') -ForegroundColor Red
            exit 1
        }
        if ($Konfirmasi.Trim() -ne $Kode.Trim()) {
            Write-Host 'Konfirmasi harus sama dengan -Kode.' -ForegroundColor Red
            exit 1
        }
        $hasil = Panggil 'DELETE' "/admin/pelanggan/${kodeAman}?konfirmasi=${kodeAman}"
        if ($hasil.pembayaran -gt 0) {
            Write-Host $hasil.pesan -ForegroundColor Yellow
            Write-Host '  Catatan: baris pembayaran sudah musnah dan tidak bisa dipulihkan.' -ForegroundColor Yellow
        }
        else {
            Write-Host $hasil.pesan -ForegroundColor Green
        }
    }
    'ekspor-desa' {
        Unduh-Csv '/admin/ekspor/desa.csv' ("daftar-desa-{0}.csv" -f (Get-Date -Format 'yyyy-MM-dd'))
    }
    'ekspor-bayar' {
        if ([string]::IsNullOrWhiteSpace($Kode)) {
            Write-Host 'Aksi ekspor-bayar butuh -Kode <kode aktivasi>.' -ForegroundColor Red
            exit 1
        }
        Unduh-Csv "/admin/ekspor/pembayaran/$kodeAman.csv" ("pembayaran-{0}.csv" -f $Kode)
    }
    'rekonsiliasi' {
        $hasil = Panggil 'POST' '/admin/rekonsiliasi'
        Write-Host ("Rekonsiliasi: diperiksa {0}, diselesaikan {1}, sudah {2}, belum bayar {3}, perlu diperiksa {4}, tidak pasti {5}" -f `
            $hasil.diperiksa, $hasil.diselesaikan, $hasil.sudahDiselesaikan, `
            $hasil.belumBayar, $hasil.perluDiperiksa, $hasil.tidakPasti) -ForegroundColor Green
    }
}

exit 0
