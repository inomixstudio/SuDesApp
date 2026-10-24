namespace SuDesApp.Server;

/// <summary>
/// Pengumuman in-memory untuk mode Development tanpa database. Data hilang saat
/// proses berhenti; produksi selalu memakai <see cref="SupabaseStore"/>.
/// </summary>
public sealed class PengumumanMemori : IPengumumanStore
{
    private readonly List<PengumumanServer> _daftar = new();
    private readonly object _kunci = new();

    public Task<List<PengumumanServer>> BacaPengumumanAsync()
    {
        lock (_kunci) return Task.FromResult(_daftar.ToList());
    }

    public Task<List<PengumumanServer>> DaftarPengumumanSemuaAsync()
    {
        lock (_kunci)
        {
            return Task.FromResult(_daftar
                .OrderByDescending(p => p.Dibuat)
                .ToList());
        }
    }

    public Task<string> BuatPengumumanAsync(
        string judul, string isi, string jenis, DateTime? mulai, DateTime? sampai)
    {
        var entri = new PengumumanServer(
            Guid.NewGuid().ToString("N"), judul, isi, jenis, DateTime.UtcNow, sampai)
        {
            Mulai = mulai,
            Aktif = true
        };

        lock (_kunci) _daftar.Add(entri);
        return Task.FromResult(entri.Id);
    }

    public Task<int> HapusPengumumanAsync(string id)
    {
        lock (_kunci)
        {
            return Task.FromResult(_daftar.RemoveAll(
                p => string.Equals(p.Id, id, StringComparison.Ordinal)));
        }
    }
}
