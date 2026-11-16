# Uji GUI Otomatis (UI Automation) Guidelines

## Skrip
- Skrip uji halaman (`uji.ps1` + `cek-parse.ps1`) tidak ada di repo — folder `artifacts/` di-ignore, jadi skripnya dibuat sendiri bila perlu. Skrip menjalankan aplikasi, login, membuka halaman via UIA, mengekstrak teks, dan screenshot ke folder yang sama.

## PowerShell 5.1
- Windows PowerShell 5.1 membaca berkas tanpa BOM sebagai ANSI: satu karakter non-ASCII (mis. em dash UTF-8) berubah menjadi tanda kutip liar dan merusak parsing. Berkas `.ps1` harus ASCII murni; periksa dengan `python -c "print(sum(1 for b in open(f,'rb').read() if b>127))"`.
- PowerShell 5.1 tidak menerima `New-Object Tipe(arg1, arg2)` multiline; pakai `[Tipe]::new(a, b)`.

## UI Automation
- Dialog `MessageDialogWindow` ber-Owner diekspos UIA sebagai descendant jendela pemiliknya, bukan jendela tingkat atas root — cari dialog dari `$main.FindAll(Descendants, ControlTypeWindow)`, bukan dari root Children. Judulnya mengikuti jenis pesan ('Informasi', 'Peringatan'), bukan selalu 'Pesan'.
- Proxy UIA bisa basi di tengah alur (UI render ulang); sentraliskan pencarian elemen segar tiap percobaan dan sediakan fallback klik mouse (`SetCursorPos` + `mouse_event`) bila `InvokePattern` melempar.
- Pantau `error.log` di folder executable (ditulis `LogGalat.Tulis`): ukur ukurannya sebelum dan sesudah sesi uji untuk membuktikan "tanpa galat baru", bukan sekadar tidak ada dialog yang terlihat.
- Preferensi aplikasi ada di `%LOCALAPPDATA%\SuDesApp\login_prefs.json` (baris `kunci=nilai`). Baca untuk membuktikan keadaan tersimpan; kembalikan ke nilai awal setelah mengubahnya lewat UI — jangan tinggalkan keadaan uji di mesin pengguna.
- **Koordinat UIA adalah piksel fisik, bukan satuan WPF.** Aplikasi berjalan pada skala 125% (jendela `Width="400"` terukur 500 px), jadi semua `BoundingRectangle` 1,25× nilai DIP. Membandingkan ukuran UIA dengan angka di XAML (DIP) tanpa faktor ini menyimpangkan kesimpulan — mis. kotak 600 DIP tampak "749 px dan membengkak".
- Jangan menjalankan/menghentikan proses aplikasi pengguna; pakai `Get-Process` untuk membaca `MainWindowTitle` lalu cocokkan jendela lewat judul itu (nama window berubah mengikuti akun, mis. `SuDesApp - admin`).
