using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {
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


    }
}
