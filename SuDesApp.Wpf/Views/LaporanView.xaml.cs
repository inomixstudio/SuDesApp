using System;
using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Laporan Penduduk. Penyusunan angka, PDF, dan Excel semuanya milik
    /// <see cref="LaporanViewModel"/> dan berjalan lewat <c>Command</c> di XAML;
    /// pemuatan data dilakukan <c>MainWindowViewModel</c> saat halaman dibuka.
    ///
    /// Code-behind hanya mengurus tata letak: pada halaman lebar kartu "Isi
    /// laporan" berdampingan dengan pratinjau (kiri-kanan) dan lebarnya bisa
    /// digeser lewat <c>GridSplitter</c>; saat halaman menyempit keduanya menumpuk
    /// vertikal supaya kolom "Keterangan" pada tabel pratinjau tetap terbaca.
    /// </summary>
    public partial class LaporanView : UserControl
    {
        /// <summary>
        /// Lebar halaman (piksel) di bawah nilai ini kedua kartu menumpuk vertikal.
        /// 1000 px ≈ jendela 1280 px dengan sidebar terbuka.
        /// </summary>
        private const double AmbangTumpuk = 1000;

        /// <summary>Mode tata letak yang sedang berlaku; null sebelum pengaturan pertama.</summary>
        private bool? _duaKolom;

        public LaporanView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => SusunTataLetak();
        }

        /// <summary>
        /// Terapkan mode tata letak sesuai lebar halaman. Hanya dijalankan saat
        /// mode berganti supaya hasil geser pengguna pada pembagi kolom tidak
        /// dikembalikan ke semula setiap kali jendela berubah ukuran.
        /// </summary>
        private void SusunTataLetak()
        {
            if (ActualWidth <= 0) return;

            bool duaKolom = ActualWidth >= AmbangTumpuk;
            if (duaKolom == _duaKolom) return;
            _duaKolom = duaKolom;

            if (duaKolom)
            {
                // Berdampingan: Isi laporan di kiri (± 32% lebar, sisanya pratinjau).
                KolomKiri.Width = new GridLength(0.32, GridUnitType.Star);
                KolomKanan.Width = new GridLength(1, GridUnitType.Star);
                BarisBawah.Height = new GridLength(0);
                Grid.SetRow(KartuPratinjau, 0);
                Grid.SetColumn(KartuPratinjau, 2);
                PembagiKolom.Visibility = Visibility.Visible;
            }
            else
            {
                // Menumpuk: Isi laporan di atas, pratinjau di bawah (lebih tinggi).
                KolomKiri.Width = new GridLength(1, GridUnitType.Star);
                KolomKanan.Width = new GridLength(0);
                BarisBawah.Height = new GridLength(1.4, GridUnitType.Star);
                Grid.SetRow(KartuPratinjau, 1);
                Grid.SetColumn(KartuPratinjau, 0);
                PembagiKolom.Visibility = Visibility.Collapsed;
            }
        }
    }
}
