using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Pengaturan Nomor Surat — halaman mandiri di navigasi Menu Utama,
    /// dahulunya seksi "Penomoran Surat" di dalam halaman Pengaturan Aplikasi.
    /// Seluruh isi kartu diikat ke <see cref="ViewModels.PengaturanNomorSuratViewModel"/>.
    /// </summary>
    public partial class PengaturanNomorSuratView : UserControl
    {
        public PengaturanNomorSuratView() => InitializeComponent();
    }
}
