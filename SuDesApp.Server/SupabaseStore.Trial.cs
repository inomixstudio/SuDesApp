using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {

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
}
