using System;
using System.Diagnostics;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Membuka tautan web resmi (situs pemerintah, dokumentasi daring) di peramban
    /// bawaan Windows.
    ///
    /// Hanya alamat <c>http</c>/<c>https</c> yang boleh dibuka lewat jalur ini:
    /// tautan berkas lokal (<c>file://</c>) atau skema lain bisa menunjuk ke
    /// isi komputer desa, jadi tidak pernah diteruskan ke peramban.
    ///
    /// Dipakai tiga tempat: tautan di dalam dokumen Markdown (halaman baca
    /// dokumen), daftar "Sumber resmi 2025" pada halaman Dokumentasi, dan kartu
    /// sumber resmi pada halaman IDM. Ketiganya menunjuk situs yang sama —
    /// DATAS government — sehingga aturan buka tautan dikumpulkan di satu tempat.
    /// </summary>
    public static class TautanWeb
    {
        /// <summary>True bila alamatnya tautan web yang boleh dibuka (http/https).</summary>
        public static bool AdalahTautanWeb(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Buka tautan di peramban bawaan. Mengembalikan pesan galat yang siap
        /// ditampilkan, atau null bila tautannya berhasil dibuka.
        /// </summary>
        public static string? Buka(string? url)
        {
            if (!AdalahTautanWeb(url))
            {
                return "Tautan tidak dapat dibuka — hanya alamat http/https yang didukung.";
            }

            try
            {
                Process.Start(new ProcessStartInfo(url!.Trim()) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Nama host yang enak dibaca, mis. <c>satudata.kemendesa.go.id</c>.
        /// Kosong bila alamatnya bukan tautan web yang sah.
        /// </summary>
        public static string NamaHost(string? url)
        {
            if (!AdalahTautanWeb(url)) return string.Empty;
            return Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri)
                ? uri.Host
                : string.Empty;
        }
    }
}
