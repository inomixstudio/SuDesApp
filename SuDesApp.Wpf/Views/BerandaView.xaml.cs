using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Beranda. Ringkasan dimuat saat halaman pertama ditampilkan dan bisa
    /// dimuat ulang lewat tombol "Muat Ulang Ringkasan".
    ///
    /// Animasi di halaman ini sengaja halus dan singkat (150–350 ms, CubicEase
    /// EaseOut) supaya terasa hidup tanpa mengganggu: kartu masuk bertahap
    /// (fade + naik 14px berurutan), angka statistik menghitung naik dari nol,
    /// dan kartu pintasan terangkat 3px saat dikursor.
    /// </summary>
    public partial class BerandaView : UserControl
    {
        /// <summary>Durasi satu elemen pada animasi masuk.</summary>
        private static readonly Duration DurasiMasuk = new(TimeSpan.FromMilliseconds(350));

        /// <summary>Jeda antar-elemen pada animasi masuk berurutan.</summary>
        private static readonly TimeSpan JedaAntarKartu = TimeSpan.FromMilliseconds(90);

        public BerandaView()
        {
            InitializeComponent();
            Loaded += BerandaView_Loaded;
        }

        private async void BerandaView_Loaded(object sender, RoutedEventArgs e)
        {
            AnimasiMasuk();

            if (DataContext is BerandaViewModel vm)
            {
                await vm.MuatAsync();

                // Angka menghitung naik setelah data selesai dimuat — nilai
                // akhir selalu sama dengan binding, animasinya saja yang beda.
                HitungNaik(NilaiHariIni, vm.SuratHariIni);
                HitungNaik(NilaiBulanIni, vm.SuratBulanIni);
                HitungNaik(NilaiTotal, vm.TotalSurat);
                HitungNaik(NilaiWarga, vm.TotalWarga);
            }
        }

        /// <summary>
        /// Setiap kartu utama memudar masuk sambil naik 14px, berurutan
        /// supaya halaman terasa tersusun, bukan muncul sekaligus.
        /// </summary>
        private void AnimasiMasuk()
        {
            var elemen = new FrameworkElement[] { HeaderKartu, StatGrid, PintasanKartu, BawahGrid };
            var papan = new Storyboard();

            for (int i = 0; i < elemen.Length; i++)
            {
                var el = elemen[i];
                if (el is null) continue;

                el.RenderTransform = new TranslateTransform();
                var mulai = TimeSpan.FromMilliseconds(i * JedaAntarKartu.TotalMilliseconds);
                var eman = new CubicEase { EasingMode = EasingMode.EaseOut };

                var memudar = new DoubleAnimation(0, 1, DurasiMasuk)
                {
                    BeginTime = mulai,
                    EasingFunction = eman,
                };
                Storyboard.SetTargetProperty(memudar, new PropertyPath(nameof(UIElement.Opacity)));
                Storyboard.SetTarget(memudar, el);
                papan.Children.Add(memudar);

                var naik = new DoubleAnimation(14, 0, DurasiMasuk)
                {
                    BeginTime = mulai,
                    EasingFunction = eman,
                };
                Storyboard.SetTargetProperty(naik, new PropertyPath(
                    "(UIElement.RenderTransform).(TranslateTransform.Y)"));
                Storyboard.SetTarget(naik, el);
                papan.Children.Add(naik);
            }

            papan.Begin();
        }

        /// <summary>
        /// Angka menghitung naik dari 0 ke nilai akhir dalam ~650 ms dengan
        /// ease-out (cepat di awal, melambat di akhir). Nilai nol langsung
        /// ditampilkan tanpa animasi.
        /// </summary>
        private void HitungNaik(TextBlock target, int nilaiAkhir)
        {
            if (target is null || nilaiAkhir <= 0) return;

            const double durasiMs = 650;
            var mulai = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };

            timer.Tick += (s, _) =>
            {
                double progres = Math.Min(1, (DateTime.UtcNow - mulai).TotalMilliseconds / durasiMs);
                double berhenti = 1 - Math.Pow(1 - progres, 3); // CubicEase EaseOut
                target.Text = ((int)Math.Round(nilaiAkhir * berhenti)).ToString("N0");

                if (progres >= 1)
                {
                    target.Text = nilaiAkhir.ToString("N0");
                    timer.Stop();
                }
            };

            timer.Start();
        }
    }
}
