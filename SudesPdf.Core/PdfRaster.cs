using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SudesPdf.Core;

/// <summary>Permukaan raster BGRA (8 bit/kanal, tidak premultiplied).</summary>
public sealed class RasterImage
{
    public RasterImage(int width, int height, bool transparent = false)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new byte[Width * Height * 4];
        if (!transparent)
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                Pixels[i] = 255;
                Pixels[i + 1] = 255;
                Pixels[i + 2] = 255;
                Pixels[i + 3] = 255;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Biru, hijau, merah, alfa.</summary>
    public byte[] Pixels { get; }

    public void Clear(byte blue, byte green, byte red, byte alpha)
    {
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            Pixels[i] = blue;
            Pixels[i + 1] = green;
            Pixels[i + 2] = red;
            Pixels[i + 3] = alpha;
        }
    }

    public RasterImage Crop(PdfRect area)
    {
        int x0 = Math.Max(0, (int)Math.Floor(area.Left));
        int y0 = Math.Max(0, (int)Math.Floor(area.Bottom));
        int x1 = Math.Min(Width, (int)Math.Ceiling(area.Right));
        int y1 = Math.Min(Height, (int)Math.Ceiling(area.Top));
        int w = Math.Max(1, x1 - x0);
        int h = Math.Max(1, y1 - y0);
        var result = new RasterImage(w, h, transparent: true);
        for (int y = 0; y < h; y++)
        {
            int sourceRow = ((y0 + y) * Width + x0) * 4;
            int targetRow = y * w * 4;
            Array.Copy(Pixels, sourceRow, result.Pixels, targetRow, w * 4);
        }

        return result;
    }

    /// <summary>Enkode PNG (RGBA) untuk diagnostik dan pengujian.</summary>
    public byte[] EncodePng()
    {
        using var output = new MemoryStream();
        WriteBigEndian(output, 0x89504E47);
        WriteBigEndian(output, 0x0D0A1A0A);

        var header = new byte[13];
        WriteInt(header, 0, Width);
        WriteInt(header, 4, Height);
        header[8] = 8;   // kedalaman bit
        header[9] = 6;   // RGBA
        WriteChunk(output, "IHDR", header);

        var raw = new byte[Height * (Width * 4 + 1)];
        for (int y = 0; y < Height; y++)
        {
            int target = y * (Width * 4 + 1);
            raw[target] = 0;
            for (int x = 0; x < Width; x++)
            {
                int source = (y * Width + x) * 4;
                int destination = target + 1 + x * 4;
                raw[destination] = Pixels[source + 2];
                raw[destination + 1] = Pixels[source + 1];
                raw[destination + 2] = Pixels[source];
                raw[destination + 3] = Pixels[source + 3];
            }
        }

        WriteChunk(output, "IDAT", PdfFilters.ZlibCompress(raw));
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteInt(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in first) crc = UpdateCrc(crc, b);
        foreach (byte b in second) crc = UpdateCrc(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc;
    }
}

/// <summary>
/// Penggambar raster dengan antialias: isi jalur (aturan nonzero/even-odd),
/// garis (dengan ujung/sambungan dan pola putus), gambar, dan gradien.
/// Cakupan dihitung dengan supersampling vertikal + luas horizontal eksak.
/// </summary>
public sealed class Rasterizer
{
    private const int SubSamples = 4;
    private readonly RasterImage _image;
    private readonly int _width;
    private readonly int _height;
    private float[] _coverage = Array.Empty<float>();
    private double[] _luasTepat = Array.Empty<double>();
    private double[] _tutupTepat = Array.Empty<double>();
    private readonly List<Edge> _edges = new(256);
    private readonly List<(float X, int Winding)> _crossings = new(64);

    public Rasterizer(RasterImage image)
    {
        _image = image;
        _width = image.Width;
        _height = image.Height;
    }

    public RasterImage Image => _image;

    private readonly struct Edge
    {
        public Edge(double x0, double y0, double x1, double y1)
        {
            // Arah asli tepi menentukan belitan (aturan nonzero).
            Winding = y1 > y0 ? 1 : -1;
            if (y0 > y1)
            {
                (y0, y1) = (y1, y0);
                (x0, x1) = (x1, x0);
            }

            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }

        public int Winding { get; }

        /// <summary>Posisi x tepi pada ketinggian y (tepi selalu lurus).</summary>
        public double XAt(double y) =>
            Y1 - Y0 <= 0 ? X0 : X0 + (y - Y0) * (X1 - X0) / (Y1 - Y0);
    }

    // ----------------------------------------------------------------- Isi

    /// <param name="subSamples">
    /// Jumlah sampel vertikal per baris piksel. Bidang/garis memakai nilai bawaan;
    /// teks memakai sampel lebih banyak supaya tepi bulat huruf tebal (O, S, G)
    /// tidak bergerigi pada ukuran kecil.
    /// </param>
    public void Fill(
        PdfPath path, PdfFillRule rule, double red, double green, double blue, double alpha,
        PdfBlendMode blend, byte[]? clipMask, double coverageGamma = 1.0, int subSamples = SubSamples,
        byte[]? coverageTable = null)
    {
        if (path.IsEmpty || alpha <= 0) return;
        BuildEdges(path, closedOnly: false);
        if (_edges.Count == 0) return;

        int samples = Math.Max(1, subSamples);
        int minY = Math.Max(0, (int)Math.Floor(path.Bounds.Bottom));
        int maxY = Math.Min(_height - 1, (int)Math.Ceiling(path.Bounds.Top));
        int minX = Math.Max(0, (int)Math.Floor(path.Bounds.Left));
        int maxX = Math.Min(_width - 1, (int)Math.Ceiling(path.Bounds.Right));
        if (minY > maxY || minX > maxX) return;

        EnsureCoverage(_width);
        for (int y = minY; y <= maxY; y++)
        {
            Array.Clear(_coverage, 0, _width);
            bool any = false;
            for (int sample = 0; sample < samples; sample++)
            {
                double scanY = y + (sample + 0.5) / samples;
                any |= AccumulateSpan(scanY, rule, 1.0f / samples);
            }

            if (!any) continue;
            CompositeRow(y, minX, maxX, red, green, blue, alpha, blend, clipMask, coverageGamma, coverageTable);
        }
    }

    /// <summary>
    /// Isi jalur dengan cakupan luas yang EKSAK — tiruan rasterizer FreeType yang
    /// dipakai pdfium, bukan perkiraan dari beberapa sampel per piksel.
    ///
    /// Setiap tepi lurus di dalam satu baris piksel menyumbangkan LUAS trapesium
    /// yang dilaluinya kepada tiap kolom, sedangkan kolom di kanan tepi mendapat
    /// penutup (cover) penuh. Cakupan sebuah piksel lalu = jumlah penutup dari
    /// kiri + luas bagiannya sendiri, yaitu luas huruf yang benar-benar jatuh di
    /// piksel itu. Karena angkanya sebanding dengan luas, tepi melengkung dan
    /// batang huruf tebal tidak lagi bergerigi atau tampak bergaris seperti pada
    /// pengambilan sampel (8 sampel vertikal hanya punya 9 tingkat keabuan).
    ///
    /// Aturan even-odd tidak bisa dihitung dari penjumlahan belitan, jadi jalur
    /// itu tetap memakai <see cref="Fill"/>.
    /// </summary>
    public void FillTepat(
        PdfPath path, PdfFillRule rule, double red, double green, double blue, double alpha,
        PdfBlendMode blend, byte[]? clipMask, double coverageGamma = 1.0, byte[]? coverageTable = null)
    {
        if (path.IsEmpty || alpha <= 0) return;
        if (rule != PdfFillRule.NonZero)
        {
            Fill(path, rule, red, green, blue, alpha, blend, clipMask, coverageGamma, SubSamples, coverageTable);
            return;
        }

        BuildEdges(path, closedOnly: false);
        if (_edges.Count == 0) return;

        int minY = Math.Max(0, (int)Math.Floor(path.Bounds.Bottom));
        int maxY = Math.Min(_height - 1, (int)Math.Ceiling(path.Bounds.Top));
        int minX = Math.Max(0, (int)Math.Floor(path.Bounds.Left));
        int maxX = Math.Min(_width - 1, (int)Math.Ceiling(path.Bounds.Right));
        if (minY > maxY || minX > maxX) return;

        EnsureCoverage(_width);
        EnsureLuasTepat(_width);

        for (int y = minY; y <= maxY; y++)
        {
            Array.Clear(_luasTepat, 0, maxX + 1);
            Array.Clear(_tutupTepat, 0, maxX + 1);

            double atas = y;
            double bawah = y + 1;
            double tutupAwal = 0;
            bool ada = false;

            foreach (var edge in _edges)
            {
                if (edge.Y1 <= atas || edge.Y0 >= bawah) continue;

                double ya = Math.Max(atas, edge.Y0);
                double yb = Math.Min(bawah, edge.Y1);
                double tinggi = yb - ya;
                if (tinggi <= 0) continue;

                double xa = edge.XAt(ya);
                double xb = edge.XAt(yb);
                double arah = edge.Winding;

                int kolomKiri = (int)Math.Floor(Math.Min(xa, xb));
                int kolomKanan = (int)Math.Floor(Math.Max(xa, xb));

                // Tepi yang seluruhnya di kanan gambar tidak menyentuh piksel
                // terlihat sama sekali.
                if (kolomKiri > maxX) continue;

                // Tepi yang seluruhnya di kiri piksel pertama memberi cakupan
                // penuh pada SEMUA piksel terlihat di baris ini.
                if (kolomKanan < minX)
                {
                    tutupAwal += arah * tinggi;
                    ada = true;
                    continue;
                }

                // Kolom di kanan tepi tertutup penuh setinggi bagian tepi di
                // baris ini; penandanya ditaruh tepat setelah kolom terakhir
                // yang masih tersentuh sebagian, lalu dibaca SETELAH piksel itu
                // (bukan sebelum) supaya kolom batas tidak ikut penuh.
                int awalPenuh = kolomKanan + 1;
                if (awalPenuh <= maxX) _tutupTepat[awalPenuh] += arah * tinggi;

                int dari = Math.Max(0, kolomKiri);
                int sampai = Math.Min(kolomKanan, maxX);
                for (int kolom = dari; kolom <= sampai; kolom++)
                {
                    // Luas kolom yang tertutup: tinggi dikurangi bagian yang
                    // masih di kiri tepi, yaitu ∫ clamp(kolom + 1 − x, 0, 1).
                    _luasTepat[kolom] += arah
                        * (tinggi - LuasTerpotong(xa - kolom, xb - kolom, tinggi));
                }

                ada = true;
            }

            if (!ada) continue;

            double tutup = tutupAwal;
            for (int x = minX; x <= maxX; x++)
            {
                // Penanda "kolom ini dan seterusnya tertutup penuh" dibaca SEBELUM
                // luas kolom ditambahkan: penandanya ditaruh di kolom pertama yang
                // sudah penuh (kolomKanan + 1), jadi kolom batas itu sendiri harus
                // ikut menerimanya.
                tutup += _tutupTepat[x];

                // Belitan di dalam huruf bisa bernilai +1 atau −1 tergantung arah
                // kontur di ruang perangkat; nonzero menganggap keduanya terisi,
                // jadi cakupan diambil nilai mutlaknya. Kontur berlubang (mis.
                // lawang "o") berbelitan berlawanan sehingga jumlahnya nol di
                // lubang, tepat seperti penggabungan belitan di <see cref="Fill"/>.
                double nilai = tutup + _luasTepat[x];
                if (nilai < 0) nilai = -nilai;
                _coverage[x] = nilai >= 1 ? 1f : (float)nilai;
            }

            CompositeRow(y, minX, maxX, red, green, blue, alpha, blend, clipMask, coverageGamma, coverageTable);
        }
    }

    /// <summary>
    /// ∫ clamp(u(t), 0, 1) dt sepanjang satu baris piksel, dengan u berubah
    /// linear dari <paramref name="ua"/> ke <paramref name="ub"/>. Inilah luas
    /// bagian kolom yang tertutup tepi. Hasilnya dibagi dengan panjang u agar
    /// tidak membagi nol pada tepi yang hampir tegak.
    /// </summary>
    private static double LuasTerpotong(double ua, double ub, double tinggi)
    {
        double selisih = ub - ua;
        if (Math.Abs(selisih) < 1e-12)
        {
            return tinggi * Math.Clamp(ua, 0.0, 1.0);
        }

        return tinggi * (PrimitifTerpotong(ub) - PrimitifTerpotong(ua)) / selisih;
    }

    /// <summary>∫₀^u clamp(t, 0, 1) dt.</summary>
    private static double PrimitifTerpotong(double u) =>
        u <= 0 ? 0 : u >= 1 ? u - 0.5 : 0.5 * u * u;

    private void EnsureLuasTepat(int lebar)
    {
        if (_luasTepat.Length >= lebar) return;
        _luasTepat = new double[lebar];
        _tutupTepat = new double[lebar];
    }

    /// <summary>Hitung cakupan jalur ke topeng 8-bit (dipakai untuk kliping).</summary>
    public void RenderCoverage(PdfPath path, PdfFillRule rule, byte[] mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (path.IsEmpty) return;
        BuildEdges(path, closedOnly: false);
        if (_edges.Count == 0) return;

        int minY = Math.Max(0, (int)Math.Floor(path.Bounds.Bottom));
        int maxY = Math.Min(_height - 1, (int)Math.Ceiling(path.Bounds.Top));
        EnsureCoverage(_width);

        for (int y = minY; y <= maxY; y++)
        {
            Array.Clear(_coverage, 0, _width);
            bool any = false;
            for (int sample = 0; sample < SubSamples; sample++)
            {
                any |= AccumulateSpan(y + (sample + 0.5) / SubSamples, rule, 1.0f / SubSamples);
            }

            if (!any) continue;
            int rowStart = y * _width;
            for (int x = 0; x < _width; x++)
            {
                float value = _coverage[x];
                if (value <= 0) continue;
                int coverage = (int)Math.Clamp(value * 255.0f, 0, 255);
                int index = rowStart + x;
                if (coverage > mask[index]) mask[index] = (byte)coverage;
            }
        }
    }

    private bool AccumulateSpan(double scanY, PdfFillRule rule, float weight)
    {
        _crossings.Clear();
        foreach (var edge in _edges)
        {
            if (scanY < edge.Y0 || scanY >= edge.Y1) continue;
            double t = (scanY - edge.Y0) / (edge.Y1 - edge.Y0);
            double x = edge.X0 + t * (edge.X1 - edge.X0);
            _crossings.Add(((float)x, edge.Winding));
        }

        if (_crossings.Count < 2) return false;
        _crossings.Sort((a, b) => a.X.CompareTo(b.X));

        if (rule == PdfFillRule.EvenOdd)
        {
            for (int i = 0; i + 1 < _crossings.Count; i += 2)
            {
                AddHorizontalSpan(_crossings[i].X, _crossings[i + 1].X, weight);
            }

            return true;
        }

        int winding = 0;
        for (int i = 0; i + 1 < _crossings.Count; i++)
        {
            winding += _crossings[i].Winding;
            if (winding != 0)
            {
                AddHorizontalSpan(_crossings[i].X, _crossings[i + 1].X, weight);
            }
        }

        return true;
    }


    private void AddHorizontalSpan(double x0, double x1, float weight)
    {
        if (x1 <= x0) return;
        x0 = Math.Max(x0, 0);
        x1 = Math.Min(x1, _width);
        if (x1 <= x0) return;

        int first = (int)Math.Floor(x0);
        int last = (int)Math.Floor(x1);
        if (last >= _width) last = _width - 1;

        if (first == last)
        {
            _coverage[first] += (float)(x1 - x0) * weight;
            return;
        }

        _coverage[first] += (float)(first + 1 - x0) * weight;
        for (int x = first + 1; x < last; x++) _coverage[x] += weight;
        _coverage[last] += (float)(x1 - last) * weight;
    }

    private void BuildEdges(PdfPath path, bool closedOnly)
    {
        _edges.Clear();
        foreach (var contour in path.Contours)
        {
            int count = contour.Count;
            if (count < 2) continue;
            for (int i = 0; i + 1 < count; i++)
            {
                AddEdge(contour.X(i), contour.Y(i), contour.X(i + 1), contour.Y(i + 1));
            }

            if (contour.Closed)
            {
                AddEdge(contour.X(count - 1), contour.Y(count - 1), contour.X(0), contour.Y(0));
            }
            else if (closedOnly)
            {
                AddEdge(contour.X(count - 1), contour.Y(count - 1), contour.X(0), contour.Y(0));
            }
        }
    }

    private void AddEdge(double x0, double y0, double x1, double y1)
    {
        if (Math.Abs(y0 - y1) < 1e-9) return;
        _edges.Add(new Edge(x0, y0, x1, y1));
    }

    // ----------------------------------------------------------------- Garis

    public void Stroke(
        PdfPath path, in PdfStrokeSpec spec, double red, double green, double blue, double alpha,
        PdfBlendMode blend, byte[]? clipMask)
    {
        if (path.IsEmpty || alpha <= 0) return;
        double half = Math.Max(spec.Width, 0.05) / 2.0;
        var outline = new PdfPathBuilder { Flatness = 0.25 };

        foreach (var contour in path.Contours)
        {
            var points = contour.Points;
            if (points.Length < 4) continue;
            if (spec.DashArray is { Length: > 0 })
            {
                StrokeDashedContour(outline, points, contour.Closed, spec, half);
            }
            else
            {
                StrokePolyline(outline, points, contour.Closed, spec, half);
            }
        }

        var strokePath = outline.Build();
        if (strokePath.IsEmpty) return;
        Fill(strokePath, PdfFillRule.NonZero, red, green, blue, alpha, blend, clipMask);
    }

    private static void StrokePolyline(
        PdfPathBuilder outline, double[] points, bool closed, in PdfStrokeSpec spec, double half)
    {
        int count = points.Length / 2;
        if (count < 2) return;

        double[] xs = new double[count];
        double[] ys = new double[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = points[i * 2];
            ys[i] = points[i * 2 + 1];
        }

        if (closed)
        {
            // Ulangi titik awal agar sambungan di titik itu ikut digambar.
            var extendedX = new double[count + 1];
            var extendedY = new double[count + 1];
            Array.Copy(xs, extendedX, count);
            Array.Copy(ys, extendedY, count);
            extendedX[count] = xs[0];
            extendedY[count] = ys[0];
            xs = extendedX;
            ys = extendedY;
            count++;
        }

        for (int i = 0; i + 1 < count; i++)
        {
            double dx = xs[i + 1] - xs[i];
            double dy = ys[i + 1] - ys[i];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9) continue;
            double nx = -dy / length * half;
            double ny = dx / length * half;
            AddPolygon(outline,
                xs[i] + nx, ys[i] + ny,
                xs[i + 1] + nx, ys[i + 1] + ny,
                xs[i + 1] - nx, ys[i + 1] - ny,
                xs[i] - nx, ys[i] - ny);

            if (i + 2 < count)
            {
                AddJoin(outline, xs[i + 1], ys[i + 1], dx, dy, xs[i + 2] - xs[i + 1], ys[i + 2] - ys[i + 1], half, spec);
            }
        }

        if (!closed)
        {
            AddCap(outline, xs[0], ys[0], xs[1] - xs[0], ys[1] - ys[0], half, spec.Cap);
            AddCap(outline, xs[count - 1], ys[count - 1], xs[count - 2] - xs[count - 1], ys[count - 2] - ys[count - 1], half, spec.Cap);
        }
    }

    private static void AddJoin(
        PdfPathBuilder outline, double x, double y, double dx0, double dy0, double dx1, double dy1,
        double half, in PdfStrokeSpec spec)
    {
        double length0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
        double length1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        if (length0 < 1e-9 || length1 < 1e-9) return;
        double ux0 = dx0 / length0;
        double uy0 = dy0 / length0;
        double ux1 = dx1 / length1;
        double uy1 = dy1 / length1;

        if (spec.Join == PdfLineJoin.Round)
        {
            AddCircle(outline, x, y, half, 12);
            return;
        }

        if (spec.Join == PdfLineJoin.Bevel)
        {
            AddPolygon(outline, x, y, x - uy0 * half, y + ux0 * half, x - uy1 * half, y + ux1 * half);
            AddPolygon(outline, x, y, x + uy0 * half, y - ux0 * half, x + uy1 * half, y - ux1 * half);
            return;
        }

        // Sambungan miter: ujung pertemuan dua garis tepi luar.
        double cross = ux0 * uy1 - uy0 * ux1;
        double dot = ux0 * ux1 + uy0 * uy1;
        if (Math.Abs(cross) < 1e-6 || dot > 0.9999)
        {
            // Hampir lurus: tidak perlu sambungan tambahan.
            return;
        }

        double miterLength = 1.0 / Math.Max(1e-6, Math.Abs(cross));
        if (miterLength > spec.MiterLimit)
        {
            AddPolygon(outline, x, y, x - uy0 * half, y + ux0 * half, x - uy1 * half, y + ux1 * half);
            AddPolygon(outline, x, y, x + uy0 * half, y - ux0 * half, x + uy1 * half, y - ux1 * half);
            return;
        }

        double sign = cross > 0 ? 1 : -1;
        double nx0 = -uy0 * sign;
        double ny0 = ux0 * sign;
        double nx1 = -uy1 * sign;
        double ny1 = ux1 * sign;

        // Titik temu dua garis tepi yang digeser.
        double miterX = x + (nx0 + nx1) * half / (1 + dot);
        double miterY = y + (ny0 + ny1) * half / (1 + dot);
        AddPolygon(outline,
            x + nx0 * half, y + ny0 * half,
            miterX, miterY,
            x + nx1 * half, y + ny1 * half);
        AddPolygon(outline,
            x - nx0 * half, y - ny0 * half,
            x - nx1 * half, y - ny1 * half,
            x - (nx0 + nx1) * half / (1 + dot), y - (ny0 + ny1) * half / (1 + dot));
    }

    private static void AddCap(
        PdfPathBuilder outline, double x, double y, double dx, double dy, double half, PdfLineCap cap)
    {
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return;
        double ux = dx / length;
        double uy = dy / length;

        switch (cap)
        {
            case PdfLineCap.Round:
                AddCircle(outline, x, y, half, 12);
                break;
            case PdfLineCap.Square:
                double nx = -uy * half;
                double ny = ux * half;
                AddPolygon(outline,
                    x + nx, y + ny,
                    x + nx + ux * half, y + ny + uy * half,
                    x - nx + ux * half, y - ny + uy * half,
                    x - nx, y - ny);
                break;
        }
    }

    private static void AddCircle(PdfPathBuilder outline, double x, double y, double radius, int segments)
    {
        double step = Math.PI * 2 / segments;
        outline.MoveTo(x + radius, y);
        for (int i = 1; i <= segments; i++)
        {
            double angle = i * step;
            outline.LineTo(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius);
        }

        outline.Close();
    }

    private static void AddPolygon(PdfPathBuilder outline, params double[] coordinates)
    {
        if (coordinates.Length < 6) return;
        outline.MoveTo(coordinates[0], coordinates[1]);
        for (int i = 2; i + 1 < coordinates.Length; i += 2)
        {
            outline.LineTo(coordinates[i], coordinates[i + 1]);
        }

        outline.Close();
    }

    private static void StrokeDashedContour(
        PdfPathBuilder outline, double[] points, bool closed, in PdfStrokeSpec spec, double half)
    {
        double[] dashes = spec.DashArray ?? Array.Empty<double>();
        if (dashes.Length == 0) return;
        double patternLength = 0;
        foreach (double dash in dashes) patternLength += dash;
        if (patternLength <= 1e-6) return;

        int count = points.Length / 2;
        double phase = spec.DashPhase % patternLength;
        int dashIndex = 0;
        bool draw = true;
        double remaining = dashes[0] - phase;
        while (remaining <= 0)
        {
            dashIndex = (dashIndex + 1) % dashes.Length;
            draw = !draw;
            remaining += dashes[dashIndex];
        }

        var segment = new List<double>(64);
        int total = closed ? count + 1 : count;
        for (int i = 0; i + 1 < total; i++)
        {
            int next = (i + 1) % count;
            double x0 = points[i * 2];
            double y0 = points[i * 2 + 1];
            double x1 = points[next * 2];
            double y1 = points[next * 2 + 1];
            double dx = x1 - x0;
            double dy = y1 - y0;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-12) continue;
            double ux = dx / length;
            double uy = dy / length;
            double position = 0;

            while (position < length)
            {
                double step = Math.Min(remaining, length - position);
                double ax = x0 + ux * position;
                double ay = y0 + uy * position;
                double bx = x0 + ux * (position + step);
                double by = y0 + uy * (position + step);
                if (draw)
                {
                    segment.Add(ax);
                    segment.Add(ay);
                    segment.Add(bx);
                    segment.Add(by);
                }

                position += step;
                remaining -= step;
                if (remaining <= 1e-9)
                {
                    FlushDashSegment(outline, segment, spec, half);
                    dashIndex = (dashIndex + 1) % dashes.Length;
                    draw = !draw;
                    remaining = dashes[dashIndex];
                }
            }
        }

        FlushDashSegment(outline, segment, spec, half);
    }

    private static void FlushDashSegment(
        PdfPathBuilder outline, List<double> segment, in PdfStrokeSpec spec, double half)
    {
        if (segment.Count >= 4)
        {
            StrokePolyline(outline, segment.ToArray(), false, spec, half);
        }

        segment.Clear();
    }

    // ---------------------------------------------------------------- Gambar

    /// <summary>
    /// Berapa kali lipat biaya membaca satu sampel mentah dibandingkan membaca
    /// satu piksel RGBA yang sudah dikonversi. Hanya dipakai untuk memilih antara
    /// konversi utuh dan pengambilan sampel langsung; kedua jalur menghasilkan
    /// warna yang sama, jadi angkanya cukup longgar.
    /// </summary>
    private const int BiayaSampelMentah = 8;

    public void DrawImage(
        PdfImage image, in PdfMatrix matrix, double alpha, byte[]? clipMask, PdfBlendMode blend)
    {
        if (!matrix.TryInvert(out var inverse)) return;

        // Kotak batas gambar pada perangkat.
        var corners = new[]
        {
            matrix.Transform(0, 0),
            matrix.Transform(image.Width, 0),
            matrix.Transform(0, image.Height),
            matrix.Transform(image.Width, image.Height)
        };

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxX = Math.Max(maxX, corner.X);
            maxY = Math.Max(maxY, corner.Y);
        }

        int x0 = Math.Max(0, (int)Math.Floor(minX));
        int x1 = Math.Min(_width - 1, (int)Math.Ceiling(maxX));
        int y0 = Math.Max(0, (int)Math.Floor(minY));
        int y1 = Math.Min(_height - 1, (int)Math.Ceiling(maxY));
        if (x0 > x1 || y0 > y1) return;

        // Turunan matriks menentukan berapa piksel gambar yang jatuh pada satu
        // piksel layar. Bila gambar diperkecil (kop surat 1500 px menjadi ±90 px),
        // satu piksel layar mewakili beberapa piksel gambar — jumlah cuplikan
        // mengikuti rasio itu supaya tepi logo tidak bergerigi.
        double scale = Math.Sqrt(Math.Abs(inverse.Determinant));
        int samples = scale > 1.5 ? Math.Clamp((int)Math.Ceiling(scale), 2, 4) : 1;
        var maskPaint = image.MaskPaint;

        // Mengonversi seluruh gambar menjadi RGBA baru sepadan bila bagian yang
        // tampil menutupi sebagian besar gambar. Kop surat 1507×2050 px yang
        // tercetak sebagai ±90 px cukup diambil sampelnya langsung per titik —
        // mengonversi seluruh pindaian hanya untuk 5.000 piksel terbuang.
        long jepitan = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * samples * samples;
        bool perluRgba = image.Encoding is PdfImageEncoding.Jpeg or PdfImageEncoding.Jpeg2000
                         || jepitan * BiayaSampelMentah >= (long)image.Width * image.Height;
        byte[]? rgba = null;
        if (perluRgba)
        {
            rgba = image.ToRgba();
            if (rgba.Length < image.Width * image.Height * 4) return;
        }

        var sampler = new PdfImageSampler(image, rgba);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                // Warna dijumlahkan dalam bentuk pramultiplied (warna × alfa).
                // Dijumlahkan langsung, piksel transparan akan menarik warna ke
                // hitam dan tepi lambang menjadi gelap.
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double px = x + (sx + 0.5) / samples;
                        double py = y + (sy + 0.5) / samples;
                        var source = inverse.Transform(px, py);
                        if (source.X < 0 || source.Y < 0 || source.X >= image.Width || source.Y >= image.Height) continue;

                        // Ruang gambar PDF berawal di kiri-bawah, sedangkan baris
                        // byte PNG/JPEG selalu berawal dari sisi atas. Balik sumbu
                        // Y saat mengambil sampel agar logo dan lambang tidak terbalik.
                        double sourceY = image.Height - source.Y;
                        sampler.Sample(source.X, sourceY, out double sr, out double sg, out double sb, out double sa);
                        r += sr * sa;
                        g += sg * sa;
                        b += sb * sa;
                        a += sa;
                    }
                }

                double divisor = samples * samples;
                a /= divisor;
                if (a <= 0.001) continue;
                // Kembalikan ke warna tak pramultiplied untuk pengomposisian.
                r /= a * divisor;
                g /= a * divisor;
                b /= a * divisor;

                if (image.IsImageMask)
                {
                    r = maskPaint.R;
                    g = maskPaint.G;
                    b = maskPaint.B;
                    a *= maskPaint.Alpha;
                }

                int index = y * _width + x;
                double clip = clipMask is null ? 1 : clipMask[index] / 255.0;
                if (clip <= 0) continue;
                Composite(index, r, g, b, a * alpha * clip, blend);
            }
        }
    }

    // -------------------------------------------------------------- Gradien

    public void DrawShading(
        PdfShading shading, in PdfMatrix matrix, double alpha, byte[]? clipMask,
        PdfRect clipBounds, PdfBlendMode blend)
    {
        if (!matrix.TryInvert(out var inverse)) return;
        int x0 = Math.Max(0, (int)Math.Floor(clipBounds.Left));
        int x1 = Math.Min(_width - 1, (int)Math.Ceiling(clipBounds.Right));
        int y0 = Math.Max(0, (int)Math.Floor(clipBounds.Bottom));
        int y1 = Math.Min(_height - 1, (int)Math.Ceiling(clipBounds.Top));
        if (x0 > x1 || y0 > y1) return;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int index = y * _width + x;
                double clip = clipMask is null ? 1 : clipMask[index] / 255.0;
                if (clip <= 0) continue;

                var point = inverse.Transform(x + 0.5, y + 0.5);
                if (!shading.TryGetParameter(point.X, point.Y, out double parameter)) continue;
                shading.GetColor(parameter, out double red, out double green, out double blue);
                Composite(index, red, green, blue, alpha * clip, blend);
            }
        }
    }

    // ------------------------------------------------------------ Komposisi

    private void EnsureCoverage(int width)
    {
        if (_coverage.Length < width) _coverage = new float[width];
    }

    private void CompositeRow(
        int y, int minX, int maxX, double red, double green, double blue, double alpha,
        PdfBlendMode blend, byte[]? clipMask, double coverageGamma = 1.0, byte[]? coverageTable = null)
    {
        int rowStart = y * _width;
        for (int x = minX; x <= maxX; x++)
        {
            float coverage = _coverage[x];
            if (coverage <= 0.001f) continue;
            int index = rowStart + x;
            double clip = clipMask is null ? 1 : clipMask[index] / 255.0;
            if (clip <= 0) continue;
            double cakupan = coverageTable is null
                ? TerapkanGamma(coverage, coverageGamma)
                : TerapkanTabel(coverage, coverageTable);
            Composite(index, red, green, blue, alpha * cakupan * clip, blend);
        }
    }

    /// <summary>
    /// Koreksi cakupan huruf memakai TABEL persis seperti pdfium
    /// (<see cref="PdfTeksGamma"/>), dipakai untuk jalur huruf.
    /// </summary>
    private static double TerapkanTabel(float coverage, byte[] tabel)
    {
        int i = (int)(coverage * 255f + 0.5f);
        if (i < 0) i = 0;
        else if (i >= tabel.Length) i = tabel.Length - 1;
        return tabel[i] / 255.0;
    }

    /// <summary>
    /// Koreksi cakupan huruf. Huruf 12pt pada 96 dpi memiliki batang lebih tipis
    /// dari satu piksel, sehingga cakupan linier mencetaknya sebagai abu-abu muda
    /// dan teks surat terbaca pudar. Pembaca PDF sungguhan memakai koreksi yang
    /// sama (gamma pada cakupan) supaya huruf tetap hitam pada ukuran kecil.
    /// Cakupan penuh (1) tidak berubah, jadi bidang dan garis tebal tidak terpengaruh.
    /// </summary>
    private static double TerapkanGamma(float coverage, double gamma)
    {
        double nilai = Math.Min(1.0, coverage);
        if (gamma == 1.0 || nilai >= 1.0) return nilai;
        return Math.Pow(nilai, gamma);
    }

    private void Composite(int index, double red, double green, double blue, double alpha, PdfBlendMode blend)
    {
        if (alpha <= 0) return;
        if (alpha > 1) alpha = 1;

        int offset = index * 4;
        byte[] pixels = _image.Pixels;
        double destinationAlpha = pixels[offset + 3] / 255.0;
        double destinationRed = pixels[offset + 2] / 255.0;
        double destinationGreen = pixels[offset + 1] / 255.0;
        double destinationBlue = pixels[offset] / 255.0;

        double sourceRed = red;
        double sourceGreen = green;
        double sourceBlue = blue;
        switch (blend)
        {
            case PdfBlendMode.Multiply:
                sourceRed *= destinationRed;
                sourceGreen *= destinationGreen;
                sourceBlue *= destinationBlue;
                break;
            case PdfBlendMode.Screen:
                sourceRed = 1 - (1 - sourceRed) * (1 - destinationRed);
                sourceGreen = 1 - (1 - sourceGreen) * (1 - destinationGreen);
                sourceBlue = 1 - (1 - sourceBlue) * (1 - destinationBlue);
                break;
            case PdfBlendMode.Darken:
                sourceRed = Math.Min(sourceRed, destinationRed);
                sourceGreen = Math.Min(sourceGreen, destinationGreen);
                sourceBlue = Math.Min(sourceBlue, destinationBlue);
                break;
            case PdfBlendMode.Lighten:
                sourceRed = Math.Max(sourceRed, destinationRed);
                sourceGreen = Math.Max(sourceGreen, destinationGreen);
                sourceBlue = Math.Max(sourceBlue, destinationBlue);
                break;
            case PdfBlendMode.Difference:
                sourceRed = Math.Abs(sourceRed - destinationRed);
                sourceGreen = Math.Abs(sourceGreen - destinationGreen);
                sourceBlue = Math.Abs(sourceBlue - destinationBlue);
                break;
            case PdfBlendMode.Exclusion:
                sourceRed = sourceRed + destinationRed - 2 * sourceRed * destinationRed;
                sourceGreen = sourceGreen + destinationGreen - 2 * sourceGreen * destinationGreen;
                sourceBlue = sourceBlue + destinationBlue - 2 * sourceBlue * destinationBlue;
                break;
        }

        double outputAlpha = alpha + destinationAlpha * (1 - alpha);
        if (outputAlpha <= 0)
        {
            pixels[offset + 3] = 0;
            return;
        }

        double outputRed = (sourceRed * alpha + destinationRed * destinationAlpha * (1 - alpha)) / outputAlpha;
        double outputGreen = (sourceGreen * alpha + destinationGreen * destinationAlpha * (1 - alpha)) / outputAlpha;
        double outputBlue = (sourceBlue * alpha + destinationBlue * destinationAlpha * (1 - alpha)) / outputAlpha;

        pixels[offset] = ToByte(outputBlue);
        pixels[offset + 1] = ToByte(outputGreen);
        pixels[offset + 2] = ToByte(outputRed);
        pixels[offset + 3] = ToByte(outputAlpha);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
