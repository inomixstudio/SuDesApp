using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Arsip dokumen APBDes di <c>%LocalAppData%\SuDesApp\apbdes.json</c>, dengan
    /// berkas sumbernya (Word/PDF/Excel dari Siskeudes) disalin ke
    /// <c>%LocalAppData%\SuDesApp\Apbdes\</c>.
    ///
    /// Kenapa arsip dokumen, bukan lagi daftar pos saja: angka APBDes resmi dibuat
    /// dan ditetapkan di Siskeudes. Yang kantor desa butuhkan dari aplikasi ini
    /// adalah menyimpan dokumennya per tahun dan per tahapan (Awal, Perubahan ke-1,
    /// ke-2, …, Pertanggungjawaban), menampilkannya, menyunting apa yang perlu, lalu
    /// menyimpannya kembali — tanpa menghitung ulang keuangan desa.
    ///
    /// Format lama (daftar pos datar) tetap dibaca dan dipindahkan ke satu dokumen
    /// "APBDes Awal" per tahun, sehingga data desa yang sudah ada tidak hilang.
    /// Gagal baca menghasilkan daftar kosong — halaman menampilkan keadaan siap-isi.
    /// </summary>
    public static class ApbdesStore
    {
        /// <summary>Versi format berkas yang ditulis sekarang.</summary>
        public const int VersiBerkas = 2;

        /// <summary>
        /// Jenis dokumen yang menampung angka dari berkas publikasi pemerintah
        /// (Excel/CSV hasil unduhan PELITA/SIKD/PPID/pemda) — tampil sebagai
        /// kolom "publikasi" pada panel riwayat APBDES.
        /// </summary>
        public const string JenisPublikasi = "Data Publikasi Pusat";

        /// <summary>Tahapan dokumen yang dipakai halaman APBDES.</summary>
        public static readonly string[] JenisDaftar =
        {
            "Rancangan APBDes",
            "APBDes Awal",
            "APBDes Perubahan",
            "APBDes Pertanggungjawaban (LPJ)",
            "Dokumen Pelaksanaan (DPA/DPPA/DPAL)",
            "Laporan Realisasi / Semester",
            JenisPublikasi,
            "Lainnya",
        };

        /// <summary>Penunjuk folder untuk pengujian — pola KatalogPaketTersimpan.</summary>
        internal static string? FolderOverride { get; set; }

        private static string Folder => FolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp");

        private static string Berkas => Path.Combine(Folder, "apbdes.json");

        /// <summary>Folder salinan berkas sumber (Word/PDF/Excel) yang diarsipkan.</summary>
        internal static string FolderBerkas => Path.Combine(Folder, "Apbdes");

        /// <summary>Satu baris rincian anggaran pada satu dokumen.</summary>
        public sealed class Pos
        {
            [JsonPropertyName("kode")] public string Kode { get; set; } = "";
            [JsonPropertyName("uraian")] public string Uraian { get; set; } = "";
            [JsonPropertyName("anggaran")] public long Anggaran { get; set; }
            [JsonPropertyName("realisasi")] public long Realisasi { get; set; }
            [JsonPropertyName("keterangan")] public string Keterangan { get; set; } = "";
        }

        /// <summary>Satu dokumen APBDes: identitas surat, berkas sumber, dan rinciannya.</summary>
        public sealed class Dokumen
        {
            [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
            [JsonPropertyName("tahun")] public int Tahun { get; set; } = DateTime.Now.Year;
            [JsonPropertyName("jenis")] public string Jenis { get; set; } = JenisDaftar[1];

            /// <summary>Urutan perubahan (1 = Perubahan ke-1). Hanya dipakai untuk jenis Perubahan.</summary>
            [JsonPropertyName("perubahanKe")] public int PerubahanKe { get; set; }

            [JsonPropertyName("judul")] public string Judul { get; set; } = "";
            [JsonPropertyName("nomor")] public string Nomor { get; set; } = "";

            /// <summary>Tanggal dokumen (yyyy-MM-dd) atau kosong bila belum diisi.</summary>
            [JsonPropertyName("tanggal")] public string Tanggal { get; set; } = "";

            [JsonPropertyName("keterangan")] public string Keterangan { get; set; } = "";

            /// <summary>Nama berkas salinan di folder Apbdes; kosong bila belum ada berkas.</summary>
            [JsonPropertyName("berkas")] public string Berkas { get; set; } = "";

            /// <summary>Nama berkas asli saat diimpor (untuk keterangan pengguna).</summary>
            [JsonPropertyName("berkasAsli")] public string BerkasAsli { get; set; } = "";

            /// <summary>Teks berkas yang diimpor, dipotong; dipakai hanya untuk dipandang.</summary>
            [JsonPropertyName("teks")] public string Teks { get; set; } = "";

            [JsonPropertyName("pos")] public List<Pos> Pos { get; set; } = new();

            /// <summary>Waktu perubahan terakhir (ISO-8601) supaya urutan kerja terbaca.</summary>
            [JsonPropertyName("diubah")] public string Diubah { get; set; } = "";

            /// <summary>Label panjang untuk daftar dokumen: jenis (+ urutan) dan tanggal.</summary>
            [JsonIgnore]
            public string Label
            {
                get
                {
                    string label = LabelJenis(Jenis, PerubahanKe);
                    string tanggal = TanggalTampil;
                    return tanggal.Length == 0 ? label : label + " · " + tanggal;
                }
            }

            [JsonIgnore]
            public string TanggalTampil => DateTime.TryParseExact(
                Tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t.ToString("dd MMM yyyy", new CultureInfo("id-ID"))
                : "";
        }

        /// <summary>Label tahapan yang dibaca pengguna; nomor perubahan ditulis apa adanya.</summary>
        public static string LabelJenis(string? jenis, int perubahanKe)
        {
            string nama = string.IsNullOrWhiteSpace(jenis) ? JenisDaftar[1] : jenis.Trim();
            if (nama.Equals("APBDes Perubahan", StringComparison.OrdinalIgnoreCase))
            {
                return perubahanKe > 0 ? $"APBDes Perubahan ke-{perubahanKe}" : "APBDes Perubahan";
            }

            return nama;
        }

        // =====================================================================
        // Baca & simpan
        // =====================================================================

        private sealed class Wadah
        {
            [JsonPropertyName("versi")] public int Versi { get; set; } = VersiBerkas;
            [JsonPropertyName("dokumen")] public List<Dokumen> Dokumen { get; set; } = new();
        }

        /// <summary>Bentuk berkas lama: daftar pos datar (satu PosItem per baris).</summary>
        private sealed class PosLama
        {
            [JsonPropertyName("tahun")] public int Tahun { get; set; }
            [JsonPropertyName("jenis")] public string Jenis { get; set; } = "Belanja";
            [JsonPropertyName("uraian")] public string Uraian { get; set; } = "";
            [JsonPropertyName("anggaran")] public long Anggaran { get; set; }
            [JsonPropertyName("realisasi")] public long Realisasi { get; set; }
        }

        /// <summary>Muat seluruh dokumen arsip; berkas rusak/kosong menghasilkan daftar kosong.</summary>
        public static List<Dokumen> Muat()
        {
            string isi;
            try
            {
                if (!File.Exists(Berkas)) return new List<Dokumen>();
                isi = File.ReadAllText(Berkas);
            }
            catch (Exception)
            {
                return new List<Dokumen>();
            }

            if (string.IsNullOrWhiteSpace(isi)) return new List<Dokumen>();

            try
            {
                using var dok = JsonDocument.Parse(isi);

                // Format lama: array pos datar → dipindahkan ke satu dokumen per tahun.
                if (dok.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var lama = JsonSerializer.Deserialize<List<PosLama>>(isi) ?? new List<PosLama>();
                    return PindahkanFormatLama(lama);
                }

                var wadah = JsonSerializer.Deserialize<Wadah>(isi);
                return wadah?.Dokumen ?? new List<Dokumen>();
            }
            catch (JsonException)
            {
                return new List<Dokumen>();
            }
        }

        /// <summary>Simpan seluruh dokumen; false bila gagal menulis (biar halaman memberi pesan).</summary>
        public static bool Simpan(IEnumerable<Dokumen> dokumen)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var wadah = new Wadah
                {
                    Versi = VersiBerkas,
                    Dokumen = dokumen.OrderBy(d => d.Tahun).ThenBy(d => d.Jenis)
                        .ThenBy(d => d.PerubahanKe).ToList()
                };

                File.WriteAllText(Berkas, JsonSerializer.Serialize(
                    wadah, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Pos tahun lama dijadikan dokumen "APBDes Awal" per tahun, dengan catatan
        /// asalnya supaya pengguna tahu dokumen itu bukan salinan Siskeudes.
        /// </summary>
        private static List<Dokumen> PindahkanFormatLama(List<PosLama> lama)
        {
            return lama
                .GroupBy(p => p.Tahun)
                .OrderBy(g => g.Key)
                .Select(g => new Dokumen
                {
                    Tahun = g.Key,
                    Jenis = "APBDes Awal",
                    Judul = "APBDes " + g.Key.ToString(CultureInfo.InvariantCulture),
                    Keterangan =
                        "Dipindahkan dari catatan pos versi lama aplikasi (bukan salinan berkas Siskeudes). " +
                        "Angka bisa diperiksa dan disesuaikan dengan dokumen resmi desa.",
                    Diubah = DateTime.Now.ToString("s"),
                    Pos = g.Select(p => new Pos
                    {
                        Kode = KodeDariJenis(p.Jenis),
                        Uraian = p.Uraian,
                        Anggaran = p.Anggaran,
                        Realisasi = p.Realisasi,
                        Keterangan = "Jenis lama: " + p.Jenis
                    }).ToList()
                })
                .ToList();
        }

        /// <summary>Kode rekening sederhana untuk data lama yang hanya punya jenis.</summary>
        private static string KodeDariJenis(string? jenis) => jenis switch
        {
            "Pendapatan" => "4",
            "Pembiayaan" => "6",
            _ => "5"
        };

        // =====================================================================
        // Berkas sumber
        // =====================================================================

        /// <summary>Salin berkas sumber ke arsip; mengembalikan nama berkas salinannya.</summary>
        internal static string SimpanBerkas(string sumber)
        {
            if (string.IsNullOrWhiteSpace(sumber) || !File.Exists(sumber))
            {
                throw new FileNotFoundException("Berkas sumber tidak ditemukan.", sumber);
            }

            Directory.CreateDirectory(FolderBerkas);

            string namaAsli = Path.GetFileName(sumber);
            string bersih = BersihkanNama(namaAsli);
            string nama = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + bersih;
            string tujuan = Path.Combine(FolderBerkas, nama);

            // Nama tidak boleh menimpa berkas lain (dua impor pada detik yang sama).
            int urutan = 2;
            while (File.Exists(tujuan))
            {
                nama = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + urutan + "-" + bersih;
                tujuan = Path.Combine(FolderBerkas, nama);
                urutan++;
            }

            File.Copy(sumber, tujuan);
            return nama;
        }

        internal static bool HapusBerkas(string? nama)
        {
            try
            {
                var jalur = JalurBerkas(nama);
                if (jalur == null) return true;
                if (File.Exists(jalur)) File.Delete(jalur);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Jalur lengkap berkas arsip; null bila nama kosong atau menunjuk ke luar folder.</summary>
        internal static string? JalurBerkas(string? nama)
        {
            if (string.IsNullOrWhiteSpace(nama)) return null;
            if (nama.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (nama.Contains("..", StringComparison.Ordinal) ||
                nama.Contains(Path.DirectorySeparatorChar) || nama.Contains(Path.AltDirectorySeparatorChar))
            {
                return null;
            }

            return Path.Combine(FolderBerkas, nama);
        }

        /// <summary>Nama berkas yang aman di disk, tetap mudah dikenali pengguna.</summary>
        private static string BersihkanNama(string nama)
        {
            var sb = new System.Text.StringBuilder(nama.Length);
            foreach (char c in nama)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }

            string hasil = sb.ToString().Trim();
            if (hasil.Length == 0) hasil = "berkas";
            return hasil.Length <= 120 ? hasil : hasil[..120];
        }
    }
}
