using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>Yang akan dilakukan terhadap satu baris berkas impor.</summary>
    public enum TindakanImpor
    {
        /// <summary>Baris bermasalah atau NIK sudah ada — tidak akan disimpan.</summary>
        Lewati,

        /// <summary>Warga baru, akan ditambahkan.</summary>
        Simpan,

        /// <summary>NIK sudah ada di database dan mode perbaruan aktif.</summary>
        Perbarui
    }

    /// <summary>
    /// Satu baris berkas impor beserta hasil pemeriksaannya. Dipakai juga untuk
    /// pratinjau di layar: <see cref="Kesalahan"/> yang terisi berarti baris
    /// tersebut tidak akan tersimpan.
    /// </summary>
    public class BarisImporWarga
    {
        /// <summary>Nomor baris di berkas; baris 1 adalah judul kolom.</summary>
        public int NomorBaris { get; init; }

        public string? NIK { get; init; }
        public string? Nama { get; init; }

        public TindakanImpor Tindakan { get; set; } = TindakanImpor.Lewati;

        /// <summary>Alasan baris ditolak. Kosong berarti baris layak simpan.</summary>
        public List<string> Kesalahan { get; } = new();

        public bool AdaKesalahan => Kesalahan.Count > 0;

        /// <summary>
        /// Alasan yang siap ditampilkan di kolom "Alasan ditolak". Setiap
        /// masalah ditulis di baris sendiri supaya mudah dibaca di kolom sempit,
        /// dan baris yang tidak bermasalah cukup menampilkan "-".
        /// </summary>
        public string AlasanTampil => Kesalahan.Count == 0
            ? "-"
            : string.Join(Environment.NewLine, Kesalahan);

        /// <summary>Data siap simpan; null bila baris ditolak.</summary>
        public WargaData? Data { get; set; }

        public string KeteranganTindakan => Tindakan switch
        {
            TindakanImpor.Simpan => "Akan ditambahkan",
            TindakanImpor.Perbarui => "Akan diperbarui",
            _ => "Dilewati"
        };
    }

    /// <summary>Ringkasan hasil pemeriksaan satu berkas impor.</summary>
    public class HasilImporWarga
    {
        public required string NamaBerkas { get; init; }
        public required IReadOnlyList<BarisImporWarga> Baris { get; init; }

        /// <summary>Judul kolom yang tidak dikenali; diabaikan, bukan error.</summary>
        public required IReadOnlyList<string> KolomTidakDikenali { get; init; }

        /// <summary>Masalah pada berkasnya sendiri, bukan pada baris.</summary>
        public string? KesalahanBerkas { get; init; }

        public bool AdaKesalahanBerkas => !string.IsNullOrWhiteSpace(KesalahanBerkas);

        public int JumlahBaris => Baris.Count;
        public int JumlahGagal => Baris.Count(b => b.AdaKesalahan);
        public int JumlahDuplikatDiBerkas => Baris.Count(b =>
            b.Kesalahan.Any(k => k.Contains("duplikat di berkas", StringComparison.OrdinalIgnoreCase)));
        public int JumlahDuplikatDiDatabase => Baris.Count(b =>
            b.Kesalahan.Any(k => k.Contains("sudah terdaftar", StringComparison.OrdinalIgnoreCase)));
        public int JumlahAkanDisimpan => Baris.Count(b => b.Tindakan == TindakanImpor.Simpan);
        public int JumlahAkanDiperbarui => Baris.Count(b => b.Tindakan == TindakanImpor.Perbarui);
        public int JumlahBisaDisimpan => JumlahAkanDisimpan + JumlahAkanDiperbarui;

        public string Ringkasan =>
            $"{JumlahBaris} baris terbaca: {JumlahBisaDisimpan} bisa disimpan " +
            $"({JumlahAkanDisimpan} baru, {JumlahAkanDiperbarui} pembaruan), {JumlahGagal} ditolak";
    }

    public interface IImporWargaService
    {
        /// <summary>
        /// Baca dan periksa berkas tanpa menyimpan apa pun. Bisa ditampilkan ke
        /// pengguna lebih dulu, lengkap dengan alasan setiap baris ditolak.
        /// </summary>
        Task<HasilImporWarga> ValidasiAsync(
            string pathBerkas,
            bool perbaruiYangSudahAda = false,
            CancellationToken ct = default);

        /// <summary>Simpan baris yang lolos pemeriksaan.</summary>
        Task<int> JalankanAsync(
            HasilImporWarga hasil,
            IProgress<int>? progres = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Impor warga massal dari .xlsx atau .csv.
    ///
    /// Berkas hasil ekspor warga bisa langsung diimpor ulang: judul kolom ekspor
    /// dikenali, urutan kolom bebas, dan kolom tambahan diabaikan. Baris
    /// bermasalah tidak pernah disimpan — pengguna mendapat daftar alasan per
    /// baris supaya berkas diperbaiki, bukan hasil impor ditebak-tebak.
    /// </summary>
    public class ImporWargaService : IImporWargaService
    {
        private readonly IWargaRepository _warga;
        private readonly ILogger _logger;

        /// <summary>
        /// NIK yang dipakai baris internal (instansi dan kematian), bukan warga
        /// sungguhan. Semua import memakai NIK lain, sehingga dua nilai ini
        /// harus ditolak agar tidak menimpa baris yang sudah ada.
        /// </summary>
        private static readonly string[] NikCadangan =
        {
            "9999999999999999", "0000000000000000"
        };

        public ImporWargaService(IWargaRepository warga, ILogger<ImporWargaService>? logger = null)
        {
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _logger = logger ?? (ILogger)NullLogger<ImporWargaService>.Instance;
        }

        // ------------------------------------------------------------------
        // Tahap 1: baca berkas
        // ------------------------------------------------------------------

        public async Task<HasilImporWarga> ValidasiAsync(
            string pathBerkas,
            bool perbaruiYangSudahAda = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(pathBerkas))
                throw new ArgumentException("Path berkas tidak boleh kosong.", nameof(pathBerkas));

            if (!File.Exists(pathBerkas))
                throw new FileNotFoundException("Berkas impor tidak ditemukan.", pathBerkas);

            string nama = Path.GetFileName(pathBerkas);
            string ekstensi = Path.GetExtension(pathBerkas).ToLowerInvariant();

            List<string[]> tabel;
            try
            {
                tabel = ekstensi switch
                {
                    ".xlsx" => BacaXlsx(pathBerkas),
                    ".csv" => BacaCsv(pathBerkas),
                    ".xls" => throw new NotSupportedException(
                        "Format .xls lama belum didukung. Buka di Excel lalu simpan ulang sebagai .xlsx."),
                    _ => throw new NotSupportedException(
                        $"Format berkas '{ekstensi}' belum didukung. Gunakan .xlsx atau .csv.")
                };
            }
            // Batas file: semua kesalahan di sini adalah berkas rusak atau
            // format yang tidak didukung, bukan bug aplikasi. Cancellation
            // tetap diteruskan agar tombol Batal bekerja.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Gagal membaca berkas impor {Berkas}", nama);
                return Gagal(nama, ex.Message);
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            return await PeriksaAsync(nama, tabel, perbaruiYangSudahAda, ct).ConfigureAwait(false);
        }

        private static HasilImporWarga Gagal(string nama, string pesan) => new()
        {
            NamaBerkas = nama,
            Baris = Array.Empty<BarisImporWarga>(),
            KolomTidakDikenali = Array.Empty<string>(),
            KesalahanBerkas = pesan
        };

        private static List<string[]> BacaXlsx(string path)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            using var package = new ExcelPackage(new FileInfo(path));
            if (package.Workbook.Worksheets.Count == 0)
                throw new InvalidDataException("Berkas tidak memiliki sheet apa pun.");

            ExcelWorksheet ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null || ws.Dimension.End.Row < 1)
                throw new InvalidDataException("Sheet pertama kosong.");

            int barisAkhir = ws.Dimension.End.Row;
            int kolomAkhir = ws.Dimension.End.Column;
            var hasil = new List<string[]>(barisAkhir);

            for (int r = 1; r <= barisAkhir; r++)
            {
                var baris = new string[kolomAkhir];
                for (int c = 1; c <= kolomAkhir; c++)
                    baris[c - 1] = TeksSel(ws.Cells[r, c]);
                hasil.Add(baris);
            }

            return hasil;
        }

        /// <summary>
        /// Sel .xlsx bisa berisi teks, angka, atau tanggal. Tanggal diambil
        /// sebagai ISO supaya usia dan rekapitulasi penduduk tidak salah hitung.
        ///
        /// Excel menyimpan tanggal sebagai nomor seri dan format selnya sering
        /// tetap "General", jadi angka dalam rentang tahun yang masuk akal
        /// (1913-2064) ikut dibaca sebagai tanggal. Rentang itu dipilih supaya
        /// kolom numerik lain tidak berubah arti: RT/RW (1-99) serta NIK, No KK,
        /// dan nomor HP yang jauh lebih besar tidak pernah dianggap tanggal.
        /// </summary>
        private static string TeksSel(ExcelRange? sel)
        {
            if (sel?.Value == null) return string.Empty;

            if (sel.Value is DateTime dt)
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (sel.Value is double angka && BisaSerialTanggal(angka, sel.Style.Numberformat.Format))
            {
                try
                {
                    return DateTime.FromOADate(angka)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                catch (ArgumentException)
                {
                    // Di luar rentang tanggal OADate; biarkan sebagai teks.
                }
            }

            return Convert.ToString(sel.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private const double SerialTanggalMin = 5000;   // 1913-05-18
        private const double SerialTanggalMaks = 60000;  // 2064-03-15

        private static bool BisaSerialTanggal(double angka, string? format)
        {
            if (double.IsNaN(angka) || double.IsInfinity(angka)) return false;
            if (angka is >= SerialTanggalMin and <= SerialTanggalMaks) return true;

            // Seri di luar rentang itu tetap diterima bila format selnya jelas
            // tanggal, misalnya angka tahun 1900-an yang ditulis manual.
            if (string.IsNullOrEmpty(format)) return false;

            string f = format.ToLowerInvariant();
            return f.Contains('y') || f.Contains('d') || f.Contains('h');
        }

        private static List<string[]> BacaCsv(string path)
        {
            // Excel di Windows sering menulis CSV dengan pemisah ';' dan awalan BOM.
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string isi = reader.ReadToEnd();

            var hasil = PemisahBaris(isi, TebakPemisah(isi));
            if (hasil.Count == 0) throw new InvalidDataException("Berkas CSV kosong.");
            return hasil;
        }

        /// <summary>
        /// Excel Indonesia memakai ';', daftar luar negeri biasanya ','. Kalau
        /// keduanya muncul, pilih yang paling banyak — kata Indonesia lebih
        /// sering mengandung koma daripada titik koma.
        /// </summary>
        private static char TebakPemisah(string isi)
        {
            int batas = Math.Min(isi.Length, 8192);
            int koma = 0, titikKoma = 0;
            for (int i = 0; i < batas; i++)
            {
                if (isi[i] == ',') koma++;
                else if (isi[i] == ';') titikKoma++;
            }

            return titikKoma > koma ? ';' : ',';
        }

        /// <summary>
        /// Pemecah CSV sesuai RFC 4180: field berpapit boleh memuat pemisah,
        /// tanda kutip ganda ("\" di dalam "" berarti satu tanda kutip), dan
        /// baris baru. Ditulis manual karena proyek tidak memakai pustaka CSV.
        /// </summary>
        internal static List<string[]> PemisahBaris(string isi, char pemisah)
        {
            var hasil = new List<string[]>();
            var field = new StringBuilder();
            var baris = new List<string>();
            bool dalamKutip = false;

            void AkhiriBaris()
            {
                baris.Add(field.ToString());
                field.Clear();
                if (baris.Any(v => v.Trim().Length > 0))
                    hasil.Add(baris.ToArray());
                baris.Clear();
            }

            for (int i = 0; i < isi.Length; i++)
            {
                char ch = isi[i];

                if (dalamKutip)
                {
                    if (ch != '"') { field.Append(ch); continue; }

                    if (i + 1 < isi.Length && isi[i + 1] == '"') { field.Append('"'); i++; }
                    else dalamKutip = false;
                    continue;
                }

                if (ch == '"') { dalamKutip = true; continue; }
                if (ch == pemisah) { baris.Add(field.ToString()); field.Clear(); continue; }
                if (ch == '\r') continue;
                if (ch == '\n') { AkhiriBaris(); continue; }

                field.Append(ch);
            }

            if (field.Length > 0 || baris.Count > 0) AkhiriBaris();

            return hasil;
        }

        // ------------------------------------------------------------------
        // Tahap 2: petakan judul kolom
        // ------------------------------------------------------------------

        /// <summary>
        /// Judul kolom yang dikenali. Key = judul bersih (huruf kecil, tanpa
        /// spasi dan tanda baca), value = nama field. Mencakup seluruh judul
        /// kolom ekspor warga dan berbagai sinonimnya.
        /// </summary>
        private static readonly Dictionary<string, string> PetakanKolom = new(StringComparer.Ordinal)
        {
            ["nik"] = "NIK",
            ["nama"] = "Nama",
            ["namalengkap"] = "Nama",
            ["namawarga"] = "Nama",
            ["nokartukeluarga"] = "NoKK",
            ["nomorkartukeluarga"] = "NoKK",
            ["nokk"] = "NoKK",
            ["nok"] = "NoKK",
            ["jeniskelamin"] = "JenisKelamin",
            ["jk"] = "JenisKelamin",
            ["gender"] = "JenisKelamin",
            ["tempatlahir"] = "TempatLahir",
            ["tempat"] = "TempatLahir",
            ["tanggallahir"] = "TanggalLahir",
            ["tgllahir"] = "TanggalLahir",
            ["tglahir"] = "TanggalLahir",
            ["agama"] = "Agama",
            ["golongandarah"] = "GolonganDarah",
            ["goldah"] = "GolonganDarah",
            ["statusperkawinan"] = "StatusPerkawinan",
            ["statuskawin"] = "StatusPerkawinan",
            ["pekerjaan"] = "Pekerjaan",
            ["pendidikan"] = "Pendidikan",
            ["namaayah"] = "NamaAyah",
            ["namaibu"] = "NamaIbu",
            ["nomorhp"] = "NomorHP",
            ["notelepon"] = "NomorHP",
            ["telepon"] = "NomorHP",
            ["hp"] = "NomorHP",
            ["rt"] = "RT",
            ["rw"] = "RW",
            ["dusun"] = "Dusun",
            ["alamatdetail"] = "AlamatDetail",
            ["alamat"] = "AlamatDetail",
            ["alamatjalankampung"] = "AlamatDetail",
            ["alamatjalan"] = "AlamatDetail",
            ["desa"] = "Desa",
            ["kecamatan"] = "Kecamatan",
            ["kabupaten"] = "Kabupaten",
            ["kewarganegaraan"] = "Kewarganegaraan",
            ["statuswarga"] = "StatusWarga",
            ["status"] = "StatusWarga",
            ["tanggalstatus"] = "TanggalStatus",
            ["keterangan"] = "KeteranganWarga",
            ["keteranganwarga"] = "KeteranganWarga"
        };

        /// <summary>Kolom hasil ekspor yang memang tidak perlu diimpor.</summary>
        private static readonly HashSet<string> KolomDiabaikan = new(StringComparer.Ordinal)
        {
            "no", "nomor", "nourut", "jumlahsurat", "idad", "iddatabase"
        };

        /// <summary>Buang spasi, tanda baca, dan huruf besar dari judul kolom.</summary>
        internal static string BersihkanJudul(string judul)
        {
            var sb = new StringBuilder(judul.Length);
            foreach (char ch in judul)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Tahap 3: periksa tiap baris
        // ------------------------------------------------------------------

        private async Task<HasilImporWarga> PeriksaAsync(
            string namaBerkas,
            List<string[]> tabel,
            bool perbaruiYangSudahAda,
            CancellationToken ct)
        {
            if (tabel.Count == 0) return Gagal(namaBerkas, "Berkas tidak berisi data.");

            var indeksKolom = new Dictionary<string, int>(StringComparer.Ordinal);
            var tidakDikenali = new List<string>();
            var sudahDilog = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] judul = tabel[0];
            for (int c = 0; c < judul.Length; c++)
            {
                string kunci = BersihkanJudul(judul[c] ?? string.Empty);
                if (kunci.Length == 0 || KolomDiabaikan.Contains(kunci)) continue;

                if (PetakanKolom.TryGetValue(kunci, out string? field))
                {
                    // Kolom pertama yang dikenali menang; judul kembar diabaikan.
                    indeksKolom.TryAdd(field, c);
                }
                else
                {
                    string asli = (judul[c] ?? string.Empty).Trim();
                    if (asli.Length > 0 && sudahDilog.Add(asli))
                        tidakDikenali.Add(asli);
                }
            }

            if (indeksKolom.Count == 0)
            {
                return new HasilImporWarga
                {
                    NamaBerkas = namaBerkas,
                    Baris = Array.Empty<BarisImporWarga>(),
                    KolomTidakDikenali = tidakDikenali,
                    KesalahanBerkas =
                        "Judul kolom tidak ada yang dikenali. Kolom yang diharapkan: NIK, Nama, " +
                        "No Kartu Keluarga, Jenis Kelamin, Tempat Lahir, Tanggal Lahir, Agama, " +
                        "Golongan Darah, dan seterusnya. Cara termudah: ekspor warga dari aplikasi, " +
                        "ubah isinya, lalu impor kembali."
                };
            }

            if (!indeksKolom.ContainsKey("NIK"))
            {
                return new HasilImporWarga
                {
                    NamaBerkas = namaBerkas,
                    Baris = Array.Empty<BarisImporWarga>(),
                    KolomTidakDikenali = tidakDikenali,
                    KesalahanBerkas = "Kolom NIK wajib ada di berkas impor."
                };
            }

            var nikTersedia = new Dictionary<string, bool>(StringComparer.Ordinal);
            var nikDiBerkas = new Dictionary<string, int>(StringComparer.Ordinal);
            var hasilBaris = new List<BarisImporWarga>(tabel.Count - 1);

            for (int i = 1; i < tabel.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Repository ini bekerja sinkron di atas SQLite, jadi antrean
                // yang panjang akan membekukan antarmuka. Melepas kendali
                // sesekali membuat jendela tetap responsif dan progres terlihat.
                if (i % 200 == 0)
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                }

                hasilBaris.Add(await PeriksaBarisAsync(
                    i + 1, tabel[i], indeksKolom, nikDiBerkas, nikTersedia,
                    perbaruiYangSudahAda, ct).ConfigureAwait(false));
            }

            return new HasilImporWarga
            {
                NamaBerkas = namaBerkas,
                Baris = hasilBaris,
                KolomTidakDikenali = tidakDikenali
            };
        }

        private async Task<BarisImporWarga> PeriksaBarisAsync(
            int nomorBaris,
            string[] baris,
            Dictionary<string, int> indeksKolom,
            Dictionary<string, int> nikDiBerkas,
            Dictionary<string, bool> nikTersedia,
            bool perbaruiYangSudahAda,
            CancellationToken ct)
        {
            string Ambil(string field) =>
                indeksKolom.TryGetValue(field, out int idx) && idx < baris.Length
                    ? (baris[idx] ?? string.Empty).Trim()
                    : string.Empty;

            var hasil = new BarisImporWarga
            {
                NomorBaris = nomorBaris,
                NIK = Ambil("NIK"),
                Nama = Ambil("Nama")
            };

            // --- NIK: wajib 16 digit angka, bukan nilai internal -------------
            string nik = hasil.NIK ?? string.Empty;
            bool nikBisaDiperiksa;

            if (nik.Length == 0)
            {
                hasil.Kesalahan.Add("NIK kosong.");
                nikBisaDiperiksa = false;
            }
            else if (!nik.All(char.IsDigit))
            {
                hasil.Kesalahan.Add("NIK hanya boleh berisi angka.");
                nikBisaDiperiksa = false;
            }
            else if (nik.Length != 16)
            {
                hasil.Kesalahan.Add($"NIK harus 16 digit, terbaca {nik.Length} digit.");
                nikBisaDiperiksa = false;
            }
            else if (NikCadangan.Contains(nik))
            {
                hasil.Kesalahan.Add("NIK ini dipakai untuk instansi atau kematian, bukan warga.");
                nikBisaDiperiksa = false;
            }
            else if (nikDiBerkas.TryGetValue(nik, out int barisPertama))
            {
                hasil.Kesalahan.Add($"NIK duplikat di berkas, sudah muncul di baris {barisPertama}.");
                nikBisaDiperiksa = false;
            }
            else
            {
                nikDiBerkas[nik] = nomorBaris;
                nikBisaDiperiksa = true;
            }

            // --- Tanggal lahir ------------------------------------------------
            string teksTanggal = Ambil("TanggalLahir");
            DateTime? tanggalLahir = null;
            if (teksTanggal.Length > 0 && !CobaParseTanggal(teksTanggal, out tanggalLahir))
                hasil.Kesalahan.Add($"Tanggal lahir '{teksTanggal}' tidak dikenali, contoh yang benar 17/08/1990.");

            // --- Jenis kelamin -------------------------------------------------
            string jenisKelamin = Ambil("JenisKelamin");
            string? jkNormal = NormalisasiJenisKelamin(jenisKelamin);
            if (jenisKelamin.Length > 0 && jkNormal == null)
                hasil.Kesalahan.Add($"Jenis kelamin '{jenisKelamin}' tidak dikenali, isi L, P, Laki-laki, atau Perempuan.");

            // --- Golongan darah -------------------------------------------------
            string golonganDarah = Ambil("GolonganDarah");
            string? goldahNormal = NormalisasiGolonganDarah(golonganDarah);
            if (golonganDarah.Length > 0 && goldahNormal == null)
                hasil.Kesalahan.Add($"Golongan darah '{golonganDarah}' tidak dikenali, isi A, B, AB, atau O.");

            // --- Status warga ----------------------------------------------------
            string status = Ambil("StatusWarga");
            if (status.Length > 0 && !StatusWargaTipe.Valid(status))
                hasil.Kesalahan.Add(
                    $"Status warga '{status}' tidak dikenali, isi {string.Join(", ", StatusWargaTipe.Semua)}.");

            if (hasil.AdaKesalahan) return hasil;

            // --- Batas panjang kolom (dari atribut model) -----------------------
            foreach (var (kolom, nilai, batas) in new (string, string?, int)[]
            {
                ("Nama", hasil.Nama, 100),
                ("Tempat Lahir", Ambil("TempatLahir"), 100),
                ("Pekerjaan", Ambil("Pekerjaan"), 100),
                ("Pendidikan", Ambil("Pendidikan"), 100),
                ("Dusun", Ambil("Dusun"), 100),
                ("Desa", Ambil("Desa"), 100),
                ("Kecamatan", Ambil("Kecamatan"), 100),
                ("Kabupaten", Ambil("Kabupaten"), 100),
                ("Nama Ayah", Ambil("NamaAyah"), 100),
                ("Nama Ibu", Ambil("NamaIbu"), 100),
                ("No Kartu Keluarga", Ambil("NoKK"), 16),
                ("Nomor HP", Ambil("NomorHP"), 20),
                ("RT", Ambil("RT"), 10),
                ("RW", Ambil("RW"), 10),
                ("Alamat (jalan/kampung)", Ambil("AlamatDetail"), 200),
                ("Keterangan", Ambil("KeteranganWarga"), 300)
            })
            {
                if (nilai is { Length: > 0 } && nilai.Length > batas)
                    hasil.Kesalahan.Add($"Kolom '{kolom}' melebihi {batas} karakter ({nilai.Length}).");
            }

            if (hasil.AdaKesalahan) return hasil;

            hasil.Data = new WargaData
            {
                NIK = nik,
                Nama = hasil.Nama!.Trim(),
                TempatLahir = Kosongkan(Ambil("TempatLahir")),
                TanggalLahir = tanggalLahir?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                JenisKelamin = jkNormal,
                Agama = Kosongkan(Ambil("Agama")),
                GolonganDarah = goldahNormal,
                StatusPerkawinan = Kosongkan(Ambil("StatusPerkawinan")),
                Pekerjaan = Kosongkan(Ambil("Pekerjaan")),
                Pendidikan = Kosongkan(Ambil("Pendidikan")),
                NamaAyah = Kosongkan(Ambil("NamaAyah")),
                NamaIbu = Kosongkan(Ambil("NamaIbu")),
                NomorHP = Kosongkan(Ambil("NomorHP")),
                NoKK = Kosongkan(Ambil("NoKK")),
                RT = Kosongkan(Ambil("RT")),
                RW = Kosongkan(Ambil("RW")),
                Dusun = Kosongkan(Ambil("Dusun")),
                AlamatDetail = Kosongkan(Ambil("AlamatDetail")),
                Desa = Kosongkan(Ambil("Desa")),
                Kecamatan = Kosongkan(Ambil("Kecamatan")),
                Kabupaten = Kosongkan(Ambil("Kabupaten")),
                Kewarganegaraan = Kosongkan(Ambil("Kewarganegaraan")) ?? "WNI",
                StatusWarga = status.Length > 0 ? StatusWargaTipe.Normalisasi(status) : StatusWargaTipe.Aktif,
                TanggalStatus = Kosongkan(Ambil("TanggalStatus")),
                KeteranganWarga = Kosongkan(Ambil("KeteranganWarga"))
            };

            // --- Aturan kolom wajib ikut model, bukan ditiru di sini ----------
            // Kalau aturan ini diduplikasi, dua tempat bisa berbeda dan berkas
            // yang lolos pemeriksaan gagal diam-diam saat disimpan. Hanya pesan
            // NIK yang dilewati, karena importer sudah memberi yang lebih
            // spesifik (panjang, karakter, duplikat di berkas, nilai internal).
            if (!hasil.Data.IsValid(out List<string> galatModel))
            {
                foreach (string pesan in galatModel)
                {
                    if (!pesan.StartsWith("NIK", StringComparison.Ordinal))
                        hasil.Kesalahan.Add(pesan);
                }

                if (hasil.AdaKesalahan) return hasil;
            }

            // --- Duplikat di database -------------------------------------------
            if (nikBisaDiperiksa)
            {
                ct.ThrowIfCancellationRequested();
                if (!nikTersedia.TryGetValue(nik, out bool sudahAda))
                {
                    sudahAda = await AdaDiDatabaseAsync(nik, ct).ConfigureAwait(false);
                    nikTersedia[nik] = sudahAda;
                }

                if (sudahAda)
                {
                    if (perbaruiYangSudahAda)
                    {
                        hasil.Tindakan = TindakanImpor.Perbarui;
                    }
                    else
                    {
                        hasil.Kesalahan.Add(
                            "NIK sudah terdaftar di database. Aktifkan perbarui bila memang ingin menimpa.");
                    }
                }
                else
                {
                    hasil.Tindakan = TindakanImpor.Simpan;
                }
            }

            return hasil;
        }

        private async Task<bool> AdaDiDatabaseAsync(string nik, CancellationToken ct)
        {
            try
            {
                var warga = await _warga.GetWargaByNikAsync(nik).ConfigureAwait(false);
                return warga != null;
            }
            catch (Exception ex)
            {
                // Gagal membaca harus terdengar, bukan diam-diam membuat semua
                // baris terlihat baru lalu menimpa data yang sudah ada.
                _logger.LogError(ex, "Gagal memeriksa NIK {NIK} di database saat impor", nik);
                throw new InvalidOperationException(
                    "Gagal memeriksa NIK di database. Periksa koneksi lalu coba lagi.", ex);
            }
        }

        // ------------------------------------------------------------------
        // Tahap 4: simpan
        // ------------------------------------------------------------------

        public async Task<int> JalankanAsync(
            HasilImporWarga hasil,
            IProgress<int>? progres = null,
            CancellationToken ct = default)
        {
            if (hasil is null) throw new ArgumentNullException(nameof(hasil));

            // Penegakan izin lapisan data: mengimpor (menulis) data warga massal
            // menuntut izin KelolaWarga. Pratinjau (ValidasiAsync) sengaja bebas —
            // hanya membaca berkas dan menghitung, tanpa menyentuh database.
            SessionContext.Wajib(IzinAplikasi.KelolaWarga);

            var siap = hasil.Baris
                .Where(b => !b.AdaKesalahan && b.Data != null && b.Tindakan != TindakanImpor.Lewati)
                .ToList();

            int tersimpan = 0;
            for (int i = 0; i < siap.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await _warga.AddOrUpdateWargaAsync(siap[i].Data!).ConfigureAwait(false);
                    siap[i].Tindakan = TindakanImpor.Simpan;
                    tersimpan++;
                }
                catch (Exception ex)
                {
                    // Satu baris gagal tidak boleh menghentikan sisa impor.
                    _logger.LogError(ex, "Gagal mengimpor warga NIK {NIK} dari baris {Baris}",
                        siap[i].NIK, siap[i].NomorBaris);
                    siap[i].Tindakan = TindakanImpor.Lewati;
                    siap[i].Kesalahan.Add("Gagal disimpan: " + ex.Message);
                }

                progres?.Report(i + 1);
            }

            return tersimpan;
        }

        // ------------------------------------------------------------------
        // Normalisasi
        // ------------------------------------------------------------------

        private static string? Kosongkan(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? null : nilai.Trim();

        internal static string? NormalisasiJenisKelamin(string? nilai) =>
            (nilai ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "L" or "LAKI-LAKI" or "LAKI LAKI" or "PRIA" or "M" => "Laki-laki",
                "P" or "PEREMPUAN" or "WANITA" or "F" => "Perempuan",
                _ => null
            };

        /// <summary>
        /// Golongan darah disimpan sebagai kode singkat (kolomnya cuma 5
        /// karakter), jadi "golongan darah A" diterjemahkan menjadi "A".
        /// </summary>
        internal static string? NormalisasiGolonganDarah(string? nilai)
        {
            string t = (nilai ?? string.Empty).Trim().ToUpperInvariant();
            if (t.Length == 0) return null;

            // Buang kata pengantar seperti "Golongan Darah A" atau "O (Rhesus -)".
            string[] kata = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string kandidat = kata.Length > 0 ? kata[^1] : t;
            if (kandidat.StartsWith("(R", StringComparison.Ordinal)) kandidat = t;

            return kandidat is "A" or "B" or "AB" or "O"
                   or "A+" or "B+" or "AB+" or "O+"
                ? kandidat
                : null;
        }

        private static readonly string[] FormatTanggal =
        {
            "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy-MM-dd",
            "yyyy/MM/dd", "dd/MM/yy"
        };

        /// <summary>
        /// Terima penulisan tanggal yang lazim dipakai orang: dd/MM/yyyy dari
        /// Excel Indonesia, yyyy-MM-dd dari sistem, dan sebagainya. Tanggal yang
        /// tak terbaca ditolak, bukan ditebak — usia dan rekapitulasi
        /// kependudukan akan salah bila tanggal keliru.
        /// </summary>
        internal static bool CobaParseTanggal(string? teks, out DateTime? tanggal)
        {
            tanggal = null;
            string t = (teks ?? string.Empty).Trim();
            if (t.Length == 0) return false;

            if (DateTime.TryParseExact(t, FormatTanggal, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime persis))
            {
                tanggal = persis;
                return true;
            }

            if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("id-ID"),
                    DateTimeStyles.None, out DateTime culture))
            {
                tanggal = culture;
                return true;
            }

            return false;
        }
    }
}
