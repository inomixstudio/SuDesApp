using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Beranda. Ringkasan dimuat saat halaman pertama ditampilkan dan bisa
    /// dimuat ulang lewat tombol "Muat Ulang Ringkasan".
    ///
    /// Isi halaman tampil LANGSUNG penuh tanpa animasi masuk — pengguna menilai
    /// animasi kartu berurutan terasa seperti kedipan/lag. Gerakan yang tersisa
    /// hanya umpan balik: angka statistik menghitung naik dari nol dan kartu
    /// pintasan terangkat saat dikursor.
    /// </summary>
    public partial class BerandaView : UserControl
    {
        public BerandaView()
        {
            InitializeComponent();
            Loaded += BerandaView_Loaded;
        }

        private async void BerandaView_Loaded(object sender, RoutedEventArgs e)
        {
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
