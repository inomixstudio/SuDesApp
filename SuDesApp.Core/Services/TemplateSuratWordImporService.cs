// TemplateSuratWordImporService.cs
// Membaca berkas Word (.docx) lalu menyusunnya menjadi satu atau beberapa
// Template Surat (menu Template Surat).
//
// Berkas dibaca tanpa library tambahan: .docx adalah arsip ZIP berisi
// word/document.xml, sehingga teks, perataan, tebal/miring, dan batas halaman
// (page break / section break) dapat diambil langsung dari XML-nya.
//
// Berkas .doc (format Word 97-2003) dikonversi otomatis lebih dulu menjadi .docx
// memakai Microsoft Word yang terpasang (WordDocConverter), sehingga pengguna tidak
// perlu menyimpan ulang berkasnya secara manual.
//
// Satu halaman/bagian pada berkas dianggap satu bentuk surat. Berkas yang berisi
// beberapa blanko sekaligus tetap bisa disimpan sebagai beberapa template, dan
// bagian yang salah terpisah dapat diabaikan pengguna lewat dialog pemilihan.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>Satu paragraf hasil pembacaan berkas Word beserta format aslinya.</summary>
    public class WordSuratParagraf
    {
        public string Teks { get; set; } = string.Empty;
        public RataBlokTemplate Rata { get; set; } = RataBlokTemplate.Kiri;
        public bool Tebal { get; set; }
        public bool Miring { get; set; }
    }

    /// <summary>
    /// Satu calon surat dari berkas Word (satu halaman/bagian). Definisi template
    /// sudah disusun di sini supaya dialog pemilihan cukup menampilkan ringkasannya.
    /// </summary>
    public class WordSuratKandidat
    {
        /// <summary>Nomor urut bagian pada berkas (1, 2, 3, …).</summary>
        public int Nomor { get; set; }

        /// <summary>Judul surat yang berhasil dikenali (boleh kosong).</summary>
        public string Judul { get; set; } = string.Empty;

        /// <summary>Jumlah baris teks yang dibaca.</summary>
        public int JumlahBaris { get; set; }

        /// <summary>Jumlah kolom isian yang dikenali (label : titik-titik).</summary>
        public int JumlahKolom { get; set; }

        /// <summary>Ringkasan pendek isi bagian untuk ditampilkan di dialog.</summary>
        public string Cuplikan { get; set; } = string.Empty;

        /// <summary>Definisi template siap simpan (nama masih dapat diubah pengguna).</summary>
        public TemplateSuratKustom Definisi { get; set; } = new();

        /// <summary>Keterangan singkat bagian ini pada dialog pemilihan.</summary>
        public string Keterangan => $"Bagian {Nomor}";
    }

    /// <summary>Hasil pembacaan berkas Word: daftar calon surat dan catatannya.</summary>
    public class WordSuratImpor
    {
        public string NamaBerkas { get; set; } = string.Empty;

        public List<WordSuratKandidat> Kandidat { get; set; } = new();

        /// <summary>Catatan proses pembacaan (batas halaman diperkirakan, tabel, dan lainnya).</summary>
        public List<string> Peringatan { get; set; } = new();

        /// <summary>True bila berkas dipisah menjadi beberapa bagian.</summary>
        public bool Terbagi => Kandidat.Count > 1;
    }

    /// <summary>Pembaca berkas Word (.docx dan .doc lama) untuk pembuatan template surat.</summary>
    public interface ITemplateSuratWordImpor
    {
        Task<WordSuratImpor> BacaAsync(string berkas, CancellationToken ct = default);
    }

    /// <summary>
    /// Mengubah berkas Word menjadi definisi template surat: kop surat desa dipakai
    /// menggantikan kop pada berkas, judul/nomor/tempat &amp; tanggal dikenali, baris
    /// \"Label : ......\" menjadi kolom isian, dan sisa baris menjadi blok teks dengan
    /// perataan serta tebal/miring sesuai berkas aslinya.
    /// </summary>
    public class TemplateSuratWordImporService : ITemplateSuratWordImpor
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        /// <summary>Ukuran berkas yang masih boleh dibaca (di atas ini ditolak).</summary>
        public const long UkuranBerkasMaks = 20L * 1024 * 1024;

        /// <summary>Ukuran XML dokumen yang masih boleh dibaca.</summary>
        public const int UkuranXmlMaks = 20 * 1024 * 1024;

        /// <summary>Batas jumlah bagian yang dibuat dari satu berkas (sisanya diabaikan).</summary>
        public const int MaksBagian = 40;

        private readonly ILogger<TemplateSuratWordImporService> _logger;

        /// <summary>
        /// Pengubah berkas .doc lama menjadi .docx. Boleh kosong: bila tidak tersedia
        /// (mis. Microsoft Word tidak terpasang), berkas .doc ditolak dengan petunjuk
        /// konversi, sedangkan berkas .docx tetap bisa dibaca.
        /// </summary>
        private readonly IWordDocConverter? _konverterDoc;

        public TemplateSuratWordImporService(
            ILogger<TemplateSuratWordImporService> logger,
            IWordDocConverter? konverterDoc = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _konverterDoc = konverterDoc;
        }

        // =====================================================================
        // Pola pengenalan isi surat
        // =====================================================================

        /// <summary>Baris \"Label : ......\" — label pendek, isinya titik-titik/kosong.</summary>
        private static readonly Regex PolaKolom = new(
            @"^(?<label>[^:]{2,45}?)\s*:\s*(?<nilai>[.…_\-–—\s]*)$",
            RegexOptions.Compiled);

        /// <summary>Label kolom yang sah (huruf/angka/tanda baca ringan).</summary>
        private static readonly Regex PolaLabelSah = new(
            @"^[A-Za-z][A-Za-z0-9 ./()'\-]{1,44}$",
            RegexOptions.Compiled);

        /// <summary>Baris nomor surat, mis. \"Nomor : 470/001/Ds/2026\".</summary>
        private static readonly Regex PolaNomor = new(
            @"^\s*(?:nomor|no\.?)\s*[:\-]?\s*(?<nilai>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Nilai nomor yang masih dapat dipakai ulang sebagai awalan nomor.</summary>
        private static readonly Regex PolaNilaiNomor = new(
            @"^[A-Za-z0-9._\-/ ]{4,60}$",
            RegexOptions.Compiled);

        /// <summary>Baris tempat &amp; tanggal, mis. \"Sumberjaya, 12 Januari 2026\".</summary>
        private static readonly Regex PolaTempatTanggal = new(
            @"^(?<tempat>[A-Za-z][A-Za-z .'\-]{2,40}),\s*(?<tanggal>\d{1,2}\s+[A-Za-z]+\s+\d{4}|\d{1,2}[-/]\d{1,2}[-/]\d{4})\s*$",
            RegexOptions.Compiled);

        /// <summary>Baris kop surat desa (pemerintah, alamat, telepon, dan lainnya).</summary>
        private static readonly Regex PolaKop = new(
            @"\b(PEMERINTAH|KABUPATEN|KECAMATAN|KELURAHAN|KANTOR DESA|DESA|KOTA|ALAMAT|TELP|TELEPON|EMAIL|WEBSITE|E-MAIL)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Penanda kop yang kuat: satu baris surat biasa tidak memuatnya, jadi berkas yang
        /// hanya menyebut kata "desa" pada badan surat tidak disangka berkop.
        /// </summary>
        private static readonly Regex PolaKopKuat = new(
            @"\b(PEMERINTAH|KABUPATEN|KECAMATAN|KELURAHAN|KANTOR DESA|ALAMAT|TELP|TELEPON|EMAIL|WEBSITE|E-MAIL|KOTA)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Awalan "a.n."/"a/n"/"atas nama" pada baris jabatan penandatangan.</summary>
        private static readonly Regex PolaDelegasiJabatan = new(
            @"^(?:A\.?\s*N\.?|A/N|ATAS NAMA)(?:\s|$)",
            RegexOptions.Compiled);

        /// <summary>
        /// Baris jabatan yang berdiri sendiri, mis. "KEPALA DESA SUMBERJAYA". Nama desa di
        /// belakang jabatan dibatasi tiga kata supaya kalimat surat tidak ikut terbaca.
        /// </summary>
        private static readonly Regex PolaJabatanKades = new(
            @"^(?:A\.?\s*N\.?|A/N|ATAS NAMA)?\s*(?:KEPALA DESA|KADES|KEPALA KAMPUNG|PETINGGI)(?:\s+[A-Z][A-Z.'\-]*(?:\s+[A-Z][A-Z.'\-]*){0,2})?$",
            RegexOptions.Compiled);

        /// <summary>Baris jabatan Sekretaris Desa yang berdiri sendiri.</summary>
        private static readonly Regex PolaJabatanSekdes = new(
            @"^(?:A\.?\s*N\.?|A/N|ATAS NAMA)?\s*(?:SEKRETARIS DESA|SEKRETARIS KAMPUNG|SEKDES)(?:\s+[A-Z][A-Z.'\-]*(?:\s+[A-Z][A-Z.'\-]*){0,2})?$",
            RegexOptions.Compiled);

        /// <summary>Awal baris yang menandakan bukan nama penandatangan (isi surat bagian lain).</summary>
        private static readonly HashSet<string> KataBukanNamaPenandatangan = new(StringComparer.OrdinalIgnoreCase)
        {
            "tembusan", "mengetahui", "saksi", "catatan", "lampiran", "kepada", "yth",
            "sekian", "demikian", "hormat", "terima", "nomor", "perihal", "hal", "yang",
            "pada", "dengan", "dan", "untuk", "dari", "apabila", "jika", "surat"
        };

        /// <summary>Kata yang menandakan baris kalimat, bukan label kolom isian.</summary>
        private static readonly HashSet<string> KataBukanLabel = new(StringComparer.OrdinalIgnoreCase)
        {
            "yang", "dengan", "dan", "atau", "untuk", "ini", "itu", "adalah", "bahwa",
            "pada", "dari", "selaku", "sebagai", "oleh", "bertanda", "tangan",
            "menerangkan", "menyatakan", "berdasarkan", "sehubungan", "demikian",
            "kepada", "dimana", "apabila", "jika", "hal", "perihal", "lampiran",
            "tembusan"
        };

        /// <summary>Label yang menandakan blok data diri satu orang.</summary>
        private static readonly HashSet<string> LabelDataDiri = new(StringComparer.OrdinalIgnoreCase)
        {
            "nik", "no_kk", "nomor_kk", "nama", "nama_lengkap", "nama_anak",
            "tempat_lahir", "tanggal_lahir", "tempat_tanggal_lahir", "ttl",
            "jenis_kelamin", "agama", "pekerjaan", "status_perkawinan", "kewarganegaraan",
            "alamat", "umur", "jabatan", "pendidikan", "golongan_darah", "no_ktp"
        };

        // =====================================================================
        // Pembacaan berkas
        // =====================================================================

        public async Task<WordSuratImpor> BacaAsync(string berkas, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(berkas))
                throw new ArgumentException("Berkas Word belum dipilih.", nameof(berkas));

            string namaBerkas = Path.GetFileName(berkas);
            var hasil = new WordSuratImpor { NamaBerkas = namaBerkas };

            if (!File.Exists(berkas))
                throw new FileNotFoundException("Berkas Word tidak ditemukan.", berkas);

            string ekstensi = Path.GetExtension(berkas).ToUpperInvariant();
            if (ekstensi != ".DOC" && ekstensi != ".DOCX")
            {
                throw new NotSupportedException(
                    "Hanya berkas Word (.docx, atau .doc lama) yang dapat dijadikan template surat.");
            }

            var info = new FileInfo(berkas);
            if (info.Length > UkuranBerkasMaks)
            {
                throw new InvalidOperationException(
                    $"Berkas terlalu besar (lebih dari {UkuranBerkasMaks / (1024 * 1024)} MB). " +
                    "Pisahkan suratnya menjadi beberapa berkas yang lebih kecil.");
            }

            // Berkas .doc lama dibaca lewat konversi otomatis (Microsoft Word) lebih dulu.
            var paragraf = ekstensi == ".DOC"
                ? await BacaDocLamaAsync(berkas, hasil, ct).ConfigureAwait(false)
                : await BacaDocxAsync(berkas, hasil, ct).ConfigureAwait(false);

            if (paragraf.Count == 0)
            {
                hasil.Peringatan.Add("Tidak ada teks yang dapat dibaca dari berkas ini.");
                return hasil;
            }

            bool adaBatasKeras = paragraf.Any(p => p.BatasKerasSetelah || p.Potongan.Any(x => x.BatasKeras));

            var bagianSurat = BagiBagian(paragraf, pakaiBatasLunak: !adaBatasKeras);
            if (!adaBatasKeras)
            {
                hasil.Peringatan.Add(
                    "Batas halaman diperkirakan dari tata letak terakhir berkas. Periksa hasil " +
                    "pemisahan surat sebelum menyimpan.");
            }

            if (bagianSurat.Count == 0)
            {
                hasil.Peringatan.Add("Seluruh isi berkas kosong sehingga tidak ada surat yang bisa dibuat.");
                return hasil;
            }

            if (bagianSurat.Count > MaksBagian)
            {
                hasil.Peringatan.Add(
                    $"Berkas berisi {bagianSurat.Count} bagian; hanya {MaksBagian} bagian pertama yang diproses.");
                bagianSurat = bagianSurat.Take(MaksBagian).ToList();
            }

            int nomor = 0;
            bool adaTandaTanganOtomatis = false;
            foreach (var isi in bagianSurat)
            {
                nomor++;
                var kandidat = SusunKandidat(isi, nomor, namaBerkas);
                if (kandidat == null) continue;

                hasil.Kandidat.Add(kandidat);
                adaTandaTanganOtomatis |= kandidat.Definisi.PakaiTandaTangan;
            }

            if (adaTandaTanganOtomatis)
            {
                hasil.Peringatan.Add(
                    "Blok tanda tangan pada berkas dikenali dan diganti tanda tangan otomatis aplikasi " +
                    "(jabatan + nama pejabat desa dari Setelan Aplikasi).");
            }

            _logger.LogInformation(
                "Berkas Word {Berkas} dibaca: {Bagian} bagian surat, {Kolom} kolom isian.",
                namaBerkas, hasil.Kandidat.Count, hasil.Kandidat.Sum(k => k.JumlahKolom));

            return hasil;
        }

        /// <summary>
        /// Baca berkas .doc (format Word 97-2003). Berkas dikonversi lebih dulu menjadi
        /// .docx memakai Microsoft Word, lalu dibaca seperti berkas .docx supaya susunan,
        /// tabel, dan batas halamannya tetap terbawa. Berkas sementara selalu dihapus.
        /// </summary>
        private async Task<List<ParagrafMentah>> BacaDocLamaAsync(
            string berkas, WordSuratImpor hasil, CancellationToken ct)
        {
            if (_konverterDoc == null || !_konverterDoc.Tersedia)
            {
                throw new NotSupportedException(WordDocConverter.PesanTanpaWord);
            }

            string? sementara = null;
            try
            {
                sementara = await _konverterDoc.KeDocxAsync(berkas, ct).ConfigureAwait(false);
                hasil.Peringatan.Add(
                    "Berkas .doc lama dikonversi otomatis ke .docx, lalu dibaca. Periksa hasil pemisahan " +
                    "surat sebelum menyimpan.");

                return await BacaDocxAsync(sementara, hasil, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not NotSupportedException && ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Berkas .doc \"{Path.GetFileName(berkas)}\" gagal dikonversi otomatis: {ex.Message}", ex);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sementara))
                {
                    try { File.Delete(sementara); }
                    catch { /* berkas sementara gagal dihapus; dibiarkan agar proses tetap jalan */ }
                }
            }
        }

        /// <summary>Baca word/document.xml dari arsip .docx.</summary>
        private async Task<List<ParagrafMentah>> BacaDocxAsync(
            string berkas, WordSuratImpor hasil, CancellationToken ct)
        {
            ZipArchive arsip;
            try
            {
                arsip = ZipFile.OpenRead(berkas);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new InvalidOperationException(
                    "Berkas tidak dapat dibuka sebagai dokumen Word (.docx). Pastikan berkasnya utuh " +
                    "dan bukan dokumen .doc lama.", ex);
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

                string xml = await BacaTeksEntriAsync(entri, ct).ConfigureAwait(false);

                XDocument dokumen;
                try
                {
                    dokumen = XDocument.Parse(xml);
                }
                catch (System.Xml.XmlException ex)
                {
                    throw new InvalidOperationException(
                        "Isi berkas Word tidak dapat dibaca karena formatnya rusak.", ex);
                }

                var body = dokumen.Root?.Element(W + "body");
                if (body == null) return new List<ParagrafMentah>();

                bool adaTabel = body.Elements(W + "tbl").Any();
                if (adaTabel)
                {
                    hasil.Peringatan.Add("Tabel pada berkas diubah menjadi baris teks biasa.");
                }

                return BacaParagraf(body);
            }
        }

        private static async Task<string> BacaTeksEntriAsync(ZipArchiveEntry entri, CancellationToken ct)
        {
            if (entri.Length > UkuranXmlMaks)
            {
                throw new InvalidOperationException(
                    $"Isi dokumen terlalu besar untuk dibaca (lebih dari {UkuranXmlMaks / (1024 * 1024)} MB).");
            }

            var sb = new StringBuilder();
            var penyangga = new char[8192];

            using var aliran = entri.Open();
            using var pembaca = new StreamReader(aliran, Encoding.UTF8);
            int dibaca;
            while ((dibaca = await pembaca.ReadAsync(penyangga, ct).ConfigureAwait(false)) > 0)
            {
                if (sb.Length + dibaca > UkuranXmlMaks)
                {
                    throw new InvalidOperationException(
                        $"Isi dokumen terlalu besar untuk dibaca (lebih dari {UkuranXmlMaks / (1024 * 1024)} MB).");
                }
                sb.Append(penyangga, 0, dibaca);
            }

            return sb.ToString();
        }

        // =====================================================================
        // Pembacaan XML → paragraf
        // =====================================================================

        /// <summary>Paragraf mentah sebelum dibagi menjadi bagian surat.</summary>
        private sealed class ParagrafMentah
        {
            public List<PotonganParagraf> Potongan { get; } = new();
            public RataBlokTemplate Rata { get; set; } = RataBlokTemplate.Kiri;
            public bool Tebal { get; set; }
            public bool Miring { get; set; }

            /// <summary>Baris pemisah (---, ===, ***) yang memisahkan dua surat.</summary>
            public bool Pemisah { get; set; }

            /// <summary>Paragraf diakhiri batas halaman tanpa teks sesudahnya.</summary>
            public bool BatasKerasSetelah { get; set; }
            public bool BatasLunakSetelah { get; set; }
        }

        /// <summary>Sepotong teks dalam satu paragraf (paragraf bisa terbelah batas halaman).</summary>
        private sealed class PotonganParagraf
        {
            public string Teks { get; set; } = string.Empty;
            public bool BatasKeras { get; set; }
            public bool BatasLunak { get; set; }
        }

        private static List<ParagrafMentah> BacaParagraf(XElement body)
        {
            var hasil = new List<ParagrafMentah>();

            foreach (var anak in body.Elements())
            {
                if (anak.Name == W + "p")
                {
                    var paragraf = BacaSatuParagraf(anak);
                    if (paragraf != null) hasil.Add(paragraf);
                }
                else if (anak.Name == W + "tbl")
                {
                    foreach (var baris in anak.Elements(W + "tr"))
                    {
                        var sel = baris.Elements(W + "tc")
                            .Select(kotak => TeksPolos(kotak))
                            .Where(teks => teks.Length > 0)
                            .ToList();
                        if (sel.Count == 0) continue;

                        var paragraf = new ParagrafMentah();
                        paragraf.Potongan.Add(new PotonganParagraf { Teks = string.Join("  ", sel) });
                        hasil.Add(paragraf);
                    }
                }
            }

            return hasil;
        }

        /// <summary>Seluruh teks di dalam sebuah elemen (dipakai untuk sel tabel).</summary>
        private static string TeksPolos(XElement elemen)
        {
            var sb = new StringBuilder();
            foreach (var teks in elemen.Descendants(W + "t"))
            {
                sb.Append(teks.Value);
            }
            return RapikanTeks(sb.ToString());
        }

        private static ParagrafMentah? BacaSatuParagraf(XElement p)
        {
            var paragraf = new ParagrafMentah { Rata = BacaRata(p.Element(W + "pPr")) };

            var sb = new StringBuilder();
            int hurufTebal = 0, hurufMiring = 0, hurufTotal = 0;
            bool batasKerasTertunda = false, batasLunakTertunda = false;

            // Batas yang tertunda hanya "terpakai" bila ada teks sesudahnya; bila tidak,
            // batas itu menjadi pemisah antar bagian surat.
            void SiramPotongan()
            {
                string teks = RapikanTeks(sb.ToString());
                sb.Clear();
                if (teks.Length == 0) return;

                paragraf.Potongan.Add(new PotonganParagraf
                {
                    Teks = teks,
                    BatasKeras = batasKerasTertunda,
                    BatasLunak = batasLunakTertunda
                });
                batasKerasTertunda = false;
                batasLunakTertunda = false;
            }

            foreach (var run in p.Descendants(W + "r"))
            {
                var rPr = run.Element(W + "rPr");
                bool tebal = BacaAktif(rPr?.Element(W + "b"));
                bool miring = BacaAktif(rPr?.Element(W + "i"));

                foreach (var simpul in run.Elements())
                {
                    if (simpul.Name == W + "t")
                    {
                        sb.Append(simpul.Value);
                    }
                    else if (simpul.Name == W + "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (simpul.Name == W + "br")
                    {
                        string jenis = simpul.Attribute(W + "type")?.Value ?? string.Empty;
                        if (string.Equals(jenis, "page", StringComparison.OrdinalIgnoreCase))
                        {
                            SiramPotongan();
                            batasKerasTertunda = true;
                        }
                        else
                        {
                            sb.Append('\n');
                        }
                    }
                    else if (simpul.Name == W + "lastRenderedPageBreak")
                    {
                        SiramPotongan();
                        batasLunakTertunda = true;
                    }
                }

                // Hitung proporsi teks tebal/miring untuk menentukan format paragraf.
                int hurufRun = run.Elements(W + "t").Sum(t => t.Value.Count(char.IsLetter));
                hurufTotal += hurufRun;
                if (tebal) hurufTebal += hurufRun;
                if (miring) hurufMiring += hurufRun;
            }

            SiramPotongan();
            paragraf.BatasKerasSetelah = batasKerasTertunda;
            paragraf.BatasLunakSetelah = batasLunakTertunda;

            if (paragraf.Potongan.Count == 0)
            {
                // Paragraf kosong atau hanya berisi batas halaman: tetap dipakai bila
                // membawa batas, supaya bagian surat terpisah dengan benar.
                return (paragraf.BatasKerasSetelah || paragraf.BatasLunakSetelah) ? paragraf : null;
            }

            paragraf.Tebal = hurufTotal > 0 && hurufTebal * 100 >= hurufTotal * 60;
            paragraf.Miring = hurufTotal > 0 && hurufMiring * 100 >= hurufTotal * 60;

            if (paragraf.Potongan.Count == 1 && IsPemisah(paragraf.Potongan[0].Teks))
            {
                paragraf.Pemisah = true;
            }

            return paragraf;
        }

        private static RataBlokTemplate BacaRata(XElement? pPr)
        {
            string nilai = pPr?.Element(W + "jc")?.Attribute(W + "val")?.Value ?? string.Empty;
            return nilai.ToLowerInvariant() switch
            {
                "center" => RataBlokTemplate.Tengah,
                "right" => RataBlokTemplate.Kanan,
                "end" => RataBlokTemplate.Kanan,
                "both" => RataBlokTemplate.Justify,
                "distribute" => RataBlokTemplate.Justify,
                _ => RataBlokTemplate.Kiri
            };
        }

        /// <summary>Format Word \"aktif\": elemen ada dan tidak dimatikan (val=\"0\"/\"false\").</summary>
        private static bool BacaAktif(XElement? elemen)
        {
            if (elemen == null) return false;
            string nilai = elemen.Attribute(W + "val")?.Value ?? string.Empty;
            return !(nilai == "0" || nilai.Equals("false", StringComparison.OrdinalIgnoreCase)
                     || nilai.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Rapikan spasi berlebih tanpa menghilangkan tab dan baris baru.</summary>
        private static string RapikanTeks(string teks)
        {
            if (string.IsNullOrEmpty(teks)) return string.Empty;

            var sb = new StringBuilder(teks.Length);
            bool spasiSebelumnya = false;
            foreach (char c in teks)
            {
                if (c == '\t')
                {
                    sb.Append(c);
                    spasiSebelumnya = false;
                }
                else if (c == ' ' || c == '\u00A0')
                {
                    if (spasiSebelumnya) continue;
                    spasiSebelumnya = true;
                    sb.Append(' ');
                }
                else
                {
                    spasiSebelumnya = false;
                    sb.Append(c);
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>Baris pemisah antar surat: ---, ===, ***, ___ (tanpa huruf/angka).</summary>
        private static bool IsPemisah(string teks)
        {
            string bersih = teks.Trim();
            if (bersih.Length < 3 || bersih.Length > 40) return false;
            if (bersih.Any(char.IsLetterOrDigit)) return false;

            int tanda = bersih.Count(c => c == '-' || c == '=' || c == '_' || c == '*' || c == '~' || c == '•');
            return tanda >= 3 && tanda == bersih.Length;
        }

        // =====================================================================
        // Pembagian berkas menjadi bagian surat
        // =====================================================================

        /// <summary>
        /// Bagi paragraf menjadi bagian-bagian surat. Batas yang dipakai adalah batas
        /// halaman/bagian Word; bila berkas tidak punya batas keras, posisi halaman
        /// terakhir yang direkam Word dipakai sebagai perkiraan.
        /// </summary>
        private static List<List<WordSuratParagraf>> BagiBagian(List<ParagrafMentah> paragraf, bool pakaiBatasLunak)
        {
            var hasil = new List<List<WordSuratParagraf>>();
            var sekarang = new List<WordSuratParagraf>();

            void Tutup()
            {
                if (sekarang.Count > 0)
                {
                    hasil.Add(sekarang);
                    sekarang = new List<WordSuratParagraf>();
                }
            }

            foreach (var p in paragraf)
            {
                if (p.Pemisah)
                {
                    Tutup();
                    continue;
                }

                foreach (var potongan in p.Potongan)
                {
                    bool batas = pakaiBatasLunak ? potongan.BatasLunak : potongan.BatasKeras;
                    if (batas) Tutup();

                    if (potongan.Teks.Length > 0)
                    {
                        sekarang.Add(new WordSuratParagraf
                        {
                            Teks = potongan.Teks,
                            Rata = p.Rata,
                            Tebal = p.Tebal,
                            Miring = p.Miring
                        });
                    }
                }

                bool batasSetelah = pakaiBatasLunak ? p.BatasLunakSetelah : p.BatasKerasSetelah;
                if (batasSetelah) Tutup();
            }

            Tutup();

            // Buang bagian yang tidak memuat huruf/angka (sisa batas halaman).
            return hasil
                .Where(bagian => bagian.Any(x => x.Teks.Any(char.IsLetterOrDigit)))
                .ToList();
        }

        // =====================================================================
        // Penyusunan definisi template
        // =====================================================================

        private WordSuratKandidat? SusunKandidat(List<WordSuratParagraf> paragraf, int nomor, string namaBerkas)
        {
            string namaDasar = $"{Path.GetFileNameWithoutExtension(namaBerkas)} — bagian {nomor}";
            var (template, judul, jumlahKolom) = SusunTemplate(paragraf, namaDasar);

            if (template.JumlahElemen == 0) return null;

            template.Nama = string.IsNullOrWhiteSpace(judul) ? namaDasar : judul;
            template.Deskripsi =
                $"Dibuat dari berkas Word \"{namaBerkas}\" — bagian {nomor} ({paragraf.Count} baris).";

            return new WordSuratKandidat
            {
                Nomor = nomor,
                Judul = judul,
                JumlahBaris = paragraf.Count,
                JumlahKolom = jumlahKolom,
                Cuplikan = Cuplikan(paragraf),
                Definisi = template
            };
        }

        private static string Cuplikan(List<WordSuratParagraf> paragraf)
        {
            var potongan = paragraf
                .Select(p => p.Teks.Replace('\n', ' ').Replace('\t', ' ').Trim())
                .Where(t => t.Length > 0)
                .Take(3)
                .ToList();

            string gabung = string.Join(" • ", potongan);
            return gabung.Length <= 140 ? gabung : gabung.Substring(0, 140) + "…";
        }

        /// <summary>
        /// Susun definisi template dari paragraf hasil pembacaan: kop, judul, nomor,
        /// tempat &amp; tanggal dikenali lebih dulu, lalu sisanya menjadi blok teks dan
        /// kelompok kolom isian berurutan.
        /// </summary>
        private static (TemplateSuratKustom Template, string Judul, int JumlahKolom) SusunTemplate(
            IReadOnlyList<WordSuratParagraf> paragraf,
            string namaCadangan)
        {
            var template = new TemplateSuratKustom
            {
                Nama = namaCadangan,
                PakaiKop = false,
                PakaiNomor = false,
                PakaiTempatTanggal = false,
                PakaiTandaTangan = false,
                PakaiTeksKaki = false,
                PakaiGrid = false,
                AwalanNomor = "470",
                PolaNomor = TemplateSuratNomor.PolaBawaan
            };

            var teks = paragraf.Select(p => p.Teks).ToList();
            var buang = new HashSet<int>();

            // ---- 1. Judul surat (baris kapital pertama yang bukan baris kop).
            int idxJudul = CariJudul(teks);
            string judul = string.Empty;
            if (idxJudul >= 0)
            {
                judul = teks[idxJudul];

                // Judul yang terpecah dua baris (mis. \"SURAT\" lalu \"KETERANGAN\").
                if (!judul.Contains(' ') && idxJudul + 1 < teks.Count
                    && IsBarisJudul(teks[idxJudul + 1]) && !IsBarisKop(teks[idxJudul + 1])
                    && judul.Length + teks[idxJudul + 1].Length + 1 <= 90)
                {
                    judul = judul + " " + teks[idxJudul + 1];
                    buang.Add(idxJudul + 1);
                }
                buang.Add(idxJudul);
            }

            // ---- 2. Kop surat pada berkas: diganti kop desa aplikasi. Kop hanya dicari
            // di atas judul, supaya baris surat di bawah judul tidak ikut terbuang.
            int batasKop = idxJudul > 0 ? idxJudul : (idxJudul < 0 ? Math.Min(4, teks.Count) : 0);
            var barisKop = new List<int>();
            for (int i = 0; i < batasKop; i++)
            {
                if (IsBarisKop(teks[i])) barisKop.Add(i);
            }

            // Baris kop baru dianggap ada bila berkas memuat penanda kuat (pemerintah,
            // kabupaten, alamat, dan sejenisnya), bukan sekadar kata "desa" pada baris surat.
            bool pakaiKop = barisKop.Count >= 2 && barisKop.Any(i => PolaKopKuat.IsMatch(teks[i]));
            if (pakaiKop)
            {
                foreach (int i in barisKop) buang.Add(i);
            }

            // ---- 3. Baris nomor surat.
            int idxNomor = -1;
            string awalan = "470";
            for (int i = 0; i < teks.Count; i++)
            {
                if (buang.Contains(i)) continue;
                if (!CobaBarisNomor(teks[i], i + 1 < teks.Count ? teks[i + 1] : null,
                        out string awalanBaru, out bool ambilBarisBerikutnya)) continue;

                idxNomor = i;
                awalan = awalanBaru;
                buang.Add(i);
                if (ambilBarisBerikutnya && i + 1 < teks.Count) buang.Add(i + 1);
                break;
            }

            // ---- 4. Baris tempat & tanggal.
            int idxTempatTanggal = -1;
            for (int i = 0; i < teks.Count; i++)
            {
                if (buang.Contains(i)) continue;
                if (!PolaTempatTanggal.IsMatch(teks[i].Trim())) continue;

                idxTempatTanggal = i;
                buang.Add(i);
                break;
            }

            // ---- 5. Blok tanda tangan di bagian bawah surat: dikenali supaya aplikasi
            // mencetak tanda tangan pejabat desa sendiri (jabatan + nama pejabat).
            var tandaTangan = CariTandaTangan(teks, buang);
            foreach (int i in tandaTangan.Baris) buang.Add(i);

            // ---- 6. Badan surat: blok teks dan kelompok kolom isian berurutan.
            var dipakaiKunci = new List<string>();
            var kelompok = new List<(string Label, bool Wajib)>();
            int jumlahKolom = 0;
            int jumlahDataDiri = 0, jumlahKelompokKolom = 0;

            void SiramKelompok()
            {
                if (kelompok.Count == 0) return;

                int identitas = kelompok.Count(k => LabelDataDiri.Contains(TemplateSuratKunci.Slug(k.Label)));
                bool dataDiri = kelompok.Count >= 2 && identitas * 2 >= kelompok.Count;
                string kunciBagian = dataDiri
                    ? $"datadiri{jumlahDataDiri++}"
                    : $"kelompok{jumlahKelompokKolom++}";

                var bagian = new BagianTemplateSurat
                {
                    Tipe = dataDiri ? TipeBagianTemplate.DataDiri : TipeBagianTemplate.Kolom,
                    Kunci = kunciBagian
                };

                foreach (var (label, wajib) in kelompok)
                {
                    string kunci = dataDiri
                        ? $"{kunciBagian}_{TemplateSuratKunci.Slug(label)}"
                        : TemplateSuratKunci.Unik(label, dipakaiKunci);
                    if (dipakaiKunci.Contains(kunci, StringComparer.OrdinalIgnoreCase))
                    {
                        kunci = TemplateSuratKunci.Unik(kunci, dipakaiKunci);
                    }
                    dipakaiKunci.Add(kunci);

                    var kolom = new KolomTemplateSurat
                    {
                        Label = label,
                        Tipe = PetaTipeKolom(label),
                        Wajib = wajib,
                        Kunci = kunci
                    };

                    bagian.Kolom.Add(kolom);
                    template.Kolom.Add(kolom);
                }

                template.Bagian.Add(bagian);
                jumlahKolom += bagian.Kolom.Count;
                kelompok.Clear();
            }

            for (int i = 0; i < paragraf.Count; i++)
            {
                if (buang.Contains(i)) continue;

                var baris = paragraf[i];
                if (baris.Teks.Length == 0) continue;

                if (CobaKolom(baris.Teks, out string label, out bool wajib))
                {
                    kelompok.Add((label, wajib));
                    continue;
                }

                SiramKelompok();
                template.Bagian.Add(new BagianTemplateSurat
                {
                    Tipe = TipeBagianTemplate.Teks,
                    Isi = baris.Teks,
                    Rata = baris.Rata,
                    Tebal = baris.Tebal,
                    Miring = baris.Miring
                });
            }
            SiramKelompok();

            template.Judul = judul;
            template.PakaiKop = pakaiKop;
            template.PakaiNomor = idxNomor >= 0;
            template.AwalanNomor = awalan;
            template.PakaiTandaTangan = tandaTangan.Ketemu;
            template.JabatanPenandatangan = tandaTangan.Sekretaris ? "Sekretaris Desa" : string.Empty;

            // Blok tanda tangan aplikasi sudah mencetak "Desa, tanggal" sendiri, jadi baris
            // tempat & tanggal pada badan surat tidak dipakai lagi bila blok tanda tangan
            // berkas berhasil dikenali (kalau tidak, tanggal akan tercetak dua kali).
            template.PakaiTempatTanggal = idxTempatTanggal >= 0 && !tandaTangan.Ketemu;

            // Surat harus punya minimal satu komponen resmi: bila berkas sama sekali
            // tidak memuat kop/judul/nomor/tempat & tanggal, kop desa dipakai.
            if (!template.PakaiKop && !template.PakaiNomor && !template.PakaiTempatTanggal
                && string.IsNullOrWhiteSpace(template.Judul))
            {
                template.PakaiKop = true;
            }

            return (template, judul, jumlahKolom);
        }

        /// <summary>Bentuk judul: baris pendek, hampir seluruhnya huruf kapital, tanpa titik dua.</summary>
        private static bool IsBarisJudul(string? teks)
        {
            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length < 3 || baris.Length > 90) return false;
            if (baris.Contains(':')) return false;

            int huruf = baris.Count(char.IsLetter);
            if (huruf < 3) return false;

            int kapital = baris.Count(c => char.IsLetter(c) && char.IsUpper(c));
            return kapital * 100 >= huruf * 85;
        }

        private static int CariJudul(IReadOnlyList<string> teks)
        {
            int batas = Math.Min(6, teks.Count);
            for (int i = 0; i < batas; i++)
            {
                if (IsBarisJudul(teks[i]) && !IsBarisKop(teks[i])) return i;
            }
            return -1;
        }

        private static bool IsBarisKop(string? teks) =>
            !string.IsNullOrWhiteSpace(teks) && PolaKop.IsMatch(teks);

        /// <summary>
        /// Kenali blok tanda tangan di bagian bawah surat — mis. "Kepala Desa" atau
        /// "a.n. Kepala Desa / Sekretaris Desa" beserta nama, titik-titik, dan NIP.
        /// Bila dikenali, baris-barisnya dibuang dari badan surat karena aplikasi mencetak
        /// blok tanda tangan sendiri dari jabatan dan nama pejabat desa pada setelan.
        /// </summary>
        private static (bool Ketemu, bool Sekretaris, List<int> Baris) CariTandaTangan(
            IReadOnlyList<string> teks, IReadOnlyCollection<int> sudahDibuang)
        {
            var tidakAda = (Ketemu: false, Sekretaris: false, Baris: new List<int>());

            // Blok tanda tangan selalu berada di bagian paling bawah surat, jadi hanya
            // beberapa baris terakhir yang diperiksa.
            var ekor = new List<int>();
            for (int i = teks.Count - 1; i >= 0 && ekor.Count < 8; i--)
            {
                if (sudahDibuang.Contains(i) || teks[i].Trim().Length == 0) continue;
                ekor.Add(i);
            }
            ekor.Reverse();

            // Cari dari bawah: bila berkas memuat "a.n. Kepala Desa" lalu "Sekretaris Desa",
            // baris jabatan yang paling bawah itulah penandatangannya.
            int idxJabatan = -1;
            bool sekretaris = false;
            for (int n = ekor.Count - 1; n >= 0; n--)
            {
                string? jabatan = BacaJabatanPenandatangan(teks[ekor[n]]);
                if (jabatan == null) continue;

                idxJabatan = ekor[n];
                sekretaris = jabatan == "sekretaris";
                break;
            }
            if (idxJabatan < 0) return tidakAda;

            var baris = new List<int> { idxJabatan };

            // Baris di atas jabatan yang masih bagian blok tanda tangan: tempat & tanggal,
            // titik-titik/"(ttd)", atau baris jabatan lain (mis. "a.n. Kepala Desa").
            int atas = idxJabatan;
            for (int langkah = 0; langkah < 3; langkah++)
            {
                int sebelum = BarisSebelumnya(teks, atas, sudahDibuang);
                if (sebelum < 0) break;

                string teksSebelum = teks[sebelum].Trim();
                if (!PolaTempatTanggal.IsMatch(teksSebelum)
                    && !IsBarisRuangTandaTangan(teksSebelum)
                    && BacaJabatanPenandatangan(teksSebelum) == null) break;

                baris.Add(sebelum);
                atas = sebelum;
            }

            // Nama penandatangan, baris NIP, dan sisa titik-titik di bawah jabatan. Baris
            // pertama yang bukan bagian tanda tangan (mis. "Tembusan :") menghentikan ini.
            int batas = Math.Min(teks.Count, idxJabatan + 5);
            for (int i = idxJabatan + 1; i < batas; i++)
            {
                if (sudahDibuang.Contains(i) || baris.Contains(i)) continue;

                string bawah = teks[i].Trim();
                if (bawah.Length == 0) continue;
                if (!IsBarisPenandatangan(bawah)) break;

                baris.Add(i);
            }

            return (true, sekretaris, baris);
        }

        /// <summary>Indeks baris berisi teks sebelum <paramref name="mulai"/>; -1 bila tidak ada.</summary>
        private static int BarisSebelumnya(
            IReadOnlyList<string> teks, int mulai, IReadOnlyCollection<int> sudahDibuang)
        {
            for (int i = mulai - 1; i >= 0; i--)
            {
                if (sudahDibuang.Contains(i)) continue;
                if (teks[i].Trim().Length == 0) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Kenali baris jabatan penandatangan: "kepala desa" atau "sekretaris" (surat yang
        /// ditandatangani a.n. Kepala Desa disamakan dengan Sekretaris Desa, sesuai bentuk
        /// baku blok tanda tangan aplikasi). Null bila barisnya bukan jabatan.
        /// </summary>
        private static string? BacaJabatanPenandatangan(string? teks)
        {
            string padat = RapikanBaris(teks);
            if (padat.Length < 4 || padat.Length > 70) return null;
            if (padat.Any(char.IsDigit)) return null;

            // Kata sambung/verba surat tidak pernah muncul pada baris jabatan yang berdiri
            // sendiri, jadi baris kalimat seperti "KEPALA DESA MENERANGKAN BAHWA ..." ditolak.
            foreach (string kata in padat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (KataBukanLabel.Contains(kata)) return null;
            }

            if (PolaJabatanSekdes.IsMatch(padat)) return "sekretaris";
            if (PolaJabatanKades.IsMatch(padat))
            {
                return PolaDelegasiJabatan.IsMatch(padat) ? "sekretaris" : "kepala desa";
            }
            return null;
        }

        /// <summary>Rapikan baris untuk pencocokan pola: kapital, spasi tunggal, tanpa tanda baca akhir.</summary>
        private static string RapikanBaris(string? teks)
        {
            string baris = Regex.Replace((teks ?? string.Empty).Trim(), @"\s+", " ").ToUpperInvariant();
            return baris.TrimEnd('.', ',', ':', ';').Trim();
        }

        /// <summary>Baris ruang tanda tangan: titik-titik/garis atau penanda "(ttd)".</summary>
        private static bool IsBarisRuangTandaTangan(string? teks)
        {
            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length < 2 || baris.Length > 60) return false;

            string rapat = baris.Replace(" ", string.Empty);
            if (rapat.Length >= 3 && rapat.All(c => c is '.' or '_' or '-' or '–' or '—' or '…')) return true;

            string saja = baris.Trim('(', ')', '.', ':', ',', ' ').ToUpperInvariant();
            return saja is "TTD" or "TANDA TANGAN";
        }

        /// <summary>Nama penandatangan, baris NIP, atau "Nama : ..." di dalam blok tanda tangan.</summary>
        private static bool IsBarisPenandatangan(string teks)
        {
            string baris = teks.Trim();
            if (IsBarisRuangTandaTangan(baris)) return true;
            if (baris.Length < 3 || baris.Length > 60) return false;

            // "Nama : H. SURYANA" dan "NIP : 1968..." masih bagian blok tanda tangan.
            var kolom = PolaKolom.Match(baris);
            if (kolom.Success)
            {
                string label = TemplateSuratKunci.Slug(kolom.Groups["label"].Value);
                return label is "nama" or "nama_lengkap" or "nip" or "nip_nip";
            }

            if (baris.Contains(':') || baris.Contains(';')) return false;

            string besar = baris.ToUpperInvariant();
            if (KataBukanNamaPenandatangan.Any(k => besar.StartsWith(k, StringComparison.Ordinal))) return false;

            if (besar.StartsWith("NIP", StringComparison.Ordinal)) return true;
            if (baris.Any(char.IsDigit) || !baris.Any(char.IsLetter)) return false;

            int kata = baris.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return kata <= 6;
        }

        /// <summary>
        /// Kenali baris nomor surat beserta awalannya. Nomor pada berkas dipakai sebagai
        /// awalan supaya penomoran otomatis aplikasi tetap sejenis dengan berkas aslinya.
        /// </summary>
        private static bool CobaBarisNomor(string? teks, string? barisBerikutnya,
            out string awalan, out bool ambilBarisBerikutnya)
        {
            awalan = "470";
            ambilBarisBerikutnya = false;

            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length == 0 || baris.Length > 80) return false;

            var cocok = PolaNomor.Match(baris);
            if (!cocok.Success) return false;

            string nilai = cocok.Groups["nilai"].Value.Trim();
            if (nilai.Length == 0 && !string.IsNullOrWhiteSpace(barisBerikutnya)
                && PolaNilaiNomor.IsMatch(barisBerikutnya.Trim())
                && barisBerikutnya.Any(char.IsDigit))
            {
                nilai = barisBerikutnya.Trim();
                ambilBarisBerikutnya = true;
            }

            if (nilai.Length < 4 || !nilai.Any(char.IsDigit)) return false;
            if (!PolaNilaiNomor.IsMatch(nilai)) return false;
            if (nilai.Count(c => c == ' ') > 3) return false;

            awalan = AwalanDariNomor(nilai);
            return true;
        }

        /// <summary>Awalan (kode klasifikasi) dari contoh nomor pada berkas.</summary>
        private static string AwalanDariNomor(string nomor)
        {
            string depan = nomor.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            depan = depan.Trim().Trim('.', '-');

            if (depan.Length is >= 1 and <= 16
                && depan.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
            {
                return depan;
            }
            return "470";
        }

        /// <summary>Kenali baris isian \"Label : ......\" sebagai kolom surat.</summary>
        public static bool CobaKolom(string? teks, out string label, out bool wajib)
        {
            label = string.Empty;
            wajib = false;

            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length == 0 || baris.Length > 90) return false;

            var cocok = PolaKolom.Match(baris);
            if (!cocok.Success) return false;

            string kandidatLabel = cocok.Groups["label"].Value.Trim();
            string nilai = cocok.Groups["nilai"].Value.Trim();

            if (kandidatLabel.Length < 2 || kandidatLabel.Length > 45) return false;
            if (!PolaLabelSah.IsMatch(kandidatLabel)) return false;
            if (kandidatLabel.Contains(',')) return false;

            // Label tidak boleh berupa potongan kalimat (\"Dengan ini menerangkan bahwa :\").
            var kata = kandidatLabel
                .Split(new[] { ' ', '\t', '/', '&', '.', '(', ')', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            if (kata.Length == 0 || kata.Length > 4) return false;
            if (kata.Any(k => KataBukanLabel.Contains(k))) return false;

            int titik = nilai.Count(c => c == '.' || c == '_' || c == '…' || c == '–' || c == '—');
            if (nilai.Length > 0 && titik < 2) return false;   // sudah ada isi asli → bukan kolom isian

            label = RapikanTeks(kandidatLabel);
            wajib = titik >= 3;
            return true;
        }

        private static TipeKolomTemplate PetaTipeKolom(string label)
        {
            string slug = TemplateSuratKunci.Slug(label);

            if (slug == "nik" || slug.StartsWith("nik") || slug == "no_ktp" || slug == "no_kk") return TipeKolomTemplate.Nik;
            if (slug.StartsWith("tanggal")) return TipeKolomTemplate.Tanggal;
            if (slug.StartsWith("alamat") || slug is "keperluan" or "keterangan" or "maksud" or "tujuan"
                or "uraian" or "catatan" or "keterangan_lain")
            {
                return TipeKolomTemplate.Paragraf;
            }
            if (slug is "jumlah" or "umur" or "usia" or "luas" or "nomor" or "no") return TipeKolomTemplate.Angka;
            return TipeKolomTemplate.Teks;
        }

        // =====================================================================
        // Bantuan nama
        // =====================================================================

        /// <summary>
        /// Nama template yang belum dipakai: \"Surat Keterangan\" → \"Surat Keterangan 2\"
        /// bila namanya sudah ada pada daftar template pengguna.
        /// </summary>
        public static string NamaUnik(string? dasar, IEnumerable<string>? terpakai)
        {
            string bersih = (dasar ?? string.Empty).Trim();
            if (bersih.Length == 0) bersih = "Template dari berkas Word";
            if (bersih.Length > 120) bersih = bersih.Substring(0, 120).Trim();

            var dipakai = terpakai == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(terpakai
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);

            if (!dipakai.Contains(bersih)) return bersih;

            for (int i = 2; i < 1000; i++)
            {
                string kandidat = $"{bersih} {i}";
                if (!dipakai.Contains(kandidat)) return kandidat;
            }
            return bersih;
        }
    }
}
