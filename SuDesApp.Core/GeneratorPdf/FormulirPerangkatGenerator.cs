using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Isi formulir pendataan perangkat desa (blanko isian tangan). Formulir ini
    /// memuat seluruh 18 kolom berkas "DATA PERANGKAT" (DESA s.d. BPJS KES) dan
    /// dipakai menarik data perangkat baru sebelum datanya diisi ke aplikasi.
    /// </summary>
    public sealed class FormulirPerangkatData
    {
        public DesaData? Desa { get; init; }

        public string Judul { get; init; } = "FORMULIR PENDATAAN DATA PERANGKAT DESA";

        public DateTime TanggalCetak { get; init; } = DateTime.Now;
    }

    /// <summary>
    /// Membuat PDF formulir pendataan perangkat desa pada halaman portrait:
    /// kop surat desa, judul, petunjuk pengisian singkat, dan 18 isian mengikuti
    /// kolom berkas "DATA PERANGKAT" (dikelompokkan agar mudah diisi), ditutup
    /// blok tanda tangan. Isian yang belum ada ditulis garis titik-titik supaya
    /// bentuk formulirnya tetap terlihat; kolom "Desa" sudah terisi nama desa.
    /// </summary>
    public class FormulirPerangkatGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<FormulirPerangkatGenerator> _logger;

        static FormulirPerangkatGenerator()
        {
        }

        public FormulirPerangkatGenerator(AppConfig config, ILogger<FormulirPerangkatGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Generator ini tidak mewarisi SuratGeneratorBase: pastikan font buku
            // (Times New Roman) sudah terdaftar juga bila ini PDF pertama sesi.
        }

        public void GeneratePdf(Stream outputStream, FormulirPerangkatData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            data ??= new FormulirPerangkatData();

            _logger.LogInformation("Membuat PDF {Judul}", data.Judul);

            // Nama wilayah dipakai di kop dan blok tanda tangan; pakai salinan
            // yang sudah bersih supaya "Desa null" tidak pernah tercetak.
            var desa = KopSurat.DesaBersih(data.Desa);
            new FormulirPerangkatDocument(data, desa, _config).GeneratePdf(outputStream);
        }
    }

    internal class FormulirPerangkatDocument : IDocument
    {
        /// <summary>Garis titik-titik isian kosong, gaya blanko surat desa.</summary>
        private const string GarisIsian = ".......................................................";

        private readonly FormulirPerangkatData _data;
        private readonly DesaData _desa;
        private readonly AppConfig _config;
        private readonly CultureInfo _culture = new("id-ID");

        public FormulirPerangkatDocument(FormulirPerangkatData data, DesaData desa, AppConfig config)
        {
            _data = data;
            _desa = desa;
            _config = config;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>Halaman portrait mengikuti Pengaturan Cetak (A4 bawaan atau F4).</summary>
        private static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi(false);
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(UkuranHalaman());
                page.MarginTop(32, Unit.Point);
                page.MarginRight(45, Unit.Point);
                page.MarginBottom(24, Unit.Point);
                page.MarginLeft(45, Unit.Point);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Element(ComposeKopSurat);
                column.Item().PaddingTop(12).AlignCenter()
                    .Text(_data.Judul).FontSize(14).SemiBold().Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text("Isian mengikuti kolom berkas DATA PERANGKAT (18 kolom)")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }

        /// <summary>
        /// Kop surat desa: logo (bila ada) + baris KopSurat, ditutup garis ganda.
        /// Susunannya milik <see cref="KopSurat.Gambar"/> supaya sama dengan dokumen lain.
        /// </summary>
        private void ComposeKopSurat(IContainer container)
            => KopSurat.Gambar(container, _desa, PengaturanCetak.JalurLogoEfektif(_config.LogoPath));

        private void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(8).Element(ComposePetunjuk);
                column.Item().PaddingTop(6).Element(ComposeIsian);
                column.Item().PaddingTop(4).ShowEntire().Element(ComposeTandaTangan);
            });
        }

        /// <summary>Petunjuk pengisian singkat — mengurangi salah isi saat pendataan.</summary>
        private void ComposePetunjuk(IContainer container)
        {
            container.Border(0.75f).BorderColor(Colors.Grey.Darken1)
                .Background(Colors.Grey.Lighten4).Padding(8)
                .Column(column =>
                {
                    column.Item().Text("Petunjuk pengisian:").SemiBold().FontSize(9.5f);
                    column.Item().PaddingTop(2).Text(
                        "1. Isi dengan huruf kapital dan tinta hitam; tanggal ditulis dd-mm-yyyy.\n" +
                        "2. Kolom yang belum ada diisi tanda \u201c-\u201d, jangan dikosongkan.\n" +
                        "3. Kolom JABATAN memakai nama jabatan resmi (mis. Sekretaris Desa, Kaur Keuangan, " +
                        "Kasi Pelayanan, Kadus).\n" +
                        "4. Lampirkan salinan KTP/KK, ijazah terakhir, serta rekomendasi/SK bila sudah ada.")
                        .FontSize(8.5f).LineHeight(1.3f);
                });
        }

        /// <summary>
        /// 18 isian dari berkas DATA PERANGKAT, dikelompokkan supaya mudah diisi:
        /// identitas, pendidikan, pengangkatan/rekomendasi, serta keuangan & kesehatan.
        /// </summary>
        private void ComposeIsian(IContainer container)
        {
            container.Table(table =>
            {
                // Label tetap, titik dua lurus, dan nilai selebar sisanya.
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(168);
                    columns.ConstantColumn(10);
                    columns.RelativeColumn();
                });

                Bagian(table, "A. IDENTITAS");
                Baris(table, "DESA", NamaDesa(), null);
                Baris(table, "NAMA PERANGKAT", null, "nama lengkap sesuai KTP");
                Baris(table, "JABATAN", null, "mis. Sekretaris Desa, Kaur Keuangan, Kasi Pelayanan, Kadus");
                Baris(table, "JENIS KELAMIN", null, "Laki-laki / Perempuan");
                Baris(table, "TEMPAT LAHIR", null, null);
                Baris(table, "TGL. LAHIR", null, "dd-mm-yyyy");
                Baris(table, "NIK", null, "16 digit sesuai KTP");
                Baris(table, "AGAMA", null, "Islam / Kristen / Katolik / Hindu / Buddha / Konghucu");
                Baris(table, "NO. TELPON", null, "nomor yang aktif (dapat dihubungi)");

                Bagian(table, "B. PENDIDIKAN");
                Baris(table, "PENDIDIKAN", null, "mis. SD, SMP/MTs, SMA/MA/SMK, D3, S1");
                Baris(table, "TAHUN LULUS", null, "mis. 2015");
                Baris(table, "NO. IJASAH", null, null);

                Bagian(table, "C. PENGANGKATAN & REKOMENDASI");
                Baris(table, "NOMOR REKOMENDASI", null, "nomor rekomendasi dari Kecamatan");
                Baris(table, "TGL. REKOMENDASI", null, "dd-mm-yyyy");
                Baris(table, "NO SK PENGANGKATAN", null, "mis. 141.3/Kep.01/Ds/2020");
                Baris(table, "TGL SK PENGANGKATAN", null, "dd-mm-yyyy");

                Bagian(table, "D. KEUANGAN & KESEHATAN");
                Baris(table, "NOREK BJB", null, "nomor rekening Bank BJB");
                Baris(table, "BPJS KES", null, "nomor kartu BPJS Kesehatan");
            });
        }

        /// <summary>Satu baris isian: label, titik dua, dan nilai (garis titik-titik bila kosong).</summary>
        private void Baris(TableDescriptor table, string label, string? nilai, string? petunjuk)
        {
            table.Cell().Element(SelLabel).Text(label).FontSize(9.5f);
            table.Cell().Element(SelLabel).AlignCenter().Text(":").FontSize(9.5f);
            table.Cell().Element(SelNilai).Column(column =>
            {
                column.Item().Text(string.IsNullOrWhiteSpace(nilai) ? GarisIsian : nilai.Trim())
                    .FontSize(9.5f);
                if (!string.IsNullOrWhiteSpace(petunjuk))
                {
                    column.Item().Text(petunjuk).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                }
            });
        }

        /// <summary>Judul kelompok isian — menutupi ketiga kolom tabel.</summary>
        private void Bagian(TableDescriptor table, string judul)
        {
            table.Cell().ColumnSpan(3).PaddingTop(4).PaddingBottom(1)
                .BorderBottom(0.75f).BorderColor(Colors.Grey.Darken2)
                .Text(judul).SemiBold().FontSize(9.5f);
        }

        // Padding baris sengaja rapat: 18 baris isian + petunjuk + tanda tangan
        // harus muat pada SATU halaman portrait, jangan sampai tanda tangan
        // terlempar ke halaman kedua sendirian.
        private static IContainer SelLabel(IContainer container) => container.PaddingVertical(1.5f);

        private static IContainer SelNilai(IContainer container) => container.PaddingVertical(1.5f);

        private void ComposeTandaTangan(IContainer container)
        {
            string namaDesa = (_desa.NamaDesa ?? string.Empty).Trim();
            string tempatTanggal = namaDesa.Length == 0
                ? TanggalIndo(_data.TanggalCetak)
                : $"{namaDesa}, {TanggalIndo(_data.TanggalCetak)}";
            string kepalaDesa = (_desa.KepalaDesa ?? string.Empty).Trim();

            container.PaddingTop(2).Row(row =>
            {
                // Kiri: diisi dan ditandatangani orang yang memberi data.
                row.RelativeItem().Column(kiri =>
                {
                    kiri.Item().Text("Yang memberi data,").FontSize(10);
                    kiri.Item().Height(40);
                    kiri.Item().AlignCenter().Text(GarisIsian).FontSize(9);
                    kiri.Item().AlignCenter().Text("Nama terang & tanda tangan")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(30);

                // Kanan: blok tanda tangan pejabat desa/kelurahan (nama sudah terisi
                // dari Pengaturan Desa). Jabatan & NIP mengikuti lembaga yang dipilih.
                row.RelativeItem().Column(kanan =>
                {
                    // Kolom pejabat berada di sisi kanan halaman; ISI bloknya
                    // rata tengah, sesuai bentuk baku tanda tangan surat dinas.
                    kanan.Item().AlignCenter().Text(tempatTanggal).FontSize(10);
                    kanan.Item().AlignCenter()
                        .Text($"{KopSurat.KataPejabat(_desa)} {namaDesa}".ToUpperInvariant().Trim())
                        .SemiBold().FontSize(10);
                    kanan.Item().Height(40);
                    kanan.Item().AlignCenter()
                        .Text(kepalaDesa.Length == 0 ? GarisIsian : kepalaDesa.ToUpperInvariant())
                        .Bold().Underline().FontSize(10);
                    // NIP hanya untuk pejabat ASN kelurahan (Lurah).
                    string nipPejabat = KopSurat.IsKelurahan(_desa)
                        ? (_desa.NipKepalaDesa ?? string.Empty).Trim()
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(nipPejabat))
                    {
                        kanan.Item().AlignCenter().Text($"NIP {nipPejabat}").FontSize(9);
                    }
                });
            });
        }

        /// <summary>Nama desa untuk baris "DESA"; kurung siku bila belum diatur.</summary>
        private string NamaDesa()
        {
            string nama = (_desa.NamaDesa ?? string.Empty).Trim();
            return nama.Length == 0 ? "[NAMA DESA]" : nama;
        }

        private string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", _culture);
    }
}
