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
    /// True bila baris masih boleh diimpor pada putaran ini: baris baru
    /// (<c>menunggu</c>) atau baris yang pernah gagal tetapi percobaannya belum
    /// habis. Tanpa cabang kedua, baris gagal tidak pernah dicoba lagi dan
    /// menempati satu slot batas 10 selamanya.
    /// </summary>
    public static bool PerluDiimpor(AntreanSuratBaris baris)
    {
        if (baris is null) throw new System.ArgumentNullException(nameof(baris));

        if (string.Equals(baris.Status, "menunggu", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(baris.Status, "gagal", StringComparison.OrdinalIgnoreCase)
            && baris.BatasCoba < BatasCobaMaks;
    }

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

                if (!PerluDiimpor(b))
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
                    await TandaiGagalAsync(conn, b, hasil.PesanGalat, ct).ConfigureAwait(false);
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
                   catatan, dibuat_pada, diubah_pada, client_uuid
              FROM antrean_surat
             WHERE tenant_id = @tenant
               AND (status = 'menunggu' OR (status = 'gagal' AND batas_coba < 3))
             ORDER BY dibuat_pada
             LIMIT 100
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                daftar.Add(BacaBaris(reader, adaClientUuid: true));
            }
            return daftar;
        }
        catch (PostgresException ex) when (ex.SqlState == "42703")
        {
            // Migrasi client_uuid (20261111000000) belum diterapkan di
            // Supabase: baca tanpa kolomnya (client_uuid = null) agar
            // sinkron tetap jalan. Idempotensi kolom ini hanya
            // pengaman; ketiadaan kolom bukan alasan putaran gagal.
            _logger.LogWarning(
                ex,
                "Kolom client_uuid tidak ditemukan — membaca antrean tanpa kolom itu. " +
                "Terapkan migrasi 20261111000000_client_uuid_idempotensi.sql di SQL Editor.");
        }

        await using var cmdLama = new NpgsqlCommand(
            """
            SELECT id, tenant_id, id_pengguna, id_perangkat, id_jenis,
                   nomor_sementara, nik, data_json::text, status, batas_coba,
                   catatan, dibuat_pada, diubah_pada
              FROM antrean_surat
             WHERE tenant_id = @tenant
               AND (status = 'menunggu' OR (status = 'gagal' AND batas_coba < 3))
             ORDER BY dibuat_pada
             LIMIT 100
            """, conn);
        cmdLama.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));

        await using var readerLama = await cmdLama.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await readerLama.ReadAsync(ct).ConfigureAwait(false))
        {
            daftar.Add(BacaBaris(readerLama, adaClientUuid: false));
        }

        return daftar;
    }

    private static AntreanSuratBaris BacaBaris(NpgsqlDataReader reader, bool adaClientUuid)
    {
        var json = reader.IsDBNull(7) ? "{}" : reader.GetString(7);
        using var doc = JsonDocument.Parse(json);

        return new AntreanSuratBaris(
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
            reader.GetDateTime(12),
            adaClientUuid && !reader.IsDBNull(13) ? reader.GetGuid(13) : null);
    }

    /// <summary>
    /// Baris berhasil: catat di <c>sinkron_log</c> lalu hapus dari antrean.
    /// Penghapusan membebaskan slot batas 10 surat — retensi jejak ada di
    /// <c>sinkron_log</c>, bukan di antrean.
    /// </summary>
    private static async Task TandaiSinkronAsync(
        NpgsqlConnection conn, Guid idAntrean, string? nomorResmi, CancellationToken ct)
    {
        // Atomik: log + hapus dalam satu transaksi — crash di antaranya
        // tidak lagi membuat baris diimpor ulang.
        await using var transaksi = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var log = new NpgsqlCommand(
                """
                INSERT INTO sinkron_log (tenant_id, id_antrean, nomor_resmi, status, catatan)
                SELECT tenant_id, @id, @nomor, 'berhasil', 'Impor desktop'
                  FROM antrean_surat WHERE id = @id
                """, conn, transaksi);
            log.Parameters.Add(new NpgsqlParameter("id", idAntrean));
            log.Parameters.Add(new NpgsqlParameter("nomor", nomorResmi ?? (object)DBNull.Value));
            await log.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var hapus = new NpgsqlCommand(
                "DELETE FROM antrean_surat WHERE id = @id", conn, transaksi);
            hapus.Parameters.Add(new NpgsqlParameter("id", idAntrean));
            await hapus.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await transaksi.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaksi.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Baris gagal: naikkan penghitung percobaan. Setelah
    /// <see cref="BatasCobaMaks"/> kali, baris dibuang dari antrean supaya
    /// tidak memblokir slot batas 10 selamanya; penyebabnya tetap tercatat di
    /// <c>sinkron_log</c> dan muatannya diarsipkan ke folder
    /// <c>Gagal</c> agar bisa diimpor ulang dari desktop setelah penyebabnya
    /// diperbaiki (tidak perlu meminta Android mengirim ulang, apalagi
    /// mengetik ulang dari PDF). Selama percobaan masih tersisa, statusnya
    /// tetap <c>gagal</c> — <see cref="PerluDiimpor"/> yang membuatnya
    /// dicoba lagi pada putaran berikutnya.
    /// </summary>
    private async Task TandaiGagalAsync(
        NpgsqlConnection conn, AntreanSuratBaris baris, string? pesan, CancellationToken ct)
    {
        var percobaan = baris.BatasCoba + 1;
        var idAntrean = baris.Id;

        if (percobaan >= BatasCobaMaks)
        {
            // Arsipkan dulu supaya operator masih bisa mengimpor ulang
            // dari desktop bila penyebab galatnya sudah diperbaiki.
            try
            {
                await ArsipkanGagalAsync(baris, pesan, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Arsip gagal impor {AntreanId} tidak tersimpan.", idAntrean);
            }

            await using var transaksi = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await using var log = new NpgsqlCommand(
                    """
                    INSERT INTO sinkron_log (tenant_id, id_antrean, nomor_resmi, status, catatan)
                    SELECT tenant_id, @id, NULL, 'gagal', @catatan
                      FROM antrean_surat WHERE id = @id
                    """, conn, transaksi);
                log.Parameters.Add(new NpgsqlParameter("id", idAntrean));
                log.Parameters.Add(new NpgsqlParameter("catatan",
                    (object?)($"Dibuang setelah {BatasCobaMaks} percobaan: {pesan}") ?? DBNull.Value));
                await log.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                await using var hapus = new NpgsqlCommand(
                    "DELETE FROM antrean_surat WHERE id = @id", conn, transaksi);
                hapus.Parameters.Add(new NpgsqlParameter("id", idAntrean));
                await hapus.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                await transaksi.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await transaksi.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
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

    /// <summary>Folder arsip baris gagal (di subfolder <c>Gagal</c> berkas sinkron).</summary>
    internal string FolderArsipGagal => Path.Combine(_folderBerkas, "Gagal");

    /// <summary>Folder hasil impor ulang arsip gagal yang berhasil.</summary>
    internal string FolderArsipSelesai => Path.Combine(_folderBerkas, "Selesai");

    private async Task ArsipkanGagalAsync(AntreanSuratBaris baris, string? pesan, CancellationToken ct)
    {
        Directory.CreateDirectory(FolderArsipGagal);
        var jalur = Path.Combine(FolderArsipGagal, $"{baris.Id}-{baris.IdJenis}.json");
        await File.WriteAllTextAsync(jalur, BangunArsipGagal(baris, pesan), ct).ConfigureAwait(false);
        _logger.LogInformation("Baris gagal {AntreanId} diarsipkan ke {Jalur}.", baris.Id, jalur);
    }

    /// <summary>
    /// Muatan arsip satu baris gagal (JSON) — dipakai impor ulang.
    /// Termasuk `data_json` utuh supaya isian tetap bisa dipulihkan.
    /// </summary>
    internal static string BangunArsipGagal(AntreanSuratBaris baris, string? pesanGalat)
    {
        var arsip = new
        {
            id = baris.Id.ToString(),
            tenant_id = baris.TenantId,
            id_pengguna = baris.IdPengguna,
            id_perangkat = baris.IdPerangkat?.ToString(),
            id_jenis = baris.IdJenis,
            nomor_sementara = baris.NomorSementara,
            nik = baris.Nik,
            data_json = baris.DataJson,
            status = baris.Status,
            batas_coba = baris.BatasCoba,
            catatan = baris.Catatan,
            dibuat_pada = baris.DibuatPada.ToString("O"),
            diubah_pada = baris.DiubahPada.ToString("O"),
            client_uuid = baris.ClientUuid?.ToString(),
            pesan_gagal = pesanGalat,
            diarsipkan_pada = DateTime.Now.ToString("O"),
        };
        return JsonSerializer.Serialize(arsip);
    }

    /// <summary>Baca kembali arsip gagal menjadi baris antrean siap impor.</summary>
    internal static AntreanSuratBaris? BacaArsipGagal(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var akar = doc.RootElement;
            var dataText = akar.TryGetProperty("data_json", out var dj) ? dj.GetRawText() : "{}";
            using var dataDoc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(dataText) ? "{}" : dataText);
            return new AntreanSuratBaris(
                akar.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var gid)
                    ? gid : Guid.NewGuid(),
                AmbilTeks(akar, "tenant_id") ?? string.Empty,
                AmbilTeks(akar, "id_pengguna") ?? string.Empty,
                AmbilTeks(akar, "id_perangkat") is { } pd && Guid.TryParse(pd, out var gpd) ? gpd : null,
                AmbilTeks(akar, "id_jenis") ?? string.Empty,
                AmbilTeks(akar, "nomor_sementara"),
                AmbilTeks(akar, "nik"),
                dataDoc.RootElement.Clone(),
                AmbilTeks(akar, "status") ?? "gagal",
                akar.TryGetProperty("batas_coba", out var bc) ? bc.GetInt16() : (short)BatasCobaMaks,
                AmbilTeks(akar, "catatan") ?? AmbilTeks(akar, "pesan_gagal"),
                AmbilTanggal(akar, "dibuat_pada"),
                AmbilTanggal(akar, "diubah_pada"),
                AmbilTeks(akar, "client_uuid") is { } cu && Guid.TryParse(cu, out var gc) ? gc : null);
        }
        catch
        {
            return null;
        }
    }

    private static string? AmbilTeks(JsonElement el, string nama) =>
        el.TryGetProperty(nama, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static DateTime AmbilTanggal(JsonElement el, string nama) =>
        AmbilTeks(el, nama) is { } t && DateTime.TryParse(t,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : DateTime.Now;

    /// <summary>
    /// Impor ulang arsip gagal lokal (folder <c>Gagal</c>) — supaya surat
    /// yang penyebabnya sudah diperbaiki (mis. skema/validator) bisa masuk
    /// register tanpa Android mengirim ulang. Berkas yang berhasil
    /// dipindah ke <c>Selesai</c>; yang masih gagal dibiarkan di tempat.
    /// </summary>
    public async Task<SinkronHasil> ImporUlangArsipGagalAsync(CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap)
        {
            return SinkronHasil.Kosong("Supabase belum dikonfigurasi lengkap.");
        }

        if (!Directory.Exists(FolderArsipGagal))
        {
            return SinkronHasil.Kosong("Tidak ada arsip gagal untuk diimpor ulang.");
        }

        var berkas = Directory.GetFiles(FolderArsipGagal, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (berkas.Count == 0)
        {
            return SinkronHasil.Kosong("Tidak ada arsip gagal untuk diimpor ulang.");
        }

        var berhasil = 0;
        var gagal = 0;

        NpgsqlConnection? conn = null;
        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            foreach (var jalur in berkas)
            {
                ct.ThrowIfCancellationRequested();

                string teks;
                try
                {
                    teks = await File.ReadAllTextAsync(jalur, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Arsip gagal {Jalur} tidak terbaca.", jalur);
                    gagal++;
                    continue;
                }

                var baris = BacaArsipGagal(teks);
                if (baris is null)
                {
                    _logger.LogWarning("Arsip gagal {Jalur} tidak dikenali formatnya.", jalur);
                    gagal++;
                    continue;
                }

                var hasil = await _impor.ImporAsync(baris, ct).ConfigureAwait(false);
                if (!hasil.Berhasil)
                {
                    gagal++;
                    _logger.LogWarning(
                        "Impor ulang arsip {AntreanId} masih gagal: {Pesan}",
                        baris.Id, hasil.PesanGalat);
                    continue;
                }

                berhasil++;
                try
                {
                    await using var log = new NpgsqlCommand(
                        """
                        INSERT INTO sinkron_log (tenant_id, id_antrean, nomor_resmi, status, catatan)
                        VALUES (@tenant, @id, @nomor, 'berhasil', 'Impor ulang arsip gagal')
                        """, conn!);
                    log.Parameters.Add(new NpgsqlParameter("tenant", baris.TenantId));
                    log.Parameters.Add(new NpgsqlParameter("id", baris.Id));
                    log.Parameters.Add(new NpgsqlParameter("nomor",
                        hasil.NomorResmi ?? (object)DBNull.Value));
                    await log.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Surat sudah masuk register; log hanya jejak audit.
                    _logger.LogWarning(ex, "Log impor ulang {AntreanId} tidak tersimpan.", baris.Id);
                }

                try
                {
                    Directory.CreateDirectory(FolderArsipSelesai);
                    var tujuan = Path.Combine(FolderArsipSelesai, Path.GetFileName(jalur));
                    if (File.Exists(tujuan)) File.Delete(tujuan);
                    File.Move(jalur, tujuan);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Arsip {Jalur} tidak bisa dipindah ke Selesai.", jalur);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impor ulang arsip gagal.");
            return SinkronHasil.Kosong($"Impor ulang gagal: {ex.Message}");
        }
        finally
        {
            conn?.Dispose();
        }

        _logger.LogInformation(
            "Impor ulang arsip selesai: {Berhasil} berhasil, {Gagal} gagal.",
            berhasil, gagal);
        return new SinkronHasil(berkas.Count, berhasil, gagal, 0, FolderArsipSelesai, null);
    }
}
