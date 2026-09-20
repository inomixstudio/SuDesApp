// File: SuDesApp/GeneratorPdf/GarapanGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.GeneratorPdf
{
    public class GarapanGenerator : SuratGeneratorBase
    {
        public GarapanGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<GarapanGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
            _logger.LogInformation("GarapanGenerator initialized.");
        }

        protected override string JudulSurat => "SURAT KETERANGAN GARAPAN";

        // Penggarap (pemohon) tidak ikut tanda tangan di kaki surat.
        protected override bool ShowPemohonInFooter => false;

        private const float UkuranTabel = DEFAULT_FONT_SIZE - 1;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));

            var desa = suratData.Desa;
            var penggarap = suratData.Warga;
            var daftarRincianGarapan = suratData.RincianGarapans; // rincian[0] = tanah pertama, sisanya bidang tambahan

            // Fallback data lama: bila rincian kosong tetapi Garapan tunggal terisi, cetak bidang itu
            // agar surat tetap menampilkan datanya (paritas dengan mode tunggal WinForms).
            if ((daftarRincianGarapan == null || !daftarRincianGarapan.Any()) &&
                suratData.Garapan != null && suratData.Garapan.IsValid())
            {
                daftarRincianGarapan = new List<GarapanData> { suratData.Garapan };
                _logger.LogInformation("RincianGarapans kosong; memakai Garapan tunggal untuk PDF Garapan. ID_Surat: {IDSurat}", suratData.ID_Surat);
            }

            if (desa == null || penggarap == null)
            {
                _logger.LogError("Data Desa atau Penggarap null saat generate PDF Garapan. ID_Surat: {IDSurat}", suratData.ID_Surat);
                badan.Paragraf("Error: Data Desa atau Penggarap tidak lengkap.", warna: Colors.Red.Medium);
                return;
            }
            if (daftarRincianGarapan == null || !daftarRincianGarapan.Any())
            {
                _logger.LogWarning("Data Rincian Garapan kosong atau null untuk PDF Garapan. ID_Surat: {IDSurat}", suratData.ID_Surat);
                // Tabel tetap dibuat (akan tampil baris kosong), tidak menggagalkan surat.
            }

            badan.Paragraf(
                $"Yang bertanda tangan dibawah ini Kepala Desa {desa.NamaDesa ?? "[Nama Desa]"} Kecamatan {desa.Kecamatan ?? "[Kecamatan]"} Kabupaten {desa.Kabupaten ?? "[Kabupaten]"}, menerangkan dengan sesungguhnya bahwa :",
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 10,
                jarakBawah: 10);

            // Data Penggarap
            string tempatTglLahirPenggarap = $"{penggarap.TempatLahir ?? "[Tempat Lahir]"}, {ParseTanggalToUiFormat(penggarap.TanggalLahir, "dd-MM-yyyy")}";
            string alamatPenggarapLengkap = AlamatFormatter.Format(penggarap, "[Alamat Penggarap]");

            badan.TabelFormulir(new List<(string, string?)>
            {
                ("Nama", penggarap.Nama == null ? "[NAMA PENGGARAP]" : NamaFormatter.ToUpperNama(penggarap.Nama)),
                ("NIK", penggarap.NIK ?? "[NIK PENGGARAP]"),
                ("Tempat Tgl lahir", tempatTglLahirPenggarap),
                ("Jenis kelamin", penggarap.JenisKelamin ?? "[Jenis Kelamin]"),
                ("Agama", penggarap.Agama ?? "[Agama]"),
                ("Pekerjaan", penggarap.Pekerjaan ?? "[Pekerjaan]"),
                ("Alamat", alamatPenggarapLengkap),
            });

            // Pernyataan penggarap
            badan.Paragraf(
                $"Nama tersebut diatas adalah Penggarap tanah sawah di Desa {desa.NamaDesa ?? "[Nama Desa]"} Kecamatan {desa.Kecamatan ?? "[Kecamatan]"} Kabupaten {desa.Kabupaten ?? "[Kabupaten]"} dengan rinci:",
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 10,
                jarakBawah: 10);

            // Tabel rincian tanah garapan: No sempit, Letak terlebar, Luas rata kanan.
            var barisRincian = new List<(int No, string Pemilik, string Letak, string Persil, string Luas, string Keterangan)>();
            double totalLuas = 0;
            int noUrut = 1;

            if (daftarRincianGarapan != null && daftarRincianGarapan.Any())
            {
                foreach (var itemGarapan in daftarRincianGarapan)
                {
                    barisRincian.Add(RingkasRincian(noUrut++, itemGarapan, penggarap.Nama));
                    totalLuas += itemGarapan.Luas;
                }
            }
            else
            {
                barisRincian.Add(BarisKosong(noUrut++));
            }

            // Tiga baris total saat kosong — gaya template manual desa (1 baris + 2 baris kosong).
            for (int i = 0; i < 2; i++)
            {
                barisRincian.Add(BarisKosong(noUrut++));
            }

            string teksTotalLuas = $"{totalLuas:N0} M2";
            badan.Blok(container => container.PaddingTop(10).PaddingBottom(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(4);   // No
                    cols.RelativeColumn(22);  // Nama Pemilik
                    cols.RelativeColumn(28);  // Letak
                    cols.RelativeColumn(16);  // No. Persil
                    cols.RelativeColumn(12);  // Luas
                    cols.RelativeColumn(18);  // Keterangan
                });

                table.Header(header =>
                {
                    SelHeader(header, "No", Rata.Tengah);
                    SelHeader(header, "Nama Pemilik", Rata.Tengah);
                    SelHeader(header, "Letak", Rata.Tengah);
                    SelHeader(header, "No. Persil", Rata.Tengah);
                    SelHeader(header, "Luas", Rata.Tengah);
                    SelHeader(header, "Keterangan", Rata.Tengah);
                });

                foreach (var baris in barisRincian)
                {
                    SelTabel(table, baris.No.ToString(CultureInfo.InvariantCulture), Rata.Tengah);
                    SelTabel(table, baris.Pemilik, Rata.Kiri);
                    SelTabel(table, baris.Letak, Rata.Kiri);
                    SelTabel(table, baris.Persil, Rata.Kiri);
                    SelTabel(table, baris.Luas, Rata.Kanan);
                    SelTabel(table, baris.Keterangan, Rata.Kiri);
                }

                // Baris Jumlah: menyatu melintasi empat kolom pertama.
                table.Cell().ColumnSpan(4).Element(SelBerbingkai).AlignCenter()
                    .Text("Jumlah").FontSize(UkuranTabel).Bold().LineHeight(1.0f);
                table.Cell().Element(SelBerbingkai).AlignRight()
                    .Text(teksTotalLuas).FontSize(UkuranTabel).Bold().LineHeight(1.0f);
                table.Cell().Element(SelBerbingkai).Text(string.Empty).FontSize(UkuranTabel).LineHeight(1.0f);
            }));

            // Paragraf penutup/keperluan. Bila keperluan kosong dipakai kalimat penutup baku —
            // sebelumnya tercetak "... dipergunakan untuk dipergunakan sebagaimana perlunya".
            string paragrafPenutup = string.IsNullOrWhiteSpace(keteranganTextBox)
                ? "Demikian surat keterangan ini kami buat dengan sebenarnya dan dipergunakan sebagaimana perlunya."
                : $"Demikian surat keterangan ini kami buat dengan sebenarnya dan dipergunakan untuk {keteranganTextBox.Trim()}";

            badan.Paragraf(paragrafPenutup,
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 10,
                jarakBawah: 10);
        }

        /// <summary>Nilai satu bidang garapan; pemilik jatuh ke nama pemohon bila kolomnya kosong.</summary>
        private static (int No, string Pemilik, string Letak, string Persil, string Luas, string Keterangan) RingkasRincian(int noUrut, GarapanData itemGarapan, string? namaPemilikFallback)
        {
            var pemilik = !string.IsNullOrWhiteSpace(itemGarapan.PemilikTanah)
                ? itemGarapan.PemilikTanah
                : namaPemilikFallback;

            return (noUrut,
                pemilik == null ? "[NAMA PEMILIK]" : NamaFormatter.ToUpperNama(pemilik),
                itemGarapan.Lokasi ?? "[Lokasi]",
                itemGarapan.NomorPersil ?? "-",
                $"{itemGarapan.Luas:N0} M2",
                itemGarapan.KeteranganGarapan ?? "[Keterangan]");
        }

        private static (int No, string Pemilik, string Letak, string Persil, string Luas, string Keterangan) BarisKosong(int noUrut)
            => (noUrut, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        /// <summary>Sel tabel rincian: border tipis 0,75pt dengan padding 3pt seperti sebelumnya.</summary>
        private static IContainer SelBerbingkai(IContainer container) => container
            .Border(0.75f)
            .BorderColor(Colors.Black)
            .Padding(3);

        private static void SelTabel(TableDescriptor table, string teks, Rata rata)
            => IsiSel(table.Cell().Element(SelBerbingkai), teks, rata, tebal: false);

        private static void SelHeader(TableCellDescriptor header, string teks, Rata rata)
            => IsiSel(header.Cell().Element(SelBerbingkai), teks, rata, tebal: true);

        private static void IsiSel(IContainer cell, string teks, Rata rata, bool tebal)
        {
            var teksSel = rata switch
            {
                Rata.Tengah => cell.AlignCenter().Text(teks),
                Rata.Kanan => cell.AlignRight().Text(teks),
                _ => cell.AlignLeft().Text(teks)
            };

            teksSel.FontSize(UkuranTabel).LineHeight(1.0f);

            if (tebal)
            {
                teksSel.Bold();
            }
        }

        /// <summary>Format tanggal dari DB (yyyy-MM-dd) ke tampilan surat.</summary>
        private string ParseTanggalToUiFormat(string dbDate, string format = "dd-MM-yyyy")
        {
            if (string.IsNullOrWhiteSpace(dbDate)) return string.Empty;
            if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.ToString(format, new CultureInfo("id-ID"));
            }
            _logger.LogWarning("ParseTanggalToUiFormat (GarapanGenerator): Gagal memformat tanggal dari DB: {DbDate}", dbDate);
            return dbDate;
        }
    }
}
