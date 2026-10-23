using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;

namespace SuDesApp.Services
{
    /// <summary>Sumber data satu arsip register tahunan.</summary>
    public class RegisterTahunanData
    {
        public required int Tahun { get; init; }
        public required string NamaDesa { get; init; }
        public required TutupBukuTahun StatusBuku { get; init; }
        public required HasilVerifikasiNomor Verifikasi { get; init; }
        public required IReadOnlyList<BarisRegisterSurat> Baris { get; init; }
        public DateTime? DicetakPada { get; init; }

        public int JumlahSurat => Baris.Count;
    }

    public interface IArsipRegisterTahunanService
    {
        /// <summary>Susun register satu tahun: status buku, hasil verifikasi, dan daftar surat.</summary>
        Task<RegisterTahunanData> SusunAsync(int tahun, CancellationToken ct = default);

        Task<byte[]> BuatExcelAsync(int tahun, CancellationToken ct = default);
        Task<byte[]> BuatPdfAsync(int tahun, CancellationToken ct = default);

        /// <summary>Nama berkas yang baku untuk hasil arsip tahun ini.</summary>
        string NamaBerkas(int tahun, string ekstensi);
    }

    /// <summary>
    /// Arsip register surat tahunan.
    ///
    /// Ini melengkapi register yang sudah ada di menu Register Surat: register
    /// lama bisa melihat dan mencetak surat, tapi tidak tahu apakah buku tahun
    /// itu sudah ditutup dan tidak tahu apakah nomornya utuh. Arsip tahunan
    /// membawa dua hal itu — status buku dan ringkasan deret.
    ///
    /// Register disusun ulang dari data yang ada setiap kali diminta, bukan
    /// dibaca dari snapshot saat buku ditutup, supaya arsip tetap bisa dicetak
    /// ulang termasuk setelah buku dibuka kembali.
    /// </summary>
    public class ArsipRegisterTahunanService : IArsipRegisterTahunanService
    {
        private readonly IJenisSuratRepository _jenis;
        private readonly ITutupBukuTahunRepository _tutupBuku;
        private readonly IVerifikasiPenomoranService _verifikasi;
        private readonly IDesaRepository _desa;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _sekarang;

        public ArsipRegisterTahunanService(
            IJenisSuratRepository jenis,
            ITutupBukuTahunRepository tutupBuku,
            IVerifikasiPenomoranService verifikasi,
            IDesaRepository desa,
            ILogger<ArsipRegisterTahunanService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _jenis = jenis ?? throw new ArgumentNullException(nameof(jenis));
            _tutupBuku = tutupBuku ?? throw new ArgumentNullException(nameof(tutupBuku));
            _verifikasi = verifikasi ?? throw new ArgumentNullException(nameof(verifikasi));
            _desa = desa ?? throw new ArgumentNullException(nameof(desa));
            _logger = logger ?? (ILogger)NullLogger<ArsipRegisterTahunanService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public string NamaBerkas(int tahun, string ekstensi) =>
            $"Register Surat Desa {tahun}{(ekstensi.StartsWith('.') ? ekstensi : "." + ekstensi)}";

        public async Task<RegisterTahunanData> SusunAsync(int tahun, CancellationToken ct = default)
        {
            if (tahun is < 1900 or > 3000)
                throw new ArgumentOutOfRangeException(nameof(tahun), tahun, "Tahun di luar rentang wajar.");

            var verifikasi = await _verifikasi.PeriksaAsync(tahun).ConfigureAwait(false);
            var baris = await _jenis.GetRegisterSuratAsync(tahun).ConfigureAwait(false);

            var status = await _tutupBuku.GetAsync(tahun, ct).ConfigureAwait(false)
                         ?? new TutupBukuTahun { Tahun = tahun, Status = StatusTutupBuku.Terbuka };

            return new RegisterTahunanData
            {
                Tahun = tahun,
                NamaDesa = await AmbilNamaDesaAsync().ConfigureAwait(false),
                StatusBuku = status,
                Verifikasi = verifikasi,
                Baris = baris,
                DicetakPada = _sekarang()
            };
        }

        private async Task<string> AmbilNamaDesaAsync()
        {
            try
            {
                var desa = await _desa.GetInfoDesaFromCacheAsync().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(desa?.NamaDesa) ? "DESA" : desa!.NamaDesa!;
            }
            catch (Exception ex)
            {
                // Arsip tetap boleh terbit walau nama desa gagal dibaca.
                _logger.LogWarning(ex, "Gagal membaca nama desa; register terbit tanpa nama wilayah.");
                return "DESA";
            }
        }

        // ---------------------------------------------------------------------
        // Excel
        // ---------------------------------------------------------------------

        public async Task<byte[]> BuatExcelAsync(int tahun, CancellationToken ct = default)
        {
            var data = await SusunAsync(tahun, ct).ConfigureAwait(false);
            return RenderExcel(data);
        }

        internal static byte[] RenderExcel(RegisterTahunanData data)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            using var package = new ExcelPackage();
            TulisRingkasan(package, data);
            TulisRegister(package, data);
            TulisDeret(package, data);

            using var penampung = new MemoryStream();
            package.SaveAs(penampung);
            return penampung.ToArray();
        }

        private static void TulisRingkasan(ExcelPackage package, RegisterTahunanData data)
        {
            var ws = package.Workbook.Worksheets.Add("Ringkasan");
            int r = 1;

            ws.Cells[r, 1].Value = "ARSIP REGISTER SURAT DESA";
            ws.Cells[r, 1, r, 4].Merge = true;
            GayaJudul(ws.Cells[r, 1, r, 4]);
            r += 2;

            TulisBaris(ws, ref r, "Nama Desa", data.NamaDesa);
            TulisBaris(ws, ref r, "Tahun Buku", FormatTahun(data.Tahun));
            TulisBaris(ws, ref r, "Status Buku", data.StatusBuku.Tertutup ? "TERTUTUP" : "TERBUKA");
            TulisBaris(ws, ref r, "Jumlah Surat", FormatJumlah(data.JumlahSurat));
            TulisBaris(ws, ref r, "Jumlah Deret", FormatJumlah(data.Verifikasi.Deret.Count));

            if (data.StatusBuku.TanggalTutup is { } tanggalTutup)
            {
                TulisBaris(ws, ref r, "Tanggal Ditutup", tanggalTutup);
                TulisBaris(ws, ref r, "Ditutup Oleh", data.StatusBuku.DitutupOleh ?? "-");
            }

            if (!string.IsNullOrWhiteSpace(data.StatusBuku.Catatan))
            {
                TulisBaris(ws, ref r, "Catatan", data.StatusBuku.Catatan!);
            }

            TulisBaris(ws, ref r, "Waktu Cetak",
                (data.DicetakPada ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            r++;

            TulisBaris(ws, ref r, "Penomoran", data.Verifikasi.Ringkasan);
            foreach (string ket in data.Verifikasi.Peringatan())
            {
                TulisBaris(ws, ref r, "Peringatan", ket);
            }

            foreach (string ket in data.Verifikasi.NomorTahunTidakCocok)
            {
                TulisBaris(ws, ref r, "Tahun Tidak Cocok", ket);
            }

            foreach (var d in data.Verifikasi.Deret.Where(x => x.AdaNomorGanda || x.NomorTidakTerbaca.Count > 0))
            {
                TulisBaris(ws, ref r, "Penyimpangan", d.Ringkasan);
            }

            ws.Column(1).Width = 22;
            ws.Column(2).Width = 60;
            for (int kolom = 3; kolom <= 4; kolom++) ws.Column(kolom).Width = 14;
        }

        private static void TulisRegister(ExcelPackage package, RegisterTahunanData data)
        {
            var ws = package.Workbook.Worksheets.Add("Register");

            string[] judul = { "No", "Nomor Surat", "Tanggal", "Jenis Surat", "Nama Warga", "NIK", "Keperluan", "Keterangan" };
            for (int k = 0; k < judul.Length; k++) ws.Cells[1, k + 1].Value = judul[k];
            GayaHeader(ws.Cells[1, 1, 1, judul.Length]);

            int baris = 2;
            foreach (var s in data.Baris)
            {
                ws.Cells[baris, 1].Value = baris - 1;
                ws.Cells[baris, 2].Value = s.NomorSurat;
                ws.Cells[baris, 3].Value = FormatTanggalPendek(s.TanggalSurat);
                ws.Cells[baris, 4].Value = s.NamaJenis;
                ws.Cells[baris, 5].Value = s.NamaWarga;
                ws.Cells[baris, 6].Value = s.NikWarga;
                ws.Cells[baris, 7].Value = s.Keperluan;
                ws.Cells[baris, 8].Value = s.Keterangan;
                baris++;
            }

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 24;
            ws.Column(3).Width = 13;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 26;
            ws.Column(6).Width = 20;
            ws.Column(7).Width = 30;
            ws.Column(8).Width = 30;
            ws.View.FreezePanes(2, 1);
        }

        private static void TulisDeret(ExcelPackage package, RegisterTahunanData data)
        {
            var ws = package.Workbook.Worksheets.Add("Deret Nomor");

            string[] judul = { "Awalan", "Tahun", "Jumlah", "Pertama", "Terakhir", "Nomor Hilang", "Nomor Ganda", "Tidak Terbaca", "Jenis Surat" };
            for (int k = 0; k < judul.Length; k++) ws.Cells[1, k + 1].Value = judul[k];
            GayaHeader(ws.Cells[1, 1, 1, judul.Length]);

            int baris = 2;
            foreach (var d in data.Verifikasi.Deret)
            {
                ws.Cells[baris, 1].Value = d.Awalan;
                ws.Cells[baris, 2].Value = d.Tahun;
                ws.Cells[baris, 3].Value = d.Jumlah;
                ws.Cells[baris, 4].Value = d.NomorPertama;
                ws.Cells[baris, 5].Value = d.NomorTerakhir;
                ws.Cells[baris, 6].Value = GabungNomor(d.NomorHilang);
                ws.Cells[baris, 7].Value = GabungNomor(d.NomorGanda);
                ws.Cells[baris, 8].Value = string.Join("; ", d.NomorTidakTerbaca);
                ws.Cells[baris, 9].Value = string.Join("; ", d.NamaJenis);
                baris++;
            }

            ws.Column(1).Width = 10;
            ws.Column(2).Width = 8;
            for (int kolom = 3; kolom <= 5; kolom++) ws.Column(kolom).Width = 10;
            ws.Column(6).Width = 30;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 30;
            ws.Column(9).Width = 34;
            ws.View.FreezePanes(2, 1);
        }

        // ---------------------------------------------------------------------
        // PDF
        // ---------------------------------------------------------------------

        public async Task<byte[]> BuatPdfAsync(int tahun, CancellationToken ct = default)
        {
            var data = await SusunAsync(tahun, ct).ConfigureAwait(false);
            return RenderPdf(data);
        }

        internal static byte[] RenderPdf(RegisterTahunanData data)
        {
            QuestPdfLisensi.Pastikan();

            using var penampung = new MemoryStream();
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.2f, Unit.Centimetre);

                    page.Header().Element(c => ComposeHeader(c, data));
                    page.Content().Element(c => ComposeContent(c, data));

                    page.Footer().Element(c => c.AlignCenter().Text(text =>
                    {
                        text.CurrentPageNumber();
                        text.Span(" dari ");
                        text.TotalPages();
                    }));
                });
            }).GeneratePdf(penampung);

            return penampung.ToArray();
        }

        private static void ComposeHeader(IContainer c, RegisterTahunanData data)
        {
            c.BorderBottom(0.8f).BorderColor(Colors.Teal.Medium).PaddingBottom(4f).Row(row =>
            {
                row.Spacing(12);

                // Column dipakai, bukan beberapa Text berurutan: satu Element
                // hanya boleh punya satu anak, jadi teks berbaris butuh Column.
                row.RelativeItem().Element(e => e.Column(kolom =>
                {
                    kolom.Item().Text("ARSIP REGISTER SURAT DESA")
                        .FontSize(14).Bold().FontColor(Colors.Teal.Darken2);
                    kolom.Item().Text($"{data.NamaDesa.ToUpperInvariant()}   -   TAHUN {data.Tahun}")
                        .FontSize(10).SemiBold();
                }));

                row.RelativeItem().AlignRight().Element(e => e.Column(kolom =>
                {
                    kolom.Item().Text(data.StatusBuku.Tertutup ? "STATUS: TERTUTUP" : "STATUS: TERBUKA")
                        .FontSize(9).SemiBold()
                        .FontColor(data.StatusBuku.Tertutup ? Colors.Teal.Darken2 : Colors.Grey.Darken1);
                    kolom.Item().Text($"{data.JumlahSurat} surat - {data.Verifikasi.Deret.Count} deret")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    if (data.DicetakPada is { } cetak)
                    {
                        kolom.Item().Text($"Dicetak {cetak:dd MMMM yyyy HH:mm}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    }
                }));
            });
        }

        private static void ComposeContent(IContainer c, RegisterTahunanData data)
        {
            if (data.Baris.Count == 0)
            {
                c.PaddingTop(12f).AlignCenter()
                    .Text($"Tidak ada surat yang tercatat pada tahun {data.Tahun}.")
                    .FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                return;
            }

            var catatan = KumpulkanCatatan(data);

            // Satu Column menampung tabel, pemisah halaman, dan catatan karena
            // satu container hanya boleh punya satu anak.
            c.Column(kolom =>
            {
                kolom.Item().Element(e => e.PaddingTop(8f).Table(table => IsiTabel(table, data)));

                if (catatan.Count == 0) return;

                kolom.Item().PageBreak();
                kolom.Item().Column(isi =>
                {
                    isi.Item().Text("CATATAN PENOMORAN")
                        .FontSize(10).SemiBold().FontColor(Colors.Teal.Darken2);
                    foreach (string ket in catatan)
                    {
                        isi.Item().PaddingTop(2f).Text(text =>
                        {
                            text.Span("-  ").FontColor(Colors.Teal.Medium).SemiBold();
                            text.Span(ket).FontSize(8.5f);
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Catatan penomoran untuk halaman lampiran arsip. Peringatan berasal dari
        /// hasil verifikasi supaya angkanya sama dengan yang tampil di Excel.
        /// </summary>
        private static List<string> KumpulkanCatatan(RegisterTahunanData data)
        {
            var catatan = new List<string>(data.Verifikasi.Peringatan());

            if (data.Verifikasi.NomorTahunTidakCocok.Count > 0)
            {
                catatan.Add("Nomor dengan tahun tidak cocok: "
                    + string.Join("; ", data.Verifikasi.NomorTahunTidakCocok));
            }

            if (!string.IsNullOrWhiteSpace(data.StatusBuku.Catatan))
            {
                catatan.Add("Catatan penutupan: " + data.StatusBuku.Catatan);
            }

            return catatan;
        }

        private static void IsiTabel(TableDescriptor table, RegisterTahunanData data)
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(26);   // no
                cols.ConstantColumn(112);  // nomor
                cols.ConstantColumn(52);   // tanggal
                cols.ConstantColumn(104);  // jenis
                cols.RelativeColumn();     // nama
                cols.RelativeColumn();     // keperluan
            });

            table.Header(header =>
            {
                foreach (string judul in new[] { "No", "Nomor Surat", "Tanggal", "Jenis Surat", "Nama Warga", "Keperluan" })
                {
                    header.Cell().Element(cell => cell
                        .Background(Colors.Teal.Medium)
                        .PaddingVertical(4f).PaddingHorizontal(3f)
                        .Text(judul).FontSize(8).SemiBold().FontColor(Colors.White));
                }
            });

            int no = 1;
            foreach (var s in data.Baris)
            {
                bool zebrak = no % 2 == 0;
                table.Cell().Element(cell => Sel(cell, zebrak)
                    .Text(no.ToString(CultureInfo.InvariantCulture)).FontSize(8f));
                table.Cell().Element(cell => Sel(cell, false)
                    .Text(s.NomorSurat ?? "-").FontSize(8f).SemiBold());
                table.Cell().Element(cell => Sel(cell, false)
                    .Text(FormatTanggalPendek(s.TanggalSurat)).FontSize(8f));
                table.Cell().Element(cell => Sel(cell, false)
                    .Text(s.NamaJenis ?? "-").FontSize(8f));
                table.Cell().Element(cell => Sel(cell, false)
                    .Text(SusunNama(s)).FontSize(8f));
                table.Cell().Element(cell => Sel(cell, false)
                    .Text(s.Keperluan ?? s.Keterangan ?? "-").FontSize(8f));
                no++;
            }
        }

        private static IContainer Sel(IContainer c, bool zebrak) =>
            c.Background(zebrak ? Colors.Grey.Lighten4 : Colors.White)
                .PaddingVertical(2f).PaddingHorizontal(3f);

        private static string SusunNama(BarisRegisterSurat s)
        {
            string nama = string.IsNullOrWhiteSpace(s.NamaWarga) ? "(tanpa warga)" : s.NamaWarga!.Trim();
            return string.IsNullOrWhiteSpace(s.NikWarga) ? nama : $"{nama} ({s.NikWarga})";
        }

        // ---------------------------------------------------------------------
        // Format
        // ---------------------------------------------------------------------

        private static void TulisBaris(ExcelWorksheet ws, ref int r, string label, string nilai)
        {
            ws.Cells[r, 1].Value = label;
            ws.Cells[r, 1].Style.Font.Bold = true;
            ws.Cells[r, 2].Value = nilai;
            r++;
        }

        private static void GayaJudul(ExcelRange sel)
        {
            sel.Style.Font.Bold = true;
            sel.Style.Font.Size = 14;
            sel.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(0, 105, 107, 94));
            sel.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        private static void GayaHeader(ExcelRange sel)
        {
            sel.Style.Font.Bold = true;
            sel.Style.Font.Color.SetColor(System.Drawing.Color.White);
            sel.Style.Fill.PatternType = ExcelFillStyle.Solid;
            sel.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(31, 122, 140, 140));
        }

        private static string GabungNomor(IReadOnlyList<int> nomor) =>
            nomor.Count == 0
                ? "-"
                : string.Join(", ", nomor.Select(n => n.ToString("000", CultureInfo.InvariantCulture)));

        private static string FormatJumlah(int nilai) => nilai.ToString("N0", CultureInfo.InvariantCulture);

        private static string FormatTahun(int tahun) => tahun.ToString(CultureInfo.InvariantCulture);

        private static string FormatTanggalPendek(string? tanggal)
        {
            string t = (tanggal ?? string.Empty).Trim();
            if (t.Length == 0) return "-";

            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime iso))
                return iso.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

            if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("id-ID"), DateTimeStyles.None, out DateTime lokal))
                return lokal.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

            return t;
        }
    }
}