using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman penyusunan Template Surat yang ditanam di area konten menu utama
    /// (bukan jendela terpisah). Isinya sama dengan wizard sebelumnya; DataContext
    /// (TemplateSuratWizardViewModel) datang dari navigasi, dan penutupan halaman
    /// diatur oleh ViewModel lewat event RequestClose.
    /// </summary>
    public partial class TemplateSuratWizardView : UserControl
    {
        public TemplateSuratWizardView()
        {
            InitializeComponent();
        }
    }
}
