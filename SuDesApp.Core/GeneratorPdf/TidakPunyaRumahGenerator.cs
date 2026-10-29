// TidakPunyaRumahGenerator.cs
// Surat Keterangan Tidak Punya Rumah: warga yang tidak memiliki rumah/tempat
// tinggal tetap di wilayah desa. Badan surat memakai BadanSurat (mesin SudesPdf)
// seperti SKD/SKTM.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class TidakPunyaRumahGenerator : SuratGeneratorBase
    {
        private const string DateFormatDb = "yyyy-MM-dd";

        public TidakPunyaRumahGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<TidakPunyaRumahGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN TIDAK PUNYA RUMAH";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk membuat PDF SK Tidak Punya Rumah.");
                var desa = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk membuat PDF SK Tidak Punya Rumah.");

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                // Pejabat mengikuti lembaga: “Kepala Desa …” atau “Lurah …”;
                // nama pejabat di atas memakai kolom Pengaturan Surat.
                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? desa.KepalaDesa ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{KopSurat.KataPejabat(desa, suratData.PejabatPenandatangan)} {desa.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", warga.Nama),
                    ("NIK", warga.NIK),
                    ("Tempat Tanggal Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {FormatTanggalLahir(warga.TanggalLahir)}"),
                    ("Jenis Kelamin", warga.JenisKelamin),
                    ("Agama", warga.Agama),
                    ("Status Perkawinan", warga.StatusPerkawinan),
                    ("Pekerjaan", warga.Pekerjaan),
                    ("Alamat", AlamatFormatter.Format(warga)),
                ]);

                string? keteranganFinal = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan;

                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    string namaKabupaten = KopSurat.NamaKabupaten(desa);
                    if (namaKabupaten.Length == 0) namaKabupaten = "[Kabupaten]";

                    keteranganFinal = string.Format(
                        "Adalah benar nama tersebut diatas adalah warga Desa {0} Kecamatan {1} {2} {3}, dan yang bersangkutan saat ini tidak memiliki rumah atau tempat tinggal tetap di wilayah desa kami.",
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

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana keperluannya.",
                    rata: Rata.Kiri,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk SK Tidak Punya Rumah.");
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
