using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


/// <summary>Pembuat font dari kamus sumber daya halaman.</summary>
public sealed class PdfFontCache
{
    private readonly PdfDocument _document;
    private readonly Dictionary<long, PdfFont?> _cache = new();

    public PdfFontCache(PdfDocument document) => _document = document;

    public PdfFont? Get(PdfObject? fontValue)
    {
        var dictionary = _document.ResolveDictionary(fontValue);
        if (dictionary is null) return null;

        long key = fontValue is PdfReference reference
            ? ((long)reference.Number << 16) | (uint)reference.Generation
            : -dictionary.Items.Count - 1;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        PdfFont? font;
        try
        {
            font = Build(dictionary);
        }
        catch (PdfException)
        {
            font = null;
        }

        _cache[key] = font;
        return font;
    }

    private PdfFont? Build(PdfDictionary dictionary)
    {
        string? subtype = dictionary.GetNameValue("Subtype");
        string name = dictionary.GetNameValue("BaseFont") ?? "Font";
        var descriptor = ResolveDescriptor(dictionary);
        var toUnicode = ReadToUnicode(dictionary);

        return subtype switch
        {
            "Type0" => BuildComposite(dictionary, name, descriptor, toUnicode),
            "Type1" or "TrueType" or "MMType1" => BuildSimple(dictionary, name, descriptor, toUnicode),
            "Type3" => null,
            _ => BuildSimple(dictionary, name, descriptor, toUnicode)
        };
    }

    private PdfDictionary? ResolveDescriptor(PdfDictionary dictionary)
    {
        var direct = _document.ResolveDictionary(dictionary.Raw("FontDescriptor"));
        if (direct is not null) return direct;
        var descendants = _document.ResolveArray(dictionary.Raw("DescendantFonts"));
        var descendant = _document.ResolveDictionary(descendants?.At(0));
        return _document.ResolveDictionary(descendant?.Raw("FontDescriptor"));
    }

    private PdfCMap? ReadToUnicode(PdfDictionary dictionary)
    {
        var stream = _document.ResolveStream(dictionary.Raw("ToUnicode"));
        if (stream is null) return null;
        try
        {
            return PdfCMap.Parse(stream.Decoded(), 2);
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private PdfFont? BuildSimple(
        PdfDictionary dictionary, string name, PdfDictionary? descriptor, PdfCMap? toUnicode)
    {
        double[] widths = Array.Empty<double>();
        var widthArray = _document.ResolveArray(dictionary.Raw("Widths"));
        if (widthArray is not null)
        {
            widths = new double[widthArray.Count];
            for (int i = 0; i < widthArray.Count; i++)
            {
                widths[i] = _document.ResolveNumber(widthArray.At(i))?.Value ?? 0;
            }
        }

        int firstChar = (int)(dictionary.GetNumber("FirstChar") ?? 0);
        double missingWidth = descriptor?.GetNumber("MissingWidth") ?? 0;

        string? encodingName = null;
        var differences = new Dictionary<int, string>();
        var encoding = _document.Resolve(dictionary.Raw("Encoding"));
        switch (encoding)
        {
            case PdfName direct:
                encodingName = direct.Value;
                break;
            case PdfDictionary encodingDict:
                encodingName = encodingDict.GetNameValue("BaseEncoding");
                ReadDifferences(encodingDict, differences);
                break;
        }

        bool symbolic = false;
        if (descriptor?.Raw("Flags") is PdfNumber flags) symbolic = (flags.ToInt() & 4) != 0;

        var (ascent, descent) = ReadMetrics(descriptor, 800, -200);
        var glyphSource = CreateGlyphSource(descriptor, name);
        return new PdfSimpleFont(
            name, toUnicode, encodingName, differences, widths, firstChar, missingWidth,
            glyphSource, ascent, descent, symbolic);
    }

    private void ReadDifferences(PdfDictionary encodingDict, Dictionary<int, string> differences)
    {
        var array = _document.ResolveArray(encodingDict.Raw("Differences"));
        if (array is null) return;
        int current = 0;
        foreach (var item in array.Items)
        {
            var resolved = _document.Resolve(item);
            switch (resolved)
            {
                case PdfNumber number:
                    current = number.ToInt();
                    break;
                case PdfName glyphName:
                    differences[current++] = glyphName.Value;
                    break;
            }
        }
    }

    private PdfFont? BuildComposite(
        PdfDictionary dictionary, string name, PdfDictionary? descriptor, PdfCMap? toUnicode)
    {
        var descendants = _document.ResolveArray(dictionary.Raw("DescendantFonts"));
        var descendant = _document.ResolveDictionary(descendants?.At(0));
        if (descendant is null) return null;
        if (descriptor is null) descriptor = _document.ResolveDictionary(descendant.Raw("FontDescriptor"));
        string? cidSubtype = descendant.GetNameValue("Subtype");
        bool isCidType2 = cidSubtype == "CIDFontType2";

        var widths = new Dictionary<int, double>();
        ReadCidWidths(descendant, widths);
        double defaultWidth = descendant.GetNumber("DW") ?? 1000;

        var encoding = _document.Resolve(dictionary.Raw("Encoding"));
        PdfCMap? cidMap = null;
        bool vertical = false;
        int codeLength = 2;
        switch (encoding)
        {
            case PdfName encodingName:
                vertical = encodingName.Value.EndsWith("-V", StringComparison.Ordinal);
                if (encodingName.Value is "Identity-H" or "Identity-V"
                    || encodingName.Value.StartsWith("UniGB", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniJIS", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniKS", StringComparison.Ordinal)
                    || encodingName.Value.StartsWith("UniCNS", StringComparison.Ordinal))
                {
                    codeLength = 2;
                }
                else
                {
                    // CMap bawaan (mis. 90ms-RKSJ-H) tidak dibundel; kode 2 byte tetap
                    // dibaca dan teks diambil dari /ToUnicode bila ada.
                    codeLength = 2;
                }

                break;
            case PdfStream cmapStream:
            {
                var parsed = PdfCMap.Parse(cmapStream.Decoded(), 2);
                cidMap = parsed;
                codeLength = parsed.CodeLength is 1 or 2 or 4 ? parsed.CodeLength : 2;
                break;
            }
        }

        ushort[]? cidToGid = null;
        if (isCidType2)
        {
            var map = _document.Resolve(descendant.Raw("CIDToGIDMap"));
            if (map is PdfName mapName && mapName.Value == "Identity")
            {
                cidToGid = null;
            }
            else if (map is PdfStream mapStream)
            {
                byte[] bytes = mapStream.Decoded();
                cidToGid = new ushort[bytes.Length / 2];
                for (int i = 0; i < cidToGid.Length; i++)
                {
                    cidToGid[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
                }
            }
        }

        var (ascent, descent) = ReadMetrics(descriptor, 880, -220);
        bool symbolic = false;
        if (descriptor?.Raw("Flags") is PdfNumber flagsNumber) symbolic = (flagsNumber.ToInt() & 4) != 0;
        var glyphSource = CreateGlyphSource(descriptor, name);

        return new PdfCompositeFont(
            name, toUnicode, cidMap, widths, defaultWidth, glyphSource, cidToGid, codeLength,
            vertical, symbolic, ascent, descent);
    }

    private void ReadCidWidths(PdfDictionary descendant, Dictionary<int, double> widths)
    {
        var array = _document.ResolveArray(descendant.Raw("W"));
        if (array is null) return;

        int index = 0;
        while (index < array.Count)
        {
            int first = (int)(_document.ResolveNumber(array.At(index))?.Value ?? 0);
            index++;
            if (index >= array.Count) break;

            var next = _document.Resolve(array.At(index));
            if (next is PdfArray list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    widths[first + i] = _document.ResolveNumber(list.At(i))?.Value ?? 0;
                }

                index++;
                continue;
            }

            if (next is PdfNumber lastNumber)
            {
                index++;
                if (index >= array.Count) break;
                double width = _document.ResolveNumber(array.At(index))?.Value ?? 0;
                index++;
                int last = lastNumber.ToInt();
                for (int cid = first; cid <= last && cid - first < 65536; cid++) widths[cid] = width;
                continue;
            }

            index++;
        }
    }

    private (double Ascent, double Descent) ReadMetrics(PdfDictionary? descriptor, double defaultAscent, double defaultDescent)
    {
        if (descriptor is null) return (defaultAscent, defaultDescent);
        double ascent = descriptor.GetNumber("Ascent") ?? defaultAscent;
        double descent = descriptor.GetNumber("Descent") ?? defaultDescent;
        return (ascent, descent);
    }

    private IPdfGlyphSource? CreateGlyphSource(PdfDictionary? descriptor, string baseFontName)
    {
        // Font standar tanpa berkas tersemat (mis. Times-Roman pada surat yang
        // ditulis SudesPdf): bentuk hurufnya dipinjam dari font sistem, sama
        // seperti pembaca PDF lain — tidak ada berkas font yang didistribusikan.
        if (descriptor is null || !HasEmbeddedProgram(descriptor))
        {
            return PdfStandardFontSource.Resolve(baseFontName);
        }

        var program = _document.Resolve(descriptor.Raw("FontFile2"));
        if (program is PdfStream truetype)
        {
            try
            {
                return TrueTypeFont.Parse(truetype.Decoded());
            }
            catch (PdfException)
            {
                return null;
            }
        }

        var cff = _document.Resolve(descriptor.Raw("FontFile3"));
        if (cff is PdfStream cffStream)
        {
            try
            {
                return CffFont.Parse(cffStream.Decoded(), cffStream.Dictionary.GetNameValue("Subtype"));
            }
            catch (PdfException)
            {
                return null;
            }
        }

        // FontFile (Type1 mentah) belum didukung; teksnya tetap terbaca lewat
        // /ToUnicode dan lebar /Widths.
        return null;
    }

    /// <summary>Benar bila font membawa program huruf tersemat.</summary>
    private static bool HasEmbeddedProgram(PdfDictionary descriptor) =>
        descriptor.Raw("FontFile") is not (null or PdfNull)
        || descriptor.Raw("FontFile2") is not (null or PdfNull)
        || descriptor.Raw("FontFile3") is not (null or PdfNull);
}

