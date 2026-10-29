using System;
using System.Globalization;
using System.IO;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Satu pintu penulisan <c>error.log</c> untuk galat perintah dan galat tak
    /// tertangani. Sebelumnya dua tempat menulis sendiri-sendiri tanpa batas
    /// ukuran; berkas bisa membengkak selama satu sesi yang mengalami galat
    /// berulang. Di sini berkas aktif dibatasi 2 MB, lalu diarsipkan ke folder
    /// <c>Logs</c> (arsip lama dibersihkan oleh <see cref="StartupFileCleanup"/>).
    /// </summary>
    internal static class LogGalat
    {
        internal const long BatasByte = 2 * 1024 * 1024;

        /// <summary>Jumlah arsip error.log yang disimpan; sisanya yang tertua dihapus.</summary>
        internal const int JumlahArsip = 10;

        private static readonly object Kunci = new();

        internal static void Tulis(string jenis, Exception galat)
        {
            ArgumentNullException.ThrowIfNull(galat);

            lock (Kunci)
            {
                try
                {
                    string aktif = Path.Combine(AppContext.BaseDirectory, "error.log");
                    PutarBilaBesar(aktif);

                    File.AppendAllText(
                        aktif,
                        $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {jenis}:\n{galat}\n\n");
                }
                catch
                {
                    // Pencatatan galat tidak boleh menjadi galat baru; dialog
                    // pengguna tetap menjadi jalur pemberitahuan.
                }
            }
        }

        private static void PutarBilaBesar(string aktif)
        {
            if (!File.Exists(aktif)) return;
            if (new FileInfo(aktif).Length < BatasByte) return;

            string folder = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(folder);
            string arsip = Path.Combine(
                folder,
                $"error_{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");
            File.Move(aktif, arsip, overwrite: true);

            foreach (var lama in Directory.EnumerateFiles(folder, "error_*.log")
                         .Select(jalur => new FileInfo(jalur))
                         .OrderByDescending(berkas => berkas.LastWriteTimeUtc)
                         .Skip(JumlahArsip)
                         .ToArray())
            {
                try { lama.Delete(); }
                catch (IOException) { /* arsip sedang dibuka — dibiarkan sampai start berikutnya */ }
                catch (UnauthorizedAccessException) { /* izin berubah — dibiarkan sampai start berikutnya */ }
            }
        }
    }
}
