using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Supabase;

namespace SuDesApp.Sinkron;

/// <summary>Hasil perangkaian satu perangkat Android.</summary>
/// <param name="Berhasil">True bila perangkat benar-benar dibuat di Supabase.</param>
/// <param name="PerangkatId">Id perangkat (dipakai untuk mencabut).</param>
/// <param name="Surel">Surel login perangkat — dipakai menyusun QR.</param>
/// <param name="KataSandi">Kata sandi perangkat — dipakai menyusun QR.</param>
/// <param name="PesanGalat">Penyebab gagal, bila gagal.</param>
public sealed record HasilPerangkaian(
    bool Berhasil,
    Guid? PerangkatId,
    string? Surel,
    string? KataSandi,
    string? PesanGalat);

/// <summary>Ringkasan satu perangkat yang terpasang.</summary>
public sealed record PerangkatTerpasang(
    Guid Id,
    string Nama,
    string IdPengguna,
    bool Aktif,
    DateTime DipasangPada,
    DateTime? TerakhirOnline);

/// <summary>
/// Perangkaian perangkat Android: membuat akun Supabase Auth + klaim tenant,
/// lalu mencatat perangkatnya. QR berisi URL Supabase, surel, dan kata sandi.
///
/// <para>
/// Kata sandi dibuat desktop (bukan dipilih pengguna) supaya cukup kuat dan
/// tetap bisa ditampilkan sekali di QR. QR hanya diperlihatkan di kantor desa.
/// </para>
/// </summary>
public sealed class PerangkaianPerangkat
{
    private readonly SupabasePengaturan _pengaturan;
    private readonly ILogger<PerangkaianPerangkat> _logger;

    public PerangkaianPerangkat(
        SupabasePengaturan pengaturan,
        ILogger<PerangkaianPerangkat> logger)
    {
        _pengaturan = pengaturan ?? throw new System.ArgumentNullException(nameof(pengaturan));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <summary>Buat kata sandi acak yang aman untuk perangkat baru.</summary>
    public static string BuatKataSandi(int panjang = 20)
    {
        const string huruf = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        using var acak = System.Security.Cryptography.RandomNumberGenerator.Create();
        var hasil = new char[panjang];
        var buffer = new byte[panjang];
        acak.GetBytes(buffer);
        for (var i = 0; i < panjang; i++)
        {
            hasil[i] = huruf[buffer[i] % huruf.Length];
        }
        return new string(hasil);
    }

    /// <summary>Pasang perangkat baru. Tidak pernah melempar.</summary>
    public async Task<HasilPerangkaian> PasangAsync(
        string nama,
        string? idPengguna = null,
        CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap)
        {
            return new HasilPerangkaian(false, null, null, null, "Supabase belum dikonfigurasi lengkap.");
        }

        var sandi = BuatKataSandi();
        var pengguna = string.IsNullOrWhiteSpace(idPengguna) ? _pengaturan.Tenant : idPengguna!;

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                "SELECT public.pasang_perangkat_android(@tenant, @pengguna, @nama, @sandi)", conn);
            cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
            cmd.Parameters.Add(new NpgsqlParameter("pengguna", pengguna));
            cmd.Parameters.Add(new NpgsqlParameter("nama", nama));
            cmd.Parameters.Add(new NpgsqlParameter("sandi", sandi));

            var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (hasil is not Guid deviceId)
            {
                return new HasilPerangkaian(false, null, null, null, "Supabase tidak mengembalikan id perangkat.");
            }

            var surel = await AmbilSurelAsync(conn, deviceId, ct).ConfigureAwait(false);

            _logger.LogInformation("Perangkat {PerangkatId} dipasang untuk {Pengguna}.", deviceId, pengguna);
            return new HasilPerangkaian(true, deviceId, surel, sandi, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal memasang perangkat {Nama}.", nama);
            return new HasilPerangkaian(false, null, null, null, ex.Message);
        }
    }

    /// <summary>Cabut perangkat: hapus antreannya, rekaman perangkat, dan akun Auth.</summary>
    public async Task<bool> CabutAsync(Guid perangkatId, CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap) return false;

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT public.cabut_perangkat_android(@id)", conn);
            cmd.Parameters.Add(new NpgsqlParameter("id", perangkatId));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Perangkat {PerangkatId} dicabut.", perangkatId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mencabut perangkat {PerangkatId}.", perangkatId);
            return false;
        }
    }

    /// <summary>Daftar perangkat tenant ini. Tidak pernah melempar.</summary>
    public async Task<List<PerangkatTerpasang>> AmbilDaftarAsync(CancellationToken ct = default)
    {
        var daftar = new List<PerangkatTerpasang>();
        if (!_pengaturan.Lengkap) return daftar;

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT id, nama, id_pengguna, aktif, dipasang_pada, terakhir_online
                  FROM perangkat_android
                 WHERE tenant_id = @tenant
                 ORDER BY dipasang_pada DESC
                """, conn);
            cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                daftar.Add(new PerangkatTerpasang(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetDateTime(4),
                    reader.IsDBNull(5) ? null : reader.GetDateTime(5)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AmbilDaftarAsync gagal — daftar dibiarkan kosong.");
        }

        return daftar;
    }

    private static async Task<string?> AmbilSurelAsync(
        NpgsqlConnection conn, Guid perangkatId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT u.email
              FROM perangkat_android p
              JOIN auth.users u ON u.id = p.supabase_user_id
             WHERE p.id = @id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter("id", perangkatId));
        var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return hasil as string;
    }
}
