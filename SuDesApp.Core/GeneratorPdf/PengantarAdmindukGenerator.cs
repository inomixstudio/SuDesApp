// PengantarAdmindukGenerator.cs
// Surat pengantar urusan administrasi kependudukan (KTP-e, KK, akta, dsb.)
// ke Disdukcapil. Badan surat memakai BadanSurat (mesin SudesPdf) seperti SKD.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class PengantarAdmindukGenerator : SuratGeneratorBase
    {
        private const string DateFormatDb = "yyyy-MM-dd";

        public PengantarAdmindukGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<PengantarAdmindukGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT PENGANTAR ADMINISTRASI KEPENDUDUKAN";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                var warga = suratData.Warga ?? throw new InvalidOperationException("Data warga tidak lengkap untuk membuat PDF Pengantar Adminduk.");
                var desa = suratData.Desa ?? throw new InvalidOperationException("Data desa tidak lengkap untuk membuat PDF Pengantar Adminduk.");

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {desa.NamaDesa ?? "[Nama Desa]"}"),
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
                        "Adalah benar nama tersebut diatas adalah warga Desa {0} Kecamatan {1} {2} {3}, dan yang bersangkutan akan mengurus dokumen administrasi kependudukan (KTP-elektronik, Kartu Keluarga, Akta Kelahiran, dan dokumen adminduk lainnya) di Dinas Kependudukan dan Pencatatan Sipil.",
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

                badan.Paragraf("Demikian surat pengantar ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana keperluannya.",
                    rata: Rata.Kiri,
                    jarakBawah: 12);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Pengantar Adminduk.");
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
