// File: SuDesApp/db/IzinOrtuService.cs
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System;
using System.Data; // Pastikan using ini ada
using System.Threading.Tasks;

namespace SuDesApp.db
{
    public class IzinOrtuService
    {
        private readonly AppConfig _config;
        private readonly ILogger<IzinOrtuService> _logger;

        public IzinOrtuService(AppConfig config, ILogger<IzinOrtuService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> AddIzinOrtuAsync(IzinOrtuData izinData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null)
        {
            if (izinData == null) throw new ArgumentNullException(nameof(izinData));
            if (izinData.ID_Surat <= 0) throw new ArgumentException("ID_Surat harus valid.", nameof(izinData.ID_Surat));
            if (izinData.ID_Warga_Anak <= 0) throw new ArgumentException("ID_Warga_Anak harus valid.", nameof(izinData.ID_Warga_Anak));

            _logger.LogInformation("Menambahkan data Izin Ortu untuk ID_Surat: {ID_Surat}, ID_Warga_Anak: {ID_Warga_Anak}", izinData.ID_Surat, izinData.ID_Warga_Anak);

            SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
            bool manageConnectionLifecycle = existingConnection == null;

            try
            {
                if (manageConnectionLifecycle) await connection.OpenAsync();

                const string query = @"
                    INSERT INTO IZIN ( 
                        ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT
                    ) VALUES (
                        @ID_Surat, @ID_Warga_Anak, @NegaraTujuan, @NamaPT
                    );";

                var affectedRows = await connection.ExecuteAsync(query, new
                {
                    izinData.ID_Surat,
                    izinData.ID_Warga_Anak,
                    izinData.NegaraTujuan,
                    izinData.NamaPT
                }, transaction: existingTransaction); // Gunakan transaksi yang ada

                if (affectedRows > 0)
                {
                    _logger.LogInformation("Data Izin Ortu berhasil ditambahkan untuk ID_Surat: {ID_Surat}", izinData.ID_Surat);
                    return izinData.ID_Surat;
                }
                _logger.LogWarning("Tidak ada baris yang ditambahkan untuk Izin Ortu ID_Surat: {ID_Surat}", izinData.ID_Surat);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan data izin orang tua untuk ID_Surat: {ID_Surat}", izinData.ID_Surat);
                throw new DataAccessException($"Gagal menambahkan data izin orang tua untuk ID_Surat: {izinData.ID_Surat}", ex);
            }
            finally
            {
                if (manageConnectionLifecycle && connection.State == System.Data.ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public async Task<IzinOrtuData> GetIzinOrtuAsync(int idSurat)
        {
            _logger.LogDebug("Mengambil data Izin Ortu untuk ID_Surat: {ID_Surat}", idSurat);
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            try
            {
                const string query = @"
            SELECT 
                iz.ID_Surat, iz.ID_Warga_Anak, iz.NegaraTujuan, iz.NamaPT,
                w.NIK AS NIKAnak,
                w.Nama AS NamaAnak,
                w.TempatLahir AS TempatLahirAnak,
                w.TanggalLahir AS TanggalLahirAnak,
                w.JenisKelamin AS JenisKelaminAnak,
                w.Agama AS AgamaAnak,
                w.StatusPerkawinan AS StatusPerkawinanAnak,
                w.Pekerjaan AS PekerjaanAnak,
                w.Alamat AS AlamatAnak
            FROM IZIN iz
            LEFT JOIN Warga w ON iz.ID_Warga_Anak = w.ID_Warga
            WHERE iz.ID_Surat = @ID_Surat";

                var result = await connection.QueryFirstOrDefaultAsync<IzinOrtuData>(query, new { ID_Surat = idSurat });
                if (result == null)
                {
                    _logger.LogWarning("Data Izin Ortu tidak ditemukan untuk ID_Surat: {ID_Surat}", idSurat);
                    return new IzinOrtuData(); // Kembalikan objek kosong untuk mencegah null
                }
                _logger.LogDebug("Izin Ortu data: ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}, NamaAnak={NamaAnak}, NIKAnak={NIKAnak}",
                    idSurat, result.ID_Warga_Anak, result.NamaAnak ?? "null", result.NIKAnak ?? "null");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil data izin orang tua untuk ID_Surat: {ID_Surat}", idSurat);
                throw new DataRetrievalException($"Gagal mengambil data izin orang tua untuk ID_Surat: {idSurat}", ex);
            }
        }

        public async Task<int> UpdateIzinOrtuAsync(IzinOrtuData izinData, SqliteConnection existingConnection = null, IDbTransaction existingTransaction = null)
        {
            if (izinData == null) throw new ArgumentNullException(nameof(izinData));
            if (izinData.ID_Surat <= 0) throw new ArgumentException("ID_Surat harus valid.", nameof(izinData.ID_Surat));
            if (izinData.ID_Warga_Anak <= 0) throw new ArgumentException("ID_Warga_Anak harus valid.", nameof(izinData.ID_Warga_Anak));

            _logger.LogInformation("Memperbarui data Izin Ortu untuk ID_Surat: {ID_Surat}, ID_Warga_Anak: {ID_Warga_Anak}, NamaAnak: {NamaAnak}",
                izinData.ID_Surat, izinData.ID_Warga_Anak, izinData.NamaAnak ?? "null");

            SqliteConnection connection = existingConnection ?? new SqliteConnection(_config.DatabaseConnectionString);
            bool manageConnectionLifecycle = existingConnection == null;

            try
            {
                if (manageConnectionLifecycle) await connection.OpenAsync();

                // Validasi bahwa ID_Warga_Anak ada di tabel Warga
                var wargaAnak = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT ID_Warga, Nama, NIK FROM Warga WHERE ID_Warga = @ID_Warga",
                    new { ID_Warga = izinData.ID_Warga_Anak },
                    existingTransaction);
                if (wargaAnak == null)
                {
                    _logger.LogError("Warga anak tidak ditemukan untuk ID_Warga_Anak: {ID_Warga_Anak}", izinData.ID_Warga_Anak);
                    throw new DataAccessException($"Warga anak dengan ID {izinData.ID_Warga_Anak} tidak ditemukan.");
                }
                _logger.LogDebug("Warga anak valid: ID_Warga_Anak={ID_Warga_Anak}, Nama={Nama}, NIK={NIK}",
                    izinData.ID_Warga_Anak, wargaAnak.Nama, wargaAnak.NIK);

                const string query = @"
            UPDATE IZIN
            SET 
                ID_Warga_Anak = @ID_Warga_Anak,
                NegaraTujuan = @NegaraTujuan,
                NamaPT = @NamaPT
            WHERE ID_Surat = @ID_Surat";

                var affectedRows = await connection.ExecuteAsync(query, new
                {
                    izinData.ID_Surat,
                    izinData.ID_Warga_Anak,
                    izinData.NegaraTujuan,
                    izinData.NamaPT
                }, transaction: existingTransaction);
                _logger.LogInformation("Data Izin Ortu diperbarui untuk ID_Surat: {ID_Surat}, Baris terpengaruh: {AffectedRows}, NamaAnak: {NamaAnak}",
                    izinData.ID_Surat, affectedRows, izinData.NamaAnak ?? "null");
                return affectedRows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memperbarui data izin orang tua untuk ID_Surat: {ID_Surat}", izinData.ID_Surat);
                throw new DataAccessException($"Gagal memperbarui data izin orang tua untuk ID_Surat: {izinData.ID_Surat}", ex);
            }
            finally
            {
                if (manageConnectionLifecycle && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public async Task<int> DeleteIzinOrtuAsync(int idSurat, SqliteConnection connection, IDbTransaction transaction)
        {
            _logger.LogInformation("Menghapus data Izin Ortu untuk ID_Surat: {ID_Surat}", idSurat);
            const string query = "DELETE FROM IZIN WHERE ID_Surat = @ID_Surat";
            return await connection.ExecuteAsync(query, new { ID_Surat = idSurat }, transaction);
        }
    }
}
