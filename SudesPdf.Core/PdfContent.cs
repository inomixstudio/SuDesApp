using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

public enum PdfBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion
}

/// <summary>Warna siap pakai pada ruang perangkat.</summary>
public struct PdfPaint
{
    public double R;
    public double G;
    public double B;
    public double Alpha;

    public PdfTilingPattern? Pattern;

    /// <summary>Matriks ruang pola ke ruang perangkat (diisi penafsir).</summary>
    public PdfMatrix PatternMatrix;

    public readonly bool IsPattern => Pattern is not null;

    public static PdfPaint FromRgb(double r, double g, double b, double alpha) => new()
    {
        R = r,
        G = g,
        B = b,
        Alpha = alpha
    };
}

/// <summary>
/// Satu kemunculan teks: dipakai untuk menggambar glif (lewat matriks glif)
/// sekaligus membaca isi halaman (lewat posisi tiap glif).
/// </summary>
public sealed class PdfTextRun
{
    public required PdfFont Font { get; init; }

    /// <summary>Ukuran huruf efektif pada ruang perangkat.</summary>
    public double FontSize { get; init; }

    /// <summary>Teks Unicode dari glif yang ditampilkan.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Matriks dari satuan glif font ke ruang perangkat.</summary>
    public PdfMatrix GlyphMatrix { get; init; } = PdfMatrix.Identity;

    /// <summary>Nomor glif tiap kode; -1 bila glif tidak tersedia.</summary>
    public int[] GlyphIds { get; init; } = Array.Empty<int>();

    /// <summary>Posisi absis tiap glif di ruang perangkat.</summary>
    public double[] GlyphX { get; init; } = Array.Empty<double>();

    /// <summary>Posisi ordinat tiap glif di ruang perangkat.</summary>
    public double[] GlyphY { get; init; } = Array.Empty<double>();

    /// <summary>Perpindahan tiap glif (besaran pada arah teks).</summary>
    public double[] GlyphAdvance { get; init; } = Array.Empty<double>();

    /// <summary>Awal dan akhir teks pada ruang perangkat.</summary>
    public double StartX { get; init; }

    public double StartY { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    /// <summary>Arah penulisan satuan (untuk menyusun urutan baca).</summary>
    public double DirectionX { get; init; } = 1;

    public double DirectionY { get; init; }

    /// <summary>Mode tampilan teks PDF (0 isi, 1 garis, 2 dua-duanya, 3 tak terlihat).</summary>
    public int RenderMode { get; init; }

    public PdfPaint Fill { get; init; }

    public PdfPaint Stroke { get; init; }

    public double StrokeWidth { get; init; } = 1;
}

/// <summary>Tujuan penggambaran halaman (raster, ekstraksi teks, dsb.).</summary>
public interface IPdfSink
{
    void Save();

    void Restore();

    void Clip(PdfPath path, PdfFillRule rule);

    void Fill(PdfPath path, PdfFillRule rule, in PdfPaint paint);

    void Stroke(PdfPath path, in PdfStrokeSpec spec, in PdfPaint paint);

    void DrawImage(PdfImage image, in PdfMatrix matrix, double alpha, bool interpolate);

    void DrawShading(PdfShading shading, in PdfMatrix matrix, double alpha);

    void ShowText(PdfTextRun run);
}

/// <summary>
/// Jalur pada ruang pengguna dengan kurva utuh. Konversi ke ruang perangkat
/// dilakukan saat menggambar supaya kerataan kurva mengikuti skala keluaran.
/// </summary>
public sealed class PdfPathData
{
    private readonly List<double> _items = new(64);
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private bool _open;

    public bool IsEmpty => _items.Count == 0;

    public void Reset()
    {
        _items.Clear();
        _open = false;
        _x = _y = _startX = _startY = 0;
    }

    public void MoveTo(double x, double y)
    {
        _items.Add(0);
        _items.Add(x);
        _items.Add(y);
        _x = _startX = x;
        _y = _startY = y;
        _open = true;
    }

    public void LineTo(double x, double y)
    {
        if (!_open) MoveTo(_x, _y);
        _items.Add(1);
        _items.Add(x);
        _items.Add(y);
        _x = x;
        _y = y;
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!_open) MoveTo(_x, _y);
        _items.Add(3);
        _items.Add(x1);
        _items.Add(y1);
        _items.Add(x2);
        _items.Add(y2);
        _items.Add(x3);
        _items.Add(y3);
        _x = x3;
        _y = y3;
    }

    /// <summary>Operator 'v': titik kendali pertama adalah titik aktif saat ini.</summary>
    public void CurveToWithCurrentControl(double x2, double y2, double x3, double y3) =>
        CurveTo(_x, _y, x2, y2, x3, y3);

    /// <summary>Operator 'y': titik kendali kedua sama dengan titik akhir.</summary>
    public void CurveToWithEndControl(double x1, double y1, double x3, double y3) =>
        CurveTo(x1, y1, x3, y3, x3, y3);

    public void Close()
    {
        if (!_open) return;
        _items.Add(4);
        _open = false;
        _x = _startX;
        _y = _startY;
    }

    public void AddRectangle(double x, double y, double width, double height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        Close();
    }

    /// <summary>Bangun jalur perangkat hasil pemetaan matriks.</summary>
    public PdfPath ToDevice(in PdfMatrix matrix, double flatness)
    {
        if (_items.Count == 0) return PdfPath.Empty;
        var builder = new PdfPathBuilder { Flatness = flatness };
        int index = 0;
        while (index < _items.Count)
        {
            switch ((int)_items[index++])
            {
                case 0:
                {
                    var point = matrix.Transform(_items[index], _items[index + 1]);
                    index += 2;
                    builder.MoveTo(point.X, point.Y);
                    break;
                }

                case 1:
                {
                    var point = matrix.Transform(_items[index], _items[index + 1]);
                    index += 2;
                    builder.LineTo(point.X, point.Y);
                    break;
                }

                case 2:
                {
                    var control = matrix.Transform(_items[index], _items[index + 1]);
                    var point = matrix.Transform(_items[index + 2], _items[index + 3]);
                    index += 4;
                    builder.QuadTo(control.X, control.Y, point.X, point.Y);
                    break;
                }

                case 3:
                {
                    var c1 = matrix.Transform(_items[index], _items[index + 1]);
                    var c2 = matrix.Transform(_items[index + 2], _items[index + 3]);
                    var point = matrix.Transform(_items[index + 4], _items[index + 5]);
                    index += 6;
                    builder.CurveTo(c1.X, c1.Y, c2.X, c2.Y, point.X, point.Y);
                    break;
                }

                default:
                    builder.Close();
                    break;
            }
        }

        return builder.Build();
    }
}

/// <summary>Status grafik tambahan (/ExtGState).</summary>
public sealed class PdfExtGState
{
    public double FillAlpha { get; set; } = 1;

    public double StrokeAlpha { get; set; } = 1;

    public PdfBlendMode Blend { get; set; } = PdfBlendMode.Normal;

    public double? LineWidth { get; set; }

    public PdfLineCap? Cap { get; set; }

    public PdfLineJoin? Join { get; set; }

    public double? MiterLimit { get; set; }

    public double[]? DashArray { get; set; }

    public double? DashPhase { get; set; }

    public PdfImage? SoftMask { get; set; }

    public bool AlphaIsShape { get; set; }

    public static PdfExtGState? Create(PdfDictionary? dictionary, PdfDocument document)
    {
        if (dictionary is null) return null;
        var state = new PdfExtGState
        {
            FillAlpha = dictionary.GetNumber("ca") ?? 1,
            StrokeAlpha = dictionary.GetNumber("CA") ?? 1,
            LineWidth = dictionary.GetNumber("LW"),
            MiterLimit = dictionary.GetNumber("ML"),
            AlphaIsShape = dictionary.GetBool("AIS") ?? false
        };

        if (dictionary.GetInt("LC") is { } cap) state.Cap = (PdfLineCap)Math.Clamp(cap, 0, 2);
        if (dictionary.GetInt("LJ") is { } join) state.Join = (PdfLineJoin)Math.Clamp(join, 0, 2);
        state.Blend = ParseBlend(dictionary.GetNameValue("BM"));

        var dash = document.ResolveArray(dictionary.Raw("D"));
        if (dash is not null && dash.Count >= 2)
        {
            var phase = document.ResolveNumber(dash.At(0))?.Value ?? 0;
            var pattern = document.ResolveArray(dash.At(1));
            if (pattern is not null)
            {
                var values = new List<double>(pattern.Count);
                foreach (var item in pattern.Items) values.Add(document.ResolveNumber(item)?.Value ?? 0);
                state.DashArray = values.ToArray();
                state.DashPhase = phase;
            }
        }

        var mask = document.ResolveDictionary(dictionary.Raw("SMask"));
        if (mask is not null && mask.GetNameValue("S") == "Alpha")
        {
            var group = document.ResolveStream(mask.Raw("G"));
            if (group is not null)
            {
                state.SoftMask = PdfImages.DecodeGroupMask(document, group);
            }
        }

        return state;
    }

    private static PdfBlendMode ParseBlend(string? name) => name switch
    {
        "Multiply" => PdfBlendMode.Multiply,
        "Screen" => PdfBlendMode.Screen,
        "Overlay" => PdfBlendMode.Overlay,
        "Darken" => PdfBlendMode.Darken,
        "Lighten" => PdfBlendMode.Lighten,
        "ColorDodge" => PdfBlendMode.ColorDodge,
        "ColorBurn" => PdfBlendMode.ColorBurn,
        "HardLight" => PdfBlendMode.HardLight,
        "SoftLight" => PdfBlendMode.SoftLight,
        "Difference" => PdfBlendMode.Difference,
        "Exclusion" => PdfBlendMode.Exclusion,
        _ => PdfBlendMode.Normal
    };
}

/// <summary>
/// Penafsir aliran isi halaman (ISO 32000-1 §8, §9). Seluruh operator grafis,
/// jalur, teks, gambar, pola, dan gradien diterjemahkan menjadi panggilan
/// <see cref="IPdfSink"/> dalam ruang perangkat.
/// </summary>
public sealed class PdfContentInterpreter
{
    private readonly PdfDocument _document;
    private readonly PdfFontCache _fonts;
    private readonly Dictionary<string, PdfColorSpace?> _colorSpaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PdfExtGState?> _extGStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PdfTilingPattern?> _patterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PdfImage?> _images = new(StringComparer.Ordinal);
    private readonly Stack<State> _stack = new();
    private readonly PdfPathData _path = new();
    private readonly List<double> _operands = new();
    private State _state = new();
    private IPdfSink _sink = null!;
    private Dictionary<string, PdfObject> _resources = new(StringComparer.Ordinal);
    private int _depth;
    private double _deviceScale = 1;

    public PdfContentInterpreter(PdfDocument document)
    {
        _document = document;
        _fonts = new PdfFontCache(document);
    }

    /// <summary>Akses font untuk pemakaian luar (mis. pengukuran teks).</summary>
    public PdfFontCache Fonts => _fonts;

    private sealed class State
    {
        public PdfMatrix Ctm = PdfMatrix.Identity;
        public double LineWidth = 1;
        public PdfLineCap Cap = PdfLineCap.Butt;
        public PdfLineJoin Join = PdfLineJoin.Miter;
        public double MiterLimit = 10;
        public double[]? DashArray;
        public double DashPhase;
        public readonly PdfColorValue Fill = new();
        public readonly PdfColorValue StrokeColor = new();
        public double FillAlpha = 1;
        public double StrokeAlpha = 1;
        public PdfBlendMode Blend = PdfBlendMode.Normal;
        public PdfImage? SoftMask;
        public double Flatness = 0.4;

        public PdfFont? Font;
        public double FontSize;
        public double CharSpacing;
        public double WordSpacing;
        public double HorizontalScale = 100;
        public double Leading;
        public double Rise;
        public int RenderMode;
        public PdfMatrix TextMatrix = PdfMatrix.Identity;
        public PdfMatrix TextLineMatrix = PdfMatrix.Identity;
        public bool InText;

        public State Clone()
        {
            var copy = (State)MemberwiseClone();
            copy.Fill.Set(Fill.Space, Fill.Components);
            copy.Fill.Pattern = Fill.Pattern;
            copy.StrokeColor.Set(StrokeColor.Space, StrokeColor.Components);
            copy.StrokeColor.Pattern = StrokeColor.Pattern;
            return copy;
        }
    }

    /// <summary>Jalankan aliran isi halaman (atau XObject form/pola).</summary>
    public void Run(
        PdfDictionary? resources,
        IReadOnlyList<byte[]> contents,
        in PdfMatrix deviceMatrix,
        IPdfSink sink,
        double deviceScale = 1,
        PdfPaint? initialFill = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _deviceScale = deviceScale <= 0 ? 1 : deviceScale;
        _state = new State { Ctm = deviceMatrix };
        if (initialFill is { } fill)
        {
            // Pola tak berwarna (PaintType 2): warna isi awal datang dari scn.
            _state.Fill.Space = PdfColorSpace.DeviceRgb;
            _state.Fill.Set(PdfColorSpace.DeviceRgb, new[] { fill.R, fill.G, fill.B });
            _state.FillAlpha = fill.Alpha;
        }

        _stack.Clear();
        _resources = BuildResources(resources);
        _colorSpaces.Clear();
        _extGStates.Clear();
        _patterns.Clear();
        _images.Clear();

        foreach (byte[] content in contents)
        {
            Execute(content);
        }
    }

    private static Dictionary<string, PdfObject> BuildResources(PdfDictionary? resources)
    {
        var map = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        if (resources is null) return map;
        foreach (var pair in resources.Items) map[pair.Key] = pair.Value;
        return map;
    }

    private void Execute(byte[] content)
    {
        var lexer = new PdfLexer(content);
        var parser = new PdfParser(lexer);
        _operands.Clear();

        while (true)
        {
            var token = parser.Next();
            if (token.Type == PdfTokenType.End) break;

            if (token.Type == PdfTokenType.Number)
            {
                _operands.Add(token.Number);
                continue;
            }

            switch (token.Type)
            {
                case PdfTokenType.String:
                case PdfTokenType.Name:
                case PdfTokenType.ArrayOpen:
                case PdfTokenType.DictOpen:
                {
                    // Nilai kompleks (nama warna pola, larik TJ, kamus inline).
                    var value = ParseComplex(parser, token);
                    if (value is not null) HandleComplex(value);
                    continue;
                }

                case PdfTokenType.Boolean:
                case PdfTokenType.Null:
                    continue;
            }

            string operation = token.Name ?? string.Empty;
            if (operation == "BI")
            {
                SkipInlineImage(lexer);
                _operands.Clear();
                continue;
            }

            ExecuteOperation(operation);
            _operands.Clear();
        }
    }

    private PdfObject? ParseComplex(PdfParser parser, PdfToken token)
    {
        switch (token.Type)
        {
            case PdfTokenType.Name:
                return new PdfName(token.Name);
            case PdfTokenType.String:
                return new PdfString(token.Bytes ?? Array.Empty<byte>());
            case PdfTokenType.ArrayOpen:
            {
                // Tanda '[' sudah dikonsumsi; isi larik dibaca sampai ']'.
                var array = new PdfArray();
                while (parser.Peek().Type is not (PdfTokenType.ArrayClose or PdfTokenType.End))
                {
                    if (!parser.TryParseValue(out var item)) break;
                    array.Items.Add(item);
                }

                if (parser.Peek().Type == PdfTokenType.ArrayClose) parser.Next();
                return array;
            }

            case PdfTokenType.DictOpen:
            {
                var dict = new PdfDictionary();
                while (parser.Peek().Type is PdfTokenType.Name)
                {
                    var key = parser.Next();
                    if (!parser.TryParseValue(out var item)) break;
                    dict.Set(key.Name, item);
                }

                if (parser.Peek().Type == PdfTokenType.DictClose) parser.Next();
                return dict;
            }

            default:
                return parser.TryParseValue(out var value) ? value : null;
        }
    }

    private void HandleComplex(PdfObject value)
    {
        switch (value)
        {
            case PdfName name:
                _lastName = name.Value;
                break;
            case PdfString text:
                _lastString = text.Bytes;
                break;
            case PdfArray array:
                // Larik TJ: angka dan string berselang-seling.
                _textItems = array.Items;
                break;
        }
    }

    private string? _lastName;
    private byte[]? _lastString;
    private List<PdfObject>? _textItems;

    private double Operand(int index)
    {
        int position = _operands.Count - 1 - index;
        return position >= 0 && position < _operands.Count ? _operands[position] : 0;
    }

    private void SkipInlineImage(PdfLexer lexer)
    {
        // Kamus gambar inline di antara BI dan ID; datanya dilewati sampai EI.
        byte[] data = lexer.Buffer;
        int position = lexer.Position;
        while (position < data.Length - 1)
        {
            if (data[position] == (byte)'E' && data[position + 1] == (byte)'I'
                && (position == 0 || PdfLexer.IsWhitespace(data[position - 1])))
            {
                lexer.Position = position + 2;
                return;
            }

            position++;
        }

        lexer.Position = data.Length;
    }

    private void ExecuteOperation(string operation)
    {
        switch (operation)
        {
            // ------------------------------------------------- status grafik
            case "q":
                _stack.Push(_state);
                _state = _state.Clone();
                _sink.Save();
                break;
            case "Q":
                if (_stack.Count > 0)
                {
                    _state = _stack.Pop();
                }

                _sink.Restore();
                break;
            case "cm":
                // Operan cm adalah a b c d e f; Operand(i) menghitung dari operan terakhir.
                _state.Ctm = new PdfMatrix(
                        Operand(5), Operand(4), Operand(3), Operand(2), Operand(1), Operand(0))
                    .Multiply(_state.Ctm);
                break;
            case "w":
                _state.LineWidth = Operand(0);
                break;
            case "J":
                _state.Cap = (PdfLineCap)Math.Clamp((int)Operand(0), 0, 2);
                break;
            case "j":
                _state.Join = (PdfLineJoin)Math.Clamp((int)Operand(0), 0, 2);
                break;
            case "M":
                _state.MiterLimit = Operand(0);
                break;
            case "d":
            {
                if (_textItems is not null)
                {
                    var values = new List<double>(_textItems.Count);
                    foreach (var item in _textItems) values.Add(item is PdfNumber number ? number.Value : 0);
                    _state.DashArray = values.ToArray();
                }

                _state.DashPhase = Operand(0);
                break;
            }

            case "gs":
                ApplyExtGState(_lastName);
                break;

            // ------------------------------------------------------ warna
            case "cs":
                SetColorSpace(isStroke: false, _lastName);
                break;
            case "CS":
                SetColorSpace(isStroke: true, _lastName);
                break;
            case "sc":
            case "scn":
                SetColor(isStroke: false, patternAware: operation == "scn");
                break;
            case "SC":
            case "SCN":
                SetColor(isStroke: true, patternAware: operation == "SCN");
                break;
            case "g":
                SetGray(isStroke: false);
                break;
            case "G":
                SetGray(isStroke: true);
                break;
            case "rg":
                SetRgb(isStroke: false);
                break;
            case "RG":
                SetRgb(isStroke: true);
                break;
            case "k":
                SetCmyk(isStroke: false);
                break;
            case "K":
                SetCmyk(isStroke: true);
                break;

            // ------------------------------------------------------- jalur
            case "m":
                _path.MoveTo(Operand(1), Operand(0));
                break;
            case "l":
                _path.LineTo(Operand(1), Operand(0));
                break;
            case "c":
                _path.CurveTo(Operand(5), Operand(4), Operand(3), Operand(2), Operand(1), Operand(0));
                break;
            case "v":
                // x2 y2 x3 y3 — titik kendali pertama adalah titik aktif.
                _path.CurveToWithCurrentControl(Operand(3), Operand(2), Operand(1), Operand(0));
                break;
            case "y":
                // x1 y1 x3 y3 — titik kendali kedua adalah titik akhir.
                _path.CurveToWithEndControl(Operand(3), Operand(2), Operand(1), Operand(0));
                break;
            case "h":
                _path.Close();
                break;
            case "re":
                _path.AddRectangle(Operand(3), Operand(2), Operand(1), Operand(0));
                break;

            // ----------------------------------------------------- melukis
            case "S":
                PaintStroke();
                break;
            case "s":
                _path.Close();
                PaintStroke();
                break;
            case "f":
            case "F":
                PaintFill(PdfFillRule.NonZero);
                break;
            case "f*":
                PaintFill(PdfFillRule.EvenOdd);
                break;
            case "B":
            case "B*":
                PaintFill(operation == "B" ? PdfFillRule.NonZero : PdfFillRule.EvenOdd);
                PaintStroke(keepPath: true);
                break;
            case "b":
            case "b*":
                _path.Close();
                PaintFill(operation == "b" ? PdfFillRule.NonZero : PdfFillRule.EvenOdd);
                PaintStroke(keepPath: true);
                break;
            case "n":
                _path.Reset();
                break;
            case "W":
            case "W*":
                ClipPath(operation == "W" ? PdfFillRule.NonZero : PdfFillRule.EvenOdd);
                break;

            // -------------------------------------------------- teks
            case "BT":
                _state.TextMatrix = PdfMatrix.Identity;
                _state.TextLineMatrix = PdfMatrix.Identity;
                _state.InText = true;
                break;
            case "ET":
                _state.InText = false;
                break;
            case "Tc":
                _state.CharSpacing = Operand(0);
                break;
            case "Tw":
                _state.WordSpacing = Operand(0);
                break;
            case "Tz":
                _state.HorizontalScale = Operand(0);
                break;
            case "TL":
                _state.Leading = Operand(0);
                break;
            case "Tf":
                _state.Font = _fonts.Get(ResolveResource("Font", _lastName));
                _state.FontSize = Operand(0);
                break;
            case "Tr":
                _state.RenderMode = (int)Operand(0);
                break;
            case "Ts":
                _state.Rise = Operand(0);
                break;
            case "Td":
                TextMove(Operand(1), Operand(0));
                break;
            case "TD":
                _state.Leading = -Operand(0);
                TextMove(Operand(1), Operand(0));
                break;
            case "Tm":
                _state.TextMatrix = new PdfMatrix(Operand(5), Operand(4), Operand(3), Operand(2), Operand(1), Operand(0));
                _state.TextLineMatrix = _state.TextMatrix;
                break;
            case "T*":
                TextMove(0, -_state.Leading);
                break;
            case "Tj":
                ShowComplexText();
                break;
            case "TJ":
                ShowTextArray();
                break;
            case "'":
                TextMove(0, -_state.Leading);
                ShowComplexText();
                break;
            case "\"":
                _state.WordSpacing = Operand(2);
                _state.CharSpacing = Operand(1);
                TextMove(0, -_state.Leading);
                ShowComplexText();
                break;

            // ------------------------------------------------- XObject, sh, MC
            case "Do":
                DrawXObject(_lastName);
                break;
            case "sh":
                DrawShading(_lastName);
                break;
            case "BI":
            case "BX":
            case "EX":
            case "MP":
            case "DP":
            case "BMC":
            case "BDC":
            case "EMC":
            case "ri":
            case "i":
                break;
        }

        _lastName = null;
        _textItems = null;
        _lastString = null;
    }

    private PdfObject? ResolveResource(string category, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _resources.TryGetValue(category, out var group)
            ? _document.ResolveDictionary(group)?.Raw(name) ?? _document.Resolve(group)
            : null;
    }

    private void ApplyExtGState(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!_extGStates.TryGetValue(name, out var state))
        {
            var value = ResolveResource("ExtGState", name);
            state = PdfExtGState.Create(_document.ResolveDictionary(value), _document);
            _extGStates[name] = state;
        }

        if (state is null) return;
        _state.FillAlpha = state.FillAlpha;
        _state.StrokeAlpha = state.StrokeAlpha;
        _state.Blend = state.Blend;
        _state.SoftMask = state.SoftMask;
        if (state.LineWidth is { } width) _state.LineWidth = width;
        if (state.Cap is { } cap) _state.Cap = cap;
        if (state.Join is { } join) _state.Join = join;
        if (state.MiterLimit is { } miter) _state.MiterLimit = miter;
        if (state.DashArray is not null)
        {
            _state.DashArray = state.DashArray;
            _state.DashPhase = state.DashPhase ?? 0;
        }
    }

    private void SetColorSpace(bool isStroke, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!_colorSpaces.TryGetValue(name, out var space))
        {
            var value = ResolveResource("ColorSpace", name);
            space = PdfColorSpace.Resolve(value, _document) ?? PdfColorSpace.DeviceGray;
            _colorSpaces[name] = space;
        }

        var target = isStroke ? _state.StrokeColor : _state.Fill;
        target.Pattern = null;
        target.Space = space ?? PdfColorSpace.DeviceGray;
        target.PatternBaseSpace = ReadPatternBaseSpace(name);
    }

    /// <summary>Ruang warna dasar pola tak berwarna: [/Pattern &lt;ruang&gt;].</summary>
    private PdfColorSpace? ReadPatternBaseSpace(string name)
    {
        var value = _document.Resolve(ResolveResource("ColorSpace", name));
        if (value is not PdfArray array || array.Count < 2) return null;
        return PdfColorSpace.Resolve(array.At(1), _document);
    }

    private void SetColor(bool isStroke, bool patternAware)
    {
        var target = isStroke ? _state.StrokeColor : _state.Fill;
        if (patternAware && _lastName is not null)
        {
            // scn terakhir berupa nama pola; komponen sebelumnya adalah warna dasar
            // untuk pola tak berwarna.
            target.Pattern = ResolvePattern(_lastName);
            _lastName = null;
            if (_operands.Count > 0)
            {
                target.Set(target.PatternBaseSpace ?? PdfColorSpace.DeviceGray, _operands);
            }

            return;
        }

        target.Pattern = null;
        target.Set(target.Space, _operands);
    }

    private void SetGray(bool isStroke)
    {
        var target = isStroke ? _state.StrokeColor : _state.Fill;
        target.Pattern = null;
        target.Space = PdfColorSpace.DeviceGray;
        target.Set(PdfColorSpace.DeviceGray, new[] { Operand(0) });
    }

    private void SetRgb(bool isStroke)
    {
        var target = isStroke ? _state.StrokeColor : _state.Fill;
        target.Pattern = null;
        target.Space = PdfColorSpace.DeviceRgb;
        target.Set(PdfColorSpace.DeviceRgb, new[] { Operand(2), Operand(1), Operand(0) });
    }

    private void SetCmyk(bool isStroke)
    {
        var target = isStroke ? _state.StrokeColor : _state.Fill;
        target.Pattern = null;
        target.Space = PdfColorSpace.DeviceCmyk;
        target.Set(PdfColorSpace.DeviceCmyk, new[] { Operand(3), Operand(2), Operand(1), Operand(0) });
    }

    private PdfTilingPattern? ResolvePattern(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_patterns.TryGetValue(name, out var cached)) return cached;

        var value = ResolveResource("Pattern", name);
        var stream = _document.ResolveStream(value);
        PdfTilingPattern? pattern = null;
        if (stream is not null && (stream.Dictionary.GetInt("PatternType") ?? 1) == 1)
        {
            pattern = PdfTilingPattern.Create(stream, _document);
        }

        _patterns[name] = pattern;
        return pattern;
    }

    private PdfPaint BuildPaint(PdfColorValue color, double alpha)
    {
        color.GetRgb(out double red, out double green, out double blue);
        var paint = PdfPaint.FromRgb(red, green, blue, alpha);
        paint.Pattern = color.Pattern;
        if (color.Pattern is { } pattern)
        {
            // Ruang pola → ruang perangkat: matriks pola dikalikan CTM saat ini.
            paint.PatternMatrix = pattern.Matrix.Multiply(_state.Ctm);
        }

        return paint;
    }

    private void PaintFill(PdfFillRule rule)
    {
        if (_path.IsEmpty) return;
        var device = _path.ToDevice(_state.Ctm, EffectiveFlatness());
        var paint = BuildPaint(_state.Fill, _state.FillAlpha);
        _sink.Fill(device, rule, paint);
        _path.Reset();
    }

    private void PaintStroke(bool keepPath = false)
    {
        if (_path.IsEmpty)
        {
            if (!keepPath) _path.Reset();
            return;
        }

        var device = _path.ToDevice(_state.Ctm, EffectiveFlatness());
        var spec = BuildStrokeSpec();
        var paint = BuildPaint(_state.StrokeColor, _state.StrokeAlpha);
        _sink.Stroke(device, spec, paint);
        if (!keepPath) _path.Reset();
    }

    private PdfStrokeSpec BuildStrokeSpec()
    {
        double scale = Math.Max(1e-6, _state.Ctm.ScaleX);
        var dash = _state.DashArray is { Length: > 0 }
            ? ScaleDash(_state.DashArray, scale)
            : null;
        return new PdfStrokeSpec
        {
            Width = Math.Abs(_state.LineWidth) * scale,
            Cap = _state.Cap,
            Join = _state.Join,
            MiterLimit = _state.MiterLimit <= 0 ? 10 : _state.MiterLimit,
            DashArray = dash,
            DashPhase = _state.DashPhase * scale
        };
    }

    private static double[] ScaleDash(double[] dash, double scale)
    {
        var scaled = new double[dash.Length];
        double total = 0;
        for (int i = 0; i < dash.Length; i++)
        {
            scaled[i] = Math.Max(0.01, Math.Abs(dash[i]) * scale);
            total += scaled[i];
        }

        return total <= 0 ? Array.Empty<double>() : scaled;
    }

    private void ClipPath(PdfFillRule rule)
    {
        if (_path.IsEmpty) return;
        var device = _path.ToDevice(_state.Ctm, EffectiveFlatness());
        _sink.Clip(device, rule);
    }

    private double EffectiveFlatness()
    {
        double scale = Math.Max(_state.Ctm.ScaleX, _state.Ctm.ScaleY);
        // Kerataan diberikan pada ruang pengguna; dibagi skala agar hasil di
        // perangkat tetap sekitar 0.25 px.
        return Math.Clamp(0.25 / Math.Max(scale, 0.01), 0.002, 2.0);
    }

    private void TextMove(double tx, double ty)
    {
        _state.TextLineMatrix = PdfMatrix.Translation(tx, ty).Multiply(_state.TextLineMatrix);
        _state.TextMatrix = _state.TextLineMatrix;
    }

    private void ShowComplexText()
    {
        if (_lastString is not { Length: > 0 }) return;
        ShowTextBytes(_lastString);
    }

    private void ShowTextArray()
    {
        var font = _state.Font;
        if (font is null || _textItems is null) return;

        foreach (var item in _textItems)
        {
            switch (_document.Resolve(item))
            {
                case PdfString text:
                    ShowTextBytes(text.Bytes);
                    break;
                case PdfNumber number:
                {
                    double adjustment = -number.Value / 1000.0 * _state.FontSize * (_state.HorizontalScale / 100.0);
                    _state.TextMatrix = PdfMatrix.Translation(adjustment, 0).Multiply(_state.TextMatrix);
                    break;
                }
            }
        }
    }

    private void ShowTextBytes(byte[] data)
    {
        var font = _state.Font;
        if (font is null || data.Length == 0) return;

        double fontSize = _state.FontSize;
        double horizontalScale = _state.HorizontalScale / 100.0;
        double unitsPerEm = font.GlyphUnitsPerEm;
        var startTextMatrix = _state.TextMatrix;

        var glyphIds = new List<int>();
        var glyphX = new List<double>();
        var glyphY = new List<double>();
        var glyphAdvance = new List<double>();
        var text = new StringBuilder();
        var textMatrix = _state.TextMatrix;
        int position = 0;
        double startX = 0;
        double startY = 0;
        double endX = 0;
        double endY = 0;
        bool first = true;

        while (position < data.Length)
        {
            int consumed = font.ReadCode(data, position, data.Length, out int code);
            if (consumed <= 0) break;
            position += consumed;

            // Matriks tampilan teks: Trm = [Tfs·Th 0 0 Tfs 0 Ts] × Tm × CTM
            var render = PdfMatrix.Scaling(fontSize * horizontalScale, fontSize)
                .Multiply(PdfMatrix.Translation(0, _state.Rise))
                .Multiply(textMatrix)
                .Multiply(_state.Ctm);

            var origin = render.Transform(0, 0);
            if (first)
            {
                startX = origin.X;
                startY = origin.Y;
                first = false;
            }

            endX = origin.X;
            endY = origin.Y;

            double width = font.GetWidth(code) / 1000.0;
            double advance = (width * fontSize + _state.CharSpacing + (code == 32 ? _state.WordSpacing : 0))
                             * horizontalScale;

            glyphIds.Add(font.TryGetGlyph(code, out int glyphId) ? glyphId : -1);
            glyphX.Add(origin.X);
            glyphY.Add(origin.Y);
            glyphAdvance.Add(0);
            text.Append(font.Decode(code) ?? string.Empty);

            textMatrix = PdfMatrix.Translation(advance, 0).Multiply(textMatrix);

            // Perpindahan perangkat sesungguhnya dihitung setelah matriks baru.
            var nextRender = PdfMatrix.Scaling(fontSize * horizontalScale, fontSize)
                .Multiply(PdfMatrix.Translation(0, _state.Rise))
                .Multiply(textMatrix)
                .Multiply(_state.Ctm);
            var next = nextRender.Transform(0, 0);
            glyphAdvance[^1] = Math.Sqrt(
                (next.X - origin.X) * (next.X - origin.X) + (next.Y - origin.Y) * (next.Y - origin.Y));
            endX = next.X;
            endY = next.Y;
        }

        _state.TextMatrix = textMatrix;
        if (glyphIds.Count == 0) return;

        // Matriks glif selalu berpangkal pada awal string; perpindahan tiap glif
        // tersimpan pada GlyphX/GlyphY sehingga penggambar dapat memakai satu
        // matriks saja untuk seluruh string. Bentuk glif dinyatakan dalam satuan
        // font (lihat IPdfGlyphSource.UnitsPerEm), bukan satuan em 1000.
        var glyphMatrix = PdfMatrix.Scaling(fontSize / unitsPerEm, fontSize / unitsPerEm)
            .Multiply(PdfMatrix.Scaling(horizontalScale, 1))
            .Multiply(PdfMatrix.Translation(0, _state.Rise))
            .Multiply(startTextMatrix)
            .Multiply(_state.Ctm);

        double directionX = 1;
        double directionY = 0;
        if (glyphX.Count > 1)
        {
            double dx = glyphX[1] - glyphX[0];
            double dy = glyphY[1] - glyphY[0];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 1e-9)
            {
                directionX = dx / length;
                directionY = dy / length;
            }
        }

        double deviceSize = fontSize * Math.Max(_state.Ctm.ScaleY, 1e-6);
        var run = new PdfTextRun
        {
            Font = font,
            FontSize = deviceSize,
            Text = text.ToString(),
            GlyphMatrix = glyphMatrix,
            GlyphIds = glyphIds.ToArray(),
            GlyphX = glyphX.ToArray(),
            GlyphY = glyphY.ToArray(),
            GlyphAdvance = glyphAdvance.ToArray(),
            StartX = startX,
            StartY = startY,
            EndX = endX,
            EndY = endY,
            DirectionX = directionX,
            DirectionY = directionY,
            RenderMode = _state.RenderMode,
            Fill = BuildPaint(_state.Fill, _state.FillAlpha),
            Stroke = BuildPaint(_state.StrokeColor, _state.StrokeAlpha),
            StrokeWidth = Math.Abs(_state.LineWidth) * Math.Max(_state.Ctm.ScaleX, 1e-6)
        };

        _sink.ShowText(run);
    }

    private void DrawXObject(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var value = ResolveResource("XObject", name);
        var stream = _document.ResolveStream(value);
        if (stream is null) return;

        if (_images.TryGetValue(name, out var cachedImage))
        {
            if (cachedImage is not null) DrawImageObject(cachedImage);
            return;
        }

        string? subtype = stream.Dictionary.GetNameValue("Subtype");
        if (subtype == "Image")
        {
            var image = PdfImages.Decode(_document, stream);
            _images[name] = image;
            if (image is not null) DrawImageObject(image);
            return;
        }

        if (subtype != "Form") return;
        if (_depth > 12) return;

        var formMatrix = ReadMatrix(stream.Dictionary.Raw("Matrix")) ?? PdfMatrix.Identity;
        var ctm = formMatrix.Multiply(_state.Ctm);
        var resources = _document.ResolveDictionary(stream.Dictionary.Raw("Resources"));
        var contents = _document.DecodeStream(stream);

        _depth++;
        var savedState = _state;
        var savedResources = _resources;
        var savedSink = _sink;
        _sink.Save();

        // XObject form menggambar di dalam ruang tersendiri; /BBox memotongnya.
        var boundingBox = ReadBox(stream.Dictionary.Raw("BBox"));
        if (boundingBox is { } box && !box.IsEmpty)
        {
            var clip = new PdfPathData();
            clip.AddRectangle(box.X, box.Y, box.Width, box.Height);
            _sink.Clip(clip.ToDevice(ctm, 0.25), PdfFillRule.NonZero);
        }

        _state = savedState.Clone();
        _state.Ctm = ctm;
        _resources = BuildResources(resources ?? _document.ResolveDictionary(savedResources.TryGetValue("Resources", out var nested) ? nested : null));

        try
        {
            Execute(contents);
        }
        catch (PdfException)
        {
            // Form rusak tidak boleh menggagalkan seluruh halaman.
        }

        _state = savedState;
        _resources = savedResources;
        _sink = savedSink;
        _sink.Restore();
        _depth--;
    }

    private void DrawImageObject(PdfImage image)
    {
        // Gambar PDF menempati persegi satuan [0,1]×[0,1]; matriks gambar
        // mengubah ruang piksel 0..lebar/tinggi menjadi persegi itu lebih dulu.
        var matrix = PdfMatrix.Scaling(1.0 / image.Width, 1.0 / image.Height).Multiply(_state.Ctm);
        _sink.DrawImage(image, matrix, _state.FillAlpha, image.Interpolate);
    }

    private void DrawShading(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var value = ResolveResource("Shading", name);
        var shading = PdfShading.Create(value, _document);
        if (shading is null) return;
        _sink.DrawShading(shading, _state.Ctm, _state.FillAlpha);
    }

    private static PdfMatrix? ReadMatrix(PdfObject? value)
    {
        // Matriks dibaca langsung dari larik angka (tanpa dokumen).
        if (value is not PdfArray array || array.Count < 6) return null;
        double[] parts = new double[6];
        for (int i = 0; i < 6; i++)
        {
            if (array.At(i) is PdfNumber number) parts[i] = number.Value;
        }

        return new PdfMatrix(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
    }

    private static PdfRect? ReadBox(PdfObject? value)
    {
        if (value is not PdfArray array || array.Count < 4) return null;
        double[] parts = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (array.At(i) is PdfNumber number) parts[i] = number.Value;
        }

        return PdfRect.FromEdges(Math.Min(parts[0], parts[2]), Math.Min(parts[1], parts[3]),
            Math.Max(parts[0], parts[2]), Math.Max(parts[1], parts[3]));
    }

    /// <summary>Susun daftar aliran isi halaman (/Contents bisa stream atau larik).</summary>
    public static IReadOnlyList<byte[]> ReadContents(PdfDocument document, PdfObject? contents)
    {
        var list = new List<byte[]>();
        switch (document.Resolve(contents))
        {
            case PdfStream stream:
                list.Add(stream.Decoded());
                break;
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    if (document.ResolveStream(item) is { } part) list.Add(part.Decoded());
                }

                break;
        }

        return list;
    }
}
