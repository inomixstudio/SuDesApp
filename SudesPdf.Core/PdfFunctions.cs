using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Fungsi PDF (ISO 32000-1 §7.10): jenis 0 (sampel), 2 (eksponensial),
/// 3 (sambungan), dan 4 (kalkulator PostScript). Dipakai pewarnaan gradien,
/// ruang warna Separation/DeviceN, dan topeng lembut.
/// </summary>
public abstract class PdfFunction
{
    public abstract int InputCount { get; }

    public abstract int OutputCount { get; }

    public abstract void Evaluate(double[] input, double[] output);

    protected static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    protected static double Interpolate(double x, double x0, double x1, double y0, double y1)
    {
        if (Math.Abs(x1 - x0) < 1e-12) return y0;
        return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
    }

    public static PdfFunction? Create(PdfObject? value, PdfDocument document)
    {
        var resolved = document.Resolve(value);
        PdfDictionary? dict;
        PdfStream? stream;
        if (resolved is PdfStream streamValue)
        {
            dict = streamValue.Dictionary;
            stream = streamValue;
        }
        else if (resolved is PdfDictionary dictionaryValue)
        {
            dict = dictionaryValue;
            stream = null;
        }
        else
        {
            return null;
        }

        int type = (int)(document.ResolveNumber(dict.Raw("FunctionType"))?.Value ?? -1);
        return type switch
        {
            0 => SampledFunction.Create(dict, stream, document),
            2 => ExponentialFunction.Create(dict, document),
            3 => StitchingFunction.Create(dict, document),
            4 => PostScriptFunction.Create(dict, stream, document),
            _ => null
        };
    }
}

internal sealed class ExponentialFunction : PdfFunction
{
    private readonly double[] _c0;
    private readonly double[] _c1;
    private readonly double _n;

    private ExponentialFunction(double[] c0, double[] c1, double n)
    {
        _c0 = c0;
        _c1 = c1;
        _n = n;
    }

    public override int InputCount => 1;

    public override int OutputCount => _c0.Length;

    public static PdfFunction? Create(PdfDictionary dict, PdfDocument document)
    {
        var c0 = ReadNumbers(document.ResolveArray(dict.Raw("C0")), 1);
        var c1 = ReadNumbers(document.ResolveArray(dict.Raw("C1")), 1);
        double n = document.ResolveNumber(dict.Raw("N"))?.Value ?? 1;
        int count = Math.Max(c0.Length, c1.Length);
        if (count == 0) count = 1;
        Array.Resize(ref c0, count);
        Array.Resize(ref c1, count);
        return new ExponentialFunction(c0, c1, n);
    }

    public override void Evaluate(double[] input, double[] output)
    {
        double x = input.Length > 0 ? Clamp(input[0], 0, 1) : 0;
        double factor = Math.Pow(x, _n);
        for (int i = 0; i < output.Length; i++)
        {
            double c0 = i < _c0.Length ? _c0[i] : 0;
            double c1 = i < _c1.Length ? _c1[i] : 1;
            output[i] = c0 + factor * (c1 - c0);
        }
    }

    private static double[] ReadNumbers(PdfArray? array, int count)
    {
        if (array is null) return new double[count];
        var values = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            values[i] = (array.At(i) as PdfNumber)?.Value ?? 0;
        }

        return values;
    }
}

internal sealed class StitchingFunction : PdfFunction
{
    private readonly List<PdfFunction> _functions = new();
    private readonly double[] _bounds;
    private readonly double[] _encode;

    private StitchingFunction(double[] bounds, double[] encode)
    {
        _bounds = bounds;
        _encode = encode;
    }

    public override int InputCount => 1;

    public override int OutputCount => _functions.Count > 0 ? _functions[0].OutputCount : 1;

    public static PdfFunction? Create(PdfDictionary dict, PdfDocument document)
    {
        var functions = document.ResolveArray(dict.Raw("Functions"));
        var bounds = Numbers(document.ResolveArray(dict.Raw("Bounds")));
        var encode = Numbers(document.ResolveArray(dict.Raw("Encode")));
        if (functions is null || functions.Count == 0) return null;
        if (encode.Length < functions.Count * 2) return null;

        var stitched = new StitchingFunction(bounds, encode);
        foreach (var item in functions.Items)
        {
            if (PdfFunction.Create(item, document) is { } function) stitched._functions.Add(function);
        }

        return stitched._functions.Count == 0 ? null : stitched;
    }

    public override void Evaluate(double[] input, double[] output)
    {
        double x = input.Length > 0 ? input[0] : 0;
        int index = 0;
        while (index < _bounds.Length && x >= _bounds[index]) index++;
        index = Math.Clamp(index, 0, _functions.Count - 1);

        double lower = index == 0 ? 0 : _bounds[index - 1];
        double upper = index < _bounds.Length ? _bounds[index] : 1;
        double e0 = _encode[index * 2];
        double e1 = _encode[index * 2 + 1];
        double mapped = Interpolate(x, lower, upper, e0, e1);
        _functions[index].Evaluate(new[] { mapped }, output);
    }

    private static double[] Numbers(PdfArray? array)
    {
        if (array is null) return Array.Empty<double>();
        var values = new double[array.Count];
        for (int i = 0; i < array.Count; i++) values[i] = (array.At(i) as PdfNumber)?.Value ?? 0;
        return values;
    }
}

internal sealed class SampledFunction : PdfFunction
{
    private readonly double[] _domain;
    private readonly double[] _range;
    private readonly int[] _sizes;
    private readonly double[] _encode;
    private readonly double[] _decode;
    private readonly double[] _samples;

    private SampledFunction(
        double[] domain, double[] range, int[] sizes, double[] encode, double[] decode, double[] samples)
    {
        _domain = domain;
        _range = range;
        _sizes = sizes;
        _encode = encode;
        _decode = decode;
        _samples = samples;
    }

    public override int InputCount => _sizes.Length;

    public override int OutputCount => _range.Length / 2;

    public static PdfFunction? Create(PdfDictionary dict, PdfStream? stream, PdfDocument document)
    {
        if (stream is null) return null;
        var domain = Numbers(document.ResolveArray(dict.Raw("Domain")));
        var range = Numbers(document.ResolveArray(dict.Raw("Range")));
        var sizeArray = document.ResolveArray(dict.Raw("Size"));
        if (sizeArray is null || domain.Length < 2) return null;

        var sizes = new int[sizeArray.Count];
        for (int i = 0; i < sizeArray.Count; i++)
        {
            sizes[i] = Math.Max(1, (int)((sizeArray.At(i) as PdfNumber)?.Value ?? 1));
        }

        double[] encode = Numbers(document.ResolveArray(dict.Raw("Encode")));
        if (encode.Length < sizes.Length * 2)
        {
            encode = new double[sizes.Length * 2];
            for (int i = 0; i < sizes.Length; i++)
            {
                encode[i * 2] = 0;
                encode[i * 2 + 1] = sizes[i] - 1;
            }
        }

        double[] decode = Numbers(document.ResolveArray(dict.Raw("Decode")));
        if (decode.Length < range.Length) decode = range;

        int bits = (int)(dict.GetNumber("BitsPerSample") ?? 8);
        if (bits is not (1 or 2 or 4 or 8 or 12 or 16)) return null;

        byte[] data = stream.Decoded();
        int components = Math.Max(1, range.Length / 2);
        int total = components;
        foreach (int size in sizes) total *= size;
        var samples = new double[total];
        int bitPosition = 0;
        for (int i = 0; i < total; i++)
        {
            samples[i] = ReadSample(data, ref bitPosition, bits);
        }

        return new SampledFunction(domain, range, sizes, encode, decode, samples);
    }

    private static double ReadSample(byte[] data, ref int bitPosition, int bits)
    {
        int value = 0;
        for (int i = 0; i < bits; i++)
        {
            int index = bitPosition >> 3;
            if (index >= data.Length) return value;
            int bit = 7 - (bitPosition & 7);
            value = (value << 1) | ((data[index] >> bit) & 1);
            bitPosition++;
        }

        return bits == 16 ? value : value / (double)((1 << bits) - 1);
    }

    public override void Evaluate(double[] input, double[] output)
    {
        int dimensions = _sizes.Length;
        var coordinates = new double[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            double x = i < input.Length ? input[i] : 0;
            double x0 = i * 2 < _domain.Length ? _domain[i * 2] : 0;
            double x1 = i * 2 + 1 < _domain.Length ? _domain[i * 2 + 1] : 1;
            x = Clamp(x, Math.Min(x0, x1), Math.Max(x0, x1));
            double mapped = Interpolate(x, x0, x1, _encode[i * 2], _encode[i * 2 + 1]);
            coordinates[i] = Clamp(mapped, 0, Math.Max(0, _sizes[i] - 1));
        }

        int components = OutputCount;
        var baseIndex = new int[dimensions];
        var fractions = new double[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            baseIndex[i] = (int)Math.Floor(coordinates[i]);
            fractions[i] = coordinates[i] - baseIndex[i];
            if (baseIndex[i] >= _sizes[i] - 1)
            {
                baseIndex[i] = Math.Max(0, _sizes[i] - 2);
                fractions[i] = 1;
            }
        }

        for (int c = 0; c < components; c++) output[c] = 0;

        // Interpolasi multilinear pada hiperkubus 2^d titik.
        int corners = 1 << dimensions;
        for (int corner = 0; corner < corners; corner++)
        {
            double weight = 1;
            int index = 0;
            for (int axis = 0; axis < dimensions; axis++)
            {
                bool high = (corner & (1 << axis)) != 0;
                weight *= high ? fractions[axis] : 1 - fractions[axis];
                int coordinate = baseIndex[axis] + (high ? 1 : 0);
                coordinate = Math.Clamp(coordinate, 0, _sizes[axis] - 1);
                index = index * _sizes[axis] + coordinate;
            }

            if (weight == 0) continue;
            for (int c = 0; c < components; c++)
            {
                int sampleIndex = (index * components) + c;
                if (sampleIndex >= _samples.Length) continue;
                output[c] += weight * _samples[sampleIndex];
            }
        }

        for (int c = 0; c < components; c++)
        {
            int lowIndex = c * 2;
            int highIndex = lowIndex + 1;
            if (highIndex < _decode.Length)
            {
                output[c] = Interpolate(output[c], 0, 1, _decode[lowIndex], _decode[highIndex]);
            }
        }
    }

    private static double[] Numbers(PdfArray? array)
    {
        if (array is null) return Array.Empty<double>();
        var values = new double[array.Count];
        for (int i = 0; i < array.Count; i++) values[i] = (array.At(i) as PdfNumber)?.Value ?? 0;
        return values;
    }
}

/// <summary>Fungsi jenis 4: kalkulator PostScript (ISO 32000-1 §7.10.5).</summary>
internal sealed class PostScriptFunction : PdfFunction
{
    private readonly double[] _domain;
    private readonly double[] _range;
    private readonly List<object> _program = new();

    private PostScriptFunction(double[] domain, double[] range, List<object> program)
    {
        _domain = domain;
        _range = range;
        _program = program;
    }

    public override int InputCount => _domain.Length / 2;

    public override int OutputCount => _range.Length / 2;

    public static PdfFunction? Create(PdfDictionary dict, PdfStream? stream, PdfDocument document)
    {
        if (stream is null) return null;

        var domain = Numbers(document.ResolveArray(dict.Raw("Domain")));
        var range = Numbers(document.ResolveArray(dict.Raw("Range")));
        var program = Tokenize(Encoding.Latin1.GetString(stream.Decoded()));
        return new PostScriptFunction(domain, range, program);
    }

    private static List<object> Tokenize(string source)
    {
        var tokens = new List<object>();
        int index = 0;
        while (index < source.Length)
        {
            char c = source[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c == '%')
            {
                while (index < source.Length && source[index] != '\n' && source[index] != '\r') index++;
                continue;
            }

            if (c == '{' || c == '}')
            {
                tokens.Add(c == '{' ? OpenBrace : CloseBrace);
                index++;
                continue;
            }

            if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            {
                int start = index;
                while (index < source.Length
                       && (char.IsDigit(source[index]) || source[index] == '.' || source[index] == '-'
                           || source[index] == 'e' || source[index] == 'E' || source[index] == '+'))
                {
                    index++;
                }

                if (double.TryParse(
                        source[start..index],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double number))
                {
                    tokens.Add(number);
                    continue;
                }

                index = start + 1;
                continue;
            }

            int wordStart = index;
            while (index < source.Length && !char.IsWhiteSpace(source[index])
                   && source[index] != '{' && source[index] != '}'
                   && source[index] != '/' && source[index] != '%')
            {
                index++;
            }

            if (index == wordStart)
            {
                index++;
                continue;
            }

            tokens.Add(source[wordStart..index]);
        }

        return tokens;
    }

    private static readonly object OpenBrace = new();
    private static readonly object CloseBrace = new();

    public override void Evaluate(double[] input, double[] output)
    {
        var stack = new List<double>(16);
        // Argumen dijalankan dalam tumpukan PostScript; hasil teratas = keluaran.
        for (int i = 0; i < InputCount; i++)
        {
            double value = i < input.Length ? input[i] : 0;
            int low = i * 2;
            int high = low + 1;
            if (high < _domain.Length)
            {
                value = Clamp(value, Math.Min(_domain[low], _domain[high]), Math.Max(_domain[low], _domain[high]));
            }

            stack.Add(value);
        }

        Execute(_program, 0, _program.Count, stack);

        for (int i = 0; i < OutputCount; i++)
        {
            double value = i < stack.Count ? stack[i] : 0;
            if (i * 2 + 1 < _range.Length)
            {
                value = Clamp(value, Math.Min(_range[i * 2], _range[i * 2 + 1]), Math.Max(_range[i * 2], _range[i * 2 + 1]));
            }

            output[i] = value;
        }
    }

    private void Execute(List<object> program, int start, int end, List<double> stack)
    {
        for (int index = start; index < end; index++)
        {
            object token = program[index];
            if (token is double number)
            {
                stack.Add(number);
                continue;
            }

            string name = (string)token;
            switch (name)
            {
                case "abs": Unary(stack, Math.Abs); break;
                case "add": Binary(stack, (a, b) => a + b); break;
                case "atan": Binary(stack, (a, b) => Math.Atan2(a, b) * 180.0 / Math.PI); break;
                case "ceiling": Unary(stack, Math.Ceiling); break;
                case "cos": Unary(stack, a => Math.Cos(a * Math.PI / 180.0)); break;
                case "cvi": Unary(stack, a => Math.Truncate(a)); break;
                case "cvr": break;
                case "div": Binary(stack, (a, b) => b == 0 ? 0 : a / b); break;
                case "exp": Binary(stack, Math.Pow); break;
                case "floor": Unary(stack, Math.Floor); break;
                case "idiv": Binary(stack, (a, b) => b == 0 ? 0 : Math.Truncate(a / b)); break;
                case "ln": Unary(stack, a => a > 0 ? Math.Log(a) : 0); break;
                case "log": Unary(stack, a => a > 0 ? Math.Log10(a) : 0); break;
                case "mod": Binary(stack, (a, b) => b == 0 ? 0 : a - b * Math.Truncate(a / b)); break;
                case "mul": Binary(stack, (a, b) => a * b); break;
                case "neg": Unary(stack, a => -a); break;
                case "round": Unary(stack, a => Math.Round(a, MidpointRounding.AwayFromZero)); break;
                case "sin": Unary(stack, a => Math.Sin(a * Math.PI / 180.0)); break;
                case "sqrt": Unary(stack, Math.Sqrt); break;
                case "sub": Binary(stack, (a, b) => a - b); break;
                case "truncate": Unary(stack, Math.Truncate); break;
                case "bitand": Binary(stack, (a, b) => (double)((long)a & (long)b)); break;
                case "bitor": Binary(stack, (a, b) => (double)((long)a | (long)b)); break;
                case "bitxor": Binary(stack, (a, b) => (double)((long)a ^ (long)b)); break;
                case "bitshift":
                {
                    if (stack.Count < 2) break;
                    double shift = stack[^1];
                    double value = stack[^2];
                    stack.RemoveRange(stack.Count - 2, 2);
                    stack.Add(shift >= 0
                        ? (double)((long)value << (int)shift)
                        : (double)((long)value >> (int)-shift));
                    break;
                }

                case "not": Unary(stack, a => a == 0 ? 1 : 0); break;
                case "and": Binary(stack, (a, b) => a != 0 && b != 0 ? 1 : 0); break;
                case "or": Binary(stack, (a, b) => a != 0 || b != 0 ? 1 : 0); break;
                case "xor": Binary(stack, (a, b) => (a != 0) ^ (b != 0) ? 1 : 0); break;
                case "true": stack.Add(1); break;
                case "false": stack.Add(0); break;
                case "eq": Test(stack, (a, b) => a == b); break;
                case "ne": Test(stack, (a, b) => a != b); break;
                case "gt": Test(stack, (a, b) => a > b); break;
                case "ge": Test(stack, (a, b) => a >= b); break;
                case "lt": Test(stack, (a, b) => a < b); break;
                case "le": Test(stack, (a, b) => a <= b); break;
                case "dup":
                    if (stack.Count > 0) stack.Add(stack[^1]);
                    break;
                case "exch":
                    if (stack.Count >= 2) (stack[^1], stack[^2]) = (stack[^2], stack[^1]);
                    break;
                case "pop":
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case "copy":
                {
                    if (stack.Count < 1) break;
                    int count = Math.Max(0, (int)stack[^1]);
                    stack.RemoveAt(stack.Count - 1);
                    if (count <= stack.Count)
                    {
                        for (int i = 0; i < count; i++) stack.Add(stack[stack.Count - count]);
                    }

                    break;
                }

                case "index":
                {
                    if (stack.Count < 1) break;
                    int position = (int)stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    int source = stack.Count - 1 - position;
                    stack.Add(source >= 0 && source < stack.Count ? stack[source] : 0);
                    break;
                }

                case "roll":
                {
                    if (stack.Count < 2) break;
                    int times = (int)stack[^1];
                    int count = (int)stack[^2];
                    stack.RemoveRange(stack.Count - 2, 2);
                    if (count <= 0 || count > stack.Count) break;
                    int offset = stack.Count - count;
                    var window = stack.GetRange(offset, count);
                    times = ((times % count) + count) % count;
                    for (int i = 0; i < count; i++)
                    {
                        stack[offset + i] = window[(i - times + count * 2) % count];
                    }

                    break;
                }

                case "if":
                {
                    if (stack.Count < 1) break;
                    bool condition = stack[^1] != 0;
                    stack.RemoveAt(stack.Count - 1);
                    int block = FindBlock(program, index + 1);
                    if (block < 0) break;
                    if (condition) Execute(program, index + 2, block, stack);
                    index = block;
                    break;
                }

                case "ifelse":
                {
                    if (stack.Count < 1) break;
                    bool condition = stack[^1] != 0;
                    stack.RemoveAt(stack.Count - 1);
                    int first = FindBlock(program, index + 1);
                    if (first < 0) break;
                    int second = FindBlock(program, first + 1);
                    if (second < 0) break;
                    if (condition) Execute(program, index + 2, first, stack);
                    else Execute(program, first + 2, second, stack);
                    index = second;
                    break;
                }

                default:
                    // Operator tak dikenal (mis. prosedur bernama): diabaikan.
                    break;
            }
        }
    }

    private static int FindBlock(List<object> program, int start)
    {
        int depth = 0;
        for (int i = start; i < program.Count; i++)
        {
            if (ReferenceEquals(program[i], OpenBrace))
            {
                depth++;
                continue;
            }

            if (ReferenceEquals(program[i], CloseBrace))
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static void Unary(List<double> stack, Func<double, double> operation)
    {
        if (stack.Count < 1) return;
        double value = stack[^1];
        stack[^1] = operation(value);
    }

    private static void Binary(List<double> stack, Func<double, double, double> operation)
    {
        if (stack.Count < 2) return;
        double b = stack[^1];
        double a = stack[^2];
        stack.RemoveRange(stack.Count - 2, 2);
        stack.Add(operation(a, b));
    }

    private static void Test(List<double> stack, Func<double, double, bool> test)
    {
        if (stack.Count < 2) return;
        double b = stack[^1];
        double a = stack[^2];
        stack.RemoveRange(stack.Count - 2, 2);
        stack.Add(test(a, b) ? 1 : 0);
    }

    private static double[] Numbers(PdfArray? array)
    {
        if (array is null) return Array.Empty<double>();
        var values = new double[array.Count];
        for (int i = 0; i < array.Count; i++) values[i] = (array.At(i) as PdfNumber)?.Value ?? 0;
        return values;
    }
}
