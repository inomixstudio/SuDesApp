using System;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Kata sandi acak untuk akun yang dibuat otomatis saat pemasangan baru.
    ///
    /// Kredensial bawaan yang sama untuk semua instalasi (mis. <c>admin/admin</c>)
    /// tidak dipakai lagi: sandi dibuat di mesin pengguna dan hanya ditampilkan
    /// sekali di layar login. Dengan begitu, sandi yang bocor dari kode sumber
    /// tidak bisa dipakai membuka instalasi mana pun.
    ///
    /// Hanya huruf kecil dan angka: layar login menormalkan kata sandi ke huruf
    /// kecil (perilaku lama), jadi sandi berhuruf besar tidak akan pernah cocok.
    /// Karakter yang mudah tertukar saat dibaca (0/o, 1/l/i) sengaja dihindari.
    /// </summary>
    public static class SandiAcak
    {
        private const string Alfabet = "abcdefghjkmnpqrstuvwxyz23456789";

        /// <summary>Panjang sandi yang dipakai akun administrator awal (tanpa tanda hubung).</summary>
        public const int PanjangBawaan = 16;

        /// <summary>
        /// Buat sandi acak kuat, dikelompokkan per empat karakter dengan tanda
        /// hubung supaya mudah disalin/diketik dari layar login.
        /// </summary>
        public static string Buat(int panjang = PanjangBawaan)
        {
            if (panjang < 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(panjang), "Kata sandi minimal 8 karakter.");
            }

            var teks = new StringBuilder(panjang + panjang / 4);
            for (var i = 0; i < panjang; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    teks.Append('-');
                }

                teks.Append(Alfabet[RandomNumberGenerator.GetInt32(Alfabet.Length)]);
            }

            return teks.ToString();
        }
    }
}
