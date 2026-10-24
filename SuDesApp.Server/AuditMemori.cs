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
}
