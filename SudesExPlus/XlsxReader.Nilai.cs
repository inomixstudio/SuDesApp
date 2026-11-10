using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SudesExPlus.Style;

namespace SudesExPlus;

internal static partial class XlsxReader
    {

    private static object? BacaNilai(
        XElement sel,
        string? tipe,
        IReadOnlyList<string> sharedStrings,
        string? format)
    {
        // inlineStr menyimpan teks langsung pada sel.
        if (string.Equals(tipe, "inlineStr", StringComparison.Ordinal))
        {
            var teks = sel.Descendants(XName.Get("t", NsUtama)).Select(t => t.Value);
            return string.Concat(teks);
        }

        string? mentah = sel.Element(XName.Get("v", NsUtama))?.Value;

        if (string.Equals(tipe, "s", StringComparison.Ordinal))
        {
            // Shared string: v berisi indeks ke tabel bersama.
            if (int.TryParse(mentah, NumberStyles.Integer, CultureInfo.InvariantCulture, out int indeks)
                && indeks >= 0 && indeks < sharedStrings.Count)
            {
                return sharedStrings[indeks];
            }

            return null;
        }

        if (string.Equals(tipe, "b", StringComparison.Ordinal))
        {
            return mentah == "1";
        }

        if (string.Equals(tipe, "str", StringComparison.Ordinal))
        {
            return mentah ?? string.Empty;
        }

        if (string.Equals(tipe, "e", StringComparison.Ordinal))
        {
            // Nilai galat (#N/A dsb.) disimpan sebagai teks apa adanya.
            return mentah;
        }

        if (string.IsNullOrWhiteSpace(mentah)) return null;

        // Sel bertipe tanggal ISO (OOXML t="d"); XML Schema menuntut titik dua.
        if (string.Equals(tipe, "d", StringComparison.Ordinal))
        {
            if (DateTime.TryParse(mentah, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime waktuIso))
            {
                return waktuIso;
            }

            return mentah;
        }

        if (!double.TryParse(mentah, NumberStyles.Float, CultureInfo.InvariantCulture, out double angka))
        {
            return mentah;
        }

        if (ApakahTanggal(format, angka))
        {
            try
            {
                return DateTime.FromOADate(angka);
            }
            catch (ArgumentException)
            {
                // Di luar rentang tanggal OADate: biarkan sebagai angka.
            }
        }

        return angka;
    }

    /// <summary>True bila angka pada sel adalah tanggal menurut format selnya.</summary>
    private static bool ApakahTanggal(string? format, double angka)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            string f = format.ToLowerInvariant();
            foreach (char buang in new[] { '"', '\\' })
            {
                f = f.Replace(buang.ToString(), string.Empty);
            }

            if (f.Contains('y') || f.Contains('d')) return true;
        }

        // Tanpa format: seri hari yang wajar (1913–2064) tetap dibaca sebagai
        // tanggal, sama seperti aturan pembaca yang sudah dipakai aplikasi.
        return angka is >= 5000 and <= 60000 && string.IsNullOrEmpty(format);
    }

    private static List<string> BacaSharedStrings(ZipArchive arsip)
    {
        var hasil = new List<string>();
        var entri = CariEntri(arsip, "xl/sharedStrings.xml");
        if (entri == null) return hasil;

        XDocument dokumen = BacaXDocument(entri);
        foreach (var si in dokumen.Descendants(XName.Get("si", NsUtama)))
        {
            // Rich text bisa memecah satu teks jadi beberapa <t>.
            hasil.Add(string.Concat(si.Descendants(XName.Get("t", NsUtama)).Select(t => t.Value)));
        }

        return hasil;
    }


    }
