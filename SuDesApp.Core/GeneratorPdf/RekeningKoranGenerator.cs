using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class RekeningKoranGenerator
    {
        private readonly AppConfig _config;
        private readonly FileService _fileService;
        private readonly ILogger<RekeningKoranGenerator> _logger;
        /// <summary>Ukuran halaman mengikuti Pengaturan Cetak (A4 bawaan atau F4).</summary>
        private static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi();
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        public RekeningKoranGenerator(
            AppConfig config,
            FileService fileService,
            IUnitOfWork unitOfWork,
            ILogger<RekeningKoranGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        /// <summary>
        /// Metode utama untuk membuat PDF Permohonan Rekening Koran.
        /// Metode ini membuat dokumen dari awal karena formatnya sangat unik.
        /// </summary>
        public async Task GeneratePdfAsync(Stream outputStream, RekeningKoranData data)
        {
            if (data == null)
            {
                _logger.LogError("Data RekeningKoranData tidak boleh null.");
                throw new ArgumentNullException(nameof(data));
            }

            // Nama wilayah dipakai di kop, badan surat, dan footer — pakai salinan bersih.
            data.Desa = KopSurat.DesaBersih(data.Desa);

            ValidateRekeningKoranData(data);

            _logger.LogInformation("Memulai pembuatan PDF untuk Permohonan Rekening Koran, NomorSurat={NomorSurat}", data.NomorSurat);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(UkuranHalaman());
                    page.MarginTop(60, Unit.Point);
                    page.MarginRight(50, Unit.Point);
                    page.MarginBottom(30, Unit.Point);
                    page.MarginLeft(50, Unit.Point);
                    page.DefaultTextStyle(x => x.FontFamily(Fonts.TimesNewRoman).FontSize(11));

                    // Memanggil metode untuk menyusun konten kustom
                    ComposeRekeningKoranContent(page, data);
                });
            }).GeneratePdf(outputStream);

            // Task.CompletedTask hanya untuk memenuhi signature async, karena QuestPDF synchronous
            await Task.CompletedTask;
        }

        /// <summary>
        /// Metode privat yang berisi logika untuk menyusun seluruh halaman PDF.
        /// </summary>
        private void ComposeRekeningKoranContent(PageDescriptor page, RekeningKoranData data)
        {
            // Komposisi Header Kustom (Hanya Kop Surat)
            page.Header().Element(c => ComposeKopSurat(c, data.Desa));

            // Konten Surat
            page.Content().Column(column =>
            {
                // Tempat dan Tanggal
                string namaDesa = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(data.Desa.NamaDesa.ToLower());
                column.Item().PaddingTop(15).AlignLeft().PaddingLeft(310).Text($"{namaDesa}, {FormatTanggalIndo(data.TanggalSurat)}");

                // Nomor, Perihal, dan Tujuan
                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"Nomor\t\t: {data.NomorSurat}");
                        col.Item().PaddingTop(3).Text($"Perihal\t\t: Permohonan Print Out Rekening Koran");
                    });

                    row.RelativeItem().PaddingLeft(70).Column(col =>
                    {
                        col.Item().Text("Kepada Yth,").Bold();
                        col.Item().Text($"Customer Service");
                        col.Item().Text($"Bank {data.Bank}");
                        col.Item().Text($"KCP {data.KCP}");
                        col.Item().PaddingTop(5).Text("Di -");
                        col.Item().PaddingLeft(20).Text("Tempat");
                    });
                });

                // Isi Surat
                column.Item().PaddingTop(25).Text("Dengan Hormat,");
                column.Item().PaddingTop(15).Text("Saya yang bertanda tangan di bawah ini:");

                string alamatLengkap = data.AlamatPejabat;
                string alamatFormatted = alamatLengkap.Replace("Kec.", "\nKec.");
                column.Item().PaddingTop(10).PaddingLeft(20).Element(c =>
                {
                    var pejabatData = new List<(string, Action<IContainer>)>
                    {
                        ("Nama", cell => cell.Text(data.NamaPejabat).Bold()),
                        ("Jabatan", cell => cell.Text(data.Jabatan)),
                        ("Alamat", cell => cell.Text(alamatFormatted)),
                    };
                    ComposeFormTable(c, pejabatData);
                });

                column.Item().PaddingTop(15).Text("Bermaksud mengajukan Permohonan Print Out Rekening Koran atas nama:");
                column.Item().PaddingTop(10).PaddingLeft(20).Element(c =>
                {
                    var rekeningData = new List<(string, Action<IContainer>)>
                    {
                        ("Nama Pemegang Rekening", cell => cell.Text(data.NamaPemegangRekening).Bold()),
                        ("No. Rekening Giro", cell => cell.Text(data.NomorRekening)),
                        ("Rekening Koran Periode", cell => cell.Text(FormatTanggalPeriode(data.PeriodeRekening))),
                    };
                    ComposeFormTable(c, rekeningData);
                });

                column.Item().PaddingTop(20).Text("Demikian permohonan ini kami sampaikan, atas perhatian dan kerjasamanya kami ucapkan terima kasih.");

                column.Item().PaddingTop(30).Element(c => ComposeRekeningFooter(c, data));
            });
        }

        /// <summary>
        /// Membuat Kop Surat saja, tanpa judul dan nomor. Isinya diambil dari
        /// KopSurat agar identik dengan dokumen lain (surat & daftar hadir).
        /// </summary>
        private void ComposeKopSurat(IContainer container, DesaData desa)
        {
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(KopSurat.LebarLogo).Element(logoContainer =>
                    {
                        // Gambar kop mengikuti Pengaturan Surat (bisa diganti pengguna).
                        string logoPath = PengaturanCetak.JalurLogoEfektif(_config.LogoPath);
                        if (logoPath != null)
                        {
                            logoContainer.Image(logoPath).FitArea();
                        }
                    });
                    row.RelativeItem().Column(col =>
                    {
                        foreach (var baris in KopSurat.BarisKop(desa))
                        {
                            var teks = col.Item().Text(baris.Teks).FontSize(baris.FontSize);
                            if (baris.Tebal)
                            {
                                teks.Bold();
                            }
                            teks.AlignCenter();
                        }
                    });
                });
                column.Item().PaddingTop(3).Height(KopSurat.GarisTipis).Background(Colors.Black);
                column.Item().PaddingTop(1).Height(KopSurat.GarisTebal).Background(Colors.Black);
            });
        }

        private static void ComposeFormTable(IContainer container, IEnumerable<(string Label, Action<IContainer> Value)> rows)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns => { columns.ConstantColumn(150); columns.ConstantColumn(12); columns.RelativeColumn(); });
                foreach (var row in rows)
                {
                    table.Cell().Text(row.Label);
                    table.Cell().Text(":");
                    table.Cell().Element(row.Value);
                }
            });
        }
        private void ComposeRekeningFooter(IContainer container, RekeningKoranData data)
        {
            container.AlignRight().Width(250).Column(column =>
            {
                column.Item().AlignCenter().Text($"KEPALA DESA {(data.Desa?.NamaDesa ?? "").ToUpper()}");
                column.Item().PaddingTop(50).AlignCenter().Text(NamaFormatter.ToUpperNama(data.NamaPejabat)).Bold();
            });
        }

        // --- Helper Methods ---
        private string FormatTanggalIndo(DateTime tanggal) => tanggal.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));

        private string FormatTanggalPeriode(string periode)
        {
            if (string.IsNullOrWhiteSpace(periode)) return "N/A";
            string[] parts = periode.Split(new[] { " s/d " }, StringSplitOptions.None);
            if (parts.Length != 2) return periode;

            if (DateTime.TryParseExact(parts[0], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tgl1) &&
                DateTime.TryParseExact(parts[1], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tgl2))
            {
                return $"{FormatTanggalIndo(tgl1)} s/d {FormatTanggalIndo(tgl2)}";
            }
            return periode;
        }

        private void ValidateRekeningKoranData(RekeningKoranData data)
        {
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(data.NomorSurat)) missingFields.Add("Nomor Surat");
            if (string.IsNullOrWhiteSpace(data.NamaPejabat)) missingFields.Add("Nama Pejabat");
            if (string.IsNullOrWhiteSpace(data.Jabatan)) missingFields.Add("Jabatan");
            if (string.IsNullOrWhiteSpace(data.NamaPemegangRekening)) missingFields.Add("Nama Pemegang Rekening");
            if (string.IsNullOrWhiteSpace(data.NomorRekening)) missingFields.Add("Nomor Rekening");
            if (string.IsNullOrWhiteSpace(data.PeriodeRekening)) missingFields.Add("Periode Rekening");
            if (string.IsNullOrWhiteSpace(data.Bank)) missingFields.Add("Bank");

            if (missingFields.Any())
            {
                throw new InvalidOperationException($"Data tidak lengkap. Kolom berikut harus diisi: {string.Join(", ", missingFields)}.");
            }
        }
    }
}

