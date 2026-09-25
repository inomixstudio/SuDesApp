// Di dalam SKCKGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    public class SKCKGenerator : SuratGeneratorBase
    {
        private const string DefaultKeperluan = "MELAMAR PEKERJAAN";
        private const string UnknownValue = "Tidak Diketahui";

        public SKCKGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<SKCKGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN PENGANTAR PEMBUATAN SURAT KETERANGAN CATATAN KEPOLISIAN (SKCK)";
        protected override bool UseDefaultLogo => false;
        protected override bool ShowPemohonInFooter => true;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                AddPeneranganSection(badan, suratData);
                AddDataWargaSection(badan, suratData);
                AddPernyataanSection(badan, suratData);
                AddKeteranganSection(badan, keteranganTextBox!);
                AddPenutupSection(badan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add content for NamaWarga={NamaWarga}", suratData.Warga?.Nama);
                throw;
            }
        }

        private void AddPeneranganSection(BadanSurat badan, SuratData suratData)
        {
            badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakAtas: 5, jarakBawah: 5);

            badan.TabelFormulir(
            [
                ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
            ]);

            badan.Paragraf("Dengan ini menerangkan bahwa :", jarakAtas: 5, jarakBawah: 5);
        }

        private void AddDataWargaSection(BadanSurat badan, SuratData suratData)
        {
            var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk membuat PDF SKCK.");

            badan.TabelFormulir(
            [
                ("Nama", warga.Nama ?? UnknownValue),
                ("Nomor KTP", warga.NIK ?? UnknownValue),
                ("Tempat/Tgl Lahir", $"{warga.TempatLahir ?? UnknownValue}, {FormatTanggalLahir(warga.TanggalLahir)}"),
                ("Kewarganegaraan", warga.Kewarganegaraan ?? UnknownValue),
                ("Jenis Kelamin", warga.JenisKelamin ?? UnknownValue),
                ("Status Perkawinan", warga.StatusPerkawinan ?? UnknownValue),
                ("Pendidikan", warga.Pendidikan ?? UnknownValue),
                ("Alamat", FormatAlamat(suratData)),
            ]);
        }

        private string FormatAlamat(SuratData suratData)
        {
            var warga = suratData.Warga;
            if (warga == null)
            {
                _logger.LogWarning("Warga data null saat memformat alamat.");
                return "Alamat Tidak Diketahui";
            }

            return AlamatFormatter.Format(warga.Dusun, warga.Desa, warga.Kecamatan, warga.Kabupaten, "Alamat Tidak Diketahui");
        }

        private void AddPernyataanSection(BadanSurat badan, SuratData suratData)
        {
            if (suratData.Desa == null || string.IsNullOrWhiteSpace(suratData.Desa.NamaDesa) ||
                string.IsNullOrWhiteSpace(suratData.Desa.Kecamatan) || string.IsNullOrWhiteSpace(suratData.Desa.Kabupaten))
            {
                _logger.LogWarning("Data desa tidak lengkap untuk Pernyataan Section.");
                badan.Paragraf("Data desa tidak lengkap, pernyataan tidak dapat ditampilkan.",
                    ukuran: 10, warna: QuestPDF.Helpers.Colors.Red.Medium);
                return;
            }

            badan.Paragraf("Berdasarkan data / catatan kami selama menjabat, bahwa orang tersebut di atas adalah benar:",
                jarakAtas: 5, jarakBawah: 3);

            string[] statements =
            [
                $"Penduduk Desa {suratData.Desa.NamaDesa} Kecamatan {suratData.Desa.Kecamatan} Kabupaten {suratData.Desa.Kabupaten}",
                "Tidak dalam perkara kriminalitas",
                "Tidak dalam status tahanan yang berwajib",
                "Tidak sedang terlibat dalam penggunaan NARKOBA",
                "Berkelakuan baik dalam kehidupan bermasyarakat"
            ];

            // Indentasi 30pt: sesuai bentuk asli surat SKCK (lihat arsip surat lama).
            badan.DaftarBernomor(statements, indentKiri: 30, jarakAtas: 3, jarakBawah: 3);
        }

        private void AddKeteranganSection(BadanSurat badan, string keteranganTextBox)
        {
            string keteranganFinal = !string.IsNullOrWhiteSpace(keteranganTextBox)
                ? $"Maksud : yang bersangkutan akan membuat SKCK untuk keperluan: {keteranganTextBox}"
                : $"Maksud : yang bersangkutan akan membuat SKCK untuk keperluan: {DefaultKeperluan}";

            badan.Paragraf(keteranganFinal,
                rata: Rata.Justify,
                indentKiri: 15,
                jarakAtas: 3,
                jarakBawah: 5);
        }

        private void AddPenutupSection(BadanSurat badan)
        {
            badan.Paragraf("Demikian surat keterangan ini kami buat untuk dipergunakan seperlunya.",
                rata: Rata.Justify,
                jarakAtas: 5,
                jarakBawah: 10);
        }

        /// <summary>SKCK menambahkan blok tanda tangan Camat di bawah tanda tangan Kepala Desa.</summary>
        protected override void ComposeFooterTambahan(BadanSurat badan, SuratData suratData)
        {
            if (suratData.Desa == null)
            {
                return;
            }

            var desa = suratData.Desa;

            badan.Blok(container => container.PaddingTop(10).Column(kolom =>
            {
                kolom.Item().Element(c => SuratRenderer.Teks(c, "Mengetahui;", rata: Rata.Tengah));
                kolom.Item().Element(c => SuratRenderer.Teks(c, $"Camat {desa.Kecamatan ?? UnknownValue}", rata: Rata.Tengah));
                kolom.Item().Height(SuratRenderer.TinggiBaris * 3f);

                string namaCamatDisplay = string.IsNullOrWhiteSpace(desa.NamaCamat)
                    ? "_______________________"
                    : desa.NamaCamat.ToUpper();

                kolom.Item().Element(c => SuratRenderer.Teks(c, namaCamatDisplay, tebal: true, rata: Rata.Tengah));

                if (!string.IsNullOrWhiteSpace(desa.GolCamat))
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, desa.GolCamat, rata: Rata.Tengah));
                }

                if (!string.IsNullOrWhiteSpace(desa.NipCamat))
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"NIP: {desa.NipCamat}", rata: Rata.Tengah));
                }
            }));
        }

        private string FormatTanggalLahir(string? dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate))
                return UnknownValue;

            try
            {
                if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    return parsedDate.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                }
                if (DateTime.TryParse(dbDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    return parsedDate.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                }

                _logger.LogWarning("Failed to parse TanggalLahir '{TanggalLahir}'", dbDate);
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
