using System;
using System.Globalization;
using System.Linq;
using SuDesApp.Services;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Menyusun teks status IDM untuk status bar utama: cadangan terakhir dari
    /// halaman IDM (<c>idm.kemendesa.go.id</c>) yang cocok dengan kode desa pada
    /// Pengaturan Surat. Murni tanpa I/O supaya bisa diuji unit — status bar
    /// hanya menampilkan, tidak pernah mengambil data sendiri.
    ///
    /// Hasilnya teks BIASA ("Status Desa (IDM): Maju  •  0,7444 (2024)"), bukan
    /// chip: sesuai permintaan tampilan agar berdiri sejajar dengan info desa di
    /// sebelahnya.
    /// </summary>
    public static class StatusIdmBar
    {
        /// <summary>Teks bila belum ada cadangan untuk desa ini (atau desa belum dipilih).</summary>
        public const string BelumAdaData = "Status Desa (IDM): belum ada data";

        /// <summary>
        /// Susun teks status dari cadangan yang cocok dengan <paramref name="kodeDesa"/>.
        /// Cadangan kosong / milik desa lain → <see cref="BelumAdaData"/>; desa tidak
        /// pernah boleh menampilkan status milik desa lain.
        /// </summary>
        public static string Susun(IdmSimpanan? cadangan, string kodeDesa)
        {
            if (cadangan == null
                || cadangan.Hasil is not { Count: > 0 }
                || string.IsNullOrWhiteSpace(kodeDesa)
                || !string.Equals(cadangan.KodeDesa, kodeDesa, StringComparison.Ordinal))
            {
                return BelumAdaData;
            }

            var hasil = cadangan.Hasil.OrderByDescending(h => h.Ringkasan.Tahun).First();
            string status = PasangHurufKapital(hasil.Ringkasan.Status);
            string skor = hasil.Ringkasan.Skor.ToString("0.0000", new CultureInfo("id-ID"));
            return "Status Desa (IDM): " + status + "  •  " + skor + " (" + hasil.Ringkasan.Tahun + ")";
        }

        /// <summary>"MAJU" → "Maju"; "Maju" → "Maju"; kosong → "—". Kapital hanya di awal.</summary>
        internal static string PasangHurufKapital(string? teks)
        {
            var bersih = (teks ?? string.Empty).Trim();
            if (bersih.Length == 0) return "—";
            return char.ToUpperInvariant(bersih[0]) + bersih[1..].ToLowerInvariant();
        }
    }
}