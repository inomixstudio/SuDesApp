using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Verifikasi Surat. Tanpa logika bisnis: seluruh pemeriksaan ada di
    /// <see cref="SuDesApp.Services.IVerifikasiSuratService"/> supaya hasilnya
    /// sama dengan endpoint API <c>GET /verifikasi/{kode}</c>.
    /// </summary>
    public partial class VerifikasiSuratView : UserControl
    {
        public VerifikasiSuratView()
        {
            InitializeComponent();
        }
    }
}
