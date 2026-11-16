using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Surat Keterangan Penghasilan — bentuk mengikuti SKTM: kop, judul + nomor,
    /// tabel pejabat, tabel pemohon (data diri lengkap), tabel data penghasilan,
    /// alinea keterangan, lalu blok tanda tangan dengan pemohon di kolom kiri.
    /// Rincian penghasilan dibaca dari <see cref="SuratData.Penghasilan"/> yang
    /// disimpan sebagai JSON pada AdditionalData.
    /// </summary>
    public class PenghasilanGenerator : SuratGeneratorBase
    {
        public PenghasilanGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<PenghasilanGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        public string UnknownValue = "Tidak Diketahui";
        public string? DefaultTanggalFormat = "dd MMMM yyyy";

        protected override string JudulSurat => "SURAT KETERANGAN PENGHASILAN";
        protected override bool UseDefaultLogo => true;
        protected override bool UseDefaultHeader => true;
        protected override bool UseDefaultFooter => true;

        // Pemohon ikut menandatangani, sejajar Kepala Desa — pola yang sama
        // dengan Pengantar SKCK.
        protected override bool ShowPemohonInFooter => true;

        public override async Task GeneratePdfAsync(Stream outputStream, int idSurat, string? keteranganTextBox = null)
        {
            try
            {
                _logger.LogDebug("Membuat PDF Penghasilan untuk ID_Surat={ID_Surat}", idSurat);

                var suratData = await _suratRepository.GetByIdAsync(idSurat);
                if (suratData == null)
                {
                    _logger.LogError("Data surat tidak ditemukan untuk ID_Surat={ID_Surat}", idSurat);
                    throw new InvalidOperationException("Data surat tidak ditemukan.");
                }

                await base.GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghasilkan PDF Penghasilan untuk ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk menghasilkan PDF Penghasilan.");
                var desa = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk menghasilkan PDF Penghasilan.");

                var penghasilan = suratData.Penghasilan ?? PenghasilanData.DariJson(suratData.AdditionalData);
                if (penghasilan == null && !PratinjauIsian.Aktif)
                {
                    throw new InvalidOperationException(
                        "Data penghasilan belum lengkap untuk menghasilkan PDF Penghasilan.");
                }

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                // Data diri pemohon lengkap — satu pola dengan SKTM.
                badan.TabelFormulir(
                [
                    ("Nama", warga.Nama),
                    ("NIK", warga.NIK),
                    ("Tempat Tanggal Lahir", $"{warga.TempatLahir}, {FormatTanggalLahir(warga.TanggalLahir)}"),
                    ("Jenis Kelamin", warga.JenisKelamin),
                    ("Agama", warga.Agama),
                    ("Status Perkawinan", warga.StatusPerkawinan),
                    ("Pekerjaan", warga.Pekerjaan),
                    ("Alamat", AlamatFormatter.Format(warga)),
                ]);

                // Data penghasilan: null dibiarkan kosong — pada pratinjau kolom
                // kosong dicetak titik-titik otomatis oleh TabelFormulir.
                badan.Paragraf("Adapun data penghasilan yang bersangkutan :", jarakAtas: 10, jarakBawah: 10);
                badan.TabelFormulir(
                [
                    ("Pekerjaan / Usaha", penghasilan?.PekerjaanUsaha),
                    ("Penghasilan per Bulan", penghasilan?.PenghasilanPerBulan),
                    ("Jumlah Tanggungan", penghasilan?.JumlahTanggungan),
                ]);

                string keteranganFinal = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : $"Adalah benar warga Desa {desa.NamaDesa} Kecamatan {desa.Kecamatan} " +
                      $"{KopSurat.KataJenisKabupaten(desa)} {KopSurat.NamaKabupaten(desa)} " +
                      $"dan penghasilan yang dinyatakan di atas adalah benar-benar nyata " +
                      $"berdasarkan pengakuan pemohon dan pemeriksaan kami.";

                // Alinea resmi: baris pertama indentasi 50pt, lanjutan rata kiri.
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentBarisPertama: 50,
                    jarakAtas: 10,
                    jarakBawah: 12);

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya untuk dipergunakan sebagaimana mestinya.",
                    rata: Rata.Justify,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Penghasilan");
                throw;
            }
        }

        private string FormatTanggalLahir(string? dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate))
                return UnknownValue;

            try
            {
                // Tanggal dari DB memakai format yyyy-MM-dd; format lain dicoba
                // dengan parser umum sebelum diteruskan apa adanya.
                if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }
                if (DateTime.TryParse(dbDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }

                _logger.LogWarning("Tanggal lahir warga tidak bisa dibaca dengan format {Format}.", DefaultTanggalFormat);
                return dbDate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tanggal lahir warga gagal diurai.");
                return dbDate;
            }
        }
    }
}
