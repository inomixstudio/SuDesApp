// SuratRenderer.cs
// Primitive tata letak surat — teks, paragraf, tabel, judul, dan blok tanda
// tangan — yang dipakai generator surat. Susunan kop dimiliki KopSurat.
//
// Kelas ini TIDAK menyimpan tujuan surat: BadanSurat memiliki urutan blok,
// dan masing-masing generator mengisi isinya.
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Komposisi bersama seluruh surat: kop, judul + nomor, tabel/daftar, dan blok
    /// tanda tangan. Seluruh ukuran yang ikut menyesuaikan kerapatan surat diambil
    /// dari <see cref="KerapatanSurat.Aktif"/>.
    /// </summary>
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
            string? teks,
            float ukuran = UkuranTeks,
            bool tebal = false,
            bool miring = false,
            Rata rata = Rata.Justify,
            float indentBarisPertama = 0f,
            string? warna = null)
        {
            // Perataan dan indentasi baris pertama adalah sifat PARAGRAF, jadi harus
            // lewat TextDescriptor — bukan pada span-nya.
            container.Text(paragraf =>
            {
                RataTeks(paragraf, rata);

                if (indentBarisPertama > 0f)
                {
                    paragraf.ParagraphFirstLineIndentation(indentBarisPertama);
                }

                var span = paragraf.Span(teks ?? string.Empty)
                    .FontSize(ukuran)
                    .LineHeight(LeadingParagraf);

                if (warna != null) span.FontColor(warna);
                if (tebal) span.Bold();
                if (miring) span.Italic();
            });
        }

        /// <summary>Paragraf dengan jarak atas/bawah dan indentasi sesuai gaya surat.</summary>
        public static void Paragraf(
            IContainer container,
            string? teks,
            Rata rata = Rata.Justify,
            bool tebal = false,
            bool miring = false,
            float ukuran = UkuranTeks,
            float indentKiri = 0f,
            float indentBarisPertama = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f,
            string? warna = null)
        {
            var target = container;
            if (jarakAtas > 0f) target = target.PaddingTop(JarakBlok(jarakAtas));
            if (jarakBawah > 0f) target = target.PaddingBottom(JarakBlok(jarakBawah));
            if (indentKiri > 0f) target = target.PaddingLeft(indentKiri);

            Teks(target, teks, ukuran, tebal, miring, rata, indentBarisPertama, warna);
        }

        /// <summary>Paragraf berisi beberapa potongan teks dengan ketebalan berbeda.</summary>
        public static void ParagrafCampur(
            IContainer container,
            IReadOnlyList<(string Teks, bool Tebal)> bagian,
            Rata rata = Rata.Justify,
            float ukuran = UkuranTeks,
            float indentKiri = 0f,
            float indentBarisPertama = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f)
        {
            var target = container;
            if (jarakAtas > 0f) target = target.PaddingTop(JarakBlok(jarakAtas));
            if (jarakBawah > 0f) target = target.PaddingBottom(JarakBlok(jarakBawah));
            if (indentKiri > 0f) target = target.PaddingLeft(indentKiri);

            target.Text(paragraf =>
            {
                RataTeks(paragraf, rata);

                if (indentBarisPertama > 0f)
                {
                    paragraf.ParagraphFirstLineIndentation(indentBarisPertama);
                }

                foreach (var (teks, tebal) in bagian)
                {
                    var span = paragraf.Span(teks ?? string.Empty)
                        .FontSize(ukuran)
                        .LineHeight(LeadingParagraf);

                    if (tebal) span.Bold();
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
                    //
                    // Pada pratinjau form (PratinjauIsian), kolom kosong dicetak
                    // sebagai titik-titik — termasuk baris "Nama" yang pada
                    // pencetakan biasa sengaja dibiarkan kosong. Tanpa ini kerangka
                    // surat terlihat seperti tabel tanpa isi.
                    string isiTeks;
                    if (PratinjauIsian.Aktif && string.IsNullOrWhiteSpace(nilai))
                    {
                        isiTeks = PratinjauIsian.TitikKosong;
                    }
                    else
                    {
                        isiTeks = barisNama
                            ? (namaOrang ? NamaFormatter.ToUpperNama(nilai ?? string.Empty) : (nilai ?? string.Empty).ToUpperInvariant())
                            : (nilai ?? "....................");
                    }

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

        /// <summary>Daftar bernomor (1., 2., …) dengan nomor di kolom tersendiri.</summary>
        public static void DaftarBernomor(
            IContainer container,
            IReadOnlyList<string> butir,
            float indentKiri = 0f,
            float jarakAtas = 0f,
            float jarakBawah = 0f,
            float lebarNomor = 20f,
            float spasiAntarButir = 3f)
        {
            var area = container;
            if (jarakAtas > 0f) area = area.PaddingTop(JarakBlok(jarakAtas));
            if (jarakBawah > 0f) area = area.PaddingBottom(JarakBlok(jarakBawah));
            if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

            area.Column(kolom =>
            {
                kolom.Spacing(JarakBlok(spasiAntarButir));
                for (int i = 0; i < butir.Count; i++)
                {
                    string nomor = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                    string teks = butir[i];
                    kolom.Item().Row(baris =>
                    {
                        baris.ConstantItem(lebarNomor).Text(nomor).FontSize(UkuranTeks);
                        baris.RelativeItem().Text(teks ?? string.Empty)
                            .FontSize(UkuranTeks)
                            .LineHeight(LeadingParagraf);
                    });
                }
            });
        }

        /// <summary>
        /// Blok tanda tangan: pemohon (opsional) di kiri, Kepala Desa di kanan.
        ///
        /// Kolom kanan mengikuti bentuk Perbup 74/2020 lampiran: tempat/tanggal,
        /// jabatan, ruang tanda tangan, lalu nama pejabat.
        /// </summary>
        public static void TandaTangan(
            IContainer container,
            bool tampilkanPemohon,
            string? namaPemohon,
            string namaDesa,
            string tanggalTerformat,
            string jabatan,
            string namaPejabat,
            string labelPemohon = "Pemohon :",
            string? nipPejabat = null)
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
                        // Blok penanda tangan tetap berada di kolom sisi kanan,
                        // sedangkan isi blok rata tengah sesuai format tanda tangan.
                        kolom.Item().Element(item => Teks(item, $"{namaDesa}, {tanggalTerformat}", rata: Rata.Tengah));
                        kolom.Item().Element(item => Teks(item, jabatan, rata: Rata.Tengah));
                        kolom.Item().Height(RuangTandaTangan);

                        // Nama pejabat langsung dicetak setelah ruang tanda tangan.
                        kolom.Item().Element(item => Teks(
                            item,
                            string.IsNullOrWhiteSpace(namaPejabat) ? "NAMA" : namaPejabat,
                            tebal: true, rata: Rata.Tengah));

                        // NIP pejabat ASN (Lurah) dicetak di bawah nama — kolom NIP
                        // Pengaturan Surat. Kosong (Kepala Desa non-ASN): tidak ada baris.
                        if (!string.IsNullOrWhiteSpace(nipPejabat))
                        {
                            kolom.Item().Element(item => Teks(item, $"NIP {nipPejabat.Trim()}", rata: Rata.Tengah));
                        }
                    });
                });
            });
        }

        private static void RataTeks(TextDescriptor paragraf, Rata rata)
        {
            switch (rata)
            {
                case Rata.Tengah: paragraf.AlignCenter(); break;
                case Rata.Kanan: paragraf.AlignRight(); break;
                case Rata.Justify: paragraf.Justify(); break;
                default: paragraf.AlignLeft(); break;
            }
        }

        private static TextSpanDescriptor SelTabel(TableDescriptor table, string teks, float padding)
            => table.Cell().PaddingTop(padding).PaddingBottom(padding).Text(teks);
    }
}
