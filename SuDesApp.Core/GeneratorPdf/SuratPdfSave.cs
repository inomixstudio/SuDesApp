using System;
using System.IO;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Pemilik siklus hidup berkas PDF: menulis ke berkas sementara yang unik,
    /// lalu memindahkannya ke nama akhir. Bila nama akhir terkunci pratinjau,
    /// berkas sementara tetap dipakai agar PDF terbaru tetap tersedia.
    /// </summary>
    public static class SuratPdfSave
    {
        /// <summary>
        /// Tulis PDF ke berkas sementara pada folder tujuan, lalu selesaikan ke nama
        /// akhir. Berkas sementara dibersihkan bila penulis gagal.
        /// </summary>
        /// <param name="jalurAkhir">Lokasi tujuan PDF.</param>
        /// <param name="tulis">Penulis konten PDF ke stream yang disediakan.</param>
        /// <returns>Jalur akhir, atau jalur sementara bila tujuan masih terkunci.</returns>
        public static async Task<string> SimpanAsync(string jalurAkhir, Func<Stream, Task> tulis)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jalurAkhir);
            ArgumentNullException.ThrowIfNull(tulis);

            var folder = Path.GetDirectoryName(jalurAkhir);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string nama = Path.GetFileNameWithoutExtension(jalurAkhir);
            string jalurTulis = Path.Combine(folder ?? string.Empty, $"{nama}_{Guid.NewGuid():N}.pdf");

            try
            {
                await using (var stream = new FileStream(
                    jalurTulis, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await tulis(stream).ConfigureAwait(false);
                }

                return Selesaikan(jalurTulis, jalurAkhir);
            }
            catch
            {
                try
                {
                    File.Delete(jalurTulis);
                }
                catch (IOException)
                {
                    // Jangan tutupi kegagalan penulisan yang asli.
                }
                catch (UnauthorizedAccessException)
                {
                    // Jangan tutupi kegagalan penulisan yang asli.
                }

                throw;
            }
        }

        private static string Selesaikan(string jalurTulis, string jalurAkhir)
        {
            try
            {
                // Replace tanpa menghapus tujuan terlebih dahulu agar berkas lama
                // tetap utuh jika penggantian gagal (mis. masih dikunci pratinjau).
                File.Move(jalurTulis, jalurAkhir, overwrite: true);
                return jalurAkhir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return jalurTulis;
            }
        }
    }
}
