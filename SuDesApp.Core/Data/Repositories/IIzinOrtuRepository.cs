using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System.Data;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public interface IIzinOrtuRepository
    {
        Task<int> AddAsync(IzinOrtuData izinData, IDbTransaction? transaction = null);
        Task<IzinOrtuData> GetBySuratIdAsync(int idSurat, IDbTransaction? transaction = null);
        Task<int> UpdateAsync(IzinOrtuData izinData, IDbTransaction? transaction = null);
        Task<int> DeleteAsync(int idSurat, IDbTransaction? transaction = null);
        Task InitializeAsync();
    }

    public class IzinOrtuRepository : IIzinOrtuRepository
    {
        private readonly SqliteConnection _connection;
        private readonly ILogger<IzinOrtuRepository> _logger;

        public IzinOrtuRepository(
            SqliteConnection connection,
            ILogger<IzinOrtuRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> AddAsync(IzinOrtuData izinData, IDbTransaction? transaction = null)
        {
            if (izinData == null) throw new ArgumentNullException(nameof(izinData));
            if (izinData.ID_Surat <= 0) throw new ArgumentException("ID_Surat harus valid.", nameof(izinData.ID_Surat));
            if (izinData.ID_Anak <= 0) throw new ArgumentException("ID_Warga_Anak harus valid.", nameof(izinData.ID_Anak));

            _logger.LogInformation("Menambahkan data Izin Ortu untuk ID_Surat: {ID_Surat}, ID_Warga_Anak: {ID_Warga_Anak}", izinData.ID_Surat, izinData.ID_Anak);

            const string query = @"
                INSERT INTO IZIN ( 
                    ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT
                ) VALUES (
                    @ID_Surat, @ID_Warga_Anak, @NegaraTujuan, @NamaPT
                );";

            var affectedRows = await _connection.ExecuteAsync(query, new
            {
                izinData.ID_Surat,
                izinData.ID_Anak,
                izinData.NegaraTujuan,
                izinData.NamaPT
            }, transaction: transaction);

            if (affectedRows > 0)
            {
                _logger.LogInformation("Data Izin Ortu berhasil ditambahkan untuk ID_Surat: {ID_Surat}", izinData.ID_Surat);
                return izinData.ID_Surat;
            }
            _logger.LogWarning("Tidak ada baris yang ditambahkan untuk Izin Ortu ID_Surat: {ID_Surat}", izinData.ID_Surat);
            return 0;
        }

        public async Task<IzinOrtuData> GetBySuratIdAsync(int idSurat, IDbTransaction? transaction = null)
        {
            _logger.LogDebug("Mengambil data Izin Ortu untuk ID_Surat: {ID_Surat}", idSurat);

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

            var result = await _connection.QueryFirstOrDefaultAsync<IzinOrtuData>(query, new { ID_Surat = idSurat }, transaction);
            if (result == null)
            {
                _logger.LogWarning("Data Izin Ortu tidak ditemukan untuk ID_Surat: {ID_Surat}", idSurat);
                return new IzinOrtuData();
            }
            _logger.LogDebug("Izin Ortu data: ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}, NamaAnak={NamaAnak}, NIKAnak={NIKAnak}",
                idSurat, result.ID_Anak, result.NamaAnak ?? "null", result.NIKAnak ?? "null");
            return result;
        }

        public async Task<int> UpdateAsync(IzinOrtuData izinData, IDbTransaction? transaction = null)
        {
            if (izinData == null) throw new ArgumentNullException(nameof(izinData));
            if (izinData.ID_Surat <= 0) throw new ArgumentException("ID_Surat harus valid.", nameof(izinData.ID_Surat));
            if (izinData.ID_Anak <= 0) throw new ArgumentException("ID_Warga_Anak harus valid.", nameof(izinData.ID_Anak));

            _logger.LogInformation("Memperbarui data Izin Ortu untuk ID_Surat: {ID_Surat}, ID_Warga_Anak: {ID_Warga_Anak}, NamaAnak: {NamaAnak}",
                izinData.ID_Surat, izinData.ID_Anak, izinData.NamaAnak ?? "null");

            // Validasi bahwa ID_Warga_Anak ada di tabel Warga
            var wargaAnak = await _connection.QueryFirstOrDefaultAsync<WargaData>(
                "SELECT ID_Warga, Nama, NIK FROM Warga WHERE ID_Warga = @ID_Warga",
                new { ID_Warga = izinData.ID_Anak },
                transaction);
            if (wargaAnak == null)
            {
                _logger.LogError("Warga anak tidak ditemukan untuk ID_Warga_Anak: {ID_Warga_Anak}", izinData.ID_Anak);
                throw new DataAccessException($"Warga anak dengan ID {izinData.ID_Anak} tidak ditemukan.");
            }
            _logger.LogDebug("Warga anak valid: ID_Warga_Anak={ID_Warga_Anak}, Nama={Nama}, NIK={NIK}",
                izinData.ID_Anak, wargaAnak.Nama, wargaAnak.NIK);

            const string query = @"
            UPDATE IZIN
            SET 
                ID_Warga_Anak = @ID_Warga_Anak,
                NegaraTujuan = @NegaraTujuan,
                NamaPT = @NamaPT
            WHERE ID_Surat = @ID_Surat";

            var affectedRows = await _connection.ExecuteAsync(query, new
            {
                izinData.ID_Surat,
                izinData.ID_Anak,
                izinData.NegaraTujuan,
                izinData.NamaPT
            }, transaction);
            _logger.LogInformation("Data Izin Ortu diperbarui untuk ID_Surat: {ID_Surat}, Baris terpengaruh: {AffectedRows}, NamaAnak: {NamaAnak}",
                izinData.ID_Surat, affectedRows, izinData.NamaAnak ?? "null");
            return affectedRows;
        }

        public async Task<int> DeleteAsync(int idSurat, IDbTransaction? transaction = null)
        {
            _logger.LogInformation("Menghapus data Izin Ortu untuk ID_Surat: {ID_Surat}", idSurat);
            const string query = "DELETE FROM IZIN WHERE ID_Surat = @ID_Surat";
            return await _connection.ExecuteAsync(query, new { ID_Surat = idSurat }, transaction);
        }

        public async Task InitializeAsync()
        {
            // Create IZIN table if not exists
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS IZIN (
                    ID_Surat INTEGER PRIMARY KEY,
                    ID_Warga_Anak INTEGER NOT NULL,
                    NegaraTujuan TEXT NOT NULL,
                    NamaPT TEXT,
                    FOREIGN KEY (ID_Surat) REFERENCES Surat(ID_Surat) ON DELETE CASCADE,
                    FOREIGN KEY (ID_Warga_Anak) REFERENCES Warga(ID_Warga)
                )";
            await _connection.ExecuteAsync(createTableSql);
        }
    }
}
