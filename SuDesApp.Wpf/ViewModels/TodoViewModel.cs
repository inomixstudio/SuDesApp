using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman placeholder untuk fitur yang belum dikonversi penuh ke WPF.
    /// Menampilkan judul dan keterangan agar navigasi sidebar tetap berfungsi.
    /// </summary>
    public class TodoViewModel : ObservableObject
    {
        public string Title { get; set; } = "Fitur";
        public string Description { get; set; } = "Fitur ini sedang disiapkan dalam versi WPF.";
        public string Icon { get; set; } = "🚧";
    }
}