using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SuDesApp.db
{
    public class DatabaseService : IDatabaseService
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseService> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public DatabaseService(
            AppConfig config,
            ILogger<DatabaseService> logger,
            IUnitOfWork unitOfWork)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        // Desa Operations
        public async Task InitializeAsync()
        {
            var initializer = _unitOfWork.GetRequiredService<IDatabaseInitializer>();
            await initializer.InitializeAsync();
        }

        public async Task<DesaData> GetInfoDesaAsync()
        {
            _logger.LogDebug("Mengambil data InfoDesa");
            var desa = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
            if (desa == null)
            {
                _logger.LogWarning("Data InfoDesa tidak ditemukan, mengembalikan default");
                return new DesaData();
            }
            _logger.LogDebug("Data InfoDesa diambil: NamaDesa={NamaDesa}", desa.NamaDesa);
            return desa;
        }

        public async Task UpdateInfoDesaAsync(DesaData desaData)
        {
            if (desaData == null) throw new ArgumentNullException(nameof(desaData));
            _logger.LogDebug("Memperbarui InfoDesa: NamaDesa={NamaDesa}", desaData.NamaDesa);
            // Use repository to update
            await _unitOfWork.DesaRepository.UpdateInfoDesaAsync(desaData);
            await _unitOfWork.CommitTransactionAsync();
            _logger.LogInformation("InfoDesa berhasil diperbarui: NamaDesa={NamaDesa}", desaData.NamaDesa);
        }

        // Warga Operations
        public async Task<WargaData> GetWargaByIdAsync(int idWarga)
        {
            _logger.LogDebug("Mengambil Warga: ID_Warga={ID_Warga}", idWarga);
            var warga = await _unitOfWork.WargaRepository.GetWargaByIdAsync(idWarga);
            _logger.LogDebug("Warga diambil: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}", idWarga, warga?.Nama ?? "null", warga?.NIK ?? "null");
            return warga;
        }

        public async Task<WargaData> GetWargaByNikAsync(string nik)
        {
            _logger.LogDebug("Mengambil Warga: NIK={NIK}", nik);
            var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
            _logger.LogDebug("Warga diambil: NIK={NIK}, Nama={Nama}, ID_Warga={ID_Warga}", nik, warga?.Nama ?? "null", warga?.ID_Warga ?? 0);
            return warga;
        }

        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData)
        {
            _logger.LogDebug("AddOrUpdate Warga: Nama={Nama}, NIK={NIK}", wargaData?.Nama ?? "null", wargaData?.NIK ?? "null");
            var id = await _unitOfWork.WargaRepository.AddOrUpdateWargaAndGetIdAsync(wargaData);
            await _unitOfWork.CommitTransactionAsync();
            _logger.LogInformation("Warga berhasil disimpan: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}", id, wargaData?.Nama ?? "null", wargaData?.NIK ?? "null");
            return id;
        }

        public async Task<int> UpdateWargaAsync(int idWarga, WargaData wargaData)
        {
            _logger.LogDebug("Memperbarui Warga: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}", idWarga, wargaData?.Nama ?? "null", wargaData?.NIK ?? "null");
            // Use AddOrUpdate which handles both insert and update
            var id = await _unitOfWork.WargaRepository.AddOrUpdateWargaAndGetIdAsync(wargaData);
            await _unitOfWork.CommitTransactionAsync();
            _logger.LogInformation("Warga berhasil diperbarui: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}", id, wargaData?.Nama ?? "null", wargaData?.NIK ?? "null");
            return id > 0 ? 1 : 0;
        }

        public Task<int> AddOrGetWargaAsync(WargaData wargaData) => _unitOfWork.WargaRepository.AddOrUpdateWargaAndGetIdAsync(wargaData);

        // Surat Operations
        public async Task<int> AddSuratAsync(SuratData suratData)
        {
            _logger.LogDebug("Menambahkan Surat: NamaJenis={NamaJenis}, NomorSurat={NomorSurat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                suratData?.NamaJenis ?? "null", suratData?.NomorSurat ?? "null", suratData?.Warga?.Nama ?? "null", suratData?.Warga?.NIK ?? "null");
            var id = await _unitOfWork.SuratRepository.AddSuratAsync(suratData);
            await _unitOfWork.CommitTransactionAsync();
            _logger.LogInformation("Surat berhasil ditambahkan: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}",
                id, suratData?.NamaJenis ?? "null", suratData?.Warga?.Nama ?? "null");
            return id;
        }

        public async Task<int> UpdateSuratAsync(int idSurat, SuratData suratData)
        {
            _logger.LogDebug("Memperbarui Surat: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                idSurat, suratData?.NamaJenis ?? "null", suratData?.Warga?.Nama ?? "null", suratData?.Warga?.NIK ?? "null");
            var result = await _unitOfWork.SuratRepository.UpdateAsync(suratData);
            if (result)
            {
                await _unitOfWork.CommitTransactionAsync();
            }
            _logger.LogInformation("Surat berhasil diperbarui: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}",
                idSurat, suratData?.NamaJenis ?? "null", suratData?.Warga?.Nama ?? "null");
            return result ? 1 : 0;
        }

        public async Task<SuratData> GetSuratDataAsync(int idSurat)
        {
            _logger.LogDebug("Mengambil Surat: ID_Surat={ID_Surat}", idSurat);
            var surat = await _unitOfWork.SuratRepository.GetByIdAsync(idSurat);
            _logger.LogDebug("Surat diambil: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                idSurat, surat?.NamaJenis ?? "null", surat?.Warga?.Nama ?? "null", surat?.Warga?.NIK ?? "null");
            return surat;
        }

        public async Task<List<SuratData>> GetAllSuratDataAsync(string sortBy, bool ascending, int skip, int take)
        {
            _logger.LogDebug("Mengambil semua Surat: SortBy={SortBy}, Ascending={Ascending}, Skip={Skip}, Take={Take}", sortBy, ascending, skip, take);
            var filters = new FilterConditions();
            var surats = await _unitOfWork.SuratRepository.GetFilteredAsync(filters, sortBy, ascending, skip, take);
            _logger.LogInformation("Diambil {Count} surat", surats.Count());
            return surats.ToList();
        }

        public async Task<int> CountAllSuratAsync()
        {
            _logger.LogDebug("Menghitung semua Surat");
            var filters = new FilterConditions();
            var count = await _unitOfWork.SuratRepository.CountAsync(filters);
            _logger.LogDebug("Jumlah Surat: {Count}", count);
            return count;
        }

        public async Task<bool> IsNomorSuratExistsAsync(string nomorSurat)
        {
            _logger.LogDebug("Memeriksa NomorSurat: {NomorSurat}", nomorSurat);
            var exists = await _unitOfWork.SuratRepository.CheckNomorSuratExistsAsync(nomorSurat);
            _logger.LogDebug("NomorSurat {NomorSurat} exists: {Exists}", nomorSurat, exists);
            return exists;
        }

        // Jenis Surat Operations
        public async Task<JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis)
        {
            _logger.LogDebug("Mengambil JenisSurat: NamaJenis={NamaJenis}", namaJenis);
            var jenisSurat = await _unitOfWork.JenisSuratRepository.GetByNamaAsync(namaJenis);
            _logger.LogDebug("JenisSurat diambil: NamaJenis={NamaJenis}, ID_Jenis={ID_Jenis}", namaJenis, jenisSurat?.ID_Jenis ?? 0);
            return jenisSurat;
        }

        public async Task<int> GetIdJenisSuratByNamaAsync(string namaJenis)
        {
            _logger.LogDebug("Mengambil ID JenisSurat: NamaJenis={NamaJenis}", namaJenis);
            var jenisSurat = await _unitOfWork.JenisSuratRepository.GetByNamaAsync(namaJenis);
            if (jenisSurat == null)
            {
                _logger.LogWarning("Jenis Surat tidak ditemukan untuk NamaJenis: {NamaJenis}", namaJenis);
                throw new DataRetrievalException($"Jenis Surat '{namaJenis}' tidak ditemukan.");
            }
            _logger.LogDebug("ID JenisSurat diambil: NamaJenis={NamaJenis}, ID_Jenis={ID_Jenis}", namaJenis, jenisSurat.ID_Jenis);
            return jenisSurat.ID_Jenis;
        }

        public async Task<string> GenerateNomorSuratAsync(string kodeJenis)
        {
            _logger.LogDebug("Generate NomorSurat: KodeJenis={KodeJenis}", kodeJenis);
            var nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(kodeJenis);
            _logger.LogDebug("NomorSurat dihasilkan: KodeJenis={KodeJenis}, Nomor={Nomor}", kodeJenis, nomor);
            return nomor;
        }

        public async Task<int> CountSuratByJenisGroupAsync(HashSet<string> grupPenomoranBersama)
        {
            _logger.LogDebug("Menghitung Surat by Jenis Group: {Grup}", string.Join(",", grupPenomoranBersama));
            // Use the existing method with filters
            var filters = new FilterConditions { JenisSurat = "GROUP_KETERANGAN_DESA" };
            var count = await _unitOfWork.SuratRepository.CountAsync(filters);
            _logger.LogDebug("Jumlah Surat by Jenis Group: {Count}", count);
            return count;
        }

        public async Task<int> CountSuratByJenisAsync(string namaJenis)
        {
            _logger.LogDebug("Menghitung Surat by Jenis: NamaJenis={NamaJenis}", namaJenis);
            var filters = new FilterConditions { JenisSurat = namaJenis };
            var count = await _unitOfWork.SuratRepository.CountAsync(filters);
            _logger.LogDebug("Jumlah Surat by Jenis: NamaJenis={NamaJenis}, Count={Count}", namaJenis, count);
            return count;
        }

        // Izin Orang Tua Operations
        public async Task<int> AddIzinOrtuAsync(IzinOrtuData izinData)
        {
            _logger.LogDebug("Menambahkan IzinOrtu: ID_Surat={ID_Surat}, NamaAnak={NamaAnak}, NIKAnak={NIKAnak}",
                izinData?.ID_Surat ?? 0, izinData?.NamaAnak ?? "null", izinData?.NIKAnak ?? "null");
            var id = await _unitOfWork.IzinOrtuRepository.AddAsync(izinData);
            await _unitOfWork.CommitTransactionAsync();
            _logger.LogInformation("IzinOrtu berhasil ditambahkan: ID_Surat={ID_Surat}, NamaAnak={NamaAnak}", izinData?.ID_Surat ?? 0, izinData?.NamaAnak ?? "null");
            return id;
        }

        public async Task<IzinOrtuData> GetIzinOrtuAsync(int idSurat)
        {
            _logger.LogDebug("Mengambil IzinOrtu: ID_Surat={ID_Surat}", idSurat);
            var izin = await _unitOfWork.IzinOrtuRepository.GetBySuratIdAsync(idSurat);
            _logger.LogDebug("IzinOrtu diambil: ID_Surat={ID_Surat}, NamaAnak={NamaAnak}, NIKAnak={NIKAnak}",
                idSurat, izin?.NamaAnak ?? "null", izin?.NIKAnak ?? "null");
            return izin;
        }

        public async Task<int> UpdateIzinOrtuAsync(IzinOrtuData izinData)
        {
            _logger.LogDebug("Memperbarui IzinOrtu: ID_Surat={ID_Surat}, NamaAnak={NamaAnak}, NIKAnak={NIKAnak}",
                izinData?.ID_Surat ?? 0, izinData?.NamaAnak ?? "null", izinData?.NIKAnak ?? "null");
            var rowsAffected = await _unitOfWork.IzinOrtuRepository.UpdateAsync(izinData);
            if (rowsAffected > 0)
            {
                await _unitOfWork.CommitTransactionAsync();
            }
            _logger.LogInformation("IzinOrtu berhasil diperbarui: ID_Surat={ID_Surat}, NamaAnak={NamaAnak}, RowsAffected={RowsAffected}",
                izinData?.ID_Surat ?? 0, izinData?.NamaAnak ?? "null", rowsAffected);
            return rowsAffected;
        }

        public async Task<int?> GetLastSuratIdByTypeAsync(string namaJenis)
        {
            _logger.LogDebug("Mengambil Last Surat ID by Jenis: NamaJenis={NamaJenis}", namaJenis);
            // Use a filter to get the last ID by type
            var filters = new FilterConditions { JenisSurat = namaJenis };
            var surats = await _unitOfWork.SuratRepository.GetFilteredAsync(filters, "ID_Surat", false, 0, 1);
            var lastSurat = surats.FirstOrDefault();
            var id = lastSurat?.ID_Surat;
            _logger.LogDebug("Last Surat ID diambil: NamaJenis={NamaJenis}, ID_Surat={ID_Surat}", namaJenis, id);
            return id;
        }
    }
}
