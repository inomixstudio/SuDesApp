using SuDesApp.Data.Models;

namespace SuDesApp.Interface
{
    public interface ISuratGenerator
    {
        // Metode yang sudah ada (mengambil data dari DB berdasarkan ID)
        Task GeneratePdfAsync(Stream outputStream, int idSurat, string keteranganTextBox = null);

        // *** Metode baru: Menggunakan objek SuratData yang sudah ada ***
        Task GeneratePdfAsync(Stream outputStream, SuratData suratData, string keteranganTextBox = null);
    }
}
