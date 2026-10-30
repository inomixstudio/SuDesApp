using System.Drawing;
using SudesExPlus.Style;

namespace SudesExPlus;

/// <summary>
/// Gaya sel dengan bentuk API yang sama seperti yang dipakai aplikasi:
/// <c>sel.Style.Font.Bold = true</c>, <c>sel.Style.Fill.BackgroundColor.SetColor(…)</c>,
/// <c>sel.Style.Border.BorderAround(ExcelBorderStyle.Thin)</c>.
///
/// Objek ini hanya jembatan: setiap penetapan langsung menulis ke gaya sel
/// sasaran (satu sel atau seluruh sel pada rentang). Karena itu tidak ada
/// keadaan gaya yang perlu disalin balik, dan menata bagian dalam rentang tidak
/// menghapus gaya yang sudah ada.
/// </summary>
public sealed class ExcelStyle
{
    private readonly ITargetGaya _target;

    internal ExcelStyle(ITargetGaya target)
    {
        _target = target;
        Font = new ExcelFont(target);
        Fill = new ExcelFill(target);
        Border = new ExcelBorder(target);
        Numberformat = new ExcelNumberFormat(target);
    }

    public ExcelFont Font { get; }

    public ExcelFill Fill { get; }

    public ExcelBorder Border { get; }

    public ExcelNumberFormat Numberformat { get; }

    public ExcelHorizontalAlignment HorizontalAlignment
    {
        get => _target.Contoh.RataMendatar ?? ExcelHorizontalAlignment.General;
        set => _target.Ubah(g => g.RataMendatar = value);
    }

    public ExcelVerticalAlignment VerticalAlignment
    {
        get => _target.Contoh.RataTegak ?? ExcelVerticalAlignment.Bottom;
        set => _target.Ubah(g => g.RataTegak = value);
    }

    /// <summary>Bungkus teks panjang ke beberapa baris di dalam satu sel.</summary>
    public bool WrapText
    {
        get => _target.Contoh.BungkusTeks;
        set => _target.Ubah(g => g.BungkusTeks = value);
    }
}

/// <summary>Huruf sel: tebal, miring, ukuran, dan warna.</summary>
public sealed class ExcelFont
{
    private readonly ITargetGaya _target;

    internal ExcelFont(ITargetGaya target)
    {
        _target = target;
        Color = new ExcelColor(warna => _target.Ubah(g => g.WarnaHuruf = warna),
            () => _target.Contoh.WarnaHuruf);
    }

    public bool Bold
    {
        get => _target.Contoh.Bold;
        set => _target.Ubah(g => g.Bold = value);
    }

    public bool Italic
    {
        get => _target.Contoh.Italic;
        set => _target.Ubah(g => g.Italic = value);
    }

    public double Size
    {
        get => _target.Contoh.UkuranHuruf ?? 11;
        set => _target.Ubah(g => g.UkuranHuruf = value);
    }

    /// <summary>
    /// Nama huruf (mis. "Calibri", "Arial"). Nilai yang dibaca dari berkas
    /// pihak lain disimpan kembali apa adanya supaya tampilannya tidak berubah.
    /// </summary>
    public string Name
    {
        get => _target.Contoh.NamaHuruf ?? "Calibri";
        set => _target.Ubah(g => g.NamaHuruf = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Garis bawah huruf.</summary>
    public bool Underline
    {
        get => _target.Contoh.GarisBawahHuruf;
        set => _target.Ubah(g => g.GarisBawahHuruf = value);
    }

    /// <summary>Warna huruf: <c>sel.Style.Font.Color.SetColor(warna)</c>.</summary>
    public ExcelColor Color { get; }
}

/// <summary>Warna ARGB yang bisa dipasang ke huruf atau latar sel.</summary>
public sealed class ExcelColor
{
    private readonly Action<Color> _pasang;
    private readonly Func<Color?> _baca;

    internal ExcelColor(Action<Color> pasang, Func<Color?>? baca = null)
    {
        _pasang = pasang;
        _baca = baca ?? (() => null);
    }

    /// <summary>Warna yang sedang terpasang, atau null bila belum ada.</summary>
    public Color? Value => _baca();

    /// <summary>Pasang warna; bentuk pemanggilan sama dengan pustaka sebelumnya.</summary>
    public void SetColor(Color warna) => _pasang(warna);
}

/// <summary>Isian latar sel.</summary>
public sealed class ExcelFill
{
    private readonly ITargetGaya _target;

    internal ExcelFill(ITargetGaya target)
    {
        _target = target;
        BackgroundColor = new ExcelColor(warna => _target.Ubah(g => g.WarnaIsian = warna),
            () => _target.Contoh.WarnaIsian);
    }

    /// <summary>
    /// Hanya <see cref="ExcelFillStyle.Solid"/> dan
    /// <see cref="ExcelFillStyle.None"/> yang berpengaruh; pola lain diperlakukan
    /// seperti isian penuh karena aplikasi tidak pernah memakainya.
    /// </summary>
    public ExcelFillStyle PatternType
    {
        get => _target.Contoh.WarnaIsian.HasValue ? ExcelFillStyle.Solid : ExcelFillStyle.None;
        set
        {
            if (value == ExcelFillStyle.None)
            {
                _target.Ubah(g => g.WarnaIsian = null);
            }
        }
    }

    public ExcelColor BackgroundColor { get; }
}

/// <summary>Garis tepi sel.</summary>
public sealed class ExcelBorder
{
    private readonly ITargetGaya _target;

    internal ExcelBorder(ITargetGaya target) => _target = target;

    public ExcelBorderStyle Left
    {
        get => _target.Contoh.GarisKiri;
        set => _target.Ubah(g => g.GarisKiri = value);
    }

    public ExcelBorderStyle Right
    {
        get => _target.Contoh.GarisKanan;
        set => _target.Ubah(g => g.GarisKanan = value);
    }

    public ExcelBorderStyle Top
    {
        get => _target.Contoh.GarisAtas;
        set => _target.Ubah(g => g.GarisAtas = value);
    }

    public ExcelBorderStyle Bottom
    {
        get => _target.Contoh.GarisBawah;
        set => _target.Ubah(g => g.GarisBawah = value);
    }

    /// <summary>
    /// Pasang garis di keliling wilayah sasaran saja (bukan garis antar sel).
    /// Bentuk pemanggilan sama dengan pustaka sebelumnya.
    /// </summary>
    public void BorderAround(ExcelBorderStyle gaya) => _target.GarisKeliling(gaya);
}

/// <summary>Format angka sel (mis. "dd/mm/yyyy").</summary>
public sealed class ExcelNumberFormat
{
    private readonly ITargetGaya _target;

    internal ExcelNumberFormat(ITargetGaya target) => _target = target;

    public string Format
    {
        get => _target.Contoh.FormatAngka ?? string.Empty;
        set => _target.Ubah(g => g.FormatAngka = string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
