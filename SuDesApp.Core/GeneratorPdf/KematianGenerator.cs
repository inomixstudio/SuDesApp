// File: SuDesApp/GeneratorPdf/KematianGenerator.cs
// Surat kematian adalah SALINAN FORMULIR resmi (kode F-2.17), jadi kepalanya bukan
// kop surat: identitas desa berlabel “Pemerintah Desa/Kecamatan/Kabupaten”.
// Seluruh tata letak disusun langsung dengan QuestPDF (tanpa lapisan kompat).
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
    public class KematianGenerator : SuratGeneratorBase
    {
        private const string KODE_FORMULIR_KEMATIAN = "Kode : F-2.17";
        protected override string JudulSurat => "SURAT KETERANGAN KEMATIAN";
        private const string DateFormatDb = "yyyy-MM-dd";
        private const string DateFormatUi = "dd-MM-yyyy";
        private const float LeadingRapat = 12f;
        private const float LeadingData = 14f;
        private const float JarakLabelTitikDua = 110f;

        public KematianGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<KematianGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override void ComposeHalaman(BadanSurat halaman, SuratData suratData, string? keteranganTextBox = null)
        {
            var desa = suratData.Desa ?? new DesaData { NamaDesa = "[Desa]", Kecamatan = "[Kecamatan]", Kabupaten = "[Kabupaten]", KepalaDesa = "[Kepala Desa]" };
            var wargaAlmarhum = suratData.Warga ?? new WargaData();
            var kematianDetail = suratData.Kematian ?? new KematianData();

            // --- Kotak kode formulir di kanan atas ---
            halaman.Blok(container => container.PaddingBottom(10f).AlignRight().Width(80f)
                .Border(0.75f).BorderColor(Colors.Black).Padding(3f)
                .AlignCenter().AlignMiddle()
                .Text(KODE_FORMULIR_KEMATIAN).FontSize(9f));

            // --- Identitas desa berlabel ---
            halaman.Blok(container => container.Column(baris =>
            {
                BarisLabelNilai(baris, "Pemerintah Desa", desa.NamaDesa?.ToUpper() ?? "[NAMA DESA]");
                BarisLabelNilai(baris, "Kecamatan", desa.Kecamatan?.ToUpper() ?? "[KECAMATAN]");
                BarisLabelNilai(baris, "Kabupaten/Kota", desa.Kabupaten?.ToUpper() ?? "[KABUPATEN]", jarakBawahTerakhir: 15f);
            }));

            // --- Judul + Nomor ---
            string nomorTampil = (string.IsNullOrWhiteSpace(suratData.NomorSurat) || suratData.NomorSurat.Contains("..."))
                ? $"Nomor : 570 / {"".PadRight(17)} / Ds. {suratData.TanggalSurat:yyyy}"
                : $"Nomor : {suratData.NomorSurat}";

            halaman.Blok(container => container.Column(judul =>
            {
                judul.Item().AlignCenter()
                    .Text(JudulSurat)
                    .FontSize(TITLE_FONT_SIZE + 1)
                    .Bold()
                    .Underline();
                judul.Item().PaddingTop(1f).PaddingBottom(15f).AlignCenter()
                    .Text(nomorTampil)
                    .FontSize(DEFAULT_FONT_SIZE)
                    .LineHeight(LeadingRapat / DEFAULT_FONT_SIZE);
            }));

            // --- Isi surat ---
            halaman.Paragraf("Yang bertanda tangan dibawah ini, menerangkan bahwa :", jarakAtas: 10, jarakBawah: 8);

            string umurAlmarhumDisplay = HitungUmurPadaTanggal(wargaAlmarhum.TanggalLahir!, kematianDetail.TanggalKematian!, "Tanggal Lahir Almarhum/ah", "Tanggal Kematian");

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("NIK", wargaAlmarhum.NIK),
                ("Nama Lengkap Jenazah", wargaAlmarhum.Nama == null ? null : NamaFormatter.ToUpperNama(wargaAlmarhum.Nama)),
                ("Jenis Kelamin", wargaAlmarhum.JenisKelamin),
                ("Agama", wargaAlmarhum.Agama),
                ("Umur pada saat Meninggal", umurAlmarhumDisplay),
                ("Pekerjaan", wargaAlmarhum.Pekerjaan),
                ("Alamat", AlamatFormatter.Format(wargaAlmarhum))
            }));

            halaman.Paragraf("Telah meninggal dunia pada :", jarakAtas: 8, jarakBawah: 3);

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("Hari", kematianDetail.HariKematian),
                ("Tanggal", FormatTanggalUntukTampilan(kematianDetail.TanggalKematian)),
                ("Pukul", string.IsNullOrWhiteSpace(kematianDetail.PukulKematian) ? null : $"{kematianDetail.PukulKematian} WIB"),
                ("Penyebab Kematian", kematianDetail.PenyebabKematian)
            }));

            halaman.Paragraf("Surat keterangan ini dibuat berdasarkan Keterangan Pelapor :", jarakAtas: 12, jarakBawah: 3);

            halaman.Paragraf("PELAPOR", tebal: true, indentKiri: 20, jarakBawah: 2);

            string umurPelaporDisplay = HitungUmurPadaTanggal(kematianDetail.UmurPelapor!, suratData.TanggalSurat.ToString(DateFormatDb), "Tanggal Lahir Pelapor", "Tanggal Surat");

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("NIK", kematianDetail.NIKPelapor),
                ("Nama lengkap", kematianDetail.NamaPelapor == null ? null : NamaFormatter.ToUpperNama(kematianDetail.NamaPelapor)),
                ("Agama", kematianDetail.AgamaPelapor),
                ("Umur", umurPelaporDisplay),
                ("Pekerjaan", kematianDetail.PekerjaanPelapor),
                ("Alamat", string.IsNullOrWhiteSpace(kematianDetail.AlamatPelapor) ? "[Alamat Pelapor]" : kematianDetail.AlamatPelapor)
            }));

            // Hubungan pelapor dicetak sebagai baris tersendiri di bawah blok data pelapor.
            if (!string.IsNullOrWhiteSpace(kematianDetail.HubunganPelapor))
            {
                string hubungan = kematianDetail.HubunganPelapor;
                halaman.Blok(container => container.PaddingTop(15f).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(55);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(43);
                    });

                    SelHubungan(table.Cell(), "Hubungan Pelapor dengan yang Meninggal Dunia");
                    SelHubungan(table.Cell(), ":");
                    SelHubungan(table.Cell(), hubungan);
                }));
            }

            // --- Kaki surat gaya formulir: hanya blok Kepala Desa ---
            string tanggalTerformat = FormatTanggalUntukTampilan(suratData.TanggalSurat.ToString(DateFormatDb));
            string namaDesa = desa.NamaDesa ?? "[NAMA DESA]";
            string namaKades = desa.KepalaDesa?.ToUpper() ?? "...............................";

            halaman.Blok(container => container.PaddingTop(30f).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(60);
                    cols.RelativeColumn(40);
                });

                table.Cell().Text(string.Empty);

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"{namaDesa}, {tanggalTerformat}", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"Kepala Desa {namaDesa}", rata: Rata.Tengah));
                    kolom.Item().Height(60);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaKades, tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        /// <summary>Baris “Pemerintah Desa : …” dengan titik dua yang lurus.</summary>
        private static void BarisLabelNilai(ColumnDescriptor baris, string label, string nilai, float jarakBawahTerakhir = 0f)
        {
            baris.Item().Row(row =>
            {
                row.ConstantItem(JarakLabelTitikDua).Text(label).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingRapat / DEFAULT_FONT_SIZE_KEMATIAN);
                row.RelativeItem().Text($": {nilai}").FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingRapat / DEFAULT_FONT_SIZE_KEMATIAN);
            });

            if (jarakBawahTerakhir > 0f)
            {
                baris.Item().Height(jarakBawahTerakhir);
            }
        }

        private const float DEFAULT_FONT_SIZE_KEMATIAN = 12f;

        /// <summary>Blok data berlabel: label 35%, titik dua 3%, nilai 62%, menjorok 20pt.</summary>
        private static void TabelData(IContainer container, List<(string Label, string? Value)> items)
        {
            if (!items.Any()) return;

            container.PaddingLeft(20f).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(35);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(62);
                });

                foreach (var item in items)
                {
                    SelData(table.Cell(), item.Label ?? string.Empty);
                    SelData(table.Cell(), ":");
                    SelData(table.Cell(), string.IsNullOrWhiteSpace(item.Value) ? "..............................." : item.Value);
                }
            });
        }

        private static void SelData(IContainer cell, string teks)
            => cell.Text(teks ?? string.Empty).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingData / DEFAULT_FONT_SIZE_KEMATIAN);

        private static void SelHubungan(IContainer cell, string teks)
            => cell.Text(teks ?? string.Empty).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingData / DEFAULT_FONT_SIZE_KEMATIAN);

        private string FormatTanggalUntukTampilan(string? tanggalInput)
        {
            if (string.IsNullOrWhiteSpace(tanggalInput)) return "...............................";
            if (DateTime.TryParseExact(tanggalInput, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDb))
                return parsedDb.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            if (DateTime.TryParseExact(tanggalInput, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedUi))
                return parsedUi.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            _logger.LogWarning("FormatTanggalUntukTampilan: Gagal mem-parse tanggal '{TanggalInput}'", tanggalInput);
            return tanggalInput;
        }

        private string HitungUmurPadaTanggal(string tanggalLahirStr, string tanggalReferensiStr, string fieldNameTglLahir, string fieldNameTglReferensi)
        {
            if (string.IsNullOrWhiteSpace(tanggalLahirStr) || string.IsNullOrWhiteSpace(tanggalReferensiStr)) return "...............................";

            DateTime tglLahir;
            DateTime tglReferensi;

            bool tglLahirValid = DateTime.TryParseExact(tanggalLahirStr, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglLahir) ||
                                 DateTime.TryParseExact(tanggalLahirStr, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglLahir);

            bool tglReferensiValid = DateTime.TryParseExact(tanggalReferensiStr, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglReferensi) ||
                                     DateTime.TryParseExact(tanggalReferensiStr, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglReferensi);

            if (tglLahirValid && tglReferensiValid)
            {
                if (tglLahir.Date > tglReferensi.Date)
                {
                    _logger.LogWarning("Tanggal lahir ({TglLahir}) lebih besar dari tanggal referensi ({TglReferensi}) untuk perhitungan umur.", tglLahir.ToShortDateString(), tglReferensi.ToShortDateString());
                    return "(Data Tanggal Tidak Valid)";
                }

                int years = tglReferensi.Year - tglLahir.Year;
                if (tglLahir.Date > tglReferensi.Date.AddYears(-years)) years--;
                return $"{years} Tahun";
            }
            _logger.LogWarning("Gagal menghitung umur. {FieldNameTglLahir}: {TglLahirStr}, {FieldNameTglReferensi}: {TglReferensiStr}", fieldNameTglLahir, tanggalLahirStr, fieldNameTglReferensi, tanggalReferensiStr);
            return "...............................";
        }
    }
}
