using System;
using System.Collections.Generic;
using System.Linq;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pemetakan teks antarmuka ke bahasa yang dipilih pengguna (Indonesia/English).
    ///
    /// Sengaja sederhana — kamus statis berbasis kunci, bukan berkas sumber daya —
    /// supaya halaman pengaturan (satu-satunya yang menampilkan pilihan bahasa)
    /// bisa ikut berpindah bahasa tanpa menambah dependensi atau berkas .resx.
    /// Bahasa aktif dibaca malas dari preferensi (<see cref="AppPreferenceStore"/>)
    /// saat pertama dipakai; pemanggil cukup mengganti lewat <see cref="SetCurrent"/>.
    /// </summary>
    public static class Lokalisasi
    {
        private static string? _bahasa;

        /// <summary>Bahasa aktif ("id" atau "en"); dibaca malas dari preferensi bila belum diisi.</summary>
        public static string Current => _bahasa ??= AppPreferenceStore.GetBahasa();

        /// <summary>
        /// Tetapkan bahasa aktif. Nilai di luar "id"/"en" ditolak dan jatuh ke "id",
        /// sehingga tampilan selalu punya satu nilai yang bisa dipakai.
        /// </summary>
        public static void SetCurrent(string nilai) =>
            _bahasa = nilai is "id" or "en" ? nilai : "id";

        /// <summary>Ambil teks untuk kunci; bila kuncinya tidak ada, kembalikan kuncinya sendiri.</summary>
        public static string Get(string kunci)
        {
            if (_kamus.TryGetValue(kunci, out var pasangan))
            {
                return _bahasa == "en" ? pasangan.English : pasangan.Indonesia;
            }
            return kunci;
        }

        /// <summary>Daftar pilihan bahasa, dengan pilihan yang sedang aktif diprioritaskan di depan.</summary>
        public static IEnumerable<ViewModels.PilihanItem> BahasaPilihan(string nilai)
        {
            var dua = new[]
            {
                new ViewModels.PilihanItem("id", "Indonesia"),
                new ViewModels.PilihanItem("en", "English")
            };
            return string.Equals(nilai, "en", StringComparison.Ordinal)
                ? new[] { dua[1], dua[0] }
                : dua;
        }

        private readonly struct PasanganTeks
        {
            public string Indonesia { get; }
            public string English { get; }

            public PasanganTeks(string indonesia, string english)
            {
                Indonesia = indonesia;
                English = english;
            }
        }

        private static readonly Dictionary<string, PasanganTeks> _kamus = new()
        {
            ["Settings.Bahasa.Judul"] = new PasanganTeks("Bahasa", "Language"),
            ["Settings.Bahasa.Keterangan"] = new PasanganTeks(
                "Pilih bahasa tampilan menu dan pengaturan aplikasi.",
                "Choose the language used to show menus and settings."),
            ["Settings.Versi.Judul"] = new PasanganTeks("Versi Aplikasi", "Application Version")
        };
    }
}
