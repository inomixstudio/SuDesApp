using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SudesPdf.Core;

/// <summary>Nilai dasar PDF (ISO 32000-1 §7.3).</summary>
public abstract class PdfObject
{
    public static readonly PdfNull Null = new();

    public virtual bool IsNull => false;
}

public sealed class PdfNull : PdfObject
{
    public override bool IsNull => true;
}

public sealed class PdfBoolean : PdfObject
{
    public static readonly PdfBoolean True = new(true);
    public static readonly PdfBoolean False = new(false);

    public PdfBoolean(bool value) => Value = value;

    public bool Value { get; }
}

public sealed class PdfNumber : PdfObject
{
    public PdfNumber(double value) => Value = value;

    public double Value { get; }

    public int ToInt() => (int)Math.Round(Value, MidpointRounding.AwayFromZero);

    public override string ToString() => Value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed class PdfName : PdfObject
{
    public PdfName(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => "/" + Value;
}

public sealed class PdfString : PdfObject
{
    private byte[] _bytes;

    public PdfString(byte[] bytes) => _bytes = bytes;

    /// <summary>Isi mentah (belum didekripsi untuk dokumen terproteksi).</summary>
    public byte[] Bytes => _bytes;

    internal void SetBytes(byte[] bytes) => _bytes = bytes;

    /// <summary>Teks Latin-1; PDFDocEncoding/UTF-16 ditangani <see cref="PdfText.DecodePdfString"/>.</summary>
    public string Text => Encoding.Latin1.GetString(Bytes);

    public override string ToString() => Text;
}

public sealed class PdfArray : PdfObject
{
    public PdfArray() => Items = new List<PdfObject>();

    public PdfArray(List<PdfObject> items) => Items = items;

    public List<PdfObject> Items { get; }

    public int Count => Items.Count;

    public PdfObject At(int index) => index >= 0 && index < Items.Count ? Items[index] : Null;

    public override string ToString() => $"[{Items.Count} item]";
}

public sealed class PdfDictionary : PdfObject
{
    public PdfDictionary() => Items = new Dictionary<string, PdfObject>(StringComparer.Ordinal);

    public Dictionary<string, PdfObject> Items { get; }

    public int Count => Items.Count;

    public bool ContainsKey(string key) => Items.ContainsKey(key);

    public PdfObject? Raw(string key) => Items.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, PdfObject value) => Items[key] = value;

    public PdfName? GetName(string key) => Raw(key) as PdfName;

    public string? GetNameValue(string key) => (Raw(key) as PdfName)?.Value;

    public double? GetNumber(string key) => (Raw(key) as PdfNumber)?.Value;

    public double GetNumberOrDefault(string key, double fallback) => (Raw(key) as PdfNumber)?.Value ?? fallback;

    public int? GetInt(string key)
    {
        return Raw(key) is PdfNumber n ? n.ToInt() : null;
    }

    public bool? GetBool(string key)
    {
        return Raw(key) is PdfBoolean b ? b.Value : null;
    }

    public PdfArray? GetArray(string key) => Raw(key) as PdfArray;

    public PdfDictionary? GetDictionary(string key) => Raw(key) as PdfDictionary;

    public PdfStream? GetStream(string key) => Raw(key) as PdfStream;

    public override string ToString() => $"<{Items.Count} kunci>";
}

/// <summary>Stream PDF: kamus + data terenkode (belum melalui filter).</summary>
public sealed class PdfStream : PdfObject
{
    private byte[]? _decoded;

    public PdfStream(PdfDictionary dictionary, byte[] data)
    {
        Dictionary = dictionary;
        Data = data;
    }

    public PdfDictionary Dictionary { get; }

    /// <summary>Data mentah seperti tersimpan di berkas (masih terenkripsi/terkompresi).</summary>
    public byte[] Data { get; }

    public PdfDocument? Owner { get; internal set; }

    /// <summary>Nomor objek stream ini, dipakai kunci dekripsi per objek.</summary>
    public int ObjectNumber { get; internal set; }

    public int Generation { get; internal set; }

    public byte[] Decoded()
    {
        if (_decoded is not null) return _decoded;
        if (Owner is null) return Data;
        _decoded = Owner.DecodeStream(this);
        return _decoded;
    }

    public override string ToString() => $"stream {Data.Length} byte {Dictionary}";
}

public sealed class PdfReference : PdfObject
{
    public PdfReference(int number, int generation)
    {
        Number = number;
        Generation = generation;
    }

    public int Number { get; }

    public int Generation { get; }

    public override string ToString() => $"{Number} {Generation} R";
}

/// <summary>Berkas PDF rusak atau memakai fitur yang tidak didukung mesin ini.</summary>
public class PdfException : Exception
{
    public PdfException(string message)
        : base(message)
    {
    }

    public PdfException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public static class PdfObjectExtensions
{
    /// <summary>Kamus dari nilai apa pun yang bisa berupa referensi.</summary>
    public static PdfDictionary? AsDictionary(this PdfObject? value) => value switch
    {
        PdfDictionary dict => dict,
        PdfStream stream => stream.Dictionary,
        _ => null
    };

    public static string Describe(this PdfObject? value) => value switch
    {
        null => "(kosong)",
        PdfNull => "null",
        PdfName name => "/" + name.Value,
        PdfNumber number => number.ToString(),
        _ => value.GetType().Name
    };
}
