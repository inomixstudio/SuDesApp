using System;
using System.IO;
using System.Threading.Tasks;
using SuDesApp.Configuration;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Penulis berkas pratinjau PDF. Seluruh alur "cetak PDF lalu buka pratinjau"
    /// memakai tempat ini supaya folder, penamaan, dan pembersihan berkasnya tidak
    /// ditulis ulang di setiap ViewModel.
    ///
    /// Berkas pratinjau selalu di bawah folder TempPDF aplikasi — folder yang sama
    /// yang dibersihkan <see cref="TempPdfCleanup"/> saat aplikasi dijalankan.
    /// </summary>
    public static class PratinjauPdf
    {
        /// <summary>
        /// Simpan PDF pratinjau dan kembalikan jalur penuhnya (yang dibaca SudesPdf).
        /// Nama yang sudah dipakai berkas lain — mis. pratinjau dokumen yang sama
        /// masih terbuka — diberi akhiran waktu supaya tidak saling menimpa.
        /// </summary>
        public static Task<string> SimpanAsync(AppConfig? config, byte[] pdf, string namaBerkas) =>
            SimpanAsync(Folder(config), pdf, namaBerkas);

        /// <summary>Varian untuk pemanggil yang sudah memegang folder pratinjau sendiri.</summary>
        public static async Task<string> SimpanAsync(string? folder, byte[] pdf, string namaBerkas)
        {
            ArgumentNullException.ThrowIfNull(pdf);

            folder = string.IsNullOrWhiteSpace(folder)
                ? Path.Combine(Path.GetTempPath(), "SuDesApp")
                : folder!;
            Directory.CreateDirectory(folder);

            string dasar = NamaAman(namaBerkas);
            if (dasar.Length == 0) dasar = "Pratinjau";

            string jalur = Path.Combine(folder, dasar + ".pdf");
            if (File.Exists(jalur))
            {
                jalur = Path.Combine(folder, $"{dasar}_{DateTime.Now:yyyyMMddHHmmssfff}.pdf");
            }

            await File.WriteAllBytesAsync(jalur, pdf).ConfigureAwait(false);
            return jalur;
        }

        /// <summary>Folder pratinjau: TempPDF aplikasi, atau folder data sementara bila belum disetel.</summary>
        private static string? Folder(AppConfig? config) => config?.TempPdfFolder;

        /// <summary>Bersihkan nama berkas dari karakter yang tidak sah (mis. "/" pada nomor surat).</summary>
        private static string NamaAman(string teks)
        {
            if (string.IsNullOrWhiteSpace(teks)) return string.Empty;

            var tidakSah = Path.GetInvalidFileNameChars();
            var hasil = new System.Text.StringBuilder(teks.Length);
            foreach (char huruf in teks.Trim())
            {
                hasil.Append(Array.IndexOf(tidakSah, huruf) >= 0 ? '_' : huruf);
            }

            return hasil.ToString().Trim().TrimEnd('.');
        }
    }
}
