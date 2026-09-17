using SuDesApp.Data.Models;

namespace SuDesApp.Controllers.Interfaces
{
    public interface ISuratInput
    {
        bool ValidateInput(out DateTime tglLahir);
        Task CollectDataAsync(SuratData? suratData);
        Task FillDataAsync(SuratData? suratData);
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
        void SetEditMode(bool isEditMode);
    }
}
