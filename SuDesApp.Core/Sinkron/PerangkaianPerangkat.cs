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
/// <param name="Kode">Kode verifikasi 4 angka yang ditunjukkan operator Android.</param>
public sealed record HasilPerangkaian(
    bool Berhasil,
    Guid? PerangkatId,
    string? Surel,
    string? KataSandi,
    string? PesanGalat,
    string? Kode);

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

    /// <summary>
    /// Kode verifikasi 4 angka. Angka acak per digit; nol di depan diperbolehkan
    /// karena kode dibaca manusia, bukan diuraikan sebagai bilangan.
    /// </summary>
    public static string BuatKode()
    {
        using var acak = System.Security.Cryptography.RandomNumberGenerator.Create();
        var buffer = new byte[4];
        acak.GetBytes(buffer);
        var hasil = new System.Text.StringBuilder(4);
        for (var i = 0; i < 4; i++)
        {
            hasil.Append((char)('0' + (buffer[i] % 10)));
        }
        return hasil.ToString();
    }

    /// <summary>
    /// Nama otomatis perangkat bila operator tidak mengisi nama
    /// (mis. "Android 0803-143022"). Cukup unik untuk dikenali di daftar
    /// dan status bar; operator tidak perlu mengetik apa pun.
    /// </summary>
    public static string BuatNamaOtomatis() =>
        $"Android {DateTime.Now:ddMM-HHmmss}";

    /// <summary>Pasang perangkat baru. Tidak pernah melempar.</summary>
    public async Task<HasilPerangkaian> PasangAsync(
        string nama,
        string? idPengguna = null,
        CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap)
        {
            return new HasilPerangkaian(false, null, null, null, "Supabase belum dikonfigurasi lengkap.", null);
        }

        var sandi = BuatKataSandi();
        var pengguna = string.IsNullOrWhiteSpace(idPengguna) ? _pengaturan.Tenant : idPengguna!;
        var namaEfektif = string.IsNullOrWhiteSpace(nama) ? BuatNamaOtomatis() : nama.Trim();

        try
        {
            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            await using var transaksi = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            Guid deviceId;
            string? surel;
            string? kode = null;
            try
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT public.pasang_perangkat_android(@tenant, @pengguna, @nama, @sandi)", conn, transaksi);
                cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
                cmd.Parameters.Add(new NpgsqlParameter("pengguna", pengguna));
                cmd.Parameters.Add(new NpgsqlParameter("nama", namaEfektif));
                cmd.Parameters.Add(new NpgsqlParameter("sandi", sandi));

                var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (hasil is not Guid idBaru)
                {
                    await transaksi.RollbackAsync(ct).ConfigureAwait(false);
                    return new HasilPerangkaian(false, null, null, null, "Supabase tidak mengembalikan id perangkat.", null);
                }
                deviceId = idBaru;

                // Satu perangkat aktif per desa — dalam transaksi yang sama
                // sehingga tidak ada jendela dua perangkat aktif bersamaan.
                try
                {
                    await using var cmdLain = new NpgsqlCommand(
                        "SELECT public.nonaktifkan_perangkat_lain(@tenant, @baru)", conn, transaksi);
                    cmdLain.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
                    cmdLain.Parameters.Add(new NpgsqlParameter("baru", deviceId));
                    var dimatikan = await cmdLain.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (dimatikan is int jumlah && jumlah > 0)
                    {
                        _logger.LogInformation(
                            "{Jumlah} perangkat lama dinonaktifkan; hanya {PerangkatId} yang aktif.",
                            jumlah, deviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Perangkat lain gagal dinonaktifkan; aturan satu perangkat belum berlaku.");
                }

                surel = await AmbilSurelAsync(conn, deviceId, ct, transaksi).ConfigureAwait(false);

                // Bersihkan kadaluwarsa di dalam transaksi yang sama.
                await using (var bersih = new NpgsqlCommand(
                    "SELECT public.bersihkan_pasangan_kedaluwarsa()", conn, transaksi))
                {
                    await bersih.ExecuteScalarAsync(ct).ConfigureAwait(false);
                }

                for (var usaha = 0; usaha < 5 && kode == null; usaha++)
                {
                    var kandidat = BuatKode();
                    await using var cmdKode = new NpgsqlCommand(
                        """
                        INSERT INTO pasangan_sementara (kode, tenant_id, email, kata_sandi, kedaluwarsa_pada)
                        VALUES (@kode, @tenant, @email, @sandi, now() + interval '10 minutes')
                        ON CONFLICT (kode) DO NOTHING
                        """, conn, transaksi);
                    cmdKode.Parameters.Add(new NpgsqlParameter("kode", kandidat));
                    cmdKode.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
                    cmdKode.Parameters.Add(new NpgsqlParameter("email", surel ?? string.Empty));
                    cmdKode.Parameters.Add(new NpgsqlParameter("sandi", sandi));

                    if (await cmdKode.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0)
                    {
                        kode = kandidat;
                    }
                }

                if (kode == null)
                {
                    await transaksi.RollbackAsync(ct).ConfigureAwait(false);
                    return new HasilPerangkaian(false, deviceId, surel, sandi,
                        "Gagal membuat kode verifikasi. Coba lagi.", null);
                }

                await transaksi.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await transaksi.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }

            _logger.LogInformation(
                "Perangkat {PerangkatId} dipasang untuk {Pengguna}; kode {Kode} terbit.",
                deviceId, pengguna, kode);

            return new HasilPerangkaian(true, deviceId, surel, sandi, null, kode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal memasang perangkat {Nama}.", namaEfektif);
            return new HasilPerangkaian(false, null, null, null, ex.Message, null);
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
        NpgsqlConnection conn, Guid perangkatId, CancellationToken ct, System.Data.Common.DbTransaction? transaksi = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT u.email
              FROM perangkat_android p
              JOIN auth.users u ON u.id = p.supabase_user_id
             WHERE p.id = @id
            """, conn, transaksi as NpgsqlTransaction);
        cmd.Parameters.Add(new NpgsqlParameter("id", perangkatId));
        var hasil = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return hasil as string;
    }
}
