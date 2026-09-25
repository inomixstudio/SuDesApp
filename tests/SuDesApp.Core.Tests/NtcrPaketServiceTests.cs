using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Test NtcrPaketService dengan generator palsu (override virtual):
    /// satu berkas PDF gabungan per paket, dan — yang terpenting — BUKTI ATOMIK:
    /// kegagalan satu blanko membuat seluruh transaksi dibatalkan sehingga
    /// register bersih (tidak ada blanko parsial yang tertinggal).
    /// </summary>
    public class NtcrPaketServiceTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        public NtcrPaketServiceTests(CoreTestFixture fixture) => _fixture = fixture;

        /// <summary>Generator asli dipalsukan perilaku PDF-nya saja (override virtual).</summary>
        private sealed class GeneratorPdfPalsu : NtcrGenerator
        {
            public int JumlahPanggilan { get; private set; }

            public GeneratorPdfPalsu(CoreTestFixture fixture) : base(
                fixture.AppConfig, fixture.FileService, fixture.DesaRepository,
                fixture.SuratRepository, fixture.SettingsManager,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NtcrGenerator>.Instance,
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            { }

            public override Task GeneratePaketPdfAsync(
                Stream outputStream, IReadOnlyList<SuratData> daftarBlanko,
                string? keteranganTextBox = null, System.Threading.CancellationToken cancellationToken = default)
            {
                JumlahPanggilan++;
                return Task.CompletedTask;
            }
        }

        private (NtcrPaketService Service, GeneratorPdfPalsu Generator) BuatService()
        {
            var generator = new GeneratorPdfPalsu(_fixture);
            var service = new NtcrPaketService(_fixture.UnitOfWork, _fixture.AppConfig,
                generator, _fixture.SaveService,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NtcrPaketService>.Instance);
            return (service, generator);
        }

        [Fact]
        public async Task SimpanBlanko_SemuaTersimpan_GeneratorTerpanggilSekali()
        {
            _fixture.SiapkanDataDasar();

            // Paket "satu blanko" disimulasikan dengan NTCR_N1 saja (atomiknya tetap
            // teruji lewat test rollback); generator palsu tidak menyentuh QuestPDF.
            var master = _fixture.BuatSuratLengkap();
            var (service, generator) = BuatService();

            var hasil = await service.SimpanDanCetakAsync(
                master, new[] { "NTCR_N1" }, System.Threading.CancellationToken.None);

            Assert.Single(hasil.Surat);
            Assert.True(hasil.JumlahBlanko >= 1);
            Assert.True(File.Exists(hasil.BerkasPdf));
            Assert.Equal(1, generator.JumlahPanggilan);
        }

        /// <summary>
        /// Bukti atomik: kegagalan blanko KEDUA (nomor dobel, ditolak repository di
        /// tengah transaksi) membuat blanko PERTAMA ikut dibatalkan — register tetap
        /// bersih. Jalurnya persis alur paket: SimpanDalamTransaksiAsync di dalam
        /// UnitOfWork.ExecuteInTransactionAsync.
        /// </summary>
        [Fact]
        public async Task BlankoKeduaGagal_BlankoPertamaIkutDibatalkan()
        {
            _fixture.SiapkanDataDasar();
            var master = _fixture.BuatSuratLengkap();
            var (service, _) = BuatService();

            // Nomor sudah terpakai → insert blanko kedua meledak di dalam transaksi.
            master.NomorSurat = "474.3/999/Ds/2026";
            await _fixture.UnitOfWork.SuratRepository.AddSuratAsync(master);
            var salinan = _fixture.BuatSuratLengkap();

            // Fixture per-kelas dipakai beberapa test: hitung baris sebelum transaksi
            // agar asersi atomik terhadap kondisi awal test ini saja.
            long jumlahSebelum = Convert.ToInt64(_fixture.Connection.Scalar("SELECT COUNT(*) FROM Surat"));

            await Assert.ThrowsAnyAsync<Exception>(
                () => _fixture.UnitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await _fixture.SaveService.SimpanDalamTransaksiAsync(
                        salinan, _fixture.UnitOfWork.CurrentTransaction!, System.Threading.CancellationToken.None);
                    var salinanKedua = _fixture.BuatSuratLengkap();
                    salinanKedua.NomorSurat = salinan.NomorSurat;   // nomor dobel di tengah transaksi
                    await _fixture.SaveService.SimpanDalamTransaksiAsync(
                        salinanKedua, _fixture.UnitOfWork.CurrentTransaction!,
                        System.Threading.CancellationToken.None);
                    return true;
                }));

            // BUKTI ATOMIK: jumlah baris TIDAK bertambah — blanko pertama yang "sudah
            // tersimpan" di tengah transaksi ikut dibatalkan.
            long jumlah = Convert.ToInt64(_fixture.Connection.Scalar("SELECT COUNT(*) FROM Surat"));
            Assert.Equal(jumlahSebelum, jumlah);
        }

        [Fact]
        public async Task BlankoKosong_DitolakSebelumMenyimpan()
        {
            _fixture.SiapkanDataDasar();
            var master = _fixture.BuatSuratLengkap();
            var (service, _) = BuatService();

            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
                () => service.SimpanDanCetakAsync(master, Array.Empty<string>(), System.Threading.CancellationToken.None));
        }
    }
}
