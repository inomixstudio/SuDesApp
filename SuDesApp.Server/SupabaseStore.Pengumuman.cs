using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {

    /// <summary>Batas waktu → parameter timestamptz (UTC), atau NULL bila tidak ada.</summary>
    private static object UtcAtauNull(DateTime? waktu) =>
        waktu is null ? DBNull.Value : DateTime.SpecifyKind(waktu.Value, DateTimeKind.Utc);

    /// <summary>
    /// Baca baris pengumuman broadcast (tabel <c>pengumuman</c> — lihat
    /// schema.sql) untuk endpoint GET /pengumuman.
    ///
    /// Saringan aktif/jendela waktu TIDAK di-SQL: daftar mentah dikembalikan
    /// lalu disaring oleh <see cref="LayananPengumuman.SaringAktif"/> yang
    /// diuji unit.
    ///
    /// Urutan WAJIB terbaru dulu. LIMIT di sini adalah batas jendela baca,
    /// bukan sekadar optimasi: dengan urutan lama-ke-baru, pengumuman yang
    /// baru diterbitkan tidak akan pernah ikut terbaca begitu tabel melewati
    /// batas — operator melihat "sudah terbit" tetapi tidak ada desa yang
    /// menerimanya.
    ///
    /// Tabel ini boleh BELUM ADA di instalasi lama yang belum menjalankan
    /// schema.sql baru. Karena itu kegagalan "relation does not exist" tidak
    /// melempar — dikembalikan daftar kosong: pengumuman adalah fitur tambahan
    /// yang tidak boleh menggagalkan server lisensi.
    /// </summary>
    public async Task<List<PengumumanServer>> BacaPengumumanAsync()
    {
        const string sql = """
            SELECT id, judul, isi, jenis, dibuat, mulai, sampai, aktif
              FROM pengumuman
             ORDER BY dibuat DESC
             LIMIT $1
            """;

        try
        {
            var hasil = new List<PengumumanServer>();
            await using var conn = await BukaAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(LayananPengumuman.BatasMuat);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                hasil.Add(BacaPengumumanDari(r));
            }
            return hasil;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            ServerLog.TabelPengumumanBelumAda(_logger);
            return [];
        }
    }

    private async Task<NpgsqlConnection> BukaAsync(string? connectionString = null)
    {
        var conn = new NpgsqlConnection(connectionString ?? _connectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Mapping baris tabel <c>pengumuman</c> (kolom 0..7) → model. Dipakai baik
    /// oleh baca publik maupun daftar admin supaya urutan kolom tidak bisa
    /// menyimpang antara dua jalur.
    /// </summary>
    private static PengumumanServer BacaPengumumanDari(NpgsqlDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.IsDBNull(2) ? "" : r.GetString(2),
        r.IsDBNull(3) ? "info" : r.GetString(3),
        DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc),
        r.IsDBNull(6) ? null : DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc))
    {
        Mulai = r.IsDBNull(5) ? null : DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
        Aktif = r.GetBoolean(7)
    };

    /// <summary>
    /// Daftar SEMUA pengumuman (termasuk yang nonaktif) untuk endpoint admin —
    /// terbaru dulu. Berbeda dari <see cref="BacaPengumumanAsync"/> yang hanya
    /// baris mentah untuk disaring di endpoint publik.
    /// </summary>
    public async Task<List<PengumumanServer>> DaftarPengumumanSemuaAsync()
    {
        const string sql = """
            SELECT id, judul, isi, jenis, dibuat, mulai, sampai, aktif
              FROM pengumuman
             ORDER BY dibuat DESC
             LIMIT 100
            """;

        var hasil = new List<PengumumanServer>();
        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            hasil.Add(BacaPengumumanDari(r));
        }
        return hasil;
    }

    /// <summary>
    /// Terbitkan pengumuman baru. Id dibuat di sini (UUID) supaya balasannya
    /// pasti membawa id yang bisa langsung dipakai UI admin untuk hapus jenis.
    /// Tabel tidak punya UNIQUE(judul) sengaja: pengumuman boleh berulang.
    /// </summary>
    public async Task<string> BuatPengumumanAsync(
        string judul, string isi, string jenis, DateTime? mulai, DateTime? sampai)
    {
        var id = Guid.NewGuid().ToString("N");
        const string sql = """
            INSERT INTO pengumuman (id, judul, isi, jenis, aktif, mulai, sampai)
            VALUES (@id, @judul, @isi, @jenis, true, @mulai, @sampai)
            """;

        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("judul", judul);
        cmd.Parameters.AddWithValue("isi", isi);
        cmd.Parameters.AddWithValue("jenis", jenis);
        cmd.Parameters.AddWithValue("mulai", mulai.HasValue ? (object)mulai.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sampai", sampai.HasValue ? (object)sampai.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Jumlah baris dihapus; 0 = id tidak dikenal.</summary>
    public async Task<int> HapusPengumumanAsync(string id)
    {
        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM pengumuman WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync();
    }

    }
}
