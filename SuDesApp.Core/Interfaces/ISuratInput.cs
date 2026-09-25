// SuDesApp.Core/Interfaces/ISuratInput.cs
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using System; // Tambahkan jika belum ada
using System.Threading.Tasks;

namespace SuDesApp.Interfaces
{
    public interface ISuratInput
    {
        bool ValidateInput(out DateTime tglLahir);

        /// <summary>
        /// Kumpulkan isian form ke objek surat. Mode Draft berarti isian belum
        /// lengkap TIDAK boleh memblokir (tidak melempar validasi ketat); keputusan
        /// status akhir ada pada SuratSaveService.
        /// </summary>
        Task CollectDataAsync(SuratData suratData, ModeSimpan mode);
        Task FillDataAsync(SuratData suratData);
        string GetNIK();
        string GetNomorSurat();
        string GetTanggalSurat();
        string GetKeteranganTextBox();
        void SetNomorSurat(string nomorSurat);
        string GetStatusPerkawinan();
        string GetPekerjaan();
        string GetAlamat();
        string GetAgama();
        string GetJenisKelamin();
        string GetTanggalLahir();
        string GetTempatLahir();
        string GetNama();
        string GetPendidikan();
        string GetKewarganegaraan();
        void SetEditMode(bool isEditMode); // Ditambahkan
    }
}
