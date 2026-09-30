using System;
using System.Linq;
using System.Reflection;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Identitas aplikasi dari atribut assembly (csproj) — satu sumber kebenaran
    /// untuk teks yang menampilkan nama, versi, dan pengembang aplikasi (status bar,
    /// footer Catatan Rilis, footer jendela login, dsb.) sehingga tidak pernah
    /// kedaluwarsa: cukup naikkan versi di csproj, semua tempat ikut berubah.
    /// </summary>
    public static class IdentitasAplikasi
    {
        private static readonly Assembly AssemblyAplikasi = Assembly.GetExecutingAssembly();

        /// <summary>Nama aplikasi dari atribut Product (bawaan "SuDesApp").</summary>
        public static string Nama
            => AssemblyAplikasi.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } produk
                ? produk
                : "SuDesApp";

        /// <summary>Nama pengembang dari atribut Company (mis. "Sumberjaya Dev.").</summary>
        public static string Pengembang
            => AssemblyAplikasi.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

        /// <summary>Teks hak cipta dari atribut Copyright.</summary>
        public static string HakCipta
            => AssemblyAplikasi.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        /// <summary>
        /// Versi aplikasi singkat tanpa "v": 2.5.3.0 → "2.5.3" — nomor revisi
        /// hanya ikut bila benar-benar dipakai (mis. 2.5.3.1 → "2.5.3.1").
        /// </summary>
        public static string VersiSingkat
        {
            get
            {
                var versi = AssemblyAplikasi.GetName().Version;
                if (versi == null) return string.Empty;

                return versi.Revision > 0
                    ? $"{versi.Major}.{versi.Minor}.{versi.Build}.{versi.Revision}"
                    : $"{versi.Major}.{versi.Minor}.{versi.Build}";
            }
        }

        /// <summary>Versi aplikasi berawalan "v" (mis. "v2.5.3"), dipakai chip status bar.</summary>
        public static string VersiDenganPrefiks => "v" + VersiSingkat;

        /// <summary>Versi lengkap empat komponen (mis. "2.5.3.0") — nomor build.</summary>
        public static string VersiLengkap
            => AssemblyAplikasi.GetName().Version?.ToString() ?? string.Empty;

        /// <summary>
        /// Footer identitas ala Catatan Rilis / jendela login:
        /// "Surat Desa V.2.5.3  •  Sumberjaya Dev." — label merek dan versi dibaca
        /// dari assembly (csproj), jadi otomatis ikut saat versi dinaikkan.
        /// </summary>
        public static string FooterVersi
        {
            get
            {
                var footer = $"{Nama} V.{VersiSingkat}";
                if (!string.IsNullOrWhiteSpace(Pengembang))
                {
                    footer += $"  \u2022  {Pengembang}";
                }

                return footer;
            }
        }
    }
}
