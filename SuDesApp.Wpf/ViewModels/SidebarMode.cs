using SuDesApp.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Tampilan sidebar navigasi:
    /// <list type="bullet">
    /// <item><see cref="Terbuka"/> — nama menu tampil langsung di sidebar (lebar penuh).</item>
    /// <item><see cref="IkonRingkas"/> — hanya ikon; nama menu lewat tooltip.</item>
    /// </list>
    /// Urutan ini sekaligus urutan perputaran tombol ganti tampilan.
    /// </summary>
    public enum SidebarMode
    {
        Terbuka,
        IkonRingkas
    }

    /// <summary>
    /// Aturan perpindahan antar mode sidebar beserta penyimpanannya di preferensi
    /// aplikasi. Dipisah dari jendela supaya bisa diperiksa uji tanpa membuka UI.
    /// </summary>
    public static class SidebarModePrefs
    {
        private const string NilaiTerbuka = "terbuka";
        private const string NilaiIkon = "ikon";

        /// <summary>Mode berikutnya saat tombol ganti tampilan ditekan (berputar dua mode).</summary>
        public static SidebarMode Berikutnya(SidebarMode sekarang) => sekarang switch
        {
            SidebarMode.Terbuka => SidebarMode.IkonRingkas,
            _ => SidebarMode.Terbuka
        };

        /// <summary>Keterangan pendek mode untuk tooltip tombol & statusbar.</summary>
        public static string Teks(SidebarMode mode) => mode switch
        {
            SidebarMode.IkonRingkas => "Ikon saja",
            _ => "Nama menu tampil"
        };

        /// <summary>Penjelasan apa yang terjadi bila tombol dipakai dari mode ini.</summary>
        public static string TeksBerikut(SidebarMode mode) => mode switch
        {
            SidebarMode.Terbuka => "perkecil ke ikon saja",
            _ => "tampilkan nama menu di sidebar"
        };

        /// <summary>
        /// Mode sidebar terakhir yang dipakai pengguna. Pemasangan lama (belum pernah
        /// memilih) tetap memakai tampilan penuh, supaya navigasi tidak tiba-tiba
        /// menciut setelah pembaruan.
        /// </summary>
        public static SidebarMode Muat()
            => DariPreferensi(AppPreferenceStore.GetString(AppPreferenceStore.KeyModeSidebar, null));

        /// <summary>Simpan pilihan tampilan sidebar untuk pembukaan aplikasi berikutnya.</summary>
        public static void Simpan(SidebarMode mode)
            => AppPreferenceStore.SetString(AppPreferenceStore.KeyModeSidebar, KePreferensi(mode));

        /// <summary>Nilai preferensi untuk sebuah mode (stabil, tidak ikut berubah bila enum diurutkan ulang).</summary>
        public static string KePreferensi(SidebarMode mode) => mode switch
        {
            SidebarMode.IkonRingkas => NilaiIkon,
            _ => NilaiTerbuka
        };

        /// <summary>Baca nilai preferensi; nilai tidak dikenal selalu jatuh ke tampilan penuh.</summary>
        public static SidebarMode DariPreferensi(string? nilai) => nilai?.Trim().ToLowerInvariant() switch
        {
            NilaiIkon => SidebarMode.IkonRingkas,
            _ => SidebarMode.Terbuka
        };
    }
}
