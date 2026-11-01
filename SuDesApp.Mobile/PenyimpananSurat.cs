using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Mobile;

/// <summary>
/// Penyimpanan ponsel: profil desa dan arsip surat sebagai berkas JSON di folder
/// data aplikasi. Tidak ada basis data yang perlu disiapkan, sehingga aplikasi
/// Android bisa dipakai tanpa menyalin <c>desa.db</c> dari komputer.
/// </summary>
public sealed class PenyimpananSurat
{
    private static readonly JsonSerializerOptions Opsi = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _folder;
    private readonly string _berkasProfil;
    private readonly string _berkasArsip;
    private readonly string _berkasSambungan;
    private readonly object _kunci = new();

    private ProfilDesa _profil = new();
    private List<SuratTersimpan> _arsip = new();
    private SambunganDesktop? _sambungan;

    public PenyimpananSurat(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Folder data kosong.", nameof(folder));

        _folder = folder;
        Directory.CreateDirectory(_folder);
        _berkasProfil = Path.Combine(_folder, "profil-desa.json");
        _berkasArsip = Path.Combine(_folder, "arsip-surat.json");
        _berkasSambungan = Path.Combine(_folder, "sambungan-desktop.json");

        Muat();
    }

    public string Folder => _folder;

    public ProfilDesa Profil
    {
        get
        {
            lock (_kunci) return _profil;
        }
    }

    public IReadOnlyList<SuratTersimpan> Arsip
    {
        get
        {
            lock (_kunci) return _arsip.OrderByDescending(s => s.Waktu).ToList();
        }
    }

    /// <summary>Sambungan ke desktop terakhir, atau null bila belum pernah tersambung.</summary>
    public SambunganDesktop? Sambungan
    {
        get
        {
            lock (_kunci) return _sambungan;
        }
    }

    public void SimpanSambungan(SambunganDesktop sambungan)
    {
        ArgumentNullException.ThrowIfNull(sambungan);

        lock (_kunci)
        {
            _sambungan = sambungan;
            TulisAtomik(_berkasSambungan, JsonSerializer.Serialize(sambungan, Opsi));
        }
    }

    /// <summary>Lupakan sambungan dan token; profil desa hasil tarikan tetap disimpan.</summary>
    public void HapusSambungan()
    {
        lock (_kunci)
        {
            _sambungan = null;
            if (File.Exists(_berkasSambungan)) File.Delete(_berkasSambungan);
        }
    }

    public void SimpanProfil(ProfilDesa profil)
    {
        ArgumentNullException.ThrowIfNull(profil);

        lock (_kunci)
        {
            _profil = profil;
            TulisAtomik(_berkasProfil, JsonSerializer.Serialize(profil, Opsi));
        }
    }

    public void Tambah(SuratTersimpan surat)
    {
        ArgumentNullException.ThrowIfNull(surat);

        lock (_kunci)
        {
            _arsip.Add(surat);
            TulisAtomik(_berkasArsip, JsonSerializer.Serialize(_arsip, Opsi));
        }
    }

    public void Hapus(string id)
    {
        lock (_kunci)
        {
            _arsip.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            TulisAtomik(_berkasArsip, JsonSerializer.Serialize(_arsip, Opsi));
        }
    }

    /// <summary>Jumlah surat berkode sama pada tahun tertentu — dasar nomor urut.</summary>
    public int HitungNomor(string kode, int tahun)
    {
        lock (_kunci)
        {
            return _arsip.Count(s =>
                string.Equals(s.Kode, kode, StringComparison.OrdinalIgnoreCase) &&
                s.Waktu.Year == tahun);
        }
    }

    /// <summary>
    /// Jumlah surat dengan awalan nomor sama pada tahun tertentu. Desktop memakai
    /// satu urutan per awalan (semua surat 470 berbagi urutan, begitu pula N1-N6
    /// pada 474.3), bukan per jenis surat; ponsel mengikuti agar nomor tidak dobel.
    /// </summary>
    public int HitungNomorAwalan(string awalan, int tahun)
    {
        if (string.IsNullOrWhiteSpace(awalan)) return 0;

        string awal = awalan.Trim() + "/";

        lock (_kunci)
        {
            return _arsip.Count(s =>
                !string.IsNullOrWhiteSpace(s.Nomor) &&
                s.Nomor.StartsWith(awal, StringComparison.OrdinalIgnoreCase) &&
                s.Waktu.Year == tahun);
        }
    }

    private void Muat()
    {
        lock (_kunci)
        {
            _profil = Baca<ProfilDesa>(_berkasProfil) ?? new ProfilDesa();
            _arsip = Baca<List<SuratTersimpan>>(_berkasArsip) ?? new List<SuratTersimpan>();
            _sambungan = Baca<SambunganDesktop>(_berkasSambungan);
        }
    }

    private static T? Baca<T>(string jalur) where T : class
    {
        if (!File.Exists(jalur)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(jalur), Opsi);
        }
        catch (Exception)
        {
            // Berkas rusak (mis. proses terhenti saat menulis) tidak boleh membuat
            // aplikasi gagal dibuka; isinya dianggap belum ada.
            return null;
        }
    }

    /// <summary>
    /// Tulis ke berkas sementara lalu ganti nama: aplikasi yang ditutup di tengah
    /// penulisan tidak meninggalkan JSON setengah jadi.
    /// </summary>
    private static void TulisAtomik(string jalur, string isi)
    {
        string sementara = jalur + ".tmp";
        File.WriteAllText(sementara, isi);
        File.Move(sementara, jalur, overwrite: true);
    }
}
