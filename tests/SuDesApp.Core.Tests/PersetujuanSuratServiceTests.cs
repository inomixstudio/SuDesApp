using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Aturan label cetak alur persetujuan. Inilah inti janji "kebiasaan tidak
    /// berubah": surat yang tidak memakai alur — dan baru dicetak sekali — tidak
    /// pernah mendapat label tambahan di kertasnya.
    /// </summary>
    public class StatusPersetujuanSuratTests
    {
        [Fact]
        public void SuratBiasa_TanpaAlur_TanpaLabel()
        {
            Assert.Null(StatusPersetujuanSurat.LabelCetak(null, 0));
            Assert.Null(StatusPersetujuanSurat.LabelCetak("", 0));
            Assert.Null(StatusPersetujuanSurat.LabelCetak(null, 0));
        }

        [Fact]
        public void CetakanKedua_DanSeterusnya_DitandaiSalinan()
        {
            Assert.Equal("SALINAN — CETAKAN KE-2", StatusPersetujuanSurat.LabelCetak(null, 1));
            Assert.Equal("SALINAN — CETAKAN KE-3", StatusPersetujuanSurat.LabelCetak("TERBIT", 2));
        }

        [Fact]
        public void SuratDalamAlur_DiberiLabelStatusnya()
        {
            Assert.Equal("DRAF — MENUNGGU VERIFIKASI",
                StatusPersetujuanSurat.LabelCetak(StatusPersetujuanSurat.Diajukan, 0));
            Assert.Equal("SUDAH DIVERIFIKASI — MENUNGGU TANDA TANGAN",
                StatusPersetujuanSurat.LabelCetak(StatusPersetujuanSurat.Diverifikasi, 0));
        }

        [Fact]
        public void SuratDitolak_LabelPalingKeras_MeskiSudahDicetakBerkali()
        {
            Assert.Equal("DITOLAK — TIDAK SAH, JANGAN DIPAKAI",
                StatusPersetujuanSurat.LabelCetak(StatusPersetujuanSurat.Ditolak, 4));
        }

        [Fact]
        public void Status_KebalHurufBesarKecil_DanValid()
        {
            Assert.Equal(StatusPersetujuanSurat.Ditolak, StatusPersetujuanSurat.Normalisasi(" ditolak "));
            Assert.True(StatusPersetujuanSurat.Valid("diverifikasi"));
            Assert.True(StatusPersetujuanSurat.Valid(null));
            Assert.False(StatusPersetujuanSurat.Valid("SELESAI"));
            Assert.Equal("Tanpa persetujuan", StatusPersetujuanSurat.Tampilan(null));
        }

        [Fact]
        public void PeringatanCetak_MengikutiKesiapanSurat()
        {
            Assert.True(StatusPersetujuanSurat.PeringatanCetak(StatusPersetujuanSurat.Diajukan));
            Assert.True(StatusPersetujuanSurat.PeringatanCetak(StatusPersetujuanSurat.Diverifikasi));
            Assert.True(StatusPersetujuanSurat.PeringatanCetak(StatusPersetujuanSurat.Ditolak));
            Assert.False(StatusPersetujuanSurat.PeringatanCetak(StatusPersetujuanSurat.Terbit));
            Assert.False(StatusPersetujuanSurat.PeringatanCetak(null));
        }
    }

    /// <summary>
    /// Uji integrasi alur persetujuan pada skema SQLite asli: alur hanya berjalan
    /// bila diminta, tidak pernah mengubah status surat (Draft/Active/Cancelled),
    /// dan surat yang tidak diajukan tetap berperilaku seperti sebelumnya.
    /// </summary>
    public class PersetujuanSuratIntegrasiTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;

        public PersetujuanSuratIntegrasiTests(CoreTestFixture fixture) => _fixture = fixture;

        private IPersetujuanSuratService Service() =>
            new PersetujuanSuratService(
                _fixture.SuratRepository,
                _fixture.AppConfig,
                fileService: _fixture.FileService);

        private async Task<SuratData> SimpanSuratAsync(string status = "Active")
        {
            _fixture.SiapkanDataDasar();
            // Alur persetujuan membuang cache surat setiap kali menyimpan, sehingga
            // pengujian ini benar-benar memuat ulang barisnya dari database.
            _fixture.SiapkanCacheDesa();
            var surat = _fixture.BuatSuratLengkap();
            surat.Status = status;

            int id = await ((ISuratInsertion)_fixture.UnitOfWork.SuratRepository).InsertSuratAsync(surat);
            Assert.True(id > 0);

            var tersimpan = await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(id);
            return tersimpan!;
        }

        private async Task<SuratData> MuatUlangAsync(int id) =>
            (await _fixture.UnitOfWork.SuratRepository.GetByIdAsync(id))!;

        // =====================================================================
        // Kebiasaan lama tetap: tanpa diajukan, tidak ada yang berubah
        // =====================================================================

        [Fact]
        public async Task SuratBaruTanpaAlur_TidakPunyaStatusPersetujuan_TanpaLabel()
        {
            var surat = await SimpanSuratAsync();

            Assert.Null(surat.StatusPersetujuan);
            Assert.False(surat.LewatPersetujuan);
            Assert.False(surat.PunyaScanSurat);
            Assert.Equal(0, surat.JumlahCetak);
            Assert.Null(StatusPersetujuanSurat.LabelCetak(surat.StatusPersetujuan, surat.JumlahCetak));
            Assert.Equal("Tanpa persetujuan", surat.StatusPersetujuanTampil);
        }

        [Fact]
        public async Task DaftarRegister_TetapMembawaKolomBaruTanpaMengubahBarisLama()
        {
            var surat = await SimpanSuratAsync();

            var daftar = await _fixture.UnitOfWork.SuratRepository.GetFilteredAsync(
                new FilterConditions(), "ID_Surat", true, 0, 50);

            var baris = Assert.Single(daftar, s => s.ID_Surat == surat.ID_Surat);
            Assert.Null(baris.StatusPersetujuan);
            Assert.Equal(0, baris.JumlahCetak);
            Assert.Null(baris.FileScanSurat);
        }

        // =====================================================================
        // Langkah alur
        // =====================================================================

        [Fact]
        public async Task AjukanVerifikasi_MengisiAlur_TanpaMengubahStatusSurat()
        {
            var surat = await SimpanSuratAsync();

            var hasil = await Service().AjukanVerifikasiAsync(surat.ID_Surat, "Sekdes Uji");
            Assert.True(hasil.Berhasil, hasil.Pesan);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, hasil.Status);

            var sesudah = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, sesudah.StatusPersetujuan);
            Assert.Equal("Active", sesudah.Status);
        }

        [Fact]
        public async Task AlurLengkap_SampaiTerbit_MencatatPemeriksaDanPenandatangan()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();

            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat, "Sekdes Uji")).Berhasil);
            Assert.True((await layanan.VerifikasiAsync(surat.ID_Surat, "Isi sudah cocok", "Sekdes Uji")).Berhasil);
            Assert.True((await layanan.TandatanganiAsync(surat.ID_Surat, "Kades Uji")).Berhasil);

            var sesudah = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(StatusPersetujuanSurat.Terbit, sesudah.StatusPersetujuan);
            Assert.Equal("Sekdes Uji", sesudah.VerifikasiOleh);
            Assert.NotNull(sesudah.VerifikasiPada);
            Assert.Equal("Kades Uji", sesudah.DitandatanganiOleh);
            Assert.NotNull(sesudah.DitandatanganiPada);
            Assert.Equal("Isi sudah cocok", sesudah.CatatanPersetujuan);
            Assert.Equal("Active", sesudah.Status);

            // Surat resmi tetap membawa kode verifikasi yang sudah dimilikinya.
            Assert.False(string.IsNullOrWhiteSpace(sesudah.KodeVerifikasi));
            Assert.Null(StatusPersetujuanSurat.LabelCetak(sesudah.StatusPersetujuan, sesudah.JumlahCetak));
        }

        [Fact]
        public async Task AjukanVerifikasi_SuratDraft_Ditolak()
        {
            var draft = await SimpanSuratAsync("Draft");

            var hasil = await Service().AjukanVerifikasiAsync(draft.ID_Surat, "Operator");

            Assert.False(hasil.Berhasil);
            Assert.Contains("Aktif", hasil.Pesan);
            Assert.Null((await MuatUlangAsync(draft.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task AjukanVerifikasi_SuratDibatalkan_Ditolak()
        {
            var dibatalkan = await SimpanSuratAsync("Cancelled");

            var hasil = await Service().AjukanVerifikasiAsync(dibatalkan.ID_Surat, "Operator");

            Assert.False(hasil.Berhasil);
            Assert.Null((await MuatUlangAsync(dibatalkan.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task AjukanVerifikasi_SaatSudahDalamAlur_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();

            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);
            var ulang = await layanan.AjukanVerifikasiAsync(surat.ID_Surat);

            Assert.False(ulang.Berhasil);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, (await MuatUlangAsync(surat.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task Verifikasi_TanpaPengajuan_Ditolak()
        {
            var surat = await SimpanSuratAsync();

            var hasil = await Service().VerifikasiAsync(surat.ID_Surat, "catatan");

            Assert.False(hasil.Berhasil);
            Assert.Null((await MuatUlangAsync(surat.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task TandaTangan_SebelumDiverifikasi_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);

            var hasil = await layanan.TandatanganiAsync(surat.ID_Surat);

            Assert.False(hasil.Berhasil);
            var sesudah = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, sesudah.StatusPersetujuan);
            Assert.Null(sesudah.DitandatanganiOleh);
        }

        [Fact]
        public async Task Tolak_TanpaAlasan_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);

            var hasil = await layanan.TolakAsync(surat.ID_Surat, "   ");

            Assert.False(hasil.Berhasil);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, (await MuatUlangAsync(surat.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task Tolak_MenyimpanAlasan_DanBolehDiajukanUlangSetelahDiperbaiki()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);

            var tolak = await layanan.TolakAsync(surat.ID_Surat, "Nama pemohon belum lengkap");
            Assert.True(tolak.Berhasil, tolak.Pesan);

            var ditolak = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(StatusPersetujuanSurat.Ditolak, ditolak.StatusPersetujuan);
            Assert.Equal("Nama pemohon belum lengkap", ditolak.CatatanPersetujuan);
            Assert.Equal("DITOLAK — TIDAK SAH, JANGAN DIPAKAI",
                StatusPersetujuanSurat.LabelCetak(ditolak.StatusPersetujuan, ditolak.JumlahCetak));

            // Setelah diperbaiki, surat boleh diajukan lagi — catatan lama dibuang.
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);
            var diajukanUlang = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(StatusPersetujuanSurat.Diajukan, diajukanUlang.StatusPersetujuan);
            Assert.Null(diajukanUlang.CatatanPersetujuan);
        }

        [Fact]
        public async Task Tolak_SetelahTerbit_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);
            Assert.True((await layanan.VerifikasiAsync(surat.ID_Surat)).Berhasil);
            Assert.True((await layanan.TandatanganiAsync(surat.ID_Surat)).Berhasil);

            var hasil = await layanan.TolakAsync(surat.ID_Surat, "batal");

            Assert.False(hasil.Berhasil);
            Assert.Equal(StatusPersetujuanSurat.Terbit, (await MuatUlangAsync(surat.ID_Surat)).StatusPersetujuan);
        }

        [Fact]
        public async Task BatalkanPengajuan_KembaliJadiSuratBiasa()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);
            Assert.True((await layanan.VerifikasiAsync(surat.ID_Surat, "sudah diperiksa")).Berhasil);

            var hasil = await layanan.BatalkanPengajuanAsync(surat.ID_Surat);
            Assert.True(hasil.Berhasil, hasil.Pesan);

            var sesudah = await MuatUlangAsync(surat.ID_Surat);
            Assert.Null(sesudah.StatusPersetujuan);
            Assert.Null(sesudah.VerifikasiOleh);
            Assert.Null(sesudah.CatatanPersetujuan);
            Assert.Null(StatusPersetujuanSurat.LabelCetak(sesudah.StatusPersetujuan, sesudah.JumlahCetak));
        }

        [Fact]
        public async Task BatalkanPengajuan_SetelahTerbit_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();
            Assert.True((await layanan.AjukanVerifikasiAsync(surat.ID_Surat)).Berhasil);
            Assert.True((await layanan.VerifikasiAsync(surat.ID_Surat)).Berhasil);
            Assert.True((await layanan.TandatanganiAsync(surat.ID_Surat)).Berhasil);

            var hasil = await layanan.BatalkanPengajuanAsync(surat.ID_Surat);

            Assert.False(hasil.Berhasil);
            Assert.Equal(StatusPersetujuanSurat.Terbit, (await MuatUlangAsync(surat.ID_Surat)).StatusPersetujuan);
        }

        // =====================================================================
        // Cetakan & berkas scan
        // =====================================================================

        [Fact]
        public async Task CatatCetak_MenaikkanJumlahCetak_DanMemunculkanLabelSalinan()
        {
            var surat = await SimpanSuratAsync();
            var layanan = Service();

            Assert.Equal(1, await layanan.CatatCetakAsync(surat.ID_Surat));
            Assert.Equal(2, await layanan.CatatCetakAsync(surat.ID_Surat));

            var sesudah = await MuatUlangAsync(surat.ID_Surat);
            Assert.Equal(2, sesudah.JumlahCetak);
            Assert.Equal("SALINAN — CETAKAN KE-3",
                StatusPersetujuanSurat.LabelCetak(sesudah.StatusPersetujuan, sesudah.JumlahCetak));
        }

        [Fact]
        public async Task LampirkanScan_MenyimpanBerkas_LepasMenghapusnya()
        {
            var surat = await SimpanSuratAsync();
            var sumber = BuatBerkasUji(".pdf");

            try
            {
                var layanan = Service();
                var hasil = await layanan.LampirkanScanAsync(surat.ID_Surat, sumber);
                Assert.True(hasil.Berhasil, hasil.Pesan);

                var sesudah = await MuatUlangAsync(surat.ID_Surat);
                Assert.True(sesudah.PunyaScanSurat);

                var path = layanan.ResolveScanFullPath(sesudah.FileScanSurat);
                Assert.NotNull(path);
                Assert.True(File.Exists(path));

                var lepas = await layanan.LepasScanAsync(surat.ID_Surat);
                Assert.True(lepas.Berhasil, lepas.Pesan);

                var akhir = await MuatUlangAsync(surat.ID_Surat);
                Assert.False(akhir.PunyaScanSurat);
                Assert.False(File.Exists(path!));
            }
            finally
            {
                HapusBerkas(sumber);
            }
        }

        [Fact]
        public async Task LampirkanScan_EkstensiTidakDidukung_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            var sumber = BuatBerkasUji(".docx");

            try
            {
                var hasil = await Service().LampirkanScanAsync(surat.ID_Surat, sumber);

                Assert.False(hasil.Berhasil);
                Assert.Contains("PDF", hasil.Pesan);
                Assert.False((await MuatUlangAsync(surat.ID_Surat)).PunyaScanSurat);
            }
            finally
            {
                HapusBerkas(sumber);
            }
        }

        [Fact]
        public async Task LampirkanScan_BerkasTidakAda_Ditolak()
        {
            var surat = await SimpanSuratAsync();

            var hasil = await Service().LampirkanScanAsync(
                surat.ID_Surat, Path.Combine(Path.GetTempPath(), "tidak-ada-" + Guid.NewGuid().ToString("N") + ".pdf"));

            Assert.False(hasil.Berhasil);
            Assert.False((await MuatUlangAsync(surat.ID_Surat)).PunyaScanSurat);
        }

        // =====================================================================
        // Cetak: surat dalam alur tetap terbit, surat biasa tidak berubah
        // =====================================================================

        [Fact]
        public async Task SuratDalamAlur_TetapTerbitPdf_LabelTidakMenggagalkanCetak()
        {
            // Label baru hanya dipasang di jalur render surat; uji ini memastikan
            // api QuestPDF-nya benar saat dipakai generator surat sungguhan —
            // kegagalannya baru akan terasa saat operator mencetak surat draf/ditolak.
            var surat = _fixture.BuatSuratLengkap();
            surat.StatusPersetujuan = StatusPersetujuanSurat.Diajukan;

            byte[] pdf = await RenderPdfAsync(surat);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
        }

        [Fact]
        public async Task SuratTanpaAlur_TetapTerbitPdfSama()
        {
            var surat = _fixture.BuatSuratLengkap();
            Assert.Null(StatusPersetujuanSurat.LabelCetak(surat.StatusPersetujuan, surat.JumlahCetak));

            byte[] pdf = await RenderPdfAsync(surat);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
        }

        [Fact]
        public void NamaBerkasScan_MembersihkanNomorYangTidakBaku()
        {
            Assert.Equal("Surat_7_470_012_Ds_2026.pdf",
                PersetujuanSuratService.NamaBerkasScan(7, "470/012/Ds/2026", ".pdf"));
            Assert.Equal("Surat_9.jpg",
                PersetujuanSuratService.NamaBerkasScan(9, "XXX/{0:D3}/Ds/{2:yyyy}", "jpg"));
            Assert.Equal("Surat_11.png",
                PersetujuanSuratService.NamaBerkasScan(11, null, ".PNG"));
        }

        [Fact]
        public async Task SimpanPersetujuan_StatusTidakDikenal_Ditolak()
        {
            var surat = await SimpanSuratAsync();
            surat.StatusPersetujuan = "SELESAI";

            await Assert.ThrowsAsync<ArgumentException>(
                () => _fixture.UnitOfWork.SuratRepository.SimpanPersetujuanAsync(surat));
        }

        // =====================================================================
        // Pembantu
        // =====================================================================

        private static string BuatBerkasUji(string ekstensi)
        {
            var jalur = Path.Combine(Path.GetTempPath(), "uji-scan-" + Guid.NewGuid().ToString("N") + ekstensi);
            File.WriteAllText(jalur, "berkas uji");
            return jalur;
        }

        private static void HapusBerkas(string jalur)
        {
            try { File.Delete(jalur); } catch { /* sementara — dibiarkan */ }
        }

        /// <summary>Render satu surat sungguhan lewat generator surat desa.</summary>
        private async Task<byte[]> RenderPdfAsync(SuratData surat)
        {
            var generator = new DomisiliWargaGenerator(
                _fixture.AppConfig,
                _fixture.FileService,
                _fixture.DesaRepository,
                _fixture.SuratRepository,
                _fixture.SettingsManager,
                NullLogger<DomisiliWargaGenerator>.Instance,
                NullLoggerFactory.Instance);

            using var penampung = new MemoryStream();
            await generator.GeneratePdfAsync(penampung, surat, null);
            return penampung.ToArray();
        }
    }
}
