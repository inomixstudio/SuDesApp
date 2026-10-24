using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Akun rahasia penyedia (Sumberjaya Dev) yang menjadi KUNCI menu
    /// <b>Pusat Administrasi</b> di aplikasi desa.
    ///
    /// Akun ini hidup hanya di berkas lokal <c>Login.dat</c> (lihat
    /// <see cref="BerkasLogin"/>) — bukan di tabel <c>Pengguna</c> — sehingga:
    /// tidak muncul di halaman Kelola Pengguna, tidak bisa diubah atau dihapus
    /// oleh Administrator desa, dan tidak ikut tercadang beserta data desa.
    ///
    /// Aturan menu (docs/policy-peran-pengguna.md): menu Pusat Administrasi
    /// HANYA ditambahkan ke navigasi bila sesi yang sedang masuk adalah akun ini
    /// (<see cref="SesiIniPusat"/>). Sesi lain tidak melihat menunya sama sekali.
    ///
    /// Kata sandi akun ini TIDAK lagi ditanam di kode. Sandi harus diprovisikan
    /// di mesin yang membutuhkannya, lewat environment variable
    /// <see cref="VariabelSandi"/> atau berkas <see cref="NamaBerkasSandi"/> di
    /// samping executable. Tanpa provisioning, akun pusat tidak bisa dipakai
    /// sama sekali (gagal-tertutup) — lebih baik daripada kata sandi bawaan
    /// yang sama di semua instalasi dan sudah pernah terbit di repo publik.
    ///
    /// Halaman pusat administrasi sendiri tetap meminta kredensial operator
    /// server (<c>Lisensi:AdminOperators</c>) yang tidak pernah ada di aplikasi
    /// desa, jadi akun ini hanya membuka KETERLIHATAN menu.
    /// </summary>
    public static class AkunPusat
    {
        // Nama pengguna bukan rahasia (disebut di dokumentasi), tetapi tetap
        // disimpan terkabur agar tidak muncul sebagai literal di rakitan.
        private const byte KunciNama = 0x5A;

        private static readonly byte[] _ByteNama =
        {
            0x3B, 0x3E, 0x37, 0x33, 0x34, 0x2A, 0x2F, 0x29, 0x3B, 0x2E
        };

        private static string? _NamaTerbuka;

        /// <summary>Buka byte-array terkabur menjadi teks aslinya (XOR + UTF8).</summary>
        private static string Buka(byte[] terkabur, byte kunci)
        {
            var hasil = new byte[terkabur.Length];
            for (var i = 0; i < hasil.Length; i++)
            {
                hasil[i] = (byte)(terkabur[i] ^ kunci);
            }

            return System.Text.Encoding.UTF8.GetString(hasil);
        }

        /// <summary>Nama pengguna akun penyedia — pembuka menu Pusat Administrasi.</summary>
        public static string Username => _NamaTerbuka ??= Buka(_ByteNama, KunciNama);

        /// <summary>Nama tampilan akun penyedia di aplikasi (label menu, bukan rahasia).</summary>
        public const string NamaTampilan = "Pusat Administrasi";

        /// <summary>
        /// Environment variable yang memprovisikan kata sandi akun pusat.
        /// Dipakai lebih dulu sebelum berkas <see cref="NamaBerkasSandi"/>.
        /// </summary>
        public const string VariabelSandi = "SUDES_PUSAT_SANDI";

        /// <summary>Nama berkas provisioning di samping executable (tidak pernah di-commit).</summary>
        public const string NamaBerkasSandi = "Pusat.kunci";

        /// <summary>
        /// Penunjuk lokasi berkas sandi untuk pengujian — pola yang sama dengan
        /// <see cref="BerkasLogin.LokasiOverride"/>. Null = di samping executable.
        /// </summary>
        public static Func<string>? LokasiBerkasSandiOverride { get; set; }

        /// <summary>Lokasi berkas provisioning yang dibaca aplikasi.</summary>
        public static string JalurBerkasSandi =>
            LokasiBerkasSandiOverride?.Invoke()
            ?? Path.Combine(AppContext.BaseDirectory, NamaBerkasSandi);

        /// <summary>
        /// Kata sandi akun pusat yang diprovisikan administrator mesin. String
        /// kosong berarti belum diprovisikan (akun pusat nonaktif).
        /// </summary>
        public static string Sandi => BacaSandi();

        /// <summary>True bila kata sandi akun pusat sudah diprovisikan.</summary>
        public static bool SandiTersedia => Sandi.Length > 0;

        private static string BacaSandi()
        {
            try
            {
                var dariEnvironment = Environment.GetEnvironmentVariable(VariabelSandi);
                if (!string.IsNullOrWhiteSpace(dariEnvironment))
                {
                    return dariEnvironment.Trim();
                }

                var jalur = JalurBerkasSandi;
                if (File.Exists(jalur))
                {
                    var isi = File.ReadAllText(jalur).Trim();
                    if (isi.Length > 0)
                    {
                        return isi;
                    }
                }
            }
            catch (Exception)
            {
                // Provisioning rusak/terbaca gagal = akun pusat nonaktif, bukan
                // alasan menggagalkan startup aplikasi desa.
            }

            return string.Empty;
        }

        /// <summary>
        /// Cocokkan kata sandi yang diketik dengan sandi provisioning.
        /// Perbandingan waktu tetap; layar login sudah menormalkan input ke
        /// huruf kecil, jadi kedua sisi dinormalkan dengan cara yang sama.
        /// </summary>
        public static bool CocokSandi(string? sandi)
        {
            var resmi = Sandi;
            if (resmi.Length == 0 || string.IsNullOrEmpty(sandi))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sandi.Trim().ToLowerInvariant()),
                Encoding.UTF8.GetBytes(resmi.ToLowerInvariant()));
        }

        /// <summary>True bila nama pengguna yang diberikan adalah akun penyedia (tidak peka huruf besar/kecil).</summary>
        public static bool Namanya(string? username) =>
            !string.IsNullOrWhiteSpace(username)
            && username!.Trim().Equals(Username, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True bila sesi yang sedang aktif adalah akun penyedia — satu-satunya
        /// tempat menu Pusat Administrasi boleh muncul.
        ///
        /// Dua syarat, bukan satu: nama pengguna cocok DAN sesi ditandai pusat
        /// oleh jalur login berkas lokal (<c>SessionContext.SesiPusat</c>).
        /// Dengan begitu akun database yang kebetulan diberi nama sama tetap
        /// tidak membuka menu rahasia ini.
        /// </summary>
        public static bool SesiIniPusat =>
            SessionContext.SesiPusat && Namanya(SessionContext.CurrentUser);
    }
}
