namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman yang ingin menuliskan judulnya sendiri di title bar jendela (mis. wizard
    /// Template Surat yang judulnya berubah mengikuti tahap). Bila ViewModel yang sedang
    /// tampil tidak memakai antarmuka ini, title bar memakai nama menu yang terakhir diklik.
    /// </summary>
    public interface IJudulHalaman
    {
        /// <summary>Judul halaman untuk title bar; kosong berarti kembali ke nama menu.</summary>
        string JudulHalaman { get; }
    }
}
