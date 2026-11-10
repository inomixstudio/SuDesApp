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

    private static Dictionary<string, string> BacaRelasi(ZipArchive arsip, string jalur)
    {
        var hasil = new Dictionary<string, string>(StringComparer.Ordinal);
        var entri = CariEntri(arsip, jalur);
        if (entri == null) return hasil;

        XDocument dokumen = BacaXDocument(entri);
        foreach (var rel in dokumen.Descendants(XName.Get("Relationship", NsPaket)))
        {
            string? id = (string?)rel.Attribute("Id");
            string? target = (string?)rel.Attribute("Target");
            if (id != null && target != null) hasil[id] = target;
        }

        return hasil;
    }

    /// <summary>Jadikan target relasi menjadi jalur di dalam paket.</summary>
    private static string NormalisasiJalur(string target)
    {
        string bersih = target.Replace('\\', '/');
        if (bersih.StartsWith("/", StringComparison.Ordinal)) return bersih.TrimStart('/');
        return bersih.StartsWith("xl/", StringComparison.Ordinal) ? bersih : "xl/" + bersih;
    }

    private static ZipArchiveEntry? CariEntri(ZipArchive arsip, string jalur) =>
        arsip.GetEntry(jalur)
        ?? arsip.Entries.FirstOrDefault(e => string.Equals(e.FullName, jalur, StringComparison.OrdinalIgnoreCase));

    private static XDocument BacaXDocument(ZipArchiveEntry entri)
    {
        if (entri.Length > MaksUkuranEntri)
            throw new InvalidOperationException($"Entri {entri.FullName} melebihi batas ukuran ({MaksUkuranEntri} byte).");
        using var aliran = entri.Open();
        return XDocument.Load(aliran, LoadOptions.None);
    }

    }
