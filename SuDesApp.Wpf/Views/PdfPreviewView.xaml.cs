using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SudesPdf;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>Pratinjau PDF multi-halaman berbasis mesin SudesPdf sendiri.</summary>
    public partial class PdfPreviewView : UserControl
    {
        private Document? _document;
        private int _loadGeneration;
        private string _title = string.Empty;
        private string? _currentPdfPath;
        private long _cachedLength = -1;
        private DateTime _cachedLastWrite = DateTime.MinValue;
        private double _scale = 1.0d;

        public PdfPreviewView()
        {
            InitializeComponent();
            DataContextChanged += PdfPreviewView_DataContextChanged;
        }

        private void PdfPreviewView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            // Saat navigasi ke PDF baru (termasuk saat elemen direcycle oleh
            // ContentPresenter), muat ulang konten.
            if (IsLoaded)
            {
                _ = LoadDocumentAsync();
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => _ = LoadDocumentAsync();

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _loadGeneration++;
            _ = DisposeDocumentAsync();
            _currentPdfPath = null;
        }

        private async Task LoadDocumentAsync(bool force = false)
        {
            if (force) _currentPdfPath = null;
            int generation = ++_loadGeneration;
            if (DataContext is not PdfPreviewViewModel viewModel) return;

            // Skip reload hanya bila path sama DAN file belum berubah (ukuran+mtime).
            // Tanpa cek isi, PDF hasil edit yang ditulis ulang ke nama file yang sama
            // akan tampil sebagai dokumen basi.
            if (_currentPdfPath == viewModel.PdfPath && _document is not null)
            {
                try
                {
                    var info = new FileInfo(viewModel.PdfPath!);
                    if (_cachedLength == info.Length && _cachedLastWrite == info.LastWriteTimeUtc) return;
                }
                catch (Exception) { /* berkas berubah/terkunci: lanjutkan muat ulang */ }
            }

            _title = viewModel.Title;
            _currentPdfPath = viewModel.PdfPath;
            _cachedLength = -1;
            _cachedLastWrite = DateTime.MinValue;
            await DisposeDocumentAsync();

            if (viewModel.IsMissing || string.IsNullOrEmpty(viewModel.PdfPath))
            {
                ShowMissing();
                return;
            }

            try
            {
                var document = await Document.LoadAsync(viewModel.PdfPath);
                if (generation != _loadGeneration || !IsLoaded)
                {
                    await document.DisposeAsync();
                    return;
                }
                _document = document;
                var info = new FileInfo(viewModel.PdfPath);
                _cachedLength = info.Exists ? info.Length : -1;
                _cachedLastWrite = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                TampilkanDokumen();
                await FitWidthAsync();
            }
            catch (Exception)
            {
                if (generation == _loadGeneration) ShowMissing();
            }
        }

        /// <summary>
        /// Kembalikan tampilan ke keadaan "dokumen ada". ShowMissing hanya
        /// mematikan, tidak pernah menyalakan kembali — padahal PdfPreviewView
        /// dipakai ulang untuk tiap dokumen yang dinavigasi. Tanpa ini, satu PDF
        /// yang gagal dimuat membuat panel "dokumen tidak ditemukan" dan tombol
        /// Cetak/Ekspor/zoom mati PERMANEN untuk sisa sesi.
        /// </summary>
        private async Task DisposeDocumentAsync()
        {
            var document = _document;
            _document = null;
            if (document is not null) await document.DisposeAsync();
        }

        private void TampilkanDokumen()
        {
            ScrollViewer.Visibility = Visibility.Visible;
            MissingPanel.Visibility = Visibility.Collapsed;
            PrintBtn.IsEnabled = _document is not null;
            ExportBtn.IsEnabled = _document is not null;
            ZoomInBtn.IsEnabled = _document is not null;
            ZoomOutBtn.IsEnabled = _document is not null;
            FitWidthBtn.IsEnabled = _document is not null;
        }

        private void ShowMissing()
        {
            ScrollViewer.Visibility = Visibility.Collapsed;
            MissingPanel.Visibility = Visibility.Visible;
            PrintBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;
            ZoomInBtn.IsEnabled = false;
            ZoomOutBtn.IsEnabled = false;
            FitWidthBtn.IsEnabled = false;
        }

        private void FitWidth_Click(object sender, RoutedEventArgs e) => _ = FitWidthAsync();

        private async Task FitWidthAsync()
        {
            if (_document is null || _document.PageCount == 0) return;
            double available = ScrollViewer.ViewportWidth > 0 ? ScrollViewer.ViewportWidth : 600;
            // Lebar halaman 100% (DIP) tidak bergantung pada skala yang sedang aktif.
            double lebarPenuh = _document.PageSizes[0].Width * 96.0 / 72.0;
            _scale = Math.Clamp((available - 40) / lebarPenuh, 0.1, 8.0);
            await RenderPagesAsync();
        }

        /// <summary>Lebar halaman pada skala tampil (DIP) — 100% = ukuran kertas sebenarnya.</summary>
        private double LebarHalamanDip(int halaman) =>
            _document!.PageSizes[halaman].Width * _scale * 96.0 / 72.0;

        /// <summary>
        /// Piksel per titik PDF. Halaman dirender pada kerapatan perangkat layar
        /// (bukan hanya DIP), sehingga teks tetap tajam pada Windows 125%/150%
        /// dan saat diperbesar.
        /// </summary>
        private double SkalaPiksel()
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return Math.Min(8.0, dpi * 96.0 / 72.0);
        }

        /// <summary>DPI bitmap yang membuat satu pikselnya sama dengan satu piksel layar.</summary>
        private double KerapatanLayar() => Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX) * 96.0;

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => _ = SetScaleAsync(_scale * 1.2);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => _ = SetScaleAsync(_scale / 1.2);

        private async Task SetScaleAsync(double value)
        {
            _scale = Math.Clamp(value, 0.1, 8.0);
            await RenderPagesAsync();
        }

        private async Task RenderPagesAsync()
        {
            if (_document is null)
            {
                return;
            }

            PagesPanel.Children.Clear();
            int generation = _loadGeneration;

            double pikselPerTitik = SkalaPiksel();
            double kerapatan = KerapatanLayar();
            for (int i = 0; i < _document.PageCount; i++)
            {
                var size = _document.PageSizes[i];
                BitmapSource bitmap;
                // Dirender tepat satu piksel bitmap per piksel layar. Sebelumnya
                // halaman digambar dua kali lebih rapat lalu diperkecil WPF dengan
                // penyaringan bilinear — huruf tebal berubah abu-abu dan tepinya
                // kabur. Tanpa pembesaran itu teks tampil setajam penampil PDF.
                try { bitmap = await _document.RenderAsync(i, Math.Min(8.0, _scale * pikselPerTitik), kerapatan); }
                catch (Exception) { return; }
                if (generation != _loadGeneration || _document is null) return;
                int w = Math.Max(1, (int)Math.Round(LebarHalamanDip(i)));
                int h = Math.Max(1, (int)Math.Round(size.Height * _scale * 96.0 / 72.0));
                var card = new Border
                {
                    Background = TryFindResource("KertasBrush") as Brush ?? Brushes.White,
                    BorderBrush = TryFindResource("KertasTepiBrush") as Brush
                                  ?? new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD4)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(7),
                    Margin = new Thickness(0, 0, 0, 16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = TryFindResource("BayanganHitamColor") is Color bayangan
                            ? bayangan
                            : Colors.Black,
                        BlurRadius = 16,
                        ShadowDepth = 2,
                        Opacity = 0.3
                    }
                };

                var pageStack = new StackPanel();
                var halaman = new Image
                {
                    Source = bitmap,
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Uniform
                };
                // Kalau pembulatan ukuran menyisakan pecahan piksel, WPF tetap
                // perlu menyesuaikan sedikit; filter kelas atas menjaga tepi huruf
                // tetap tegas alih-alih bilinear yang mengaburkannya.
                RenderOptions.SetBitmapScalingMode(halaman, BitmapScalingMode.HighQuality);
                pageStack.Children.Add(halaman);

                // Label "Halaman N dari M" di bawah setiap halaman DIHAPUS.
                // Angka halaman sudah bisa dibaca dari jumlah kartu halaman yang
                // berbentuk edge-to-edge (dan dari nomor surat pada dokumen),
                // sementara label ini memakan tempat, mengulang informasi yang
                // sama untuk setiap kartu, dan selalu tampil "Halaman 1 dari 1"
                // pada surat satu halaman — seakan-ini pesan, bukan keterangan.

                card.Child = pageStack;
                PagesPanel.Children.Add(card);
            }

            ZoomText.Text = $"{_scale * 100.0:0}%";
        }

        private void Print_Click(object sender, RoutedEventArgs e) => _ = PrintAsync();

        private async Task PrintAsync()
        {
            var document = _document;
            if (document is null || document.PageCount == 0) return;
            try
            {
                if (DataContext is PdfPreviewViewModel vm && !await vm.BolehCetakAsync()) return;
                if (!ReferenceEquals(document, _document)) return;
                await document.PrintAsync(_title);
            }
            catch (Exception)
            {
                // Cetak dibatalkan atau gagal; biarkan pengguna mencoba lagi.
            }
        }

    }
}