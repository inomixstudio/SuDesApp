# WPF Guidelines

## Perintah ViewModel
- Jangan memakai `ConfigureAwait(false)` di perintah ViewModel yang menyentuh state terikat UI setelah await: setter seperti `SedangUji` memicu `RaiseCanExecuteChanged` → `ButtonBase.UpdateCanExecute` → `DependencyObject.GetValue` yang melempar "The calling thread cannot access this object" dari thread latar. Galat ditangkap `AsyncRelayCommand.LaporkanGagal` dan memunculkan dialog "Perintah tidak selesai" yang menyesalkan (terbukti di `error.log` dari tombol Uji Koneksi halaman API sebelum diperbaiki).

## HTTP Listener
- `HttpListener` prefix `/sudes/api/v1/` tidak cocok dengan permintaan ke `/sudes/api/v1` tanpa segmen rute — endpoint uji koneksi harus mengarah ke `/status`, bukan alamat prefix.

## Viewbox
- `Viewbox` di dalam `ScrollViewer` (ruang tinggi tak terbatas) berskala memenuhi `MaxHeight`, dan `StretchDirection="DownOnly"` **tidak** mencegah pembesaran: isi yang lebih pendek dari `MaxHeight` justru diperbesar, sehingga elemen melebar melebihi lebar yang diharapkan. Ikat juga `Width` Viewbox pada lebar acuan supaya rasio lebar tidak pernah > 1.
- `MaxHeight` pada `Viewbox` tidak memotong: anak yang diskalakan bisa meluber di luar batas.
