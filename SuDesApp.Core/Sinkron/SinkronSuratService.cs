using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Supabase;
namespace SuDesApp.Sinkron;

/// <summary>
/// Layanan sinkronisasi surat Android → desktop.
///
/// <para>
/// Satu putaran: tarik antrean milik tenant ini → tulis berkas .xlsx jejak →
/// impor tiap baris lewat <see cref="ImporAntreanSurat"/> → baris berhasil
/// dihapus dari antrean (dan jejaknya masuk <c>sinkron_log</c>), baris gagal
/// ditandai <c>gagal</c> dengan penghitung percobaan.
/// </para>
///
/// <para>
/// Kegagalan koneksi tidak pernah melempar ke pemanggil: Offline adalah
/// keadaan normal instalasi desa, dan tombol sinkron bisa dicoba lagi. Kegagalan
/// itu dikembalikan sebagai <see cref="SinkronHasil"/> dengan pesan.
/// </para>
/// </summary>
public sealed class SinkronSuratService
{
    /// <summary>Batas percobaan impor sebelum baris dibuang dari antrean.</summary>
    public const int BatasCobaMaks = 3;

    /// <summary>
    /// Folder berkas .xlsx jejak sinkron. Di luar folder instalasi supaya
    /// tambalan pembaruan tidak menimpanya.
    /// </summary>
    public static string FolderBawaan => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SuDesApp", "Sinkron");

    private readonly SupabasePengaturan _pengaturan;
    private readonly ImporAntreanSurat _impor;
    private readonly ILogger<SinkronSuratService> _logger;
    private readonly string _folderBerkas;

    public SinkronSuratService(
        SupabasePengaturan pengaturan,
        ImporAntreanSurat impor,
        ILogger<SinkronSuratService> logger,
        string folderBerkas)
    {
        _pengaturan = pengaturan ?? throw new System.ArgumentNullException(nameof(pengaturan));
        _impor = impor ?? throw new System.ArgumentNullException(nameof(impor));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        _folderBerkas = folderBerkas ?? throw new System.ArgumentNullException(nameof(folderBerkas));
    }

    /// <summary>True bila Supabase sudah dikonfigurasi lengkap.</summary>
    public bool Siap => _pengaturan.Lengkap;

    /// <summary>Jumlah surat yang masih menunggu di antrean tenant ini.</summary>
    public async Task<int> HitungAntreanAsync(CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap) return 0;

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*)::int FROM antrean_surat WHERE tenant_id = @tenant", conn);
            cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
            var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return hasil is int n ? n : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HitungAntrean gagal — dianggap 0.");
            return 0;
        }
    }

    /// <summary>Jalankan satu putaran sinkron penuh.</summary>
    public async Task<SinkronHasil> SinkronkanAsync(CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap)
        {
            return SinkronHasil.Kosong("Supabase belum dikonfigurasi lengkap.");
        }

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            var baris = await AmbilAntreanAsync(conn, ct).ConfigureAwait(false);
            if (baris.Count == 0)
            {
                return SinkronHasil.Kosong("Antrean kosong.");
            }

            var jalur = await PembuatBerkasSinkron
                .TulisAsync(_folderBerkas, baris)
                .ConfigureAwait(false);

            var berhasil = 0;
            var gagal = 0;
            var dilewati = 0;

            foreach (var b in baris)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.Equals(b.Status, "menunggu", StringComparison.OrdinalIgnoreCase))
                {
                    dilewati++;
                    continue;
                }

                var hasil = await _impor.ImporAsync(b, ct).ConfigureAwait(false);

                if (hasil.Berhasil)
                {
                    berhasil++;
                    await TandaiSinkronAsync(conn, b.Id, hasil.NomorResmi, ct).ConfigureAwait(false);
                }
                else
                {
                    gagal++;
                    await TandaiGagalAsync(conn, b.Id, hasil.PesanGalat, b.BatasCoba, ct).ConfigureAwait(false);
                }
            }

            _logger.LogInformation(
                "Sinkron selesai: {Diambil} diambil, {Berhasil} berhasil, {Gagal} gagal, {Dilewati} dilewati.",
                baris.Count, berhasil, gagal, dilewati);

            return new SinkronHasil(baris.Count, berhasil, gagal, dilewati, jalur, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Putaran sinkron gagal.");
            return SinkronHasil.Kosong($"Sinkron gagal: {ex.Message}");
        }
    }

    private async Task<List<AntreanSuratBaris>> AmbilAntreanAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        var daftar = new List<AntreanSuratBaris>();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, id_pengguna, id_perangkat, id_jenis,
                   nomor_sementara, nik, data_json::text, status, batas_coba,
                   catatan, dibuat_pada, diubah_pada
              FROM antrean_surat
             WHERE tenant_id = @tenant
             ORDER BY dibuat_pada
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.IsDBNull(7) ? "{}" : reader.GetString(7);
            using var doc = JsonDocument.Parse(json);

            daftar.Add(new AntreanSuratBaris(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                doc.RootElement.Clone(),
                reader.GetString(8),
                reader.GetInt16(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDateTime(11),
                reader.GetDateTime(12)));
        }

        return daftar;
    }

    /// <summary>
    /// Baris berhasil: catat di <c>sinkron_log</c> lalu hapus dari antrean.
    /// Penghapusan membebaskan slot batas 10 surat — retensi jejak ada di
    /// <c>sinkron_log</c>, bukan di antrean.
    /// </summary>
    private static async Task TandaiSinkronAsync(
        NpgsqlConnection conn, Guid idAntrean, string? nomorResmi, CancellationToken ct)
    {
        await using var log = new NpgsqlCommand(
            """
            INSERT INTO sinkron_log (tenant_id, id_antrean, nomor_resmi, status, catatan)
            SELECT tenant_id, @id, @nomor, 'berhasil', 'Impor desktop'
              FROM antrean_surat WHERE id = @id
            """, conn);
        log.Parameters.Add(new NpgsqlParameter("id", idAntrean));
        log.Parameters.Add(new NpgsqlParameter("nomor", nomorResmi ?? (object)DBNull.Value));
        await log.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var hapus = new NpgsqlCommand(
            "DELETE FROM antrean_surat WHERE id = @id", conn);
        hapus.Parameters.Add(new NpgsqlParameter("id", idAntrean));
        await hapus.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Baris gagal: naikkan penghitung percobaan. Setelah
    /// <see cref="BatasCobaMaks"/> kali, baris dibuang dari antrean supaya
    /// tidak memblokir slot batas 10 selamanya; penyebabnya tetap tercatat di
    /// <c>sinkron_log</c>.
    /// </summary>
    private static async Task TandaiGagalAsync(
        NpgsqlConnection conn, Guid idAntrean, string? pesan, short batasCoba, CancellationToken ct)
    {
        var sisa = BatasCobaMaks - (batasCoba + 1);

        if (sisa <= 0)
        {
            await using var log = new NpgsqlCommand(
                """
                INSERT INTO sinkron_log (tenant_id, id_antrean, nomor_resmi, status, catatan)
                SELECT tenant_id, @id, NULL, 'gagal', @catatan
                  FROM antrean_surat WHERE id = @id
                """, conn);
            log.Parameters.Add(new NpgsqlParameter("id", idAntrean));
            log.Parameters.Add(new NpgsqlParameter("catatan",
                (object?)($"Dibuang setelah {BatasCobaMaks} percobaan: {pesan}") ?? DBNull.Value));
            await log.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var hapus = new NpgsqlCommand(
                "DELETE FROM antrean_surat WHERE id = @id", conn);
            hapus.Parameters.Add(new NpgsqlParameter("id", idAntrean));
            await hapus.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE antrean_surat
               SET status = 'gagal',
                   catatan = @catatan,
                   batas_coba = batas_coba + 1,
                   diubah_pada = now()
             WHERE id = @id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("id", idAntrean));
        cmd.Parameters.Add(new NpgsqlParameter("catatan", (object?)pesan ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
