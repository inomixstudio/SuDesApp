using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Supabase;

namespace SuDesApp.Sinkron;

/// <summary>Hasil perangkaian satu perangkat Android.</summary>
/// <param name="Berhasil">True bila perangkat benar-benar dibuat di Supabase.</param>
/// <param name="PerangkatId">Id perangkat (dipakai untuk mencabut).</param>
/// <param name="Surel">Surel login perangkat — hanya untuk log; TIDAK pernah masuk QR.</param>
/// <param name="KataSandi">Kata sandi perangkat — hanya disimpan di pasangan_sementara; TIDAK pernah masuk QR.</param>
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
/// lalu mencatat perangkatnya.
///
/// <para>
/// QR HANYA memuat URL project dan kode verifikasi 4 angka. Surel dan kata sandi
/// perangkat tidak pernah tampil di layar maupun di dalam QR; ponsel menukar
/// kodenya lewat RPC <c>public.ambil_pasangan</c> (sekali pakai, dibatasi
/// percobaan). Kata sandi dibuat desktop supaya cukup kuat dan tidak perlu
/// diketik siapa pun.
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

    /// <summary>
    /// Buat kata sandi acak yang aman untuk perangkat baru.
    ///
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator.GetInt32(int)"/>
    /// dipakai alih-alih mereduksi byte acak dengan modulo: 256 tidak habis
    /// dibagi 56, sehingga sisa byte membuat sebagian karakter lebih mungkin
    /// muncul daripada yang lain (bias) pada sandi yang persis 20 karakter.
    /// </summary>
    public static string BuatKataSandi(int panjang = 20)
    {
        const string huruf = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var hasil = new char[panjang];
        for (var i = 0; i < panjang; i++)
        {
            hasil[i] = huruf[System.Security.Cryptography.RandomNumberGenerator.GetInt32(huruf.Length)];
        }
        return new string(hasil);
    }

    /// <summary>
    /// Kode verifikasi 4 angka. Angka acak per digit; nol di depan diperbolehkan
    /// karena kode dibaca manusia, bukan diuraikan sebagai bilangan.
    /// </summary>
    public static string BuatKode()
    {
        // Sama seperti BuatKataSandi: 256 % 10 != 0, jadi byte acak per digit
        // memberi angka 0-5 sedikit lebih sering daripada 6-9. Ruang kode hanya
        // 10.000 dan penebakannya dibatasi di server, tetapi tidak ada alasan
        // meninggalkan bias yang bisa dihindari satu baris.
        var hasil = new System.Text.StringBuilder(4);
        for (var i = 0; i < 4; i++)
        {
            hasil.Append(System.Security.Cryptography.RandomNumberGenerator.GetInt32(10));
        }
        return hasil.ToString();
    }

    /// <summary>Hasil pemeriksaan URL project yang akan masuk baris pertama QR.</summary>
    /// <param name="Layak">True bila kode boleh diterbitkan untuk URL ini.</param>
    /// <param name="Url">Bentuk bersih <c>https://&lt;ref&gt;.supabase.co</c>, atau null.</param>
    /// <param name="PesanGalat">Penjelasan untuk operator bila tidak layak.</param>
    public sealed record HasilUrlProject(bool Layak, string? Url, string? PesanGalat);

    /// <summary>
    /// Bersihkan dan periksa nilai <c>Supabase:UrlRest</c> sebelum dipakai sebagai
    /// baris pertama kode QR.
    ///
    /// <para>
    /// Namanya menyesatkan: ponsel tidak memakai nilai ini sebagai basis REST,
    /// melainkan menempelkan sendiri <c>/rest/v1</c> dan <c>/auth/v1</c> padanya
    /// (lihat <c>SupabaseKlien</c>), dan <c>LoginActivity</c> menolak QR yang
    /// skemanya bukan https atau yang host-nya tidak diakhiri <c>.supabase.co</c>.
    /// Karena itu nilai <c>https://ref.supabase.co/rest/v1</c> — yang paling
    /// mungkin ditulis operator — menghasilkan kode yang sudah terlanjur dibuat
    /// (akun Auth + baris perangkat) tetapi tidak bisa ditebus ponsel.
    /// </para>
    /// <para>
    /// Kesalahan bentuk basis REST/Auth dimaafkan dan dipulihkan ke project root;
    /// bentuk lain ditolak dengan pesan yang menyebut kunci konfigurasi yang
    /// harus diisi, dan penolakan terjadi SEBELUM ada kerja ke Supabase.
    /// </para>
    /// </summary>
    public static HasilUrlProject PeriksaUrlProject(string? nilai)
    {
        var mentah = nilai?.Trim() ?? string.Empty;
        if (mentah.Length == 0)
        {
            return new HasilUrlProject(false, null,
                "Supabase:UrlRest belum diisi. Isi URL project Supabase (https://<ref>.supabase.co) pada bagian Supabase di appsettings.json, lalu jalankan ulang aplikasi.");
        }

        if (!System.Uri.TryCreate(mentah, System.UriKind.Absolute, out var uri)
            || uri is null
            || !string.Equals(uri.Scheme, System.Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return new HasilUrlProject(false, null,
                $"Supabase:UrlRest harus URL https lengkap, mis. https://<ref>.supabase.co — yang terisi: \"{mentah}\".");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Equals("/rest/v1", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/auth/v1", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/rest", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/auth", StringComparison.OrdinalIgnoreCase))
        {
            path = string.Empty;
        }

        if (path.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return new HasilUrlProject(false, null,
                $"Supabase:UrlRest cukup URL project tanpa path, mis. https://{uri.Host} — yang terisi: \"{mentah}\".");
        }

        if (!uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
        {
            return new HasilUrlProject(false, null,
                $"Host pada Supabase:UrlRest harus project Supabase (*.supabase.co) karena aplikasi Android menolak QR di luar host itu — yang terisi: \"{uri.Host}\".");
        }

        return new HasilUrlProject(true, $"https://{uri.Host}", null);
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

        // Diperiksa di sini, bukan di halaman: tanpa URL yang layak, kode yang
        // terbit pasti tidak bisa ditebus ponsel, sementara perangkat dan akun
        // Auth-nya sudah terlanjur dibuat.
        var urlQr = PeriksaUrlProject(_pengaturan.UrlRest);
        if (!urlQr.Layak)
        {
            return new HasilPerangkaian(false, null, null, null, urlQr.PesanGalat, null);
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
                if (string.IsNullOrWhiteSpace(surel))
                {
                    // pasangan_sementara.email ber-NOT-NULL dan ponsel memakai nilai
                    // itu untuk masuk; menyimpan string kosong berarti menerbitkan
                    // kode yang pasti gagal. Rollback ikut menghapus akun Auth dan
                    // baris perangkat yang baru dibuat RPC (transaksi yang sama).
                    await transaksi.RollbackAsync(ct).ConfigureAwait(false);
                    return new HasilPerangkaian(false, deviceId, null, null,
                        "Perangkat dibuat tetapi surel login tidak dikembalikan Supabase; kode tidak diterbitkan.", null);
                }

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
