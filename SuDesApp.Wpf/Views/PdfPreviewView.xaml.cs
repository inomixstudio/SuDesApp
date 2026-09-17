using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PdfiumViewer;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Pratinjau PDF berbasis PDFium (PdfiumViewer) dengan zoom, fit-lebar, dan cetak.
    /// Pengganti implementasi sebelumnya yang bergantung pada WebView2 Runtime.
    /// Padanan FormulirControl/ShowPdfBytesInMainPanel (WinForms).
    /// </summary>
    public partial class PdfPreviewView : UserControl
    {
        private PdfDocument? _document;
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
                LoadDocument();
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDocument();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _document?.Dispose();
            _document = null;
            _currentPdfPath = null;
        }

        private void LoadDocument(bool force = false)
        {
            if (force) _currentPdfPath = null;
            if (DataContext is not PdfPreviewViewModel viewModel)
            {
                return;
            }

            // Skip reload hanya bila path sama DAN file belum berubah (ukuran+mtime).
            // Tanpa cek isi, PDF hasil edit yang ditulis ulang ke nama file yang sama
            // akan tampil sebagai dokumen basi.
            if (_currentPdfPath == viewModel.PdfPath && _document is not null)
            {
                try
                {
                    var info = new FileInfo(viewModel.PdfPath);
                    if (_cachedLength == info.Length && _cachedLastWrite == info.LastWriteTimeUtc)
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    // File tak terbaca: lanjut coba muat ulang.
                }
            }

            _title = viewModel.Title;
            _currentPdfPath = viewModel.PdfPath;
            _cachedLength = -1;
            _cachedLastWrite = DateTime.MinValue;

            if (viewModel.IsMissing || string.IsNullOrEmpty(viewModel.PdfPath))
            {
                ShowMissing();
                return;
            }

            try
            {
                _document?.Dispose();
                _document = null;
                _document = PdfDocument.Load(viewModel.PdfPath);
                var info = new FileInfo(viewModel.PdfPath);
                _cachedLength = info.Exists ? info.Length : -1;
                _cachedLastWrite = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                FitWidth();
            }
            catch (Exception)
            {
                // Bila gagal muat (mis. file baru saja dibuat dan masih ditulis),
                // coba sekali lagi setelah jeda singkat sebelum menyerah.
                try
                {
                    System.Threading.Thread.Sleep(150);
                    _document?.Dispose();
                    _document = PdfDocument.Load(viewModel.PdfPath);
                    FitWidth();
                }
                catch (Exception)
                {
                    ShowMissing();
                }
            }
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

        private void FitWidth_Click(object sender, RoutedEventArgs e) => FitWidth();

        private void FitWidth()
        {
            if (_document is null || _document.PageCount == 0)
            {
                return;
            }

            double available = ScrollViewer.ViewportWidth > 0 ? ScrollViewer.ViewportWidth : 600;
            available -= 40; // margin halaman + estimasi scrollbar
            double renderedW = _document.PageSizes[0].Width; // dalam titik (72 dpi)
            _scale = Math.Max(0.1, Math.Min(8.0, available / renderedW));
            RenderPages();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetScale(_scale * 1.2d);

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetScale(_scale / 1.2d);

        private void SetScale(double value)
        {
            _scale = Math.Max(0.1, Math.Min(8.0, value));
            RenderPages();
        }

        private void RenderPages()
        {
            if (_document is null)
            {
                return;
            }

            PagesPanel.Children.Clear();

            for (int i = 0; i < _document.PageCount; i++)
            {
                var size = _document.PageSizes[i];
                int w = Math.Max(1, (int)(size.Width * _scale));
                int h = Math.Max(1, (int)(size.Height * _scale));

                using var bmp = (System.Drawing.Bitmap)_document.Render(i, w, h, 96f, 96f, PdfRenderFlags.LcdText);
                var image = new Image
                {
                    Source = ToBitmapSource(bmp),
                    Width = w,
                    Height = h,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                PagesPanel.Children.Add(image);
            }

            ZoomText.Text = $"{_scale * 100.0:0}%";
        }

        private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_document is null || _document.PageCount == 0)
            {
                return;
            }

            // Cetak seperti aplikasi lama (WinForms): dialog cetak Windows standar
            // (pilih printer, rentang halaman, salinan) → langsung kirim ke printer.
            try
            {
                if (_currentPdfPath is not null && File.Exists(_currentPdfPath))
                {
                    await App.PrintService.PrintPdfFileAsync(_currentPdfPath, _title);
                    return;
                }

                // Dokumen dimuat dari memori (tanpa file): render PDFium → PrintDocument.
                using var printDoc = _document.CreatePrintDocument();
                printDoc.DocumentName = _title;
                printDoc.Print();
            }
            catch (Exception)
            {
                // Cetak dibatalkan atau gagal; biarkan pengguna mencoba lagi.
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}