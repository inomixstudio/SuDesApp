using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Interfaces;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    public class SuratRegisterGenerator : ISuratGenerator
    {
        private readonly AppConfig _config;
        private readonly FileService _fileService;
        private readonly ISuratRepository _suratRepository;
        private readonly IDesaRepository _desaRepository;
        private readonly SettingsManager _settingsManager;
        private readonly ILogger<SuratRegisterGenerator> _logger;
        private const string JudulSurat = "REGISTER SURAT";

        /// <summary>
        /// Label jenis surat untuk surat dari Template Surat: menyebut nama templatenya
        /// (dibaca dari payload surat) supaya buku register tetap informatif.
        /// </summary>
        private static string LabelTemplateSurat(SuratData surat)
        {
            var payload = TemplateSuratTercatat.FromJson(surat?.AdditionalData);
            string nama = payload?.NamaTemplate?.Trim() ?? string.Empty;

            return nama.Length == 0 ? "Surat dari Template Surat" : $"Template: {nama}";
        }
        private const float DEFAULT_FONT_SIZE = 9f;
        private const float TITLE_FONT_SIZE = 12f;

        public SuratRegisterGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<SuratRegisterGenerator> logger,
            ILoggerFactory loggerFactory)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Generator register tidak mewarisi SuratGeneratorBase, jadi pendaftaran
            // font harus dipanggil sendiri — bila ini dokumen PDF pertama pada sesi,
            // keluarga "Times New Roman" belum ada dan dokumen akan gagal dibuat.
            SuratGeneratorBase.DaftarkanFont(config, logger);

            QuestPdfLisensi.Pastikan();
        }

        // Generate PDF berdasarkan ID surat
        public async Task GeneratePdfAsync(Stream outputStream, int idSurat, string? keteranganTextBox = null)
        {
            _logger.LogInformation("Generating PDF for single surat with ID: {IdSurat}", idSurat);

            var suratData = await _suratRepository.GetByIdAsync(idSurat);
            if (suratData == null)
            {
                _logger.LogError("Surat with ID {IdSurat} not found in database.", idSurat);
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Content().Text($"Surat dengan ID {idSurat} tidak ditemukan.").AlignCenter();
                    });
                }).GeneratePdf(outputStream);
                return;
            }

            await GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
        }

        // Generate PDF berdasarkan SuratData tunggal
        public async Task GeneratePdfAsync(Stream outputStream, SuratData suratData, string? keteranganTextBox = null)
        {
            _logger.LogInformation("Generating PDF for single surat with ID: {IdSurat}", suratData.ID_Surat);

            var suratList = new List<SuratData> { suratData };
            await GenerateRegisterPdfAsync(outputStream, suratList, keteranganTextBox);
        }

        // Generate PDF register (banyak surat)
        public async Task GenerateRegisterPdfAsync(Stream outputStream, List<SuratData> suratList, string? keteranganTextBox = null)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (suratList == null)
            {
                _logger.LogError("SuratList is null in GenerateRegisterPdfAsync.");
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Content().Text("Tidak ada data surat untuk ditampilkan dalam register.").AlignCenter();
                    });
                }).GeneratePdf(outputStream);
                return;
            }

            var sortedSuratListForPdf = suratList.OrderBy(s => s.ID_Surat).ToList();
            if (!sortedSuratListForPdf.Any())
            {
                _logger.LogInformation("SuratList is empty after sorting in GenerateRegisterPdfAsync.");
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Content().Text("Tidak ada data surat yang cocok untuk ditampilkan dalam register.").AlignCenter();
                    });
                }).GeneratePdf(outputStream);
                return;
            }

            _logger.LogInformation("Generating PDF register for {Count} surat items, sorted by ID_Surat ascending.", sortedSuratListForPdf.Count);

            DesaData? desaDataForHeaderAndFooter = sortedSuratListForPdf.FirstOrDefault()?.Desa;

            if (desaDataForHeaderAndFooter == null || string.IsNullOrWhiteSpace(desaDataForHeaderAndFooter.NamaDesa))
            {
                _logger.LogWarning("DesaData invalid or missing from list. Fetching fallback from database.");
                desaDataForHeaderAndFooter = await _desaRepository.GetInfoDesaAsync() ?? new DesaData
                {
                    NamaDesa = "Sumberjaya",
                    Kecamatan = "Tempuran",
                    Kabupaten = "Karawang",
                    Alamat = "Belendung 02",
                    Kodepos = "41385",
                    KepalaDesa = "Yayang",
                    SekretarisDesa = "Susi"
                };
            }

            var finalDesaData = desaDataForHeaderAndFooter;

            var isRegisterNtcr = sortedSuratListForPdf.Any(s => SuratConstants.IsNtcr(s.NamaJenis));

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(12.7f, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(DEFAULT_FONT_SIZE).FontFamily("Times New Roman"));

                    // Header
                    page.Header().Column(column =>
                    {
                        column.Item().AlignCenter().Text(isRegisterNtcr ? "BUKU REGISTER NTCR" : "BUKU REGISTER SURAT").Bold().FontSize(16);
                        column.Item().AlignCenter().Text($"DESA {finalDesaData.NamaDesa?.ToUpper()} KECAMATAN {finalDesaData.Kecamatan?.ToUpper()}").Bold().FontSize(12);
                        column.Item().AlignCenter().Text($"KABUPATEN {finalDesaData.Kabupaten?.ToUpper()}").Bold().FontSize(12);
                        column.Item().PaddingVertical(8);
                    });

                    // Tabel. Kolom "Calon Mempelai" hanya dicetak pada buku register NTCR:
                    // pada register surat desa umum, semua barisnya bukan blanko NTCR
                    // sehingga kolom itu selalu berisi "-" (halaman Register Surat pun
                    // sudah lama tidak menampilkannya) — ruangnya lebih berguna untuk
                    // kolom identitas surat.
                    page.Content().PaddingVertical(10).AlignCenter().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);   // No.
                            columns.ConstantColumn(60);   // Tgl Terbit (dd-MM-yyyy)
                            columns.RelativeColumn(1.6f); // No. Register
                            columns.RelativeColumn(2.5f); // Nama
                            columns.RelativeColumn(2.0f); // TTL
                            columns.ConstantColumn(22);   // JK
                            columns.RelativeColumn(3.0f); // Alamat
                            columns.RelativeColumn(2.0f); // Digunakan Untuk
                            columns.RelativeColumn(2.0f); // Jenis Surat

                            if (isRegisterNtcr)
                            {
                                columns.RelativeColumn(1.8f); // Calon Mempelai (khas blanko NTCR)
                            }
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("No").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Tgl Terbit").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("No. Register").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Nama Lengkap").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Tempat Tanggal Lahir").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("JK").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Alamat").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Digunakan Untuk").AlignCenter().Bold();
                            header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Jenis Surat").AlignCenter().Bold();

                            if (isRegisterNtcr)
                            {
                                header.Cell().Element(TableCellStyle).PaddingVertical(5).Text("Calon Mempelai").AlignCenter().Bold();
                            }
                        });

                        int noUrut = 1;
                        foreach (var surat in sortedSuratListForPdf)
                        {
                            // Surat dari Template Surat tidak punya data kependudukan:
                            // identitas penerimanya dibaca dari isian surat itu sendiri.
                            bool dariTemplate = TemplateSuratTercatat.DariTemplateSurat(surat);

                            string namaPemohonAtauInstansi;
                            if (dariTemplate)
                            {
                                string namaTemplate = TemplateSuratTercatat.NamaPenerimaTampil(surat);
                                namaPemohonAtauInstansi = namaTemplate.Length > 0 ? namaTemplate : "[Surat dari Template Surat]";
                            }
                            else if (surat.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                            {
                                namaPemohonAtauInstansi = surat.Instansi?.NamaInstansi ?? "[Data Instansi Tidak Ada]";
                            }
                            else
                            {
                                namaPemohonAtauInstansi = surat.Warga?.Nama ?? "[Data Warga Tidak Ada]";
                            }

                            string ttl = "-";
                            if (dariTemplate)
                            {
                                string ttlTemplate = TemplateSuratTercatat.TempatTanggalLahirTampil(surat);
                                if (ttlTemplate.Length > 0) ttl = ttlTemplate;
                            }
                            else if (surat.Warga != null && !string.IsNullOrWhiteSpace(surat.Warga.TempatLahir) && !string.IsNullOrWhiteSpace(surat.Warga.TanggalLahir))
                            {
                                if (DateTime.TryParseExact(surat.Warga.TanggalLahir, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime tglLahirParsed))
                                {
                                    ttl = $"{surat.Warga.TempatLahir}, {tglLahirParsed:dd-MM-yyyy}";
                                }
                                else
                                {
                                    ttl = $"{surat.Warga.TempatLahir}, {surat.Warga.TanggalLahir}";
                                }
                            }

                            string jk = dariTemplate
                                ? TemplateSuratTercatat.JenisKelaminTampil(surat)
                                : (surat.Warga?.JenisKelamin?.Length > 0 ? surat.Warga.JenisKelamin.Substring(0, 1).ToUpper() : "-");
                            if (jk.Length == 0) jk = "-";

                            string alamatPemohonAtauInstansi = "-";
                            if (surat.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                            {
                                alamatPemohonAtauInstansi = surat.Instansi?.AlamatInstansi ?? "[Alamat Instansi Kosong]";
                            }
                            else if (dariTemplate)
                            {
                                // Surat dari Template Surat menyimpan alamat bebas (bukan
                                // dusun/desa/kecamatan) pada isian suratnya.
                                string alamatTemplate = TemplateSuratTercatat.AlamatPenerimaTampil(surat);
                                alamatPemohonAtauInstansi = alamatTemplate.Length > 0 ? alamatTemplate : "-";
                            }
                            else
                            {
                                alamatPemohonAtauInstansi = GeneratorPdf.AlamatFormatter.Format(
                                    surat.Warga?.Dusun,
                                    surat.Warga?.Desa,
                                    surat.Warga?.Kecamatan,
                                    surat.Warga?.Kabupaten,
                                    fallback: "[Alamat Warga Kosong]").Replace("\n", " ");
                            }

                            string keperluanData = surat.Ntcr != null && !string.IsNullOrWhiteSpace(surat.Ntcr.NamaIstri)
                                ? $"Akan menikah dengan {surat.Ntcr.NamaIstri}"
                                : (surat.Keperluan ?? "-");

                            string namaPasangan = surat.Ntcr != null && !string.IsNullOrWhiteSpace(surat.Ntcr.NamaIstri)
                                ? surat.Ntcr.NamaIstri
                                : "-";

                            string jenisSuratDisplay = surat.NamaJenis?.ToUpperInvariant() switch
                            {
                                "NTCR_N1" => "N1 - Surat Pengantar Nikah",
                                "NTCR_N2" => "N2 - Permohonan Kehendak Nikah",
                                "NTCR_N3" => "N3 - Permohonan Pencatatan Isbat",
                                "NTCR_N4" => "N4 - Persetujuan Calon Pengantin",
                                "NTCR_N5" => "N5 - Surat Izin Orang Tua",
                                "NTCR_N6" => "N6 - Ket. Kematian Suami/Istri",
                                "NTCR_N8" => "N8 - Ket. Numpang Nikah",
                                "REKENING_KORAN" => "Permohonan Rekening Koran",
                                "TEMPLATE_SURAT" => LabelTemplateSurat(surat),
                                "BEDANAMA" => "Surat Ket. Beda Data",
                                "SKD_UMUM" => "SKD Umum",
                                "DOMISILI_WARGA" => "Domisili Warga",
                                "INSTANSI" => "Domisili Instansi/Lembaga",
                                "SKU" => "SKU (Surat Ket. Usaha)",
                                "PENGANTAR_SKCK" => "Pengantar SKCK",
                                "IZIN_ORTU" => "Izin Suami/Orang Tua",
                                "GARAPAN_SAWAH" => "Ket. Garapan Sawah",
                                "KEMATIAN" => "Surat Kematian",
                                "SKTM" => "SKTM (Surat Ket. Tidak Mampu)",
                                _ => surat.NamaJenis?.Replace("_", " ") ?? "-"
                            };

                            table.Cell().Element(TableCellStyle).AlignCenter().Text(noUrut.ToString());
                            table.Cell().Element(TableCellStyle).AlignCenter().Text(surat.TanggalSurat.ToString("dd-MM-yyyy") ?? "-");
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(surat.NomorSurat ?? "-");
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(namaPemohonAtauInstansi);
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(ttl);
                            table.Cell().Element(TableCellStyle).AlignCenter().Text(jk);
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(alamatPemohonAtauInstansi);
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(keperluanData);
                            table.Cell().Element(TableCellStyle).AlignLeft().Text(jenisSuratDisplay);

                            if (isRegisterNtcr)
                            {
                                table.Cell().Element(TableCellStyle).AlignLeft().Text(namaPasangan);
                            }

                            noUrut++;
                        }

                        // Keterangan tambahan dicetak sebagai baris penutup tabel.
                        // QuestPDF menolak page.Content() dipanggil dua kali, sedangkan
                        // cara lama membuat layer konten kedua — akibatnya buku register
                        // gagal dibuat setiap kali kolom keterangan diisi.
                        if (!string.IsNullOrWhiteSpace(keteranganTextBox))
                        {
                            table.Cell().ColumnSpan((uint)(isRegisterNtcr ? 10 : 9)).Element(TableCellStyle)
                                .Text(teks =>
                                {
                                    teks.Span("Keterangan Tambahan: ").Bold();
                                    teks.Span(keteranganTextBox);
                                });
                        }
                    });

                    // Footer
                    page.Footer().AlignCenter().Column(column =>
                    {
                        column.Item().PaddingTop(25).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("Mengetahui,").AlignCenter();
                                col.Item().Text($"Kepala Desa {finalDesaData.NamaDesa ?? ""}").AlignCenter();
                                col.Item().PaddingTop(35).Text("_______________________").AlignCenter().Bold();
                            });
                            row.RelativeItem().Column(col =>
                            {
                                string tanggalSekarang = DateTime.Now.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
                                col.Item().Text($"{finalDesaData.NamaDesa ?? "[Nama Desa]"}, {tanggalSekarang}").AlignCenter();
                                col.Item().Text("Sekretaris Desa").AlignCenter();
                                col.Item().PaddingTop(35).Text("_______________________").AlignCenter().Bold();
                            });
                        });
                        string tanggalCetak = DateTime.Now.ToString("dd MMMM yyyy HH:mm:ss", new CultureInfo("id-ID"));
                        column.Item().PaddingTop(20).Text($"Dicetak pada: {tanggalCetak} oleh Petugas Desa {finalDesaData.NamaDesa}").FontSize(8);
                    });
                });
            }).GeneratePdf(outputStream);
        }

        private static IContainer TableCellStyle(IContainer container)
        {
            return container
                .Border(0.5f)
                .BorderColor(Colors.Black)
                .Padding(3)
                .AlignMiddle();
        }
    }
}
