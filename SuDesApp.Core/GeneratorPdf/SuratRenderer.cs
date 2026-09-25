// SuratRenderer.cs
// Komposisi surat memakai API QuestPDF LANGSUNG (tanpa lapisan kompat iText).
// Isi kop diambil dari KopSurat supaya seluruh dokumen — surat, daftar hadir,
// rekening koran — memakai kop yang sama persis.
using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>Perataan teks yang dipakai badan surat.</summary>
    public enum Rata
    {
        Kiri,
        Tengah,
        Kanan,
        Justify
    }

    /// <summary>
    /// Potongan-potongan badan surat yang siap dirender QuestPDF. Generator mengisi
    /// daftar ini lewat <see cref="Paragraf"/>, <see cref="TabelFormulir"/>, atau
    /// <see cref="Blok"/>; setiap potongan menjadi satu item tersendiri sehingga
    /// pemenggalan halaman tetap sama seperti sebelumnya.
    /// </summary>
    public sealed class BadanSurat
    {
        private readonly List<Action<IContainer>> _blok = new();

        /// <summary>Potongan siap render, urut sesuai pemanggilan.</summary>
        public IReadOnlyList<Action<IContainer>> Potongan => _blok;

        /// <summary>Tambahkan potongan bebas (tabel, daftar, gambar) dengan API QuestPDF.</summary>
        public void Blok(Action<IContainer> render)
        {
            if (render != null)
            {
                _blok.Add(render);
            }
        }

        /// <summary>Tambahkan satu paragraf teks.</summary>
        public void Paragraf(
            string teks,
            float ukuran = SuratRenderer.UkuranTeks,
            bool tebal = false,
            bool miring = false,
            Rata rata = Rata.Kiri,
            float indentBarisPertama = 0f,
            float indentKiri = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f,
            float? leading = null,
            string? warna = null)
        {
            _blok.Add(container =>
            {
                var area = container;
                if (jarakAtas > 0f) area = area.PaddingTop(SuratRenderer.JarakBlok(jarakAtas));
                if (jarakBawah > 0f) area = area.PaddingBottom(SuratRenderer.JarakBlok(jarakBawah));
                if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

                SuratRenderer.Teks(area, teks, ukuran, tebal, miring, rata, indentBarisPertama, leading, warna);
            });
        }

        /// <summary>Paragraf dengan campuran tebal/tidak tebal dalam satu baris.</summary>
        public void ParagrafCampur(
            IEnumerable<(string Teks, bool Tebal)> potongan,
            float ukuran = SuratRenderer.UkuranTeks,
            Rata rata = Rata.Kiri,
            float indentBarisPertama = 0f,
            float indentKiri = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f)
        {
            var segmen = potongan == null ? new List<(string, bool)>() : new List<(string, bool)>(potongan);
            if (segmen.Count == 0) return;

            _blok.Add(container =>
            {
                var area = container;
                if (jarakAtas > 0f) area = area.PaddingTop(SuratRenderer.JarakBlok(jarakAtas));
                if (jarakBawah > 0f) area = area.PaddingBottom(SuratRenderer.JarakBlok(jarakBawah));
                if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

                SuratRenderer.TeksCampur(area, segmen, ukuran, rata, indentBarisPertama);
            });
        }

        /// <summary>Ruang kosong setinggi <paramref name="tinggi"/> point.</summary>
        public void Spasi(float tinggi)
        {
            if (tinggi <= 0f) return;
            _blok.Add(container => container.Height(SuratRenderer.JarakBlok(tinggi)));
        }

        /// <summary>Daftar bernomor (1., 2., …) dengan nomor di kolom tersendiri.</summary>
        public void DaftarBernomor(
            IEnumerable<string> butir,
            float indentKiri = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f,
            float lebarNomor = 20f,
            float spasiAntarButir = 3f)
        {
            var daftar = butir == null ? new List<string>() : new List<string>(butir);
            if (daftar.Count == 0) return;

            _blok.Add(container =>
            {
                var area = container;
                if (jarakAtas > 0f) area = area.PaddingTop(SuratRenderer.JarakBlok(jarakAtas));
                if (jarakBawah > 0f) area = area.PaddingBottom(SuratRenderer.JarakBlok(jarakBawah));
                if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

                area.Column(kolom =>
                {
                    kolom.Spacing(SuratRenderer.JarakBlok(spasiAntarButir));
                    for (int i = 0; i < daftar.Count; i++)
                    {
                        string nomor = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                        string teks = daftar[i];
                        kolom.Item().Row(baris =>
                        {
                            baris.ConstantItem(lebarNomor).Text(nomor).FontSize(SuratRenderer.UkuranTeks);
                            baris.RelativeItem().Text(teks).FontSize(SuratRenderer.UkuranTeks);
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Tabel berlabel ala surat desa: kolom label, titik dua, lalu nilai.
        /// Nilai baris "Nama" dicetak tebal seperti template resmi.
        /// </summary>
        public void TabelFormulir(
            IEnumerable<(string Label, string? Nilai)> baris,
            float jarakAtas = 3f,
            float jarakBawah = 3f,
            bool tebalkanNama = true,
            float indentKiri = 0f,
            IReadOnlyCollection<string>? labelTebal = null)
        {
            if (baris == null) return;

            var daftar = new List<(string Label, string? Nilai)>(baris);
            _blok.Add(container => SuratRenderer.TabelFormulir(container, daftar, jarakAtas, jarakBawah, tebalkanNama, indentKiri, labelTebal));
        }
    }

    /// <summary>Komposisi bersama seluruh surat: kop, judul + nomor, dan blok tanda tangan.</summary>
    public static class SuratRenderer
    {
        public const float UkuranTeks = 12f;
        public const float UkuranJudul = 14f;
        public const float JarakBarisSel = 15f;

        /// <summary>Tinggi satu baris teks 12pt pada kerapatan normal.</summary>
        public const float TinggiBaris = UkuranTeks * 1.2f;

        /// <summary>
        /// Ruang kosong untuk tanda tangan di atas nama penandatangan. Ikut menyesuaikan
        /// kerapatan supaya blok tanda tangan tetap muat di halaman yang sama.
        /// </summary>
        public static float RuangTandaTangan => TinggiBaris * KerapatanSurat.Aktif.BarisRuangTandaTangan;

        /// <summary>Jarak baris teks paragraf sesuai kerapatan yang berlaku.</summary>
        private static float LeadingParagraf => KerapatanSurat.Aktif.FaktorLeadingTeks;

        /// <summary>Jarak antar blok/paragraf sesudah dikalikan kerapatan berlaku.</summary>
        public static float JarakBlok(float jarak) => jarak * KerapatanSurat.Aktif.FaktorJarakBlok;

        /// <summary>Kop surat: logo + lima baris dari KopSurat, ditutup garis ganda.</summary>
        public static void Kop(IContainer container, DesaData desa, string logoPath)
        {
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(KopSurat.LebarLogo).Element(logo =>
                    {
                        if (!string.IsNullOrWhiteSpace(logoPath))
                        {
                            logo.Image(logoPath).FitArea();
                        }
                    });

                    row.RelativeItem().Column(col =>
                    {
                        // Jarak baris kop sengaja mengikuti bawaan QuestPDF: ukurannya
                        // sudah pas dan tidak ikut dirapatkan agar kop tetap sama
                        // di semua ukuran kertas.
                        foreach (var baris in KopSurat.BarisKop(desa))
                        {
                            col.Item().AlignCenter().Text(teks =>
                            {
                                var utama = teks.Span(baris.Teks).FontSize(baris.FontSize);
                                if (baris.Tebal)
                                {
                                    utama.Bold();
                                }

                                // Surel desa (opsional) dicetak biru sebagai baris tersendiri
                                // di bawah nama kabupaten (bukan sambungan baris alamat).
                                if (baris.AdaSurel)
                                {
                                    var pemisah = baris.Teks.Length == 0 ? string.Empty : " ";
                                    teks.Span(pemisah + baris.Surel)
                                        .FontSize(baris.FontSize)
                                        .FontColor(KopSurat.WarnaSurel);
                                }
                            });
                        }
                    });
                });

                column.Item().PaddingTop(3).Height(KopSurat.GarisTipis).Background(QuestPDF.Helpers.Colors.Black);
                column.Item().PaddingTop(1).PaddingBottom(5).Height(KopSurat.GarisTebal).Background(QuestPDF.Helpers.Colors.Black);
            });
        }

        /// <summary>Judul surat (bergaris bawah, kapital) dan baris NOMOR di bawahnya.</summary>
        public static void JudulDanNomor(
            IContainer container,
            string judul,
            string nomor,
            float? leadingJudul = null,
            float? leadingNomor = null,
            float jarakBawahNomor = 15f)
        {
            container.Column(column =>
            {
                column.Item().AlignCenter()
                    .Text((judul ?? string.Empty).ToUpperInvariant())
                    .FontSize(UkuranJudul)
                    .LineHeight((leadingJudul ?? JarakBarisSel) / UkuranJudul)
                    .Bold()
                    .Underline();

                column.Item().PaddingBottom(JarakBlok(jarakBawahNomor)).AlignCenter()
                    .Text($"NOMOR : {nomor}")
                    .FontSize(UkuranTeks)
                    .LineHeight((leadingNomor ?? JarakBarisSel) / UkuranTeks);
            });
        }

        /// <summary>Judul halaman (rata tengah, kapital, bergaris bawah) tanpa baris NOMOR.</summary>
        public static void JudulTengah(IContainer container, string judul, float ukuran = UkuranJudul)
        {
            container.AlignCenter()
                .Text((judul ?? string.Empty).ToUpperInvariant())
                .FontSize(ukuran)
                .LineHeight(JarakBarisSel / ukuran)
                .Bold()
                .Underline();
        }

        /// <summary>Satu paragraf teks dengan gaya surat desa.</summary>
        public static void Teks(
            IContainer container,
            string teks,
            float ukuran = UkuranTeks,
            bool tebal = false,
            bool miring = false,
            Rata rata = Rata.Kiri,
            float indentBarisPertama = 0f,
            float? leading = null,
            string? warna = null)
        {
            // Perataan dan indentasi baris pertama adalah sifat PARAGRAF, jadi harus
            // lewat TextDescriptor — bukan pada span-nya.
            container.Text(paragraf =>
            {
                switch (rata)
                {
                    case Rata.Tengah: paragraf.AlignCenter(); break;
                    case Rata.Kanan: paragraf.AlignRight(); break;
                    case Rata.Justify: paragraf.Justify(); break;
                    default: paragraf.AlignLeft(); break;
                }

                if (indentBarisPertama > 0f)
                {
                    paragraf.ParagraphFirstLineIndentation(indentBarisPertama);
                }

                var span = paragraf.Span(teks ?? string.Empty)
                    .FontSize(ukuran)
                    .LineHeight((leading ?? ukuran * LeadingParagraf) / ukuran);

                if (warna != null) span.FontColor(warna);
                if (tebal) span.Bold();
                if (miring) span.Italic();
            });
        }

        /// <summary>Paragraf berisi beberapa potongan teks dengan ketebalan berbeda.</summary>
        public static void TeksCampur(
            IContainer container,
            IReadOnlyList<(string Teks, bool Tebal)> potongan,
            float ukuran = UkuranTeks,
            Rata rata = Rata.Kiri,
            float indentBarisPertama = 0f)
        {
            container.Text(paragraf =>
            {
                switch (rata)
                {
                    case Rata.Tengah: paragraf.AlignCenter(); break;
                    case Rata.Kanan: paragraf.AlignRight(); break;
                    case Rata.Justify: paragraf.Justify(); break;
                    default: paragraf.AlignLeft(); break;
                }

                if (indentBarisPertama > 0f)
                {
                    paragraf.ParagraphFirstLineIndentation(indentBarisPertama);
                }

                foreach (var (teks, tebal) in potongan)
                {
                    var span = paragraf.Span(teks ?? string.Empty)
                        .FontSize(ukuran)
                        .LineHeight(LeadingParagraf);

                    if (tebal)
                    {
                        span.Bold();
                    }
                }
            });
        }

        /// <summary>Tabel berlabel: label, titik dua, nilai (tanpa garis).</summary>
        public static void TabelFormulir(
            IContainer container,
            IReadOnlyList<(string Label, string? Nilai)> baris,
            float jarakAtas = 3f,
            float jarakBawah = 3f,
            bool tebalkanNama = true,
            float indentKiri = 0f,
            IReadOnlyCollection<string>? labelTebal = null)
        {
            var area = container;
            if (jarakAtas > 0f) area = area.PaddingTop(JarakBlok(jarakAtas));
            if (jarakBawah > 0f) area = area.PaddingBottom(JarakBlok(jarakBawah));
            if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

            float leadingSel = KerapatanSurat.Aktif.LeadingSelTabel / UkuranTeks;
            float paddingSel = KerapatanSurat.Aktif.PaddingSelTabel;

            area.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(160f);
                    columns.RelativeColumn(10f);
                    columns.RelativeColumn(340f);
                });

                foreach (var (label, nilai) in baris)
                {
                    SelTabel(table, label ?? string.Empty, paddingSel).FontSize(UkuranTeks).LineHeight(leadingSel);
                    SelTabel(table, ":", paddingSel).FontSize(UkuranTeks).LineHeight(leadingSel);

                    // Baris "Nama" (dan label yang diminta khusus) mengikuti template resmi:
                    // isinya KAPITAL dan dicetak tebal.
                    bool namaOrang = tebalkanNama && string.Equals(label, "Nama", StringComparison.OrdinalIgnoreCase);
                    bool barisNama = namaOrang
                        || (labelTebal != null && labelTebal.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)));

                    // Nama orang: hanya namanya yang KAPITAL, gelar dibiarkan apa adanya.
                    // Label lain (mis. nama instansi) tetap dikapitalkan seluruhnya.
                    string isiTeks = barisNama
                        ? (namaOrang ? NamaFormatter.ToUpperNama(nilai ?? string.Empty) : (nilai ?? string.Empty).ToUpperInvariant())
                        : (nilai ?? "....................");

                    var isi = SelTabel(table, isiTeks, paddingSel)
                        .FontSize(UkuranTeks)
                        .LineHeight(leadingSel);

                    if (barisNama)
                    {
                        isi.Bold();
                    }
                }
            });
        }

        /// <summary>Blok tanda tangan: pemohon (opsional) di kiri, Kepala Desa di kanan.</summary>
        public static void TandaTangan(
            IContainer container,
            bool tampilkanPemohon,
            string namaPemohon,
            string namaDesa,
            string tanggalTerformat,
            string jabatan,
            string namaPejabat,
            string labelPemohon = "Pemohon :")
        {
            container.PaddingTop(JarakBlok(10)).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Cell().Element(cell =>
                {
                    if (!tampilkanPemohon || string.IsNullOrWhiteSpace(namaPemohon))
                    {
                        return;
                    }

                    cell.Column(kolom =>
                    {
                        kolom.Item().Element(item => Teks(item, labelPemohon, rata: Rata.Tengah));

                        // Ruang tanda tangan + satu baris penyeimbang, supaya nama pemohon
                        // duduk SEJAJAR dengan nama Kepala Desa di kolom kanan.
                        kolom.Item().Height(RuangTandaTangan + TinggiBaris);

                        kolom.Item().Element(item => Teks(item, NamaFormatter.ToUpperNama(namaPemohon), tebal: true, rata: Rata.Tengah));
                    });
                });

                table.Cell().Element(cell =>
                {
                    cell.Column(kolom =>
                    {
                        kolom.Item().Element(item => Teks(item, $"{namaDesa}, {tanggalTerformat}", rata: Rata.Tengah));
                        kolom.Item().Element(item => Teks(item, jabatan, rata: Rata.Tengah));
                        kolom.Item().Height(RuangTandaTangan);
                        kolom.Item().Element(item => Teks(item, namaPejabat, tebal: true, rata: Rata.Tengah));
                    });
                });
            });
        }

        private static TextSpanDescriptor SelTabel(TableDescriptor table, string teks, float padding)
            => table.Cell().PaddingTop(padding).PaddingBottom(padding).Text(teks);
    }
}
