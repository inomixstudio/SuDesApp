using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;

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

