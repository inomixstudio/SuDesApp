using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Naskah Peraturan Desa yang disusun per halaman. Halaman adalah bagian
    /// naskah yang disunting pengguna (pembukaan, batang tubuh, penutup), bukan
    /// paksaan satu lembar kertas: isi yang panjang tetap mengalir ke halaman
    /// PDF berikutnya.
    /// </summary>
    public sealed class NaskahPerdes
    {
        [JsonPropertyName("judul")]
        public string Judul { get; set; } = string.Empty;

        [JsonPropertyName("halaman")]
        public List<NaskahPerdesHalaman> Halaman { get; set; } = new();

        /// <summary>Salinan dalam supaya pengisian nomor/tanggal tidak mengubah sumber.</summary>
        public NaskahPerdes Salin() => new()
        {
            Judul = Judul,
            Halaman = Halaman.Select(h => h.Salin()).ToList()
        };
    }

    /// <summary>
    /// Satu bagian naskah. Setiap bagian dimulai di lembar PDF baru; isi yang
    /// panjang tetap mengalir ke lembar berikutnya.
    /// </summary>
    public sealed class NaskahPerdesHalaman
    {
        [JsonPropertyName("judul")]
        public string Judul { get; set; } = "Halaman";

        [JsonPropertyName("isi")]
        public string Isi { get; set; } = string.Empty;

        public bool Kosong => string.IsNullOrWhiteSpace(Isi);

        public NaskahPerdesHalaman Salin() => new()
        {
            Judul = Judul,
            Isi = Isi
        };
    }
}
