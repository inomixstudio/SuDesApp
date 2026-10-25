using System.Collections.Concurrent;
using System.Security.Cryptography;
using SuDesApp.Data.Models;

namespace SuDesApp.Api;

/// <summary>
/// Sesi login backend (dipakai endpoint /sudes/api/v1/sesi).
///
/// Token dibuat acak 32 byte dan hanya hidup di memori proses: restart server
/// berarti semua sesi berakhir (semua orang diminta masuk ulang), yang memang
/// diinginkan untuk aplikasi desa satu-mesin. Kata sandi TIDAK pernah menyentuh
/// lapisan ini selain diteruskan ke <c>PenggunaService.MasukAsync</c> yang sudah
/// memakai PBKDF2 + kunci 3x gagal persis seperti layar login desktop.
///
/// Token disimpan apa adanya (bukan hash) karena tidak pernah ditulis ke disk;
/// kerentanannya setara dengan sesi di memori aplikasi desktop mana pun.
/// </summary>
public sealed class SesiService
{
    /// <summary>Masa berlaku sesi. Kerja desa satu hari, jadi 12 jam tanpa aktivitas tetap wajar.</summary>
    public static readonly TimeSpan Durasi = TimeSpan.FromHours(12);

    /// <summary>Satu sesi yang sedang aktif.</summary>
    public sealed record SesiAktif(string Token, string Username, string NamaTampilan, string Peran, DateTime BerlakuSampai);

    private readonly ConcurrentDictionary<string, SesiAktif> _sesi = new(StringComparer.Ordinal);
    private readonly object _sapu = new();

    /// <summary>Buat sesi baru untuk akun yang baru saja berhasil masuk.</summary>
    public SesiAktif Buat(Pengguna pengguna)
    {
        ArgumentNullException.ThrowIfNull(pengguna);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var sesi = new SesiAktif(
            token,
            pengguna.Username,
            pengguna.NamaTampilan,
            pengguna.Peran,
            DateTime.UtcNow.Add(Durasi));

        _sesi[token] = sesi;
        SapuKadaluarsa();
        return sesi;
    }

    /// <summary>Ambil sesi dari token; null bila tidak dikenal atau sudah kedaluwarsa.</summary>
    public SesiAktif? Ambil(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!_sesi.TryGetValue(token, out var sesi)) return null;

        if (sesi.BerlakuSampai <= DateTime.UtcNow)
        {
            _sesi.TryRemove(token, out _);
            return null;
        }
        return sesi;
    }

    /// <summary>Akhiri sesi (logout). True bila token memang terdaftar.</summary>
    public bool Akhiri(string? token)
        => !string.IsNullOrWhiteSpace(token) && _sesi.TryRemove(token, out _);

    /// <summary>
    /// Akhiri SEMUA sesi milik satu akun; mengembalikan jumlah yang diputus.
    /// Dipakai saat akun dihapus, dinonaktifkan, atau perannya diubah: token
    /// yang sudah terlanjur dipegang tidak boleh tetap berlaku sampai
    /// kedaluwarsa hanya karena sesi hidup di memori proses.
    /// </summary>
    public int AkhiriSemua(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return 0;

        var target = username.Trim();
        var diputus = 0;
        foreach (var (token, sesi) in _sesi)
        {
            if (string.Equals(sesi.Username, target, StringComparison.OrdinalIgnoreCase)
                && _sesi.TryRemove(token, out _))
                diputus++;
        }
        return diputus;
    }

    private void SapuKadaluarsa()
    {
        // Sekali sapuan per pembuatan sesi sudah cukup: jumlah sesi aplikasi
        // desa kecil, dan penguncian singkat di sini lebih murah daripada
        // pembersihan terjadwal yang hidup sebagai timer proses.
        if (_sesi.Count < 64) return;

        lock (_sapu)
        {
            var kadaluarsa = DateTime.UtcNow;
            foreach (var (token, sesi) in _sesi)
                if (sesi.BerlakuSampai <= kadaluarsa)
                    _sesi.TryRemove(token, out _);
        }
    }
}
