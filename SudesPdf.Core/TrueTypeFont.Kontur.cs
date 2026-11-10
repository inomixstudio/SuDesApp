using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SudesPdf.Core;

public sealed partial class TrueTypeFont : IPdfGlyphSource
{

    private static void AppendContour(
        GlyphOutline.Builder builder, byte[] flags, int[] xs, int[] ys, int first, int last)
    {
        int count = last - first + 1;
        if (count < 2) return;

        bool OnCurve(int index) => (flags[index] & 1) != 0;

        // Titik awal: on-curve pertama; bila tidak ada, titik tengah dua off-curve.
        int startIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (OnCurve(first + i))
            {
                startIndex = i;
                break;
            }
        }

        double startX;
        double startY;
        int scan;
        if (startIndex < 0)
        {
            int a = first;
            int b = first + (count > 1 ? 1 : 0);
            startX = (xs[a] + xs[b]) / 2.0;
            startY = (ys[a] + ys[b]) / 2.0;
            scan = 0;
        }
        else
        {
            startX = xs[first + startIndex];
            startY = ys[first + startIndex];
            scan = startIndex + 1;
        }

        builder.MoveTo(startX, startY);

        double currentX = startX;
        double currentY = startY;
        double? pendingX = null;
        double? pendingY = null;

        for (int step = 0; step < count; step++)
        {
            int index = first + ((scan + step) % count);
            double px = xs[index];
            double py = ys[index];
            if (OnCurve(index))
            {
                if (pendingX is not null)
                {
                    builder.QuadTo(pendingX.Value, pendingY!.Value, px, py);
                    pendingX = null;
                }
                else
                {
                    builder.LineTo(px, py);
                }

                currentX = px;
                currentY = py;
            }
            else if (pendingX is null)
            {
                pendingX = px;
                pendingY = py;
            }
            else
            {
                double midX = (pendingX.Value + px) / 2.0;
                double midY = (pendingY!.Value + py) / 2.0;
                builder.QuadTo(pendingX.Value, pendingY.Value, midX, midY);
                currentX = midX;
                currentY = midY;
                pendingX = px;
                pendingY = py;
            }
        }

        if (pendingX is not null)
        {
            builder.QuadTo(pendingX.Value, pendingY!.Value, startX, startY);
        }
        else if (Math.Abs(currentX - startX) > 1e-9 || Math.Abs(currentY - startY) > 1e-9)
        {
            builder.LineTo(startX, startY);
        }

        builder.Close();
    }

    private GlyphOutline? BuildComposite(int offset, int depth)
    {
        var builder = new GlyphOutline.Builder();
        int cursor = offset + 10;
        int guard = 0;

        while (guard++ < 64)
        {
            if (cursor + 4 > _data.Length) break;
            int flags = ReadUInt16(cursor);
            int glyphIndex = ReadUInt16(cursor + 2);
            cursor += 4;

            int arg1;
            int arg2;
            if ((flags & 1) != 0)
            {
                arg1 = ReadInt16(cursor);
                arg2 = ReadInt16(cursor + 2);
                cursor += 4;
            }
            else
            {
                arg1 = (sbyte)_data[cursor];
                arg2 = (sbyte)_data[cursor + 1];
                cursor += 2;
            }

            double a = 1;
            double b = 0;
            double c = 0;
            double d = 1;
            if ((flags & 8) != 0)
            {
                a = d = ReadF2Dot14(cursor);
                cursor += 2;
            }
            else if ((flags & 64) != 0)
            {
                a = ReadF2Dot14(cursor);
                d = ReadF2Dot14(cursor + 2);
                cursor += 4;
            }
            else if ((flags & 128) != 0)
            {
                a = ReadF2Dot14(cursor);
                b = ReadF2Dot14(cursor + 2);
                c = ReadF2Dot14(cursor + 4);
                d = ReadF2Dot14(cursor + 6);
                cursor += 8;
            }

            var component = BuildGlyph(glyphIndex, depth + 1);
            double dx = (flags & 2) != 0 ? arg1 : 0;
            double dy = (flags & 2) != 0 ? arg2 : 0;
            if ((flags & 2) == 0)
            {
                // Titik acuan: jarang dipakai; abaikan pergeseran agar tetap aman.
                dx = 0;
                dy = 0;
            }

            if (component is not null)
            {
                AppendTransformed(builder, component, a, b, c, d, dx, dy);
            }

            if ((flags & 32) == 0) break;
        }

        return builder.IsEmpty ? null : builder.Build();
    }

    private static void AppendTransformed(
        GlyphOutline.Builder builder, GlyphOutline outline,
        double a, double b, double c, double d, double dx, double dy)
    {
        double[] commands = outline.Commands;
        int index = 0;
        while (index < commands.Length)
        {
            int command = (int)commands[index++];
            switch (command)
            {
                case 0:
                case 1:
                {
                    double x = commands[index++];
                    double y = commands[index++];
                    double tx = a * x + c * y + dx;
                    double ty = b * x + d * y + dy;
                    if (command == 0) builder.MoveTo(tx, ty);
                    else builder.LineTo(tx, ty);
                    break;
                }

                case 2:
                {
                    double cx = commands[index++];
                    double cy = commands[index++];
                    double x = commands[index++];
                    double y = commands[index++];
                    builder.QuadTo(a * cx + c * cy + dx, b * cx + d * cy + dy, a * x + c * y + dx, b * x + d * y + dy);
                    break;
                }

                case 3:
                {
                    double x1 = commands[index++];
                    double y1 = commands[index++];
                    double x2 = commands[index++];
                    double y2 = commands[index++];
                    double x = commands[index++];
                    double y = commands[index++];
                    builder.CurveTo(
                        a * x1 + c * y1 + dx, b * x1 + d * y1 + dy,
                        a * x2 + c * y2 + dx, b * x2 + d * y2 + dy,
                        a * x + c * y + dx, b * x + d * y + dy);
                    break;
                }

                default:
                    builder.Close();
                    break;
            }
        }
    }


}
