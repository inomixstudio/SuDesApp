namespace SuDesApp.Server;

/// <summary>
/// Penampung audit admin untuk mode Development tanpa database. Sengaja tidak
/// dipakai di produksi: jejak audit harus bertahan setelah proses berhenti.
/// </summary>
public sealed class AuditMemori : IAdminAuditStore
{
    private readonly List<AdminAuditEntry> _entri = new();
    private readonly object _kunci = new();

    public IReadOnlyList<AdminAuditEntry> Entri
    {
        get
        {
            lock (_kunci) return _entri.ToArray();
        }
    }

    public Task CatatAuditAdminAsync(AdminAuditEntry entry)
    {
        lock (_kunci) _entri.Add(entry);
        return Task.CompletedTask;
    }

    /// <summary>Salinan seluruh entri audit untuk disimpan ke berkas.</summary>
    internal IReadOnlyList<AdminAuditEntry> AmbilSnapshot()
    {
        lock (_kunci) return _entri.ToArray();
    }

    /// <summary>Ganti seluruh entri dengan isi berkas simpanan.</summary>
    internal void TerapkanSnapshot(IEnumerable<AdminAuditEntry> entri)
    {
        lock (_kunci)
        {
            _entri.Clear();
            _entri.AddRange(entri);
        }
    }
}
