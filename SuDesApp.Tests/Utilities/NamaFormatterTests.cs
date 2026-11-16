using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities
{
    /// <summary>
    /// Tampilan seragam daftar warga: nama yang bercampur penulisannya
    /// (KAPITAL semua / kecil semua) dinormalisasi kapital di awal tiap kata,
    /// sedangkan gelar bertitik tidak dirusak.
    /// </summary>
    public class NamaFormatterTests
    {
        [Theory]
        [InlineData("AHMAD SURYANA", "Ahmad Suryana")]
        [InlineData("ahmad suryana", "Ahmad Suryana")]
        [InlineData("aHmAd SuRyAnA", "Ahmad Suryana")]
        [InlineData("  siti   aminah  ", "Siti Aminah")]
        [InlineData("suryana-sholeh", "Suryana-Sholeh")]
        [InlineData("d'jamal", "D'Jamal")]
        [InlineData("Mohammad Riyan Pratama, S.H., M.M.", "Mohammad Riyan Pratama, S.H., M.M.")]
        [InlineData("H. Abdul Karim", "H. Abdul Karim")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void KapitalTiapKata_MenyeragamkanPenulisan(string? masukan, string diharapkan)
        {
            Assert.Equal(diharapkan, NamaFormatter.KapitalTiapKata(masukan));
        }
    }
}
