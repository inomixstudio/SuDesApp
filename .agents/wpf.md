# WPF Guidelines

## Perintah ViewModel
- Jangan memakai `ConfigureAwait(false)` di perintah ViewModel yang menyentuh state terikat UI setelah await: setter seperti `SedangUji` memicu `RaiseCanExecuteChanged` → `ButtonBase.UpdateCanExecute` → `DependencyObject.GetValue` yang melempar "The calling thread cannot access this object" dari thread latar. Galat ditangkap `AsyncRelayCommand.LaporkanGagal` dan memunculkan dialog "Perintah tidak selesai" yang menyesalkan (terbukti di `error.log` dari tombol Uji Koneksi halaman API sebelum diperbaiki).

## HTTP Listener
- `HttpListener` prefix `/sudes/api/v1/` tidak cocok dengan permintaan ke `/sudes/api/v1` tanpa segmen rute — endpoint uji koneksi harus mengarah ke `/status`, bukan alamat prefix.

## Viewbox
- `Viewbox` di dalam `ScrollViewer` (ruang tinggi tak terbatas) berskala memenuhi `MaxHeight`, dan `StretchDirection="DownOnly"` **tidak** mencegah pembesaran: isi yang lebih pendek dari `MaxHeight` justru diperbesar, sehingga elemen melebar melebihi lebar yang diharapkan. Ikat juga `Width` Viewbox pada lebar acuan supaya rasio lebar tidak pernah > 1.
- `MaxHeight` pada `Viewbox` tidak memotong: anak yang diskalakan bisa meluber di luar batas.

## Label kontrol: jangan ukur tanpa batas lebar
- Akar `ControlTemplate` yang berupa `StackPanel Orientation="Horizontal"` mengukur anak berikutnya dengan lebar **tak hingga**. Konten yang mengandalkan `TextWrapping="Wrap"` tidak pernah membungkus: teks meluap ke kanan (terpotong tepi jendela), dan karena `SizeToContent="Height"` menghitung tinggi dari ukuran satu baris itu, baris selanjutnya ikut terpotong di bawah. Ini yang membuat kalimat persetujuan di `PersetujuanDokumenWindow` terputus dan tombolnya tidak tampil.
- Pakai `BulletDecorator` (seperti template `RadioButton`) atau `Grid` dengan `ColumnDefinition Width="*"` untuk bagian konten; keduanya memberi sisa lebar sehingga pembungkusan bekerja.
- `VerticalAlignment` kotak/label di template sebaiknya `{TemplateBinding VerticalContentAlignment}` supaya penempat label multi-baris (mis. `VerticalContentAlignment="Top"`) dihormati.
- Penjaga: `SuDesApp.Tests/UI/KotakCentangMembungkusTests.cs`. Template `CheckBox`/`RadioButton` di `Themes/ThemeStyles.xaml` berlaku untuk **seluruh** aplikasi (46 `CheckBox`), jadi perubahan di situ diuji lewat XAML, bukan satu halaman.
