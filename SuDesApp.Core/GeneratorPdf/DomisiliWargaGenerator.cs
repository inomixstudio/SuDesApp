// DomisiliWargaGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class DomisiliWargaGenerator : SuratGeneratorBase
    {
        private const string DefaultTanggalFormat = "dd MMMM yyyy"; // Format tanggal Indonesia
        private const string UnknownValue = "Tidak Diketahui";

        public DomisiliWargaGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<DomisiliWargaGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN DOMISILI";
        protected override bool UseDefaultLogo => false;
        protected override bool UseDefaultHeader => true;
        protected override bool UseDefaultFooter => true;
        protected override bool ShowPemohonInFooter => false;

        public override async Task GeneratePdfAsync(Stream outputStream, int idSurat, string? keteranganTextBox = null)
        {
            try
            {
                _logger.LogInformation("Generating PDF for ID_Surat={ID_Surat}, KeteranganTextBox={KeteranganTextBox}", idSurat, keteranganTextBox);

                var suratData = await _suratRepository.GetByIdAsync(idSurat);
                if (suratData == null)
                {
                    _logger.LogError("Data surat tidak ditemukan untuk ID_Surat={ID_Surat}", idSurat);
                    throw new InvalidOperationException("Data surat tidak ditemukan.");
                }

                if (string.IsNullOrWhiteSpace(suratData.Warga?.Nama) || string.IsNullOrWhiteSpace(suratData.Warga?.NIK))
                {
                    _logger.LogError("Data warga tidak lengkap untuk ID_Surat={ID_Surat}: Nama={Nama}, NIK={NIK}", idSurat, suratData.Warga?.Nama, suratData.Warga?.NIK);
                    throw new InvalidOperationException("Data warga tidak lengkap untuk generate PDF.");
                }

                if (string.IsNullOrWhiteSpace(suratData.Desa?.NamaDesa) ||
                    string.IsNullOrWhiteSpace(suratData.Desa?.KepalaDesa) ||
                    string.IsNullOrWhiteSpace(suratData.Desa?.Kecamatan) ||
                    string.IsNullOrWhiteSpace(suratData.Desa?.Kabupaten))
                {
                    _logger.LogError("Data desa tidak lengkap untuk ID_Surat={ID_Surat}: NamaDesa={NamaDesa}, KepalaDesa={KepalaDesa}, Kecamatan={Kecamatan}, Kabupaten={Kabupaten}",
                        idSurat, suratData.Desa?.NamaDesa ?? "null", suratData.Desa?.KepalaDesa ?? "null",
                        suratData.Desa?.Kecamatan ?? "null", suratData.Desa?.Kabupaten ?? "null");
                    throw new InvalidOperationException("Data desa tidak lengkap untuk generate PDF.");
                }

                _logger.LogInformation("SuratData retrieved: NamaWarga={NamaWarga}, NIK={NIK}, NomorSurat={NomorSurat}, NamaDesa={NamaDesa}, KepalaDesa={KepalaDesa}, Keterangan={Keterangan}",
                    suratData.Warga?.Nama, suratData.Warga?.NIK, suratData.NomorSurat, suratData.Desa?.NamaDesa, suratData.Desa?.KepalaDesa, suratData.Keterangan);

                await base.GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghasilkan PDF untuk ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk menghasilkan PDF Domisili Warga.");
                var desa = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk menghasilkan PDF Domisili Warga.");

                _logger.LogInformation("Rendering content: NamaWarga={NamaWarga}, NamaDesa={NamaDesa}, KepalaDesa={KepalaDesa}, KeteranganTextBox={KeteranganTextBox}, SuratData.Keterangan={SuratDataKeterangan}",
                    warga.Nama, desa.NamaDesa, desa.KepalaDesa, keteranganTextBox, suratData.Keterangan);

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

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

                // Tambahan: Masukkan keteranganTextBox atau fallback ke suratData.Keterangan
                string? keteranganFinal = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan;

                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger?.LogWarning("KeteranganFinal kosong setelah fallback. Tidak menambahkan paragraf keterangan untuk ID_Surat={ID_Surat}", suratData.ID_Surat);
                }
                else
                {
                    // Indentasi 50pt untuk seluruh baris (bentuk surat desa yang lama).
                    badan.Paragraf(keteranganFinal,
                        rata: Rata.Justify,
                        indentKiri: 50,
                        jarakAtas: 15,
                        jarakBawah: 20);
                    _logger?.LogInformation("KeteranganFinal ditambahkan ke PDF: {KeteranganFinal}", keteranganFinal);
                }

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana keperluannya.",
                    rata: Rata.Justify,
                    jarakBawah: 20);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Domisili Warga, NamaWarga={NamaWarga}", suratData.Warga?.Nama);
                throw;
            }
        }
        private string FormatTanggalLahir(string? dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate))
                return UnknownValue;

            try
            {
                // Coba parse tanggal dari format DB (yyyy-MM-dd)
                if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }
                // Fallback jika format DB bukan yyyy-MM-dd, coba parse dengan format lain atau default
                if (DateTime.TryParse(dbDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }

                _logger.LogWarning("Failed to parse TanggalLahir '{TanggalLahir}' to format {Format}", dbDate, DefaultTanggalFormat);
                return dbDate; // Kembalikan string asli jika gagal
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing TanggalLahir '{TanggalLahir}'", dbDate);
                return dbDate; // Kembalikan string asli jika error
            }
        }
    }
}
