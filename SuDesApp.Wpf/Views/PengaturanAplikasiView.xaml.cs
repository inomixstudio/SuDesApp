using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Pengaturan Aplikasi.
    ///
    /// Isinya dipisah menjadi beberapa bagian yang dipilih lewat daftar navigasi
    /// di sisi kiri (hanya bagian terpilih yang ditampilkan). Bila halaman dibuka
    /// dengan permintaan fokus (mis. dari chip status WhatsApp/Sheet di statusbar),
    /// bagian yang memuat kartu tersebut langsung dipilih, lalu kartunya diberi
    /// highlight lembut.
    /// </summary>
    public partial class PengaturanAplikasiView : UserControl
    {
        /// <summary>Kunci kartu pengaturan yang bisa menjadi target fokus.</summary>
        public const string SectionGatewayWa = "gateway-wa";
        public const string SectionGoogleSheet = "google-sheet";

        public PengaturanAplikasiView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // Pindah bagian → isi dimulai dari atas, supaya bagian baru tidak
            // terbuka di tengah (posisi gulir bagian sebelumnya tidak terbawa).
            DaftarBagian.SelectionChanged += (_, _) => IsiPengaturan?.ScrollToTop();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ViewModels.PengaturanAplikasiViewModel vm && !string.IsNullOrEmpty(vm.FocusSection))
            {
                // Tunggu layout selesai agar posisi kartu & ExtentHeight sudah final.
                Dispatcher.BeginInvoke(new Action(() => ScrollToSection(vm.FocusSection)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (e.NewValue is ViewModels.PengaturanAplikasiViewModel kredensialVm)
                GoogleSecretBox.Password = kredensialVm.GoogleClientSecret ?? string.Empty;
        }

        private void GoogleSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.PengaturanAplikasiViewModel vm)
                vm.GoogleClientSecret = GoogleSecretBox.Password ?? string.Empty;
        }

        /// <summary>
        /// Menggulir area isi ke kartu pengaturan dengan kunci yang diberikan.
        /// Kartu tersebut berada di bagian yang sudah dipilih pembaca
        /// <c>FocusSection</c> pada ViewModel.
        /// </summary>
        public void ScrollToSection(string sectionKey)
        {
            var target = FindChildWithTag(this, sectionKey);
            if (target == null) return;

            HighlightCard(target);

            if (IsiPengaturan == null) return;

            var transform = target.TransformToAncestor(IsiPengaturan);
            var topLeft = transform.Transform(new Point(0, 0));
            IsiPengaturan.ScrollToVerticalOffset(Math.Max(0, topLeft.Y - 24));
        }

        /// <summary>
        /// Highlight lembut pada kartu target: border berubah ke warna aksen lalu
        /// memudar ke warna border normal; brush asli dipulihkan tepat setelah
        /// animasi berakhir (HoldEnd dihentikan dalam tick yang sama — tanpa kilat).
        /// </summary>
        private static void HighlightCard(FrameworkElement target)
        {
            if (target is not Border targetBorder) return;

            var originalBrush = targetBorder.BorderBrush;

            // Warna akhir fade: warna border tema bila bisa dibaca; fallback netral.
            var endColor = (targetBorder.TryFindResource("BorderBrush") as SolidColorBrush)?.Color
                           ?? Color.FromRgb(0x80, 0x80, 0x80);

            var animated = new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)); // biru Google
            targetBorder.BorderBrush = animated;

            var fade = new System.Windows.Media.Animation.ColorAnimation(
                endColor, TimeSpan.FromSeconds(1.6))
            {
                FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
            };
            animated.BeginAnimation(SolidColorBrush.ColorProperty, fade);

            var restoreTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.8)
            };
            restoreTimer.Tick += (_, _) =>
            {
                restoreTimer.Stop();
                // Hentikan animasi (HoldEnd) dan pulihkan brush asli dalam satu
                // operasi dispatcher agar tidak ada kilatan warna di antaranya.
                animated.BeginAnimation(SolidColorBrush.ColorProperty, null);
                targetBorder.BorderBrush = originalBrush;
            };
            restoreTimer.Start();
        }

        /// <summary>Mencari FrameworkElement turunan dengan Tag == key (DFS).</summary>
        private static FrameworkElement? FindChildWithTag(DependencyObject parent, string key)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && string.Equals(fe.Tag as string, key, StringComparison.OrdinalIgnoreCase))
                    return fe;

                var deep = FindChildWithTag(child, key);
                if (deep != null) return deep;
            }
            return null;
        }

        /// <summary>Mencari turunan bertipe T (pertama yang ditemukan, DFS).</summary>
        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var deep = FindChild<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }
    }
}
