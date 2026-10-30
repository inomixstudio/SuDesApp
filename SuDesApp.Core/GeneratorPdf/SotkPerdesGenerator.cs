// SotkPerdesGenerator.cs
// Dua dokumen pasangan pada menu SOTK & Perdes:
//   1. SOTK Pemerintah Desa — susunan organisasi (lambang Garuda, uraian tugas
//      pokok per unsur, dan tabel pejabat menurut data perangkat tersimpan).
//   2. Peraturan Desa tentang SOTK — naskah lengkap Perdes (lambang Garuda,
//      Menimbang, Mengingat, MEMUTUSKAN, BAB I s.d. Ketentuan Penutup,
//      pengundangan & pengesahan Kepala Desa).
// Keduanya memakai Times New Roman, ukuran kertas Pengaturan Cetak (A4/F4),
// dan tata letak naskah dinas seperti dokumen lain aplikasi.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>Satu baris pejabat pada tabel susunan organisasi SOTK.</summary>
    public sealed class SotkPerangkatBaris
    {
        public string Jabatan { get; init; } = string.Empty;
        public string Nama { get; init; } = "-";
        public string Wilayah { get; init; } = "-";
    }

    /// <summary>Isi dokumen SOTK Pemerintah Desa.</summary>
    public sealed class SotkDesaData
    {
        public DesaData? Desa { get; init; }
        public DateTime Tanggal { get; init; } = DateTime.Today;

        /// <summary>Pejabat menurut data perangkat tersimpan (jabatan kosong tetap
        /// ada barisnya dengan nama "-").</summary>
        public IReadOnlyList<SotkPerangkatBaris> Pejabat { get; init; } = Array.Empty<SotkPerangkatBaris>();
    }

    /// <summary>Isi naskah Peraturan Desa tentang SOTK.</summary>
    public sealed class PerdesSotkData
    {
        public DesaData? Desa { get; init; }

        /// <summary>Nomor Perdes, bebas teks (mis. "2" atau "4/DESA/2026").</summary>
        public string Nomor { get; init; } = string.Empty;

        public DateTime Tanggal { get; init; } = DateTime.Today;

        /// <summary>Tempat penetapan; kosong memakai nama desa.</summary>
        public string Tempat { get; init; } = string.Empty;

        /// <summary>Rujukan Perda/Perbup setempat yang diverifikasi untuk desa ini.</summary>
        public string RujukanDaerah { get; init; } = string.Empty;

        /// <summary>Nama dan jabatan BPD aktif dari menu Perangkat Desa, sebagai data referensi terpisah.</summary>
        public IReadOnlyList<string> NamaBpd { get; init; } = Array.Empty<string>();

        /// <summary>Pejabat aktif untuk lampiran susunan organisasi yang faktual.</summary>
        public IReadOnlyList<SotkPerangkatBaris> SusunanPerangkat { get; init; } = Array.Empty<SotkPerangkatBaris>();
    }

    /// <summary>Membuat PDF SOTK Pemerintah Desa dari data perangkat tersimpan.</summary>
    public class SotkDesaGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<SotkDesaGenerator> _logger;

        static SotkDesaGenerator()
        {
        }

        public SotkDesaGenerator(AppConfig config, ILogger<SotkDesaGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void GeneratePdf(Stream outputStream, SotkDesaData data)
        {
            data ??= new SotkDesaData();
            _logger.LogInformation("Membuat PDF SOTK Pemerintah Desa pejabat={Jumlah}", data.Pejabat.Count);
            var desa = KopSurat.DesaBersih(data.Desa);
            new SotkDesaDocument(data, desa, _config).GeneratePdf(outputStream);
        }
    }

    /// <summary>Membuat PDF naskah lengkap Peraturan Desa tentang SOTK.</summary>
    public class PerdesSotkGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<PerdesSotkGenerator> _logger;

        static PerdesSotkGenerator()
        {
        }

        public PerdesSotkGenerator(AppConfig config, ILogger<PerdesSotkGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void GeneratePdf(Stream outputStream, PerdesSotkData data)
        {
            data ??= new PerdesSotkData();
            _logger.LogInformation("Membuat PDF Perdes SOTK nomor={Nomor}", data.Nomor);
            var desa = KopSurat.DesaBersih(data.Desa);
            new PerdesSotkDocument(data, desa).GeneratePdf(outputStream);
        }
    }

    /// <summary>
    /// Bagian naskah yang dipakai bersama kedua dokumen: kop surat, judul bab,
    /// butir bernomor, pasal, dan blok penetap — supaya SOTK dan Perdes tampak
    /// satu keluarga naskah dengan SK.
    /// </summary>
    internal static class NaskahDesa
    {
        public const float UkuranTeks = 11f;
        public const float UkuranJudul = 13f;

        /// <summary>
        /// Jarak baris teks paragraf. 1,08× ukuran font membuat paragraf panjang
        /// terlihat padat dan sulit dibaca; 1,15× tetap hemat ruang tetapi nyaman.
        /// </summary>
        public const float JarakBarisTeks = 1.15f;

        // Skala jarak tunggal untuk menjaga alur naskah rapat dan konsisten.
        public const float JarakRapat = 4f;
        public const float JarakParagraf = 8f;
        public const float JarakBagian = 12f;
        public const float JarakTandaTangan = 14f;

        private static readonly string[] NamaBulan =
        {
            "Januari", "Februari", "Maret", "April", "Mei", "Juni",
            "Juli", "Agustus", "September", "Oktober", "November", "Desember"
        };

        public static string TanggalIndo(DateTime tanggal) =>
            $"{tanggal.Day} {NamaBulan[tanggal.Month - 1]} {tanggal.Year}";

        /// <summary>Ukuran kertas mengikuti Pengaturan Cetak (A4 bawaan atau F4).</summary>
        public static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi();
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        /// <summary>
        /// Lambang negara (Garuda Pancasila) ±2,5 × 2,7 cm di puncak naskah
        /// peraturan. Peraturan Desa/Peraturan Kepala Desa memakai lambang
        /// Garuda — bukan kop surat desa. Bila resource gambar tidak ada,
        /// naskah tetap terbentuk tanpa lambang.
        /// </summary>
        public static void LambangNegara(ColumnDescriptor kolom)
        {
            byte[]? garuda = GambarBawaan.Garuda();
            if (garuda is null) return;

            kolom.Item().AlignCenter().PaddingBottom(6).Width(71f).Height(77f)
                .Element(blok => blok.Image(garuda).FitArea());
        }

        /// <summary>Judul rata tengah (blok kepala dokumen).</summary>
        public static void JudulTengah(ColumnDescriptor kolom, string teks, float ukuran = UkuranJudul, bool spasiAtas = false)
        {
            var item = kolom.Item().AlignCenter();
            if (spasiAtas) item = item.PaddingTop(JarakBagian);
            item.Text(teks).FontSize(ukuran).Bold();
        }

        /// <summary>Judul BAB: "BAB I" di atas, judul di bawahnya — rata tengah tebal.</summary>
        public static void JudulBab(ColumnDescriptor kolom, string bab, string judul)
        {
            kolom.Item().PaddingTop(JarakBagian).AlignCenter().Text(bab).FontSize(UkuranTeks).Bold();
            kolom.Item().PaddingBottom(JarakRapat).AlignCenter().Text(judul).FontSize(UkuranTeks).Bold();
        }

        /// <summary>Butir bernomor/berhuruf (Menimbang a., Mengingat 1.).</summary>
        public static void ButirBerlabel(ColumnDescriptor kolom, string label, IEnumerable<(string Nomor, string Teks)> butir)
        {
            kolom.Item().Text(label).FontSize(UkuranTeks).Bold();
            foreach (var (nomor, teks) in butir)
            {
                kolom.Item().Row(baris =>
                {
                    baris.ConstantItem(36).Text(nomor).FontSize(UkuranTeks);
                    baris.RelativeItem().Text(teks).FontSize(UkuranTeks);
                });
            }

            kolom.Item().PaddingBottom(JarakParagraf);
        }

        /// <summary>Satu pasal: baris "Pasal N" lalu tiap ayat bernomor "(1) …".</summary>
        public static void Pasal(ColumnDescriptor kolom, int nomor, params string[] ayat)
        {
            kolom.Item().PaddingTop(JarakParagraf).AlignCenter().Text($"Pasal {nomor}").FontSize(UkuranTeks).Bold();
            for (int i = 0; i < ayat.Length; i++)
            {
                kolom.Item().Row(baris =>
                {
                    baris.ConstantItem(36).Text($"({i + 1})").FontSize(UkuranTeks);
                    baris.RelativeItem().Text(ayat[i]).FontSize(UkuranTeks);
                });
            }

            kolom.Item().PaddingBottom(JarakRapat);
        }

        /// <summary>
        /// Blok penetapan Perdes: "Ditetapkan di … pada
        /// tanggal …" rata kanan di atas nama Kepala Desa tebal bergaris bawah.
        /// Blok disatukan ShowEntire supaya tidak terbelah antar halaman.
        /// </summary>
        public static void BlokPenetap(ColumnDescriptor kolom, DesaData desa, DateTime tanggal, string? tempat = null)
        {
            string wilayah = string.IsNullOrWhiteSpace(desa.NamaDesa) ? "...................." : desa.NamaDesa!.Trim();
            string titik = string.IsNullOrWhiteSpace(tempat) ? wilayah : tempat.Trim();
            string kepala = string.IsNullOrWhiteSpace(desa.KepalaDesa) ? "..........................." : desa.KepalaDesa!.Trim();

            // Blok penanda tangan menempel di kolom KANAN halaman: tempat/tanggal,
            // jabatan, ruang tanda tangan, lalu nama Kepala Desa — ISI bloknya
            // rata tengah, sesuai bentuk baku tanda tangan surat dinas.
            kolom.Item().PaddingTop(JarakTandaTangan).ShowEntire().AlignRight().Width(280).Column(blok =>
            {
                blok.Item().AlignCenter().Text($"Ditetapkan di {titik}").FontSize(UkuranTeks);
                blok.Item().AlignCenter().Text($"pada tanggal {TanggalIndo(tanggal)}").FontSize(UkuranTeks);
                blok.Item().PaddingTop(JarakBagian).AlignCenter()
                    .Text($"KEPALA DESA {wilayah}".ToUpperInvariant())
                    .FontSize(UkuranTeks);
                blok.Item().Height(45);
                blok.Item().AlignCenter().Text(kepala.ToUpperInvariant())
                    .FontSize(UkuranTeks).Bold().Underline();
            });
        }

        /// <summary>Blok pengundangan dengan ruang tanggal dan nomor Lembaran Desa.</summary>
        public static void BlokPengundang(ColumnDescriptor kolom, DesaData desa, string? tempat = null)
        {
            string wilayah = string.IsNullOrWhiteSpace(desa.NamaDesa) ? "...................." : desa.NamaDesa!.Trim();
            string titik = string.IsNullOrWhiteSpace(tempat) ? wilayah : tempat.Trim();
            string sekretaris = string.IsNullOrWhiteSpace(desa.SekretarisDesa) ? "..........................." : desa.SekretarisDesa!.Trim();

            kolom.Item().PaddingTop(JarakTandaTangan).ShowEntire().AlignLeft().Column(blok =>
            {
                blok.Item().AlignLeft().Text($"Diundangkan di {titik}").FontSize(UkuranTeks);
                blok.Item().AlignLeft().Text("pada tanggal ....................").FontSize(UkuranTeks);
                blok.Item().PaddingTop(JarakBagian).AlignLeft()
                    .Text($"SEKRETARIS DESA {wilayah}".ToUpperInvariant())
                    .FontSize(UkuranTeks);
                blok.Item().Height(45);
                blok.Item().AlignLeft().Text(sekretaris.ToUpperInvariant())
                    .FontSize(UkuranTeks).Bold().Underline();
                blok.Item().PaddingTop(JarakBagian).AlignLeft()
                    .Text("LEMBARAN DESA " + wilayah.ToUpperInvariant()).FontSize(UkuranTeks);
                blok.Item().AlignLeft().Text("TAHUN ........ NOMOR ........").FontSize(UkuranTeks);
            });
        }
    }

    // =====================================================================
    // Dokumen SOTK
    // =====================================================================

    internal static class KopSotkPdf
{
    /// <summary>
    /// Kop naskah SOTK. Susunannya milik <see cref="KopSurat.Gambar"/> supaya sama
    /// dengan dokumen lain — termasuk baris surel desa yang opsional.
    /// </summary>
    public static void ComposeKop(ColumnDescriptor kolom, DesaData desa, AppConfig config)
        => kolom.Item().Element(c =>
            KopSurat.Gambar(c, desa, PengaturanCetak.JalurLogoEfektif(config.LogoPath)));
}

internal class SotkDesaDocument : IDocument
    {
        private readonly SotkDesaData _data;
        private readonly DesaData _desa;
        private readonly AppConfig _config;

        public SotkDesaDocument(SotkDesaData data, DesaData desa, AppConfig config)
        {
            _data = data;
            _desa = desa;
            _config = config;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(halaman =>
            {
                halaman.Size(NaskahDesa.UkuranHalaman());
                halaman.MarginTop(48);
                halaman.MarginRight(54);
                halaman.MarginBottom(48);
                halaman.MarginLeft(54);
                halaman.DefaultTextStyle(x => x
                    .FontFamily("Times New Roman")
                    .FontSize(NaskahDesa.UkuranTeks)
                    .LineHeight(1.05f));

                halaman.Content().Column(kolom =>
                {
                    KopSotkPdf.ComposeKop(kolom, _desa, _config);
                    kolom.Item().PaddingTop(NaskahDesa.JarakBagian).Column(isi => ComposeIsi(isi));
                });
            });
        }

        private void ComposeIsi(ColumnDescriptor kolom)
        {
            string namaDesa = string.IsNullOrWhiteSpace(_desa.NamaDesa) ? "............" : _desa.NamaDesa!.Trim();

            NaskahDesa.JudulTengah(kolom, "SUSUNAN ORGANISASI DAN TATA KERJA", NaskahDesa.UkuranJudul);

            // ---------- BAB I ----------
            NaskahDesa.JudulBab(kolom, "BAB I", "KETENTUAN UMUM");
            NaskahDesa.Pasal(kolom, 1,
                "Susunan Organisasi dan Tata Kerja Pemerintah Desa yang selanjutnya disingkat SOTK Pemerintah Desa "
                + $"adalah ringkasan susunan jabatan Pemerintah Desa {namaDesa} berdasarkan data Perangkat Desa "
                + "yang tercatat saat dokumen ini dibuat.");
            NaskahDesa.Pasal(kolom, 2,
                "Susunan, jumlah, dan nomenklatur jabatan mengikuti ketentuan peraturan perundang-undangan dan peraturan daerah yang berlaku.");

            // ---------- BAB II ----------
            NaskahDesa.JudulBab(kolom, "BAB II", "SUSUNAN ORGANISASI");
            NaskahDesa.Pasal(kolom, 3,
                "Nama dan jabatan pada tabel berikut mengikuti data aktif yang tersimpan pada menu Perangkat Desa.");
            kolom.Item().PaddingTop(NaskahDesa.JarakRapat).Table(tabel => ComposeTabelPejabat(tabel));

            // ---------- BAB III ----------
            NaskahDesa.JudulBab(kolom, "BAB III", "KETENTUAN TUGAS DAN TATA KERJA");
            NaskahDesa.Pasal(kolom, 4,
                "Uraian tugas, fungsi, dan hubungan kerja setiap jabatan mengikuti peraturan perundang-undangan serta peraturan daerah yang berlaku.");

            // ---------- BAB IV ----------
            NaskahDesa.JudulBab(kolom, "BAB IV", "KETENTUAN PENUTUP");
            NaskahDesa.Pasal(kolom, 5,
                "Dokumen ini merupakan ringkasan data organisasi. Ketentuan yang mengikat mengikuti peraturan desa dan peraturan daerah yang berlaku.");
            NaskahDesa.BlokPenetap(kolom, _desa, _data.Tanggal);
        }

        private void ComposeTabelPejabat(TableDescriptor tabel)
        {
            tabel.ColumnsDefinition(c =>
            {
                c.ConstantColumn(26);
                c.RelativeColumn(3f);
                c.RelativeColumn(3f);
                c.RelativeColumn(1.6f);
            });

            tabel.Header(header =>
            {
                foreach (var judul in new[] { "NO", "JABATAN", "NAMA", "WILAYAH" })
                {
                    header.Cell().BorderBottom(1).PaddingVertical(2)
                        .AlignCenter().Text(judul).FontSize(10f).Bold();
                }
            });

            int no = 0;
            foreach (var baris in _data.Pejabat)
            {
                no++;
                tabel.Cell().BorderBottom(0.5f).PaddingVertical(2).AlignCenter()
                    .Text(no.ToString(CultureInfo.InvariantCulture)).FontSize(10f);
                tabel.Cell().BorderBottom(0.5f).PaddingVertical(2)
                    .Text(baris.Jabatan).FontSize(10f);
                tabel.Cell().BorderBottom(0.5f).PaddingVertical(2)
                    .Text(string.IsNullOrWhiteSpace(baris.Nama) ? "-" : baris.Nama).FontSize(10f);
                tabel.Cell().BorderBottom(0.5f).PaddingVertical(2)
                    .Text(string.IsNullOrWhiteSpace(baris.Wilayah) ? "-" : baris.Wilayah).FontSize(10f);
            }
        }

        private static string Isi(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "............" : nilai.Trim();
    }

    // =====================================================================
    // Dokumen Perdes
    // =====================================================================

    internal class PerdesSotkDocument : IDocument
    {
        private const string JudulPerdes =
            "SUSUNAN ORGANISASI DAN TATA KERJA PEMERINTAH DESA DAN PERANGKAT DESA";

        private readonly PerdesSotkData _data;
        private readonly DesaData _desa;

        public PerdesSotkDocument(PerdesSotkData data, DesaData desa)
        {
            _data = data;
            _desa = desa;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(halaman =>
            {
                halaman.Size(NaskahDesa.UkuranHalaman());
                halaman.MarginTop(48);
                halaman.MarginRight(54);
                halaman.MarginBottom(48);
                halaman.MarginLeft(54);
                halaman.DefaultTextStyle(x => x
                    .FontFamily("Times New Roman")
                    .FontSize(NaskahDesa.UkuranTeks)
                    .LineHeight(1.05f));

                halaman.Content().Column(kolom => ComposeIsi(kolom));
            });
        }

        private void ComposeIsi(ColumnDescriptor kolom)
        {
            string namaDesa = string.IsNullOrWhiteSpace(_desa.NamaDesa) ? "............" : _desa.NamaDesa!.Trim();
            string tahun = _data.Tanggal.Year.ToString(CultureInfo.InvariantCulture);

            // Perdes memakai lambang Garuda di puncak naskah, bukan kop surat
            // desa — bentuk baku peraturan perundang-undangan di desa.
            NaskahDesa.LambangNegara(kolom);

            // ---------- Kepala Perdes ----------
            NaskahDesa.JudulTengah(kolom, $"KEPALA DESA {namaDesa}".ToUpperInvariant(), NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, $"KECAMATAN {Isi(_desa.Kecamatan)} {KopSurat.LabelKabupaten(_desa)} {Isi(KopSurat.NamaKabupaten(_desa))}".ToUpperInvariant(), NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, $"PERATURAN DESA {namaDesa}".ToUpperInvariant(), NaskahDesa.UkuranJudul, spasiAtas: true);
            NaskahDesa.JudulTengah(kolom, $"NOMOR {NomorTampil()} TAHUN {tahun}", NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, "TENTANG", NaskahDesa.UkuranTeks, spasiAtas: true);
            NaskahDesa.JudulTengah(kolom, JudulPerdes);

            kolom.Item().PaddingTop(NaskahDesa.JarakTandaTangan).AlignCenter()
                .Text("DENGAN RAHMAT TUHAN YANG MAHA ESA").FontSize(NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, $"KEPALA DESA {namaDesa},".ToUpperInvariant(), NaskahDesa.UkuranTeks, spasiAtas: true);

            // ---------- Menimbang & Mengingat ----------
            NaskahDesa.ButirBerlabel(kolom, "Menimbang :", new[]
            {
                ("a.", $"bahwa untuk menjamin kelancaran penyelenggaraan pemerintahan, pembangunan, "
                    + $"pembinaan kemasyarakatan, dan pemberdayaan masyarakat di Desa {namaDesa} "
                    + "perlu disusun Susunan Organisasi dan Tata Kerja Pemerintah Desa dan Perangkat Desa;"),
                ("b.", "bahwa susunan organisasi dan tata kerja sebagaimana dimaksud pada huruf a perlu "
                    + "ditetapkan dengan Peraturan Desa agar memiliki kepastian hukum dan mengikat "
                    + "setiap pihak;"),
                ("c.", "bahwa berdasarkan pertimbangan sebagaimana dimaksud pada huruf a dan huruf b, "
                    + "perlu menetapkan Peraturan Desa tentang Susunan Organisasi dan Tata Kerja "
                    + "Pemerintah Desa dan Perangkat Desa.")
            });

            var dasarHukum = new List<(string Nomor, string Teks)>
            {
                ("1.", "Pasal 18 ayat (6) Undang-Undang Dasar Negara Republik Indonesia Tahun 1945;"),
                ("2.", "Undang-Undang Nomor 6 Tahun 2014 tentang Desa sebagaimana telah beberapa kali diubah, "
                    + "terakhir dengan Undang-Undang Nomor 3 Tahun 2024 tentang Perubahan Kedua atas "
                    + "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;"),
                ("3.", "Peraturan Pemerintah Nomor 43 Tahun 2014 tentang Peraturan Pelaksanaan "
                    + "Undang-Undang Nomor 6 Tahun 2014 tentang Desa sebagaimana telah beberapa kali diubah;"),
                ("4.", "Peraturan Menteri Dalam Negeri Nomor 84 Tahun 2015 tentang Susunan Organisasi "
                    + "dan Tata Kerja Pemerintah Desa;"),
                ("5.", "Peraturan Menteri Dalam Negeri Nomor 111 Tahun 2014 tentang Pedoman Teknis "
                    + "Peraturan di Desa.")
            };
            foreach (string rujukan in (_data.RujukanDaerah ?? string.Empty)
                         .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                dasarHukum.Add(($"{dasarHukum.Count + 1}.", rujukan.EndsWith(";") || rujukan.EndsWith(".") ? rujukan : rujukan + ";"));
            }
            NaskahDesa.ButirBerlabel(kolom, "Mengingat :", dasarHukum);

            kolom.Item().PaddingTop(NaskahDesa.JarakBagian).AlignCenter()
                .Text("DENGAN PERSETUJUAN BERSAMA").FontSize(NaskahDesa.UkuranTeks).Bold();
            kolom.Item().PaddingTop(NaskahDesa.JarakParagraf).AlignCenter()
                .Text($"BADAN PERMUSYAWARATAN DESA {namaDesa.ToUpperInvariant()}").FontSize(NaskahDesa.UkuranTeks).Bold();
            kolom.Item().AlignCenter().Text($"dan\nKEPALA DESA {namaDesa.ToUpperInvariant()}")
                .FontSize(NaskahDesa.UkuranTeks).Bold();
            // ---------- Memutuskan ----------
            kolom.Item().PaddingTop(NaskahDesa.JarakBagian).AlignCenter()
                .Text("MEMUTUSKAN :").FontSize(NaskahDesa.UkuranTeks).Bold();
            kolom.Item().PaddingBottom(NaskahDesa.JarakParagraf).Row(baris =>
            {
                baris.ConstantItem(82).Text("Menetapkan :").FontSize(NaskahDesa.UkuranTeks);
                baris.RelativeItem().Text($"PERATURAN DESA {namaDesa} TENTANG {JudulPerdes}")
                    .FontSize(NaskahDesa.UkuranTeks);
            });

            NaskahDesa.JudulBab(kolom, "BAB I", "KETENTUAN UMUM");
            NaskahDesa.Pasal(kolom, 1,
                "Dalam Peraturan Desa ini yang dimaksud dengan Desa adalah Desa " + namaDesa + ".",
                "Pemerintah Desa adalah Kepala Desa yang dibantu perangkat Desa sebagai unsur penyelenggara Pemerintahan Desa.",
                "Perangkat Desa adalah unsur pembantu Kepala Desa dalam penyelenggaraan pemerintahan Desa.");

            NaskahDesa.JudulBab(kolom, "BAB II", "SUSUNAN ORGANISASI DAN TATA KERJA");
            NaskahDesa.Pasal(kolom, 2,
                "Pemerintah Desa terdiri atas Kepala Desa dan perangkat Desa.",
                "Perangkat Desa terdiri atas Sekretariat Desa, pelaksana teknis, dan pelaksana kewilayahan sesuai kebutuhan serta ketentuan peraturan perundang-undangan.",
                "Bagan organisasi tercantum dalam Lampiran yang merupakan bagian tidak terpisahkan dari Peraturan Desa ini.");
            NaskahDesa.Pasal(kolom, 3,
                "Kepala Desa memimpin penyelenggaraan pemerintahan, pembangunan, pembinaan kemasyarakatan, dan pemberdayaan masyarakat Desa.",
                "Perangkat Desa bertanggung jawab kepada Kepala Desa dan melaksanakan tugas berdasarkan jabatan serta pembagian kerja yang ditetapkan.");

            NaskahDesa.JudulBab(kolom, "BAB III", "TUGAS DAN FUNGSI");
            NaskahDesa.Pasal(kolom, 4,
                "Sekretariat Desa membantu Kepala Desa dalam bidang administrasi pemerintahan, pembangunan, pembinaan kemasyarakatan, dan pemberdayaan masyarakat.",
                "Pelaksana teknis membantu Kepala Desa melaksanakan urusan pemerintahan sesuai bidang tugasnya.",
                "Pelaksana kewilayahan membantu Kepala Desa dalam penyelenggaraan pemerintahan dan pelayanan masyarakat di wilayah kerjanya.");
            NaskahDesa.Pasal(kolom, 5,
                "Rincian tugas perangkat Desa dilaksanakan sesuai bidang jabatan, peraturan perundang-undangan, dan penugasan Kepala Desa.",
                "Dalam melaksanakan tugas, perangkat Desa mengutamakan koordinasi, tertib administrasi, pelayanan masyarakat, dan akuntabilitas.");

            NaskahDesa.JudulBab(kolom, "BAB IV", "PENGANGKATAN DAN TATA KERJA");
            NaskahDesa.Pasal(kolom, 6,
                "Pengangkatan dan pemberhentian perangkat Desa dilaksanakan sesuai ketentuan peraturan perundang-undangan.",
                "Hubungan kerja Kepala Desa dan perangkat Desa bersifat koordinatif, konsultatif, dan bertanggung jawab sesuai ketentuan yang berlaku.");
            NaskahDesa.JudulBab(kolom, "BAB V", "KETENTUAN PERALIHAN");
            NaskahDesa.Pasal(kolom, 7,
                "Pada saat Peraturan Desa ini mulai berlaku, penyesuaian organisasi dan tata kerja dilaksanakan sesuai ketentuan peraturan perundang-undangan.");
            NaskahDesa.JudulBab(kolom, "BAB VI", "KETENTUAN PENUTUP");
            NaskahDesa.Pasal(kolom, 8,
                "Pada saat Peraturan Desa ini mulai berlaku, Peraturan Desa tentang Susunan Organisasi dan Tata Kerja yang bertentangan dengan Peraturan Desa ini dicabut dan dinyatakan tidak berlaku.",
                "Peraturan Desa ini mulai berlaku pada tanggal diundangkan.");

            kolom.Item().PaddingTop(NaskahDesa.JarakParagraf).Text(
                "Agar setiap orang mengetahuinya, memerintahkan pengundangan Peraturan Desa ini "
                + $"dengan penempatannya dalam Lembaran Desa {namaDesa}.")
                .FontSize(NaskahDesa.UkuranTeks);

            NaskahDesa.BlokPenetap(kolom, _desa, _data.Tanggal, _data.Tempat);
            NaskahDesa.BlokPengundang(kolom, _desa, _data.Tempat);

            kolom.Item().PageBreak();
            NaskahDesa.JudulTengah(kolom, "LAMPIRAN", NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, $"PERATURAN DESA {namaDesa}".ToUpperInvariant(), NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, $"NOMOR {NomorTampil()} TAHUN {tahun}", NaskahDesa.UkuranTeks);
            NaskahDesa.JudulTengah(kolom, "SUSUNAN ORGANISASI DAN TATA KERJA PEMERINTAH DESA", NaskahDesa.UkuranTeks);
            kolom.Item().PaddingTop(NaskahDesa.JarakBagian).Table(table => ComposeLampiran(table));
            if (_data.NamaBpd.Count > 0)
            {
                kolom.Item().PaddingTop(NaskahDesa.JarakBagian)
                    .Text("DATA ANGGOTA BPD (INFORMASI TERPISAH DARI SUSUNAN PEMERINTAH DESA)")
                    .FontSize(NaskahDesa.UkuranTeks).Bold();
                foreach (string nama in _data.NamaBpd)
                    kolom.Item().Text(nama).FontSize(NaskahDesa.UkuranTeks);
            }
        }

        private void ComposeLampiran(TableDescriptor table)
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
                columns.RelativeColumn(1.5f);
            });
            table.Header(header =>
            {
                foreach (string title in new[] { "NO", "JABATAN", "NAMA", "WILAYAH" })
                    header.Cell().BorderBottom(1).PaddingVertical(3).AlignCenter().Text(title).Bold();
            });
            int number = 0;
            foreach (var person in _data.SusunanPerangkat)
            {
                number++;
                table.Cell().BorderBottom(0.5f).PaddingVertical(3).AlignCenter().Text(number.ToString(CultureInfo.InvariantCulture));
                table.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(person.Jabatan);
                table.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(string.IsNullOrWhiteSpace(person.Nama) ? "-" : person.Nama);
                table.Cell().BorderBottom(0.5f).PaddingVertical(3).Text(string.IsNullOrWhiteSpace(person.Wilayah) ? "-" : person.Wilayah);
            }
        }
        private string NomorTampil() =>
            string.IsNullOrWhiteSpace(_data.Nomor) ? "............" : _data.Nomor.Trim();

        private static string Isi(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "............" : nilai.Trim();

}
}
