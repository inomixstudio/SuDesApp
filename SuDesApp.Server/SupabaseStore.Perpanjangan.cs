using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {
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

    }
}
