using System;
using System.Collections.Generic;
using System.IO;

namespace SudesPdf.Core;

/// <summary>
/// Bentuk huruf untuk font standar PDF (base-14) yang tidak menyematkan berkas
/// font — termasuk seluruh surat yang ditulis SudesPdf sendiri.
///
/// Berkas keluaran tidak memuat font pihak mana pun, jadi ukuran dan posisi
/// huruf diambil dari tabel /Widths pada dokumen (persis seperti yang tertulis),
/// sedangkan bentuk hurufnya diambil dari font yang memang sudah terpasang di
/// sistem — sama seperti setiap pembaca PDF lain melakukan penggantian. Dengan
/// begitu aplikasi tidak pernah mendistribusikan berkas font milik pihak lain.
/// </summary>
internal static class PdfStandardFontSource
{
    private static readonly Dictionary<string, IPdfGlyphSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _gate = new();

    /// <summary>Nama berkas font pengganti untuk setiap muka standar.</summary>
    private static string? SubstituteFile(string baseFontName)
    {
        // Nama pada berkas sering memuat awalan subset ("ABCDEF+Times-Roman").
        int plus = baseFontName.IndexOf('+');
        string nama = plus >= 0 ? baseFontName[(plus + 1)..] : baseFontName;

        return nama switch
        {
            "Times-Roman" or "TimesNewRomanPSMT" or "TimesNewRoman" => "times.ttf",
            "Times-Bold" or "TimesNewRomanPS-BoldMT" or "TimesNewRoman-Bold" => "timesbd.ttf",
            "Times-Italic" or "TimesNewRomanPS-ItalicMT" or "TimesNewRoman-Italic" => "timesi.ttf",
            "Times-BoldItalic" or "TimesNewRomanPS-BoldItalicMT" or "TimesNewRoman-BoldItalic" => "timesbi.ttf",
            "Helvetica" or "Arial" or "ArialMT" => "arial.ttf",
            "Helvetica-Bold" or "Arial-Bold" or "Arial-BoldMT" => "arialbd.ttf",
            "Helvetica-Oblique" or "Arial-Italic" or "Arial-ItalicMT" => "ariali.ttf",
            "Helvetica-BoldOblique" or "Arial-BoldItalic" or "Arial-BoldItalicMT" => "arialbi.ttf",
            "Courier" or "CourierNew" or "CourierNewPSMT" => "cour.ttf",
            "Courier-Bold" or "CourierNew-Bold" or "CourierNewPS-BoldMT" => "courbd.ttf",
            "Courier-Oblique" or "CourierNew-Italic" or "CourierNewPS-ItalicMT" => "couri.ttf",
            "Courier-BoldOblique" or "CourierNew-BoldItalic" or "CourierNewPS-BoldItalicMT" => "courbi.ttf",
            _ => null,
        };
    }

    /// <summary>
    /// Nama berkas font sistem untuk satu muka Times. Dipakai saat menulis PDF
    /// supaya berkas keluaran benar-benar memuat Times New Roman — bukan nama
    /// base-14 yang setiap pembaca PDF ganti dengan font serif lain.
    /// </summary>
    internal static string? FaceFile(PdfTimesFace face) => face switch
    {
        PdfTimesFace.Roman => "times.ttf",
        PdfTimesFace.Bold => "timesbd.ttf",
        PdfTimesFace.Italic => "timesi.ttf",
        PdfTimesFace.BoldItalic => "timesbi.ttf",
        _ => null,
    };

    /// <summary>Jalur berkas font di folder font sistem, atau null bila tidak ada.</summary>
    internal static string? FindFile(string fileName)
    {
        foreach (string folder in FontFolders())
        {
            string path = Path.Combine(folder, fileName);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>Program huruf pengganti, atau null bila tidak ditemukan di sistem.</summary>
    internal static IPdfGlyphSource? Resolve(string baseFontName)
    {
        if (string.IsNullOrWhiteSpace(baseFontName)) return null;

        lock (_gate)
        {
            if (_cache.TryGetValue(baseFontName, out var tersimpan)) return tersimpan;

            var sumber = Muat(baseFontName);
            _cache[baseFontName] = sumber;
            return sumber;
        }
    }

    private static IPdfGlyphSource? Muat(string baseFontName)
    {
        string? berkas = SubstituteFile(baseFontName);
        if (berkas is null) return null;

        foreach (string folder in FontFolders())
        {
            string path = Path.Combine(folder, berkas);
            if (!File.Exists(path)) continue;

            try
            {
                return TrueTypeFont.Parse(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                // Font sistem yang tidak terbaca diabaikan; teks tetap terbaca.
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> FontFolders()
    {
        string? fonts = null;
        try
        {
            fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        }
        catch (PlatformNotSupportedException)
        {
            // Bukan Windows: tidak ada folder font baku.
        }

        if (!string.IsNullOrWhiteSpace(fonts)) yield return fonts;

        string? windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "Fonts");
        }
    }
}
