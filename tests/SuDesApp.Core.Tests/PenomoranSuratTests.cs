using System.Text.Json;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Kunci regresi penomoran surat sesuai <c>docs/referensi-penomoran-surat.md</c>
    /// (Perbup Kab. Karawang No. 74 Tahun 2020). Kegagalan test di sini berarti
    /// nomor surat yang terbit tidak lagi sesuai regulasi — bukan sekadar
    /// perubahan kosmetik pada format.
    /// </summary>
    public sealed class PenomoranSuratTests
    {
        /// <summary>Format baku seluruh surat keterangan desa menurut Perbup 74/2020.</summary>
        private const string FormatKeteranganDesa = "470/{0:D3}/Ds/{2:yyyy}";

        /// <summary>13 surat keterangan desa yang wajib memakai satu kode 470 dan satu deret.</summary>
        private static readonly string[] SuratKeteranganDesa =
        {
            "SKD_UMUM", "DOMISILI_WARGA", "INSTANSI", "SKU", "PENGANTAR_SKCK",
            "IZIN_ORTU", "GARAPAN_SAWAH", "KEMATIAN", "SKTM", "BEDANAMA",
            "KENAL_LAHIR", "AHLI_WARIS", "IJIN_TINGGAL"
        };

        private static List<Entri> BacaKonfigurasi() =>
            JsonSerializer.Deserialize<List<Entri>>(
                CoreTestFixture.ReadProjectFile(
                    Path.Combine("SuDesApp.Core", "Configuration", "JenisSuratConfig.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<Entri>();

        private sealed class Entri
        {
            public string NamaJenis { get; set; } = string.Empty;
            public string KodeJenis { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string NomorFormat { get; set; } = string.Empty;
            public bool IsSharedNumbering { get; set; }
            public bool IsKeteranganDesa { get; set; }
        }

        // ---------- Kunci format 470 ----------

        [Fact]
        public void Konfigurasi_SuratKeteranganDesa_SemuaMemakai470DanDeretBersama()
        {
            var config = BacaKonfigurasi();

            foreach (var nama in SuratKeteranganDesa)
            {
                var entri = Assert.Single(config, e => e.NamaJenis == nama);
                Assert.Equal(FormatKeteranganDesa, entri.NomorFormat);
                Assert.True(entri.IsSharedNumbering,
                    $"{nama} harus ikut penomoran bersama (satu deret 470).");
                Assert.True(entri.IsKeteranganDesa,
                    $"{nama} harus ditandai sebagai surat keterangan desa.");
            }
        }

        [Fact]
        public void Konfigurasi_TidakAdaSuratKeteranganDesaBerAwalanLain()
        {
            // Kode lama 474 / 474.4 / 510 / 570 / 593.2 tidak ada dalam Perbup
            // 74/2020 untuk naskah desa. Kalau salah satu muncul lagi pada surat
            // keterangan desa, berarti format lama yang salah sudah merayap balik
            // ke berkas konfigurasi. Blanko NTCR dikecualikan: 474.3/474.2 berasal
            // dari Keputusan Dirjen Bimas Islam 473/2020, bukan dari naskah desa.
            var config = BacaKonfigurasi()
                .Where(e => e.IsKeteranganDesa
                            && !e.NamaJenis.StartsWith("NTCR_N", StringComparison.Ordinal)
                            && !e.NomorFormat.StartsWith("470/", StringComparison.Ordinal))
                .Select(e => $"{e.NamaJenis}={e.NomorFormat}")
                .ToList();

            Assert.Empty(config);
        }

        [Fact]
        public void Konfigurasi_PengecualianTetapSesuaiDasarDiLuarPerbup()
        {
            var config = BacaKonfigurasi();

            var rekkor = Assert.Single(config, e => e.NamaJenis == "REKENING_KORAN");
            Assert.Equal("130/{0:D3}/Ds/{2:yyyy}", rekkor.NomorFormat);
            Assert.False(rekkor.IsSharedNumbering, "Rekening Koran punya deret sendiri.");

            var tmpl = Assert.Single(config, e => e.NamaJenis == "TEMPLATE_SURAT");
            Assert.Equal("TMPL/{0:D3}/Ds/{2:yyyy}", tmpl.NomorFormat);
            Assert.False(tmpl.IsSharedNumbering, "Template surat punya deret sendiri.");

            // Blanko N1–N6: satu kode 474.3, satu deret bersama.
            var ntcr = config.Where(e => e.NamaJenis.StartsWith("NTCR_N")).ToList();
            Assert.Equal(7, ntcr.Count); // N1–N6 + N8
            foreach (var e in ntcr.Where(e => e.NamaJenis != "NTCR_N8"))
            {
                Assert.Equal("474.3/{0:D3}/Ds/{2:yyyy}", e.NomorFormat);
            }

            var n8 = Assert.Single(config, e => e.NamaJenis == "NTCR_N8");
            Assert.Equal("474.2/{0:D3}/Ds/{2:yyyy}", n8.NomorFormat);
            Assert.False(n8.IsSharedNumbering, "Numpang Nikah punya deret sendiri.");
        }

        [Fact]
        public void Konfigurasi_SemuaJenisPunyaFormatDanAwalanValid()
        {
            foreach (var entri in BacaKonfigurasi())
            {
                Assert.False(string.IsNullOrWhiteSpace(entri.NomorFormat),
                    $"{entri.NamaJenis} tidak punya NomorFormat.");
                Assert.True(
                    PenomoranSuratService.AwalanValid(
                        PenomoranSuratService.AmbilAwalan(entri.NomorFormat), out var pesan),
                    $"{entri.NamaJenis}: {pesan}");
            }
        }

        [Fact]
        public void Konfigurasi_TidakAdaNamaJenisGanda()
        {
            var duplikat = BacaKonfigurasi()
                .GroupBy(e => e.NamaJenis, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.Empty(duplikat);
        }

        // ---------- PenomoranSuratService: helper format ----------

        [Theory]
        [InlineData("470/{0:D3}/Ds/{2:yyyy}", "470")]
        [InlineData("474.3/{0:D3}/Ds/{2:yyyy}", "474.3")]
        [InlineData("TMPL/{0:D3}/Ds/{2:yyyy}", "TMPL")]
        [InlineData("130/{0:D3}/Ds/{2:yyyy}", "130")]
        [InlineData("470", "470")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void AmbilAwalan_MengambilBagianSebelumGarisMiring(string? format, string harapan)
            => Assert.Equal(harapan, PenomoranSuratService.AmbilAwalan(format));

        [Theory]
        [InlineData("471.1", "470/{0:D3}/Ds/{2:yyyy}", "471.1/{0:D3}/Ds/{2:yyyy}")]
        [InlineData("471", "130/{0:D3}/Ds/{2:yyyy}", "471/{0:D3}/Ds/{2:yyyy}")]
        [InlineData("470", "TMPL/{0:D3}/Ds/{2:yyyy}", "470/{0:D3}/Ds/{2:yyyy}")]
        public void BangunFormat_MenggantiAwalanTanpaMengubahBentuknya(
            string awalan, string formatBawaan, string harapan)
            => Assert.Equal(harapan, PenomoranSuratService.BangunFormat(formatBawaan, awalan));

        [Fact]
        public void BangunFormat_FormatKosong_TetapMenghasilkanNomorYangSah()
        {
            var format = PenomoranSuratService.BangunFormat(string.Empty, "471");

            Assert.Equal("471/{0:D3}/Ds/{2:yyyy}", format);
            Assert.Equal("471/001/Ds/2026", PenomoranSuratService.Contoh(format, 1, 2026));
        }

        [Theory]
        [InlineData("470", true)]
        [InlineData("471.1", true)]
        [InlineData("474.3", true)]
        [InlineData("TMPL", true)]
        [InlineData("a-b_c", true)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("470/471", false)]
        [InlineData("470 471", false)]
        [InlineData("470#", false)]
        public void AwalanValid_MenolakKarakterBerbahaya(string awalan, bool valid)
            => Assert.Equal(valid, PenomoranSuratService.AwalanValid(awalan, out _));

        [Fact]
        public void AwalanValid_MessageDlamaTerlaluPanjang()
        {
            var panjang = new string('9', PenomoranSuratServiceTests_BatasPanjang + 1);

            Assert.False(PenomoranSuratService.AwalanValid(panjang, out var pesan));
            Assert.Contains("maksimal", pesan, StringComparison.OrdinalIgnoreCase);
        }

        private const int PenomoranSuratServiceTests_BatasPanjang = 16;

        [Fact]
        public void Contoh_MenghasilkanNomorSesuaiContohPerbup()
        {
            // Contoh pada dokumen: 470/015/Ds/2026
            Assert.Equal("470/015/Ds/2026",
                PenomoranSuratService.Contoh("470/{0:D3}/Ds/{2:yyyy}", 15, 2026));
            Assert.Equal("474.3/006/Ds/2026",
                PenomoranSuratService.Contoh("474.3/{0:D3}/Ds/{2:yyyy}", 6, 2026));
        }

        [Fact]
        public void Contoh_FormatRusak_TidakLemparException()
        {
            // Kurung kurawal tidak ditutup: string.Format akan melempar
            // FormatException, dan pengaturan penomoran tidak boleh crash
            // hanya karena format bawaan yang gagal dibaca.
            Assert.Equal("{0:D3", PenomoranSuratService.Contoh("{0:D3", 1, 2026));
        }

        // ---------- Deteksi bentrok awalan ----------

        [Fact]
        public void CariBentrok_DeretSendiriDenganAwalanSama_DiPeringatkan()
        {
            var entri = new List<PenomoranSuratEntri>
            {
                new() { DisplayName = "Rekening Koran", Awalan = "471", IsSharedNumbering = false },
                new() { DisplayName = "Template Surat", Awalan = "471", IsSharedNumbering = false }
            };

            var bentrok = PenomoranSuratService.CariBentrok(entri);

            Assert.Single(bentrok);
            Assert.Contains("471", bentrok[0]);
        }

        [Fact]
        public void CariBentrok_SatuGrupBersama_Diidakatkan()
        {
            // 13 surat keterangan desa semuanya ber-awalan 470 tapi satu deret:
            // ini kondisi yang benar, bukan bentrok.
            var entri = SuratKeteranganDesa
                .Select(n => new PenomoranSuratEntri { DisplayName = n, Awalan = "470", IsSharedNumbering = true })
                .ToList();

            Assert.Empty(PenomoranSuratService.CariBentrok(entri));
        }

        [Fact]
        public void CariBentrok_KonfigurasiBawaan_TidakAdaBentrok()
        {
            var entri = BacaKonfigurasi()
                .Select(e => new PenomoranSuratEntri
                {
                    NamaJenis = e.NamaJenis,
                    DisplayName = e.DisplayName,
                    Awalan = PenomoranSuratService.AmbilAwalan(e.NomorFormat),
                    IsSharedNumbering = e.IsSharedNumbering
                })
                .ToList();

            Assert.Empty(PenomoranSuratService.CariBentrok(entri));
        }
    }
}
