using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Indeks Desa &amp; IDM. Seluruh kebijakan tata letak kartu atas
    /// (tiga/dua/satu kolom, jarak, tinggi baris) dimiliki
    /// <c>Controls.PanelKartuResponsif</c>; code-behind ini sengaja kosong
    /// supaya keadaan tampilan tidak terpecah antara panel dan halaman.
    /// </summary>
    public partial class IdmView : UserControl
    {
        public IdmView()
        {
            InitializeComponent();
        }
    }
}
