using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman input/edit data buku agenda Surat Masuk/Keluar yang ditanam di area
    /// konten menu utama (bukan jendela terpisah). DataContext
    /// (InputAgendaViewModel) datang dari navigasi; penutupan halaman diatur oleh
    /// ViewModel lewat event RequestClose.
    /// </summary>
    public partial class InputAgendaView : UserControl
    {
        public InputAgendaView()
        {
            InitializeComponent();
        }
    }
}
