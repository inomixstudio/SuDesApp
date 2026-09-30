using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    public class KeputusanPeraturanGenerator
    {
        private readonly DesaData _desaInfo;

        static KeputusanPeraturanGenerator()
        {
            QuestPdfLisensi.Pastikan();
        }

        public KeputusanPeraturanGenerator(DesaData desaInfo)
        {
            // Generator ini tidak mewarisi SuratGeneratorBase: pastikan font buku
            // (Times New Roman) sudah terdaftar juga bila ini PDF pertama sesi.
            SuratGeneratorBase.DaftarkanFont();

            // Header & footer buku memakai nama wilayah; pakai salinan yang sudah bersih.
            _desaInfo = KopSurat.DesaBersih(desaInfo);
        }

        public void GenerateKeputusanPdf(Stream outputStream, List<DataKeputusan> data, string jenisKeputusanFilter, int? yearFilter)
        {
            var document = new KeputusanDocument(data, _desaInfo, jenisKeputusanFilter, yearFilter);
            document.GeneratePdf(outputStream);
        }
    }

    internal class KeputusanDocument : IDocument
    {
        private readonly List<DataKeputusan> _data;
        private readonly DesaData _desaInfo;
        private readonly string _judulDokumen;
        private readonly int? _tahunFilter;
        private const int RowsPerPage = 13;

        public KeputusanDocument(List<DataKeputusan> data, DesaData desaInfo, string judulDokumen, int? tahunFilter)
        {
            _data = data;
            _desaInfo = desaInfo;
            _judulDokumen = judulDokumen;
            _tahunFilter = tahunFilter;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            var dataChunks = _data.Chunk(RowsPerPage).ToList();
            if (!dataChunks.Any())
            {
                dataChunks.Add(Array.Empty<DataKeputusan>());
            }

            dataChunks.ForEach(pageData =>
            {
                container.Page(page =>
                {
                    page.Size(new PageSize(330, 215, Unit.Millimetre));
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(style => style.FontSize(11).FontFamily("Times New Roman"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(content => ComposeContentForPage(content, pageData.ToList()));
                    page.Footer().Element(ComposeFooter);
                });
            });
        }

        void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(5);
                string judul = _judulDokumen switch
                {
                    "SK" => "BUKU SURAT KEPUTUSAN",
                    "PERDES" => "BUKU PERATURAN DESA",
                    "PERKADES" => "BUKU PERATURAN KEPALA DESA",
                    _ => _judulDokumen
                };
                column.Item().AlignCenter().Text(judul).SemiBold().FontSize(14);
                column.Item().AlignCenter().Text($"DESA {_desaInfo.NamaDesa?.ToUpper()} KECAMATAN {_desaInfo.Kecamatan?.ToUpper()} KABUPATEN {_desaInfo.Kabupaten?.ToUpper()}").SemiBold().FontSize(11);
                var tahunText = _tahunFilter.HasValue ? _tahunFilter.Value.ToString() : "SEMUA TAHUN";
                column.Item().AlignCenter().Text($"TAHUN {tahunText}").SemiBold().FontSize(11);
            });
        }

        void ComposeContentForPage(IContainer container, List<DataKeputusan> pageData)
        {
            // Nomor urut direset tiap halaman (1..RowsPerPage): tiap lembar buku adalah
            // formulir yang bernomor mulai dari 1, tidak melanjutkan halaman sebelumnya.
            int nomorUrut = 0;
            container.PaddingTop(1, Unit.Centimetre).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(6);
                    columns.RelativeColumn(4);
                });

                table.Header(header =>
                {
                    static IContainer HeaderCellStyle(IContainer c) => c.BorderBottom(1.5f).BorderColor(Colors.Black).PaddingVertical(5);
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("No.").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Nomor").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Tanggal").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text("Tentang").SemiBold();
                    header.Cell().Element(HeaderCellStyle).AlignLeft().Text("Keterangan").SemiBold();
                });

                static IContainer BodyCellStyle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);

                foreach (var item in pageData)
                {
                    nomorUrut++;
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(nomorUrut.ToString());
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.Nomor);
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(item.Tanggal.ToString("dd-MM-yyyy"));
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.Tentang);
                    table.Cell().Element(BodyCellStyle).AlignLeft().Text(item.Keterangan);
                }

                for (int i = pageData.Count; i < RowsPerPage; i++)
                {
                    nomorUrut++;
                    table.Cell().Element(BodyCellStyle).AlignCenter().Text(nomorUrut.ToString());
                    for (int j = 0; j < 4; j++)
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