using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SuDesApp.Services;

/// <summary>Skor resmi satu sub-dimensi Indeks Desa 2025.</summary>
public sealed record SubDimensiIndeksDesa2025(string Nama, string Dimensi, int Skor);

/// <summary>Data penilaian Indeks Desa 2025 untuk satu kode desa.</summary>
public sealed record IndeksDesa2025(
    string KodeDesa,
    string NamaDesa,
    string Kecamatan,
    string Kabupaten,
    string Provinsi,
    int LayananDasar,
    int Sosial,
    int Ekonomi,
    int Lingkungan,
    int Aksesibilitas,
    int TataKelola,
    IReadOnlyList<SubDimensiIndeksDesa2025> SubDimensi)
{
    /// <summary>Tanggal Last-Modified berkas CSV yang dikirim sumber pemerintah.</summary>
    public DateTimeOffset? TanggalPembaruanSumber { get; init; }

    public int TotalSkor => LayananDasar + Sosial + Ekonomi + Lingkungan + Aksesibilitas + TataKelola;

    /// <summary>Nilai indeks dari total 635 poin, dibulatkan dua angka sesuai tampilan penetapan.</summary>
    public double NilaiIndeks => Math.Round(TotalSkor * 100d / 635d, 2, MidpointRounding.AwayFromZero);

    /// <summary>Klasifikasi resmi menurut ambang nilai pada Permendesa No. 9 Tahun 2024.</summary>
    public string Status => NilaiIndeks >= 79.63 ? "Mandiri"
        : NilaiIndeks >= 69.35 ? "Maju"
        : NilaiIndeks >= 57.39 ? "Berkembang"
        : NilaiIndeks >= 49.49 ? "Tertinggal"
        : "Sangat Tertinggal";
}

/// <summary>Mengambil dataset resmi Indeks Desa 2025 dan mencocokkan kode desa.</summary>
public interface IIndeksDesa2025Klien
{
    Task<IndeksDesa2025?> MuatAsync(string kodeDesa, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dataset 2025 terbit di Satu Data Indonesia oleh Kementerian Desa PDT.
/// Berkas nasional disimpan di LocalAppData agar tetap tersedia setelah aplikasi
/// ditutup dan saat jaringan putus. Pemeriksaan jaringan diulang paling cepat
/// setelah enam jam.
/// </summary>
public sealed class IndeksDesa2025Klien : IIndeksDesa2025Klien
{
    public const string AlamatDataset = "https://satudata.kemendesa.go.id/dataset/9054899a-b8d9-495e-a830-be60c4295c5b/resource/71d8caca-9e3e-41b1-8888-3518991e27b9/download/indeks-desa.csv";
    private static readonly TimeSpan UmurCache = TimeSpan.FromHours(6);
    private static readonly TimeSpan JedaUlangSaatOffline = TimeSpan.FromMinutes(10);
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gerbang = new(1, 1);
    private Dictionary<string, IndeksDesa2025>? _data;
    private DateTimeOffset _berlakuSampai;
    private DateTimeOffset _waktuUnduh;
    private DateTimeOffset? _tanggalPembaruanSumber;

    public IndeksDesa2025Klien()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(45) }, null) { }

    public IndeksDesa2025Klien(HttpClient http, string? cachePath = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SuDesApp", "indeks-desa-2025.csv");
    }

    public async Task<IndeksDesa2025?> MuatAsync(string kodeDesa, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kodeDesa)) return null;
        var data = await DapatkanDataAsync(cancellationToken).ConfigureAwait(false);
        return data.TryGetValue(kodeDesa.Trim(), out var hasil) ? hasil : null;
    }

    private async Task<Dictionary<string, IndeksDesa2025>> DapatkanDataAsync(CancellationToken ct)
    {
        if (_data is not null && DateTimeOffset.UtcNow < _berlakuSampai) return _data;
        await _gerbang.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_data is not null && DateTimeOffset.UtcNow < _berlakuSampai) return _data;
            if (_data is null) MuatCacheLokal();
            if (_data is not null && DateTimeOffset.UtcNow - _waktuUnduh < UmurCache)
            {
                _berlakuSampai = _waktuUnduh.Add(UmurCache);
                return _data;
            }

            // Cache dari proses sebelumnya langsung ditampilkan. Lakukan refresh
            // di pemanggilan berikutnya, dan biarkan dataset lokal jadi fallback.
            if (_data is not null && _waktuUnduh == DateTimeOffset.MinValue)
            {
                _berlakuSampai = DateTimeOffset.UtcNow.Add(JedaUlangSaatOffline);
                return _data;
            }

            try
            {
                using var response = await _http.GetAsync(
                    AlamatDataset, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                DateTimeOffset? tanggalSumber = response.Content.Headers.LastModified;
                if (tanggalSumber is null && response.Headers.TryGetValues("Last-Modified", out var nilaiTanggal)
                    && DateTimeOffset.TryParse(nilaiTanggal.FirstOrDefault(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var tanggalHeader))
                    tanggalSumber = tanggalHeader;

                string? folder = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string sementara = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var isi = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var berkas = new FileStream(sementara, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, 81920, useAsync: true))
                    {
                        await isi.CopyToAsync(berkas, ct).ConfigureAwait(false);
                    }

                    var dataBaru = BacaDataset(sementara, tanggalSumber);
                    if (dataBaru.Count == 0)
                        throw new InvalidDataException("Dataset Indeks Desa tidak memuat baris desa yang dapat dibaca.");

                    // Pertahankan salinan lama jika proses penggantian dataset gagal.
                    File.Move(sementara, _cachePath, overwrite: true);
                    _tanggalPembaruanSumber = tanggalSumber;
                    _waktuUnduh = DateTimeOffset.UtcNow;
                    SimpanMetadata(new MetadataCache(_waktuUnduh, _tanggalPembaruanSumber));
                    _data = dataBaru;
                    _berlakuSampai = DateTimeOffset.UtcNow.Add(UmurCache);
                    return _data;
                }
                finally
                {
                    if (File.Exists(sementara)) File.Delete(sementara);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch when (_data is not null)
            {
                // Data publik yang tersimpan tetap bisa dipakai tanpa jaringan.
                _berlakuSampai = DateTimeOffset.UtcNow.Add(JedaUlangSaatOffline);
                return _data;
            }
        }
        finally { _gerbang.Release(); }
    }

    private void MuatCacheLokal()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var metadata = BacaMetadata();
            var data = BacaDataset(_cachePath, metadata?.TanggalPembaruanSumber);
            if (data.Count == 0) return;

            _data = data;
            _tanggalPembaruanSumber = metadata?.TanggalPembaruanSumber;
            _waktuUnduh = metadata?.WaktuUnduh ?? DateTimeOffset.MinValue;
            _berlakuSampai = _waktuUnduh == DateTimeOffset.MinValue
                ? DateTimeOffset.UtcNow
                : _waktuUnduh.Add(UmurCache);
        }
        catch (Exception)
        {
            // Berkas cache rusak tidak menghentikan pemuatan dari sumber resmi.
        }
    }

    private static Dictionary<string, IndeksDesa2025> BacaDataset(
        string jalur, DateTimeOffset? tanggalPembaruanSumber)
    {
        using var stream = File.OpenRead(jalur);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = BacaBaris(reader.ReadLine() ?? string.Empty);
        var kolom = header.Select((nama, index) => (nama: nama.Trim().ToUpperInvariant(), index))
            .ToDictionary(x => x.nama, x => x.index, StringComparer.Ordinal);
        string[] perlu = ["PROVINSI", "KABUPATEN", "KECAMATAN", "KODE DESA", "NAMA DESA",
            "SUB-DIMENSI PENDIDIKAN", "SUB-DIMENSI KESEHATAN", "SUB-DIMENSI UTILITAS DASAR",
            "SUB-DIMENSI AKTIVITAS", "SUB-DIMENSI FASILITAS MASYARAKAT", "SUB-DIMENSI PRODUKSI DESA",
            "SUB-DIMENSI FASILTAS PENDUKUNG EKONOMI", "SUB-DIMENSI PENGELOLAAN LINGKUNGAN",
            "SUB-DIMENSI PENANGGULANGAN BENCANA", "SUB-DIMENSI KONDISI AKSES JALAN",
            "SUB-DIMENSI KEMUDAHAN AKSES", "SUB-DIMENSI KELEMBAGAAN DAN PELAYANAN DESA",
            "SUB-DIMENSI TATA KELOLA KEUANGAN DESA"];
        foreach (var nama in perlu)
            if (!kolom.ContainsKey(nama)) throw new InvalidDataException("Kolom dataset Indeks Desa tidak dikenali: " + nama);
        int jumlahKolom = kolom.Values.Max() + 1;

        var data = new Dictionary<string, IndeksDesa2025>(StringComparer.Ordinal);
        string? baris;
        while ((baris = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(baris)) continue;
            var f = BacaBaris(baris);
            if (f.Count < jumlahKolom) continue;
            string Ambil(string nama) => f[kolom[nama]].Trim();
            string kode = Ambil("KODE DESA");
            if (kode.Length != 10) continue;
            if (!perlu.Skip(5).All(nama => int.TryParse(
                    Ambil(nama), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                continue;
            int Angka(string nama) => int.Parse(Ambil(nama), NumberStyles.Integer, CultureInfo.InvariantCulture);
            int pendidikan = Angka("SUB-DIMENSI PENDIDIKAN");
            int kesehatan = Angka("SUB-DIMENSI KESEHATAN");
            int utilitas = Angka("SUB-DIMENSI UTILITAS DASAR");
            int aktivitas = Angka("SUB-DIMENSI AKTIVITAS");
            int fasilitas = Angka("SUB-DIMENSI FASILITAS MASYARAKAT");
            int produksi = Angka("SUB-DIMENSI PRODUKSI DESA");
            int ekonomi = Angka("SUB-DIMENSI FASILTAS PENDUKUNG EKONOMI");
            int lingkungan = Angka("SUB-DIMENSI PENGELOLAAN LINGKUNGAN");
            int bencana = Angka("SUB-DIMENSI PENANGGULANGAN BENCANA");
            int jalan = Angka("SUB-DIMENSI KONDISI AKSES JALAN");
            int akses = Angka("SUB-DIMENSI KEMUDAHAN AKSES");
            int kelembagaan = Angka("SUB-DIMENSI KELEMBAGAAN DAN PELAYANAN DESA");
            int keuangan = Angka("SUB-DIMENSI TATA KELOLA KEUANGAN DESA");

            var subDimensi = new SubDimensiIndeksDesa2025[]
            {
                new("Pendidikan", "Layanan dasar", pendidikan),
                new("Kesehatan", "Layanan dasar", kesehatan),
                new("Utilitas dasar", "Layanan dasar", utilitas),
                new("Aktivitas", "Sosial", aktivitas),
                new("Fasilitas masyarakat", "Sosial", fasilitas),
                new("Produksi desa", "Ekonomi", produksi),
                new("Fasilitas pendukung ekonomi", "Ekonomi", ekonomi),
                new("Pengelolaan lingkungan", "Lingkungan", lingkungan),
                new("Penanggulangan bencana", "Lingkungan", bencana),
                new("Kondisi akses jalan", "Aksesibilitas", jalan),
                new("Kemudahan akses", "Aksesibilitas", akses),
                new("Kelembagaan dan pelayanan desa", "Tata kelola pemerintahan desa", kelembagaan),
                new("Tata kelola keuangan desa", "Tata kelola pemerintahan desa", keuangan),
            };
            data[kode] = new IndeksDesa2025(kode, Ambil("NAMA DESA"), Ambil("KECAMATAN"),
                Ambil("KABUPATEN"), Ambil("PROVINSI"), pendidikan + kesehatan + utilitas,
                aktivitas + fasilitas, produksi + ekonomi, lingkungan + bencana, jalan + akses,
                kelembagaan + keuangan, subDimensi)
            {
                TanggalPembaruanSumber = tanggalPembaruanSumber
            };
        }

        return data;
    }

    private MetadataCache? BacaMetadata()
    {
        try
        {
            string jalur = _cachePath + ".json";
            return File.Exists(jalur)
                ? JsonSerializer.Deserialize<MetadataCache>(File.ReadAllText(jalur))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SimpanMetadata(MetadataCache metadata)
    {
        string sementara = _cachePath + ".json.tmp";
        try
        {
            File.WriteAllText(sementara, JsonSerializer.Serialize(metadata));
            File.Move(sementara, _cachePath + ".json", overwrite: true);
        }
        catch (Exception)
        {
            // Datasetnya sudah tersimpan; metadata yang tidak bisa ditulis hanya
            // membuat tanggal sumber tidak tersedia pada sesi berikutnya.
        }
        finally
        {
            try { if (File.Exists(sementara)) File.Delete(sementara); }
            catch (Exception) { /* berkas sementara terkunci; dibersihkan pada simpan berikutnya */ }
        }
    }

    private sealed record MetadataCache(DateTimeOffset WaktuUnduh, DateTimeOffset? TanggalPembaruanSumber);

    private static List<string> BacaBaris(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ';' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        fields.Add(value.ToString());
        return fields;
    }
}
