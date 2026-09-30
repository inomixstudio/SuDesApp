using System;
using System.Diagnostics;
using System.IO;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Akses berkas dokumentasi bawaan aplikasi — folder <c>docs</c> yang ikut
    /// installer dan paket portable. Dipakai halaman Dokumentasi dan tombol
    /// "Buka PDF Perbup" di Pengaturan; cara menemukan berkasnya sengaja ada di
    /// satu tempat supaya kedua halaman tidak menyimpang satu sama lain.
    ///
    /// Dokumennya sendiri <b>tidak</b> dibuka dari sini: halaman Dokumentasi membaca
    /// berkas di dalam aplikasi (lihat DokumentasiViewModel). Yang tersisa di sini
    /// hanyalah pencarian berkas dan <see cref="Buka"/> untuk membuka <i>folder</i>
    /// dokumen — keperluan teknisi, bukan cara membaca.
    /// </summary>
    public static class BerkasDokumentasi
    {
        /// <summary>Nama folder dokumentasi di dalam folder aplikasi.</summary>
        public const string NamaFolder = "docs";

        /// <summary>
        /// Folder dokumentasi yang ditemukan: <c>{folder aplikasi}\docs</c> pada
        /// aplikasi terpasang, atau folder <c>docs</c> proyek saat aplikasi
        /// dijalankan dari source (dicari naik dari folder bin). Null bila tidak ada.
        /// </summary>
        public static string? CariFolder()
        {
            var folder = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(folder))
            {
                var kandidat = Path.Combine(folder, NamaFolder);
                if (Directory.Exists(kandidat))
                {
                    return kandidat;
                }

                folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }

        /// <summary>
        /// Jalur lengkap sebuah berkas dokumentasi, dicari dari jalur relatifnya
        /// terhadap folder <c>docs</c> (mis. <c>api-desa.md</c> atau
        /// <c>regulasi/nama-berkas.pdf</c>). Null bila berkasnya tidak ada.
        /// </summary>
        public static string? Cari(string jalurRelatif)
        {
            if (string.IsNullOrWhiteSpace(jalurRelatif))
            {
                return null;
            }

            var relatif = jalurRelatif
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var namaBerkas = Path.GetFileName(relatif);

            var folder = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(folder))
            {
                var kandidat = Path.Combine(folder, NamaFolder, relatif);
                if (File.Exists(kandidat))
                {
                    return kandidat;
                }

                // Salinan datar: dokumen yang diletakkan langsung di sebelah aplikasi
                // (mis. hasil unduhan manual) tetap bisa dibuka.
                kandidat = Path.Combine(folder, namaBerkas);
                if (File.Exists(kandidat))
                {
                    return kandidat;
                }

                folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }

        /// <summary>
        /// Buka berkas atau folder dengan aplikasi bawaan Windows — <b>hanya</b>
        /// untuk folder (atau berkas bukan dokumen); dokumen aplikasi dibaca di
        /// dalam aplikasi. Mengembalikan pesan galat yang siap ditampilkan, atau
        /// null bila pembukaan berhasil.
        /// </summary>
        public static string? Buka(string? jalur)
        {
            if (string.IsNullOrWhiteSpace(jalur)
                || (!File.Exists(jalur) && !Directory.Exists(jalur)))
            {
                return "Berkas tidak ditemukan.";
            }

            try
            {
                Process.Start(new ProcessStartInfo(jalur) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Ukuran berkas dalam satuan yang mudah dibaca (KB/MB).</summary>
        public static string UkuranTerbaca(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return Math.Max(1, byteCount / 1024) + " KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }
    }
}
