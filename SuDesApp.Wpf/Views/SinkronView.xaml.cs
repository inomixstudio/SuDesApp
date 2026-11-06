using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Sinkron Android. Tanpa logika bisnis: seluruh pekerjaan ada di
    /// <see cref="SuDesApp.Sinkron.SinkronSuratService"/> dan
    /// <see cref="SuDesApp.Sinkron.PerangkaianPerangkat"/>.
    /// </summary>
    public partial class SinkronView : UserControl
    {
        public SinkronView()
        {
            InitializeComponent();
        }
    }
}
