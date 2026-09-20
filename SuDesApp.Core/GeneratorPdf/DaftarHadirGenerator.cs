using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Membuat PDF Daftar Hadir: kop surat desa di bagian atas, judul yang bisa
    /// diganti, hari &amp; tanggal cetak otomatis, tabel peserta, serta blok tanda
    /// tangan Kepala Desa yang bisa dimatikan.
    /// </summary>
    public class DaftarHadirGenerator
    {
        private readonly AppConfig _config;
        private readonly FileService _fileService;
        private readonly ILogger<DaftarHadirGenerator> _logger;

        static DaftarHadirGenerator()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public DaftarHadirGenerator(
            AppConfig config,
            FileService fileService,
            ILogger<DaftarHadirGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _logger = logger ?? NullLogger<DaftarHadirGenerator>.Instance;
        }

        public void GeneratePdf(Stream outputStream, DaftarHadirData data)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            data ??= new DaftarHadirData();

            // Nama wilayah dipakai di kop dan footer; pakai salinan yang sudah bersih.
            data.Desa = KopSurat.DesaBersih(data.Desa);

            _logger.LogInformation(
                "Membuat PDF Daftar Hadir judul={Judul} peserta={Jumlah}",
                data.Judul,
                data.Peserta?.Count ?? 0);

            new DaftarHadirDocument(data, _config, _fileService).GeneratePdf(outputStream);
        }
    }

    internal class DaftarHadirDocument : IDocument
    {
        /// <summary>
        /// Ukuran halaman mengikuti Pengaturan Cetak (A4 bawaan atau F4), landscape
        /// dipakai bila kolom yang dicetak banyak.
        /// </summary>
        private static PageSize UkuranHalaman(bool landscape)
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi(landscape);
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        /// <summary>Ambang jumlah kolom yang membuat dokumen dialihkan ke landscape.</summary>
        private const int AmbangLandscape = 7;

        /// <summary>Skala tiga baris judul kop saat landscape (ruang vertikalnya pendek).</summary>
        private const float SkalaJudulKopLandscape = 0.82f;

        /// <summary>
        /// Minimal jumlah baris tabel, sisanya diisi baris kosong untuk tanda tangan.
        /// Sengaja tidak terlalu banyak agar blok tanda tangan Kepala Desa tetap
        /// ikut pada halaman yang sama meski kolom yang dicetak banyak.
        /// </summary>
        private const int MinimalBarisPortrait = 10;
        private const int MinimalBarisLandscape = 6;

        private readonly DaftarHadirData _data;
        private readonly AppConfig _config;
        private readonly FileService _fileService;
        private readonly CultureInfo _culture = new("id-ID");

        public DaftarHadirDocument(DaftarHadirData data, AppConfig config, FileService fileService)
        {
            _data = data;
            _config = config;
            _fileService = fileService;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>Jumlah kolom yang dicentang pengguna untuk dicetak.</summary>
        int JumlahKolomTerpilih =>
            new[]
            {
                _data.TampilkanNo, _data.TampilkanNama, _data.TampilkanJenisKelamin,
                _data.TampilkanJabatan, _data.TampilkanNip, _data.TampilkanNik, _data.TampilkanNoHp,
                _data.TampilkanAlamat, _data.TampilkanKeterangan, _data.TampilkanTandaTangan
            }.Count(aktif => aktif);

        bool Landscape => JumlahKolomTerpilih >= AmbangLandscape;

        /// <summary>Baris kosong penambah ruang tanda tangan, menyesuaikan orientasi.</summary>
        int MinimalBaris => Landscape ? MinimalBarisLandscape : MinimalBarisPortrait;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // Bila banyak kolom dipilih (mis. + NIK dan No. Hp), lembar
                // dialihkan ke landscape agar tiap kolom tetap terbaca.
                page.Size(UkuranHalaman(Landscape));
                // Margin landscape lebih ringkas: ruang vertikalnya pendek, sehingga
                // blok tanda tangan Kepala Desa tetap ikut di halaman yang sama.
                page.MarginTop(Landscape ? 32 : 50, Unit.Point);
                page.MarginRight(45, Unit.Point);
                page.MarginBottom(Landscape ? 18 : 40, Unit.Point);
                page.MarginLeft(45, Unit.Point);
                // Landscape memuat banyak kolom → teks sedikit lebih kecil agar tidak berlipat.
                page.DefaultTextStyle(x => x.FontFamily(Fonts.TimesNewRoman).FontSize(Landscape ? 10 : 11));

                // Kop + judul diletakkan di header agar tetap tampil di tiap halaman.
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
            });
        }

        void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Element(ComposeKopSurat);

                column.Item().PaddingTop(14).Element(ComposeJudul);
                column.Item().PaddingTop(6).Element(ComposeBarisInfo);
            });
        }

        /// <summary>
        /// Baris Hari/Tanggal, Pukul, dan Tempat. Label dan titik dua ditaruh di
        /// kolom selebar tetap sehingga semua titik dua lurus satu garis.
        /// </summary>
        void ComposeBarisInfo(IContainer container)
        {
            // Cukup untuk label terpanjang ("Hari/Tanggal") pada font terkecil.
            const float lebarLabel = 80;
            const float lebarTitikDua = 9;

            container.Column(column =>
            {
                void Baris(string label, string nilai)
                {
                    column.Item().Row(row =>
                    {
                        row.ConstantItem(lebarLabel).Text(label);
                        row.ConstantItem(lebarTitikDua).Text(":");
                        row.RelativeItem().Text(nilai);
                    });
                }

                // Hari/Tanggal ikut aturan yang sama: hanya dicetak bila textbox-nya berisi.
                if (!string.IsNullOrWhiteSpace(_data.HariTanggal))
                {
                    Baris("Hari/Tanggal", _data.HariTanggal.Trim());
                }

                // Pukul & Tempat hanya dicetak bila diisi pada form.
                if (!string.IsNullOrWhiteSpace(_data.Pukul))
                {
                    Baris("Pukul", _data.Pukul.Trim());
                }
                if (!string.IsNullOrWhiteSpace(_data.Tempat))
                {
                    Baris("Tempat", _data.Tempat.Trim());
                }
            });
        }

        /// <summary>
        /// Judul dokumen — tiap baris (hasil tombol Enter pada textbox) dirender
        /// sebagai baris tersendiri, rata tengah dan bergaris bawah.
        /// </summary>
        void ComposeJudul(IContainer container)
        {
            var baris = NormalisasiBaris(
                string.IsNullOrWhiteSpace(_data.Judul) ? "DAFTAR HADIR" : _data.Judul);

            container.AlignCenter().Text(text =>
            {
                for (int i = 0; i < baris.Count; i++)
                {
                    var span = i < baris.Count - 1 ? text.Line(baris[i]) : text.Span(baris[i]);
                    span.FontSize(15).SemiBold().Underline();
                }
            });
        }

        /// <summary>
        /// Pecah teks menjadi baris-baris, rapikan spasi tepi, dan buang baris
        /// kosong di awal/akhir (jeda baris di tengah tetap dipertahankan).
        /// </summary>
        static List<string> NormalisasiBaris(string teks)
        {
            var baris = (teks ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(b => b.Trim())
                .ToList();

            while (baris.Count > 0 && baris[0].Length == 0)
            {
                baris.RemoveAt(0);
            }
            while (baris.Count > 0 && baris[^1].Length == 0)
            {
                baris.RemoveAt(baris.Count - 1);
            }

            if (baris.Count == 0)
            {
                baris.Add("DAFTAR HADIR");
            }

            return baris;
        }

        void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(12).Table(ComposeTable);

                if (_data.TampilkanFooterKepalaDesa)
                {
                    // ShowEntire: blok tanda tangan tidak boleh terbelah antar halaman.
                    column.Item().PaddingTop(Landscape ? 12 : 20).ShowEntire().Element(ComposeFooterKepalaDesa);
                }
            });
        }

        /// <summary>
        /// Kop surat desa: logo (bila ada) + lima baris dari KopSurat, ditutup
        /// garis ganda pemisah. Isinya sama persis dengan dokumen lain.
        /// </summary>
        void ComposeKopSurat(IContainer container)
        {
            float skalaJudul = Landscape ? SkalaJudulKopLandscape : 1f;
            var barisKop = KopSurat.BarisKop(_data.Desa, skalaJudul);

            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(KopSurat.LebarLogoUntuk(skalaJudul)).Element(logoContainer =>
                    {
                        // Gambar kop mengikuti Pengaturan Surat (bisa diganti pengguna).
                        string? logoPath = PengaturanCetak.JalurLogoEfektif(_config.LogoPath);
                        if (logoPath != null)
                        {
                            logoContainer.Image(logoPath).FitArea();
                        }
                    });

                    row.RelativeItem().Column(col =>
                    {
                        foreach (var baris in barisKop)
                        {
                            var teks = col.Item().AlignCenter().Text(baris.Teks).FontSize(baris.FontSize);
                            if (baris.Tebal)
                            {
                                teks.Bold();
                            }
                        }
                    });
                });

                column.Item().PaddingTop(3).Height(KopSurat.GarisTipis).Background(Colors.Black);
                column.Item().PaddingTop(1).Height(KopSurat.GarisTebal).Background(Colors.Black);
            });
        }

        void ComposeTable(TableDescriptor table)
        {
            table.ColumnsDefinition(columns =>
            {
                // Di landscape kolom identitas (NIP/NIK/No. Hp) diberi porsi lebih
                // supaya nomor panjang tidak berlipat; di portrait porsi itu
                // dikembalikan ke Nama/Alamat.
                if (_data.TampilkanNo) columns.ConstantColumn(30);
                if (_data.TampilkanNama) columns.RelativeColumn(Landscape ? 3.7f : 4f);
                if (_data.TampilkanJenisKelamin) columns.RelativeColumn(1.4f);
                if (_data.TampilkanJabatan) columns.RelativeColumn(Landscape ? 2.8f : 3f);
                // NIP (18 digit) diberi porsi cukup agar tidak terbelah dua baris.
                if (_data.TampilkanNip) columns.RelativeColumn(Landscape ? 3.2f : 2f);
                if (_data.TampilkanNik) columns.RelativeColumn(Landscape ? 3f : 3f);
                // No. Handphone pakai lebar tetap seperlunya (cukup untuk nomor 14 digit
                // dan judul kolomnya), tidak ikut melebar walau kolom lain dihidupkan.
                if (_data.TampilkanNoHp) columns.ConstantColumn(88);
                if (_data.TampilkanAlamat) columns.RelativeColumn(Landscape ? 2.8f : 3.4f);
                if (_data.TampilkanKeterangan) columns.RelativeColumn(Landscape ? 2.1f : 2.4f);
                if (_data.TampilkanTandaTangan) columns.RelativeColumn(Landscape ? 2.5f : 3f);
            });

            table.Header(header =>
            {
                if (_data.TampilkanNo) header.Cell().Element(HeaderCell).AlignCenter().Text("No.").SemiBold();
                if (_data.TampilkanNama) header.Cell().Element(HeaderCell).AlignCenter().Text("Nama").SemiBold();
                if (_data.TampilkanJenisKelamin) header.Cell().Element(HeaderCell).AlignCenter().Text("L/P").SemiBold();
                if (_data.TampilkanJabatan) header.Cell().Element(HeaderCell).AlignCenter().Text("Jabatan").SemiBold();
                if (_data.TampilkanNip) header.Cell().Element(HeaderCell).AlignCenter().Text("NIP").SemiBold();
                if (_data.TampilkanNik) header.Cell().Element(HeaderCell).AlignCenter().Text("NIK").SemiBold();
                if (_data.TampilkanNoHp) header.Cell().Element(HeaderCell).AlignCenter().Text("No. Handphone").SemiBold();
                if (_data.TampilkanAlamat) header.Cell().Element(HeaderCell).AlignCenter().Text("Alamat").SemiBold();
                if (_data.TampilkanKeterangan) header.Cell().Element(HeaderCell).AlignCenter().Text("Keterangan").SemiBold();
                if (_data.TampilkanTandaTangan) header.Cell().Element(HeaderCell).AlignCenter().Text("Tanda Tangan").SemiBold();
            });

            // Setiap baris yang disusun pengguna di form ikut dicetak — termasuk
            // baris yang sengaja dibiarkan kosong — agar jumlah baris dan nomornya
            // di PDF sama persis dengan tampilan grid di aplikasi. Sisa kekurangan
            // diisi baris kosong sampai MinimalBaris agar formulir tidak terlihat pendek.
            var peserta = (_data.Peserta ?? new List<DaftarHadirPeserta>())
                .Where(p => p != null)
                .ToList();

            for (int i = 0; i < peserta.Count; i++)
            {
                ComposeRow(table, i + 1, peserta[i]);
            }

            int totalBaris = Math.Max(peserta.Count, MinimalBaris);
            for (int i = peserta.Count; i < totalBaris; i++)
            {
                ComposeRow(table, i + 1, null);
            }
        }

        void ComposeRow(TableDescriptor table, int nomor, DaftarHadirPeserta? baris)
        {
            if (_data.TampilkanNo)
            {
                table.Cell().Element(BodyCell).AlignCenter().Text(nomor.ToString());
            }
            if (_data.TampilkanNama)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Nama ?? string.Empty);
            }
            if (_data.TampilkanJenisKelamin)
            {
                table.Cell().Element(BodyCell).AlignCenter().Text(baris?.JenisKelamin ?? string.Empty);
            }
            if (_data.TampilkanJabatan)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Jabatan ?? string.Empty);
            }
            if (_data.TampilkanNip)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Nip ?? string.Empty);
            }
            if (_data.TampilkanNik)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Nik ?? string.Empty);
            }
            if (_data.TampilkanNoHp)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.NoHp ?? string.Empty);
            }
            if (_data.TampilkanAlamat)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Alamat ?? string.Empty);
            }
            if (_data.TampilkanKeterangan)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(baris?.Keterangan ?? string.Empty);
            }
            if (_data.TampilkanTandaTangan)
            {
                // Sel dibiarkan kosong sebagai ruang tanda tangan.
                table.Cell().Element(BodyCell).MinHeight(28);
            }
        }

        static IContainer HeaderCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .Background(Colors.Grey.Lighten4)
            .PaddingVertical(4)
            .PaddingHorizontal(4);

        IContainer BodyCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            // Baris landscape sedikit lebih pendek agar tabel + footer tetap satu halaman.
            .MinHeight(Landscape ? 24 : 28);

        /// <summary>
        /// Blok tanda tangan Kepala Desa di bagian bawah dokumen (opsional).
        /// </summary>
        void ComposeFooterKepalaDesa(IContainer container)
        {
            var desa = _data.Desa ?? new DesaData();
            string namaDesa = string.IsNullOrWhiteSpace(desa.NamaDesa)
                ? string.Empty
                : _culture.TextInfo.ToTitleCase(desa.NamaDesa.ToLowerInvariant());
            // Tanggal pada baris kota ikut Hari/Tanggal yang dipakai di atas, bila
            // teks itu bisa dibaca sebagai tanggal; kalau tidak, pakai tanggal cetak.
            DateTime tanggalKaki = _data.TanggalDariHariTanggal() ?? _data.TanggalCetak;
            string tempatTanggal = string.IsNullOrWhiteSpace(namaDesa)
                ? TanggalIndo(tanggalKaki)
                : $"{namaDesa}, {TanggalIndo(tanggalKaki)}";

            container.AlignRight().Width(260).Column(column =>
            {
                column.Item().AlignCenter().Text(tempatTanggal);
                column.Item().AlignCenter().Text($"KEPALA DESA {(desa.NamaDesa ?? string.Empty).ToUpperInvariant()}");
                column.Item().Height(Landscape ? 36 : 55);
                column.Item().AlignCenter().Text((desa.KepalaDesa ?? "...........................").ToUpperInvariant()).Bold().Underline();
            });
        }

        /// <summary>Nama hari + tanggal dalam bahasa Indonesia, mis. "Senin, 17 September 2026".</summary>
        string HariTanggalIndo(DateTime tanggal) => tanggal.ToString("dddd, d MMMM yyyy", _culture);

        string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", _culture);
    }
}
