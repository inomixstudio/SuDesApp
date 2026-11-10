using Npgsql;

namespace SuDesApp.Server
{
public partial class SupabaseStore : IRepositoryLangganan, IAdminAuditStore, IPengumumanStore
    {
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

    }
}
