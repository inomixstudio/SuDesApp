using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Satu baris pada cetakan daftar perangkat desa — kolomnya mengikuti berkas
    /// "DATA PERANGKAT" contoh pemakai (18 kolom, urut sama, kosong dicetak "-").
    /// </summary>
    public sealed class StrukturPerangkatBaris
    {
        public string Nama { get; init; } = "-";

        /// <summary>Jabatan bernomor per jabatan berbeda, mis. "8. KADUS".</summary>
        public string Jabatan { get; init; } = "-";

        public string JenisKelamin { get; init; } = "-";
        public string TempatLahir { get; init; } = "-";
        public string TanggalLahir { get; init; } = "-";
        public string Nik { get; init; } = "-";
        public string Agama { get; init; } = "-";
        public string NoTelp { get; init; } = "-";
        public string NomorRekomendasi { get; init; } = "-";
        public string TanggalRekomendasi { get; init; } = "-";

        /// <summary>Nomor SK pengangkatan; "-" bila belum ada.</summary>
        public string NomorSk { get; init; } = "-";

        /// <summary>Tanggal SK pengangkatan, format dd-MM-yyyy.</summary>
        public string TanggalSk { get; init; } = "-";

        public string Pendidikan { get; init; } = "-";
        public string TahunLulus { get; init; } = "-";
        public string NoIjasah { get; init; } = "-";
        public string NoRekBjb { get; init; } = "-";
        public string BpjsKes { get; init; } = "-";
    }

    /// <summary>Isi cetakan daftar perangkat desa (tombol Cetak PDF halaman Data Perangkat Desa).</summary>
    public sealed class StrukturPerangkatData
    {
        public DesaData? Desa { get; init; }

        public string Judul { get; init; } = "DAFTAR PERANGKAT DESA";

        public DateTime TanggalCetak { get; init; } = DateTime.Now;

        /// <summary>Baris tabel mengikuti berkas DATA PERANGKAT contoh (hanya orang yang tercatat).</summary>
        public IReadOnlyList<StrukturPerangkatBaris> Baris { get; init; } = Array.Empty<StrukturPerangkatBaris>();

        /// <summary>Jumlah orang yang benar-benar tercatat (baris tanpa jabatan kosong).</summary>
        public int JumlahOrang { get; init; }

        /// <summary>Jumlah jabatan resmi yang belum ada orangnya.</summary>
        public int JumlahJabatanKosong { get; init; }
    }

    /// <summary>
    /// Membuat PDF Daftar Perangkat Desa: kop surat desa, judul, tabel 18 kolom
    /// mengikuti berkas "DATA PERANGKAT" contoh (DESA s.d. BPJS KES) pada halaman
    /// landscape, serta blok tanda tangan Kepala Desa. Hanya orang yang tercatat
    /// yang dicetak; jumlah jabatan kosong tampil pada ringkasan tanggal cetak.
    /// </summary>
    public class StrukturPerangkatGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<StrukturPerangkatGenerator> _logger;

        static StrukturPerangkatGenerator()
        {
        }

        public StrukturPerangkatGenerator(AppConfig config, ILogger<StrukturPerangkatGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Generator ini tidak mewarisi SuratGeneratorBase: pastikan font buku
            // (Times New Roman) sudah terdaftar juga bila ini PDF pertama sesi.
        }

        public void GeneratePdf(Stream outputStream, StrukturPerangkatData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            data ??= new StrukturPerangkatData();

            _logger.LogInformation(
                "Membuat PDF Daftar Perangkat Desa judul={Judul} baris={Jumlah}", data.Judul, data.Baris.Count);

            // Nama wilayah dipakai di kop dan blok tanda tangan; pakai salinan
            // yang sudah bersih supaya "Desa null" tidak pernah tercetak.
            var desa = KopSurat.DesaBersih(data.Desa);
            new StrukturPerangkatDocument(data, desa, _config).GeneratePdf(outputStream);
        }
    }

    internal class StrukturPerangkatDocument : IDocument
    {
        /// <summary>Skala tiga baris judul kop: ruang vertikal landscape lebih pendek.</summary>
        private const float SkalaJudulKopLandscape = 0.82f;

        private readonly StrukturPerangkatData _data;
        private readonly DesaData _desa;
        private readonly AppConfig _config;
        private readonly CultureInfo _culture = new("id-ID");

        public StrukturPerangkatDocument(StrukturPerangkatData data, DesaData desa, AppConfig config)
        {
            _data = data;
            _desa = desa;
            _config = config;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>Halaman landscape mengikuti Pengaturan Cetak (A4 bawaan atau F4),
        /// sebab 18 kolom tabel tidak muat pada portrait.</summary>
        private static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi(true);
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(UkuranHalaman());
                page.MarginTop(32, Unit.Point);
                page.MarginRight(45, Unit.Point);
                page.MarginBottom(18, Unit.Point);
                page.MarginLeft(45, Unit.Point);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

                // Kop + judul di header supaya tetap tampil di tiap halaman.
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Element(ComposeKopSurat);
                column.Item().PaddingTop(12).Element(ComposeJudul);
                column.Item().PaddingTop(4).Element(ComposeInfoCetak);
            });
        }

        /// <summary>
        /// Kop surat desa: logo (bila ada) + baris KopSurat, ditutup garis ganda.
        /// Susunannya milik <see cref="KopSurat.Gambar"/> supaya sama dengan dokumen lain.
        /// </summary>
        private void ComposeKopSurat(IContainer container)
        {
            float skalaJudul = SkalaJudulKopLandscape;
            KopSurat.Gambar(container, _desa, PengaturanCetak.JalurLogoEfektif(_config.LogoPath), skalaJudul);
        }

        private void ComposeJudul(IContainer container)
        {
            container.AlignCenter().Text(_data.Judul).FontSize(15).SemiBold().Underline();
        }

        /// <summary>Tanggal cetak dan ringkasan isi — pembaca perlu tahu kapan daftar ini diambil.</summary>
        private void ComposeInfoCetak(IContainer container)
        {
            string tanggal = _data.TanggalCetak.ToString("dddd, d MMMM yyyy", _culture);
            string ringkas = $"{_data.JumlahOrang} orang tercatat";
            if (_data.JumlahJabatanKosong > 0)
            {
                ringkas += $", {_data.JumlahJabatanKosong} jabatan belum terisi";
            }

            container.AlignCenter()
                .Text($"Tanggal cetak: {tanggal} — {ringkas}")
                .FontSize(9.5f)
                .FontColor(Colors.Grey.Darken1);
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10);

                if (_data.Baris.Count == 0)
                {
                    // Daftar benar-benar kosong: beri keterangan, jangan mencetak
                    // tabel tanpa satu pun baris.
                    column.Item().AlignCenter()
                        .Text("Belum ada data perangkat desa yang dapat dicetak.")
                        .SemiBold();
                    return;
                }

                column.Item().Table(ComposeTable);

                // ShowEntire: blok tanda tangan tidak boleh terbelah antar halaman.
                column.Item().PaddingTop(14).ShowEntire().Element(ComposeTandaTangan);
            });
        }

        /// <summary>Ukuran teks tabel 18 kolom; lebih besar membuat berdesakan.</summary>
        private const float UkuranTabel = 7f;

        private void ComposeTable(TableDescriptor table)
        {
            // Lebar kolom disesuaikan agar 18 kolom (sama seperti berkas DATA
            // PERANGKAT contoh) muat pada halaman landscape: kolom tanggal dan
            // angka berukuran tetap, sisanya relatif.
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(40);   // DESA
                columns.RelativeColumn(2.1f); // NAMA PERANGKAT
                columns.RelativeColumn(1.9f); // JABATAN
                columns.ConstantColumn(36);   // JENIS KELAMIN
                columns.RelativeColumn(1.4f); // TEMPAT LAHIR
                columns.ConstantColumn(38);   // TGL. LAHIR
                columns.RelativeColumn(2.4f); // NIK (16 digit, jangan terbelah)
                columns.ConstantColumn(24);   // AGAMA
                columns.ConstantColumn(44);   // NO. TELPON
                columns.RelativeColumn(1.6f); // NOMOR REKOMENDASI
                columns.ConstantColumn(38);   // TGL. REKOMENDASI
                columns.RelativeColumn(1.8f); // NO SK PENGANGKATAN
                columns.ConstantColumn(38);   // TGL SK
                columns.RelativeColumn(1.5f); // PENDIDIKAN
                columns.ConstantColumn(24);   // TAHUN LULUS
                columns.RelativeColumn(1.6f); // NO. IJASAH
                columns.ConstantColumn(46);   // NOREK BJB
                columns.ConstantColumn(46);   // BPJS KES
            });

            string namaDesa = (_desa.NamaDesa ?? string.Empty).Trim().ToUpperInvariant();
            if (namaDesa.Length == 0) namaDesa = "-";

            table.Header(header =>
            {
                string[] judul =
                {
                    "DESA", "NAMA PERANGKAT", "JABATAN", "JENIS KELAMIN", "TEMPAT LAHIR",
                    "TGL. LAHIR", "NIK", "AGAMA", "NO. TELPON", "NOMOR REKOMENDASI",
                    "TGL. REKOMENDASI", "NO SK PENGANGKATAN", "TGL SK PENGANGKATAN",
                    "PENDIDIKAN", "TAHUN LULUS", "NO. IJASAH", "NOREK BJB", "BPJS KES"
                };
                foreach (var teks in judul)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text(teks)
                        .SemiBold().FontSize(UkuranTabel);
                }
            });

            foreach (var baris in _data.Baris)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(namaDesa).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Nama).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Jabatan).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.JenisKelamin).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.TempatLahir).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.TanggalLahir).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Nik).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Agama).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NoTelp).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NomorRekomendasi).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.TanggalRekomendasi).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NomorSk).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.TanggalSk).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Pendidikan).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.TahunLulus).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NoIjasah).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NoRekBjb).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.BpjsKes).FontSize(UkuranTabel);
            }
        }

        private static IContainer HeaderCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .Background(Colors.Grey.Lighten4)
            .PaddingVertical(3)
            .PaddingHorizontal(2);

        private static IContainer BodyCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(2)
            .PaddingHorizontal(2)
            .MinHeight(18);

        /// <summary>Blok tanda tangan Kepala Desa di bagian bawah dokumen.</summary>
        private void ComposeTandaTangan(IContainer container)
        {
            string namaDesa = (_desa.NamaDesa ?? string.Empty).Trim();
            string tempatTanggal = namaDesa.Length == 0
                ? TanggalIndo(_data.TanggalCetak)
                : $"{namaDesa}, {TanggalIndo(_data.TanggalCetak)}";

            string kepalaDesa = (_desa.KepalaDesa ?? string.Empty).Trim();
            if (kepalaDesa.Length == 0) kepalaDesa = "...........................";

            // Kolom penanda tangan menempel di sisi kanan halaman; isi bloknya
            // rata tengah, sesuai bentuk baku tanda tangan surat dinas.
            container.AlignRight().Width(260).Column(column =>
            {
                column.Item().AlignCenter().Text(tempatTanggal);
                // Jabatan & NIP mengikuti lembaga: “LURAH …” + NIP untuk kelurahan.
                column.Item().AlignCenter()
                    .Text($"{KopSurat.KataPejabat(_desa)} {namaDesa}".ToUpperInvariant().Trim())
                    .SemiBold();
                column.Item().Height(40);
                column.Item().AlignCenter().Text(kepalaDesa.ToUpperInvariant()).Bold().Underline();
                string nipPejabat = KopSurat.IsKelurahan(_desa)
                    ? (_desa.NipKepalaDesa ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(nipPejabat))
                {
                    column.Item().AlignCenter().Text($"NIP {nipPejabat}");
                }
            });
        }

        private string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", _culture);
    }
}
