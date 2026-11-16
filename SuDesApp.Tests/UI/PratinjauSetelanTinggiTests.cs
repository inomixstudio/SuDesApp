using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Pratinjau kop pada Pengaturan Surat dibatasi tinggi dan lebarnya mengikuti
    /// acuan kertas. Bentang gambar kop + blok tanda tangan pada kertas 600 px sekitar
    /// 275 px, dan gambar itu menggeser formulir ke bawah. Batas ini dijaga agar
    /// penyesuaian ukuran pratinjau tidak hilang saat halaman disunting.
    ///
    /// <para>
    /// Pemeriksaan XAML membaca berkas, bukan membangun halamannya: memuat
    /// <c>SetelanView</c> butuh sumber daya aplikasi yang dibuat pada utas STA,
    /// dan berbagi <c>Application</c> antar-utas membuat kuas <c>Freezable</c>
    /// gagal dipakai lintas utas — uji jadi acak gagal tanpa hubungannya dengan
    /// halaman ini.
    /// </para>
    /// </summary>
    public sealed class PratinjauSetelanTinggiTests
    {
        [Fact]
        public void Pratinjau_kop_dibatas_tinggi_dan_lebar()
        {
            var root = CariRootRepo();
            Assert.NotNull(root);

            string xaml = File.ReadAllText(
                Path.Combine(root!, "SuDesApp.Wpf", "Views", "SetelanView.xaml"));

            var viewbox = Regex.Match(xaml, @"<Viewbox\b[^>]*StretchDirection=""DownOnly""[^>]*>");
            Assert.True(viewbox.Success, "Viewbox pratinjau tidak ditemukan di SetelanView.xaml.");

            var batas = Regex.Match(viewbox.Value, @"MaxHeight=""(?<nilai>\d+)""");
            Assert.True(batas.Success, "Viewbox pratinjau tidak memakai MaxHeight.");
            Assert.Equal("330", batas.Groups["nilai"].Value);

            // Lebar Viewbox ikut dibatasi pada acuan kertas: tanpa ini Viewbox
            // mengisi ruang tinggi yang tersedia dan memperbesar gambar kop
            // melebihi lebar kertas, sehingga pratinjau melebar dan tidak proporsional.
            Assert.Contains(
                @"Width=""{Binding PratinjauLebarKertas}""",
                viewbox.Value,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Halaman tempat kop diukur harus cukup longgar untuk kop terpanjang,
        /// supaya alamat yang panjang tidak membelah pratinjau menjadi dua halaman
        /// (halaman kedua tidak pernah ditampilkan).
        /// </summary>
        [Fact]
        public void Halaman_ukur_kop_menampung_alamat_panjang()
        {
            string alamat = "Jl. Raya Desa Sumberjaya No. 123 RT 04 RW 02 Dusun Krajan " +
                            "Kelurahan Sumberjaya Kecamatan Tempuran Kabupaten Karawang " +
                            "Provinsi Jawa Barat Indonesia";

            var desa = new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = alamat,
                Kodepos = "41362",
                Email = "desa.sumberjaya@example.id",
            };

            byte[] pdf = KopSuratCetak.BuatPdf(desa, null, UkuranKertasSurat.A4);
            var dokumen = SudesPdf.Core.PdfDocument.Load(pdf);

            Assert.Equal(1, dokumen.PageCount);
        }

        /// <summary>
        /// Pratinjau dipangkas ke batas tinta + margin bawah: gambarnya jauh lebih
        /// pendek dari halaman ukur, tanpa pita kertas kosong di bawah garis ganda.
        /// </summary>
        [Fact]
        public async Task Pratinjau_kop_terpangkas_pas_dengan_tintanya()
        {
            var desa = new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = "Jl. Raya No. 1",
                Kodepos = "41362",
            };

            using var batal = new System.Threading.CancellationTokenSource();
            var gambar = await KopSuratPratinjauService.BuatAsync(
                desa, null, UkuranKertasSurat.A4, batal.Token);

            Assert.NotNull(gambar);

            // Halaman ukur 400 pt; pratinjau (kop + margin bawah) di bawah 200 pt.
            double skala = 620.0 / 595.28;
            double tinggiMaksimal = 200f * skala;

            Assert.True(
                gambar!.PixelHeight < tinggiMaksimal,
                $"Pratinjau kop masih menyisakan pita kertas: {gambar.PixelHeight}px " +
                $"(batas {tinggiMaksimal:0.#}px).");

            // Dan gambarnya tidak boleh lebih pendek dari kop itu sendiri.
            Assert.True(
                gambar.PixelHeight > 120f * skala,
                $"Pratinjau kop terpotong: {gambar.PixelHeight}px.");
        }

        /// <summary>
        /// Alamat panjang tidak boleh memotong pratinjau: halaman ukur menampungnya
        /// satu halaman, dan pemangkasan mengikuti batas tinta yang naik.
        /// </summary>
        [Fact]
        public async Task Pratinjau_kop_panjang_tetap_satu_gambar()
        {
            var pendek = new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = "Jl. Raya No. 1",
                Kodepos = "41362",
            };
            var panjang = new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = "Jl. Raya Desa Sumberjaya No. 123 RT 04 RW 02 Dusun Krajan " +
                         "Kelurahan Sumberjaya Kecamatan Tempuran Kabupaten Karawang " +
                         "Provinsi Jawa Barat Indonesia",
                Kodepos = "41362",
                Email = "desa.sumberjaya@example.id",
            };

            using var batal = new System.Threading.CancellationTokenSource();
            var gambarPendek = await KopSuratPratinjauService.BuatAsync(
                pendek, null, UkuranKertasSurat.A4, batal.Token);
            var gambarPanjang = await KopSuratPratinjauService.BuatAsync(
                panjang, null, UkuranKertasSurat.A4, batal.Token);

            Assert.NotNull(gambarPendek);
            Assert.NotNull(gambarPanjang);

            // Lebar sama (acuan kertas); gambar panjang lebih tinggi karena
            // baris alamat bertambah, dan tetap jauh di bawah halaman ukur.
            Assert.Equal(gambarPendek!.PixelWidth, gambarPanjang!.PixelWidth);
            Assert.True(
                gambarPanjang.PixelHeight > gambarPendek.PixelHeight,
                "Alamat panjang tidak menggeser batas tinta pratinjau.");

            double skala = 620.0 / 595.28;
            Assert.True(gambarPanjang.PixelHeight < 400f * skala);
        }

        private static string? CariRootRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
