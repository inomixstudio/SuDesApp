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

/// <summary>
/// Pembaca .xlsx untuk model <see cref="ExcelPackage"/>. Menerima berkas yang
/// ditulis SudesExPlus maupun berkas buatan Excel, LibreOffice, atau WPS:
/// teks bisa tersimpan sebagai shared strings atau inline, tanggal bisa berupa
/// nomor seri dengan format sel, dan sel boleh tidak mencantumkan rujukannya.
///
/// Selain nilai, pembaca ini mempertahankan yang dibutuhkan untuk membuka lalu
/// menyimpan ulang berkas pihak lain tanpa kehilangan tampilan: font (nama,
/// ukuran, tebal, miring, garis bawah, warna), isian latar, garis tepi,
/// perataan, format angka, lebar kolom, tinggi baris, sel gabungan, panel beku,
/// dan rumus sel (termasuk rumus bersama/shared yang dipakai Excel saat satu
/// rumus disalin ke banyak baris).
/// </summary>
internal static partial class XlsxReader
{
    private const string NsUtama = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRelasiDokumen = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPaket = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Batas jumlah sel bergaya tanpa nilai yang dibaca per lembar. Berkas besar
    /// kadang memformat ribuan baris kosong; melewati batas ini menjaga memori
    /// tetap wajar tanpa memengaruhi tabel yang benar-benar berisi.
    /// </summary>
    private const int MaksSelGaya = 200_000;
    private const long MaksUkuranEntri = 50 * 1024 * 1024; // 50 MB per entri zip

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
        var gaya = GayaBerkas.Baca(arsip);

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
            BacaSheet(arsip, target, worksheet, sharedStrings, gaya);
            urutan++;
        }
    }

    private static void BacaSheet(
        ZipArchive arsip,
        string target,
        ExcelWorksheet lembar,
        IReadOnlyList<string> sharedStrings,
        GayaBerkas gaya)
    {
        string jalur = NormalisasiJalur(target);
        var entri = CariEntri(arsip, jalur);
        if (entri == null) return;

        XDocument dokumen = BacaXDocument(entri);

        BacaTampilan(dokumen, lembar);
        BacaKolom(dokumen, lembar);

        var rumusBersama = new Dictionary<int, (string Teks, int Baris, int Kolom)>();
        int selBergaya = 0;

        foreach (var baris in dokumen.Descendants(XName.Get("row", NsUtama)))
        {
            int nomorBaris = (int?)baris.Attribute("r") ?? 0;
            int kolomBerjalan = 0;

            BacaTinggiBaris(baris, nomorBaris, lembar);

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
                var formatSel = gaya.Urai(indeksGaya);

                object? nilai = BacaNilai(sel, tipe, sharedStrings, formatSel.Format);
                string? rumus = BacaRumus(sel, barisSel, kolomSel, rumusBersama);

                // Sel tanpa nilai dan tanpa rumus hanya perlu dibuat bila punya
                // gaya sendiri — supaya latar/garis tabel tidak hilang saat
                // berkas disimpan ulang.
                bool adaGaya = indeksGaya != 0 || formatSel.AdaGaya;
                if (nilai == null && rumus == null && !adaGaya) continue;

                if (nilai == null && rumus == null)
                {
                    if (selBergaya >= MaksSelGaya) continue;
                    selBergaya++;
                }

                var sasaran = lembar.SelAtauBuat(barisSel, kolomSel);
                if (nilai != null) sasaran.Value = nilai;
                if (rumus != null) sasaran.Formula = rumus;
                formatSel.Terapkan(sasaran.Gaya);
            }
        }

        foreach (var gabungan in dokumen.Descendants(XName.Get("mergeCell", NsUtama)))
        {
            string? rujukan = (string?)gabungan.Attribute("ref");
            if (!string.IsNullOrWhiteSpace(rujukan)) lembar.TambahGabungan(rujukan);
        }
    }

    /// <summary>Panel beku dan (kalau ada) pengaturan tampilan lain.</summary>
    private static void BacaTampilan(XDocument dokumen, ExcelWorksheet lembar)
    {
        var pane = dokumen.Descendants(XName.Get("pane", NsUtama)).FirstOrDefault();
        if (pane == null) return;

        string? keadaan = (string?)pane.Attribute("state");
        if (!string.Equals(keadaan, "frozen", StringComparison.Ordinal) &&
            !string.Equals(keadaan, "frozenSplit", StringComparison.Ordinal))
        {
            return;
        }

        int xSplit = (int?)pane.Attribute("xSplit") ?? 0;
        int ySplit = (int?)pane.Attribute("ySplit") ?? 0;

        // Rujukan sel bisa dipakai saat split 0 (mis. topLeftCell="A2").
        if (xSplit == 0 && ySplit == 0 &&
            ExcelRujukan.Urai((string?)pane.Attribute("topLeftCell"), out int baris, out int kolom))
        {
            ySplit = baris - 1;
            xSplit = kolom - 1;
        }

        if (xSplit <= 0 && ySplit <= 0) return;
        lembar.Bekukan = (Math.Max(0, ySplit) + 1, Math.Max(0, xSplit) + 1);
    }

    /// <summary>Lebar kolom yang dipasang penulis berkas (termasuk rentang min–max).</summary>
    private static void BacaKolom(XDocument dokumen, ExcelWorksheet lembar)
    {
        var cols = dokumen.Descendants(XName.Get("col", NsUtama));
        foreach (var col in cols)
        {
            int min = (int?)col.Attribute("min") ?? 0;
            int maks = (int?)col.Attribute("max") ?? min;
            double? lebar = (double?)col.Attribute("width");
            if (min < 1 || maks < min || lebar is not > 0) continue;

            // Rentang lebar bisa mencakup seluruh 16.384 kolom; cukup sampai
            // batas yang wajar untuk data desa.
            int akhir = Math.Min(maks, min + 511);
            for (int kolom = min; kolom <= akhir; kolom++)
            {
                lembar.LebarKolom[kolom] = lebar.Value;
            }
        }
    }

    private static void BacaTinggiBaris(XElement baris, int nomorBaris, ExcelWorksheet lembar)
    {
        if (nomorBaris < 1) return;

        double? tinggi = (double?)baris.Attribute("ht");
        bool khusus = string.Equals((string?)baris.Attribute("customHeight"), "1", StringComparison.Ordinal);

        if (tinggi is > 0 && (khusus || Math.Abs(tinggi.Value - 15) > 0.01))
        {
            lembar.TinggiBaris[nomorBaris] = tinggi.Value;
        }
    }

    /// <summary>
    /// Rumus sel: rumus biasa dipakai apa adanya; rumus bersama (shared) yang
    /// hanya menyimpan indeks memakai teks induknya lalu rujukan relatifnya
    /// digeser sesuai posisi sel — begitulah Excel menyimpan satu rumus yang
    /// disalin ke seluruh kolom.
    /// </summary>
    private static string? BacaRumus(
        XElement sel,
        int baris,
        int kolom,
        Dictionary<int, (string Teks, int Baris, int Kolom)> bersama)
    {
        var f = sel.Element(XName.Get("f", NsUtama));
        if (f == null) return null;

        string? tipe = (string?)f.Attribute("t");
        string teks = f.Value;
        bool adalahBersama = string.Equals(tipe, "shared", StringComparison.Ordinal);
        int indeksBersama = (int?)f.Attribute("si") ?? -1;

        if (adalahBersama && string.IsNullOrEmpty(teks))
        {
            if (indeksBersama >= 0 && bersama.TryGetValue(indeksBersama, out var induk))
            {
                string digeser = GeserRumus(induk.Teks, baris - induk.Baris, kolom - induk.Kolom);
                return string.IsNullOrWhiteSpace(digeser) ? null : digeser;
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(teks)) return null;

        if (adalahBersama && indeksBersama >= 0)
        {
            bersama[indeksBersama] = (teks, baris, kolom);
        }

        return teks;
    }

    /// <summary>
    /// Geser semua rujukan sel relatif di dalam rumus (A1 → A2 bila turun satu
    /// baris). Rujukan mutlak ($A$1) dan teks di dalam tanda kutip tidak diubah.

}
