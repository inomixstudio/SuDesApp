using System.Windows;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Properti tempel penanda bagian halaman Perangkat Desa pada tombol pemilih
    /// bagian (<c>PerangkatDesaView</c>).
    ///
    /// Sebelumnya tiap bagian punya gaya tombol sendiri-sendiri
    /// (<c>TombolBagianPerangkat</c>, <c>TombolBagianRtRw</c>, …) yang isinya
    /// sama persis dan hanya berbeda pada properti <c>Tampilkan…</c> yang
    /// diikat. Dengan satu penanda ini cukup SATU gaya tombol untuk seluruh
    /// bagian: gaya membangkitkan DataTrigger dari nilai penanda, lalu
    /// membandingkannya dengan <c>PerangkatDesaViewModel.BagianAktif</c>.
    ///
    /// Nilai penanda = nama anggota enum <c>BagianPerangkat</c> (mis. "Linmas").
    /// Sengaja teks, bukan enum, supaya XAML tidak perlu mengenal tipe view model.
    /// </summary>
    public static class BagianAktif
    {
        public static readonly DependencyProperty BagianProperty =
            DependencyProperty.RegisterAttached(
                "Bagian",
                typeof(string),
                typeof(BagianAktif),
                new PropertyMetadata(string.Empty));

        public static string GetBagian(DependencyObject elemen) =>
            (string)elemen.GetValue(BagianProperty);

        public static void SetBagian(DependencyObject elemen, string nilai) =>
            elemen.SetValue(BagianProperty, nilai);
    }
}
