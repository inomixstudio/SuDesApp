// GambarBawaan.cs
// Pemuat gambar bawaan yang ditanam (embedded resource) di assembly
// SuDesApp.Core. Memakai resource — bukan berkas folder instalasi — supaya
// kepala naskah SK tetap terbentuk walau aplikasi dipindah atau folder
// instalasi tidak lengkap.
using System.IO;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Gambar bawaan assembly Core untuk dokumen PDF.
    /// </summary>
    public static class GambarBawaan
    {
        /// <summary>Nama resource Garuda Pancasila (RootNamespace + jalur berkas).</summary>
        private const string NamaGaruda = "SuDesApp.Resources.GarudaPancasila.png";

        /// <summary>
        /// Gambar Garuda Pancasila untuk puncak naskah SK. Format naskah dinas
        /// (Perbup 74/2020) dan contoh SK desa menempatkannya rata tengah di atas
        /// blok KEPALA DESA/KECAMATAN/KABUPATEN. Null bila resource tidak
        /// ditemukan — pemanggil melewati gambar agar dokumen tetap terbentuk.
        /// </summary>
        public static byte[]? Garuda()
        {
            using var aliran = typeof(GambarBawaan).Assembly.GetManifestResourceStream(NamaGaruda);
            if (aliran == null) return null;

            using var penampung = new MemoryStream();
            aliran.CopyTo(penampung);
            return penampung.ToArray();
        }
    }
}
