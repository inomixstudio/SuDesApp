// SKDGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (lewat BadanSurat), bukan lagi
// lapisan kompat iText.
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;
using System.Threading.Tasks;
using SuDesApp.ControlSurat; // Pastikan SettingsManager bisa diakses jika diperlukan langsung

namespace SuDesApp.GeneratorPdf
{
    public class SKDGenerator : SuratGeneratorBase
    {
        private const string DateFormatDb = "yyyy-MM-dd";
        private const string DateFormatUi = "dd-MM-yyyy";

        // Konstruktor diperbarui untuk menerima SettingsManager dan meneruskannya ke base
        public SKDGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<SKDGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
            _logger.LogInformation("SKDGenerator initialized, using default font from base class.");
        }

        protected override string JudulSurat => "SURAT KETERANGAN DESA";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                if (suratData.Warga == null)
                {
                    _logger.LogError("Data Warga null saat membuat konten SKD untuk Surat ID: {SuratId}", suratData.ID_Surat);
                    throw new InvalidOperationException("Data Warga tidak boleh null untuk membuat SKD.");
                }
                // Pemanggilan EnsureDesaDataLoadedAsync sudah ada di SuratGeneratorBase.GeneratePdfAsync

                _logger.LogInformation("Rendering content for SKD: NamaWarga={NamaWarga}, NomorSurat={NomorSurat}",
                    suratData.Warga?.Nama ?? "N/A", suratData.NomorSurat ?? "N/A");

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                // Pejabat mengikuti lembaga: “Kepala Desa …” atau “Lurah …”;
                // nama pejabat di atas memakai kolom Pengaturan Surat.
                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? suratData.Desa?.KepalaDesa ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{KopSurat.KataPejabat(suratData.Desa, suratData.PejabatPenandatangan)} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                string tanggalLahirFormatted = "[Tanggal Lahir]";
                if (!string.IsNullOrWhiteSpace(suratData.Warga?.TanggalLahir))
                {
                    if (DateTime.TryParseExact(suratData.Warga.TanggalLahir, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tglLahir))
                    {
                        tanggalLahirFormatted = tglLahir.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                    }
                    else
                    {
                        tanggalLahirFormatted = suratData.Warga.TanggalLahir;
                        _logger.LogWarning("Gagal memformat TanggalLahir Warga: {TanggalLahirWarga}", suratData.Warga.TanggalLahir);
                    }
                }
                string alamatLengkap = AlamatFormatter.Format(suratData.Warga);
                badan.TabelFormulir(
                [
                    ("Nama", suratData.Warga?.Nama ?? "[Nama Warga]"),
                    ("NIK", suratData.Warga?.NIK ?? "[NIK]"),
                    ("Tempat Tanggal Lahir", $"{(suratData.Warga?.TempatLahir ?? "[Tempat Lahir]")}, {tanggalLahirFormatted}"),
                    ("Jenis Kelamin", suratData.Warga?.JenisKelamin ?? "[Jenis Kelamin]"),
                    ("Agama", suratData.Warga?.Agama ?? "[Agama]"),
                    ("Status Perkawinan", suratData.Warga?.StatusPerkawinan ?? "[Status Perkawinan]"),
                    ("Pekerjaan", suratData.Warga?.Pekerjaan ?? "[Pekerjaan]"),
                    ("Alamat", alamatLengkap),
                ]);

                string? keteranganFinal = keteranganTextBox;
                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger.LogWarning("keteranganTextBox (suratData.Keterangan) is null or empty in SKDGenerator.ComposeBody. Generating default keterangan again.");
                    DesaData desa = suratData.Desa ?? new DesaData();

                    // Jenis wilayah mengikuti data desa: “Kabupaten Karawang” atau
                    // “Kota Bandung”, bukan selalu “Kabupaten”.
                    string namaKabupaten = KopSurat.NamaKabupaten(desa);
                    if (namaKabupaten.Length == 0) namaKabupaten = "[Kabupaten]";

                    keteranganFinal = string.Format(
                         "Adalah benar nama tersebut diatas adalah warga {4} {0} Kecamatan {1} {2} {3}, dan sampai saat ini masih berdomisili di desa kami dan belum pernah menikah.",
                         desa.NamaDesa ?? "[Nama Desa]",
                         desa.Kecamatan ?? "[Kecamatan]",
                         KopSurat.KataJenisKabupaten(desa),
                         namaKabupaten,
                         KopSurat.KataLembaga(desa)
                     );
                }

                // Alinea pembuka: baris pertama indentasi 50pt, baris lanjutan
                // rata kiri kembali ke margin — bentuk alinea surat resmi.
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentBarisPertama: 50,
                    jarakAtas: 10,
                    jarakBawah: 12);
                _logger.LogInformation("KeteranganFinal added to PDF: {KeteranganFinal}", keteranganFinal);

                // Kalimat penutup rata kiri: justify membuat baris yang pecah
                // jadi terlihat renggang dan janggal.
                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana keperluannya.",
                    rata: Rata.Kiri,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk SKD, NamaWarga={NamaWarga}", suratData.Warga?.Nama ?? "N/A");
                throw;
            }
        }
    }
}
