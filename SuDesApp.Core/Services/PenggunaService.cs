using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>Hasil satu percobaan masuk.</summary>
    public sealed class HasilMasukPengguna
    {
        public bool Berhasil { get; init; }

        /// <summary>Pesan siap tampil untuk pengguna.</summary>
        public string Pesan { get; init; } = string.Empty;

        /// <summary>Akun yang berhasil masuk; null bila gagal.</summary>
        public Pengguna? Pengguna { get; init; }

        /// <summary>True bila kegagalan disebabkan akun sedang terkunci sementara.</summary>
        public bool Terkunci { get; init; }

        public static HasilMasukPengguna Gagal(string pesan, bool terkunci = false) =>
            new() { Berhasil = false, Pesan = pesan, Terkunci = terkunci };

        public static HasilMasukPengguna Sukses(Pengguna pengguna) =>
            new() { Berhasil = true, Pengguna = pengguna, Pesan = "Berhasil masuk." };
    }

    /// <summary>
    /// Akun pengguna aplikasi: masuk, kelola akun, dan aturan keamanan
    /// (kata sandi hash PBKDF2, kunci 5 menit setelah 3 percobaan gagal,
    /// akun administrator terakhir tidak boleh dihapus/dinonaktifkan).
    /// </summary>
    public interface IPenggunaService
    {
        /// <summary>True bila sudah ada minimal satu akun di database.</summary>
        Task<bool> AdaPenggunaAsync(CancellationToken ct = default);

        Task<HasilMasukPengguna> MasukAsync(string username, string kataSandi, CancellationToken ct = default);

        Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default);

        /// <summary>Akun berdasarkan nama pengguna (huruf besar/kecil sama); null bila tidak ada.</summary>
        Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default);

        /// <param name="dariMigrasi">
        /// True saat memindahkan akun dari file Login.dat lama: kata sandi lama
        /// boleh lebih pendek dari aturan baru agar pemilik desa tidak terkunci
        /// dari aplikasinya sendiri.
        /// </param>
        Task<Pengguna> TambahAsync(
            string username, string namaTampilan, string peran, string kataSandi,
            string? oleh = null, bool dariMigrasi = false, CancellationToken ct = default);

        Task UbahAsync(
            int id, string namaTampilan, string peran, bool aktif,
            string? oleh = null, CancellationToken ct = default);

        Task ResetKataSandiAsync(int id, string kataSandiBaru, string? oleh = null, CancellationToken ct = default);

        Task<bool> UbahKataSandiAsync(int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default);

        Task HapusAsync(int id, CancellationToken ct = default);
    }

    public sealed class PenggunaService : IPenggunaService
    {
        /// <summary>Jumlah percobaan gagal sebelum akun terkunci sementara.</summary>
        public const int MaksPercobaanGagal = 3;

        /// <summary>Lama kunci setelah percobaan gagal beruntun.</summary>
        public static readonly TimeSpan DurasiKunci = TimeSpan.FromMinutes(5);

        private static readonly Regex PolaUsername = new(@"^[a-z0-9][a-z0-9._-]{2,29}$");

        private readonly IPenggunaRepository _repo;
        private readonly ActivityLogService? _audit;
        private readonly ILogger<PenggunaService> _logger;
        private readonly Func<DateTime> _sekarang;

        public PenggunaService(
            IPenggunaRepository repo,
            ActivityLogService? audit = null,
            ILogger<PenggunaService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _audit = audit;
            _logger = logger ?? NullLogger<PenggunaService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public async Task<bool> AdaPenggunaAsync(CancellationToken ct = default)
            => await _repo.HitungAsync(ct).ConfigureAwait(false) > 0;

        public async Task<HasilMasukPengguna> MasukAsync(string username, string kataSandi, CancellationToken ct = default)
        {
            var akun = await _repo.GetByUsernameAsync(username, ct).ConfigureAwait(false);

            // Pesan untuk akun tidak ada dan kata sandi salah sengaja SAMA:
            // agar orang tidak bisa memakai layar login untuk menebak daftar akun.
            if (akun == null)
                return HasilMasukPengguna.Gagal("Nama pengguna atau kata sandi salah.");

            if (!akun.Aktif)
                return HasilMasukPengguna.Gagal("Akun ini dinonaktifkan. Hubungi administrator desa.");

            var sekarang = _sekarang();

            if (akun.TerkunciSampai.HasValue && akun.TerkunciSampai.Value > sekarang)
            {
                var sisa = Math.Max(1, (int)Math.Ceiling((akun.TerkunciSampai.Value - sekarang).TotalMinutes));
                return HasilMasukPengguna.Gagal(
                    $"Terlalu banyak percobaan gagal. Coba lagi dalam {sisa} menit.", terkunci: true);
            }

            if (!KataSandiPengguna.Cocok(kataSandi, akun.Salt, akun.Hash, akun.Iterasi))
            {
                int gagal = akun.GagalLogin + 1;
                DateTime? terkunciSampai = gagal >= MaksPercobaanGagal ? sekarang + DurasiKunci : null;

                await _repo.CatatPercobaanMasukAsync(akun.ID, gagal, terkunciSampai, null, ct).ConfigureAwait(false);

                _logger.LogWarning("Percobaan masuk gagal untuk {Username} (ke-{Gagal}).", akun.Username, gagal);
                _audit?.Log("Pengguna", akun.Username, "Login gagal", $"percobaan ke-{gagal}");

                return terkunciSampai.HasValue
                    ? HasilMasukPengguna.Gagal(
                        $"Terlalu banyak percobaan gagal. Akun terkunci {DurasiKunci.TotalMinutes:0} menit.", terkunci: true)
                    : HasilMasukPengguna.Gagal("Nama pengguna atau kata sandi salah.");
            }

            await _repo.CatatPercobaanMasukAsync(akun.ID, 0, null, sekarang, ct).ConfigureAwait(false);
            akun.GagalLogin = 0;
            akun.TerkunciSampai = null;
            akun.LoginTerakhir = sekarang;

            _logger.LogInformation("Pengguna {Username} berhasil masuk ({Peran}).", akun.Username, akun.Peran);
            _audit?.Log("Pengguna", akun.Username, "Login", PeranPengguna.Tampilan(akun.Peran));

            return HasilMasukPengguna.Sukses(akun);
        }

        public Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default)
            => _repo.GetAllAsync(ct);

        public Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default)
            => _repo.GetByUsernameAsync(username, ct);

        public async Task<Pengguna> TambahAsync(
            string username, string namaTampilan, string peran, string kataSandi,
            string? oleh = null, bool dariMigrasi = false, CancellationToken ct = default)
        {
            // Membuat akun menuntut izin KelolaPengguna. Panggilan migrasi akun
            // lama (dariMigrasi: true) dikecualikan: itu jalur pemulihan pemilik
            // desa yang dijalankan layar login sebelum sesi pengguna ada.
            if (!dariMigrasi) SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

            var rapiUsername = (username ?? string.Empty).Trim().ToLowerInvariant();

            if (!PolaUsername.IsMatch(rapiUsername))
                throw new InvalidOperationException(
                    "Nama pengguna harus 3–30 karakter, hanya huruf kecil/angka/titik/garis bawah/garis hubung, dan diawali huruf atau angka.");

            if (string.IsNullOrWhiteSpace(namaTampilan))
                throw new InvalidOperationException("Nama lengkap wajib diisi.");

            if (!PeranPengguna.Valid(peran))
                throw new InvalidOperationException("Peran yang dipilih tidak dikenal.");

            var temuan = KataSandiPengguna.Validasi(kataSandi, rapiUsername);

            // Akun hasil migrasi dari file lama boleh memakai kata sandi pendek;
            // pengguna tetap diminta menggantinya saat membuka kelola pengguna.
            if (dariMigrasi)
                temuan = temuan.Where(t => !t.Contains("minimal", StringComparison.OrdinalIgnoreCase)).ToList();

            if (temuan.Count > 0)
                throw new InvalidOperationException(string.Join(" ", temuan));

            if (await _repo.GetByUsernameAsync(rapiUsername, ct).ConfigureAwait(false) != null)
                throw new InvalidOperationException($"Nama pengguna '{rapiUsername}' sudah dipakai akun lain.");

            var salt = KataSandiPengguna.BuatSalt();
            var data = new Pengguna
            {
                Username = rapiUsername,
                NamaTampilan = namaTampilan.Trim(),
                Peran = PeranPengguna.Normalisasi(peran),
                Salt = salt,
                Hash = KataSandiPengguna.HitungHash(kataSandi, salt),
                Iterasi = KataSandiPengguna.IterasiBawaan,
                Aktif = true,
                DibuatOleh = oleh,
                DiperbaruiOleh = oleh,
                CreatedAt = _sekarang(),
                UpdatedAt = _sekarang()
            };

            data.ID = await _repo.InsertAsync(data, ct).ConfigureAwait(false);

            _logger.LogInformation("Akun pengguna {Username} dibuat oleh {Oleh}.", data.Username, oleh ?? "sistem");
            _audit?.Log("Pengguna", data.Username, "Dibuat", $"{PeranPengguna.Tampilan(data.Peran)} oleh {oleh ?? "sistem"}");

            return data;
        }

        public async Task UbahAsync(
            int id, string namaTampilan, string peran, bool aktif,
            string? oleh = null, CancellationToken ct = default)
        {
            SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

            var akun = await _repo.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Akun tidak ditemukan.");

            if (string.IsNullOrWhiteSpace(namaTampilan))
                throw new InvalidOperationException("Nama lengkap wajib diisi.");

            if (!PeranPengguna.Valid(peran))
                throw new InvalidOperationException("Peran yang dipilih tidak dikenal.");

            var peranBaru = PeranPengguna.Normalisasi(peran);

            // Desa tidak boleh terkunci dari pengelolaannya sendiri: akun
            // administrator aktif terakhir tidak boleh diturunkan perannya,
            // dinonaktifkan, atau dihapus.
            bool turunDariAdmin = akun.Peran == PeranPengguna.Administrator
                && (peranBaru != PeranPengguna.Administrator || !aktif);
            if (turunDariAdmin && await HitungAdministratorAktifAsync(ct).ConfigureAwait(false) <= 1)
                throw new InvalidOperationException(
                    "Ini satu-satunya akun Administrator yang aktif. Buat atau aktifkan Administrator lain lebih dulu.");

            akun.NamaTampilan = namaTampilan.Trim();
            akun.Peran = peranBaru;
            akun.Aktif = aktif;
            akun.DiperbaruiOleh = oleh;

            if (!await _repo.UpdateAsync(akun, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Perubahan akun tidak tersimpan.");

            _audit?.Log("Pengguna", akun.Username, "Diubah",
                $"{PeranPengguna.Tampilan(akun.Peran)}, {(aktif ? "aktif" : "nonaktif")} oleh {oleh ?? "sistem"}");
        }

        public async Task ResetKataSandiAsync(int id, string kataSandiBaru, string? oleh = null, CancellationToken ct = default)
        {
            SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

            var akun = await _repo.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Akun tidak ditemukan.");

            var temuan = KataSandiPengguna.Validasi(kataSandiBaru, akun.Username);
            if (temuan.Count > 0)
                throw new InvalidOperationException(string.Join(" ", temuan));

            var salt = KataSandiPengguna.BuatSalt();
            var hash = KataSandiPengguna.HitungHash(kataSandiBaru, salt);

            if (!await _repo.UpdateKataSandiAsync(id, salt, hash, KataSandiPengguna.IterasiBawaan, oleh, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Kata sandi baru tidak tersimpan.");

            _logger.LogInformation("Kata sandi akun {Username} direset oleh {Oleh}.", akun.Username, oleh ?? "sistem");
            _audit?.Log("Pengguna", akun.Username, "Reset kata sandi", $"oleh {oleh ?? "sistem"}");
        }

        public async Task<bool> UbahKataSandiAsync(int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default)
        {
            var akun = await _repo.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Akun tidak ditemukan.");

            if (!KataSandiPengguna.Cocok(kataSandiLama, akun.Salt, akun.Hash, akun.Iterasi))
                throw new InvalidOperationException("Kata sandi lama salah.");

            var temuan = KataSandiPengguna.Validasi(kataSandiBaru, akun.Username);
            if (temuan.Count > 0)
                throw new InvalidOperationException(string.Join(" ", temuan));

            var salt = KataSandiPengguna.BuatSalt();
            var hash = KataSandiPengguna.HitungHash(kataSandiBaru, salt);

            if (!await _repo.UpdateKataSandiAsync(id, salt, hash, KataSandiPengguna.IterasiBawaan, akun.Username, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Kata sandi baru tidak tersimpan.");

            _audit?.Log("Pengguna", akun.Username, "Ubah kata sandi", null);
            return true;
        }

        public async Task HapusAsync(int id, CancellationToken ct = default)
        {
            SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

            var akun = await _repo.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Akun tidak ditemukan.");

            if (akun.Peran == PeranPengguna.Administrator
                && await HitungAdministratorAktifAsync(ct).ConfigureAwait(false) <= 1)
                throw new InvalidOperationException(
                    "Akun Administrator aktif terakhir tidak boleh dihapus. Buat Administrator lain lebih dulu.");

            if (!await _repo.HapusAsync(id, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Akun tidak terhapus.");

            _logger.LogWarning("Akun pengguna {Username} dihapus.", akun.Username);
            _audit?.Log("Pengguna", akun.Username, "Dihapus", PeranPengguna.Tampilan(akun.Peran));
        }

        private async Task<int> HitungAdministratorAktifAsync(CancellationToken ct)
        {
            var semua = await _repo.GetAllAsync(ct).ConfigureAwait(false);
            return semua.Count(p => p.Aktif && p.Peran == PeranPengguna.Administrator);
        }
    }
}
