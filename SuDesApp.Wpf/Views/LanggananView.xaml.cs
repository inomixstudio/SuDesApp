using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Langganan. Tidak ada logika bisnis di sini: status lisensi,
    /// kode aktivasi, dan pembuatan tagihan milik
    /// <see cref="ViewModels.LanggananViewModel"/>. Halaman ini sengaja dibuat
    /// berdiri sendiri di navigasi menu utama (bukan bagian dari Pengaturan
    /// Aplikasi), supaya masa aktif lisensi mudah ditemukan pengguna.
    /// </summary>
    public partial class LanggananView : UserControl
    {
        public LanggananView()
        {
            InitializeComponent();
        }
    }
}
