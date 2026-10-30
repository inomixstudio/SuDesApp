using System.Drawing;
using System.Globalization;

namespace SudesExPlus;

/// <summary>
/// Satu sel worksheet. Nilai disimpan apa adanya (string, angka, bool, atau
/// <see cref="DateTime"/>) dan gaya sel disimpan per sel, supaya penulis .xlsx
/// bisa menghasilkan xf yang tepat untuk setiap sel.
/// </summary>
public sealed class ExcelCell
{
    private object? _nilai;

    internal ExcelCell(ExcelWorksheet lembar, int baris, int kolom)
    {
        Lembar = lembar;
        Baris = baris;
        Kolom = kolom;
    }

    internal ExcelWorksheet Lembar { get; }

    internal int Baris { get; }

    internal int Kolom { get; }

    internal GayaSel Gaya { get; } = new();

    /// <summary>
    /// Isi sel. Menyimpan null mengosongkan sel tetapi mempertahankan gayanya —
    /// sama seperti pustaka sebelumnya.
    /// </summary>
    public object? Value
    {
        get => _nilai;
        set => _nilai = value;
    }

    /// <summary>True bila sel punya nilai atau gaya yang perlu ditulis.</summary>
    internal bool AdaIsi => _nilai != null || Gaya.AdaGaya;

    /// <summary>
    /// Teks tampilan sel: nilai yang sudah diformat menurut format angkanya.
    /// Tanggal ditampilkan dd/MM/yyyy (dan HH:mm bila formatnya memuat jam),
    /// angka bulat tanpa desimal.
    /// </summary>
    public string Text => TeksSel.Tampilkan(_nilai, Gaya.FormatAngka);

    /// <summary>Gaya sel; penetapan langsung berlaku untuk sel ini saja.</summary>
    public ExcelStyle Style => new(new TargetSel(this));
}

/// <summary>
/// Aturan teks tampilan sel untuk pembaca berkas. Dibuat terpisah supaya
/// pembaca dan penulis memakai satu tafsir yang sama terhadap format angka.
/// </summary>
internal static class TeksSel
{
    private static readonly CultureInfo Budaya = CultureInfo.InvariantCulture;

    public static string Tampilkan(object? nilai, string? format)
    {
        if (nilai == null) return string.Empty;

        switch (nilai)
        {
            case string teks:
                return teks;

            case bool logika:
                return logika ? "TRUE" : "FALSE";

            case DateTime waktu:
                return waktu.ToString(PolaTanggal(format), Budaya);

            case double angka:
                return FormatAngka(angka, format);

            case decimal desimal:
                return FormatAngka((double)desimal, format);

            case IFormattable dapatFormat:
                return dapatFormat.ToString(null, Budaya) ?? string.Empty;

            default:
                return nilai.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Pola .NET untuk sel tanggal. Format Excel yang rumit tidak diterjemahkan
    /// penuh — yang dibutuhkan hanya hasil yang bisa dibaca manusia dan dikenali
    /// pengurai tanggal aplikasi (dd-MM-yyyy, dd/MM/yyyy, atau ISO).
    /// </summary>
    public static string PolaTanggal(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return "dd/MM/yyyy";

        string f = format.ToLowerInvariant();
        bool adaJam = f.Contains('h') || f.Contains(":mm");

        // Format ISO ("yyyy-mm-dd") dipertahankan supaya jelas urutannya.
        if (f.Contains("yyyy-mm-dd") || f.Contains("yyyy/mm/dd"))
        {
            return adaJam ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd";
        }

        return adaJam ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy";
    }

    /// <summary>Angka: bulat tanpa desimal, pecahan apa adanya (maks. 15 digit).</summary>
    public static string FormatAngka(double angka, string? format)
    {
        if (double.IsNaN(angka) || double.IsInfinity(angka)) return string.Empty;

        if (string.IsNullOrWhiteSpace(format))
        {
            return angka == Math.Floor(angka) && Math.Abs(angka) < 1e15
                ? ((long)angka).ToString(Budaya)
                : angka.ToString("G15", Budaya);
        }

        string f = format.ToLowerInvariant();
        if (f.Contains('%'))
        {
            return (angka * 100).ToString(DesimalDari(f), Budaya) + " %";
        }

        return angka.ToString(DesimalDari(f), Budaya);
    }

    /// <summary>Tebak jumlah desimal dari format angka Excel ("0.00" → "0.00").</summary>
    private static string DesimalDari(string format)
    {
        int titik = format.IndexOf('.');
        if (titik < 0) return "0";

        int desimal = 0;
        for (int i = titik + 1; i < format.Length && (format[i] == '0' || format[i] == '#'); i++)
        {
            desimal++;
        }

        return desimal == 0 ? "0" : "0." + new string('0', desimal);
    }
}
