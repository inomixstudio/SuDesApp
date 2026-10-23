// KeramaianGenerator.cs
// Surat Izin Keramaian: izin penyelenggaraan kegiatan (hajatan, pengajian,
// dan sejenisnya) di wilayah desa. Data kegiatan dibaca dari tabel Keramaian
// (via KeramaianDataHandler); penanggung jawabnya adalah pemohon.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class KeramaianGenerator : SuratGeneratorBase
    {
        private const string DateFormatDb = "yyyy-MM-dd";

        public KeramaianGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<KeramaianGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT IZIN KERAMAIAN";
        protected override bool ShowPemohonInFooter => true;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk membuat PDF Izin Keramaian.");
                var desa = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk membuat PDF Izin Keramaian.");

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {desa.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini memberikan izin kepada :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", warga.Nama),
                    ("NIK", warga.NIK),
                    ("Tempat Tanggal Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {FormatTanggalLahir(warga.TanggalLahir)}"),
                    ("Jenis Kelamin", warga.JenisKelamin),
                    ("Pekerjaan", warga.Pekerjaan),
                    ("Alamat", AlamatFormatter.Format(warga)),
                ]);

                badan.Paragraf("untuk menyelenggarakan kegiatan :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Jenis Kegiatan", suratData.JenisKegiatan ?? "[Jenis Kegiatan]"),
                    ("Tanggal", suratData.TanggalKegiatan ?? "[Tanggal]"),
                    ("Tempat", suratData.LokasiKegiatan ?? "[Tempat]"),
                ],
                labelTebal: ["Jenis Kegiatan"]);

                string? keteranganFinal = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan;

                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    string namaKabupaten = KopSurat.NamaKabupaten(desa);
                    if (namaKabupaten.Length == 0) namaKabupaten = "[Kabupaten]";

                    keteranganFinal = string.Format(
                        "Benar nama tersebut diatas adalah warga Desa {0} Kecamatan {1} {2} {3}, dan kegiatan tersebut di atas adalah kegiatan warga desa kami yang tidak bertentangan dengan peraturan perundang-undangan yang berlaku.",
                        desa.NamaDesa ?? "[Nama Desa]",
                        desa.Kecamatan ?? "[Kecamatan]",
                        KopSurat.KataJenisKabupaten(desa),
                        namaKabupaten);
                }

                // Alinea pembuka: baris pertama indentasi 50pt, baris lanjutan rata kiri.
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentBarisPertama: 50,
                    jarakAtas: 10,
                    jarakBawah: 12);

                badan.Paragraf("Demikian izin ini diberikan untuk dipergunakan sebagaimana mestinya.",
                    rata: Rata.Kiri,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Izin Keramaian, NamaWarga={NamaWarga}", suratData.Warga?.Nama);
                throw;
            }
        }

        private string FormatTanggalLahir(string? dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate))
                return "[Tanggal Lahir]";

            if (DateTime.TryParseExact(dbDate, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return parsed.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));

            return dbDate;
        }
    }
}
