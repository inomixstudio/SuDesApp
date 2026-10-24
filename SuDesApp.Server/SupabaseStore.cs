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
public class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
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
    public async Task<Pelanggan?> CariByKodeAsync(string kode)
    {
        if (string.IsNullOrWhiteSpace(kode)) return null;

        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM dir_cari_pelanggan(@kode)", conn)
        {
            Parameters = { new("kode", kode.Trim()) }
        };
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? BacaPelangganDari(r) : null;
    }

    /// <summary>
    /// Catat perangkat yang sedang memakai kode ini dan perbarui waktu cek.
    /// Mengembalikan true bila perangkat ini dikenal atau baru pertama kali
    /// direkam (kurang dari batas <c>max_perangkat</c>).
    /// </summary>
    public async Task<(bool Bolehkan, string? Pesan)> CatatPerangkatAsync(string pelangganId, string? idPerangkat)
    {
        // Tanpa id perangkat (klien versi lama / server tak pernah menerima)
        // tidak bisa dibatasi — izinkan, kompatibilitas lebih penting.
        if (string.IsNullOrWhiteSpace(idPerangkat)) return (true, null);

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, pelangganId);

        // Batas, jumlah perangkat tercatat, dan status perangkat ini dibaca dalam
        // SATU pernyataan: keputusan "boleh" harus dihitung dari keadaan sebelum
        // baris apa pun ditulis. (Sebelumnya barisnya di-upsert lebih dulu lalu
        // dihitung, sehingga perangkat yang DITOLAK tetap tercatat.)
        int batas;
        int jumlah;
        bool dikenal;
        bool adaPelanggan;
        await using (var cek = new NpgsqlCommand("""
            SELECT p.max_perangkat,
                   (SELECT COUNT(*) FROM perangkat d WHERE d.pelanggan_id = p.id),
                   EXISTS (SELECT 1 FROM perangkat d WHERE d.pelanggan_id = p.id AND d.id_perangkat = @dev)
            FROM pelanggan p WHERE p.id = @id
            """, conn, tx))
        {
            cek.Parameters.AddWithValue("id", pelangganId);
            cek.Parameters.AddWithValue("dev", idPerangkat);
            await using var r = await cek.ExecuteReaderAsync();
            adaPelanggan = await r.ReadAsync();
            if (adaPelanggan)
            {
                batas = r.GetInt32(0);
                jumlah = r.GetInt32(1);
                dikenal = r.GetBoolean(2);
            }
            else
            {
                batas = jumlah = 0;
                dikenal = false;
            }
        }

        // Reader sudah tertutup (blok di atas selesai) sebelum rollback: ROLLBACK
        // pada koneksi yang masih punya reader aktif ditolak Npgsql ("A command
        // is already in progress") dan berakhir sebagai galat HTTP 500 — bukan
        // penolakan yang rapi seperti seharusnya.
        if (!adaPelanggan)
        {
            // Tidak terlihat = id salah ATAU milik desa lain (RLS).
            await tx.RollbackAsync();
            return (false, "Pelanggan tidak dikenal.");
        }

        // Perangkat yang sudah dikenal selalu lolos: aplikasi mengirim aktivasi
        // berkala (cek lisensi), jadi tiap kali online tidak boleh dihitung
        // sebagai perangkat baru.
        if (!dikenal && jumlah >= batas)
        {
            // PENTING: barisnya TIDAK ditulis. Kalau perangkat yang ditolak ikut
            // tercatat, jumlah perangkat desa langsung melewati batas dan
            // aktivasi berikutnya dari komputer yang sah pun ikut ditolak —
            // pelanggan yang sudah bayar kehilangan akses karena satu percobaan
            // dari komputer lain, tanpa jalan memulihkannya (tidak ada endpoint
            // yang menghapus perangkat).
            await tx.RollbackAsync();
            ServerLog.PerangkatBaruDitolak(_logger, pelangganId, jumlah, batas);
            return (false, $"Batas {batas} perangkat tercapai untuk langganan ini. Hapus perangkat lain di pusat administer.");
        }

        await using (var upsert = new NpgsqlCommand("""
            INSERT INTO perangkat (pelanggan_id, tenant_id, id_perangkat, terakhir_lihat)
            VALUES (@id, @id, @dev, now())
            ON CONFLICT (pelanggan_id, id_perangkat) DO UPDATE SET terakhir_lihat = now()
            """, conn, tx))
        {
            upsert.Parameters.AddWithValue("id", pelangganId);
            upsert.Parameters.AddWithValue("dev", idPerangkat);
            await upsert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return (true, null);
    }

    /// <summary>
    /// Daftar semua desa + ringkasan langganan &amp; jumlah perangkat. Lintas-tenant
    /// secara sengaja (operator menolong semua desa) — lewat fungsi
    /// <c>dir_daftar_pelanggan</c> yang hanya bisa dipanggil role server, dan
    /// endpoint pemanggilnya wajib bertoken admin.
    /// </summary>
    public async Task<IReadOnlyList<RingkasPelanggan>> DaftarPelangganAsync()
    {
        var hasil = new List<RingkasPelanggan>();
        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM dir_daftar_pelanggan()", conn);
        await using var r = await cmd.ExecuteReaderAsync();

        // Kolom 0..6 sesuai RETURNS TABLE di schema.sql.
        while (await r.ReadAsync())
        {
            hasil.Add(new RingkasPelanggan(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? 2 : r.GetInt32(3),
                (int)r.GetInt64(6),
                r.IsDBNull(4) ? "bulanan" : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDateTime(5).ToUniversalTime()));
        }

        return hasil;
    }

    /// <summary>
    /// Perangkat terdaftar sebuah desa. Desa ditentukan lebih dulu lewat fungsi
    /// pencarian (kode aktivasi), lalu konteks tenant disetel sehingga RLS
    /// membatasi pembacaan ke desa itu saja.
    /// </summary>
    public async Task<IReadOnlyList<InfoPerangkat>?> DaftarPerangkatAsync(string kodeAktivasi)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return null;

        var hasil = new List<InfoPerangkat>();
        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        await using (var cmd = new NpgsqlCommand("""
            SELECT id_perangkat, terakhir_lihat
              FROM perangkat WHERE pelanggan_id = @id
             ORDER BY terakhir_lihat DESC
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", desa.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                hasil.Add(new InfoPerangkat(r.GetString(0), r.GetDateTime(1).ToUniversalTime()));
        }

        await tx.CommitAsync();
        return hasil;
    }

    /// <summary>
    /// Hapus perangkat desa (satu atau semua) supaya kuota bisa dipulihkan saat
    /// pelanggan mengganti komputer. Baris yang dihapus dibatasi RLS ke desa itu.
    /// </summary>
    public async Task<int?> HapusPerangkatAsync(string kodeAktivasi, string? idPerangkat)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return null;

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        int dihapus;
        if (string.IsNullOrWhiteSpace(idPerangkat))
        {
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM perangkat WHERE pelanggan_id = @id", conn, tx);
            cmd.Parameters.AddWithValue("id", desa.Id);
            dihapus = await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM perangkat WHERE pelanggan_id = @id AND id_perangkat = @dev", conn, tx);
            cmd.Parameters.AddWithValue("id", desa.Id);
            cmd.Parameters.AddWithValue("dev", idPerangkat.Trim());
            dihapus = await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return dihapus;
    }

    /// <summary>
    /// Ubah batas jumlah perangkat desa. Bukan operasi uang (tidak menambah masa
    /// akses), jadi tidak diproteksi di sisi database — cukup token admin di HTTP.
    /// </summary>
    public async Task<bool> UbahBatasPerangkatAsync(string kodeAktivasi, int batas)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return false;

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE pelanggan SET max_perangkat = @batas WHERE id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("batas", batas);
            cmd.Parameters.AddWithValue("id", desa.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return true;
    }

    /// <summary>
    /// Riwayat pembayaran (dana masuk) sebuah desa, terbaru lebih dulu. Desa
    /// ditentukan lebih dulu lewat fungsi pencarian (kode aktivasi), lalu
    /// konteks tenant disetel sehingga RLS membatasi pembacaan ke desa itu.
    /// Paket diambil dari tagihan terkait supaya operator tahu yang dibeli;
    /// pembayaran tanpa tagihan (mis. tagihan dibersihkan) tetap tampil dengan
    /// paket kosong.
    /// </summary>
    public async Task<IReadOnlyList<InfoPembayaran>?> DaftarPembayaranAsync(string kodeAktivasi)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return null;

        var hasil = new List<InfoPembayaran>();
        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        // Gambar bukti TIDAK ikut dibaca di sini (hanya jenis & ukurannya):
        // riwayat dipakai untuk menampilkan tabel, dan kolom BYTEA yang ikut
        // ditarik akan membuat daftar pembayaran satu desa membawa megabyte
        // tanpa dipakai. Isinya diambil satu per satu lewat AmbilBuktiAsync.
        await using (var cmd = new NpgsqlCommand("""
            SELECT b.order_id, b.nominal, b.status, b.dicatat, t.paket, b.catatan,
                   b.bukti_tipe, b.bukti_ukuran
              FROM pembayaran b
              LEFT JOIN tagihan t ON t.order_id = b.order_id
             WHERE b.pelanggan_id = @id
             ORDER BY b.dicatat DESC
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", desa.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                hasil.Add(new InfoPembayaran(
                    r.GetString(0),
                    r.GetInt64(1),
                    r.GetString(2),
                    r.GetDateTime(3).ToUniversalTime(),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    !r.IsDBNull(7),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    r.IsDBNull(7) ? 0 : r.GetInt32(7)));
        }

        await tx.CommitAsync();
        return hasil;
    }

    /// <summary>
    /// Ubah nama &amp; kontak desa. Desa ditentukan lewat fungsi pencarian, lalu
    /// RLS membatasi UPDATE ke baris desa itu. Kode aktivasi tidak disentuh.
    /// </summary>
    public async Task<bool> UbahDesaAsync(string kodeAktivasi, string namaDesa, string kontak)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return false;

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE pelanggan SET nama_desa = @nama, kontak = @kontak WHERE id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("nama", namaDesa.Trim());
            cmd.Parameters.AddWithValue("kontak", kontak ?? "");
            cmd.Parameters.AddWithValue("id", desa.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return true;
    }

    /// <summary>
    /// Hapus desa + langganan/perangkat/tagihan/pembayarannya (semua CASCADE).
    /// DITOLAK bila masih ada tagihan atau pembayaran: itu jejak uang/audit yang
    /// tidak boleh hilang karena satu klik — kecuali <paramref name="paksa"/>.
    /// Pengecekan, penghitungan, dan penghapusan berada di SATU transaksi
    /// supaya tagihan yang masuk bersamaan tidak bisa lolos di antaranya.
    /// </summary>
    public async Task<RincianHapusDesa> HapusDesaAsync(string kodeAktivasi, bool paksa)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null) return new RincianHapusDesa(HasilHapusDesa.TidakDikenal, 0, 0);

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        // Dihitung SEBELUM dihapus: angka ini masuk ke log audit, jadi harus
        // menggambarkan jejak yang benar-benar ada, bukan perkiraan.
        int tagihan, pembayaran;
        await using (var cek = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM tagihan    WHERE pelanggan_id = @id),
                   (SELECT count(*) FROM pembayaran WHERE pelanggan_id = @id)
            """, conn, tx))
        {
            cek.Parameters.AddWithValue("id", desa.Id);
            await using var r = await cek.ExecuteReaderAsync();
            await r.ReadAsync();
            tagihan = r.GetInt32(0);
            pembayaran = r.GetInt32(1);
        }

        if (tagihan + pembayaran > 0 && !paksa)
        {
            await tx.RollbackAsync();
            return new RincianHapusDesa(HasilHapusDesa.AdaRiwayat, tagihan, pembayaran);
        }

        await using (var del = new NpgsqlCommand("DELETE FROM pelanggan WHERE id = @id", conn, tx))
        {
            del.Parameters.AddWithValue("id", desa.Id);
            await del.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new RincianHapusDesa(HasilHapusDesa.Terhapus, tagihan, pembayaran);
    }

    /// <summary>
    /// Daftarkan pelanggan baru + baris langganannya. Dipakai endpoint admin.
    /// Idempoten terhadap <paramref name="kodeAktivasi"/>: kode yang sudah ada
    /// dikembalikan apa adanya, bukan dibuat ganda.
    /// </summary>
    public async Task<Pelanggan> DaftarkanAsync(string kodeAktivasi, string namaDesa,
                                                 string kontak = "", int maxPerangkat = 2)
    {
        if (string.IsNullOrWhiteSpace(kodeAktivasi))
            throw new ArgumentException("Kode aktivasi wajib diisi.", nameof(kodeAktivasi));
        if (string.IsNullOrWhiteSpace(namaDesa))
            throw new ArgumentException("Nama desa wajib diisi.", nameof(namaDesa));

        var kode = kodeAktivasi.Trim();
        var existing = await CariByKodeAsync(kode);
        if (existing != null) return existing;

        // Id desa dibuat lebih dulu karena sekaligus menjadi tenant_id baris ini:
        // konteks tenant harus sudah disetel saat barisnya ditulis.
        var id = Guid.NewGuid().ToString("N");

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, id);

        await using (var pelanggan = new NpgsqlCommand("""
            INSERT INTO pelanggan (id, tenant_id, kode_aktivasi, nama_desa, kontak, max_perangkat)
            VALUES (@id, @id, @kode, @nama, @kontak, @maks)
            ON CONFLICT (kode_aktivasi) DO NOTHING
            """, conn, tx))
        {
            pelanggan.Parameters.AddWithValue("id", id);
            pelanggan.Parameters.AddWithValue("kode", kode);
            pelanggan.Parameters.AddWithValue("nama", namaDesa.Trim());
            pelanggan.Parameters.AddWithValue("kontak", kontak ?? "");
            pelanggan.Parameters.AddWithValue("maks", maxPerangkat <= 0 ? 2 : maxPerangkat);
            await pelanggan.ExecuteNonQueryAsync();
        }

        await using (var langganan = new NpgsqlCommand("""
            INSERT INTO langganan (pelanggan_id, tenant_id, paket, berlaku_sampai, order_id_tagihan_terakhir, total_terbayar)
            SELECT id, id, 'bulanan', NULL, NULL, 0 FROM pelanggan WHERE kode_aktivasi = @kode
            ON CONFLICT (pelanggan_id) DO NOTHING
            """, conn, tx))
        {
            langganan.Parameters.AddWithValue("kode", kode);
            await langganan.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        var hasil = await CariByKodeAsync(kode);
        return hasil ?? throw new InvalidOperationException("Pelanggan gagal didaftarkan.");
    }

    /// <summary>
    /// Catat tagihan yang baru dibuat (belum dibayar) beserta paket, nominal,
    /// dan jumlah hari yang akan diberikan bila dibayar.
    ///
    /// <paramref name="nominal"/> hanya dipakai jalur pembukuan operator
    /// (pembayaran QRIS statis/transfer): di sana yang dicatat adalah uang yang
    /// BENAR-BENAR masuk menurut bukti transfer, bukan harga katalog. Jalur
    /// klien selalu mengirim null sehingga nominalnya tetap harga server.
    /// </summary>
    public async Task SimpanTagihanAsync(string pelangganId, string orderId, PaketLangganan paket, long? nominal = null)
    {
        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, pelangganId);

        await using (var tagih = new NpgsqlCommand("""
            INSERT INTO tagihan (order_id, pelanggan_id, tenant_id, paket, nominal, hari)
            VALUES (@order, @id, @id, @paket, @nominal, @hari)
            ON CONFLICT (order_id) DO NOTHING
            """, conn, tx))
        {
            tagih.Parameters.AddWithValue("order", orderId);
            tagih.Parameters.AddWithValue("id", pelangganId);
            tagih.Parameters.AddWithValue("paket", paket.Kode);
            tagih.Parameters.AddWithValue("nominal", nominal ?? paket.Harga);
            tagih.Parameters.AddWithValue("hari", paket.Hari);
            await tagih.ExecuteNonQueryAsync();
        }

        // Tagihan terakhir dicatat di baris langganan supaya mudah dilihat,
        // tapi perpanjangan PADA WEBHOOK membaca tabel tagihan (yang per
        // order_id), bukan kolom ini — kolom ini hanya satu order terakhir.
        await using (var langganan = new NpgsqlCommand("""
            INSERT INTO langganan (pelanggan_id, tenant_id, paket, berlaku_sampai, order_id_tagihan_terakhir, total_terbayar)
            VALUES (@id, @id, @paket, NULL, @order, 0)
            ON CONFLICT (pelanggan_id) DO UPDATE
            SET order_id_tagihan_terakhir = @order
            """, conn, tx))
        {
            langganan.Parameters.AddWithValue("id", pelangganId);
            langganan.Parameters.AddWithValue("paket", paket.Kode);
            langganan.Parameters.AddWithValue("order", orderId);
            await langganan.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>
    /// Ambil tagihan by order id — dipakai webhook untuk tahu paket &amp; durasi.
    /// Order id tidak bisa ditebak dan dipanggil hanya setelah signature
    /// Midtrans diverifikasi, jadi pencariannya lintas-tenant secara sah.
    /// </summary>
    public async Task<(string PelangganId, string Paket, long Nominal, int Hari)?> CariTagihanAsync(string orderId)
    {
        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM dir_cari_tagihan(@order)", conn)
        {
            Parameters = { new("order", orderId) }
        };
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return (
            r.GetString(0),
            r.GetString(1),
            r.GetInt64(2),
            r.GetInt32(3));
    }

    /// <summary>
    /// Perpanjang langganan secara IDEMPOTEN.
    ///
    /// <b>Idempotensi</b>: keunikan dijamin oleh <c>pembayaran.order_id</c>
    /// sebagai PRIMARY KEY di dalam transaksi yang sama — bila order sudah
    /// pernah tercatat, baris tidak ter-insert dan perpanjangan dilewati.
    /// Webhook berulang (yang memang terjadi) karena itu tidak menambah hari.
    ///
    /// <para><paramref name="hari"/> diambil dari tabel <c>tagihan</c>, bukan
    /// dari nilai tetap server: paket bulanan menambah 31 hari, tahunan 365
    /// hari. Perpanjangan dihitung dari <c>max(berlaku_sampai, now())</c>
    /// sehingga pembayaran yang telat tidak membuat masa langganan yang sedang
    /// berjalan hangus.</para>
    ///
    /// <para>Desa pemilik order ditentukan lewat fungsi pencarian (lintas-tenant,
    /// sah karena signature sudah diverifikasi), lalu konteks tenant disetel
    /// sehingga seluruh tulisan berikutnya dibatasi RLS ke desa itu.</para>
    ///
    /// <para><paramref name="catatan"/> diteruskan ke kolom
    /// <c>pembayaran.catatan</c> — hanya diisi jalur pembukuan manual; webhook
    /// dan rekonsiliasi memanggilnya tanpa catatan.</para>
    ///
    /// <para><paramref name="bukti"/> adalah lampiran gambar bukti transfer dari
    /// pembukuan operator, disimpan di kolom <c>pembayaran.bukti_*</c> di dalam
    /// transaksi yang sama dengan baris pembayarannya — tidak ada keadaan di
    /// mana pembayaran tercatat tetapi lampirannya hilang, atau sebaliknya.</para>
    /// </summary>
    public async Task<HasilPerpanjang> PerpanjangAsync(
        string orderId, int hari, string status, string? catatan = null, BuktiBayar? bukti = null)
    {
        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();

        string pelangganId;
        long nominal;
        bool tagihanDitemukan;
        await using (var tagih = new NpgsqlCommand(
            "SELECT pelanggan_id, nominal FROM dir_cari_tagihan(@order)", conn, tx))
        {
            tagih.Parameters.AddWithValue("order", orderId);
            await using var r = await tagih.ExecuteReaderAsync();
            tagihanDitemukan = await r.ReadAsync();
            if (tagihanDitemukan)
            {
                pelangganId = r.GetString(0);
                nominal = r.GetInt64(1);
            }
            else
            {
                pelangganId = string.Empty;
                nominal = 0;
            }
        }

        // Reader ditutup dulu sebelum rollback (lihat catatan di
        // CatatPerangkatAsync): rollback dengan reader aktif jadi galat 500.
        if (!tagihanDitemukan)
        {
            await tx.RollbackAsync();
            return new HasilPerpanjang(false, false, null);
        }

        await SetTenantAsync(conn, tx, pelangganId);

        // Idempotensi: INSERT ... ON CONFLICT DO NOTHING — rows==0 berarti
        // order ini sudah pernah diproses → jangan perpanjang lagi.
        int barisBaru;
        // catatan = referensi bukti bayar dari pembukuan operator; NULL pada
        // pembayaran gateway. Kolomnya nullable dan ditambah lewat ALTER di
        // schema.sql, jadi instalasi lama tidak perlu migrasi terpisah.
        await using (var catat = new NpgsqlCommand("""
            INSERT INTO pembayaran (order_id, pelanggan_id, tenant_id, nominal, status, catatan,
                                   bukti_gambar, bukti_tipe, bukti_ukuran, bukti_nama)
            VALUES (@order, @id, @id, @nominal, @status, @catatan,
                    @bukti, @buktiTipe, @buktiUkuran, @buktiNama)
            ON CONFLICT (order_id) DO NOTHING
            """, conn, tx))
        {
            catat.Parameters.AddWithValue("order", orderId);
            catat.Parameters.AddWithValue("id", pelangganId);
            catat.Parameters.AddWithValue("nominal", nominal);
            catat.Parameters.AddWithValue("status", status);
            catat.Parameters.AddWithValue("catatan",
                string.IsNullOrWhiteSpace(catatan) ? DBNull.Value : catatan.Trim());
            catat.Parameters.AddWithValue("bukti",
                bukti is null ? DBNull.Value : (object)bukti.Isi);
            catat.Parameters.AddWithValue("buktiTipe",
                bukti is null ? DBNull.Value : bukti.Tipe);
            catat.Parameters.AddWithValue("buktiUkuran",
                bukti is null ? DBNull.Value : bukti.Isi.Length);
            catat.Parameters.AddWithValue("buktiNama",
                bukti?.Nama is { Length: > 0 } nama ? nama : (object)DBNull.Value);
            barisBaru = await catat.ExecuteNonQueryAsync();
        }

        if (barisBaru == 0)
        {
            await tx.CommitAsync();
            ServerLog.WebhookDuplikat(_logger, orderId);
            return new HasilPerpanjang(true, true, pelangganId);
        }

        await using (var perpanjang = new NpgsqlCommand("""
            UPDATE langganan
            SET berlaku_sampai = GREATEST(COALESCE(berlaku_sampai, now()), now()) + (@hari || ' days')::interval,
                total_terbayar = total_terbayar + 1,
                paket = (SELECT paket FROM tagihan WHERE order_id = @order)
            WHERE pelanggan_id = @id
            """, conn, tx))
        {
            perpanjang.Parameters.AddWithValue("id", pelangganId);
            perpanjang.Parameters.AddWithValue("hari", hari);
            perpanjang.Parameters.AddWithValue("order", orderId);
            await perpanjang.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        ServerLog.LanggananDiperpanjang(_logger, pelangganId, hari, orderId);
        return new HasilPerpanjang(true, false, pelangganId);
    }

    /// <summary>
    /// Lampiran bukti transfer satu pembayaran. Desa ditentukan lebih dulu dari
    /// kode aktivasi (fungsi pencarian lintas-tenant yang memang sudah dipakai
    /// jalur admin), lalu konteks tenant disetel sehingga barisnya hanya bisa
    /// dibaca bila benar-benar milik desa itu.
    ///
    /// <c>null</c> untuk desa/order yang tidak ada ATAU pembayaran tanpa
    /// lampiran (jalur gateway) — pemanggil tidak perlu membedakannya: keduanya
    /// sama-sama "tidak ada gambar untuk disajikan".
    /// </summary>
    public async Task<BuktiBayar?> AmbilBuktiAsync(string kodeAktivasi, string orderId)
    {
        var desa = await CariByKodeAsync(kodeAktivasi);
        if (desa is null || string.IsNullOrWhiteSpace(orderId)) return null;

        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, desa.Id);

        byte[]? isi = null;
        string? tipe = null;
        string? nama = null;
        await using (var cmd = new NpgsqlCommand("""
            SELECT bukti_gambar, bukti_tipe, bukti_nama
              FROM pembayaran
             WHERE order_id = @order AND pelanggan_id = @id
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("order", orderId.Trim());
            cmd.Parameters.AddWithValue("id", desa.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync() && !r.IsDBNull(0))
            {
                isi = (byte[])r.GetValue(0);
                tipe = r.IsDBNull(1) ? null : r.GetString(1);
                nama = r.IsDBNull(2) ? null : r.GetString(2);
            }
        }

        await tx.CommitAsync();
        return isi is null ? null : new BuktiBayar(isi, tipe ?? BuktiTransfer.TipeGambar(isi) ?? "application/octet-stream", nama);
    }

    /// <summary>
    /// Daftar tagihan yang belum punya baris pembayaran — dipakai
    /// rekonsiliasi. Lintas-tenant secara sah karena serverlah yang
    /// menjalankannya (bukan satu desa): ia perlu mengetahui order mana saja
    /// yang masih menunggu, lalu menanyakan statusnya ke Midtrans.
    ///
    /// Fungsi di schema.sql hanya mengembalikan kolom tagihan (order, paket,
    /// nominal, hari, waktu) — tidak ada kontak maupun data desa lain.
    /// </summary>
    public async Task<IReadOnlyList<TagihanBelumDibayar>> DaftarTagihanBelumDibayarAsync(
        int batas, DateTime? lebihTuaDari = null, DateTime? lebihBaruDari = null, CancellationToken ct = default)
    {
        var hasil = new List<TagihanBelumDibayar>();
        if (batas <= 0) return hasil;

        await using var conn = await BukaAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM dir_tagihan_belum_dibayar(@batas, @tua, @baru)", conn);

        // Tipe parameter ditulis eksplisit: tanpa itu, parameter bertipe NULL
        // membuat Postgres tidak bisa memilih fungsi pada pemanggilan pertama
        // (dan DateTimeKind.Unspecified akan dicocokkan ke `timestamp`, bukan
        // `timestamptz`).
        cmd.Parameters.Add(new NpgsqlParameter("batas", NpgsqlTypes.NpgsqlDbType.Integer) { Value = batas });
        cmd.Parameters.Add(new NpgsqlParameter("tua", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = UtcAtauNull(lebihTuaDari)
        });
        cmd.Parameters.Add(new NpgsqlParameter("baru", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = UtcAtauNull(lebihBaruDari)
        });

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            hasil.Add(new TagihanBelumDibayar(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetInt64(3),
                r.GetInt32(4),
                r.GetDateTime(5).ToUniversalTime()));
        }

        return hasil;
    }

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

    /// <summary>
    /// Baca satu baris pelanggan+langganan dari hasil
    /// <c>dir_cari_pelanggan</c> (kolom 0..9 sesuai RETURNS TABLE di schema.sql).
    /// </summary>
    private static Pelanggan? BacaPelangganDari(NpgsqlDataReader r)
    {
        var p = new Pelanggan
        {
            Id = r.GetString(0),
            KodeAktivasi = r.GetString(1),
            NamaDesa = r.GetString(2),
            Kontak = r.IsDBNull(3) ? "" : r.GetString(3),
            Dibuat = r.GetDateTime(4),
            MaxPerangkat = r.IsDBNull(5) ? 2 : r.GetInt32(5)
        };
        // LEFT JOIN: kolom langganan NULL bila langganan belum pernah dibuat.
        p.Langganan = new Langganan
        {
            PelangganId = p.Id,
            Paket = r.IsDBNull(6) ? "bulanan" : r.GetString(6),
            BerlakuSampai = r.IsDBNull(7) ? null : r.GetDateTime(7).ToUniversalTime(),
            OrderIdTagihanTerakhir = r.IsDBNull(8) ? null : r.GetString(8),
            TotalTerbayar = r.IsDBNull(9) ? 0 : r.GetInt32(9)
        };

        // Kolom 10 (trial_sudah) hanya ada bila schema.sql baru sudah dijalankan;
        // skema lama tidak memecah aktivasi/tagihan — trial ditolak dengan
        // pesan yang jelas, bukan pengecualian.
        p.TrialSudah = r.FieldCount > 10 && !r.IsDBNull(10) && r.GetBoolean(10);
        return p;
    }

    /// <summary>
    /// Aktifkan trial (tanpa uang) untuk satu desa. Guard dua lapis dilakukan
    /// DIDALAM TRANSAKSI yang sama supaya dua PC desa yang menekan tombol
    /// bersamaan tidak bisa keduanya meloloskan trial:
    ///   1. baca trial_sudah + berlaku_sampai (FOR UPDATE pada baris pelanggan);
    ///   2. bila sah → taruh paket 'trial' + jatuh tempo (INSERT bila baris
    ///      langganan belum ada) dan tanda trial_sudah = true.
    /// Hari datang dari konst server; sekarang dari parameter supaya uji waktu
    /// palsu (kalau perlu) tidak melihat jam DB yang berbeda.
    /// </summary>
    public async Task<HasilTrial> MulaiTrialAsync(string pelangganId, int hari, DateTime sekarang)
    {
        await using var conn = await BukaAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Konteks tenant WAJIB disetel sebelum baris pelanggan dibaca. Tanpa
        // GUC, kebijakan RLS menutup baris itu (tenant_id <> tenant_aktif())
        // sehingga barisnya tidak terlihat sama sekali — gejalanya: trial
        // selalu ditolak dengan "Desa tidak dikenal" padahal desanya ada.
        await SetTenantAsync(conn, tx, pelangganId);

        bool sudah;
        DateTime? berlaku;
        bool ditemukan;
        await using (var baca = new NpgsqlCommand(
            "SELECT trial_sudah, " +
            "(SELECT l.berlaku_sampai FROM langganan l WHERE l.pelanggan_id = p.id) " +
            "FROM pelanggan p WHERE p.id = @id FOR UPDATE", conn, tx))
        {
            baca.Parameters.AddWithValue("id", pelangganId);
            await using var r = await baca.ExecuteReaderAsync();
            ditemukan = await r.ReadAsync();
            sudah = ditemukan && !r.IsDBNull(0) && r.GetBoolean(0);
            berlaku = !ditemukan || r.IsDBNull(1) ? null : r.GetDateTime(1).ToUniversalTime();
        }

        // Reader ditutup (blok di atas selesai) sebelum rollback: ROLLBACK
        // pada koneksi yang masih punya reader aktif ditolak Npgsql ("A
        // command is already in progress") dan berakhir sebagai galat 500.
        if (!ditemukan)
        {
            await tx.RollbackAsync();
            return new HasilTrial(false, "Desa tidak dikenal.");
        }

        if (sudah)
        {
            await tx.RollbackAsync();
            return new HasilTrial(false, "Trial sudah pernah dipakai untuk desa ini.");
        }

        if (berlaku is not null && berlaku.Value > sekarang)
        {
            await tx.RollbackAsync();
            return new HasilTrial(false,
                $"Langganan masih aktif s.d. {berlaku.Value:yyyy-MM-dd}; trial tidak perlu.");
        }

        var baru = sekarang.AddDays(hari);
        await using (var tulis = new NpgsqlCommand(
            """
            INSERT INTO langganan (pelanggan_id, tenant_id, paket, berlaku_sampai)
            VALUES (@id, @id, 'trial', @baru)
            ON CONFLICT (pelanggan_id) DO UPDATE
                SET paket = 'trial', berlaku_sampai = EXCLUDED.berlaku_sampai;

            UPDATE pelanggan SET trial_sudah = true WHERE id = @id;
            """, conn, tx))
        {
            tulis.Parameters.AddWithValue("id", pelangganId);
            tulis.Parameters.AddWithValue("baru", baru);
            await tulis.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new HasilTrial(true, BerlakuSampai: baru);
    }
}
