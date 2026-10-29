using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Warna RGB 0..255 untuk penulisan dokumen.</summary>
public readonly record struct PdfRgb(byte R, byte G, byte B)
{
    public static readonly PdfRgb Black = new(0, 0, 0);
    public static readonly PdfRgb White = new(255, 255, 255);

    /// <summary>Warna dari teks “#RRGGBB” atau “#RGB”.</summary>
    public static PdfRgb FromHex(string? value)
    {
        string text = (value ?? string.Empty).Trim().TrimStart('#');
        return text.Length switch
        {
            3 => new PdfRgb(
                (byte)(Convert.ToInt32(new string(text[0], 2), 16)),
                (byte)(Convert.ToInt32(new string(text[1], 2), 16)),
                (byte)(Convert.ToInt32(new string(text[2], 2), 16))),
            6 => new PdfRgb(
                (byte)Convert.ToInt32(text.Substring(0, 2), 16),
                (byte)Convert.ToInt32(text.Substring(2, 2), 16),
                (byte)Convert.ToInt32(text.Substring(4, 2), 16)),
            _ => Black,
        };
    }
}

/// <summary>
/// Penyusun berkas PDF milik SudesPdf: mengumpulkan objek, halaman, gambar, dan
/// sumber daya, lalu menulis tabel xref beserta trailer. Seluruh isinya ditulis
/// proyek ini sendiri — tidak ada pustaka pihak ketiga pada jalur dokumen.
/// </summary>
public sealed class PdfWriter
{
    private readonly List<byte[]?> _objects = new();
    private readonly List<PdfWriterPage> _pages = new();
    private readonly Dictionary<string, PdfWriterImage> _images = new();
    private readonly Dictionary<PdfTimesFace, int> _fontObjects = new();
    private readonly Dictionary<PdfTimesFace, HashSet<int>> _kodeDipakai = new();
    private readonly Dictionary<PdfTimesFace, (byte[] Program, TrueTypeFont Source)?> _fontTersemat = new();
    private int _imageCounter;

    /// <summary>Nama perangkat yang dicatat pada Info dokumen.</summary>
    public string Producer { get; set; } = "SudesPdf";

    /// <summary>Keterangan dokumen (judul, penulis, subjek, kata kunci, pembuat).</summary>
    public PdfMetadata? Metadata { get; set; }

    /// <summary>Jumlah halaman yang sudah ditambahkan.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Tambah halaman baru berukuran <paramref name="width"/>×<paramref name="height"/> titik.</summary>
    public PdfWriterPage AddPage(float width, float height)
    {
        if (width <= 0 || height <= 0) throw new PdfException("Ukuran halaman harus lebih besar dari nol.");
        var page = new PdfWriterPage(width, height);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Daftarkan gambar untuk dipakai halaman. Nama yang dikembalikan dipakai
    /// <see cref="PdfContentStream.DrawImage"/>.
    /// </summary>
    public string RegisterImage(PdfWriterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        string name = "Im" + (++_imageCounter).ToString(CultureInfo.InvariantCulture);
        _images[name] = image;
        return name;
    }

    /// <summary>Susun berkas PDF lengkap. Panggil sekali; hasilnya byte berkas utuh.</summary>
    public byte[] Finish()
    {
        // Nomor objek penting dipesan lebih dulu supaya halaman dapat menunjuk
        // induknya tanpa penulisan ulang.
        int pagesId = Reserve();
        int catalogId = Reserve();
        int infoId = Reserve();

        // Kumpulkan kode WinAnsi seluruh halaman lebih dulu: subset font harus
        // sudah tahu glif mana yang dipakai sebelum objek font ditulis.
        foreach (var halaman in _pages)
        {
            foreach (var pasangan in halaman.Content.CodesUsed)
            {
                if (!_kodeDipakai.TryGetValue(pasangan.Key, out var kumpulan))
                {
                    kumpulan = new HashSet<int>();
                    _kodeDipakai[pasangan.Key] = kumpulan;
                }

                kumpulan.UnionWith(pasangan.Value);
            }
        }

        var kids = new StringBuilder();
        for (int i = 0; i < _pages.Count; i++)
        {
            int pageId = WritePage(_pages[i], pagesId);
            kids.Append(Ref(pageId)).Append(' ');
        }

        Set(pagesId, Ascii("<< /Type /Pages /Kids [" + kids.ToString().TrimEnd() + "] /Count " + _pages.Count + " >>"));
        Set(catalogId, Ascii("<< /Type /Catalog /Pages " + Ref(pagesId) + " >>"));

        var info = new StringBuilder("<< /Producer ").Append(Literal(Producer));
        info.Append(" /Creator ").Append(Literal(Metadata?.Creator ?? "SudesPdf"));
        if (!string.IsNullOrWhiteSpace(Metadata?.Title)) info.Append(" /Title ").Append(Literal(Metadata!.Title));
        if (!string.IsNullOrWhiteSpace(Metadata?.Author)) info.Append(" /Author ").Append(Literal(Metadata!.Author));
        if (!string.IsNullOrWhiteSpace(Metadata?.Subject)) info.Append(" /Subject ").Append(Literal(Metadata!.Subject));
        if (!string.IsNullOrWhiteSpace(Metadata?.Keywords)) info.Append(" /Keywords ").Append(Literal(Metadata!.Keywords));
        info.Append(" /CreationDate ").Append(Literal("D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)));
        info.Append(" >>");
        Set(infoId, Ascii(info.ToString()));

        return Build(catalogId, infoId);
    }

    // ---------------------------------------------------------------- objek

    private int Reserve()
    {
        _objects.Add(null);
        return _objects.Count;
    }

    private void Set(int id, byte[] body) => _objects[id - 1] = body;

    private static string Ref(int id) => id.ToString(CultureInfo.InvariantCulture) + " 0 R";

    /// <summary>Tulis halaman: aliran isi, kamus halaman, dan sumber dayanya.</summary>
    private int WritePage(PdfWriterPage page, int pagesId)
    {
        byte[] content = page.Content.Build();
        int contentId = Reserve();
        Set(contentId, ContentStream(content));

        var resources = new StringBuilder("<< ");
        if (page.Content.FontsUsed.Count > 0)
        {
            resources.Append("/Font << ");
            foreach (var face in page.Content.FontsUsed)
            {
                resources.Append('/').Append(PdfStandardTimes.ResourceName(face)).Append(' ')
                    .Append(Ref(FontObject(face))).Append(' ');
            }

            resources.Append(">> ");
        }

        if (page.Content.ImagesUsed.Count > 0)
        {
            resources.Append("/XObject << ");
            foreach (string name in page.Content.ImagesUsed)
            {
                resources.Append('/').Append(name).Append(' ').Append(Ref(ImageObject(name))).Append(' ');
            }

            resources.Append(">> ");
        }

        resources.Append(">>");

        int pageId = Reserve();
        Set(pageId, Ascii("<< /Type /Page /Parent " + Ref(pagesId) + " /MediaBox [0 0 " +
            Num(page.Width) + " " + Num(page.Height) + "] /Resources " + resources +
            " /Contents " + Ref(contentId) + " >>"));
        return pageId;
    }

    /// <summary>
    /// Objek font (dibuat sekali per muka). Bila Times New Roman tersedia di
    /// sistem, subset font itu DISEMATKAN ke berkas sehingga semua pembaca PDF
    /// menampilkan bentuk huruf yang sama — bukan mengganti nama base-14 dengan
    /// font serif lain yang lebih tipis/berbeda metrik. Bila tidak tersedia,
    /// jatuh kembali ke font standar PDF agar dokumen tetap terbentuk.
    /// </summary>
    private int FontObject(PdfTimesFace face)
    {
        if (_fontObjects.TryGetValue(face, out int existing)) return existing;

        var widths = new StringBuilder();
        var table = PdfStandardTimes.Widths(face);
        for (int i = 0; i < table.Length; i++)
        {
            if (i > 0) widths.Append(' ');
            widths.Append(table[i].ToString(CultureInfo.InvariantCulture));
        }

        int id = Reserve();
        var tersemat = MuatFontTersemat(face);
        if (tersemat is { } font)
        {
            int fileId = Reserve();
            Set(fileId, FontFileStream(font.Program));

            int descriptorId = Reserve();
            Set(descriptorId, Ascii(FontDescriptor(face, font.Source, fileId)));

            Set(id, Ascii(
                "<< /Type /Font /Subtype /TrueType /BaseFont /" + NamaFontTersemat(face) +
                " /Encoding /WinAnsiEncoding /FirstChar " + PdfStandardTimes.FirstChar +
                " /LastChar " + PdfStandardTimes.LastChar + " /Widths [" + widths + "]" +
                " /FontDescriptor " + Ref(descriptorId) + " >>"));
        }
        else
        {
            Set(id, Ascii(
                "<< /Type /Font /Subtype /Type1 /BaseFont /" + PdfStandardTimes.BaseFontName(face) +
                " /Encoding /WinAnsiEncoding /FirstChar " + PdfStandardTimes.FirstChar +
                " /LastChar " + PdfStandardTimes.LastChar + " /Widths [" + widths + "] >>"));
        }

        _fontObjects[face] = id;
        return id;
    }

    /// <summary>Nama Times New Roman pada kamus font berkas PDF.</summary>
    private static string NamaFontTersemat(PdfTimesFace face) => face switch
    {
        PdfTimesFace.Bold => "TimesNewRomanPS-BoldMT",
        PdfTimesFace.Italic => "TimesNewRomanPS-ItalicMT",
        PdfTimesFace.BoldItalic => "TimesNewRomanPS-BoldItalicMT",
        _ => "TimesNewRomanPSMT",
    };

    /// <summary>
    /// Bangun subset font untuk muka ini (sekali per dokumen). Null bila font
    /// sistem tidak ada/tidak terbaca atau tabel wajibnya tidak lengkap.
    /// </summary>
    private (byte[] Program, TrueTypeFont Source)? MuatFontTersemat(PdfTimesFace face)
    {
        if (_fontTersemat.TryGetValue(face, out var tersimpan)) return tersimpan;

        (byte[] Program, TrueTypeFont Source)? hasil = null;
        try
        {
            string? berkas = PdfStandardFontSource.FaceFile(face);
            string? jalur = berkas is null ? null : PdfStandardFontSource.FindFile(berkas);
            if (jalur is not null)
            {
                var font = TrueTypeFont.Parse(File.ReadAllBytes(jalur));
                if (font is not null)
                {
                    var glyphs = new HashSet<int> { 0 };
                    if (_kodeDipakai.TryGetValue(face, out var kode))
                    {
                        foreach (int k in kode)
                        {
                            char? karakter = PdfEncodingTables.Lookup("WinAnsiEncoding", k);
                            if (karakter is { } c && font.TryGetGlyphIdForCharacter(c, out int glyphId))
                            {
                                glyphs.Add(glyphId);
                            }
                        }
                    }

                    byte[] subset = font.BuildSubset(glyphs);
                    if (subset.Length > 0) hasil = (subset, font);
                }
            }
        }
        catch (Exception)
        {
            // Font sistem yang rusak tidak boleh menggagalkan surat: dokumen
            // tetap dibuat dengan font standar PDF.
            hasil = null;
        }

        _fontTersemat[face] = hasil;
        return hasil;
    }

    /// <summary>Aliran program font tersemat (/FontFile2).</summary>
    private static byte[] FontFileStream(byte[] program)
    {
        byte[] data = Compress(program);
        return Build(
            "<< /Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) +
            " /Filter /FlateDecode /Length " + data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n",
            data);
    }

    /// <summary>Kamus /FontDescriptor dengan metrik yang diambil dari font asli.</summary>
    private static string FontDescriptor(
        PdfTimesFace face, TrueTypeFont font, int fileId)
    {
        var m = font.ReadFontMetrics();
        double skala = 1000.0 / font.UnitsPerEm;
        int S(int nilai) => (int)Math.Round(nilai * skala, MidpointRounding.AwayFromZero);

        int flags = 32 | 2; // nonsimbolik + serif
        if (face is PdfTimesFace.Italic or PdfTimesFace.BoldItalic) flags |= 64;

        int ascent = S(m.Ascent);
        int descent = S(m.Descent);
        int capHeight = m.CapHeight > 0 ? S(m.CapHeight) : (int)Math.Round(ascent * 0.7);
        int stemV = face is PdfTimesFace.Bold or PdfTimesFace.BoldItalic ? 120 : 80;

        return "<< /Type /FontDescriptor /FontName /" + NamaFontTersemat(face) +
               " /Flags " + flags.ToString(CultureInfo.InvariantCulture) +
               " /FontBBox [" + S(m.XMin) + " " + S(m.YMin) + " " + S(m.XMax) + " " + S(m.YMax) + "]" +
               " /ItalicAngle " + m.ItalicAngle.ToString("0.####", CultureInfo.InvariantCulture) +
               " /Ascent " + ascent + " /Descent " + descent +
               " /CapHeight " + capHeight + " /StemV " + stemV +
               " /FontFile2 " + Ref(fileId) + " >>";
    }

    /// <summary>Objek gambar (dibuat sekali per gambar terdaftar).</summary>
    private int ImageObject(string name)
    {
        var image = _images[name];
        if (image.ObjectId != 0) return image.ObjectId;

        int? maskId = null;
        if (image.Alpha is { Length: > 0 })
        {
            maskId = Reserve();
            Set(maskId.Value, ImageStream(image.Width, image.Height, "/DeviceGray", 8, "/FlateDecode", Compress(image.Alpha)));
        }

        string filter = image.Jpeg ? "/DCTDecode" : "/FlateDecode";
        byte[] data = image.Jpeg ? image.Data : Compress(image.Data);

        int id = Reserve();
        Set(id, ImageStream(image.Width, image.Height, image.ColorSpace, image.BitsPerComponent, filter, data, maskId));
        image.ObjectId = id;
        return id;
    }

    /// <summary>Objek aliran isi halaman (terkompresi Flate).</summary>
    private static byte[] ContentStream(byte[] data) =>
        Build("<< /Filter /FlateDecode /Length " + data.Length + " >>\nstream\n", data);

    /// <summary>Objek gambar: kamus gambar lalu data (DCT untuk JPEG, Flate untuk lainnya).</summary>
    private static byte[] ImageStream(
        int width, int height, string colorSpace, int bitsPerComponent, string filter, byte[] data, int? maskId = null)
    {
        string head = "<< /Type /XObject /Subtype /Image /Width " + width + " /Height " + height +
            " /ColorSpace " + colorSpace + " /BitsPerComponent " + bitsPerComponent +
            (maskId is { } mask ? " /SMask " + Ref(mask) : string.Empty) +
            " /Filter " + filter + " /Length " + data.Length + " >>\nstream\n";
        return Build(head, data);
    }

    private static byte[] Build(string head, byte[] content)
    {
        var stream = new MemoryStream(head.Length + content.Length + 16);
        WriteAscii(stream, head);
        stream.Write(content, 0, content.Length);
        WriteAscii(stream, "\nendstream");
        return stream.ToArray();
    }

    /// <summary>Tulis tabel xref, trailer, dan penanda akhir berkas.</summary>
    private byte[] Build(int catalogId, int infoId)
    {
        var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.7\n%âãÏÓ\n");

        var offsets = new long[_objects.Count + 1];
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets[i + 1] = output.Length;
            byte[] body = _objects[i] ?? Ascii("null");
            WriteAscii(output, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            output.Write(body, 0, body.Length);
            WriteAscii(output, "\nendobj\n");
        }

        long xrefOffset = output.Length;
        WriteAscii(output, "xref\n0 " + (_objects.Count + 1) + "\n");
        WriteAscii(output, "0000000000 65535 f \n");
        for (int i = 1; i <= _objects.Count; i++)
        {
            WriteAscii(output, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(output, "trailer\n<< /Size " + (_objects.Count + 1) + " /Root " + Ref(catalogId) +
            " /Info " + Ref(infoId) + " >>\nstartxref\n" + xrefOffset + "\n%%EOF\n");
        return output.ToArray();
    }

    // ------------------------------------------------------------- utilitas

    internal static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    internal static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    internal static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = Ascii(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Angka PDF: tanpa notasi ilmiah dan tanpa nol berekor.</summary>
    internal static string Num(double value)
    {
        if (Math.Abs(value) < 0.0005) return "0";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Teks PDF aman di dalam tanda kurung.</summary>
    internal static string Literal(string? text)
    {
        var builder = new StringBuilder("(");
        foreach (byte b in PdfStandardTimes.Encode(text))
        {
            switch (b)
            {
                case (byte)'(': builder.Append("\\("); break;
                case (byte)')': builder.Append("\\)"); break;
                case (byte)'\\': builder.Append("\\\\"); break;
                case (byte)'\r': builder.Append("\\r"); break;
                case (byte)'\n': builder.Append("\\n"); break;
                default:
                    if (b < 32 || b > 126) builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else builder.Append((char)b);
                    break;
            }
        }

        return builder.Append(')').ToString();
    }
}

/// <summary>Keterangan dokumen yang dicatat pada kamus Info berkas PDF.</summary>
public sealed class PdfMetadata
{
    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string? Creator { get; set; }
}

/// <summary>Satu halaman yang sedang disusun.</summary>
public sealed class PdfWriterPage
{
    internal PdfWriterPage(float width, float height)
    {
        Width = width;
        Height = height;
        Content = new PdfContentStream();
    }

    /// <summary>Lebar halaman dalam titik.</summary>
    public float Width { get; }

    /// <summary>Tinggi halaman dalam titik.</summary>
    public float Height { get; }

    /// <summary>Aliran isi halaman.</summary>
    public PdfContentStream Content { get; }
}
