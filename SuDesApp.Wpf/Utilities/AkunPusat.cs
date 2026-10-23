using System;
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
    /// Catatan keamanan yang disengaja pemilik produk: nama pengguna dan kata
    /// sandi bawaan ikut tersimpan di kode, tetapi dalam bentuk TERKABUR
    /// (byte-array XOR yang didekode saat properti pertama kali dibaca) supaya
    /// scan strings sederhana pada binari tidak langsung menemukannya. Ini
    /// obfuskasi, bukan enkripsi: hasilnya fungsi tunggal dari byte-array dan
    /// kunci yang sama-sama ada di rakitan, jadi pihak yang membongkar kode
    /// tetap bisa menghitung mundur. Perlindungan yang dibawa akun ini tetap
    /// pada KETERLIHATAN menu; halaman pusat administrasi sendiri tetap meminta
    /// kredensial operator server (<c>Lisensi:AdminOperators</c>) yang tidak pernah ada di
    /// aplikasi desa.
    /// </summary>
    public static class AkunPusat
    {
        // ---------- kredensial terkabur ----------

        // Nilai asli TIDAK pernah muncul sebagai literal string di rakitan:
        // byte-array di bawah adalah hasil XOR tiap karakter dengan kuncinya,
        // dan hanya dibuka fikir-akhir saat properti pertama kali dibaca.
        //
        //   _ByteNama  = "a..zt"  ^ KunciNama   (10 byte)
        //   _ByteSandi = "a..zt11" ^ KunciSandi  (12 byte)
        private const byte KunciNama = 0x5A;
        private const byte KunciSandi = 0x6C;

        private static readonly byte[] _ByteNama =
        {
            0x3B, 0x3E, 0x37, 0x33, 0x34, 0x2A, 0x2F, 0x29, 0x3B, 0x2E
        };

        private static readonly byte[] _ByteSandi =
        {
            0x0D, 0x08, 0x01, 0x05, 0x02, 0x1C, 0x19, 0x1F, 0x0D, 0x18, 0x5D, 0x5D
        };

        private static string? _NamaTerbuka;
        private static string? _SandiTerbuka;

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

        /// <summary>Nama pengguna akun penyedia — rahasia pembuka menu Pusat Administrasi.</summary>
        public static string Username => _NamaTerbuka ??= Buka(_ByteNama, KunciNama);

        /// <summary>Kata sandi bawaan akun penyedia (dipakai saat akun dibuat di Login.dat).</summary>
        public static string KataSandiBawaan => _SandiTerbuka ??= Buka(_ByteSandi, KunciSandi);

        /// <summary>Nama tampilan akun penyedia di aplikasi (label menu, bukan rahasia).</summary>
        public const string NamaTampilan = "Pusat Administrasi";

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
