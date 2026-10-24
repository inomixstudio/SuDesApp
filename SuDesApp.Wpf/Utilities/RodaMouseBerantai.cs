using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Gulir roda mouse DIRANTAI: roda digulirkan ke ScrollViewer terdekat yang
    /// benar-benar bisa bergerak ke arah itu; bila sudah di ujung — atau
    /// penampilnya memang tidak bisa digulir sama sekali — guliran diteruskan
    /// ke ScrollViewer berikutnya yang ada di atasnya.
    ///
    /// Kenapa perlu: WPF menandai kejadian MouseWheel "sudah ditangani" di
    /// penampil paling dalam, termasuk daftar ListBox/DataGrid yang ScrollBar
    /// vertikalnya <c>Disabled</c>. Selama ini akibatnya roda terasa MATI
    /// begitu kursor berada di atas tabel, daftar, atau grid di dalam halaman:
    /// penampil dalam menelan kejadiannya, halaman di luarnya tidak pernah ikut
    /// bergulir. Penangan ini dipasang sebagai class handler pada
    /// <see cref="Window"/> untuk event <see cref="UIElement.PreviewMouseWheelEvent"/>
    /// sehingga berjalan lebih dulu (tunneling) dan memilih penampil yang tepat
    /// sebelum WPF sempat menelan kejadiannya.
    ///
    /// Yang sengaja TIDAK diganggu:
    /// <list type="bullet">
    ///   <item>popup (dropdown, kalender, menu konteks) — visual tree-nya
    ///         terpisah, jadi kejadiannya tidak melewati Window aplikasi;</item>
    ///   <item>kontrol native seperti WebView (<see cref="System.Windows.Interop.HwndHost"/>)
    ///         — biarkan isinya yang menggulir;</item>
    ///   <item>kejadian yang sudah ditangani kontrol lain, roda horizontal, dan
    ///         keadaan ketika tidak ada satu pun penampil yang bisa bergerak
    ///         (kejadian dibiarkan mengalir seperti perilaku bawaan).</item>
    /// </list>
    /// </summary>
    internal static class RodaMouseBerantai
    {
        /// <summary>Toleransi setengah piksel untuk pembulatan layout.</summary>
        private const double Selisih = 0.5;

        /// <summary>Perkiraan tinggi satu baris teks saat Windows disetel
        /// menggulir beberapa baris per notch.</summary>
        private const double PikselPerBaris = 16;

        private static bool _terpasang;

        internal static bool SudahTerpasang => _terpasang;

        /// <summary>
        /// Pasang sekali per proses. Pemanggilan berulang tidak menambah handler
        /// kedua — handler ganda membuat satu putaran roda bergulir dua kali.
        /// </summary>
        internal static void Pasang()
        {
            if (_terpasang) return;
            _terpasang = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel));
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0) return;

            if (Gulirkan(e.OriginalSource as DependencyObject, e.Delta))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Gulirkan penampil terdekat yang bisa bergerak ke arah <paramref name="delta"/>
        /// (negatif = roda ke bawah), lalu teruskan ke penampil di atasnya bila
        /// sudah di ujung. Mengembalikan true bila ada penampil yang benar-benar
        /// bergerak.
        /// </summary>
        internal static bool Gulirkan(DependencyObject? mulai, int delta)
        {
            for (var kursor = mulai; kursor != null; kursor = Induk(kursor))
            {
                // Kontrol native (WebView dan sejenisnya) menggulir isinya
                // sendiri — jangan direbut.
                if (kursor is System.Windows.Interop.HwndHost) return false;

                if (kursor is ScrollViewer penampil
                    && penampil.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                    && Bisa(penampil, delta))
                {
                    // Gerak sebanding dengan putaran roda: satu notch (120)
                    // setara sejumlah baris sesuai setelan Windows.
                    penampil.ScrollToVerticalOffset(
                        penampil.VerticalOffset - (delta / 120.0) * Langkah(penampil));
                    return true;
                }
            }

            return false;
        }

        private static bool Bisa(ScrollViewer penampil, int delta)
        {
            if (penampil.ScrollableHeight <= Selisih) return false;

            // delta < 0 (roda ke bawah) butuh sisa ruang ke bawah; sebaliknya ke atas.
            return delta < 0
                ? penampil.VerticalOffset < penampil.ScrollableHeight - Selisih
                : penampil.VerticalOffset > Selisih;
        }

        /// <summary>Satu notch = sejumlah baris sesuai setelan Windows; bila
        /// setelannya "satu halaman penuh", pakai tinggi jendela penampil.</summary>
        private static double Langkah(ScrollViewer penampil)
        {
            int baris = SystemParameters.WheelScrollLines;
            if (baris < 0) return Math.Max(1, penampil.ViewportHeight);
            return Math.Max(1, baris) * PikselPerBaris;
        }

        /// <summary>Induk visual, atau induk logis untuk elemen konten (mis.
        /// Run di dalam TextBlock) yang bukan Visual.</summary>
        private static DependencyObject? Induk(DependencyObject kursor)
        {
            if (kursor is Visual or Visual3D)
            {
                return VisualTreeHelper.GetParent(kursor);
            }

            return kursor is FrameworkContentElement isi ? isi.Parent : null;
        }
    }
}
