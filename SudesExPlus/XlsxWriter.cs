using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using SudesExPlus.Style;

namespace SudesExPlus;

/// <summary>
/// Penulis .xlsx (OOXML) untuk model <see cref="ExcelPackage"/>.
///
/// Berkas disusun sebagai ZIP berisi bagian XML minimum yang dibutuhkan Excel:
/// content types, relasi, workbook, relasi workbook, satu worksheet per tab,
/// dan styles. Teks ditulis sebagai <c>inlineStr</c> supaya tidak perlu tabel
/// shared strings; Excel, LibreOffice, dan WPS membacanya sama baiknya, dan
/// pembaca SudesExPlus tetap menerima kedua bentuk saat membaca berkas lain.
/// </summary>
internal static class XlsxWriter
{
    private const string NsUtama = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRelasi = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPaket = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Tulis(ExcelPackage paket, Stream aliran)
    {
        var tabelGaya = new TabelGaya();
        var daftarLembar = paket.Workbook.Worksheets.ToList();

        // Kumpulkan gaya semua sel lebih dulu: indeks xf harus sudah final
        // sebelum sheet ditulis.
        foreach (var lembar in daftarLembar)
        {
            foreach (var sel in lembar.Sel)
            {
                tabelGaya.Indeks(sel.Gaya, sel.Value);
            }
        }

        using var arsip = new ZipArchive(aliran, ZipArchiveMode.Create, leaveOpen: true);

        TulisContentTypes(arsip, daftarLembar.Count);
        TulisRelasiUtama(arsip);
        TulisWorkbook(arsip, daftarLembar);
        TulisRelasiWorkbook(arsip, daftarLembar.Count);
        TulisStyles(arsip, tabelGaya);

        for (int i = 0; i < daftarLembar.Count; i++)
        {
            TulisSheet(arsip, daftarLembar[i], tabelGaya, i + 1);
        }
    }

    private static void TulisContentTypes(ZipArchive arsip, int jumlahLembar)
    {
        TulisXml(arsip, "[Content_Types].xml", xml =>
        {
            xml.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");

            BagianDefault(xml, "rels", "application/vnd.openxmlformats-package.relationships+xml");
            BagianDefault(xml, "xml", "application/xml");

            BagianGanti(xml, "/xl/workbook.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            BagianGanti(xml, "/xl/styles.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

            for (int i = 1; i <= jumlahLembar; i++)
            {
                BagianGanti(xml, $"/xl/worksheets/sheet{i}.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            }

            xml.WriteEndElement();
        });
    }

    private static void BagianDefault(XmlWriter xml, string ekstensi, string tipe)
    {
        xml.WriteStartElement("Default");
        xml.WriteAttributeString("Extension", ekstensi);
        xml.WriteAttributeString("ContentType", tipe);
        xml.WriteEndElement();
    }

    private static void BagianGanti(XmlWriter xml, string nama, string tipe)
    {
        xml.WriteStartElement("Override");
        xml.WriteAttributeString("PartName", nama);
        xml.WriteAttributeString("ContentType", tipe);
        xml.WriteEndElement();
    }

    private static void TulisRelasiUtama(ZipArchive arsip)
    {
        TulisXml(arsip, "_rels/.rels", xml =>
        {
            xml.WriteStartElement("Relationships", NsPaket);
            xml.WriteStartElement("Relationship");
            xml.WriteAttributeString("Id", "rId1");
            xml.WriteAttributeString("Type",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            xml.WriteAttributeString("Target", "xl/workbook.xml");
            xml.WriteEndElement();
            xml.WriteEndElement();
        });
    }

    private static void TulisWorkbook(ZipArchive arsip, IReadOnlyList<ExcelWorksheet> lembar)
    {
        TulisXml(arsip, "xl/workbook.xml", xml =>
        {
            xml.WriteStartElement("workbook", NsUtama);
            xml.WriteAttributeString("xmlns", "r", null, NsRelasi);

            xml.WriteStartElement("sheets");
            for (int i = 0; i < lembar.Count; i++)
            {
                xml.WriteStartElement("sheet");
                xml.WriteAttributeString("name", ExcelWorksheetCollection.NamaAman(lembar[i].Name));
                xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("id", NsRelasi, $"rId{i + 1}");
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        });
    }

    private static void TulisRelasiWorkbook(ZipArchive arsip, int jumlahLembar)
    {
        TulisXml(arsip, "xl/_rels/workbook.xml.rels", xml =>
        {
            xml.WriteStartElement("Relationships", NsPaket);

            for (int i = 1; i <= jumlahLembar; i++)
            {
                Relasi(xml, $"rId{i}",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    $"worksheets/sheet{i}.xml");
            }

            Relasi(xml, $"rId{jumlahLembar + 1}",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
                "styles.xml");

            xml.WriteEndElement();
        });
    }

    private static void Relasi(XmlWriter xml, string id, string tipe, string target)
    {
        xml.WriteStartElement("Relationship");
        xml.WriteAttributeString("Id", id);
        xml.WriteAttributeString("Type", tipe);
        xml.WriteAttributeString("Target", target);
        xml.WriteEndElement();
    }

    private static void TulisStyles(ZipArchive arsip, TabelGaya tabel)
    {
        TulisXml(arsip, "xl/styles.xml", xml =>
        {
            xml.WriteStartElement("styleSheet", NsUtama);

            if (tabel.FormatKhusus.Count > 0)
            {
                xml.WriteStartElement("numFmts");
                xml.WriteAttributeString("count", tabel.FormatKhusus.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var (id, kode) in tabel.FormatKhusus.OrderBy(p => p.Key))
                {
                    xml.WriteStartElement("numFmt");
                    xml.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("formatCode", kode);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            // ---- Font ----
            xml.WriteStartElement("fonts");
            xml.WriteAttributeString("count", tabel.Font.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var font in tabel.Font)
            {
                xml.WriteStartElement("font");
                if (font.Bold) xml.WriteElementString("b", string.Empty);
                if (font.Italic) xml.WriteElementString("i", string.Empty);
                if (font.GarisBawah) xml.WriteElementString("u", string.Empty);

                xml.WriteStartElement("sz");
                xml.WriteAttributeString("val", font.Ukuran.ToString("0.##", CultureInfo.InvariantCulture));
                xml.WriteEndElement();

                if (font.Warna.HasValue)
                {
                    xml.WriteStartElement("color");
                    xml.WriteAttributeString("rgb", Rgb(font.Warna.Value));
                    xml.WriteEndElement();
                }

                xml.WriteStartElement("name");
                xml.WriteAttributeString("val", string.IsNullOrWhiteSpace(font.Nama) ? "Calibri" : font.Nama!);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            xml.WriteEndElement();

            // ---- Isian ----
            xml.WriteStartElement("fills");
            xml.WriteAttributeString("count", tabel.Isian.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < tabel.Isian.Count; i++)
            {
                var isian = tabel.Isian[i];
                xml.WriteStartElement("fill");
                xml.WriteStartElement("patternFill");

                // Slot 0 "none" dan slot 1 "gray125" adalah konvensi berkas
                // Excel; isian sungguhan mulai dari slot 2.
                xml.WriteAttributeString("patternType",
                    i == 0 ? "none" : i == 1 ? "gray125" : isian.HasValue ? "solid" : "none");

                if (isian.HasValue)
                {
                    xml.WriteStartElement("fgColor");
                    xml.WriteAttributeString("rgb", Rgb(isian.Value));
                    xml.WriteEndElement();

                    xml.WriteStartElement("bgColor");
                    xml.WriteAttributeString("indexed", "64");
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            xml.WriteEndElement();

            // ---- Garis ----
            xml.WriteStartElement("borders");
            xml.WriteAttributeString("count", tabel.Garis.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var garis in tabel.Garis)
            {
                xml.WriteStartElement("border");
                GarisSisi(xml, "left", garis.Kiri);
                GarisSisi(xml, "right", garis.Kanan);
                GarisSisi(xml, "top", garis.Atas);
                GarisSisi(xml, "bottom", garis.Bawah);
                xml.WriteElementString("diagonal", string.Empty);
                xml.WriteEndElement();
            }

            xml.WriteEndElement();

            // ---- Gaya sel ----
            xml.WriteStartElement("cellStyleXfs");
            xml.WriteAttributeString("count", "1");
            xml.WriteStartElement("xf");
            xml.WriteAttributeString("numFmtId", "0");
            xml.WriteAttributeString("fontId", "0");
            xml.WriteAttributeString("fillId", "0");
            xml.WriteAttributeString("borderId", "0");
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("cellXfs");
            xml.WriteAttributeString("count", tabel.Xf.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var xf in tabel.Xf)
            {
                xml.WriteStartElement("xf");
                xml.WriteAttributeString("numFmtId", xf.NumFmtId.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("fontId", xf.FontId.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("fillId", xf.FillId.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("borderId", xf.BorderId.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("xfId", "0");

                if (xf.NumFmtId != 0) xml.WriteAttributeString("applyNumberFormat", "1");
                if (xf.FontId != 0) xml.WriteAttributeString("applyFont", "1");
                if (xf.FillId != 0) xml.WriteAttributeString("applyFill", "1");
                if (xf.BorderId != 0) xml.WriteAttributeString("applyBorder", "1");

                if (xf.RataMendatar.HasValue || xf.RataTegak.HasValue || xf.BungkusTeks)
                {
                    xml.WriteAttributeString("applyAlignment", "1");
                    xml.WriteStartElement("alignment");
                    if (xf.RataMendatar.HasValue)
                    {
                        xml.WriteAttributeString("horizontal", TeksRata(xf.RataMendatar.Value));
                    }

                    if (xf.RataTegak.HasValue)
                    {
                        xml.WriteAttributeString("vertical", TeksRataTegak(xf.RataTegak.Value));
                    }

                    if (xf.BungkusTeks) xml.WriteAttributeString("wrapText", "1");

                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();

            // Gaya sel bernama "Normal" — wajib ada supaya Excel dan pembaca
            // lain tidak menganggap workbook kehilangan gaya bawaannya.
            xml.WriteStartElement("cellStyles");
            xml.WriteAttributeString("count", "1");
            xml.WriteStartElement("cellStyle");
            xml.WriteAttributeString("name", "Normal");
            xml.WriteAttributeString("xfId", "0");
            xml.WriteAttributeString("builtinId", "0");
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteEndElement();
        });
    }

    private static void GarisSisi(XmlWriter xml, string sisi, ExcelBorderStyle gaya)
    {
        xml.WriteStartElement(sisi);
        if (gaya != ExcelBorderStyle.None)
        {
            xml.WriteAttributeString("style", TeksGaris(gaya));
        }

        xml.WriteEndElement();
    }

    private static string TeksGaris(ExcelBorderStyle gaya) => gaya switch
    {
        ExcelBorderStyle.Hair => "hair",
        ExcelBorderStyle.Thin => "thin",
        ExcelBorderStyle.Medium => "medium",
        ExcelBorderStyle.Thick => "thick",
        ExcelBorderStyle.Double => "double",
        _ => "none",
    };

    private static string TeksRata(ExcelHorizontalAlignment rata) => rata switch
    {
        ExcelHorizontalAlignment.Left => "left",
        ExcelHorizontalAlignment.Center => "center",
        ExcelHorizontalAlignment.Right => "right",
        _ => "general",
    };

    private static string TeksRataTegak(ExcelVerticalAlignment rata) => rata switch
    {
        ExcelVerticalAlignment.Top => "top",
        ExcelVerticalAlignment.Center => "center",
        _ => "bottom",
    };

    private static void TulisSheet(ZipArchive arsip, ExcelWorksheet lembar, TabelGaya tabel, int nomor)
    {
        var sel = lembar.Sel.ToList();
        var dimensi = lembar.Dimension;

        TulisXml(arsip, $"xl/worksheets/sheet{nomor}.xml", xml =>
        {
            xml.WriteStartElement("worksheet", NsUtama);

            if (dimensi != null)
            {
                xml.WriteStartElement("dimension");
                xml.WriteAttributeString("ref",
                    ExcelRujukan.Rentang(dimensi.Start.Row, dimensi.Start.Column,
                        dimensi.End.Row, dimensi.End.Column));
                xml.WriteEndElement();
            }

            if (lembar.Bekukan is { } beku)
            {
                TulisBekukan(xml, beku);
            }

            xml.WriteStartElement("sheetFormatPr");
            xml.WriteAttributeString("defaultRowHeight", "15");
            xml.WriteEndElement();

            if (lembar.LebarKolom.Count > 0)
            {
                xml.WriteStartElement("cols");
                foreach (var (kolom, lebar) in lembar.LebarKolom.OrderBy(p => p.Key))
                {
                    xml.WriteStartElement("col");
                    xml.WriteAttributeString("min", kolom.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("max", kolom.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("width", lebar.ToString("0.##", CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("customWidth", "1");
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteStartElement("sheetData");

            foreach (var grup in sel.GroupBy(s => s.Baris).OrderBy(g => g.Key))
            {
                xml.WriteStartElement("row");
                xml.WriteAttributeString("r", grup.Key.ToString(CultureInfo.InvariantCulture));

                if (lembar.TinggiBaris.TryGetValue(grup.Key, out double tinggi))
                {
                    xml.WriteAttributeString("ht", tinggi.ToString("0.##", CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("customHeight", "1");
                }

                foreach (var satu in grup.OrderBy(s => s.Kolom))
                {
                    TulisSel(xml, satu, tabel);
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();

            if (lembar.Gabungan.Count > 0)
            {
                xml.WriteStartElement("mergeCells");
                xml.WriteAttributeString("count", lembar.Gabungan.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string rujukan in lembar.Gabungan)
                {
                    xml.WriteStartElement("mergeCell");
                    xml.WriteAttributeString("ref", rujukan);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        });
    }

    private static void TulisBekukan(XmlWriter xml, (int Baris, int Kolom) beku)
    {
        int ySplit = beku.Baris - 1;
        int xSplit = beku.Kolom - 1;
        if (ySplit <= 0 && xSplit <= 0) return;

        string kiriAtas = ExcelRujukan.Sel(beku.Baris, beku.Kolom);

        xml.WriteStartElement("sheetViews");
        xml.WriteStartElement("sheetView");
        xml.WriteAttributeString("workbookViewId", "0");

        xml.WriteStartElement("pane");
        if (xSplit > 0) xml.WriteAttributeString("xSplit", xSplit.ToString(CultureInfo.InvariantCulture));
        if (ySplit > 0) xml.WriteAttributeString("ySplit", ySplit.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("topLeftCell", kiriAtas);
        xml.WriteAttributeString("activePane",
            xSplit > 0 && ySplit > 0 ? "bottomRight" : ySplit > 0 ? "bottomLeft" : "topRight");
        xml.WriteAttributeString("state", "frozen");
        xml.WriteEndElement();

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void TulisSel(XmlWriter xml, ExcelCell sel, TabelGaya tabel)
    {
        var nilai = sel.Value;
        int indeksGaya = tabel.Indeks(sel.Gaya, nilai);

        xml.WriteStartElement("c");
        xml.WriteAttributeString("r", ExcelRujukan.Sel(sel.Baris, sel.Kolom));

        if (indeksGaya > 0)
        {
            xml.WriteAttributeString("s", indeksGaya.ToString(CultureInfo.InvariantCulture));
        }

        if (sel.Rumus is { } rumus)
        {
            TulisSelRumus(xml, nilai, rumus);
            xml.WriteEndElement();
            return;
        }

        switch (nilai)
        {
            case null:
                break;

            case string teks:
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is");
                xml.WriteStartElement("t");
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteString(teks);
                xml.WriteEndElement();
                xml.WriteEndElement();
                break;

            case bool logika:
                xml.WriteAttributeString("t", "b");
                xml.WriteElementString("v", logika ? "1" : "0");
                break;

            case DateTime waktu:
                xml.WriteElementString("v", SerialTanggal(waktu).ToString("R", CultureInfo.InvariantCulture));
                break;

            case sbyte or byte or short or ushort or int or uint or long or ulong:
                xml.WriteElementString("v",
                    Convert.ToInt64(nilai, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;

            case float or double or decimal:
                xml.WriteElementString("v",
                    Convert.ToDouble(nilai, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                break;

            default:
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is");
                xml.WriteStartElement("t");
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteString(nilai.ToString() ?? string.Empty);
                xml.WriteEndElement();
                xml.WriteEndElement();
                break;
        }

        xml.WriteEndElement();
    }

    /// <summary>
    /// Sel berumus: tulis <c>&lt;f&gt;</c> lalu nilai terakhir yang tersimpan
    /// (kalau ada) sebagai <c>&lt;v&gt;</c>. Excel menghitung ulang saat berkas
    /// dibuka, dan pembaca lain tetap melihat hasil terakhir tanpa menghitung.
    /// </summary>
    private static void TulisSelRumus(XmlWriter xml, object? nilai, string rumus)
    {
        switch (nilai)
        {
            case string teks:
                xml.WriteAttributeString("t", "str");
                xml.WriteStartElement("f");
                xml.WriteString(rumus);
                xml.WriteEndElement();
                xml.WriteStartElement("v");
                xml.WriteString(teks);
                xml.WriteEndElement();
                break;

            case bool logika:
                xml.WriteAttributeString("t", "b");
                xml.WriteStartElement("f");
                xml.WriteString(rumus);
                xml.WriteEndElement();
                xml.WriteElementString("v", logika ? "1" : "0");
                break;

            case DateTime waktu:
                xml.WriteStartElement("f");
                xml.WriteString(rumus);
                xml.WriteEndElement();
                xml.WriteElementString("v", SerialTanggal(waktu).ToString("R", CultureInfo.InvariantCulture));
                break;

            case null:
                xml.WriteStartElement("f");
                xml.WriteString(rumus);
                xml.WriteEndElement();
                break;

            default:
                xml.WriteStartElement("f");
                xml.WriteString(rumus);
                xml.WriteEndElement();
                xml.WriteElementString("v",
                    Convert.ToDouble(nilai, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Serial tanggal Excel (sistem 1900, seperti yang dipakai Excel).</summary>
    internal static double SerialTanggal(DateTime waktu) => waktu.ToOADate();

    private static string Rgb(Color warna) => "FF" + warna.R.ToString("X2") + warna.G.ToString("X2") + warna.B.ToString("X2");

    private static void TulisXml(ZipArchive arsip, string nama, Action<XmlWriter> isi)
    {
        var entri = arsip.CreateEntry(nama, CompressionLevel.Optimal);
        using var aliran = entri.Open();

        var pengaturan = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false,
        };

        using var xml = XmlWriter.Create(aliran, pengaturan);
        xml.WriteStartDocument(standalone: true);
        isi(xml);
        xml.WriteEndDocument();
    }

    // =====================================================================
    // Tabel gaya: mengubah gaya sel menjadi indeks xf
    // =====================================================================

    /// <summary>Kumpulan font, isian, garis, dan xf unik untuk satu workbook.</summary>
    private sealed class TabelGaya
    {
        private readonly Dictionary<string, int> _indeksFont = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _indeksIsian = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _indeksGaris = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _indeksXf = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _indeksFormat = new(StringComparer.Ordinal);

        public List<EntriFont> Font { get; } = new();

        public List<Color?> Isian { get; } = new();

        public List<EntriGaris> Garis { get; } = new();

        public List<EntriXf> Xf { get; } = new();

        /// <summary>numFmtId → formatCode untuk format yang tidak ada di daftar bawaan.</summary>
        public Dictionary<int, string> FormatKhusus { get; } = new();

        public TabelGaya()
        {
            // Indeks 0 selalu gaya bawaan supaya sel tanpa gaya memakai xf 0.
            IsiFont(new EntriFont());
            Isian.Add(null);
            Isian.Add(null); // gray125: slot wajib kedua pada berkas Excel
            IsiGaris(new EntriGaris());
            Xf.Add(new EntriXf(0, 0, 0, 0, null, null, false));
            _indeksXf["0"] = 0;
        }

        public int Indeks(GayaSel gaya, object? nilai)
        {
            int fontId = FontId(gaya);
            int fillId = FillId(gaya);
            int borderId = BorderId(gaya);
            int numFmtId = NumFmtId(gaya, nilai);

            string kunci = string.Join('|',
                numFmtId, fontId, fillId, borderId,
                gaya.RataMendatar?.ToString() ?? "-",
                gaya.RataTegak?.ToString() ?? "-",
                gaya.BungkusTeks);

            if (_indeksXf.TryGetValue(kunci, out int ada)) return ada;

            var xf = new EntriXf(numFmtId, fontId, fillId, borderId,
                gaya.RataMendatar, gaya.RataTegak, gaya.BungkusTeks);
            Xf.Add(xf);
            _indeksXf[kunci] = Xf.Count - 1;
            return Xf.Count - 1;
        }

        private int NumFmtId(GayaSel gaya, object? nilai)
        {
            if (!string.IsNullOrWhiteSpace(gaya.FormatAngka))
            {
                string kode = gaya.FormatAngka!;
                if (!_indeksFormat.TryGetValue(kode, out int id))
                {
                    // 164 ke atas adalah wilayah format buatan sendiri di OOXML.
                    id = 164 + _indeksFormat.Count;
                    _indeksFormat[kode] = id;
                    FormatKhusus[id] = kode;
                }

                return id;
            }

            // Tanggal tanpa format eksplisit tetap harus tampil sebagai tanggal.
            return nilai is DateTime ? 14 : 0;
        }

        private int FontId(GayaSel gaya)
        {
            if (!gaya.Bold && !gaya.Italic && !gaya.GarisBawahHuruf &&
                string.IsNullOrEmpty(gaya.NamaHuruf) &&
                !gaya.UkuranHuruf.HasValue && !gaya.WarnaHuruf.HasValue)
            {
                return 0;
            }

            // Kunci harus memakai ukuran EFEKTIF (default 11), sama seperti
            // kunci di IsiFont; kalau tidak, sel tanpa ukuran eksplisit akan
            // dianggap font baru setiap kali dan menghasilkan xf tambahan di
            // luar indeks yang sudah ditulis ke styles.xml.
            string kunci = string.Join('|', gaya.Bold, gaya.Italic, gaya.GarisBawahHuruf,
                gaya.NamaHuruf ?? "Calibri",
                (gaya.UkuranHuruf ?? 11).ToString(CultureInfo.InvariantCulture),
                gaya.WarnaHuruf.HasValue ? Rgb(gaya.WarnaHuruf.Value) : "-");

            if (_indeksFont.TryGetValue(kunci, out int ada)) return ada;

            return IsiFont(new EntriFont
            {
                Bold = gaya.Bold,
                Italic = gaya.Italic,
                GarisBawah = gaya.GarisBawahHuruf,
                Nama = gaya.NamaHuruf,
                Ukuran = gaya.UkuranHuruf ?? 11,
                Warna = gaya.WarnaHuruf,
            });
        }

        private int IsiFont(EntriFont font)
        {
            string kunci = string.Join('|', font.Bold, font.Italic, font.GarisBawah,
                font.Nama ?? "Calibri",
                font.Ukuran.ToString(CultureInfo.InvariantCulture),
                font.Warna.HasValue ? Rgb(font.Warna.Value) : "-");

            Font.Add(font);
            int indeks = Font.Count - 1;
            _indeksFont[kunci] = indeks;
            return indeks;
        }

        private int FillId(GayaSel gaya)
        {
            if (!gaya.WarnaIsian.HasValue) return 0;

            string kunci = Rgb(gaya.WarnaIsian.Value);
            if (_indeksIsian.TryGetValue(kunci, out int ada)) return ada;

            Isian.Add(gaya.WarnaIsian);
            int indeks = Isian.Count - 1;
            _indeksIsian[kunci] = indeks;
            return indeks;
        }

        private int BorderId(GayaSel gaya)
        {
            if (gaya.GarisKiri == ExcelBorderStyle.None && gaya.GarisKanan == ExcelBorderStyle.None &&
                gaya.GarisAtas == ExcelBorderStyle.None && gaya.GarisBawah == ExcelBorderStyle.None)
            {
                return 0;
            }

            string kunci = string.Join('|', (int)gaya.GarisKiri, (int)gaya.GarisKanan,
                (int)gaya.GarisAtas, (int)gaya.GarisBawah);

            if (_indeksGaris.TryGetValue(kunci, out int ada)) return ada;

            return IsiGaris(new EntriGaris
            {
                Kiri = gaya.GarisKiri,
                Kanan = gaya.GarisKanan,
                Atas = gaya.GarisAtas,
                Bawah = gaya.GarisBawah,
            });
        }

        private int IsiGaris(EntriGaris garis)
        {
            string kunci = string.Join('|', (int)garis.Kiri, (int)garis.Kanan, (int)garis.Atas, (int)garis.Bawah);
            Garis.Add(garis);
            int indeks = Garis.Count - 1;
            _indeksGaris[kunci] = indeks;
            return indeks;
        }
    }

    private sealed class EntriFont
    {
        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool GarisBawah { get; init; }

        /// <summary>Nama huruf; null berarti Calibri.</summary>
        public string? Nama { get; init; }

        public double Ukuran { get; init; } = 11;

        public Color? Warna { get; init; }
    }

    private sealed class EntriGaris
    {
        public ExcelBorderStyle Kiri { get; init; }

        public ExcelBorderStyle Kanan { get; init; }

        public ExcelBorderStyle Atas { get; init; }

        public ExcelBorderStyle Bawah { get; init; }
    }

    private sealed record EntriXf(
        int NumFmtId,
        int FontId,
        int FillId,
        int BorderId,
        ExcelHorizontalAlignment? RataMendatar,
        ExcelVerticalAlignment? RataTegak,
        bool BungkusTeks);
}
