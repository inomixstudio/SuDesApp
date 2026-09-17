using SuDesApp.Data.Models;

namespace SuDesApp.db
{
    public interface IDatabaseService
    {
        // Desa
        Task InitializeAsync();
        Task<DesaData> GetInfoDesaAsync();
        Task UpdateInfoDesaAsync(DesaData desaData);

        // Warga
        Task<WargaData> GetWargaByIdAsync(int idWarga);
        Task<WargaData> GetWargaByNikAsync(string nik);
        Task<int> AddOrGetWargaAsync(WargaData wargaData);
        Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData);
        Task<int> UpdateWargaAsync(int idWarga, WargaData wargaData);

        // Surat
        Task<int> AddSuratAsync(SuratData suratData);
        Task<int> UpdateSuratAsync(int idSurat, SuratData suratData);
        Task<SuratData> GetSuratDataAsync(int idSurat);
        Task<List<SuratData>> GetAllSuratDataAsync(string sortBy, bool ascending, int skip, int take);
        Task<int> CountAllSuratAsync();
        Task<bool> IsNomorSuratExistsAsync(string nomorSurat);

        // Jenis Surat
        Task<JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis);
        Task<int> GetIdJenisSuratByNamaAsync(string namaJenis);
        Task<string> GenerateNomorSuratAsync(string kodeJenis);
        Task<int> CountSuratByJenisGroupAsync(HashSet<string> grupPenomoranBersama);
        Task<int> CountSuratByJenisAsync(string namaJenis);

        // Izin Orang Tua
        Task<int> AddIzinOrtuAsync(IzinOrtuData izinData);
        Task<IzinOrtuData> GetIzinOrtuAsync(int idSurat);
        Task<int> UpdateIzinOrtuAsync(IzinOrtuData izinData);
        Task<int?> GetLastSuratIdByTypeAsync(string templateName);
    }
}
