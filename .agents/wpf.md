# WPF Guidelines

## Perintah ViewModel
- Jangan memakai `ConfigureAwait(false)` di perintah ViewModel yang menyentuh state terikat UI setelah await: setter seperti `SedangUji` memicu `RaiseCanExecuteChanged` → `ButtonBase.UpdateCanExecute` → `DependencyObject.GetValue` yang melempar "The calling thread cannot access this object" dari thread latar. Galat ditangkap `AsyncRelayCommand.LaporkanGagal` dan memunculkan dialog "Perintah tidak selesai" yang menyesalkan (terbukti di `error.log` dari tombol Uji Koneksi halaman API sebelum diperbaiki).

## HTTP Listener
- `HttpListener` prefix `/sudes/api/v1/` tidak cocok dengan permintaan ke `/sudes/api/v1` tanpa segmen rute — endpoint uji koneksi harus mengarah ke `/status`, bukan alamat prefix.
