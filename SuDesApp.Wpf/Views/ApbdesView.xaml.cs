using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman APBDES. Seluruh keadaan halaman — termasuk daftar tahun anggaran —
    /// diurus <see cref="ViewModels.ApbdesViewModel"/> dan pengikatan datanya, jadi
    /// tidak ada kode di belakang layar yang perlu menyinkronkan pilihan.
    /// </summary>
    public partial class ApbdesView : UserControl
    {
        public ApbdesView()
        {
            InitializeComponent();
        }
    }
}
