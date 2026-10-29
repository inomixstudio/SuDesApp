// BedaNamaGenerator.cs
// Badan surat dibuat langsung dengan mesin SudesPdf (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SuDesApp.ControlSurat;

namespace SuDesApp.GeneratorPdf
{
    public class BedaNamaGenerator : SuratGeneratorBase
    {
        private const string DateFormatDb = "yyyy-MM-dd";
        private const string DateFormatUi = "dd-MM-yyyy";

        public BedaNamaGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<BedaNamaGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN BEDA DATA";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                if (suratData.Warga == null)
                {
                    _logger.LogError("Data Warga null saat membuat konten Beda Nama untuk Surat ID: {SuratId}", suratData.ID_Surat);
                    throw new InvalidOperationException("Data Warga tidak boleh null untuk membuat Surat Beda Nama.");
                }

                // Bagian pengantar
                badan.Paragraf("Yang bertanda tangan di bawah ini:", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa terdapat perbedaan data sebagai berikut:", jarakBawah: 10);

                // Tabel Data Pertama
                badan.Paragraf($"Data di: {suratData.DataSource1 ?? "[Sumber Data 1]"}", jarakBawah: 5);

                string tanggalLahirData1 = "[Tanggal Lahir]";
                if (!string.IsNullOrWhiteSpace(suratData.Warga?.TanggalLahir))
                {
                    if (DateTime.TryParseExact(suratData.Warga.TanggalLahir, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tglLahir))
                    {
                        tanggalLahirData1 = tglLahir.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                    }
                    else
                    {
                        tanggalLahirData1 = suratData.Warga.TanggalLahir;
                        _logger.LogWarning("Tanggal lahir Data 1 tidak bisa diformat.");
                    }
                }

                badan.TabelFormulir(
                [
                    ("Nama", suratData.Warga?.Nama ?? "[Nama Warga]"),
                    ("NIK", suratData.Warga?.NIK ?? "[NIK]"),
                    ("Tempat Tanggal Lahir", $"{(suratData.Warga?.TempatLahir ?? "[Tempat Lahir]")}, {tanggalLahirData1}"),
                    ("Jenis Kelamin", suratData.Warga?.JenisKelamin ?? "[Jenis Kelamin]"),
                    ("Alamat", AlamatFormatter.Format(suratData.Warga)),
                ]);

                // Tabel Data Kedua
                badan.Paragraf($"Data di: {suratData.DataSource2 ?? "[Sumber Data 2]"}", jarakAtas: 10, jarakBawah: 5);

                string tanggalLahirData2 = "[Tanggal Lahir]";
                if (!string.IsNullOrWhiteSpace(suratData.WargaKK?.TanggalLahir))
                {
                    if (DateTime.TryParseExact(suratData.WargaKK.TanggalLahir, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tglLahirData2))
                    {
                        tanggalLahirData2 = tglLahirData2.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                    }
                    else
                    {
                        tanggalLahirData2 = suratData.WargaKK.TanggalLahir;
                        _logger.LogWarning("Tanggal lahir Data 2 tidak bisa diformat.");
                    }
                }

                badan.TabelFormulir(
                [
                    ("Nama", string.IsNullOrWhiteSpace(suratData.WargaKK?.Nama)
                        ? "[Nama Data 2]"
                        : NamaFormatter.ToUpperNama(suratData.WargaKK!.Nama)),
                    ("NIK", suratData.WargaKK?.NIK ?? "[NIK Data 2]"),
                    ("Tempat Tanggal Lahir", $"{(suratData.WargaKK?.TempatLahir ?? "[Tempat Lahir Data 2]")}, {tanggalLahirData2}"),
                    ("Jenis Kelamin", suratData.WargaKK?.JenisKelamin ?? "[Jenis Kelamin Data 2]"),
                    ("Alamat", AlamatFormatter.Format(suratData.WargaKK)),
                ]);

                // Keterangan
                string? keteranganFinal = suratData.Keterangan;
                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger.LogWarning("Keterangan is null or empty. Generating default keterangan.");
                    DesaData desa = suratData.Desa ?? new DesaData();

                    // Kata jenis wilayah mengikuti data desa: “KABUPATEN KARAWANG” atau
                    // “KOTA BANDUNG”. Nama wilayahnya dibersihkan lebih dulu supaya
                    // “Kota Bandung” yang terlanjur diketik tidak menjadi “KOTA KOTA BANDUNG”.
                    string namaKabupaten = KopSurat.NamaKabupaten(desa);
                    keteranganFinal = string.Format(
                        "Adalah benar warga desa {0} Kecamatan {1} {2} {3}, dan menurut sepengetahuan kami orang tersebut di atas adalah benar orang yang sama.",
                        desa.NamaDesa ?? "Sumberjaya",
                        desa.Kecamatan ?? "Tempuran",
                        KopSurat.KataJenisKabupaten(desa),
                        namaKabupaten.Length > 0 ? namaKabupaten : "Karawang"
                    );
                }

                // Alinea pembuka: baris pertama indentasi 50pt, baris lanjutan
                // rata kiri kembali ke margin — bentuk alinea surat resmi.
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
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Beda Nama.");
                throw;
            }
        }
    }
}
