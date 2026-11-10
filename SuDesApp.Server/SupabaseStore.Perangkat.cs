using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {
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


    }
}
