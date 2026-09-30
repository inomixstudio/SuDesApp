using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Laporan Penduduk. Tidak ada logika bisnis di sini: penyusunan
    /// angka, PDF, dan Excel semuanya milik <see cref="LaporanViewModel"/>.
    /// Code-behind sengaja kosong karena halaman ini tidak punya interaksi yang
    /// perlu diterjemahkan — seluruh aksi berjalan lewat
    /// <c>Command</c> yang diikat di XAML, dan pemuatan data dilakukan
    /// <c>MainWindowViewModel</c> saat halaman dibuka.
    /// </summary>
    public partial class LaporanView : UserControl
    {
        public LaporanView()
        {
            InitializeComponent();
        }
    }
}
