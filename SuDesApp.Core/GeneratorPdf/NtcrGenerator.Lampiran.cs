using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SudesPdf.Core.Layout;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public partial class NtcrGenerator : SuratGeneratorBase
    {
        private static void ComposeBlokLampiran(BadanSurat badan, string jenis)
        {
            float ukuran = KerapatanSurat.Aktif.UkuranFontLampiran;
            string lampiran = NtcrKatalog.Lampiran(jenis);
            string kode = NtcrKatalog.Kode(jenis);

            var baris = new List<string>();
            if (!string.IsNullOrWhiteSpace(lampiran))
            {
                baris.Add($"LAMPIRAN {lampiran}");
            }

            baris.Add("KEPUTUSAN DIREKTUR JENDERAL BIMBINGAN MASYARAKAT ISLAM");
            baris.Add("NOMOR 473 TAHUN 2020");
            baris.Add("TENTANG");
            baris.Add("PETUNJUK TEKNIS PELAKSANAAN PENCATATAN PERNIKAHAN");

            badan.Blok(container => container.PaddingBottom(SuratRenderer.JarakBlok(14f)).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(70);
                    cols.RelativeColumn(30);
                });

                table.Cell().Column(kolom =>
                {
                    foreach (var teks in baris)
                    {
                        kolom.Item().Element(c => SuratRenderer.Teks(c, teks, ukuran: ukuran));
                    }
                });

                table.Cell().Element(c => SuratRenderer.Teks(c,
                    $"Model {(string.IsNullOrEmpty(kode) ? "N1" : kode)}", ukuran: ukuran, rata: Rata.Kanan));
            }));
        }


    }
}
