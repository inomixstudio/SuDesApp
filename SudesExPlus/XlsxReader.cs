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
internal static class XlsxReader
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
    /// </summary>
    internal static string GeserRumus(string rumus, int deltaBaris, int deltaKolom)
    {
        if (string.IsNullOrEmpty(rumus) || (deltaBaris == 0 && deltaKolom == 0)) return rumus;

        var hasil = new StringBuilder(rumus.Length + 8);
        int i = 0;
        bool dalamTeks = false;

        while (i < rumus.Length)
        {
            char huruf = rumus[i];

            if (huruf == '"')
            {
                dalamTeks = !dalamTeks;
                hasil.Append(huruf);
                i++;
                continue;
            }

            if (dalamTeks)
            {
                hasil.Append(huruf);
                i++;
                continue;
            }

            int panjang = CocokRujukan(rumus, i, out int kolom, out int baris,
                out bool mutlakKolom, out bool mutlakBaris);

            if (panjang > 0)
            {
                int kolomBaru = mutlakKolom ? kolom : kolom + deltaKolom;
                int barisBaru = mutlakBaris ? baris : baris + deltaBaris;

                if (kolomBaru >= 1 && barisBaru >= 1)
                {
                    if (mutlakKolom) hasil.Append('$');
                    hasil.Append(ExcelRujukan.Kolom(kolomBaru));
                    if (mutlakBaris) hasil.Append('$');
                    hasil.Append(barisBaru.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    // Rujukan yang bergeser keluar lembar: biarkan apa adanya.
                    hasil.Append(rumus, i, panjang);
                }

                i += panjang;
                continue;
            }

            hasil.Append(huruf);
            i++;
        }

        return hasil.ToString();
    }

    /// <summary>
    /// Kenali rujukan A1/$A$1 pada posisi <paramref name="mulai"/>; kembalikan
    /// panjangnya. Nama fungsi (LOG10, ATAN2) dan teks biasa tidak dianggap
    /// rujukan karena harus didahului pemisah dan diakhiri digit.
    /// </summary>
    private static int CocokRujukan(
        string teks,
        int mulai,
        out int kolom,
        out int baris,
        out bool mutlakKolom,
        out bool mutlakBaris)
    {
        kolom = 0;
        baris = 0;
        mutlakKolom = false;
        mutlakBaris = false;

        if (mulai > 0)
        {
            char sebelum = teks[mulai - 1];
            if (char.IsLetterOrDigit(sebelum) || sebelum is '_' or '.' or '$') return 0;
        }

        int i = mulai;
        if (i < teks.Length && teks[i] == '$')
        {
            mutlakKolom = true;
            i++;
        }

        int kolomHasil = 0;
        int panjangKolom = 0;
        while (i < teks.Length && char.IsLetter(teks[i]) && panjangKolom < 3)
        {
            kolomHasil = kolomHasil * 26 + (char.ToUpperInvariant(teks[i]) - 'A' + 1);
            i++;
            panjangKolom++;
        }

        if (panjangKolom == 0) return 0;

        // Huruf keempat menandakan nama fungsi/nama lain, bukan rujukan.
        if (i < teks.Length && char.IsLetter(teks[i])) return 0;

        if (i < teks.Length && teks[i] == '$')
        {
            mutlakBaris = true;
            i++;
        }

        int awalAngka = i;
        int barisHasil = 0;
        while (i < teks.Length && char.IsDigit(teks[i]))
        {
            barisHasil = barisHasil * 10 + (teks[i] - '0');
            i++;
        }

        if (i == awalAngka || barisHasil < 1) return 0;

        // Sesudah angka tidak boleh huruf/garis bawah (bagian dari nama).
        if (i < teks.Length && (char.IsLetterOrDigit(teks[i]) || teks[i] == '_')) return 0;

        kolom = kolomHasil;
        baris = barisHasil;
        return i - mulai;
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

    // =====================================================================
    // styles.xml → gaya per sel
    // =====================================================================

    /// <summary>
    /// Gaya per indeks sel (xf) hasil pembacaan styles.xml: format angka, font,
    /// isian, garis, dan perataan. Warna tema (theme) dan palet indeks lama
    /// diterjemahkan menjadi RGB supaya bisa ditulis kembali sebagai RGB.
    /// </summary>
    private sealed class GayaBerkas
    {
        private readonly List<GayaSelBerkas> _xf;

        private GayaBerkas(List<GayaSelBerkas> xf) => _xf = xf;

        public static GayaBerkas Kosong { get; } = new(new List<GayaSelBerkas> { new() });

        public GayaSelBerkas Urai(int indeks) =>
            indeks >= 0 && indeks < _xf.Count ? _xf[indeks] : _xf[0];

        public static GayaBerkas Baca(ZipArchive arsip)
        {
            var entri = CariEntri(arsip, "xl/styles.xml");
            if (entri == null) return Kosong;

            XDocument dokumen = BacaXDocument(entri);
            var warna = SkemaWarna.Baca(arsip);

            var khusus = new Dictionary<int, string>();
            foreach (var numFmt in dokumen.Descendants(XName.Get("numFmt", NsUtama)))
            {
                int? id = (int?)numFmt.Attribute("numFmtId");
                string? kode = (string?)numFmt.Attribute("formatCode");
                if (id.HasValue && kode != null) khusus[id.Value] = kode;
            }

            var font = BacaFont(dokumen, warna);
            var isian = BacaIsian(dokumen, warna);
            var garis = BacaGaris(dokumen);

            var hasil = new List<GayaSelBerkas>();
            var cellXfs = dokumen.Descendants(XName.Get("cellXfs", NsUtama)).FirstOrDefault();

            if (cellXfs != null)
            {
                foreach (var xf in cellXfs.Elements(XName.Get("xf", NsUtama)))
                {
                    int numFmtId = (int?)xf.Attribute("numFmtId") ?? 0;
                    int fontId = (int?)xf.Attribute("fontId") ?? 0;
                    int fillId = (int?)xf.Attribute("fillId") ?? 0;
                    int borderId = (int?)xf.Attribute("borderId") ?? 0;

                    var alignment = xf.Element(XName.Get("alignment", NsUtama));

                    hasil.Add(new GayaSelBerkas
                    {
                        Format = khusus.TryGetValue(numFmtId, out string? kode)
                            ? kode
                            : FormatBawaan(numFmtId),
                        Font = font.ElementAtOrDefault(fontId),
                        Isian = isian.ElementAtOrDefault(fillId),
                        Garis = garis.ElementAtOrDefault(borderId),
                        RataMendatar = BacaRataMendatar((string?)alignment?.Attribute("horizontal")),
                        RataTegak = BacaRataTegak((string?)alignment?.Attribute("vertical")),
                        BungkusTeks = string.Equals((string?)alignment?.Attribute("wrapText"), "1", StringComparison.Ordinal),
                    });
                }
            }

            if (hasil.Count == 0) hasil.Add(new GayaSelBerkas());
            return new GayaBerkas(hasil);
        }

        private static List<FontBerkas?> BacaFont(XDocument dokumen, SkemaWarna warna)
        {
            var hasil = new List<FontBerkas?>();
            var fonts = dokumen.Descendants(XName.Get("fonts", NsUtama)).FirstOrDefault();
            if (fonts == null) return hasil;

            foreach (var font in fonts.Elements(XName.Get("font", NsUtama)))
            {
                var underline = font.Element(XName.Get("u", NsUtama));
                string? jenisGarisBawah = (string?)underline?.Attribute("val");

                hasil.Add(new FontBerkas
                {
                    Bold = font.Element(XName.Get("b", NsUtama)) is { } b && !string.Equals((string?)b.Attribute("val"), "0", StringComparison.Ordinal),
                    Italic = font.Element(XName.Get("i", NsUtama)) is { } i && !string.Equals((string?)i.Attribute("val"), "0", StringComparison.Ordinal),
                    GarisBawah = underline != null && !string.Equals(jenisGarisBawah, "none", StringComparison.Ordinal),
                    Nama = (string?)font.Element(XName.Get("name", NsUtama))?.Attribute("val"),
                    Ukuran = (double?)font.Element(XName.Get("sz", NsUtama))?.Attribute("val"),
                    Warna = warna.Baca(font.Element(XName.Get("color", NsUtama))),
                });
            }

            return hasil;
        }

        private static List<Color?> BacaIsian(XDocument dokumen, SkemaWarna warna)
        {
            var hasil = new List<Color?>();
            var fills = dokumen.Descendants(XName.Get("fills", NsUtama)).FirstOrDefault();
            if (fills == null) return hasil;

            foreach (var fill in fills.Elements(XName.Get("fill", NsUtama)))
            {
                var pola = fill.Element(XName.Get("patternFill", NsUtama));
                string? tipe = (string?)pola?.Attribute("patternType");

                // Hanya isian penuh yang bisa diwakili model gaya ini; pola
                // bergaris/ber titik diperlakukan sebagai tanpa isian.
                if (!string.Equals(tipe, "solid", StringComparison.Ordinal))
                {
                    hasil.Add(null);
                    continue;
                }

                hasil.Add(warna.Baca(pola!.Element(XName.Get("fgColor", NsUtama))));
            }

            return hasil;
        }

        private static List<GarisBerkas?> BacaGaris(XDocument dokumen)
        {
            var hasil = new List<GarisBerkas?>();
            var borders = dokumen.Descendants(XName.Get("borders", NsUtama)).FirstOrDefault();
            if (borders == null) return hasil;

            foreach (var border in borders.Elements(XName.Get("border", NsUtama)))
            {
                hasil.Add(new GarisBerkas
                {
                    Kiri = BacaGarisSisi(border.Element(XName.Get("left", NsUtama))),
                    Kanan = BacaGarisSisi(border.Element(XName.Get("right", NsUtama))),
                    Atas = BacaGarisSisi(border.Element(XName.Get("top", NsUtama))),
                    Bawah = BacaGarisSisi(border.Element(XName.Get("bottom", NsUtama))),
                });
            }

            return hasil;
        }

        private static ExcelBorderStyle BacaGarisSisi(XElement? sisi)
        {
            string? gaya = (string?)sisi?.Attribute("style");
            return gaya switch
            {
                "hair" => ExcelBorderStyle.Hair,
                "thin" => ExcelBorderStyle.Thin,
                "medium" or "mediumDashed" or "mediumDashDot" or "mediumDashDotDot" or "slantDashDot"
                    => ExcelBorderStyle.Medium,
                "thick" => ExcelBorderStyle.Thick,
                "double" => ExcelBorderStyle.Double,
                // Garis putus-putus tidak punya padanan; garis tipis menjaga
                // tabel tetap terbaca.
                "dashed" or "dotted" or "dashDot" or "dashDotDot" => ExcelBorderStyle.Thin,
                _ => ExcelBorderStyle.None,
            };
        }

        private static ExcelHorizontalAlignment? BacaRataMendatar(string? nilai) => nilai switch
        {
            "left" => ExcelHorizontalAlignment.Left,
            "center" or "centerContinuous" => ExcelHorizontalAlignment.Center,
            "right" => ExcelHorizontalAlignment.Right,
            _ => null,
        };

        private static ExcelVerticalAlignment? BacaRataTegak(string? nilai) => nilai switch
        {
            "top" => ExcelVerticalAlignment.Top,
            "center" => ExcelVerticalAlignment.Center,
            "bottom" => ExcelVerticalAlignment.Bottom,
            _ => null,
        };

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
    }

    /// <summary>Gaya satu xf hasil pembacaan.</summary>
    private sealed class GayaSelBerkas
    {
        public string? Format { get; init; }

        public FontBerkas? Font { get; init; }

        public Color? Isian { get; init; }

        public GarisBerkas? Garis { get; init; }

        public ExcelHorizontalAlignment? RataMendatar { get; init; }

        public ExcelVerticalAlignment? RataTegak { get; init; }

        public bool BungkusTeks { get; init; }

        /// <summary>True bila xf memuat gaya yang perlu ditulis balik.</summary>
        public bool AdaGaya =>
            !string.IsNullOrEmpty(Format) || Font != null || Isian.HasValue || Garis != null ||
            RataMendatar.HasValue || RataTegak.HasValue || BungkusTeks;

        public void Terapkan(GayaSel gaya)
        {
            if (!string.IsNullOrEmpty(Format)) gaya.FormatAngka = Format;

            if (Font is { } font)
            {
                gaya.Bold = font.Bold;
                gaya.Italic = font.Italic;
                gaya.GarisBawahHuruf = font.GarisBawah;
                if (!string.IsNullOrWhiteSpace(font.Nama)) gaya.NamaHuruf = font.Nama;
                if (font.Ukuran.HasValue) gaya.UkuranHuruf = font.Ukuran;
                if (font.Warna.HasValue) gaya.WarnaHuruf = font.Warna;
            }

            if (Isian.HasValue) gaya.WarnaIsian = Isian;
            if (Garis is { } garis)
            {
                gaya.GarisKiri = garis.Kiri;
                gaya.GarisKanan = garis.Kanan;
                gaya.GarisAtas = garis.Atas;
                gaya.GarisBawah = garis.Bawah;
            }

            if (RataMendatar.HasValue) gaya.RataMendatar = RataMendatar;
            if (RataTegak.HasValue) gaya.RataTegak = RataTegak;
            if (BungkusTeks) gaya.BungkusTeks = true;
        }
    }

    private sealed record FontBerkas
    {
        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool GarisBawah { get; init; }

        public string? Nama { get; init; }

        public double? Ukuran { get; init; }

        public Color? Warna { get; init; }
    }

    private sealed record GarisBerkas
    {
        public ExcelBorderStyle Kiri { get; init; }

        public ExcelBorderStyle Kanan { get; init; }

        public ExcelBorderStyle Atas { get; init; }

        public ExcelBorderStyle Bawah { get; init; }
    }

    /// <summary>
    /// Warna tema workbook (xl/theme/theme1.xml) dan palet indeks lama. Excel
    /// menulis warna tema+rona (tint) untuk hampir semua latar sel, jadi tanpa
    /// terjemahan ini isian akan hilang saat berkas disimpan ulang.
    /// </summary>
    private sealed class SkemaWarna
    {
        private readonly IReadOnlyDictionary<int, Color> _tema;
        private readonly Color[] _palet;

        public SkemaWarna(IReadOnlyDictionary<int, Color> tema)
        {
            _tema = tema;
            _palet = PaletIndeks;
        }

        public static SkemaWarna Baca(ZipArchive arsip)
        {
            var tema = new Dictionary<int, Color>();
            var entri = CariEntri(arsip, "xl/theme/theme1.xml");
            if (entri == null) return new SkemaWarna(tema);

            try
            {
                XDocument dokumen = BacaXDocument(entri);
                var skema = dokumen.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "clrScheme");

                if (skema != null)
                {
                    foreach (var slot in skema.Elements())
                    {
                        Color? warna = WarnaNode(slot);
                        if (!warna.HasValue) continue;

                        int indeks = slot.Name.LocalName switch
                        {
                            "dk1" => 1,
                            "lt1" => 0,
                            "dk2" => 3,
                            "lt2" => 2,
                            "hlink" => 10,
                            "folHlink" => 11,
                            _ => slot.Name.LocalName.StartsWith("accent", StringComparison.Ordinal) &&
                                 int.TryParse(slot.Name.LocalName["accent".Length..], out int n) &&
                                 n is >= 1 and <= 6
                                ? 3 + n
                                : -1,
                        };

                        if (indeks >= 0) tema[indeks] = warna.Value;
                    }
                }
            }
            catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException)
            {
                // Tema rusak tidak boleh menggagalkan pembacaan berkas; warna
                // tema saja yang hilang.
            }

            return new SkemaWarna(tema);
        }

        private static Color? WarnaNode(XElement slot)
        {
            var srgb = slot.Elements().FirstOrDefault(e => e.Name.LocalName == "srgbClr");
            if (srgb != null) return UraiRgb((string?)srgb.Attribute("val"));

            var sistem = slot.Elements().FirstOrDefault(e => e.Name.LocalName == "sysClr");
            return sistem == null ? null : UraiRgb((string?)sistem.Attribute("lastClr"));
        }

        /// <summary>Terjemahkan elemen warna sel (rgb/theme/indexed + tint).</summary>
        public Color? Baca(XElement? warna)
        {
            if (warna == null) return null;

            // Warna otomatis mengikuti tema; tidak perlu ditulis balik.
            if (string.Equals((string?)warna.Attribute("auto"), "1", StringComparison.Ordinal)) return null;

            double tint = (double?)warna.Attribute("tint") ?? 0;

            string? rgb = (string?)warna.Attribute("rgb");
            if (rgb != null && UraiRgb(rgb) is { } langsung)
            {
                return TerapkanTint(langsung, tint);
            }

            if (int.TryParse((string?)warna.Attribute("theme"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int indeksTema) &&
                _tema.TryGetValue(indeksTema, out Color dariTema))
            {
                return TerapkanTint(dariTema, tint);
            }

            if (int.TryParse((string?)warna.Attribute("indexed"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int indeks) &&
                indeks >= 0)
            {
                // 64 adalah "sistem" (biasanya hitam), sisanya palet lama.
                if (indeks == 64) return Color.Black;
                if (indeks < _palet.Length) return _palet[indeks];
            }

            return null;
        }

        private static Color? UraiRgb(string? argb)
        {
            if (string.IsNullOrWhiteSpace(argb)) return null;

            string bersih = argb.Length == 8 ? argb[2..] : argb;
            if (bersih.Length != 6 ||
                !int.TryParse(bersih, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int nilai))
            {
                return null;
            }

            return Color.FromArgb(255, (nilai >> 16) & 0xFF, (nilai >> 8) & 0xFF, nilai & 0xFF);
        }

        /// <summary>Rona (tint) OOXML: menggeser kecerahan dalam ruang HSL.</summary>
        internal static Color TerapkanTint(Color warna, double tint)
        {
            if (Math.Abs(tint) < 0.0001) return warna;

            (double h, double s, double l) = KeHsl(warna);

            l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;

            return DariHsl(h, s, Math.Clamp(l, 0, 1));
        }

        private static (double H, double S, double L) KeHsl(Color warna)
        {
            double r = warna.R / 255.0;
            double g = warna.G / 255.0;
            double b = warna.B / 255.0;

            double maks = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (maks + min) / 2;

            if (Math.Abs(maks - min) < 0.0001) return (0, 0, l);

            double d = maks - min;
            double s = l > 0.5 ? d / (2 - maks - min) : d / (maks + min);

            double h = maks switch
            {
                _ when Math.Abs(maks - r) < 0.0001 => ((g - b) / d + (g < b ? 6 : 0)) / 6,
                _ when Math.Abs(maks - g) < 0.0001 => ((b - r) / d + 2) / 6,
                _ => ((r - g) / d + 4) / 6,
            };

            return (h, s, l);
        }

        private static Color DariHsl(double h, double s, double l)
        {
            if (s <= 0)
            {
                int abu = Abu(l);
                return Color.FromArgb(255, abu, abu, abu);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            return Color.FromArgb(255,
                Abu(Komponen(p, q, h + 1.0 / 3)),
                Abu(Komponen(p, q, h)),
                Abu(Komponen(p, q, h - 1.0 / 3)));

            static int Abu(double nilai) => (int)Math.Round(Math.Clamp(nilai, 0, 1) * 255);

            static double Komponen(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
        }

        /// <summary>
        /// Palet indeks Excel (indexedColors bawaan). Dipakai berkas lama dan
        /// sebagian penulis pihak ketiga sebelum warna RGB/tema umum.
        /// </summary>
        private static readonly Color[] PaletIndeks =
        {
            Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255),
            Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0),
            Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 255),
            Color.FromArgb(128, 0, 0), Color.FromArgb(0, 128, 0), Color.FromArgb(0, 0, 128), Color.FromArgb(128, 128, 0),
            Color.FromArgb(128, 0, 128), Color.FromArgb(0, 128, 128), Color.FromArgb(192, 192, 192), Color.FromArgb(128, 128, 128),
            Color.FromArgb(153, 153, 255), Color.FromArgb(153, 51, 102), Color.FromArgb(255, 255, 204), Color.FromArgb(204, 255, 255),
            Color.FromArgb(102, 0, 102), Color.FromArgb(255, 128, 128), Color.FromArgb(0, 102, 204), Color.FromArgb(204, 204, 255),
            Color.FromArgb(0, 0, 128), Color.FromArgb(255, 0, 255), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 255),
            Color.FromArgb(128, 0, 128), Color.FromArgb(128, 0, 0), Color.FromArgb(0, 128, 128), Color.FromArgb(0, 0, 255),
            Color.FromArgb(0, 204, 255), Color.FromArgb(204, 255, 255), Color.FromArgb(204, 255, 204), Color.FromArgb(255, 255, 153),
            Color.FromArgb(153, 204, 255), Color.FromArgb(255, 153, 204), Color.FromArgb(204, 153, 255), Color.FromArgb(255, 204, 153),
            Color.FromArgb(51, 102, 255), Color.FromArgb(51, 204, 204), Color.FromArgb(153, 204, 0), Color.FromArgb(255, 204, 0),
            Color.FromArgb(255, 153, 0), Color.FromArgb(255, 102, 0), Color.FromArgb(102, 102, 153), Color.FromArgb(150, 150, 150),
            Color.FromArgb(0, 51, 102), Color.FromArgb(51, 153, 102), Color.FromArgb(0, 51, 0), Color.FromArgb(51, 51, 0),
            Color.FromArgb(153, 51, 0), Color.FromArgb(153, 51, 102), Color.FromArgb(51, 51, 153), Color.FromArgb(51, 51, 51),
        };
    }

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
