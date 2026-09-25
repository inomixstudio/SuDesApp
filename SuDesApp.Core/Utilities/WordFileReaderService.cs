using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Membaca teks paragraf dari file Word (.docx) dan (.doc).
    /// .docx dibaca tanpa library eksternal (System.IO.Compression + XmlReader).
    /// .doc (format biner lama) dikonversi otomatis menjadi .docx memakai Microsoft Word
    /// (IWordDocConverter) lalu dibaca dengan cara yang sama, sehingga susunan berkas dan
    /// teks header/footer (tempat nomor & tanggal resmi) ikut terbaca. Bila Microsoft Word
    /// tidak terpasang, pengguna menerima petunjuk yang jelas, bukan kesalahan misterius.
    /// </summary>
    public interface IWordFileReader
    {
        Task<WordFileContent> ReadAsync(string filePath, CancellationToken ct = default);
    }

    public class WordFileContent
    {
        public string FullText { get; set; } = string.Empty;
        public List<string> Paragraphs { get; set; } = new();
    }

    public class WordFileReaderService : IWordFileReader
    {
        private readonly ILogger<WordFileReaderService> _logger;
        private readonly IWordDocConverter? _konverterDoc;

        /// <param name="konverterDoc">
        /// Pengubah berkas .doc lama menjadi .docx. Bila tidak tersedia (Word tidak
        /// terpasang), berkas .doc ditolak dengan petunjuk konversi manual.
        /// </param>
        public WordFileReaderService(
            ILogger<WordFileReaderService> logger, IWordDocConverter? konverterDoc = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _konverterDoc = konverterDoc;
        }

        public async Task<WordFileContent> ReadAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Path file tidak valid.", nameof(filePath));

            var ext = Path.GetExtension(filePath)?.ToUpperInvariant();
            _logger.LogInformation("Membaca file Word: {Path} ({Ext})", filePath, ext);

            var content = new WordFileContent { Paragraphs = new List<string>() };

            if (ext == ".DOCX")
            {
                await ReadDocxAsync(filePath, content, ct);
            }
            else if (ext == ".DOC")
            {
                await ReadDocLamaAsync(filePath, content, ct).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException("Hanya file .docx atau .doc yang didukung.");
            }

            content.FullText = string.Join("\n", content.Paragraphs).Trim();
            _logger.LogInformation("Berhasil membaca {Count} paragraf ({Chars} karakter) dari {Path}",
                content.Paragraphs.Count, content.FullText.Length, filePath);
            return content;
        }

        private static async Task ReadDocxAsync(string filePath, WordFileContent content, CancellationToken ct)
        {
            using var zip = ZipFile.OpenRead(filePath);
            var mainEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
            var paragraphs = new List<string>();

            if (mainEntry != null)
                paragraphs.AddRange(ExtractParagraphsFromDocumentXml(await ReadEntryAsync(mainEntry, ct)));

            // Header/footer sering memuat nomor & tanggal resmi.
            foreach (var entry in zip.Entries.Where(e =>
                e.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)))
            {
                var xml = await ReadEntryAsync(entry, ct);
                paragraphs.InsertRange(0, ExtractParagraphsFromDocumentXml(xml));
            }

            content.Paragraphs.AddRange(paragraphs);
        }

        private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }

        private static List<string> ExtractParagraphsFromDocumentXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return new();
            var paragraphs = new List<string>();
            try
            {
                using var xr = XmlReader.Create(new StringReader(xml));
                while (xr.Read())
                {
                    if (xr.NodeType != XmlNodeType.Element || xr.LocalName != "p") continue;

                    var sb = new StringBuilder();
                    using (var subtree = xr.ReadSubtree())
                    {
                        while (subtree.Read())
                        {
                            if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName == "t")
                            {
                                var text = subtree.ReadElementContentAsString();
                                if (!string.IsNullOrEmpty(text)) sb.Append(text);
                            }
                            else if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName == "tab")
                            {
                                sb.Append('\t');
                            }
                        }
                    }
                    var p = sb.ToString().Trim();
                    if (p.Length > 0) paragraphs.Add(p);
                }
            }
            catch
            {
                // Jika satu paragraf rusak, parser tetap melanjutkan.
            }
            return paragraphs;
        }

        /// <summary>
        /// Baca berkas .doc (Word 97-2003) lewat konversi otomatis ke .docx, memakai
        /// pengubah yang sama dengan fitur "Buat dari File Word". Berkas sementara
        /// selalu dihapus setelah dibaca.
        /// </summary>
        private async Task ReadDocLamaAsync(string filePath, WordFileContent content, CancellationToken ct)
        {
            if (_konverterDoc == null || !_konverterDoc.Tersedia)
            {
                throw new NotSupportedException(WordDocConverter.PesanTanpaWord);
            }

            _logger.LogInformation("Mengonversi berkas .doc untuk dibaca: {Path}", filePath);

            string? sementara = null;
            try
            {
                sementara = await _konverterDoc.KeDocxAsync(filePath, ct).ConfigureAwait(false);
                await ReadDocxAsync(sementara, content, ct).ConfigureAwait(false);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sementara))
                {
                    try { File.Delete(sementara); }
                    catch { /* berkas sementara gagal dihapus; dibersihkan saat aplikasi dijalankan lagi */ }
                }
            }
        }
    }

    internal static class WordParsingRegex
    {
        // Nilai nomor sengaja tidak boleh memuat baris baru: tanpa itu pola ini menelan
        // baris di bawahnya ("TENTANG", "MENETAPKAN :", …) sehingga kolom Nomor formulir
        // terisi beberapa baris sekaligus.
        public static readonly Regex NomorRegex = new(
            @"\b(?:NOMOR|No\.?|NOMOR PERATURAN|NO\.)\s*[:\-]?\s*[.:]?\s*(\d+[ \t]+TAHUN[ \t]+\d{4}|[A-Za-z0-9][A-Za-z0-9/.\- \t]{3,})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] Bulan =
        {
            "Januari","Februari","Maret","April","Mei","Juni",
            "Juli","Agustus","September","Oktober","November","Desember",
            "Jan","Feb","Mar","Apr","May","Jun",
            "Jul","Aug","Sep","Oct","Nov","Dec",
            "I","II","III","IV","V","VI","VII","VIII","IX","X","XI","XII"
        };

        // day month-name year | day sep month-text sep year | dd/MM/yyyy
        public static readonly Regex TanggalRegex = new(
            $@"(?<!\d)(?:tanggal\s*)?(?<d1>\d{{1,2}})\s+(?<m1>{string.Join("|", Bulan)})\s+(?:tahun\s*)?(?<th1>\d{{4}})\b" +
            @"|(?<!\d)(?<d2>\d{{1,2}})\s*[-/](?<m2>[A-Za-z]{{3,9}})\s*[-/]\s*(?<th2>\d{{4}})\b" +
            @"|(?<!\d)(?<dd>\d{{1,2}})[-/](?<mm>\d{{1,2}})[-/](?<yyyy>\d{{4}})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, int> MonthMap = BuildMonthMap();

        private static Dictionary<string, int> BuildMonthMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] names =
            {
                "Januari", "Februari", "Maret", "April", "Mei", "Juni",
                "Juli", "Agustus", "September", "Oktober", "November", "Desember"
            };
            for (int i = 0; i < names.Length; i++)
            {
                map[names[i]] = i + 1;
                map[names[i].Substring(0, 3)] = i + 1; // Jan, Feb, ...
            }
            // Romawi I..XII
            string[] rom = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
            for (int i = 0; i < rom.Length; i++) map[rom[i]] = i + 1;
            return map;
        }

        public static int? ParseMonth(string token) =>
            token != null && MonthMap.TryGetValue(token, out var m) ? m : null;
    }

    /// <summary>Hasil ekstraksi otomatis berdasarkan teks Word.</summary>
    public class KeputusanWordFields
    {
        public string? Nomor { get; set; }
        public string? TanggalDdMmYyyy { get; set; }
        public string? Tentang { get; set; }
        public string? Keterangan { get; set; }
    }

    public static class WordTextParser
    {
        private static readonly string[] TentangMarkers =
        {
            "TENTANG", "T E N T A N G", "PERIHAL", "HAL", "URAIAN INDIKATIF", "INFORMASI",
            "SUMMARY", "RINGKASAN"
        };

        private static readonly string[] KeteranganMarkers =
        {
            "MENETAPKAN", "MENETEUARKAN", "MEMUAT", "DASAR",
            "DASAR HUKUM", "MENGIKUTI", "MENURUT", "SESUDAH", "SETELAH"
        };

        // CATATAN: pendeteksian jenis dokumen tidak lagi memakai skor kata kunci
        // (lihat DetectDocumentType) karena "KEPUTUSAN KEPALA DESA" ikut
        // menghitung skor SK dan "KEPALA DESA" menaikkan skor Perdes sekaligus
        // Perkades sekaligus — jenis jadi saling tertukar.

        public static KeputusanWordFields ExtractKeputusanFields(string fullText, IList<string> paragraphs)
        {
            var result = new KeputusanWordFields();
            var lines = paragraphs ?? new List<string>();

            var mNomor = WordParsingRegex.NomorRegex.Match(fullText ?? "");
            if (mNomor.Success)
            {
                result.Nomor = mNomor.Groups[1].Value.Trim();
                // Extract year from "NOMOR X TAHUN YYYY" or "xxx/xxxx/YYYY" format for date fallback
                var tahunMatch = Regex.Match(result.Nomor, @"TAHUN\s+(\d{4})", RegexOptions.IgnoreCase);
                if (!tahunMatch.Success)
                {
                    // Try to find year at end of nomor (e.g., "340/01-Kep/Ds/2024" or "445.8 / 01-Kep. / Ds / 2026")
                    tahunMatch = Regex.Match(result.Nomor, @"/\s*(\d{4})$", RegexOptions.IgnoreCase);
                }
                if (tahunMatch.Success && int.TryParse(tahunMatch.Groups[1].Value, out int tahun))
                {
                    result.TanggalDdMmYyyy = $"01-01-{tahun:D4}";
                }
            }

            var mTgl = WordParsingRegex.TanggalRegex.Match(fullText ?? "");
            if (mTgl.Success)
                result.TanggalDdMmYyyy = NormalizeDate(mTgl);

            result.Tentang = FindLineAfterMarker(lines, TentangMarkers);

            result.Keterangan = FindLineAfterMarker(lines, KeteranganMarkers);
            // Jangan fallback ke baris pertama yang panjang - biarkan kosong jika marker tidak ditemukan

            return result;
        }

        /// <summary>
        /// Mendeteksi jenis dokumen: SK / PERDES / PERKADES / Unknown.
        ///
        /// Strategi:
        /// 1. Judul/kop dulu — sebutan TERATAS menentukan jenis (judul resmi selalu
        ///    di bagian atas dokumen; sebutan jenis lain di bawahnya biasanya hanya
        ///    dasar hukum/konsideran).
        /// 2. Fallback: skor seluruh isi dokumen, dengan "KEPUTUSAN" yang bagian
        ///    dari "KEPUTUSAN KEPALA DESA" dihitung sebagai Perkades, bukan SK.
        /// </summary>
        public static string DetectDocumentType(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return "Unknown";

            var text = fullText.ToUpperInvariant();
            var lines = text.Split('\n')
                .Select(l => Regex.Replace(l, "\\s+", " ").Trim())
                .Where(l => l.Length > 0)
                .ToList();

            // 1) Deteksi berdasarkan kop/judul: baris pertama yang memuat frasa
            //    penentu dianggap judul dokumen. Urutan cek per baris dari yang
            //    paling spesifik (Kepala Desa) ke umum (Keputusan).
            foreach (var line in lines.Take(20))
            {
                if (ContainsAny(line, "PERATURAN KEPALA DESA", "KEPUTUSAN KEPALA DESA", "PERKADES"))
                    return "PERKADES";
                if (ContainsAny(line, "PERATURAN DESA", "PERDES"))
                    return "PERDES";
                if (ContainsAny(line, "SURAT KEPUTUSAN", "KEPUTUSAN"))
                    return "SK";
            }

            // 2) Fallback: skor isi dokumen. Kata "KEPUTUSAN" yang berdiri sendiri
            //    menandai SK; "KEPUTUSAN KEPALA DESA" dihitung untuk Perkades.
            int sk = Regex.Matches(text, "KEPUTUSAN(?!\\s*KEPALA\\s*DESA)").Count;
            int perdes = Regex.Matches(text, "PERATURAN DESA").Count + Regex.Matches(text, "PERDES").Count;
            int perkades = Regex.Matches(text, "PERATURAN KEPALA DESA").Count
                         + Regex.Matches(text, "PERKADES").Count
                         + Regex.Matches(text, "KEPUTUSAN KEPALA DESA").Count;

            if (perdes > perkades && perdes > sk) return "PERDES";
            if (perkades > perdes && perkades > sk) return "PERKADES";
            if (sk > 0) return "SK";

            return "Unknown";
        }

        /// <summary>
        /// Cek frasa pada satu baris, toleran terhadap spasi berlebih/antar-huruf
        /// (mis. judul "P E R A T U R A N  D E S A" tetap dikenali).
        /// </summary>
        private static bool ContainsAny(string line, params string[] phrases)
        {
            var compact = line.Replace(" ", "");
            foreach (var phrase in phrases)
            {
                if (line.Contains(phrase, StringComparison.Ordinal) ||
                    compact.Contains(phrase.Replace(" ", ""), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string? FindLineAfterMarker(IList<string> lines, string[] markers)
        {
            if (lines == null || lines.Count == 0) return null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                foreach (var marker in markers)
                {
                    if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        var after = CleanSeparator(line.Substring(marker.Length).Trim());
                        if (!string.IsNullOrEmpty(after)) return after;
                        if (i + 1 < lines.Count)
                            return CleanSeparator(lines[i + 1].Trim());
                    }
                }
            }
            return null;
        }

        private static string CleanSeparator(string value)
        {
            return value.TrimStart(' ', ':', '-', '.', '\t');
        }

        private static string? NormalizeDate(Match m)
        {
            try
            {
                if (m.Groups["dd"].Success && m.Groups["mm"].Success && m.Groups["yyyy"].Success)
                {
                    int dd = int.Parse(m.Groups["dd"].Value);
                    int mm = int.Parse(m.Groups["mm"].Value);
                    int yy = int.Parse(m.Groups["yyyy"].Value);
                    if (DateTime.TryParse($"{dd:00}-{mm:00}-{yy:0000}", out var dt))
                        return dt.ToString("dd-MM-yyyy");
                }
                if (m.Groups["d1"].Success && m.Groups["m1"].Success && m.Groups["th1"].Success)
                {
                    int dd = int.Parse(m.Groups["d1"].Value);
                    var mm = WordParsingRegex.ParseMonth(m.Groups["m1"].Value);
                    int yy = int.Parse(m.Groups["th1"].Value);
                    if (mm.HasValue && DateTime.TryParse($"{dd:00}-{mm:00}-{yy:0000}", out var dt))
                        return dt.ToString("dd-MM-yyyy");
                }
                if (m.Groups["d2"].Success && m.Groups["m2"].Success && m.Groups["th2"].Success)
                {
                    int dd = int.Parse(m.Groups["d2"].Value);
                    var mm = WordParsingRegex.ParseMonth(m.Groups["m2"].Value);
                    int yy = int.Parse(m.Groups["th2"].Value);
                    if (mm.HasValue && DateTime.TryParse($"{dd:00}-{mm:00}-{yy:0000}", out var dt))
                        return dt.ToString("dd-MM-yyyy");
                }
            }
            catch { /* ignore */ }
            return m.Value;
        }
    }
}