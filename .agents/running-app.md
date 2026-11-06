# Menjalankan Aplikasi Guidelines

## Executable
- Executable hasil build bernama `SuDesApp.exe`, bukan `SuDesApp.Wpf.exe`, di `SuDesApp.Wpf/bin/<Config>/net10.0-windows/`.

## PowerShell
- Di Git Bash lingkungan ini `powershell` tidak ada di PATH; panggil langsung `/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe`. Untuk smoke test alihkan log ke berkas dan cek proses dengan `ps -W | grep net10.0-windows/SuDesApp`.

## Database & Login
- `desa.db` mengikuti folder kerja executable (koneksi relatif di `appsettings.json`), jadi build baru mengisinya dengan penanda `DesaContoh` ("Nama Desa", "Nama Kepala Desa") sampai Pengaturan Surat diisi. Membaca `desa.db` build debug adalah cara melihat data yang sedang dipakai.
- Akun login build debug: `admin`/`admin` (tersimpan di tabel `Pengguna` dengan PBKDF2; 3 kali salah akan terkunci — verifikasi hashnya dulu dengan `hashlib.pbkdf2_hmac('sha256', sandi, salt, 120000, 32)` sebelum mengetik di aplikasi).
