using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public partial class WargaRepository : IWargaRepository
    {

        private bool IsDummyNik(string nik) => nik == DummyNikKematian || nik == DummyNikInstansi;

        private string BuildAlamatLengkap(WargaData warga)
        {
            if (warga == null) return string.Empty;

            var alamatParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(warga.Dusun))
                alamatParts.Add(warga.Dusun);
            var rtRw = GeneratorPdf.AlamatFormatter.FormatRtRw(warga.RT, warga.RW);
            if (rtRw.Length > 0)
                alamatParts.Add(rtRw);
            if (!string.IsNullOrWhiteSpace(warga.Desa))
                alamatParts.Add(warga.Desa);
            if (!string.IsNullOrWhiteSpace(warga.Kecamatan))
                alamatParts.Add(warga.Kecamatan);
            if (!string.IsNullOrWhiteSpace(warga.Kabupaten))
                alamatParts.Add(warga.Kabupaten);

            return string.Join(", ", alamatParts);
        }
        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData)
        {
            return await AddOrUpdateWargaAsync(wargaData);
        }

        public async Task<int> GetOrCreateDummyWargaAsync(SqliteConnection connection, IDbTransaction transaction)
        {
            const string dummyNik = "9999999999999999";
            const string dummyName = "INSTANSI DUMMY";

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var existing = await connection.QueryFirstOrDefaultAsync<WargaData>(
                    "SELECT * FROM Warga WHERE NIK = @NIK",
                    new { NIK = dummyNik },
                    transaction);

                if (existing != null)
                {
                    return existing.ID_Warga;
                }

                var dummyWarga = new WargaData
                {
                    NIK = dummyNik,
                    Nama = dummyName,
                    TempatLahir = "N/A",
                    TanggalLahir = "1900-01-01",
                    JenisKelamin = "N/A",
                    Dusun = "N/A",
                    Desa = "N/A",
                    Kecamatan = "N/A",
                    Kabupaten = "N/A",
                    Agama = "N/A",
                    StatusPerkawinan = "N/A",
                    Pekerjaan = "N/A",
                    Pendidikan = "N/A",
                    Kewarganegaraan = "N/A",
                    IsForInstansi = true
                };

                var idWarga = await connection.ExecuteScalarAsync<int>(
                    @"INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, 
                       Dusun, Desa, Kecamatan, Kabupaten, Agama, StatusPerkawinan, Pekerjaan, Pendidikan, Kewarganegaraan)
                     VALUES (@NIK, @Nama, @TempatLahir, @TanggalLahir, @JenisKelamin, 
                             @Dusun, @Desa, @Kecamatan, @Kabupaten, @Agama, @StatusPerkawinan, 
                             @Pekerjaan, @Pendidikan, @Kewarganegaraan);
                     SELECT last_insert_rowid();",
                    dummyWarga, transaction);

                if (idWarga <= 0)
                    throw new DataAccessException("Gagal membuat dummy warga (Instansi).");

                return idWarga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create dummy warga (Instansi)");
                throw new DataAccessException("Failed to get or create dummy warga (Instansi)", ex);
            }
        }

        public async Task<int> AddOrUpdateWargaAndGetIdAsync(WargaData wargaData, SqliteConnection connection, IDbTransaction transaction)
        {
            return await AddOrUpdateWargaAsync(wargaData, connection, transaction);
        }

        // =====================================================================
        // Daftar halaman, ekspor, mutasi status, dan Kartu Keluarga
        // =====================================================================

        /// <summary>Syarat SQL yang selalu menyaring baris dummy Instansi/Kematian.</summary>
        private const string SyaratBukanDummy =
            "(NIK IS NULL OR NIK NOT IN ('9999999999999999','0000000000000000'))";

        /// <summary>Susun syarat WHERE + parameter dari filter halaman/ekspor. Semua opsional.</summary>
        private static void BangunFilterWhere(WargaFilter filter, List<string> syarat, Dictionary<string, object> param)
        {
            if (!string.IsNullOrWhiteSpace(filter.Cari))
            {
                // Pencarian bebas mencakup NIK, nama, No KK, alamat, nomor HP,
                // dan RT — operator mengetik \"003\" untuk menemukan alamat.
                syarat.Add("(NIK LIKE @Cari OR Nama LIKE @Cari OR NoKK LIKE @Cari OR AlamatDetail LIKE @Cari OR NomorHP LIKE @Cari OR RT LIKE @Cari)");
                param["Cari"] = $"%{filter.Cari.Trim()}%";
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                syarat.Add("UPPER(COALESCE(StatusWarga,'')) = @Status");
                param["Status"] = filter.Status.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(filter.RT))
            {
                syarat.Add("RT = @RT");
                param["RT"] = filter.RT.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filter.RW))
            {
                syarat.Add("RW = @RW");
                param["RW"] = filter.RW.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filter.Dusun))
            {
                // Dropdown wilayah menyimpan dusun huruf besar sebagai kunci.
                syarat.Add("UPPER(COALESCE(Dusun,'')) = @Dusun");
                param["Dusun"] = filter.Dusun.Trim().ToUpperInvariant();
            }
        }

        public async Task<WargaPagedResult> GetWargaPageAsync(WargaFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new WargaFilter();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var syarat = new List<string> { SyaratBukanDummy };
                var param = new Dictionary<string, object>();
                BangunFilterWhere(filter, syarat, param);
                string where = "WHERE " + string.Join(" AND ", syarat);

                int total = await _connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM Warga {where}", param);

                int ukuran = filter.UkuranHalamanEfektif;
                int totalHalaman = Math.Max(1, (int)Math.Ceiling(total / (double)ukuran));
                // Halaman melebihi batas dikembalikan ke halaman terakhir supaya
                // operator tidak pernah melihat tabel kosong karena salah klik.
                int halaman = Math.Min(Math.Max(1, filter.HalamanEfektif), totalHalaman);

                var items = (await _connection.QueryAsync<WargaData>(
                    $"SELECT * FROM Warga {where} " +
                    "ORDER BY COALESCE(Nama,'') COLLATE NOCASE, ID_Warga " +
                    "LIMIT @Ambil OFFSET @Lewati",
                    new Dictionary<string, object>(param)
                    {
                        ["Ambil"] = ukuran,
                        ["Lewati"] = (halaman - 1) * ukuran
                    })).ToList();

                foreach (var warga in items)
                {
                    warga.StatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);
                }

                return new WargaPagedResult
                {
                    Items = items,
                    Total = total,
                    Halaman = halaman,
                    UkuranHalaman = ukuran
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca halaman daftar warga");
                throw new DataRetrievalException("Gagal membaca halaman daftar warga", ex);
            }
        }

        public async Task<List<WargaData>> GetWargaForExportAsync(WargaFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new WargaFilter();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var syarat = new List<string> { SyaratBukanDummy };
                var param = new Dictionary<string, object>();
                BangunFilterWhere(filter, syarat, param);

                var hasil = (await _connection.QueryAsync<WargaData>(
                    "SELECT * FROM Warga WHERE " + string.Join(" AND ", syarat) +
                    " ORDER BY COALESCE(Nama,'') COLLATE NOCASE",
                    param)).ToList();

                foreach (var warga in hasil)
                {
                    warga.StatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);
                }

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan ekspor daftar warga");
                throw new DataRetrievalException("Gagal menyiapkan ekspor daftar warga", ex);
            }
        }

    }
}
