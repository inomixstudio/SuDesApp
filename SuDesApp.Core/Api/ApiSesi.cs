using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SuDesApp.Api
{
    /// <summary>
    /// Sesi login untuk pemanggil API (mis. aplikasi Android) setelah memakai
    /// akun yang sama dengan layar masuk desktop.
    ///
    /// Token disimpan di memori proses, bukan di berkas: listener API ikut mati
    /// bersama aplikasi desktop, dan sesi yang tersisa setelah aplikasi ditutup
    /// justru berbahaya. Token dibuat dari 32 bita acak kriptografis sehingga
    /// tidak bisa ditebak; yang disimpan hanya token, bukan kata sandi.
    /// </summary>
    public sealed class ApiSesi
    {
        /// <summary>Masa berlaku satu sesi. Sama dengan sesi backend web (12 jam).</summary>
        public static readonly TimeSpan MasaBerlaku = TimeSpan.FromHours(12);

        /// <summary>Satu sesi aktif: siapa yang masuk dan sampai kapan.</summary>
        public sealed record Sesi(
            string Token,
            string Username,
            string Nama,
            string Peran,
            DateTime BerlakuSampai);

        private readonly ConcurrentDictionary<string, Sesi> _sesi = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _sekarang;

        /// <param name="sekarang">
        /// Sumber waktu; disuntikkan supaya kedaluwarsa bisa diuji tanpa menunggu 12 jam.
        /// </param>
        public ApiSesi(Func<DateTime>? sekarang = null)
        {
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        /// <summary>Jumlah sesi yang masih berlaku (untuk uji dan pemantauan).</summary>
        public int Jumlah
        {
            get
            {
                BersihkanKedaluwarsa();
                return _sesi.Count;
            }
        }

        /// <summary>Buat sesi baru untuk pengguna yang baru lolos pemeriksaan sandi.</summary>
        public Sesi Buat(string username, string nama, string peran)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var sesi = new Sesi(
                token,
                username ?? string.Empty,
                string.IsNullOrWhiteSpace(nama) ? username ?? string.Empty : nama,
                peran ?? string.Empty,
                _sekarang().Add(MasaBerlaku));

            _sesi[token] = sesi;
            return sesi;
        }

        /// <summary>
        /// Sesi yang cocok dengan token, atau null bila token kosong, tidak
        /// dikenal, atau sudah kedaluwarsa. Token kedaluwarsa langsung dibuang.
        /// </summary>
        public Sesi? Ambil(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            if (!_sesi.TryGetValue(token!.Trim(), out var sesi)) return null;

            if (sesi.BerlakuSampai <= _sekarang())
            {
                _sesi.TryRemove(sesi.Token, out _);
                return null;
            }

            return sesi;
        }

        /// <summary>Hentikan satu sesi (keluar). True bila token memang sedang aktif.</summary>
        public bool Akhiri(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            return _sesi.TryRemove(token!.Trim(), out _);
        }

        /// <summary>Hentikan semua sesi milik satu pengguna (mis. sandi direset).</summary>
        public int AkhiriSemua(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return 0;

            var jumlah = 0;
            foreach (var pasangan in _sesi)
            {
                if (!string.Equals(pasangan.Value.Username, username, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_sesi.TryRemove(pasangan.Key, out _)) jumlah++;
            }
            return jumlah;
        }

        private void BersihkanKedaluwarsa()
        {
            var sekarang = _sekarang();
            foreach (var pasangan in _sesi)
            {
                if (pasangan.Value.BerlakuSampai <= sekarang)
                    _sesi.TryRemove(pasangan.Key, out _);
            }
        }
    }
}
