using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SudesExPlus.Style;

namespace SudesExPlus;

internal static partial class XlsxReader
    {
    // =====================================================================
    // styles.xml → gaya per sel
    // =====================================================================

    /// <summary>
    /// Gaya per indeks sel (xf) hasil pembacaan styles.xml: format angka, font,
    /// isian, garis, dan perataan. Warna tema (theme) dan palet indeks lama
    /// diterjemahkan menjadi RGB supaya bisa ditulis kembali sebagai RGB.
    /// </summary>
    private sealed class GayaBerkas
    {
        private readonly List<GayaSelBerkas> _xf;

        private GayaBerkas(List<GayaSelBerkas> xf) => _xf = xf;

        public static GayaBerkas Kosong { get; } = new(new List<GayaSelBerkas> { new() });

        public GayaSelBerkas Urai(int indeks) =>
            indeks >= 0 && indeks < _xf.Count ? _xf[indeks] : _xf[0];

        public static GayaBerkas Baca(ZipArchive arsip)
        {
            var entri = CariEntri(arsip, "xl/styles.xml");
            if (entri == null) return Kosong;

            XDocument dokumen = BacaXDocument(entri);
            var warna = SkemaWarna.Baca(arsip);

            var khusus = new Dictionary<int, string>();
            foreach (var numFmt in dokumen.Descendants(XName.Get("numFmt", NsUtama)))
            {
                int? id = (int?)numFmt.Attribute("numFmtId");
                string? kode = (string?)numFmt.Attribute("formatCode");
                if (id.HasValue && kode != null) khusus[id.Value] = kode;
            }

            var font = BacaFont(dokumen, warna);
            var isian = BacaIsian(dokumen, warna);
            var garis = BacaGaris(dokumen);

            var hasil = new List<GayaSelBerkas>();
            var cellXfs = dokumen.Descendants(XName.Get("cellXfs", NsUtama)).FirstOrDefault();

            if (cellXfs != null)
            {
                foreach (var xf in cellXfs.Elements(XName.Get("xf", NsUtama)))
                {
                    int numFmtId = (int?)xf.Attribute("numFmtId") ?? 0;
                    int fontId = (int?)xf.Attribute("fontId") ?? 0;
                    int fillId = (int?)xf.Attribute("fillId") ?? 0;
                    int borderId = (int?)xf.Attribute("borderId") ?? 0;

                    var alignment = xf.Element(XName.Get("alignment", NsUtama));

                    hasil.Add(new GayaSelBerkas
                    {
                        Format = khusus.TryGetValue(numFmtId, out string? kode)
                            ? kode
                            : FormatBawaan(numFmtId),
                        Font = font.ElementAtOrDefault(fontId),
                        Isian = isian.ElementAtOrDefault(fillId),
                        Garis = garis.ElementAtOrDefault(borderId),
                        RataMendatar = BacaRataMendatar((string?)alignment?.Attribute("horizontal")),
                        RataTegak = BacaRataTegak((string?)alignment?.Attribute("vertical")),
                        BungkusTeks = string.Equals((string?)alignment?.Attribute("wrapText"), "1", StringComparison.Ordinal),
                    });
                }
            }

            if (hasil.Count == 0) hasil.Add(new GayaSelBerkas());
            return new GayaBerkas(hasil);
        }

        private static List<FontBerkas?> BacaFont(XDocument dokumen, SkemaWarna warna)
        {
            var hasil = new List<FontBerkas?>();
            var fonts = dokumen.Descendants(XName.Get("fonts", NsUtama)).FirstOrDefault();
            if (fonts == null) return hasil;

            foreach (var font in fonts.Elements(XName.Get("font", NsUtama)))
            {
                var underline = font.Element(XName.Get("u", NsUtama));
                string? jenisGarisBawah = (string?)underline?.Attribute("val");

                hasil.Add(new FontBerkas
                {
                    Bold = font.Element(XName.Get("b", NsUtama)) is { } b && !string.Equals((string?)b.Attribute("val"), "0", StringComparison.Ordinal),
                    Italic = font.Element(XName.Get("i", NsUtama)) is { } i && !string.Equals((string?)i.Attribute("val"), "0", StringComparison.Ordinal),
                    GarisBawah = underline != null && !string.Equals(jenisGarisBawah, "none", StringComparison.Ordinal),
                    Nama = (string?)font.Element(XName.Get("name", NsUtama))?.Attribute("val"),
                    Ukuran = (double?)font.Element(XName.Get("sz", NsUtama))?.Attribute("val"),
                    Warna = warna.Baca(font.Element(XName.Get("color", NsUtama))),
                });
            }

            return hasil;
        }

        private static List<Color?> BacaIsian(XDocument dokumen, SkemaWarna warna)
        {
            var hasil = new List<Color?>();
            var fills = dokumen.Descendants(XName.Get("fills", NsUtama)).FirstOrDefault();
            if (fills == null) return hasil;

            foreach (var fill in fills.Elements(XName.Get("fill", NsUtama)))
            {
                var pola = fill.Element(XName.Get("patternFill", NsUtama));
                string? tipe = (string?)pola?.Attribute("patternType");

                // Hanya isian penuh yang bisa diwakili model gaya ini; pola
                // bergaris/ber titik diperlakukan sebagai tanpa isian.
                if (!string.Equals(tipe, "solid", StringComparison.Ordinal))
                {
                    hasil.Add(null);
                    continue;
                }

                hasil.Add(warna.Baca(pola!.Element(XName.Get("fgColor", NsUtama))));
            }

            return hasil;
        }

        private static List<GarisBerkas?> BacaGaris(XDocument dokumen)
        {
            var hasil = new List<GarisBerkas?>();
            var borders = dokumen.Descendants(XName.Get("borders", NsUtama)).FirstOrDefault();
            if (borders == null) return hasil;

            foreach (var border in borders.Elements(XName.Get("border", NsUtama)))
            {
                hasil.Add(new GarisBerkas
                {
                    Kiri = BacaGarisSisi(border.Element(XName.Get("left", NsUtama))),
                    Kanan = BacaGarisSisi(border.Element(XName.Get("right", NsUtama))),
                    Atas = BacaGarisSisi(border.Element(XName.Get("top", NsUtama))),
                    Bawah = BacaGarisSisi(border.Element(XName.Get("bottom", NsUtama))),
                });
            }

            return hasil;
        }

        private static ExcelBorderStyle BacaGarisSisi(XElement? sisi)
        {
            string? gaya = (string?)sisi?.Attribute("style");
            return gaya switch
            {
                "hair" => ExcelBorderStyle.Hair,
                "thin" => ExcelBorderStyle.Thin,
                "medium" or "mediumDashed" or "mediumDashDot" or "mediumDashDotDot" or "slantDashDot"
                    => ExcelBorderStyle.Medium,
                "thick" => ExcelBorderStyle.Thick,
                "double" => ExcelBorderStyle.Double,
                // Garis putus-putus tidak punya padanan; garis tipis menjaga
                // tabel tetap terbaca.
                "dashed" or "dotted" or "dashDot" or "dashDotDot" => ExcelBorderStyle.Thin,
                _ => ExcelBorderStyle.None,
            };
        }

        private static ExcelHorizontalAlignment? BacaRataMendatar(string? nilai) => nilai switch
        {
            "left" => ExcelHorizontalAlignment.Left,
            "center" or "centerContinuous" => ExcelHorizontalAlignment.Center,
            "right" => ExcelHorizontalAlignment.Right,
            _ => null,
        };

        private static ExcelVerticalAlignment? BacaRataTegak(string? nilai) => nilai switch
        {
            "top" => ExcelVerticalAlignment.Top,
            "center" => ExcelVerticalAlignment.Center,
            "bottom" => ExcelVerticalAlignment.Bottom,
            _ => null,
        };

        /// <summary>Kode format bawaan Excel yang punya arti untuk pembacaan sel.</summary>
        private static string? FormatBawaan(int numFmtId) => numFmtId switch
        {
            1 => "0",
            2 => "0.00",
            3 => "#,##0",
            4 => "#,##0.00",
            9 => "0%",
            10 => "0.00%",
            14 => "dd/mm/yyyy",
            15 => "d-mmm-yy",
            16 => "d-mmm",
            17 => "mmm-yy",
            18 => "h:mm AM/PM",
            19 => "h:mm:ss AM/PM",
            20 => "h:mm",
            21 => "h:mm:ss",
            22 => "dd/mm/yyyy h:mm",
            45 => "mm:ss",
            46 => "[h]:mm:ss",
            47 => "mm:ss.0",
            _ => null,
        };
    }

    /// <summary>Gaya satu xf hasil pembacaan.</summary>
    private sealed class GayaSelBerkas
    {
        public string? Format { get; init; }

        public FontBerkas? Font { get; init; }

        public Color? Isian { get; init; }

        public GarisBerkas? Garis { get; init; }

        public ExcelHorizontalAlignment? RataMendatar { get; init; }

        public ExcelVerticalAlignment? RataTegak { get; init; }

        public bool BungkusTeks { get; init; }

        /// <summary>True bila xf memuat gaya yang perlu ditulis balik.</summary>
        public bool AdaGaya =>
            !string.IsNullOrEmpty(Format) || Font != null || Isian.HasValue || Garis != null ||
            RataMendatar.HasValue || RataTegak.HasValue || BungkusTeks;

        public void Terapkan(GayaSel gaya)
        {
            if (!string.IsNullOrEmpty(Format)) gaya.FormatAngka = Format;

            if (Font is { } font)
            {
                gaya.Bold = font.Bold;
                gaya.Italic = font.Italic;
                gaya.GarisBawahHuruf = font.GarisBawah;
                if (!string.IsNullOrWhiteSpace(font.Nama)) gaya.NamaHuruf = font.Nama;
                if (font.Ukuran.HasValue) gaya.UkuranHuruf = font.Ukuran;
                if (font.Warna.HasValue) gaya.WarnaHuruf = font.Warna;
            }

            if (Isian.HasValue) gaya.WarnaIsian = Isian;
            if (Garis is { } garis)
            {
                gaya.GarisKiri = garis.Kiri;
                gaya.GarisKanan = garis.Kanan;
                gaya.GarisAtas = garis.Atas;
                gaya.GarisBawah = garis.Bawah;
            }

            if (RataMendatar.HasValue) gaya.RataMendatar = RataMendatar;
            if (RataTegak.HasValue) gaya.RataTegak = RataTegak;
            if (BungkusTeks) gaya.BungkusTeks = true;
        }
    }

    private sealed record FontBerkas
    {
        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool GarisBawah { get; init; }

        public string? Nama { get; init; }

        public double? Ukuran { get; init; }

        public Color? Warna { get; init; }
    }

    private sealed record GarisBerkas
    {
        public ExcelBorderStyle Kiri { get; init; }

        public ExcelBorderStyle Kanan { get; init; }

        public ExcelBorderStyle Atas { get; init; }

        public ExcelBorderStyle Bawah { get; init; }
    }

    /// <summary>
    /// Warna tema workbook (xl/theme/theme1.xml) dan palet indeks lama. Excel
    /// menulis warna tema+rona (tint) untuk hampir semua latar sel, jadi tanpa
    /// terjemahan ini isian akan hilang saat berkas disimpan ulang.
    /// </summary>
    private sealed class SkemaWarna
    {
        private readonly IReadOnlyDictionary<int, Color> _tema;
        private readonly Color[] _palet;

        public SkemaWarna(IReadOnlyDictionary<int, Color> tema)
        {
            _tema = tema;
            _palet = PaletIndeks;
        }

        public static SkemaWarna Baca(ZipArchive arsip)
        {
            var tema = new Dictionary<int, Color>();
            var entri = CariEntri(arsip, "xl/theme/theme1.xml");
            if (entri == null) return new SkemaWarna(tema);

            try
            {
                XDocument dokumen = BacaXDocument(entri);
                var skema = dokumen.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "clrScheme");

                if (skema != null)
                {
                    foreach (var slot in skema.Elements())
                    {
                        Color? warna = WarnaNode(slot);
                        if (!warna.HasValue) continue;

                        int indeks = slot.Name.LocalName switch
                        {
                            "dk1" => 1,
                            "lt1" => 0,
                            "dk2" => 3,
                            "lt2" => 2,
                            "hlink" => 10,
                            "folHlink" => 11,
                            _ => slot.Name.LocalName.StartsWith("accent", StringComparison.Ordinal) &&
                                 int.TryParse(slot.Name.LocalName["accent".Length..], out int n) &&
                                 n is >= 1 and <= 6
                                ? 3 + n
                                : -1,
                        };

                        if (indeks >= 0) tema[indeks] = warna.Value;
                    }
                }
            }
            catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException)
            {
                // Tema rusak tidak boleh menggagalkan pembacaan berkas; warna
                // tema saja yang hilang.
            }

            return new SkemaWarna(tema);
        }

        private static Color? WarnaNode(XElement slot)
        {
            var srgb = slot.Elements().FirstOrDefault(e => e.Name.LocalName == "srgbClr");
            if (srgb != null) return UraiRgb((string?)srgb.Attribute("val"));

            var sistem = slot.Elements().FirstOrDefault(e => e.Name.LocalName == "sysClr");
            return sistem == null ? null : UraiRgb((string?)sistem.Attribute("lastClr"));
        }

        /// <summary>Terjemahkan elemen warna sel (rgb/theme/indexed + tint).</summary>
        public Color? Baca(XElement? warna)
        {
            if (warna == null) return null;

            // Warna otomatis mengikuti tema; tidak perlu ditulis balik.
            if (string.Equals((string?)warna.Attribute("auto"), "1", StringComparison.Ordinal)) return null;

            double tint = (double?)warna.Attribute("tint") ?? 0;

            string? rgb = (string?)warna.Attribute("rgb");
            if (rgb != null && UraiRgb(rgb) is { } langsung)
            {
                return TerapkanTint(langsung, tint);
            }

            if (int.TryParse((string?)warna.Attribute("theme"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int indeksTema) &&
                _tema.TryGetValue(indeksTema, out Color dariTema))
            {
                return TerapkanTint(dariTema, tint);
            }

            if (int.TryParse((string?)warna.Attribute("indexed"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int indeks) &&
                indeks >= 0)
            {
                // 64 adalah "sistem" (biasanya hitam), sisanya palet lama.
                if (indeks == 64) return Color.Black;
                if (indeks < _palet.Length) return _palet[indeks];
            }

            return null;
        }

        private static Color? UraiRgb(string? argb)
        {
            if (string.IsNullOrWhiteSpace(argb)) return null;

            string bersih = argb.Length == 8 ? argb[2..] : argb;
            if (bersih.Length != 6 ||
                !int.TryParse(bersih, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int nilai))
            {
                return null;
            }

            return Color.FromArgb(255, (nilai >> 16) & 0xFF, (nilai >> 8) & 0xFF, nilai & 0xFF);
        }

        /// <summary>Rona (tint) OOXML: menggeser kecerahan dalam ruang HSL.</summary>
        internal static Color TerapkanTint(Color warna, double tint)
        {
            if (Math.Abs(tint) < 0.0001) return warna;

            (double h, double s, double l) = KeHsl(warna);

            l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;

            return DariHsl(h, s, Math.Clamp(l, 0, 1));
        }

        private static (double H, double S, double L) KeHsl(Color warna)
        {
            double r = warna.R / 255.0;
            double g = warna.G / 255.0;
            double b = warna.B / 255.0;

            double maks = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (maks + min) / 2;

            if (Math.Abs(maks - min) < 0.0001) return (0, 0, l);

            double d = maks - min;
            double s = l > 0.5 ? d / (2 - maks - min) : d / (maks + min);

            double h = maks switch
            {
                _ when Math.Abs(maks - r) < 0.0001 => ((g - b) / d + (g < b ? 6 : 0)) / 6,
                _ when Math.Abs(maks - g) < 0.0001 => ((b - r) / d + 2) / 6,
                _ => ((r - g) / d + 4) / 6,
            };

            return (h, s, l);
        }

        private static Color DariHsl(double h, double s, double l)
        {
            if (s <= 0)
            {
                int abu = Abu(l);
                return Color.FromArgb(255, abu, abu, abu);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            return Color.FromArgb(255,
                Abu(Komponen(p, q, h + 1.0 / 3)),
                Abu(Komponen(p, q, h)),
                Abu(Komponen(p, q, h - 1.0 / 3)));

            static int Abu(double nilai) => (int)Math.Round(Math.Clamp(nilai, 0, 1) * 255);

            static double Komponen(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
        }

        /// <summary>
        /// Palet indeks Excel (indexedColors bawaan). Dipakai berkas lama dan
        /// sebagian penulis pihak ketiga sebelum warna RGB/tema umum.
        /// </summary>
        private static readonly Color[] PaletIndeks =
        {
            Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255),
            Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255),
            Color.FromArgb(128, 0, 0), Color.FromArgb(0, 128, 0), Color.FromArgb(0, 0, 128), Color.FromArgb(128, 128, 0),
            Color.FromArgb(128, 0, 128), Color.FromArgb(0, 128, 128), Color.FromArgb(192, 192, 192), Color.FromArgb(128, 128, 128),
            Color.FromArgb(153, 153, 255), Color.FromArgb(153, 51, 102), Color.FromArgb(255, 255, 204), Color.FromArgb(204, 255, 255),
            Color.FromArgb(102, 0, 102), Color.FromArgb(255, 128, 128), Color.FromArgb(0, 102, 204), Color.FromArgb(204, 204, 255),
            Color.FromArgb(0, 0, 128), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 255),
            Color.FromArgb(128, 0, 128), Color.FromArgb(128, 0, 0), Color.FromArgb(0, 128, 128), Color.FromArgb(0, 0, 255),
            Color.FromArgb(0, 204, 255), Color.FromArgb(204, 255, 255), Color.FromArgb(204, 255, 204), Color.FromArgb(255, 255, 153),
            Color.FromArgb(153, 204, 255), Color.FromArgb(255, 153, 204), Color.FromArgb(204, 153, 255), Color.FromArgb(255, 204, 153),
            Color.FromArgb(51, 102, 255), Color.FromArgb(51, 204, 204), Color.FromArgb(153, 204, 0), Color.FromArgb(255, 204, 0),
            Color.FromArgb(255, 153, 0), Color.FromArgb(255, 102, 0), Color.FromArgb(102, 102, 153), Color.FromArgb(150, 150, 150),
            Color.FromArgb(0, 51, 102), Color.FromArgb(51, 153, 102), Color.FromArgb(0, 51, 0), Color.FromArgb(51, 51, 0),
            Color.FromArgb(153, 51, 0), Color.FromArgb(153, 51, 102), Color.FromArgb(51, 51, 153), Color.FromArgb(51, 51, 51),
        };
    }

    }
