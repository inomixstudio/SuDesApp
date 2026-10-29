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
    // Badan surat memakai API SudesPdf.Layout langsung (BadanSurat), bukan lapisan kompat.
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

        public override async Task GeneratePdfAsync(Stream outputStream, int idSurat, string? keteranganTextBox = null)
        {

            try
            {
                _logger.LogDebug("Membuat PDF SKU untuk ID_Surat={ID_Surat}", idSurat);

                var suratData = await _suratRepository.GetByIdAsync(idSurat);
                if (suratData == null)
                {
                    _logger.LogError("Data surat tidak ditemukan untuk ID_Surat={ID_Surat}", idSurat);
                    throw new InvalidOperationException("Data surat tidak ditemukan.");
                }

                if (string.IsNullOrWhiteSpace(suratData.Warga?.Nama) || string.IsNullOrWhiteSpace(suratData.Warga?.NIK))
                {
                    _logger.LogError("Data warga tidak lengkap untuk ID_Surat={ID_Surat}", idSurat);
                    throw new InvalidOperationException("Data warga tidak lengkap untuk generate PDF.");
                }

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
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk menghasilkan PDF SKU.");
                _ = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk menghasilkan PDF SKU.");

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                string alamatLengkap = AlamatFormatter.Format(warga);
                badan.TabelFormulir(
                [
                    ("Nama", warga.Nama),
                    ("NIK", warga.NIK),
                    ("Tempat/Tanggal Lahir", $"{warga.TempatLahir}, {FormatTanggalLahir(warga.TanggalLahir)}"),
                    ("Jenis Kelamin", warga.JenisKelamin),
                    ("Agama", warga.Agama),
                    ("Status Perkawinan", warga.StatusPerkawinan),
                    ("Pekerjaan", warga.Pekerjaan ?? ""),
                    ("Alamat", alamatLengkap),
                ]);

                string? keteranganFinal = keteranganTextBox ?? suratData.Keterangan;
                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger.LogDebug("Keterangan SKU kosong; memakai keterangan bawaan.");
                    // Jenis wilayah mengikuti data desa (“Kabupaten Karawang”/“Kota Bandung”)
                    // — sebelumnya selalu “Kab.” walau desanya berada di wilayah kota.
                    string namaKabupaten = KopSurat.NamaKabupaten(suratData.Desa);
                    if (namaKabupaten.Length == 0) namaKabupaten = "Karawang";

                    keteranganFinal = $"Adalah benar warga Desa {suratData.Desa?.NamaDesa ?? "Sumberjaya"} Kecamatan {suratData.Desa?.Kecamatan ?? "Tempuran"} {KopSurat.KataJenisKabupaten(suratData.Desa)} {namaKabupaten} dan menurut sepengetahuan kami orang tersebut diatas mempunyai usaha :";
                }

                // Alinea pembuka: baris pertama indentasi 50pt, baris lanjutan
                // rata kiri kembali ke margin — bentuk alinea surat resmi.
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentBarisPertama: 50,
                    jarakAtas: 10,
                    jarakBawah: 12);

                // Tahun usaha kosong jangan tercetak "0 s/d Sekarang".
                int sejakTahun = suratData.SKU?.SejakTahun ?? 0;
                badan.TabelFormulir(
                [
                    ("Bidang Usaha", suratData.SKU?.BidangUsaha ?? ""),
                    ("Sejak Tahun", sejakTahun > 0 ? $"{sejakTahun} s/d Sekarang" : "")
                ]);

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana perlunya.",
                    rata: Rata.Kiri,
                    jarakAtas: 10,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk SKU");
                throw;
            }
        }

        private string FormatTanggalLahir(string? dbDate)
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
