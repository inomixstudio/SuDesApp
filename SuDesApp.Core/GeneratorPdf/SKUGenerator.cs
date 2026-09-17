using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    // Badan surat memakai API QuestPDF langsung (BadanSurat), bukan lapisan kompat.
    public class SKUGenerator : SuratGeneratorBase
    {
        public SKUGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<SKUGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN USAHA";
        protected override bool UseDefaultLogo => true;
        protected override bool UseDefaultHeader => true;
        protected override bool UseDefaultFooter => true;
        protected override bool ShowPemohonInFooter => true;

        private const string DefaultTanggalFormat = "dd MMMM yyyy";
        private const string UnknownValue = "Tidak Diketahui";

        public override async Task GeneratePdfAsync(Stream outputStream, int idSurat, string keteranganTextBox = null)
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

                _logger.LogInformation("SuratData retrieved: NamaWarga={NamaWarga}, NIK={NIK}, NomorSurat={NomorSurat}, NamaDesa={NamaDesa}, KepalaDesa={KepalaDesa}, Keterangan={Keterangan}",
                    suratData.Warga.Nama, suratData.Warga.NIK, suratData.NomorSurat, suratData.Desa.NamaDesa, suratData.Desa.KepalaDesa, suratData.Keterangan);

                await base.GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghasilkan PDF untuk ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string keteranganTextBox = null)
        {
            try
            {
                _logger.LogInformation("Rendering content: NamaWarga={NamaWarga}, NamaDesa={NamaDesa}, KepalaDesa={KepalaDesa}, KeteranganTextBox={KeteranganTextBox}, SuratData.Keterangan={SuratDataKeterangan}",
                    suratData.Warga.Nama, suratData.Desa.NamaDesa, suratData.Desa.KepalaDesa, keteranganTextBox, suratData.Keterangan);

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                string alamatLengkap = AlamatFormatter.Format(suratData.Warga);
                badan.TabelFormulir(
                [
                    ("Nama", suratData.Warga.Nama),
                    ("NIK", suratData.Warga.NIK),
                    ("Tempat/Tanggal Lahir", $"{suratData.Warga.TempatLahir}, {FormatTanggalLahir(suratData.Warga.TanggalLahir)}"),
                    ("Jenis Kelamin", suratData.Warga.JenisKelamin),
                    ("Agama", suratData.Warga.Agama),
                    ("Status Perkawinan", suratData.Warga.StatusPerkawinan),
                    ("Pekerjaan", suratData.Warga.Pekerjaan ?? ""),
                    ("Alamat", alamatLengkap),
                ]);

                string keteranganFinal = keteranganTextBox ?? suratData.Keterangan;
                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger.LogWarning("keteranganTextBox and suratData.Keterangan are null or empty in SKUGenerator.ComposeBody. Using default keterangan.");
                    keteranganFinal = $"Adalah benar warga Desa {suratData.Desa?.NamaDesa ?? "Sumberjaya"} Kecamatan {suratData.Desa?.Kecamatan ?? "Tempuran"} Kab. {suratData.Desa?.Kabupaten ?? "Karawang"} dan menurut sepengetahuan kami orang tersebut diatas mempunyai usaha :";
                }

                // Indentasi 50pt untuk seluruh baris, mengikuti bentuk surat desa yang lama.
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentKiri: 50,
                    jarakAtas: 15,
                    jarakBawah: 15);

                // Tahun usaha kosong jangan tercetak "0 s/d Sekarang".
                int sejakTahun = suratData.SKU?.SejakTahun ?? 0;
                badan.TabelFormulir(
                [
                    ("Bidang Usaha", suratData.SKU?.BidangUsaha ?? ""),
                    ("Sejak Tahun", sejakTahun > 0 ? $"{sejakTahun} s/d Sekarang" : "")
                ]);

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana perlunya.",
                    rata: Rata.Justify,
                    jarakAtas: 15,
                    jarakBawah: 20);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk SKU, NamaWarga={NamaWarga}", suratData.Warga.Nama);
                throw;
            }
        }

        private string FormatTanggalLahir(string dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate))
                return UnknownValue;

            try
            {
                if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }

                if (DateTime.TryParse(dbDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return parsedDate.ToString(DefaultTanggalFormat, new CultureInfo("id-ID"));
                }

                _logger.LogWarning("Failed to parse TanggalLahir '{TanggalLahir}' to format {Format}", dbDate, DefaultTanggalFormat);
                return dbDate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing TanggalLahir '{TanggalLahir}'", dbDate);
                return dbDate;
            }
        }
    }
}
