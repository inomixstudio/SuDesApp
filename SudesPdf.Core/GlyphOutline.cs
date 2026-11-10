using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

/// <summary>
/// Bentuk glif dalam satuan font (y ke atas). Geometri disimpan sebagai aliran
/// perintah bertumpuk agar hemat memori dan mudah dipetakan ulang ke ukuran
/// berapa pun: 0 = move(x,y), 1 = line(x,y), 2 = quad(cx,cy,x,y),
/// 3 = cubic(x1,y1,x2,y2,x,y), 4 = close.
/// </summary>
public sealed class GlyphOutline
{
    public static readonly GlyphOutline Empty = new(Array.Empty<double>());

    public GlyphOutline(double[] commands) => Commands = commands;

    public double[] Commands { get; }

    public bool IsEmpty => Commands.Length == 0;

    /// <summary>Petakan glif ke jalur perangkat melalui matriks yang diberikan.</summary>
    public void AppendTo(PdfPathBuilder builder, in PdfMatrix matrix)
    {
        double[] commands = Commands;
        int index = 0;
        while (index < commands.Length)
        {
            switch ((int)commands[index++])
            {
                case 0:
                {
                    var point = matrix.Transform(commands[index], commands[index + 1]);
                    index += 2;
                    builder.MoveTo(point.X, point.Y);
                    break;
                }

                case 1:
                {
                    var point = matrix.Transform(commands[index], commands[index + 1]);
                    index += 2;
                    builder.LineTo(point.X, point.Y);
                    break;
                }

                case 2:
                {
                    var control = matrix.Transform(commands[index], commands[index + 1]);
                    var point = matrix.Transform(commands[index + 2], commands[index + 3]);
                    index += 4;
                    builder.QuadTo(control.X, control.Y, point.X, point.Y);
                    break;
                }

                case 3:
                {
                    var c1 = matrix.Transform(commands[index], commands[index + 1]);
                    var c2 = matrix.Transform(commands[index + 2], commands[index + 3]);
                    var point = matrix.Transform(commands[index + 4], commands[index + 5]);
                    index += 6;
                    builder.CurveTo(c1.X, c1.Y, c2.X, c2.Y, point.X, point.Y);
                    break;
                }

                default:
                    builder.Close();
                    break;
            }
        }
    }

    /// <summary>Penyusun aliran perintah dari pengurai font.</summary>
    public sealed class Builder
    {
        private readonly List<double> _commands = new(64);

        public bool IsEmpty => _commands.Count == 0;

        public void MoveTo(double x, double y)
        {
            _commands.Add(0);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void LineTo(double x, double y)
        {
            _commands.Add(1);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void QuadTo(double cx, double cy, double x, double y)
        {
            _commands.Add(2);
            _commands.Add(cx);
            _commands.Add(cy);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void CurveTo(double x1, double y1, double x2, double y2, double x, double y)
        {
            _commands.Add(3);
            _commands.Add(x1);
            _commands.Add(y1);
            _commands.Add(x2);
            _commands.Add(y2);
            _commands.Add(x);
            _commands.Add(y);
        }

        public void Close() => _commands.Add(4);

        public GlyphOutline Build() => _commands.Count == 0 ? Empty : new GlyphOutline(_commands.ToArray());
    }
}

