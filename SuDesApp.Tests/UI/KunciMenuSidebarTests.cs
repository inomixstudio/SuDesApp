using SuDesApp.Services;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Penanda gembok di sidebar: menu vital harus sudah terlihat terkunci
    /// sebelum diklik, dengan tooltip yang menyebut jalan keluarnya, dan tanda
    /// itu hilang lagi begitu lisensi aktif.
    ///
    /// Diuji pada pohon menu kecil (bukan seluruh sidebar) karena aturan yang
    /// bisa salah ada di penyebaran tanda: rute mana yang vital, dan kapan
    /// sebuah akordeon ikut bergembok.
    /// </summary>
    public sealed class KunciMenuSidebarTests
    {
        private const string AlasanDemo = PenjagaLisensi.TooltipTerkunciDemo;

        private static NavItem Menu(string title, string route) => new()
        {
            Title = title,
            Route = route,
            Description = title + " — penjelasan."
        };

        [Fact]
        public void MenuVital_Ditandai_MenuBiasa_Tidak()
        {
            var warga = Menu("Data Warga", "warga");
            var surat = Menu("Surat Keterangan", "surat:SKTM");
            var register = Menu("Register Surat", "register-surat");
            var langganan = Menu("Langganan", "langganan");

            KunciMenuSidebar.Terapkan(
                new[] { warga, surat, register, langganan }, terkunci: true, AlasanDemo);

            Assert.True(warga.Terkunci);
            Assert.True(surat.Terkunci);
            Assert.False(register.Terkunci);
            Assert.False(langganan.Terkunci);
        }

        [Fact]
        public void Akordeon_BergembokBilaSeluruhAnaknyaTerkunci()
        {
            var buatSurat = new NavItem { Title = "Buat Surat Baru", IsAccordion = true };
            buatSurat.Children.Add(Menu("SKTM", "surat:SKTM"));
            buatSurat.Children.Add(Menu("Domisili", "surat:DOMISILI"));

            KunciMenuSidebar.Terapkan(new[] { buatSurat }, terkunci: true, AlasanDemo);

            Assert.True(buatSurat.Terkunci);
            Assert.All(buatSurat.Children, anak => Assert.True(anak.Terkunci));
        }

        [Fact]
        public void AkordeonCampuran_TanpaGembok()
        {
            // Formulir berisi templat (vital) DAN baris non-vital; gembok di
            // header akan menjanjikan kunci untuk isi yang sebenarnya terbuka.
            var formulir = new NavItem { Title = "Formulir", IsAccordion = true };
            formulir.Children.Add(Menu("Blanko", "template-pdf:DOMISILI"));
            formulir.Children.Add(Menu("Panduan", "dokumentasi"));

            KunciMenuSidebar.Terapkan(new[] { formulir }, terkunci: true, AlasanDemo);

            Assert.False(formulir.Terkunci);
            Assert.True(formulir.Children[0].Terkunci);
            Assert.False(formulir.Children[1].Terkunci);
        }

        [Fact]
        public void LisensiAktif_TandaDilepasLagi()
        {
            var warga = Menu("Data Warga", "warga");

            KunciMenuSidebar.Terapkan(new[] { warga }, terkunci: true, AlasanDemo);
            Assert.True(warga.Terkunci);

            KunciMenuSidebar.Terapkan(new[] { warga }, terkunci: false, pesan: null);

            Assert.False(warga.Terkunci);
            Assert.Equal("Data Warga — penjelasan.", warga.TooltipTampil);
            Assert.Equal("Data Warga", warga.TooltipCiut);
        }

        [Fact]
        public void Tooltip_MenuTerkunci_MenyebutJalanKeluar()
        {
            var warga = Menu("Data Warga", "warga");

            KunciMenuSidebar.Terapkan(new[] { warga }, terkunci: true, AlasanDemo);

            Assert.Contains("terkunci", warga.TooltipTampil);
            Assert.Contains("trial 14 hari", warga.TooltipTampil);
            Assert.Contains("Data Warga", warga.TooltipCiut);
            Assert.Contains("trial 14 hari", warga.TooltipCiut);
        }

        [Fact]
        public void Tooltip_MenuTerkunciSetelahTrial_MengarahkanPembayaran()
        {
            var warga = Menu("Data Warga", "warga");

            KunciMenuSidebar.Terapkan(
                new[] { warga }, terkunci: true,
                PenjagaLisensi.TooltipTerkunci(KeadaanLisensi.BacaSaja));

            Assert.Contains("pembayaran lisensi", warga.TooltipTampil);
            Assert.DoesNotContain("trial 14 hari", warga.TooltipTampil);
        }
    }
}
