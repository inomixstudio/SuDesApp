namespace SuDesApp.Utilities
{
    /// <summary>
    /// Penanda keadaan panduan awal (panduan singkat saat aplikasi pertama kali
    /// dibuka): apakah panduan sudah ditutup/diselesaikan pengguna, dan apakah
    /// langkah nomor surat sudah dinyatakan sesuai.
    ///
    /// Dipisahkan sebagai antarmuka supaya halaman panduan bisa diuji tanpa
    /// menyentuh berkas preferensi milik pengguna, dan supaya penyimpanannya
    /// tetap satu tempat (<see cref="AppPreferenceStore"/>).
    /// </summary>
    public interface IPanduanAwalStore
    {
        /// <summary>Panduan tidak perlu dibuka otomatis lagi.</summary>
        bool SudahSelesai { get; }

        /// <summary>Tandai panduan selesai/ditutup (atau dibuka lagi: false).</summary>
        void SetSudahSelesai(bool selesai);

        /// <summary>Pengguna menyatakan penomoran surat sudah sesuai (langkah nomor surat).</summary>
        bool PenomoranDiperiksa { get; }

        /// <summary>Tandai langkah nomor surat sudah diperiksa.</summary>
        void SetPenomoranDiperiksa(bool diperiksa);
    }

    /// <summary>
    /// Penyimpanan panduan awal di preferensi aplikasi
    /// (%LOCALAPPDATA%/SuDesApp/login_prefs.json). Kegagalan menulis diabaikan
    /// karena preferensi ini non-kritis — paling buruk panduan muncul sekali lagi.
    /// </summary>
    public sealed class PanduanAwalStore : IPanduanAwalStore
    {
        public bool SudahSelesai => AppPreferenceStore.IsPanduanAwalSelesai();

        public void SetSudahSelesai(bool selesai) => AppPreferenceStore.SetPanduanAwalSelesai(selesai);

        public bool PenomoranDiperiksa => AppPreferenceStore.IsPenomoranSuratDiperiksa();

        public void SetPenomoranDiperiksa(bool diperiksa) => AppPreferenceStore.SetPenomoranSuratDiperiksa(diperiksa);
    }
}
