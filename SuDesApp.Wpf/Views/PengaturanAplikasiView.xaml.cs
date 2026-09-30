using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SuDesApp.Utilities;

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

        /// <summary>Urutan bagian sama persis dengan urutan StackPanel pada XAML.
        /// Bagian "google-oauth" (Kredensial Google) dihapus — kredensial kini
        /// tertanam di aplikasi dan tidak dikelola dari halaman ini.</summary>
        private static readonly string[] UrutanSeksi =
        {
            "umum", "penomoran", "layanan-wa", "gateway-wa", "google-sheet", "database", "informasi"
        };

        private ViewModels.PengaturanAplikasiViewModel? _vm;
        private bool _seksiDipetakan;
        private readonly Dictionary<string, UIElement> _seksiPanel = new();
        private bool _memilihSeksi;

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
            if (e.OldValue is ViewModels.PengaturanAplikasiViewModel lama)
                lama.PropertyChanged -= OnVmPropertyChanged;

            if (e.NewValue is not ViewModels.PengaturanAplikasiViewModel vm)
            {
                TumpukanBagian?.Children.Clear();
                return;
            }

            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;

            if (!string.IsNullOrEmpty(vm.FocusSection))
            {
                // Tunggu layout selesai agar posisi kartu & ExtentHeight sudah final.
                Dispatcher.BeginInvoke(new Action(() => ScrollToSection(vm.FocusSection)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            KumpulkanSeksi();
            TampilkanSeksi(vm.SelectedSection);
            IsiPengaturan?.ScrollToTop();
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModels.PengaturanAplikasiViewModel.SelectedSection)) return;
            if (_vm == null) return;

            TampilkanSeksi(_vm.SelectedSection);
            IsiPengaturan?.ScrollToTop();
        }

        /// <summary>
        /// Rekam pemetaan kunci bagian → StackPanel-nya. Dilakukan sekali saja
        /// (saat semua bagian memang masih ada di pohon), karena setelah bagian
        /// dilepas dari pohon pencarannya tidak lengkap lagi.
        /// </summary>
        private void KumpulkanSeksi()
        {
            if (_seksiDipetakan || TumpukanBagian == null) return;

            int i = 0;
            foreach (UIElement anak in TumpukanBagian.Children)
            {
                if (i < UrutanSeksi.Length) _seksiPanel[UrutanSeksi[i]] = anak;
                i++;
            }
            _seksiDipetakan = true;
        }

        /// <summary>
        /// Hanya bagian yang terpilih yang disambungkan ke pohon visual; bagian
        /// lain dilepas sehingga tidak dirender, diukur, maupun digambar — halaman
        /// tetap ringan dan gulirannya tidak tersendat.
        /// </summary>
        private void TampilkanSeksi(ViewModels.PengaturanSectionVM? bagian)
        {
            if (_memilihSeksi || TumpukanBagian == null) return;
            _memilihSeksi = true;
            try
            {
                var kunci = bagian?.Key ?? "umum";
                _seksiPanel.TryGetValue(kunci, out var target);

                TumpukanBagian.Children.Clear();
                if (target != null) TumpukanBagian.Children.Add(target);
            }
            finally
            {
                _memilihSeksi = false;
            }
        }

        // ===== Contoh animasi kecepatan (Lambat/Normal/Cepat) =====

        /// <summary>Keadaan contoh: tertutup (sidebar lebar, dropdown menutup) atau terbuka.</summary>
        private bool _contohTerbuka;

        /// <summary>
        /// Putar contoh animasi memakai durasi dari kecepatan yang sedang dipilih, supaya
        /// pengguna bisa merasakan bedanya sebelum menutup halaman pengaturan. Yang
        /// dianimasikan adalah tiruan kecil di kartu ini, bukan sidebar aplikasi, agar
        /// tata letak kerja pengguna tidak ikut berubah.
        /// </summary>
        private void ContohAnimasi_Click(object sender, RoutedEventArgs e)
        {
            _contohTerbuka = !_contohTerbuka;

            var durasiSidebar = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarSidebar);
            var durasiDropdown = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarDropdown);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // (1) Sidebar tiruan menyempit/melebar — sama seperti menekan tombol tampilan navigasi.
            AnimasiContoh(ContohSidebar, FrameworkElement.WidthProperty,
                _contohTerbuka ? 104d : 196d, durasiSidebar, ease);
            AnimasiContoh(ContohSidebarJudul, UIElement.OpacityProperty,
                _contohTerbuka ? 0d : 1d, durasiSidebar, ease);

            // (2) Dropdown grup membuka/menutup, panahnya berputar serentak.
            AnimasiContoh(ContohDropdown, FrameworkElement.MaxHeightProperty,
                _contohTerbuka ? 72d : 0d, durasiDropdown, ease);
            AnimasiContoh(ContohDropdown, UIElement.OpacityProperty,
                _contohTerbuka ? 1d : 0d, durasiDropdown, ease);
            AnimasiContoh(SiapkanPutarContoh(), RotateTransform.AngleProperty,
                _contohTerbuka ? 90d : 0d, durasiDropdown, ease);

            TombolContohAnimasi.Content = _contohTerbuka ? "Tutup contoh" : "Putar contoh";
        }

        /// <summary>
        /// Putaran panah contoh. Transform hasil XAML dibekukan WPF, sehingga
        /// BeginAnimation langsung melempar — versi bekunya diklon lebih dulu.
        /// </summary>
        private RotateTransform SiapkanPutarContoh()
        {
            if (ContohChevron.RenderTransform is RotateTransform putar)
            {
                if (!putar.IsFrozen)
                {
                    return putar;
                }

                var klon = putar.Clone();
                ContohChevron.RenderTransform = klon;
                return klon;
            }

            var baru = new RotateTransform { CenterX = 5, CenterY = 6 };
            ContohChevron.RenderTransform = baru;
            return baru;
        }

        /// <summary>
        /// Animasi contoh: nilai akhir dipegang animasi (HoldEnd), jadi gerakan berikutnya
        /// selalu berangkat dari posisi nyatanya dan tidak ada lompatan di ujung animasi.
        /// </summary>
        private static void AnimasiContoh(
            DependencyObject target, DependencyProperty properti, double ke, TimeSpan durasi, IEasingFunction ease)
        {
            var animasi = new DoubleAnimation
            {
                To = ke,
                Duration = durasi,
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd
            };

            switch (target)
            {
                case UIElement ui:
                    ui.BeginAnimation(properti, animasi);
                    break;
                case Animatable animatable:
                    animatable.BeginAnimation(properti, animasi);
                    break;
            }
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
