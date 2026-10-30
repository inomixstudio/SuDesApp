using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace SudesExPlus;

/// <summary>
/// Pembaca .xlsx untuk model <see cref="ExcelPackage"/>. Menerima berkas yang
/// ditulis SudesExPlus maupun berkas buatan Excel, LibreOffice, atau WPS:
/// teks bisa tersimpan sebagai shared strings atau inline, tanggal bisa berupa
/// nomor seri dengan format sel, dan sel boleh tidak mencantumkan rujukannya.
///
/// Yang diambil dari berkas adalah NILAI dan FORMAT ANGKA sel. Gaya visual
/// (font, isian, garis) tidak dipakai aplikasi saat membaca, jadi tidak
/// disimpan — halaman hanya memakai teks/angka sel.
/// </summary>
internal static class XlsxReader
{
    private const string NsUtama = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRelasiDokumen = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPaket = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Baca berkas; kegagalan I/O dilempar ke pemanggil.</summary>
    public static void Muat(string jalur, ExcelWorkbook workbook)
    {
        using var aliran = new FileStream(jalur, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var arsip = new ZipArchive(aliran, ZipArchiveMode.Read);

        var bagianWorkbook = CariEntri(arsip, "xl/workbook.xml");
        if (bagianWorkbook == null)
        {
            throw new InvalidDataException("Berkas bukan workbook Excel (.xlsx) yang dikenali.");
        }

        XDocument dokumenWorkbook = BacaXDocument(bagianWorkbook);
        var relasi = BacaRelasi(arsip, "xl/_rels/workbook.xml.rels");
        var sharedStrings = BacaSharedStrings(arsip);
        var formatSel = BacaFormatSel(arsip);

        var lembar = dokumenWorkbook.Descendants(XName.Get("sheet", NsUtama)).ToList();
        int urutan = 1;

        foreach (var node in lembar)
        {
            string nama = (string?)node.Attribute("name") ?? "Sheet" + urutan;
            string? idRelasi = (string?)node.Attribute(XName.Get("id", NsRelasiDokumen));

            string target = idRelasi != null && relasi.TryGetValue(idRelasi, out var t)
                ? t
                : "worksheets/sheet" + urutan + ".xml";

            var worksheet = workbook.Worksheets.TambahDariBerkas(nama);
            BacaSheet(arsip, target, worksheet, sharedStrings, formatSel);
            urutan++;
        }
    }

    private static void BacaSheet(
        ZipArchive arsip,
        string target,
        ExcelWorksheet lembar,
        IReadOnlyList<string> sharedStrings,
        IReadOnlyDictionary<int, string?> formatSel)
    {
        string jalur = NormalisasiJalur(target);
        var entri = CariEntri(arsip, jalur);
        if (entri == null) return;

        XDocument dokumen = BacaXDocument(entri);

        foreach (var baris in dokumen.Descendants(XName.Get("row", NsUtama)))
        {
            int nomorBaris = (int?)baris.Attribute("r") ?? 0;
            int kolomBerjalan = 0;

            foreach (var sel in baris.Elements(XName.Get("c", NsUtama)))
            {
                string? rujukan = (string?)sel.Attribute("r");
                int barisSel;
                int kolomSel;

                if (ExcelRujukan.Urai(rujukan, out int r, out int c))
                {
                    barisSel = r;
                    kolomSel = c;
                }
                else
                {
                    // Sebagian penulis berkas tidak mencantumkan rujukan sel;
                    // urutannya dianggap mengikuti kolom sebelumnya.
                    barisSel = nomorBaris > 0 ? nomorBaris : 1;
                    kolomSel = kolomBerjalan + 1;
                }

                kolomBerjalan = kolomSel;
                if (barisSel < 1 || kolomSel < 1) continue;

                string? tipe = (string?)sel.Attribute("t");
                int indeksGaya = (int?)sel.Attribute("s") ?? 0;
                string? format = formatSel.TryGetValue(indeksGaya, out var f) ? f : null;

                object? nilai = BacaNilai(sel, tipe, sharedStrings, format);
                if (nilai == null) continue;

                var sasaran = lembar.SelAtauBuat(barisSel, kolomSel);
                sasaran.Value = nilai;
                if (!string.IsNullOrEmpty(format))
                {
                    sasaran.Gaya.FormatAngka = format;
                }
            }
        }
    }

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

    /// <summary>
    /// Format angka per indeks gaya sel (xf). Hanya nomor format yang dibaca:
    /// cukup untuk mengenali tanggal dan menampilkan teks sel.
    /// </summary>
    private static Dictionary<int, string?> BacaFormatSel(ZipArchive arsip)
    {
        var hasil = new Dictionary<int, string?>();
        var entri = CariEntri(arsip, "xl/styles.xml");
        if (entri == null) return hasil;

        XDocument dokumen = BacaXDocument(entri);

        var khusus = new Dictionary<int, string>();
        foreach (var numFmt in dokumen.Descendants(XName.Get("numFmt", NsUtama)))
        {
            int? id = (int?)numFmt.Attribute("numFmtId");
            string? kode = (string?)numFmt.Attribute("formatCode");
            if (id.HasValue && kode != null) khusus[id.Value] = kode;
        }

        var cellXfs = dokumen.Descendants(XName.Get("cellXfs", NsUtama)).FirstOrDefault();
        if (cellXfs == null) return hasil;

        int indeks = 0;
        foreach (var xf in cellXfs.Elements(XName.Get("xf", NsUtama)))
        {
            int numFmtId = (int?)xf.Attribute("numFmtId") ?? 0;
            hasil[indeks] = khusus.TryGetValue(numFmtId, out string? kode)
                ? kode
                : FormatBawaan(numFmtId);
            indeks++;
        }

        return hasil;
    }

    /// <summary>Kode format bawaan Excel yang punya arti untuk pembacaan sel.</summary>
    private static string? FormatBawaan(int numFmtId) => numFmtId switch
    {
        1 => "0",
        2 => "0.00",
        3 => "#,##0",
        4 => "#,##0.00",
        9 => "0%",
        10 => "0.00%",
        14 => "dd/mm/yyyy",
        15 => "d-mmm-yy",
        16 => "d-mmm",
        17 => "mmm-yy",
        18 => "h:mm AM/PM",
        19 => "h:mm:ss AM/PM",
        20 => "h:mm",
        21 => "h:mm:ss",
        22 => "dd/mm/yyyy h:mm",
        45 => "mm:ss",
        46 => "[h]:mm:ss",
        47 => "mm:ss.0",
        _ => null,
    };

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
        using var aliran = entri.Open();
        return XDocument.Load(aliran, LoadOptions.None);
    }
}
