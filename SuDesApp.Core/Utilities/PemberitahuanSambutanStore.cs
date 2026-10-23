namespace SuDesApp.Utilities
{
    /// <summary>
    /// Penanda pemberitahuan sambutan di lonceng notifikasi ("Notifikasi aktif —
    /// selamat datang …"): cukup muncul SEKALI seumur pemasangan.
    ///
    /// Dipisahkan sebagai antarmuka supaya aturannya bisa diperiksa uji tanpa menyentuh
    /// berkas preferensi milik pengguna, dan supaya penyimpanannya tetap satu tempat
    /// (<see cref="AppPreferenceStore"/>).
    /// </summary>
    public interface IPemberitahuanSambutanStore
    {
        /// <summary>Pemberitahuan sambutan sudah pernah ditampilkan (jangan diulang).</summary>
        bool PernahDitampilkan { get; }

        /// <summary>
        /// <c>true</c> bila pemberitahuan sambutan perlu dikirim, dan sekaligus
        /// menandainya sudah ditampilkan. Panggilan berikutnya — termasuk pada sesi
        /// aplikasi berikutnya — selalu <c>false</c>.
        ///
        /// Penanda ditulis saat itu juga, bukan menunggu pengguna membersihkan daftar
        /// notifikasi, supaya sambutan tidak muncul lagi setiap aplikasi dibuka.
        /// </summary>
        bool AmbilSekali();
    }

    /// <summary>
    /// Penyimpanan penanda pemberitahuan sambutan di preferensi aplikasi
    /// (%LOCALAPPDATA%/SuDesApp/login_prefs.json). Kegagalan menulis diabaikan karena
    /// preferensi ini non-kritis — paling buruk sambutan muncul sekali lagi.
    /// </summary>
    public sealed class PemberitahuanSambutanStore : IPemberitahuanSambutanStore
    {
        public bool PernahDitampilkan => AppPreferenceStore.IsPemberitahuanSambutanPernahTampil();

        public bool AmbilSekali()
        {
            if (PernahDitampilkan)
            {
                return false;
            }

            AppPreferenceStore.SetPemberitahuanSambutanPernahTampil(true);
            return true;
        }
    }
}
