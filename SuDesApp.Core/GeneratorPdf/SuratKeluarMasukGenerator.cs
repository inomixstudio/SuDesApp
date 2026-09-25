using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class SuratKeluarMasukGenerator
    {
        private readonly DesaData _desaInfo;

        public SuratKeluarMasukGenerator(DesaData desaInfo)
        {
            // Generator ini tidak mewarisi SuratGeneratorBase: pastikan font buku
            // (Times New Roman) sudah terdaftar juga bila ini PDF pertama sesi.
            SuratGeneratorBase.DaftarkanFont();

            _desaInfo = desaInfo ?? new DesaData();
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public void GenerateAllSuratPdf(Stream outputStream, List<SuratKeluarMasukData> data, string jenisSuratFilter, int? yearFilter)
        {
            var document = new SuratKeluarMasukDocument(data, _desaInfo, jenisSuratFilter, yearFilter);
            document.GeneratePdf(outputStream);
        }
    }

    internal class SuratKeluarMasukDocument : IDocument
    {
        private readonly List<SuratKeluarMasukData> _data;
        private readonly DesaData _desaInfo;
        private readonly string _jenisSuratFilter;
        private readonly int? _tahunFilter;
        private const int RowsPerPage = 13;

        private int _itemCounter = 0;

        public SuratKeluarMasukDocument(List<SuratKeluarMasukData> data, DesaData desaInfo, string jenisSuratFilter, int? tahunFilter)
        {
            _data = data;
            _desaInfo = desaInfo;
            _jenisSuratFilter = jenisSuratFilter;
            _tahunFilter = tahunFilter;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            var dataChunks = _data.Chunk(RowsPerPage).ToList();
            if (!dataChunks.Any())
            {
                dataChunks.Add(Array.Empty<SuratKeluarMasukData>());
            }

            dataChunks.ForEach(pageData =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Times New Roman"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(content => ComposeContentForPage(content, pageData.ToList()));
                    page.Footer().Element(ComposeFooter);
                });
            });
        }

        void ComposeHeader(IContainer container)
        {
            string judul = _jenisSuratFilter.Equals("MASUK", StringComparison.OrdinalIgnoreCase)
                ? "BUKU AGENDA SURAT MASUK"
                : "BUKU AGENDA SURAT KELUAR";
            var tahunText = _tahunFilter.HasValue ? _tahunFilter.Value.ToString() : "SEMUA TAHUN";

            container.Column(column =>
            {
                column.Spacing(5);
                column.Item().AlignCenter().Text(judul).SemiBold().FontSize(12);
                column.Item().AlignCenter().Text($"DESA {_desaInfo.NamaDesa?.ToUpper()} KECAMATAN {_desaInfo.Kecamatan?.ToUpper()} KABUPATEN {_desaInfo.Kabupaten?.ToUpper()}").SemiBold().FontSize(11);
                column.Item().AlignCenter().Text($"TAHUN {tahunText}").SemiBold().FontSize(11);
            });
        }

        void ComposeContentForPage(IContainer container, List<SuratKeluarMasukData> pageData)
        {
            container.PaddingTop(1, Unit.Centimetre).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(25);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(3);
                });

                table.Header(header =>
                {
                    static IContainer HeaderCellStyle(IContainer c) => c.BorderBottom(1.5f).BorderColor(Colors.Black).PaddingVertical(5);
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("No.").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Nomor Surat").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Tgl. Surat").SemiBold();
                    string tglHeader = _jenisSuratFilter.Equals("MASUK", StringComparison.OrdinalIgnoreCase) ? "Tgl. Diterima" : "Tgl. Dikirim";
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text(tglHeader).SemiBold();
                    string asalHeader = _jenisSuratFilter.Equals("MASUK", StringComparison.OrdinalIgnoreCase) ? "Asal Surat" : "Tujuan Surat";
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text(asalHeader).SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text("Perihal").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text("Isi Ringkas").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text("Keterangan").SemiBold();
                });

                static IContainer BodyCellStyle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);

                foreach (var item in pageData)
                {
                    _itemCounter++;
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(_itemCounter.ToString());
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.NomorSurat);
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(item.TanggalSurat.ToString("dd-MM-yyyy"));
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(item.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy"));
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.AsalTujuan);
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.Perihal);
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.IsiRingkas);
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.Keterangan);
                }

                for (int i = pageData.Count; i < RowsPerPage; i++)
                {
                    _itemCounter++;
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(_itemCounter.ToString());
                    for (int j = 0; j < 7; j++)
                    {
                        table.Cell().Element(BodyCellStyle).Text("");
                    }
                }
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(25);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignCenter().Text("Mengetahui,");
                        col.Item().AlignCenter().Text($"KEPALA DESA {_desaInfo.NamaDesa?.ToUpper()}");
                        col.Item().Height(50);
                        col.Item().AlignCenter().Text((_desaInfo.KepalaDesa ?? "...........................").ToUpper()).Underline().SemiBold();
                    });
                    row.RelativeItem().Column(col =>
                    {
                        var culture = new CultureInfo("id-ID");
                        string locationAndDate = $"{_desaInfo.NamaDesa}, {DateTime.Now.ToString("d MMMM yyyy", culture)}";
                        col.Item().AlignCenter().Text(locationAndDate);
                        col.Item().AlignCenter().Text("SEKRETARIS DESA");
                        col.Item().Height(50);
                        col.Item().AlignCenter().Text((_desaInfo.SekretarisDesa ?? "...........................").ToUpper()).Underline().SemiBold();
                    });
                });
            });
        }
    }
}