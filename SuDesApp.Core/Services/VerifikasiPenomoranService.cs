using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>
    /// Keadaan satu deret nomor (satu kode klasifikasi) dalam satu tahun,
    /// misalnya seluruh surat <c>470/…/Ds/2026</c>.
    /// </summary>
    public class DeretNomor
    {
        public required string Awalan { get; init; }
        public required int Tahun { get; init; }

        public int Jumlah { get; init; }

        /// <summary>Nomor urut terkecil dan terbesar yang ditemukan.</summary>
        public int? NomorPertama { get; init; }
        public int? NomorTerakhir { get; init; }

        /// <summary>Nomor urut yang tidak dipakai di tengah deret.</summary>
        public IReadOnlyList<int> NomorHilang { get; init; } = Array.Empty<int>();

        /// <summary>Nomor yang muncul lebih dari sekali pada deret ini.</summary>
        public IReadOnlyList<int> NomorGanda { get; init; } = Array.Empty<int>();

        /// <summary>Nomor surat yang tidak bisa diurai (tanpa segmen, dsb).</summary>
        public IReadOnlyList<string> NomorTidakTerbaca { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> NamaJenis { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Deret yang benar dimulai dari 1. Deret kosong dianggap benar
        /// karena tidak ada yang perlu diperiksa.
        /// </summary>
        public bool MulaiDariSatu => Jumlah == 0 || NomorPertama == 1;

        public bool AdaNomorHilang => NomorHilang.Count > 0;
        public bool AdaNomorGanda => NomorGanda.Count > 0;

        /// <summary>Nomor ganda dan nomor tak terbaca menutup buku; nomor hilang tidak.</summary>
        public bool AdaMasalahBlocking => AdaNomorGanda || NomorTidakTerbaca.Count > 0;

        public string Ringkasan
        {
            get
            {
                if (Jumlah == 0) return $"{Awalan}/{Tahun}: belum ada surat";

                var bagian = new List<string>
                {
                    $"{Awalan}/{Tahun}: {Jumlah} surat, nomor {NomorPertama:D3}-{NomorTerakhir:D3}"
                };

                if (!MulaiDariSatu)
                    bagian.Add($"dimulai dari {NomorPertama:D3}, bukan 001");

                if (AdaNomorHilang)
                    bagian.Add($"{NomorHilang.Count} nomor hilang");

                if (AdaNomorGanda)
                    bagian.Add($"{NomorGanda.Count} nomor ganda");

                if (NomorTidakTerbaca.Count > 0)
                    bagian.Add($"{NomorTidakTerbaca.Count} nomor tak terbaca");

                return string.Join(", ", bagian);
            }
        }
    }

    /// <summary>Hasil pemeriksaan penomoran untuk satu tahun buku.</summary>
    public class HasilVerifikasiNomor
    {
        public required int Tahun { get; init; }
        public required IReadOnlyList<DeretNomor> Deret { get; init; }

        /// <summary>
        /// Nomor yang tahun di nomornya berbeda dari tahun tanggal penerbitan,
        /// misalnya <c>470/015/Ds/2025</c> bertanggal 17-01-2026.
        /// </summary>
        public required IReadOnlyList<string> NomorTahunTidakCocok { get; init; }

        public required IReadOnlyList<int> TahunTersedia { get; init; }

        /// <summary>
        /// Jumlah surat yang benar-benar milik deret tahun ini. Baris yang
        /// ditolak karena tahunnya tidak cocok tidak ikut dihitung.
        /// </summary>
        public int JumlahSeluruhSurat => Deret.Sum(d => d.Jumlah);

        public bool AdaDeretTidakMulaiDariSatu => Deret.Any(d => !d.MulaiDariSatu);
        public bool AdaNomorGanda => Deret.Any(d => d.AdaNomorGanda);
        public bool AdaNomorTidakTerbaca => Deret.Any(d => d.NomorTidakTerbaca.Count > 0);
        public bool AdaTahunTidakCocok => NomorTahunTidakCocok.Count > 0;

        /// <summary>
        /// Masalah yang menutup buku: nomor ganda, nomor tak terbaca, dan
        /// nomor yang_assertionnya berbeda dari tanggal surat. Nomor yang hilang
        /// hanya peringatan — bisa jadi nomor dibatalkan atau memang tidak
        /// pernah dipakai, dan itu tidak boleh memblokir desa.
        /// </summary>
        public bool AdaMasalahBlocking =>
            AdaNomorGanda || AdaNomorTidakTerbaca || AdaTahunTidakCocok;

        public string Ringkasan
        {
            get
            {
                var bagian = new List<string>
                {
                    $"{Tahun}: {JumlahSeluruhSurat} surat, {Deret.Count} deret"
                };

                if (Deret.Count == 0) return bagian[0] + " (belum ada surat)";
                if (AdaMasalahBlocking)
                    bagian.Add("ada masalah yang menutup buku");
                else if (AdaDeretTidakMulaiDariSatu || Deret.Any(d => d.AdaNomorHilang))
                    bagian.Add("hanya ada peringatan");
                else
                    bagian.Add("penomoran rapi");

                return string.Join(", ", bagian);
            }
        }

        /// <summary>
        /// Peringatan yang tidak menutup buku: nomor hilang dan deret yang tidak
        /// mulai dari 001. Ini informasi, bukan penyimpangan. Numeral yang
        /// banyak diringkas supaya catatan arsip tetap enak dibaca.
        /// </summary>
        public IReadOnlyList<string> Peringatan()
        {
            var peringatan = new List<string>();

            foreach (var d in Deret)
            {
                if (!d.MulaiDariSatu)
                    peringatan.Add($"{d.Awalan}/{d.Tahun} dimulai dari {d.NomorPertama:D3}, bukan 001.");

                if (d.AdaNomorHilang)
                    peringatan.Add($"{d.Awalan}/{d.Tahun} punya {d.NomorHilang.Count} nomor hilang: {FormatRentang(d.NomorHilang)}.");
            }

            return peringatan;
        }

        private static string FormatRentang(IReadOnlyList<int> nomor)
        {
            if (nomor.Count == 0) return "-";

            string TigaDigit(int n) => n.ToString("000", CultureInfo.InvariantCulture);
            if (nomor.Count <= 5) return string.Join(", ", nomor.Select(TigaDigit));
            return string.Join(", ", nomor.Take(3).Select(TigaDigit)) + $", … (+{nomor.Count - 3})";
        }
    }

    public interface IVerifikasiPenomoranService
    {
        /// <summary>Tahun yang punya surat di register, terbaru lebih dulu.</summary>
        Task<List<int>> AmbilTahunTersediaAsync();

        /// <summary>
        /// Periksa keutuhan penomoran satu tahun: setiap deret harus mulai dari
        /// 001, tidak ada nomor ganda atau hilang, dan tahun pada nomor harus
        /// sama dengan tahun tanggal penerbitan. Tidak mengubah apa pun.
        /// </summary>
        Task<HasilVerifikasiNomor> PeriksaAsync(int tahun);
    }

    /// <summary>
    /// Pemeriksaan penomoran untuk tutup buku tahunan.
    ///
    /// Penomoran sudah ter-scope per tahun sejak awal: penerbitan selalu
    /// memfilter <c>NomorSurat LIKE '%/Ds/' || tahun</c>, sehingga deret baru
    /// mulai lagi dari 001 setiap ganti tahun. Service ini membuktikannya dari
    /// register yang ada, bukan dari asumsi — termasuk saat operator memakai
    /// format kustom lewat Pengaturan Aplikasi.
    /// </summary>
    public class VerifikasiPenomoranService : IVerifikasiPenomoranService
    {
        private readonly IJenisSuratRepository _jenis;
        private readonly ILogger _logger;

        public VerifikasiPenomoranService(
            IJenisSuratRepository jenis,
            ILogger<VerifikasiPenomoranService>? logger = null)
        {
            _jenis = jenis ?? throw new ArgumentNullException(nameof(jenis));
            _logger = logger ?? (ILogger)NullLogger<VerifikasiPenomoranService>.Instance;
        }

        public async Task<List<int>> AmbilTahunTersediaAsync()
        {
            var tahun = await _jenis.GetAvailableYearsAsync().ConfigureAwait(false);
            var hasil = new List<int>();

            foreach (string t in tahun)
            {
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nilai)
                    && nilai is > 1900 and < 3000)
                {
                    hasil.Add(nilai);
                }
            }

            hasil.Sort((a, b) => b.CompareTo(a));
            return hasil;
        }

        public async Task<HasilVerifikasiNomor> PeriksaAsync(int tahun)
        {
            if (tahun is < 1900 or > 3000)
                throw new ArgumentOutOfRangeException(nameof(tahun), tahun, "Tahun tidak wajar.");

            var semua = await _jenis.GetNomorTerbitAsync(tahun).ConfigureAwait(false);
            var tahunTersedia = await AmbilTahunTersediaAsync().ConfigureAwait(false);

            return Analisis(tahun, semua, tahunTersedia);
        }

        /// <summary>
        /// Analisis murni: dipisah dari akses database supaya aturan
        /// penomoran (dimulai dari 001, nomor hilang, tahun tidak cocok) bisa
        /// diuji dengan data buatan tanpa menyiapkan SQLite.
        /// </summary>
        internal static HasilVerifikasiNomor Analisis(
            int tahun,
            IReadOnlyList<BarisNomorTerbit> baris,
            IReadOnlyList<int> tahunTersedia)
        {
            var deret = new Dictionary<string, DeretAccumulator>(StringComparer.Ordinal);
            var tidakCocok = new List<string>();

            foreach (var b in baris)
            {
                string nomor = (b.NomorSurat ?? string.Empty).Trim();
                var urai = UraiNomor(nomor);

                if (urai == null)
                {
                    string awalan = AmbilSegmenPertama(nomor);
                    var kunci = string.IsNullOrEmpty(awalan) ? "(tanpa awalan)" : awalan;
                    if (!deret.TryGetValue(kunci, out var takTerbaca))
                    {
                        takTerbaca = new DeretAccumulator(kunci, tahun);
                        deret[kunci] = takTerbaca;
                    }

                    takTerbaca.TidakTerbaca.Add(nomor.Length == 0 ? "(kosong)" : nomor);
                    takTerbaca.NamaJenis.Add(b.NamaJenis ?? "-");
                    continue;
                }

                // Tahun pada nomor harus sama dengan tahun tanggal penerbitan.
                // Baris yang tidak cocok dicatat sebagai penyimpangan tapi TIDAK
                // dihitung dalam deret: nomor tahun lain bukan bagian dari
                // deret tahun ini, menghitungnya membuat jumlah dan rentang
                // nomor jadi bohong.
                int? tahunTanggal = AmbilTahunTanggal(b.TanggalSurat);
                bool tahunCocok = true;

                if (tahunTanggal.HasValue && tahunTanggal.Value != tahun)
                {
                    tidakCocok.Add($"{nomor} (tanggal surat {b.TanggalSurat})");
                    tahunCocok = false;
                }
                else if (urai.Tahun.HasValue && urai.Tahun.Value != tahun)
                {
                    tidakCocok.Add($"{nomor} (tahun pada nomor {urai.Tahun.Value})");
                    tahunCocok = false;
                }

                if (!tahunCocok) continue;

                if (!deret.TryGetValue(urai.Awalan, out var akum))
                {
                    akum = new DeretAccumulator(urai.Awalan, tahun);
                    deret[urai.Awalan] = akum;
                }

                akum.Nomor.Add(urai.Nomor!.Value);
                akum.NamaJenis.Add(b.NamaJenis ?? "-");
            }

            var hasilDeret = deret.Values
                .Select(a => a.Susun())
                .OrderBy(d => d.Awalan, StringComparer.Ordinal)
                .ToList();

            return new HasilVerifikasiNomor
            {
                Tahun = tahun,
                Deret = hasilDeret,
                NomorTahunTidakCocok = tidakCocok,
                TahunTersedia = tahunTersedia
            };
        }

        /// <summary>
        /// Urai nomor surat <c>AWALAN/URUT/Ds/TAHUN</c>. Segmen tengah boleh
        /// berisi angka saja; bila tidak, nomor dianggap tak terbaca supaya
        /// operator memperbaikinya, bukan dihitung diam-diam.
        /// </summary>
        internal static UraiNomorSurat? UraiNomor(string? nomor)
        {
            string n = (nomor ?? string.Empty).Trim();
            if (n.Length == 0) return null;

            string[] segmen = n.Split('/', StringSplitOptions.TrimEntries);
            if (segmen.Length < 3) return null;

            string awalan = segmen[0];
            if (awalan.Length == 0) return null;

            if (!int.TryParse(segmen[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nomorUrut))
                return null;

            int? tahun = null;
            string segmenTahun = segmen[^1];
            if (segmenTahun.Length == 4
                && int.TryParse(segmenTahun, NumberStyles.Integer, CultureInfo.InvariantCulture, out int th))
            {
                tahun = th;
            }

            return new UraiNomorSurat(awalan, nomorUrut, tahun);
        }

        private static string AmbilSegmenPertama(string nomor)
        {
            string n = (nomor ?? string.Empty).Trim();
            int pos = n.IndexOf('/');
            return pos > 0 ? n[..pos].Trim() : n;
        }

        private static int? AmbilTahunTanggal(string? tanggal)
        {
            string t = (tanggal ?? string.Empty).Trim();
            if (t.Length < 4) return null;

            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return parsed.Year;

            // Tanggal yang tersimpan sebagai teks non-ISO, mis. "17-08-2026".
            if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("id-ID"), DateTimeStyles.None, out DateTime parsed2))
                return parsed2.Year;

            return null;
        }
    }

    /// <summary>Hasil uraian satu nomor surat.</summary>
    public class UraiNomorSurat
    {
        public UraiNomorSurat(string awalan, int nomor, int? tahun)
        {
            Awalan = awalan;
            Nomor = nomor;
            Tahun = tahun;
        }

        public string Awalan { get; }
        public int? Nomor { get; }
        public int? Tahun { get; }
    }

    /// <summary>Pengumpul angka urut satu deret selama analisis berjalan.</summary>
    internal sealed class DeretAccumulator
    {
        public DeretAccumulator(string awalan, int tahun)
        {
            Awalan = awalan;
            Tahun = tahun;
        }

        public string Awalan { get; }
        public int Tahun { get; }
        public List<int> Nomor { get; } = new();
        public List<string> TidakTerbaca { get; } = new();
        public HashSet<string> NamaJenis { get; } = new(StringComparer.Ordinal);

        public DeretNomor Susun()
        {
            int[] semua = Nomor.ToArray();
            int[] urut = semua.Distinct().OrderBy(n => n).ToArray();

            var hilang = new List<int>();
            for (int n = 1; n < (urut.Length > 0 ? urut[^1] : 1); n++)
            {
                if (!urut.Contains(n)) hilang.Add(n);
            }

            // Nomor yang sama dipakai lebih dari sekali tidak mungkin terjadi
            // karena kolom NomorSurat UNIQUE, tapi tetap diperiksa: kalau
            // muncul, berarti nomor itu ditambahkan lewat jalur lain.
            int[] ganda = semua
                .GroupBy(n => n)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(n => n)
                .ToArray();

            return new DeretNomor
            {
                Awalan = Awalan,
                Tahun = Tahun,
                Jumlah = semua.Length,
                NomorPertama = urut.Length > 0 ? urut[0] : null,
                NomorTerakhir = urut.Length > 0 ? urut[^1] : null,
                NomorHilang = hilang,
                NomorGanda = ganda,
                NomorTidakTerbaca = TidakTerbaca.ToList(),
                NamaJenis = NamaJenis.OrderBy(n => n, StringComparer.Ordinal).ToList()
            };
        }
    }
}
