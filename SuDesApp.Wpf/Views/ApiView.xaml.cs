using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman API Desa. Tidak ada logika bisnis di sini: nyala/mati
    /// listener, pengaturan port, dan pengelolaan kunci API milik
    /// <see cref="ViewModels.ApiViewModel"/>, yang juga melakukan uji koneksi
    /// sehingga halaman tidak perlu HttpClient sendiri.
    /// </summary>
    public partial class ApiView : UserControl
    {
        public ApiView()
        {
            InitializeComponent();
        }
    }
}
