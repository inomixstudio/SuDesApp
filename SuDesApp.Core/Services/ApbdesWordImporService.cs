using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu baris anggaran hasil pembacaan berkas APBDes: kode rekening, uraian,
    /// beserta angka yang tertulis di berkas.
    /// </summary>
    /// <param name="Kode">Kode rekening apa adanya, mis. "4.2.1.01" (boleh kosong).</param>
    /// <param name="Uraian">Nama rekening/kegiatan apa adanya dari berkas.</param>
    /// <param name="Anggaran">Anggaran pada kolom pertama angka; 0 bila tidak terbaca.</param>
    /// <param name="Realisasi">Realisasi pada kolom kedua angka; 0 bila tidak ada.</param>
    public sealed record BarisAnggaranApbdes(string Kode, string Uraian, long Anggaran, long Realisasi);

    /// <summary>Hasil pembacaan satu berkas APBDes.</summary>
    public sealed class HasilImporApbdes
    {
        public string NamaBerkas { get; init; } = string.Empty;

        /// <summary>Baris anggaran yang berhasil dikenali dari tabel/baris berkas.</summary>
        public IReadOnlyList<BarisAnggaranApbdes> Anggaran { get; init; } = Array.Empty<BarisAnggaranApbdes>();

        /// <summary>Seluruh teks berkas (untuk dipandang di halaman, bukan untuk dihitung).</summary>
        public string Teks { get; init; } = string.Empty;

        /// <summary>Nomor dokumen bila terbaca dari berkas (mis. "1 TAHUN 2026").</summary>
        public string Nomor { get; init; } = string.Empty;

        /// <summary>Tahun anggaran bila terbaca dari berkas.</summary>
        public int? Tahun { get; init; }

        /// <summary>Tahapan yang paling cocok menurut kata di berkas (Awal/Perubahan/…).</summary>
        public string? Jenis { get; init; }

        /// <summary>Catatan proses supaya pengguna tahu apa yang tidak terbaca.</summary>
        public IReadOnlyList<string> Peringatan { get; init; } = Array.Empty<string>();
    }

    /// <summary>Pembaca berkas Word dokumen APBDes.</summary>
    public interface IApbdesWordImpor
    {
        Task<HasilImporApbdes> BacaAsync(string berkas, CancellationToken ct = default);
    }

    /// <summary>
    /// Membaca berkas Word (.docx, dan .doc lama lewat konversi Microsoft Word) untuk
    /// arsip APBDes.
    ///
    /// Yang diambil ada dua: (1) teks utuh berkas supaya isinya bisa dipandang di
    /// halaman, dan (2) baris anggaran — kode rekening, uraian, anggaran, realisasi —
    /// yang dikenali dari tabel maupun baris teks. Angka keuangan desa tetap milik
    /// dokumen Siskeudes; pembacaan di sini hanya memindahkan apa yang tertulis, dan
    /// hasilnya selalu bisa disunting pengguna sebelum disimpan.
    ///
    /// Sengaja tanpa library tambahan: .docx adalah arsip ZIP berisi word/document.xml.
    /// </summary>
    public class ApbdesWordImporService : IApbdesWordImpor
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        /// <summary>Ukuran berkas yang masih boleh dibaca.</summary>
        public const long UkuranBerkasMaks = 25L * 1024 * 1024;

        /// <summary>Batas jumlah baris anggaran yang dibaca dari satu berkas.</summary>
        public const int MaksBarisAnggaran = 3000;

        /// <summary>Batas teks yang disimpan di arsip (berkas aslinya tetap utuh).</summary>
        public const int MaksTeks = 200_000;

        /// <summary>Kode rekening: "4", "4.1", "4.1.1.01" (angka dan titik).</summary>
        private static readonly Regex PolaKode = new(
            @"^\d(?:\.\d{1,4}){0,5}$", RegexOptions.Compiled);

        /// <summary>Satu baris teks: kode lalu uraian dipisah dua spasi/tab, lalu angka-angka.</summary>
        private static readonly Regex PolaBarisTeks = new(
            @"^(?<kode>\d(?:\.\d{1,4}){0,5})\s+(?<uraian>.+?)\s{2,}(?<angka>[-\d.,\s]+)$",
            RegexOptions.Compiled);

        /// <summary>Angka rupiah yang boleh dibaca dari satu sel.</summary>
        private static readonly Regex PolaAngka = new(@"^[-\d.,]+$", RegexOptions.Compiled);

        /// <summary>Baris yang bukan data meski diawali angka (judul kolom, jumlah).</summary>
        private static readonly string[] KataBukanData =
        {
            "jumlah", "total", "sub jumlah", "kode", "uraian", "anggaran",
            "realisasi", "lebih", "kurang", "persen", "keterangan", "bertambah", "berkurang"
        };

        private readonly ILogger<ApbdesWordImporService> _logger;
        private readonly IWordDocConverter? _konverterDoc;

        public ApbdesWordImporService(
            ILogger<ApbdesWordImporService> logger,
            IWordDocConverter? konverterDoc = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _konverterDoc = konverterDoc;
        }

        public async Task<HasilImporApbdes> BacaAsync(string berkas, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(berkas))
            {
                throw new ArgumentException("Berkas belum dipilih.", nameof(berkas));
            }

            if (!File.Exists(berkas))
            {
                throw new FileNotFoundException("Berkas tidak ditemukan.", berkas);
            }

            string ekstensi = Path.GetExtension(berkas).ToUpperInvariant();
            if (ekstensi != ".DOC" && ekstensi != ".DOCX")
            {
                throw new NotSupportedException(
                    "Hanya berkas Word (.docx, atau .doc lama) yang bisa dibaca di sini. " +
                    "Berkas PDF/Excel dari Siskeudes bisa disimpan apa adanya lewat tombol Lampirkan Berkas.");
            }

            var info = new FileInfo(berkas);
            if (info.Length > UkuranBerkasMaks)
            {
                throw new InvalidOperationException(
                    $"Berkas terlalu besar (lebih dari {UkuranBerkasMaks / (1024 * 1024)} MB).");
            }

            var peringatan = new List<string>();
            string berkasDibaca = berkas;

            // Berkas .doc lama: konversi otomatis lebih dulu (butuh Microsoft Word).
            string? sementara = null;
            if (ekstensi == ".DOC")
            {
                if (_konverterDoc == null || !_konverterDoc.Tersedia)
                {
                    throw new NotSupportedException(WordDocConverter.PesanTanpaWord);
                }

                sementara = await _konverterDoc.KeDocxAsync(berkas, ct);
                berkasDibaca = sementara;
                peringatan.Add("Berkas .doc lama dikonversi otomatis ke .docx lebih dulu, lalu dibaca.");
            }

            try
            {
                var baris = await BacaBarisAsync(berkasDibaca, ct);

                var anggaran = SusunAnggaran(baris, peringatan);
                string teks = string.Join("\n", baris.Select(b => string.Join("\t", b)))
                    .Trim();

                if (teks.Length > MaksTeks)
                {
                    teks = teks[..MaksTeks];
                    peringatan.Add("Teks berkas dipotong pada tampilan arsip; berkas aslinya tersimpan utuh.");
                }

                if (anggaran.Count == 0)
                {
                    peringatan.Add(
                        "Tidak ada baris anggaran yang bisa dikenali (kode rekening + uraian + angka). " +
                        "Rincian bisa diisi sendiri di tabel, atau pakai berkas yang tabelnya masih berbentuk tabel Word.");
                }

                string nomor = BacaNomor(teks);
                int? tahun = BacaTahun(teks, nomor);
                string? jenis = TebakJenis(teks);

                _logger.LogInformation(
                    "Berkas APBDes {Berkas} dibaca: {Baris} baris anggaran, {Teks} karakter teks.",
                    Path.GetFileName(berkas), anggaran.Count, teks.Length);

                return new HasilImporApbdes
                {
                    NamaBerkas = Path.GetFileName(berkas),
                    Anggaran = anggaran,
                    Teks = teks,
                    Nomor = nomor,
                    Tahun = tahun,
                    Jenis = jenis,
                    Peringatan = peringatan
                };
            }
            finally
            {
                if (!string.IsNullOrEmpty(sementara))
                {
                    try { File.Delete(sementara); }
                    catch { /* berkas sementara; dibersihkan lain waktu */ }
                }
            }
        }

        // =====================================================================
        // Pembacaan berkas → baris (sel tabel atau baris teks)
        // =====================================================================

        private static async Task<List<List<string>>> BacaBarisAsync(string berkas, CancellationToken ct)
        {
            var hasil = new List<List<string>>();

            ZipArchive arsip;
            try
            {
                arsip = await Task.Run(() => ZipFile.OpenRead(berkas), ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new InvalidOperationException(
                    "Berkas tidak bisa dibuka sebagai dokumen Word (.docx). Pastikan berkasnya utuh.", ex);
            }

            using (arsip)
            {
                var entri = arsip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
                if (entri == null)
                {
                    throw new InvalidOperationException(
                        "Berkas ini tidak memuat isi dokumen Word (word/document.xml).");
                }

                string xml = await Task.Run(() =>
                {
                    using var aliran = entri.Open();
                    using var pembaca = new StreamReader(aliran, Encoding.UTF8);
                    return pembaca.ReadToEnd();
                }, ct);

                XDocument dokumen;
                try
                {
                    dokumen = XDocument.Parse(xml);
                }
                catch (System.Xml.XmlException ex)
                {
                    throw new InvalidOperationException("Isi berkas Word tidak bisa dibaca (formatnya rusak).", ex);
                }

                var body = dokumen.Root?.Element(W + "body");
                if (body == null) return hasil;

                foreach (var anak in body.Elements())
                {
                    if (anak.Name == W + "p")
                    {
                        string teks = TeksPolos(anak);
                        if (teks.Length > 0) hasil.Add(new List<string> { teks });
                    }
                    else if (anak.Name == W + "tbl")
                    {
                        foreach (var baris in anak.Elements(W + "tr"))
                        {
                            var sel = baris.Elements(W + "tc").Select(TeksPolos).ToList();
                            if (sel.Any(s => s.Length > 0)) hasil.Add(sel);
                        }
                    }
                }
            }

            return hasil;
        }

        /// <summary>Teks seluruh isi satu elemen (paragraf atau sel tabel), dipisah tanda baca.</summary>
        private static string TeksPolos(XElement elemen)
        {
            var sb = new StringBuilder();
            foreach (var anak in elemen.Descendants())
            {
                if (anak.Name == W + "t")
                {
                    sb.Append(anak.Value);
                }
                else if (anak.Name == W + "tab")
                {
                    sb.Append('\t');
                }
                else if (anak.Name == W + "br")
                {
                    sb.Append(' ');
                }
            }

            return Rapikan(sb.ToString());
        }

        /// <summary>
        /// Rapikan teks tanpa memadatkan spasi: jarak antar kolom pada dokumen
        /// APBDes yang terlanjur menjadi teks adalah satu-satunya pemisah antara
        /// uraian dan angka, jadi spasi ganda harus dipertahankan.
        /// </summary>
        private static string Rapikan(string? teks)
        {
            if (string.IsNullOrEmpty(teks)) return string.Empty;

            return teks
                .Replace('\u00A0', ' ')
                .Replace("\r", string.Empty)
                .Replace('\n', ' ')
                .Trim();
        }

        // =====================================================================
        // Baris → anggaran
        // =====================================================================

        private static List<BarisAnggaranApbdes> SusunAnggaran(
            List<List<string>> baris, List<string> peringatan)
        {
            var hasil = new List<BarisAnggaranApbdes>();
            bool adaTabel = false;

            foreach (var sel in baris)
            {
                if (hasil.Count >= MaksBarisAnggaran)
                {
                    peringatan.Add($"Hanya {MaksBarisAnggaran} baris pertama yang dibaca.");
                    break;
                }

                // Baris tabel: sel pertama kode rekening.
                if (sel.Count >= 2)
                {
                    var dariTabel = BacaBarisSel(sel);
                    if (dariTabel != null)
                    {
                        hasil.Add(dariTabel);
                        adaTabel = true;
                        continue;
                    }
                }

                // Baris teks (tabel yang terlanjur menjadi teks, atau berkas tanpa tabel).
                var dariTeks = BacaBarisTeks(sel.FirstOrDefault() ?? string.Empty);
                if (dariTeks != null)
                {
                    hasil.Add(dariTeks);
                }
            }

            if (!adaTabel && hasil.Count > 0)
            {
                peringatan.Add(
                    "Tabel pada berkas tidak berbentuk tabel Word, jadi baris dibaca dari teksnya. " +
                    "Periksa kolom anggaran/realisasi sebelum menyimpan.");
            }

            return hasil;
        }

        /// <summary>Baca satu baris tabel; null bila bukan baris data anggaran.</summary>
        private static BarisAnggaranApbdes? BacaBarisSel(List<string> sel)
        {
            string kode = (sel[0] ?? string.Empty).Trim();
            if (!PolaKode.IsMatch(kode)) return null;

            string uraian = string.Empty;
            int mulaiAngka = sel.Count;
            for (int i = 1; i < sel.Count; i++)
            {
                string isi = (sel[i] ?? string.Empty).Trim();
                if (isi.Length == 0) continue;

                if (PolaAngka.IsMatch(isi) && isi.Any(char.IsDigit) && Uang(isi).HasValue)
                {
                    mulaiAngka = i;
                    break;
                }

                uraian = uraian.Length == 0 ? isi : uraian + " " + isi;
            }

            if (uraian.Length == 0) return null;
            if (KataBukanData.Any(k => uraian.Equals(k, StringComparison.OrdinalIgnoreCase))) return null;

            var angka = new List<long>();
            for (int i = mulaiAngka; i < sel.Count && angka.Count < 2; i++)
            {
                var nilai = Uang(sel[i]);
                if (nilai.HasValue) angka.Add(nilai.Value);
            }

            // Baris tanpa angka apa pun bukan data anggaran (mis. baris judul
            // kelompok rekening); dibiarkan supaya tabel hasil impor bersih.
            if (angka.Count == 0) return null;

            return new BarisAnggaranApbdes(
                kode,
                uraian,
                angka[0],
                angka.Count > 1 ? angka[1] : 0);
        }

        /// <summary>Baca satu baris teks; null bila bukan baris data anggaran.</summary>
        private static BarisAnggaranApbdes? BacaBarisTeks(string baris)
        {
            if (string.IsNullOrWhiteSpace(baris)) return null;

            // Tabel yang terbaca sebagai teks memakai tab antar kolom.
            if (baris.Contains('\t'))
            {
                var sel = baris.Split('\t', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
                if (sel.Count >= 2)
                {
                    var dariSel = BacaBarisSel(sel);
                    if (dariSel != null) return dariSel;
                }
            }

            var cocok = PolaBarisTeks.Match(baris);
            if (!cocok.Success) return null;

            string kode = cocok.Groups["kode"].Value;
            string uraian = Rapikan(cocok.Groups["uraian"].Value);
            if (uraian.Length == 0) return null;
            if (KataBukanData.Any(k => uraian.Equals(k, StringComparison.OrdinalIgnoreCase))) return null;

            var angka = cocok.Groups["angka"].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uang)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (angka.Count == 0) return null;

            return new BarisAnggaranApbdes(
                kode,
                uraian,
                angka[0],
                angka.Count > 1 ? angka[1] : 0);
        }

        /// <summary>
        /// Baca angka rupiah dari tulisan apa adanya: "1.234.567", "1.234.567,00",
        /// "1,234,567.00", "Rp 900.000", "900000". Null bila tidak ada angkanya.
        /// </summary>
        public static long? Uang(string? teks)
        {
            if (string.IsNullOrWhiteSpace(teks)) return null;

            string bersih = new((teks ?? string.Empty)
                .Where(c => char.IsDigit(c) || c == ',' || c == '.' || c == '-')
                .ToArray());
            bersih = bersih.TrimStart('-');

            if (bersih.Length == 0 || !bersih.Any(char.IsDigit)) return null;

            int koma = bersih.LastIndexOf(',');
            int titik = bersih.LastIndexOf('.');
            char pemisahDesimal = '\0';

            if (koma >= 0 && titik >= 0)
            {
                pemisahDesimal = koma > titik ? ',' : '.';
            }
            else
            {
                int satu = Math.Max(koma, titik);
                if (satu >= 0)
                {
                    int sisa = bersih.Length - satu - 1;
                    // Tiga digit di belakang pemisah tunggal = pemisah ribuan
                    // ("1.234", "12.500"), kecuali angkanya pendek ("1,5").
                    pemisahDesimal = sisa == 3 && bersih.Length > 4 ? '\0' : bersih[satu];
                }
            }

            string bulat = bersih;
            string pecahan = string.Empty;
            if (pemisahDesimal != '\0')
            {
                int posisi = bersih.LastIndexOf(pemisahDesimal);
                bulat = bersih[..posisi];
                pecahan = bersih[(posisi + 1)..];
            }

            bulat = bulat.Replace(".", string.Empty).Replace(",", string.Empty);
            if (bulat.Length == 0) bulat = "0";
            if (!bulat.All(char.IsDigit)) return null;

            if (!long.TryParse(bulat, NumberStyles.None, CultureInfo.InvariantCulture, out long nilai))
            {
                return null;
            }

            if (pecahan.Length > 0 && pecahan.All(char.IsDigit) &&
                long.TryParse(pecahan[..Math.Min(pecahan.Length, 6)], NumberStyles.None,
                    CultureInfo.InvariantCulture, out long senilai))
            {
                double bagian = senilai / Math.Pow(10, Math.Min(pecahan.Length, 6));
                nilai += (long)Math.Round(bagian, MidpointRounding.AwayFromZero);
            }

            return nilai;
        }

        // =====================================================================
        // Petunjuk dari teks berkas
        // =====================================================================

        /// <summary>Nomor dokumen dari teks, mis. "NOMOR 1 TAHUN 2026".</summary>
        private static string BacaNomor(string teks)
        {
            try
            {
                var cocok = WordParsingRegex.NomorRegex.Match(teks);
                return cocok.Success ? cocok.Groups[1].Value.Trim() : string.Empty;
            }
            catch (RegexMatchTimeoutException)
            {
                return string.Empty;
            }
        }

        /// <summary>Tahun anggaran: dari nomor bila ada, kalau tidak dari tahun pertama di teks.</summary>
        private static int? BacaTahun(string teks, string nomor)
        {
            var dariNomor = Regex.Match(nomor, @"(19|20)\d{2}");
            if (dariNomor.Success && int.TryParse(dariNomor.Value, out int tahunNomor))
            {
                return tahunNomor;
            }

            var dariTeks = Regex.Match(teks, @"\bTAHUN\s+(?<tahun>(?:19|20)\d{2})\b",
                RegexOptions.IgnoreCase);
            if (dariTeks.Success && int.TryParse(dariTeks.Groups["tahun"].Value, out int tahunTeks))
            {
                return tahunTeks;
            }

            var pertama = Regex.Match(teks, @"\b(?:19|20)\d{2}\b");
            return pertama.Success && int.TryParse(pertama.Value, out int tahun) ? tahun : null;
        }

        /// <summary>Tahapan dokumen menurut kata di berkas; null bila tidak jelas.</summary>
        private static string? TebakJenis(string teks)
        {
            string besar = teks.ToUpperInvariant();
            if (besar.Contains("PERTANGGUNGJAWABAN")) return "APBDes Pertanggungjawaban (LPJ)";
            if (besar.Contains("PERUBAHAN")) return "APBDes Perubahan";
            if (besar.Contains("RANCANGAN")) return "Rancangan APBDes";
            if (besar.Contains("PENJABARAN") || besar.Contains("APB DESA") || besar.Contains("APBDESA"))
                return "APBDes Awal";
            return null;
        }
    }
}
