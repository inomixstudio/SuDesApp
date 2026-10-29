using System;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Menyimpan keadaan kunci percobaan akun pusat di preferensi lokal agar
    /// tetap berlaku setelah aplikasi ditutup. Nilainya hanya
    /// <c>jumlah gagal|waktu gagal terakhir (UTC ticks)</c>; nama pengguna,
    /// kata sandi, dan hash-nya tidak pernah ditulis ke sini.
    /// </summary>
    internal sealed class KunciPercobaanPusat : IPenyimpananPercobaanLogin
    {
        public (int Gagal, DateTime Terakhir)? Baca()
        {
            var mentah = AppPreferenceStore.GetString(
                AppPreferenceStore.KeyKunciPercobaanPusat, null);
            if (string.IsNullOrWhiteSpace(mentah))
            {
                return null;
            }

            var bagian = mentah.Split('|');
            if (bagian.Length != 2
                || !int.TryParse(bagian[0], out int gagal)
                || gagal < 1
                || gagal > 1_000_000
                || !long.TryParse(bagian[1], out long ticks))
            {
                return null;
            }

            try
            {
                return (gagal, new DateTime(ticks, DateTimeKind.Utc).ToLocalTime());
            }
            catch (ArgumentOutOfRangeException)
            {
                // Nilai rusak diperlakukan seperti belum pernah ada kegagalan.
                return null;
            }
        }

        public void Simpan(int gagal, DateTime terakhir) =>
            AppPreferenceStore.SetString(
                AppPreferenceStore.KeyKunciPercobaanPusat,
                $"{gagal}|{terakhir.ToUniversalTime().Ticks}");

        public void Hapus()
        {
            // Jangan menulis berkas preferensi bila memang belum ada keadaan
            // yang perlu dibersihkan; login biasa tidak perlu menyentuh disk.
            if (string.IsNullOrEmpty(AppPreferenceStore.GetString(
                    AppPreferenceStore.KeyKunciPercobaanPusat, null)))
            {
                return;
            }

            AppPreferenceStore.SetString(AppPreferenceStore.KeyKunciPercobaanPusat, string.Empty);
        }
    }
}
