using System;
using System.IO;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Penyimpanan berkas PDF SK yang diarsipkan dari luar aplikasi (SK Bupati untuk
    /// Kepala Desa dan anggota BPD). Berkas disalin ke folder
    /// <c>%LOCALAPPDATA%\SuDesApp\BerkasSkPerangkat</c> — folder data pengguna, bukan
    /// folder program — supaya salinannya ikut tercadangkan bersama data dan tidak ikut
    /// terhapus saat aplikasi diperbarui. Nama berkasnya di database hanya nama berkas
    /// (tanpa jalur), jadi baris data tidak mati bila folder dipindah komputer.
    /// </summary>
    public static class BerkasSkPerangkat
    {
        /// <summary>Nama folder di bawah %LOCALAPPDATA%\SuDesApp.</summary>
        public const string NamaFolder = "BerkasSkPerangkat";

        /// <summary>Lokasi folder untuk pengujian; null = lokasi asli milik pengguna.</summary>
        internal static Func<string>? LokasiOverride { get; set; }

        private static string Folder =>
            LokasiOverride?.Invoke()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", NamaFolder);

        /// <summary>Folder penyimpanan berkas SK terarsip (dibuat bila belum ada).</summary>
        public static string SiapkanFolder()
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            return Folder;
        }

        /// <summary>Folder penyimpanan tanpa membuatnya (untuk pesan &amp; diagnostika).</summary>
        public static string LokasiFolder => Folder;

        /// <summary>
        /// Salin berkas SK ke folder arsip dan kembalikan namanya untuk disimpan di
        /// kolom <c>BerkasSK</c>. Nama berkas diawali ID baris agar berkas milik
        /// orang berbeda tidak saling menimpa, lalu diikuti nama asli yang sudah
        /// dibersihkan dari karakter terlarang.
        /// </summary>
        public static string Arsipkan(int idPerangkat, string sumberPath)
        {
            if (idPerangkat <= 0)
                throw new ArgumentException("ID perangkat desa belum terisi.", nameof(idPerangkat));
            if (string.IsNullOrWhiteSpace(sumberPath) || !File.Exists(sumberPath))
                throw new FileNotFoundException("Berkas SK tidak ditemukan.", sumberPath);

            string ext = Path.GetExtension(sumberPath).ToLowerInvariant();
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Berkas SK yang diarsipkan harus PDF. Ubah dulu berkasnya ke PDF, lalu lampirkan kembali.");

            string folder = SiapkanFolder();
            string namaAsli = Path.GetFileNameWithoutExtension(sumberPath);
            string aman = string.Join("_", namaAsli
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (aman.Length == 0) aman = "SK";
            if (aman.Length > 60) aman = aman[..60];

            // Nama unik: SK_12_2019-SK-08.04.100-352.pdf, bila bentrok diberi akhiran (2), (3), …
            string nama = $"SK_{idPerangkat}_{aman}{ext}";
            for (int urutan = 2; File.Exists(Path.Combine(folder, nama)); urutan++)
            {
                nama = $"SK_{idPerangkat}_{aman} ({urutan}){ext}";
            }

            File.Copy(sumberPath, Path.Combine(folder, nama), overwrite: false);
            return nama;
        }

        /// <summary>Path lengkap berkas terarsip, atau null bila namanya tidak sah/tidak ada.</summary>
        public static string? JalurLengkap(string? namaBerkas)
        {
            if (string.IsNullOrWhiteSpace(namaBerkas)) return null;
            if (Path.IsPathRooted(namaBerkas) || namaBerkas.Contains("..")) return null;

            string full = Path.Combine(Folder, namaBerkas);
            return File.Exists(full) ? full : null;
        }

        /// <summary>Hapus berkas terarsip dari folder (aman dipanggil bila berkasnya sudah tidak ada).</summary>
        public static void Hapus(string? namaBerkas)
        {
            string? full = JalurLengkap(namaBerkas);
            if (full == null) return;

            try
            {
                File.Delete(full);
            }
            catch (IOException)
            {
                // Berkas sedang dibuka penampil — biarkan; pembersihan folder manual
                // tetap bisa dilakukan pengguna.
            }
        }

        /// <summary>Daftar nama berkas di folder arsip (untuk cadangan &amp; pemeriksaan).</summary>
        public static System.Collections.Generic.IReadOnlyList<string> DaftarBerkas()
        {
            if (!Directory.Exists(Folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(Folder, "*.pdf")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Cast<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
