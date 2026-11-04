using SuDesApp.Data.Models;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Pintasan halaman Beranda harus membaca satu sumber dengan sidebar
    /// (MenuSuratKatalog) dan menghitung per jenis persis seperti kunci
    /// register — bukan mencocokkan longgar pada kata pertama nama tampilan,
    /// yang dulu membuat label usang ("SKD UMUM") tetap muncul dan hitungan
    /// bisa tercampur antar jenis.
    /// </summary>
    public sealed class BerandaPintasanTests
    {
        [Fact]
        public void RegisterKosong_MemakaiSetBawaan_BerlabelKatalog()
        {
            var pintasan = BerandaViewModel.SusunPintasan(new Dictionary<string, int>());

            Assert.Equal(6, pintasan.Count);
            Assert.Equal(SuratConstants.SKD_UMUM, pintasan[0].Jenis);
            Assert.Equal("SKD Belum Menikah", pintasan[0].Nama);
            Assert.DoesNotContain(pintasan, p => p.Nama == "SKD UMUM");
            Assert.Equal(0, pintasan[0].Jumlah);
        }

        [Fact]
        public void HitunganMengurut_DanTidakTercampurAntarJenis()
        {
            var hitungan = new Dictionary<string, int>
            {
                [SuratConstants.PENGANTAR_ADMINDUK] = 9,
                [SuratConstants.PENGANTAR_SKCK] = 2
            };

            var pintasan = BerandaViewModel.SusunPintasan(hitungan);

            // Jenis paling sering dipakai di depan, dengan jumlahnya sendiri.
            Assert.Equal(SuratConstants.PENGANTAR_ADMINDUK, pintasan[0].Jenis);
            Assert.Equal(9, pintasan[0].Jumlah);

            // Pencocokan persis: "PENGANTAR" pada satu jenis tidak ikut
            // menghitung surat jenis lain yang namanya berbagi kata itu.
            var skck = Assert.Single(pintasan, p => p.Jenis == SuratConstants.PENGANTAR_SKCK);
            Assert.Equal(2, skck.Jumlah);

            // Urutan jumlah tidak pernah naik.
            for (int i = 1; i < pintasan.Count; i++)
                Assert.True(pintasan[i - 1].Jumlah >= pintasan[i].Jumlah);
        }

        [Fact]
        public void SemuaPintasan_BerasalDariMenuSuratKatalog()
        {
            var hitungan = new Dictionary<string, int> { [SuratConstants.SKU] = 3 };

            foreach (var p in BerandaViewModel.SusunPintasan(hitungan))
            {
                var pilihan = Assert.Single(MenuSuratKatalog.Semua, m => m.Jenis == p.Jenis);
                Assert.Equal(pilihan.Label, p.Nama);
            }
        }
    }
}
