using System;
using System.Collections.Generic;

namespace SudesPdf.Core;

public sealed partial class CffFont : IPdfGlyphSource
{
    // --------------------------------------------------------- Tipe bantu

    private readonly struct Range
    {
        public Range(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }

    private sealed class Index
    {
        public List<int> Offsets { get; } = new();
        public int DataStart { get; set; }
        public int Count { get; set; }

        /// <summary>Rentang data entri ke-<paramref name="index"/>.</summary>
        public Range At(int index)
        {
            if (index < 0 || index + 1 >= Offsets.Count) return default;
            int start = DataStart + Offsets[index];
            int end = DataStart + Offsets[index + 1];
            return new Range(start, Math.Max(0, end - start));
        }
    }

    private sealed class CffDict
    {
        private readonly Dictionary<int, double[]> _values = new();

        public bool Has(string key) => _values.ContainsKey(KeyOf(key));

        public void Set(int operation, int operandIndex, double value)
        {
            if (!_values.TryGetValue(operation, out var array))
            {
                array = new double[8];
                _values[operation] = array;
            }

            if (operandIndex < array.Length) array[operandIndex] = value;
        }

        public double? Get(string key, int index = 0)
        {
            int operation = KeyOf(key);
            if (!_values.TryGetValue(operation, out var array)) return null;
            return index < array.Length ? array[index] : null;
        }

        private static int KeyOf(string key) => key switch
        {
            "charset" => 15,
            "Encoding" => 16,
            "CharStrings" => 17,
            "Private" => 18,
            "Subrs" => 19,
            "ROS" => 1230,
            "FDArray" => 1236,
            "FDSelect" => 1237,
            "defaultWidthX" => 20,
            "nominalWidthX" => 21,
            _ => -1
        };
    }

    private sealed class PrivateDict
    {
        public double DefaultWidthX { get; set; }

        public double NominalWidthX { get; set; }

        public Index? Subrs { get; set; }
    }

    /// <summary>Penafsir charstring Type2 (ISO/IEC 14496-22 §C.5).</summary>
    private sealed class CharstringRunner
    {
        private readonly CffFont _font;
        private readonly PrivateDict _private;
        private readonly GlyphOutline.Builder _builder;
        private readonly double[] _stack = new double[96];
        private readonly double[] _transient = new double[32];
        private int _sp;
        private double _x;
        private double _y;
        private int _hints;
        private bool _widthParsed;
        private bool _open;

        public CharstringRunner(CffFont font, PrivateDict privateDict, GlyphOutline.Builder builder)
        {
            _font = font;
            _private = privateDict;
            _builder = builder;
        }

        public void Run(int start, int end, int depth)
        {
            if (depth > 10) return;
            int index = start;
            while (index < end)
            {
                int b0 = _font._data[index];
                if (b0 >= 32 || b0 == 28 || b0 == 255)
                {
                    if (!ReadOperand(ref index, end)) return;
                    continue;
                }

                index++;
                switch (b0)
                {
                    case 1:
                    case 3:
                    case 18:
                    case 23:
                        CountStems();
                        break;
                    case 19:
                    case 20:
                        CountStems();
                        index += (_hints + 7) / 8;
                        break;
                    case 21:
                        ParseWidth(2);
                        Move(_stack[_sp - 2], _stack[_sp - 1]);
                        _sp = 0;
                        break;
                    case 22:
                        ParseWidth(1);
                        Move(_stack[_sp - 1], 0);
                        _sp = 0;
                        break;
                    case 4:
                        ParseWidth(1);
                        Move(0, _stack[_sp - 1]);
                        _sp = 0;
                        break;
                    case 5:
                        for (int i = 0; i + 1 < _sp; i += 2) Line(_stack[i], _stack[i + 1]);
                        _sp = 0;
                        break;
                    case 6:
                    case 7:
                    {
                        bool horizontal = b0 == 6;
                        for (int i = 0; i < _sp; i++)
                        {
                            if (horizontal) Line(_stack[i], 0);
                            else Line(0, _stack[i]);
                            horizontal = !horizontal;
                        }

                        _sp = 0;
                        break;
                    }

                    case 8:
                        for (int i = 0; i + 5 < _sp; i += 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    case 24:
                    {
                        int i = 0;
                        for (; i + 5 < _sp - 2; i += 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        if (_sp - i >= 2) Line(_stack[i], _stack[i + 1]);
                        _sp = 0;
                        break;
                    }

                    case 25:
                    {
                        int i = 0;
                        for (; i + 1 < _sp - 6; i += 2) Line(_stack[i], _stack[i + 1]);
                        if (_sp - i >= 6) Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    }

                    case 26:
                    {
                        int i = 0;
                        double dx1 = _sp % 4 == 1 ? _stack[i++] : 0;
                        double dy1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4)
                        {
                            Curve(dx1, dy1, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3]);
                            dx1 = 0;
                            dy1 = 0;
                        }

                        _sp = 0;
                        break;
                    }

                    case 27:
                    {
                        int i = 0;
                        double dy1 = _sp % 4 == 1 ? _stack[i++] : 0;
                        double dx1 = _stack[i++];
                        for (; i + 3 < _sp; i += 4)
                        {
                            Curve(dx1, dy1, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3]);
                            dx1 = 0;
                            dy1 = 0;
                        }

                        _sp = 0;
                        break;
                    }

                    case 30:
                    case 31:
                    {
                        bool horizontal = b0 == 31;
                        int i = 0;
                        while (_sp - i >= 4)
                        {
                            if (horizontal)
                            {
                                double dxa = _stack[i];
                                double dxb = _stack[i + 1];
                                double dyb = _stack[i + 2];
                                int remaining = _sp - i;
                                double dyc = remaining == 5 ? _stack[i + 4] : 0;
                                double dxc = remaining == 5 ? 0 : _stack[i + 3];
                                Curve(dxa, 0, dxb, dyb, dxc, dyc);
                            }
                            else
                            {
                                double dya = _stack[i];
                                double dxb = _stack[i + 1];
                                double dyb = _stack[i + 2];
                                int remaining = _sp - i;
                                double dxc = remaining == 5 ? _stack[i + 4] : 0;
                                double dyc = remaining == 5 ? 0 : _stack[i + 3];
                                Curve(0, dya, dxb, dyb, dxc, dyc);
                            }

                            horizontal = !horizontal;
                            i += 4;
                        }

                        _sp = 0;
                        break;
                    }

                    case 10:
                        CallSubr(_private.Subrs, SubrBias(_private.Subrs?.Count ?? 0), depth);
                        break;
                    case 29:
                        CallSubr(_font._globalSubrs, _font._globalSubrBias, depth);
                        break;
                    case 11:
                        return;
                    case 14:
                        ParseWidth(0);
                        _sp = 0;
                        CloseContour();
                        return;
                    case 12:
                    {
                        if (index >= end) return;
                        int operation = _font._data[index++];
                        ExecuteEscape(operation);
                        break;
                    }

                    default:
                        _sp = 0;
                        break;
                }
            }
        }

        private bool ReadOperand(ref int index, int end)
        {
            int b0 = _font._data[index];
            if (b0 == 28)
            {
                if (index + 3 > end) return false;
                Push((short)((_font._data[index + 1] << 8) | _font._data[index + 2]));
                index += 3;
                return true;
            }

            if (b0 == 255)
            {
                if (index + 5 > end) return false;
                int value = (_font._data[index + 1] << 24) | (_font._data[index + 2] << 16)
                            | (_font._data[index + 3] << 8) | _font._data[index + 4];
                Push(value / 65536.0);
                index += 5;
                return true;
            }

            if (b0 >= 32 && b0 <= 246)
            {
                Push(b0 - 139);
                index++;
                return true;
            }

            if (b0 >= 247 && b0 <= 250)
            {
                if (index + 2 > end) return false;
                Push((b0 - 247) * 256 + _font._data[index + 1] + 108);
                index += 2;
                return true;
            }

            if (b0 >= 251 && b0 <= 254)
            {
                if (index + 2 > end) return false;
                Push(-(b0 - 251) * 256 - _font._data[index + 1] - 108);
                index += 2;
                return true;
            }

            return false;
        }

        private void Push(double value)
        {
            if (_sp < _stack.Length) _stack[_sp++] = value;
        }

        private void ParseWidth(int expectedArgs)
        {
            if (!_widthParsed)
            {
                _widthParsed = true;
                if (_sp > expectedArgs)
                {
                    // Argumen pertama adalah lebar (defaultWidthX/nominalWidthX);
                    // lebar tidak dipakai karena PDF punya /Widths sendiri.
                    for (int i = 1; i < _sp; i++) _stack[i - 1] = _stack[i];
                    _sp--;
                }
            }
        }

        private void CountStems()
        {
            if (!_widthParsed)
            {
                _widthParsed = true;
                if (_sp % 2 == 1)
                {
                    for (int i = 1; i < _sp; i++) _stack[i - 1] = _stack[i];
                    _sp--;
                }
            }

            _hints += _sp / 2;
            _sp = 0;
        }

        private void Move(double dx, double dy)
        {
            CloseContour();
            _x += dx;
            _y += dy;
            _builder.MoveTo(_x, _y);
            _open = true;
        }

        private void Line(double dx, double dy)
        {
            if (!_open) Move(0, 0);
            _x += dx;
            _y += dy;
            _builder.LineTo(_x, _y);
        }

        private void Curve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            if (!_open) Move(0, 0);
            double c1x = _x + dx1;
            double c1y = _y + dy1;
            double c2x = c1x + dx2;
            double c2y = c1y + dy2;
            _x = c2x + dx3;
            _y = c2y + dy3;
            _builder.CurveTo(c1x, c1y, c2x, c2y, _x, _y);
        }

        private void CloseContour()
        {
            if (!_open) return;
            _builder.Close();
            _open = false;
        }

        private void CallSubr(Index? index, int bias, int depth)
        {
            if (index is null || _sp < 1) return;
            int subrNumber = (int)_stack[--_sp] + bias;
            if (subrNumber < 0 || subrNumber >= index.Count) return;
            var range = index.At(subrNumber);
            if (range.Length <= 0) return;
            Run(range.Start, range.Start + range.Length, depth + 1);
        }

        private void ExecuteEscape(int operation)
        {
            switch (operation)
            {
                case 34: // hflex
                    if (_sp >= 7)
                    {
                        double dx1 = _stack[0], dx2 = _stack[1], dx3 = _stack[2], dx4 = _stack[3];
                        double dx5 = _stack[4], dx6 = _stack[5];
                        Curve(dx1, 0, dx2, 0, dx3, 0);
                        Curve(dx4, 0, dx5, 0, dx6, 0);
                    }

                    break;
                case 35: // flex
                    if (_sp >= 13)
                    {
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        Curve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                    }

                    break;
                case 36: // hflex1
                    if (_sp >= 9)
                    {
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                        Curve(_stack[5], 0, _stack[6], 0, _stack[7], _stack[8]);
                    }

                    break;
                case 37: // flex1
                    if (_sp >= 11)
                    {
                        double dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                        double dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                        Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        Curve(_stack[6], _stack[7], _stack[8], _stack[9],
                            Math.Abs(dx) > Math.Abs(dy) ? _stack[10] : 0,
                            Math.Abs(dx) > Math.Abs(dy) ? 0 : _stack[10]);
                    }

                    break;
                case 3: Binary((a, b) => a != 0 && b != 0 ? 1 : 0); break;
                case 4: Binary((a, b) => a != 0 || b != 0 ? 1 : 0); break;
                case 5: Unary(a => a == 0 ? 1 : 0); break;
                case 9: Unary(Math.Abs); break;
                case 10: Binary((a, b) => a + b); break;
                case 11: Binary((a, b) => a - b); break;
                case 12: Binary((a, b) => b == 0 ? 0 : a / b); break;
                case 14: Unary(a => -a); break;
                case 15: Binary((a, b) => a == b ? 1 : 0); break;
                case 18: if (_sp > 0) _sp--; break;
                case 20:
                    if (_sp >= 2)
                    {
                        int slot = (int)_stack[--_sp];
                        double value = _stack[--_sp];
                        if (slot >= 0 && slot < _transient.Length) _transient[slot] = value;
                    }

                    break;
                case 21:
                    if (_sp >= 1)
                    {
                        int slot = (int)_stack[_sp - 1];
                        _stack[_sp - 1] = slot >= 0 && slot < _transient.Length ? _transient[slot] : 0;
                    }

                    break;
                case 22:
                    if (_sp >= 4)
                    {
                        double s2 = _stack[--_sp];
                        double s1 = _stack[--_sp];
                        double v2 = _stack[--_sp];
                        double v1 = _stack[--_sp];
                        Push(s1 <= s2 ? v1 : v2);
                    }

                    break;
                case 23: Push(Random.Shared.NextDouble()); break;
                case 24: Binary((a, b) => a * b); break;
                case 26: Unary(a => a < 0 ? 0 : Math.Sqrt(a)); break;
                case 27: if (_sp > 0) Push(_stack[_sp - 1]); break;
                case 28:
                    if (_sp >= 2)
                    {
                        (_stack[_sp - 1], _stack[_sp - 2]) = (_stack[_sp - 2], _stack[_sp - 1]);
                    }

                    break;
                case 29:
                    if (_sp >= 1)
                    {
                        int offset = (int)_stack[_sp - 1];
                        int source = _sp - 2 - offset;
                        _stack[_sp - 1] = source >= 0 && source < _sp - 1 ? _stack[source] : 0;
                    }

                    break;
                case 30:
                    if (_sp >= 2)
                    {
                        int times = (int)_stack[_sp - 1];
                        int count = (int)_stack[_sp - 2];
                        _sp -= 2;
                        if (count > 0 && count <= _sp)
                        {
                            times = ((times % count) + count) % count;
                            for (int round = 0; round < times; round++)
                            {
                                double last = _stack[_sp - 1];
                                for (int i = _sp - 1; i > _sp - count; i--) _stack[i] = _stack[i - 1];
                                _stack[_sp - count] = last;
                            }
                        }
                    }

                    break;
            }
        }

        private void Unary(Func<double, double> operation)
        {
            if (_sp < 1) return;
            _stack[_sp - 1] = operation(_stack[_sp - 1]);
        }

        private void Binary(Func<double, double, double> operation)
        {
            if (_sp < 2) return;
            double b = _stack[--_sp];
            double a = _stack[--_sp];
            Push(operation(a, b));
        }
    }


}
