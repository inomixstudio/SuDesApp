using Xunit;

namespace SuDesApp.Tests
{
    /// <summary>
    /// Uji yang menimpa penunjuk folder statis proses
    /// (<c>AppPreferenceStore.LokasiOverride</c>, <c>ApiKunci.LokasiOverride</c>,
    /// <c>BerkasLogin.LokasiOverride</c>) dijalankan berurutan, bukan paralel.
    /// Penimpaan itu global bagi seluruh uji: bila dua uji saling menimpa, uji
    /// API bisa membaca preferensi sesungguhnya dan gagal dinyalakan, atau uji
    /// lain membaca folder sementara yang salah.
    /// </summary>
    [CollectionDefinition("PreferensiBersama", DisableParallelization = true)]
    public sealed class PreferensiBersamaCollection
    {
    }
}
