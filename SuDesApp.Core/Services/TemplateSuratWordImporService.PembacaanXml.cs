// TemplateSuratWordImporService.cs
// Membaca berkas Word (.docx) lalu menyusunnya menjadi satu atau beberapa
// Template Surat (menu Template Surat).
//
// Berkas dibaca tanpa library tambahan: .docx adalah arsip ZIP berisi
// word/document.xml, sehingga teks, perataan, tebal/miring, dan batas halaman
// (page break / section break) dapat diambil langsung dari XML-nya.
//
// Berkas .doc (format Word 97-2003) dikonversi otomatis lebih dulu menjadi .docx
// memakai Microsoft Word yang terpasang (WordDocConverter), sehingga pengguna tidak
// perlu menyimpan ulang berkasnya secara manual.
//
// Satu halaman/bagian pada berkas dianggap satu bentuk surat. Berkas yang berisi
// beberapa blanko sekaligus tetap bisa disimpan sebagai beberapa template, dan
// bagian yang salah terpisah dapat diabaikan pengguna lewat dialog pemilihan.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class TemplateSuratWordImporService : ITemplateSuratWordImpor
    {
        // =====================================================================
        // Pembacaan XML → paragraf
        // =====================================================================

        /// <summary>Paragraf mentah sebelum dibagi menjadi bagian surat.</summary>
        private sealed class ParagrafMentah
        {
            public List<PotonganParagraf> Potongan { get; } = new();
            public RataBlokTemplate Rata { get; set; } = RataBlokTemplate.Kiri;
            public bool Tebal { get; set; }
            public bool Miring { get; set; }

            /// <summary>Baris pemisah (---, ===, ***) yang memisahkan dua surat.</summary>
            public bool Pemisah { get; set; }

            /// <summary>Paragraf diakhiri batas halaman tanpa teks sesudahnya.</summary>
            public bool BatasKerasSetelah { get; set; }
            public bool BatasLunakSetelah { get; set; }
        }

        /// <summary>Sepotong teks dalam satu paragraf (paragraf bisa terbelah batas halaman).</summary>
        private sealed class PotonganParagraf
        {
            public string Teks { get; set; } = string.Empty;
            public bool BatasKeras { get; set; }
            public bool BatasLunak { get; set; }
        }

        private static List<ParagrafMentah> BacaParagraf(XElement body)
        {
            var hasil = new List<ParagrafMentah>();

            foreach (var anak in body.Elements())
            {
                if (anak.Name == W + "p")
                {
                    var paragraf = BacaSatuParagraf(anak);
                    if (paragraf != null) hasil.Add(paragraf);
                }
                else if (anak.Name == W + "tbl")
                {
                    foreach (var baris in anak.Elements(W + "tr"))
                    {
                        var sel = baris.Elements(W + "tc")
                            .Select(kotak => TeksPolos(kotak))
                            .Where(teks => teks.Length > 0)
                            .ToList();
                        if (sel.Count == 0) continue;

                        var paragraf = new ParagrafMentah();
                        paragraf.Potongan.Add(new PotonganParagraf { Teks = string.Join("  ", sel) });
                        hasil.Add(paragraf);
                    }
                }
            }

            return hasil;
        }

        /// <summary>Seluruh teks di dalam sebuah elemen (dipakai untuk sel tabel).</summary>
        private static string TeksPolos(XElement elemen)
        {
            var sb = new StringBuilder();
            foreach (var teks in elemen.Descendants(W + "t"))
            {
                sb.Append(teks.Value);
            }
            return RapikanTeks(sb.ToString());
        }

        private static ParagrafMentah? BacaSatuParagraf(XElement p)
        {
            var paragraf = new ParagrafMentah { Rata = BacaRata(p.Element(W + "pPr")) };

            var sb = new StringBuilder();
            int hurufTebal = 0, hurufMiring = 0, hurufTotal = 0;
            bool batasKerasTertunda = false, batasLunakTertunda = false;

            // Batas yang tertunda hanya "terpakai" bila ada teks sesudahnya; bila tidak,
            // batas itu menjadi pemisah antar bagian surat.
            void SiramPotongan()
            {
                string teks = RapikanTeks(sb.ToString());
                sb.Clear();
                if (teks.Length == 0) return;

                paragraf.Potongan.Add(new PotonganParagraf
                {
                    Teks = teks,
                    BatasKeras = batasKerasTertunda,
                    BatasLunak = batasLunakTertunda
                });
                batasKerasTertunda = false;
                batasLunakTertunda = false;
            }

            foreach (var run in p.Descendants(W + "r"))
            {
                var rPr = run.Element(W + "rPr");
                bool tebal = BacaAktif(rPr?.Element(W + "b"));
                bool miring = BacaAktif(rPr?.Element(W + "i"));

                foreach (var simpul in run.Elements())
                {
                    if (simpul.Name == W + "t")
                    {
                        sb.Append(simpul.Value);
                    }
                    else if (simpul.Name == W + "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (simpul.Name == W + "br")
                    {
                        string jenis = simpul.Attribute(W + "type")?.Value ?? string.Empty;
                        if (string.Equals(jenis, "page", StringComparison.OrdinalIgnoreCase))
                        {
                            SiramPotongan();
                            batasKerasTertunda = true;
                        }
                        else
                        {
                            sb.Append('\n');
                        }
                    }
                    else if (simpul.Name == W + "lastRenderedPageBreak")
                    {
                        SiramPotongan();
                        batasLunakTertunda = true;
                    }
                }

                // Hitung proporsi teks tebal/miring untuk menentukan format paragraf.
                int hurufRun = run.Elements(W + "t").Sum(t => t.Value.Count(char.IsLetter));
                hurufTotal += hurufRun;
                if (tebal) hurufTebal += hurufRun;
                if (miring) hurufMiring += hurufRun;
            }

            SiramPotongan();
            paragraf.BatasKerasSetelah = batasKerasTertunda;
            paragraf.BatasLunakSetelah = batasLunakTertunda;

            if (paragraf.Potongan.Count == 0)
            {
                // Paragraf kosong atau hanya berisi batas halaman: tetap dipakai bila
                // membawa batas, supaya bagian surat terpisah dengan benar.
                return (paragraf.BatasKerasSetelah || paragraf.BatasLunakSetelah) ? paragraf : null;
            }

            paragraf.Tebal = hurufTotal > 0 && hurufTebal * 100 >= hurufTotal * 60;
            paragraf.Miring = hurufTotal > 0 && hurufMiring * 100 >= hurufTotal * 60;

            if (paragraf.Potongan.Count == 1 && IsPemisah(paragraf.Potongan[0].Teks))
            {
                paragraf.Pemisah = true;
            }

            return paragraf;
        }

        private static RataBlokTemplate BacaRata(XElement? pPr)
        {
            string nilai = pPr?.Element(W + "jc")?.Attribute(W + "val")?.Value ?? string.Empty;
            return nilai.ToLowerInvariant() switch
            {
                "center" => RataBlokTemplate.Tengah,
                "right" => RataBlokTemplate.Kanan,
                "end" => RataBlokTemplate.Kanan,
                "both" => RataBlokTemplate.Justify,
                "distribute" => RataBlokTemplate.Justify,
                _ => RataBlokTemplate.Kiri
            };
        }

        /// <summary>Format Word \"aktif\": elemen ada dan tidak dimatikan (val=\"0\"/\"false\").</summary>
        private static bool BacaAktif(XElement? elemen)
        {
            if (elemen == null) return false;
            string nilai = elemen.Attribute(W + "val")?.Value ?? string.Empty;
            return !(nilai == "0" || nilai.Equals("false", StringComparison.OrdinalIgnoreCase)
                     || nilai.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Rapikan spasi berlebih tanpa menghilangkan tab dan baris baru.</summary>
        private static string RapikanTeks(string teks)
        {
            if (string.IsNullOrEmpty(teks)) return string.Empty;

            var sb = new StringBuilder(teks.Length);
            bool spasiSebelumnya = false;
            foreach (char c in teks)
            {
                if (c == '\t')
                {
                    sb.Append(c);
                    spasiSebelumnya = false;
                }
                else if (c == ' ' || c == '\u00A0')
                {
                    if (spasiSebelumnya) continue;
                    spasiSebelumnya = true;
                    sb.Append(' ');
                }
                else
                {
                    spasiSebelumnya = false;
                    sb.Append(c);
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>Baris pemisah antar surat: ---, ===, ***, ___ (tanpa huruf/angka).</summary>
        private static bool IsPemisah(string teks)
        {
            string bersih = teks.Trim();
            if (bersih.Length < 3 || bersih.Length > 40) return false;
            if (bersih.Any(char.IsLetterOrDigit)) return false;

            int tanda = bersih.Count(c => c == '-' || c == '=' || c == '_' || c == '*' || c == '~' || c == '•');
            return tanda >= 3 && tanda == bersih.Length;
        }


    }
}
