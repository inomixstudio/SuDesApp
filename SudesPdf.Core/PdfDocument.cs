using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Satu halaman PDF beserta atribut warisannya.</summary>
public sealed class PdfPage
{
    internal PdfPage(PdfDocument document, PdfDictionary dictionary, int index)
    {
        Document = document;
        Dictionary = dictionary;
        Index = index;
    }

    public PdfDocument Document { get; }

    public PdfDictionary Dictionary { get; }

    public int Index { get; }

    public PdfRect MediaBox { get; internal set; }

    public PdfRect CropBox { get; internal set; }

    /// <summary>Rotasi tampilan halaman dalam derajat (kelipatan 90).</summary>
    public int Rotation { get; internal set; }

    public double Width => CropBox.Width;

    public double Height => CropBox.Height;

    public PdfDictionary? Resources => ResolveInherited("Resources");

    public PdfObject? ContentsRaw => Dictionary.Raw("Contents");

    internal PdfDictionary? ResolveInherited(string key) => Document.ResolveDictionary(Dictionary.Raw(key));

    public override string ToString() => $"Halaman {Index + 1} ({Width:0.#}x{Height:0.#}pt)";
}

/// <summary>Galat yang muncul saat PDF memerlukan kata sandi.</summary>
public sealed class PdfPasswordException : PdfException
{
    public PdfPasswordException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Berkas PDF yang sudah diurai: tabel xref (klasik/stream), object stream,
/// pohon halaman, dan dekoder stream. Semua penulisan ulang file dilakukan di
/// kelas lain; kelas ini hanya membaca.
/// </summary>
public sealed class PdfDocument
{
    private readonly byte[] _data;
    private readonly Dictionary<long, PdfObject?> _cache = new();
    private readonly List<PdfPage> _pages = new();
    private Dictionary<int, XrefEntry>? _xref;
    private Dictionary<int, int>? _scanIndex;
    private bool _pagesLoaded;

    private PdfDocument(byte[] data)
    {
        _data = data;
    }

    private readonly record struct XrefEntry(int Type, int Field1, int Field2);

    public byte[] Data => _data;

    public string Version { get; private set; } = "1.4";

    public PdfDictionary Trailer { get; private set; } = new();

    public PdfDictionary Catalog { get; private set; } = new();

    public PdfEncryption? Encryption { get; private set; }

    public bool IsEncrypted => Encryption is not null;

    public static PdfDocument Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new PdfException("Berkas PDF kosong.");
        var document = new PdfDocument(data);
        document.Initialize();
        return document;
    }

    public static PdfDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path));
    }

    public IReadOnlyList<PdfPage> Pages
    {
        get
        {
            EnsurePages();
            return _pages;
        }
    }

    public int PageCount
    {
        get
        {
            EnsurePages();
            return _pages.Count;
        }
    }

    // ------------------------------------------------------------- Inisialisasi

    private void Initialize()
    {
        ReadHeader();
        PdfDictionary? trailer = null;
        try
        {
            _xref = new Dictionary<int, XrefEntry>();
            trailer = ReadCrossReferences();
        }
        catch (PdfException)
        {
            _xref = null;
            trailer = null;
        }

        // Berkas cacat (xref meleset, tanpa xref, atau stream objek rusak) tetap
        // dibuka dengan memindai seluruh isi berkas — cara yang sama dipakai
        // pemulihan (recovery) pembaca PDF besar.
        if (trailer is null || !trailer.ContainsKey("Root"))
        {
            trailer = RebuildTrailer();
        }

        Trailer = trailer;

        var encrypt = Resolve(trailer.Raw("Encrypt"));
        if (encrypt is PdfDictionary encryptDict)
        {
            Encryption = PdfEncryption.Create(encryptDict, this);
        }

        var root = ResolveDictionary(trailer.Raw("Root"));
        if (root is null)
        {
            root = FindCatalogByScan();
        }

        Catalog = root ?? throw new PdfException("Berkas bukan PDF yang sah: katalog dokumen tidak ditemukan.");
    }

    private void ReadHeader()
    {
        int limit = Math.Min(_data.Length, 1024);
        string head = Encoding.Latin1.GetString(_data, 0, limit);
        int marker = head.IndexOf("%PDF-", StringComparison.Ordinal);
        if (marker < 0) throw new PdfException("Berkas bukan PDF (tanda %PDF- tidak ditemukan).");
        int end = marker + 5;
        while (end < head.Length && (char.IsDigit(head[end]) || head[end] == '.')) end++;
        Version = head[(marker + 5)..end].Trim();
        if (Version.Length == 0) Version = "1.4";
    }

    private int FindStartXref()
    {
        int from = Math.Max(0, _data.Length - 2048);
        for (int i = _data.Length - 9; i >= from; i--)
        {
            if (_data[i] == (byte)'s' && Matches(i, "startxref"))
            {
                var lexer = new PdfLexer(_data, i + 9);
                if (lexer.TryReadToken(out var token) && token.Type == PdfTokenType.Number)
                {
                    return (int)token.Number;
                }
            }
        }

        return -1;
    }

    private bool Matches(int offset, string text)
    {
        if (offset + text.Length > _data.Length) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (_data[offset + i] != (byte)text[i]) return false;
        }

        return true;
    }

    private PdfDictionary? ReadCrossReferences()
    {
        int start = FindStartXref();
        PdfDictionary? trailer = null;
        if (start < 0) return null;

        var queue = new Stack<int>();
        var visited = new HashSet<int>();
        queue.Push(start);

        while (queue.Count > 0)
        {
            int offset = queue.Pop();
            if (!visited.Add(offset)) continue;
            if (offset <= 0 || offset >= _data.Length) continue;

            PdfDictionary? candidate;
            if (Matches(offset, "xref"))
            {
                candidate = ReadClassicXref(offset);
            }
            else
            {
                candidate = ReadXrefStream(offset);
            }

            if (candidate is null) continue;

            if (trailer is null || !trailer.ContainsKey("Root"))
            {
                foreach (var pair in candidate.Items)
                {
                    if (!trailer?.ContainsKey(pair.Key) ?? true)
                    {
                        trailer ??= new PdfDictionary();
                        trailer.Set(pair.Key, pair.Value);
                    }
                }
            }

            int previous = (int)(candidate.GetNumber("Prev") ?? -1);
            if (previous > 0) queue.Push(previous);
            int hybrid = (int)(candidate.GetNumber("XRefStm") ?? -1);
            if (hybrid > 0) queue.Push(hybrid);
        }

        return trailer;
    }

    private PdfDictionary? ReadClassicXref(int offset)
    {
        var lexer = new PdfLexer(_data, offset + 4);
        while (true)
        {
            lexer.SkipWhitespace();
            int save = lexer.Position;
            if (!lexer.TryReadToken(out var first)) return null;

            if (first.Type == PdfTokenType.Keyword && first.Name == "trailer")
            {
                var parser = new PdfParser(lexer);
                return parser.ParseValue() as PdfDictionary;
            }

            if (first.Type != PdfTokenType.Number) return null;
            if (!lexer.TryReadToken(out var countToken) || countToken.Type != PdfTokenType.Number)
            {
                return null;
            }

            int startObject = (int)first.Number;
            int count = (int)countToken.Number;
            for (int i = 0; i < count; i++)
            {
                lexer.SkipWhitespace();
                if (!lexer.TryReadToken(out var offsetToken) || offsetToken.Type != PdfTokenType.Number) break;
                if (!lexer.TryReadToken(out var genToken) || genToken.Type != PdfTokenType.Number) break;
                if (!lexer.TryReadToken(out var typeToken)) break;
                bool free = typeToken.Type == PdfTokenType.Keyword && typeToken.Name == "f";
                if (!free && _xref is not null)
                {
                    int number = startObject + i;
                    if (!_xref.ContainsKey(number))
                    {
                        _xref[number] = new XrefEntry(1, (int)offsetToken.Number, (int)genToken.Number);
                    }
                }
            }

            _ = save;
        }
    }

    private PdfDictionary? ReadXrefStream(int offset)
    {
        var value = PdfIndirectReader.Read(_data, offset, out _, 0, 0);
        if (value is not PdfStream stream) return null;
        var dict = stream.Dictionary;
        if (dict.GetNameValue("Type") != "XRef") return null;
        stream.Owner = this;

        var widths = ResolveArray(dict.Raw("W"));
        if (widths is null || widths.Count < 3) return null;
        int w0 = (int)NumberAt(widths, 0);
        int w1 = (int)NumberAt(widths, 1);
        int w2 = (int)NumberAt(widths, 2);

        var index = ResolveArray(dict.Raw("Index"));
        var ranges = new List<(int Start, int Count)>();
        if (index is not null && index.Count >= 2)
        {
            for (int i = 0; i + 1 < index.Count; i += 2)
            {
                ranges.Add(((int)NumberAt(index, i), (int)NumberAt(index, i + 1)));
            }
        }
        else
        {
            ranges.Add((0, (int)(dict.GetNumber("Size") ?? 0)));
        }

        byte[] data = stream.Decoded();
        int position = 0;
        int entrySize = w0 + w1 + w2;
        if (entrySize <= 0) return null;

        foreach (var (rangeStart, rangeCount) in ranges)
        {
            for (int i = 0; i < rangeCount; i++)
            {
                if (position + entrySize > data.Length) break;
                int type = w0 == 0 ? 1 : ReadBigEndian(data, ref position, w0);
                int field1 = ReadBigEndian(data, ref position, w1);
                int field2 = ReadBigEndian(data, ref position, w2);
                int number = rangeStart + i;
                if (_xref is not null && !_xref.ContainsKey(number))
                {
                    _xref[number] = new XrefEntry(type, field1, field2);
                }
            }
        }

        return dict;
    }

    /// <summary>Angka pada posisi tertentu dari larik PDF (referensi diselesaikan).</summary>
    private double NumberAt(PdfArray array, int index) => ResolveNumber(array.At(index))?.Value ?? 0;

    private static int ReadBigEndian(byte[] data, ref int position, int width)
    {
        int value = 0;
        for (int i = 0; i < width; i++)
        {
            value = (value << 8) | data[position++];
        }

        return value;
    }

    /// <summary>Pemulihan: pindai "N G obj" di seluruh berkas dan cari katalog.</summary>
    private PdfDictionary RebuildTrailer()
    {
        var trailer = new PdfDictionary();
        var rootRef = FindCatalogReferenceByScan();
        if (rootRef is not null) trailer.Set("Root", rootRef);
        else
        {
            // Trailer masih bisa tertulis walau objeknya tidak ditemukan.
            int index = LastIndexOf("trailer");
            if (index >= 0)
            {
                var parser = new PdfParser(new PdfLexer(_data, index + 7));
                if (parser.ParseValue() is PdfDictionary parsed) return parsed;
            }
        }

        return trailer;
    }

    private PdfReference? FindCatalogReferenceByScan()
    {
        // Pilih objek yang kelihatan katalog: /Type /Catalog, dan utamakan yang
        // punya /Pages (lebih lengkap dari katalog bertanda tangan).
        var index = BuildScanIndex();
        PdfReference? best = null;
        int bestScore = -1;
        foreach (var pair in index)
        {
            var value = PdfIndirectReader.Read(_data, pair.Value, out _, pair.Key, 0);
            if (value is not PdfDictionary dict) continue;
            if (dict.GetNameValue("Type") != "Catalog") continue;
            int score = dict.ContainsKey("Pages") ? 2 : 1;
            if (score > bestScore)
            {
                bestScore = score;
                best = new PdfReference(pair.Key, 0);
            }
        }

        return best;
    }

    private PdfDictionary? FindCatalogByScan()
    {
        var reference = FindCatalogReferenceByScan();
        return reference is null ? null : ResolveDictionary(reference);
    }

    private Dictionary<int, int> BuildScanIndex()
    {
        if (_scanIndex is not null) return _scanIndex;

        var index = new Dictionary<int, int>();
        for (int i = 0; i < _data.Length; i++)
        {
            if (_data[i] != (byte)'o') continue;
            if (!Matches(i, "obj")) continue;

            // Baca mundur: "gen" lalu "nomor".
            int cursor = i - 1;
            while (cursor >= 0 && PdfLexer.IsWhitespace(_data[cursor])) cursor--;
            int endGen = cursor;
            while (cursor >= 0 && _data[cursor] >= (byte)'0' && _data[cursor] <= (byte)'9') cursor--;
            if (cursor == endGen) continue;
            while (cursor >= 0 && PdfLexer.IsWhitespace(_data[cursor])) cursor--;
            int endNumber = cursor;
            while (cursor >= 0 && _data[cursor] >= (byte)'0' && _data[cursor] <= (byte)'9') cursor--;
            if (cursor == endNumber) continue;

            string numberText = Encoding.Latin1.GetString(_data, cursor + 1, endNumber - cursor);
            if (!int.TryParse(numberText, out int number)) continue;
            index.TryAdd(number, cursor + 1);
        }

        _scanIndex = index;
        return index;
    }

    private int LastIndexOf(string text)
    {
        for (int i = _data.Length - text.Length; i >= 0; i--)
        {
            if (Matches(i, text)) return i;
        }

        return -1;
    }

    // ---------------------------------------------------------------- Objek

    public PdfObject? Resolve(PdfObject? value)
    {
        int guard = 0;
        while (value is PdfReference reference && guard++ < 64)
        {
            value = GetObject(reference.Number, reference.Generation);
        }

        return value;
    }

    public T? ResolveAs<T>(PdfObject? value)
        where T : PdfObject
    {
        var resolved = Resolve(value);
        return resolved as T;
    }

    public PdfDictionary? ResolveDictionary(PdfObject? value) => Resolve(value) switch
    {
        PdfDictionary dict => dict,
        PdfStream stream => stream.Dictionary,
        _ => null
    };

    public PdfArray? ResolveArray(PdfObject? value) => Resolve(value) as PdfArray;

    public PdfNumber? ResolveNumber(PdfObject? value) => Resolve(value) as PdfNumber;

    public double GetNumber(PdfDictionary dict, string key, double fallback = 0)
        => ResolveNumber(dict.Raw(key))?.Value ?? fallback;

    public PdfObject? GetObject(int number, int generation)
    {
        if (number <= 0) return null;
        long key = ((long)number << 16) | (uint)generation;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var value = ReadObject(number, generation);
        _cache[key] = value;
        return value;
    }

    private PdfObject? ReadObject(int number, int generation)
    {
        if (_xref is not null && _xref.TryGetValue(number, out var entry))
        {
            switch (entry.Type)
            {
                case 1:
                {
                    var value = PdfIndirectReader.Read(_data, entry.Field1, out _, number, entry.Field2);
                    if (value is not null) return PostProcess(number, entry.Field2, value);
                    break;
                }

                case 2:
                {
                    var value = ReadFromObjectStream(entry.Field1, entry.Field2);
                    if (value is not null) return PostProcess(number, 0, value);
                    break;
                }
            }
        }

        // Cadangan: pindai berkas (xref hilang/meleset).
        var index = BuildScanIndex();
        if (index.TryGetValue(number, out int offset))
        {
            var value = PdfIndirectReader.Read(_data, offset, out _, number, generation);
            if (value is not null) return PostProcess(number, generation, value);
        }

        return null;
    }

    private PdfObject? PostProcess(int number, int generation, PdfObject value)
    {
        if (value is PdfStream stream)
        {
            stream.Owner = this;
            stream.ObjectNumber = number;
            stream.Generation = generation;
        }

        if (Encryption is not null)
        {
            PdfEncryption.DecryptStrings(value, number, generation, Encryption, this);
        }

        return value;
    }

    private PdfObject? ReadFromObjectStream(int streamNumber, int index)
    {
        var container = GetObject(streamNumber, 0) as PdfStream;
        if (container is null) return null;
        var dict = container.Dictionary;
        if (dict.GetNameValue("Type") != "ObjStm") return null;

        int count = (int)(dict.GetNumber("N") ?? 0);
        int first = (int)(dict.GetNumber("First") ?? 0);
        byte[] data = container.Decoded();
        if (count <= 0 || first <= 0 || first > data.Length) return null;

        var lexer = new PdfLexer(data, 0, first);
        var numbers = new int[count];
        var offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (!lexer.TryReadToken(out var numberToken) || numberToken.Type != PdfTokenType.Number) return null;
            if (!lexer.TryReadToken(out var offsetToken) || offsetToken.Type != PdfTokenType.Number) return null;
            numbers[i] = (int)numberToken.Number;
            offsets[i] = (int)offsetToken.Number;
        }

        if (index < 0 || index >= count) return null;
        int start = first + offsets[index];
        int end = index + 1 < count ? first + offsets[index + 1] : data.Length;
        if (start < 0 || start > data.Length) return null;
        end = Math.Clamp(end, start, data.Length);

        var parser = new PdfParser(new PdfLexer(data, start, end));
        return parser.TryParseValue(out var value) ? value : null;
    }

    // -------------------------------------------------------------- Stream

    public byte[] DecodeStream(PdfStream stream)
    {
        byte[] raw = stream.Data;
        bool metadataExempt = Encryption is not null && !Encryption.EncryptMetadata
            && stream.Dictionary.GetNameValue("Type") == "Metadata";

        if (Encryption is not null && !metadataExempt)
        {
            string? type = stream.Dictionary.GetNameValue("Type");
            if (type is not "XRef")
            {
                raw = Encryption.DecryptStream(raw, stream.ObjectNumber, stream.Generation);
            }
        }

        var filters = PdfFilters.FiltersOf(stream.Dictionary);
        if (filters.Length == 0) return raw;

        // Filter gambar di akhir rantai (DCT/JPX/JBIG2/CCITT) bukan urusan
        // dekompresi teks: sisakan berkas gambarnya apa adanya.
        int textual = filters.Length;
        while (textual > 0 && PdfFilters.IsImageFilter(PdfFilters.Normalize(filters[textual - 1]))) textual--;

        var usable = new List<string>(textual);
        for (int i = 0; i < textual; i++) usable.Add(filters[i]);

        var parms = PdfFilters.ParmsOf(stream.Dictionary, filters.Length);
        var usableParms = new List<PdfDictionary?>(textual);
        for (int i = 0; i < textual; i++) usableParms.Add(i < parms.Length ? parms[i] : null);

        if (usable.Count == 0) return raw;
        return PdfFilters.Apply(raw, usable, usableParms);
    }

    /// <summary>Ambil stream dari nilai apa pun (referensi, kamus, atau stream).</summary>
    public PdfStream? ResolveStream(PdfObject? value) => Resolve(value) as PdfStream;

    // -------------------------------------------------------------- Halaman

    private void EnsurePages()
    {
        if (_pagesLoaded) return;
        _pagesLoaded = true;

        var pagesNode = ResolveDictionary(Catalog.Raw("Pages"));
        if (pagesNode is null) return;

        var visited = new HashSet<int>();
        Walk(pagesNode, null, visited, 0);
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            page.MediaBox = ResolveBox(page.Dictionary, "MediaBox", page) ?? new PdfRect(0, 0, 612, 792);
            page.CropBox = ResolveBox(page.Dictionary, "CropBox", page) ?? page.MediaBox;
            if (page.CropBox.IsEmpty) page.CropBox = page.MediaBox;
            int rotation = (int)(ResolveNumber(page.Dictionary.Raw("Rotate"))?.Value ?? 0);
            page.Rotation = ((rotation % 360) + 360) % 360;
        }
    }

    private PdfRect? ResolveBox(PdfDictionary page, string key, PdfPage owner)
    {
        var array = ResolveArray(page.Raw(key));
        if (array is null || array.Count < 4) return null;
        double x0 = ResolveNumber(array.At(0))?.Value ?? 0;
        double y0 = ResolveNumber(array.At(1))?.Value ?? 0;
        double x1 = ResolveNumber(array.At(2))?.Value ?? 0;
        double y1 = ResolveNumber(array.At(3))?.Value ?? 0;
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        _ = owner;
        return PdfRect.FromEdges(x0, y0, x1, y1);
    }

    private void Walk(PdfDictionary node, PdfDictionary? inherited, HashSet<int> visited, int depth)
    {
        if (depth > 64) return;
        var merged = MergeInherited(inherited, node);
        string? type = node.GetNameValue("Type");

        var kids = ResolveArray(node.Raw("Kids"));
        if (type == "Page" || (kids is null && node.ContainsKey("Contents")))
        {
            _pages.Add(new PdfPage(this, node, _pages.Count));
            return;
        }

        if (kids is null)
        {
            if (node.ContainsKey("Contents")) _pages.Add(new PdfPage(this, node, _pages.Count));
            return;
        }

        foreach (var kid in kids.Items)
        {
            if (kid is PdfReference reference)
            {
                if (!visited.Add(reference.Number)) continue;
                var child = ResolveDictionary(kid);
                if (child is not null) Walk(child, merged, visited, depth + 1);
                continue;
            }

            if (ResolveDictionary(kid) is { } inlineChild) Walk(inlineChild, merged, visited, depth + 1);
        }
    }

    private PdfDictionary MergeInherited(PdfDictionary? inherited, PdfDictionary node)
    {
        if (inherited is null) return node;
        var merged = new PdfDictionary();
        foreach (var pair in inherited.Items) merged.Set(pair.Key, pair.Value);
        foreach (var pair in node.Items) merged.Set(pair.Key, pair.Value);
        return merged;
    }

    // ------------------------------------------------------------- Metadata

    public string? GetInfoString(string key)
    {
        var info = ResolveDictionary(Trailer.Raw("Info"));
        var value = Resolve(info?.Raw(key));
        return value is PdfString text ? DecodeTextString(text) : null;
    }

    public string? GetCatalogString(string key)
    {
        var value = Resolve(Catalog.Raw(key));
        return value is PdfString text ? DecodeTextString(text) : null;
    }

    private static string DecodeTextString(PdfString text) => PdfText.DecodePdfString(text.Bytes);

    /// <summary>Penanda berkas (/ID[0]) yang dipakai kunci enkripsi dan cache.</summary>
    public byte[] DocumentId
    {
        get
        {
            var ids = ResolveArray(Trailer.Raw("ID"));
            var first = Resolve(ids?.At(0));
            return first is PdfString text ? text.Bytes : Array.Empty<byte>();
        }
    }

    public override string ToString() =>
        $"PDF {Version}{(IsEncrypted ? " (terproteksi)" : string.Empty)}, {PageCount} halaman";

    internal static string FormatNumber(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
