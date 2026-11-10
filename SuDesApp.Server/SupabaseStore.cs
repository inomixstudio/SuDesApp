using Npgsql;

namespace SuDesApp.Server;

/// <summary>Hasil perpanjangan langganan dari satu webhook pembayaran.</summary>
/// <param name="Diterima">True bila dana dipakai untuk memperpanjang.</param>
/// <param name="Duplikat">True bila order ini sudah pernah diproses sebelumnya.</param>
/// <param name="PelangganId">Id desa bila ditemukan.</param>
public readonly record struct HasilPerpanjang(bool Diterima, bool Duplikat, string? PelangganId);

/// <summary>
/// Hasil upaya menghapus desa dari pusat administer.
///
/// <c>AdaRiwayat</c> sengaja bukan galat: penghapusan ditolak karena desa itu
/// masih menyimpan tagihan/pembayaran — jejak uang yang tidak boleh hilang
/// hanya karena satu klik. Operator yang benar-benar perlu menghapusnya harus
/// membereskan jejak itu lebih dulu (di luar halaman ini).
/// </summary>
public enum HasilHapusDesa
{
    /// <summary>Kode aktivasi tidak dikenal.</summary>
    TidakDikenal,

    /// <summary>Masih ada tagihan/pembayaran; penghapusan ditolak.</summary>
    AdaRiwayat,

    /// <summary>Desa terhapus.</summary>
    Terhapus
}

/// <summary>
/// Hasil hapus desa beserta JEJAK APA YANG IKUT HILANG.
///
/// Angkanya ada supaya log audit menyebut jumlah tagihan &amp; pembayaran yang
/// dibuang: baris "Admin menghapus desa X" tanpa angka tidak memberi tahu
/// operator (atau siapa pun yang membaca log) bahwa 12 baris pembayaran ikut
/// musnah bersamanya. Angka juga diisi saat penghapusan DITOLAK, supaya
/// operator tahu berapa banyak jejak yang menahannya.
/// </summary>
public readonly record struct RincianHapusDesa(HasilHapusDesa Status, int Tagihan, int Pembayaran);

/// <summary>One durable admin request outcome, attributable to an operator and permission.</summary>
public sealed record AdminAuditEntry(
    string? OperatorId, string? OperatorName, string Permission, string Outcome,
    string Method, string Route, int StatusCode, string TraceId);

/// <summary>
/// Penyimpanan pelanggan &amp; langganan di Supabase Postgres, dengan isolasi
/// antar-desa lewat ROW LEVEL SECURITY (<c>tenant_id</c> = id desa).
///
/// ATURAN YANG MENGIKAT SEMUA METODE DI SINI
/// ----------------------------------------
/// Satu desa = satu tenant. Setiap transaksi yang menyentuh data desa harus
/// lebih dulu menyetel GUC <c>app.tenant_id</c> (lihat <see cref="SetTenantAsync"/>);
/// kebijakan RLS di <c>schema.sql</c> hanya meloloskan baris yang tenant-nya
/// sama. Tanpa GUC, predicate-nya NULL → nol baris: gagal tertutup.
///
/// Empat pencarian yang memang lintas-tenant (kode aktivasi → desa,
/// order_id → tagihan, daftar tagihan belum dibayar untuk rekonsiliasi, dan
/// daftar semua desa untuk "pusat administer") TIDAK memakai GUC: semuanya
/// lewat fungsi <c>SECURITY DEFINER</c> yang hanya bisa dipanggil role server
/// dan hanya mengembalikan kolom yang diperlukan. Operasi tulis pusat
/// administer tetap memakai GUC: setelah desa ditentukan, baris yang disentuh
/// dibatasi RLS seperti biasa. Lihat schema.sql.
///
/// Idempotensi pembayaran tetap seperti semula: PRIMARY KEY(order_id) di
/// <c>pembayaran</c>, di dalam transaksi yang sama dengan perpanjangannya.
/// </summary>
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
{
    /// <summary>GUC konteks tenant — harus sama dengan yang dibaca kebijakan RLS di schema.sql.</summary>
    public const string GucTenant = "app.tenant_id";

    private readonly string _connectionString;
    private readonly string? _migrateConnectionString;
    private readonly ILogger<SupabaseStore> _logger;

    /// <summary>Append an admin audit event to the restricted, append-only audit table.</summary>
    public async Task CatatAuditAdminAsync(AdminAuditEntry entry)
    {
        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO admin_action_audit
                (operator_id, operator_name, permission, outcome, http_method, route, status_code, trace_id)
            VALUES (@operator_id, @operator_name, @permission, @outcome, @method, @route, @status, @trace)
            """, conn);
        cmd.Parameters.AddWithValue("operator_id", (object?)entry.OperatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operator_name", (object?)entry.OperatorName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("permission", entry.Permission);
        cmd.Parameters.AddWithValue("outcome", entry.Outcome);
        cmd.Parameters.AddWithValue("method", entry.Method);
        cmd.Parameters.AddWithValue("route", entry.Route);
        cmd.Parameters.AddWithValue("status", entry.StatusCode);
        cmd.Parameters.AddWithValue("trace", entry.TraceId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <param name="connectionString">Koneksi role terbatas (RLS berlaku).</param>
    /// <param name="migrateConnectionString">
    /// Koneksi pemilik untuk DDL saat start; null = skema tidak diubah
    /// (operator menjalankan schema.sql sendiri).
    /// </param>
    public SupabaseStore(string connectionString, string? migrateConnectionString, ILogger<SupabaseStore> logger)
    {
        _connectionString = connectionString;
        _migrateConnectionString = migrateConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// Susun string koneksi Postgres.
    ///
    /// PENTING untuk Supabase: lewat pooler, username harus berformat
    /// <c>&lt;user&gt;.&lt;project-ref&gt;</c> (mis.
    /// <c>sudes_server.jotnrhjzyslyaozzawwq</c>), bukan <c>sudes_server</c>
    /// polos. Format polos hanya berlaku untuk koneksi langsung ke
    /// <c>db.&lt;ref&gt;.supabase.co</c> — memakai yang salah di pooler
    /// menghasilkan "tenant not found" atau login gagal.
    ///
    /// Port 5432 = session pooler, 6543 = transaction pooler. Yang dipakai di
    /// sini 5432 karena kode ini memakai transaksi eksplisit
    /// (idempotensi pembayaran), dan session pooler tidak memutus transaksi
    /// di tengah jalan. Beban server ini rendah, jadi trade-off-nya aman.
    /// </summary>
    public static string BuildConnectionString(
        string host, string password, string user = "sudes_server", string db = "postgres",
        int port = 5432, string sslMode = "Require")
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = db,
            Username = user,
            Password = password,
            SslMode = string.Equals(sslMode, "Disable", StringComparison.OrdinalIgnoreCase)
                ? SslMode.Disable
                : SslMode.Require,
            // Batas waktu penting: tanpa ini, koneksi yang menggantung di
            // jaringan buruk akan membekukan permintaan web sampai timeout
            // bawaan yang jauh lebih lama.
            Timeout = 15,
            CommandTimeout = 30
        };
        return csb.ConnectionString;
    }

    /// <summary>
    /// Siapkan skema (bila kredensial migrasi tersedia) lalu PASTIKAN isolasi
    /// RLS benar-benar berlaku untuk role yang dipakai. Melempar bila tidak —
    /// server yang jalan tanpa isolasi lebih berbahaya daripada server mati.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_migrateConnectionString is not null)
        {
            await using var conn = await BukaAsync(_migrateConnectionString);
            await using var cmd = new NpgsqlCommand(BacaSkema(), conn);
            await cmd.ExecuteNonQueryAsync();
            ServerLog.SkemaDiterapkan(_logger);
        }
        else
        {
            ServerLog.SkemaTidakDiubah(_logger);
        }

        await PastikanIsolasiAsync();
    }

    /// <summary>
    /// Verifikasi kondisi nyata di database: role koneksi tidak istimewa,
    /// seluruh tabel ber-RLS, dan fungsi pencarian yang dibutuhkan server ada.
    /// </summary>
    private async Task PastikanIsolasiAsync()
    {
        const string sql = """
            SELECT current_user,
                   (SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user) AS istimewa,
                   (SELECT count(*) FROM pg_class
                     WHERE relname IN ('pelanggan','langganan','tagihan','pembayaran',
                                       'perangkat','pengumuman','admin_action_audit')
                       AND relkind = 'r' AND relrowsecurity) AS tabel_rls,
                   (SELECT count(*) FROM pg_class c JOIN pg_roles o ON o.oid = c.relowner
                     WHERE c.relname = 'pelanggan' AND o.rolname = current_user) AS pemilik,
                   to_regprocedure('dir_cari_pelanggan(text)') IS NOT NULL AS fungsi_kode,
                   to_regprocedure('dir_cari_tagihan(text)') IS NOT NULL AS fungsi_order,
                   to_regprocedure('dir_daftar_pelanggan()') IS NOT NULL AS fungsi_daftar
            """;

        try
        {
            await using var conn = await BukaAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();

            var pengguna = r.GetString(0);
            var istimewa = !r.IsDBNull(1) && r.GetBoolean(1);
            var tabelRls = r.GetInt64(2);
            var pemilik = r.GetInt64(3) > 0;
            var fungsiKode = r.GetBoolean(4);
            var fungsiOrder = r.GetBoolean(5);
            var fungsiDaftar = r.GetBoolean(6);

            if (istimewa || pemilik)
            {
                throw new InvalidOperationException(
                    $"Role koneksi '{pengguna}' melewati RLS ({(istimewa ? "superuser/BYPASSRLS" : "pemilik tabel")}) — " +
                    "isolasi antar-desa tidak akan berlaku. Pakai role terbatas `sudes_server` di SUPABASE_DB_USER, bukan postgres.");
            }

            // Five tenant tables, public-read announcements, and the private
            // append-only admin audit table must all have RLS enabled.
            if (tabelRls != 7 || !fungsiKode || !fungsiOrder || !fungsiDaftar)
            {
                throw new InvalidOperationException(
                    $"Skema/RLS belum lengkap untuk role '{pengguna}': RLS aktif di {tabelRls}/7 tabel, " +
                    $"fungsi pencarian {(fungsiKode ? "ada" : "tidak ada")}/{(fungsiOrder ? "ada" : "tidak ada")}/" +
                    $"{(fungsiDaftar ? "ada" : "tidak ada")}. " +
                    "Jalankan SuDesApp.Server/schema.sql, atau isi SUPABASE_MIGRATE_PASSWORD agar server menerapkannya sendiri.");
            }

            ServerLog.RlsTerverifikasi(_logger, pengguna, tabelRls);
        }
        catch (PostgresException ex) when (ex.SqlState is "XX000" or "28000" or "28P01")
        {
            // Pooler Supabase menolak login role aplikasi. Penyebab tersering:
            // kata sandi role-nya belum disetel (schema.sql membuat role tanpa
            // sandi). Pesannya harus langsung menuntun ke perbaikannya.
            throw new InvalidOperationException(
                $"Role aplikasi tidak bisa login ({ex.SqlState}: {ex.MessageText}). Setel kata sandinya sekali di SQL Editor: " +
                "ALTER ROLE sudes_server PASSWORD '<kata sandi acak>'; lalu samakan dengan SUPABASE_DB_PASSWORD.", ex);
        }
    }

    /// <summary>Isi schema.sql dari assembly — satu sumber kebenaran, bukan salinan kedua di C#.</summary>
    private static string BacaSkema()
    {
        var asm = typeof(SupabaseStore).Assembly;
        var nama = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("schema.sql", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("schema.sql tidak tertanam di assembly SuDesApp.Server.");
        using var stream = asm.GetManifestResourceStream(nama)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Setel konteks tenant untuk transaksi yang sedang berjalan. Lokal
    /// transaksi (bukan sesi) supaya aman di connection pool, dan selalu
    /// sebagai parameter terikat — nilainya tidak pernah dirangkai ke SQL.
    /// </summary>
    private static async Task SetTenantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string tenantId)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config(@guc, @tenant, true)", conn, tx);
        cmd.Parameters.AddWithValue("guc", GucTenant);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Cari desa berdasarkan kode aktivasi. Lintas-tenant secara sah (kode itu
    /// sendiri adalah kredensialnya), karena itu lewat fungsi SECURITY DEFINER.
    /// </summary>

    }
