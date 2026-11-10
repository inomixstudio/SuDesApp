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

        public async Task<bool> UbahStatusWargaAsync(
            int idWarga, string status, string? tanggalStatus, string? keterangan,
            CancellationToken cancellationToken = default)
        {
            if (idWarga <= 0)
                throw new ArgumentException("ID warga tidak sah.", nameof(idWarga));

            // Status tidak dikenal ditolak: salah ketik tidak boleh merusak
            // data status warga yang membedakan penduduk dari arsip.
            if (!StatusWargaTipe.Valid(status))
                throw new ArgumentException($"Status '{status}' tidak dikenal.", nameof(status));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                // Keterangan hanya ditulis bila benar-benar dikirim. Sebelumnya
                // `keterangan ?? string.Empty` selalu menulis '' — callable dari
                // dropdown mutasi per baris tidak pernah mengirimkannya, sehingga
                // catatan sebab pindah/meninggal milik warga TERHAPUS tiap kali
                // statusnya diubah. Catatan itu satu-satunya rekaman alasan
                // mutasi, dan hilang tanpa bisa dipulihkan dari aplikasi.
                bool adaKeterangan = !string.IsNullOrWhiteSpace(keterangan);
                int baris = await _connection.ExecuteAsync(
                    @"UPDATE Warga
                      SET StatusWarga = @Status,
                          TanggalStatus = @Tanggal" +
                          (adaKeterangan ? ", KeteranganWarga = @Keterangan" : string.Empty) + @"
                      WHERE ID_Warga = @ID",
                    new
                    {
                        Status = StatusWargaTipe.Normalisasi(status),
                        Tanggal = tanggalStatus ?? string.Empty,
                        Keterangan = adaKeterangan ? keterangan!.Trim() : string.Empty,
                        ID = idWarga
                    });

                if (baris > 0)
                {
                    await _cacheService.RemoveAsync<int?>("Warga_Count");
                    await _cacheService.RemoveAsync<WargaData>($"Warga_Id_{idWarga}");
                    await _cacheService.RemoveByPrefixAsync("Warga_Search_");
                    await _cacheService.RemoveAsync<WargaStatistikRingkasan>("Warga_Statistik");
                }

                return baris > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status warga ID {ID}", idWarga);
                throw new DataAccessException($"Gagal mengubah status warga ID {idWarga}", ex);
            }
        }

        /// <summary>Tabel KartuKeluarga dibuat bila belum ada (database lama).</summary>
        private async Task EnsureKartuKeluargaTableAsync()
        {
            var ada = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KartuKeluarga'");
            if (ada > 0) return;

            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS KartuKeluarga (
                    NoKK TEXT PRIMARY KEY,
                    NamaKepalaKeluarga TEXT,
                    Alamat TEXT,
                    RT TEXT,
                    RW TEXT,
                    Dusun TEXT,
                    Desa TEXT,
                    Kecamatan TEXT,
                    Kabupaten TEXT,
                    NomorHP TEXT,
                    Catatan TEXT,
                    JumlahAnggota INTEGER NOT NULL DEFAULT 0,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )");
        }

        public async Task SaveKartuKeluargaAsync(KartuKeluargaData kartuKeluarga, CancellationToken cancellationToken = default)
        {
            if (kartuKeluarga == null)
                throw new ArgumentNullException(nameof(kartuKeluarga));
            if (string.IsNullOrWhiteSpace(kartuKeluarga.NoKK))
                throw new ArgumentException("Nomor Kartu Keluarga wajib diisi.", nameof(kartuKeluarga));

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                await EnsureKartuKeluargaTableAsync();

                await _connection.ExecuteAsync(@"
                    INSERT INTO KartuKeluarga
                        (NoKK, NamaKepalaKeluarga, Alamat, RT, RW, Dusun, Desa, Kecamatan, Kabupaten, NomorHP, Catatan, UpdatedAt)
                    VALUES
                        (@NoKK, @NamaKepalaKeluarga, @Alamat, @RT, @RW, @Dusun, @Desa, @Kecamatan, @Kabupaten, @NomorHP, @Catatan, CURRENT_TIMESTAMP)
                    ON CONFLICT(NoKK) DO UPDATE SET
                        NamaKepalaKeluarga = excluded.NamaKepalaKeluarga,
                        Alamat = excluded.Alamat,
                        RT = excluded.RT,
                        RW = excluded.RW,
                        Dusun = excluded.Dusun,
                        Desa = excluded.Desa,
                        Kecamatan = excluded.Kecamatan,
                        Kabupaten = excluded.Kabupaten,
                        NomorHP = excluded.NomorHP,
                        Catatan = excluded.Catatan,
                        UpdatedAt = CURRENT_TIMESTAMP",
                    kartuKeluarga);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan Kartu Keluarga {NoKK}", kartuKeluarga.NoKK);
                throw new DataAccessException("Gagal menyimpan Kartu Keluarga", ex);
            }
        }

        public async Task<KartuKeluargaData?> GetKartuKeluargaAsync(string noKk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(noKk)) return null;

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                await EnsureKartuKeluargaTableAsync();

                var kk = await _connection.QueryFirstOrDefaultAsync<KartuKeluargaData>(
                    "SELECT * FROM KartuKeluarga WHERE NoKK = @NoKK",
                    new { NoKK = noKk.Trim() });
                if (kk == null) return null;

                // Jumlah anggota selalu dihitung dari tabel Warga (sumber sebenarnya).
                kk.JumlahAnggota = await _connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Warga WHERE NoKK = @NoKK AND " + SyaratBukanDummy,
                    new { kk.NoKK });

                return kk;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca Kartu Keluarga {NoKK}", noKk);
                throw new DataRetrievalException("Gagal membaca Kartu Keluarga", ex);
            }
        }

        public async Task<List<WargaData>> GetAnggotaKeluargaAsync(string noKk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(noKk)) return new List<WargaData>();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var hasil = (await _connection.QueryAsync<WargaData>(
                    "SELECT * FROM Warga WHERE NoKK = @NoKK AND " + SyaratBukanDummy +
                    " ORDER BY COALESCE(Nama,'') COLLATE NOCASE",
                    new { NoKK = noKk.Trim() })).ToList();

                foreach (var warga in hasil)
                    warga.AlamatLengkap = BuildAlamatLengkap(warga);

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca anggota Kartu Keluarga {NoKK}", noKk);
                throw new DataRetrievalException("Gagal membaca anggota Kartu Keluarga", ex);
            }
        }

        public async Task<IEnumerable<string>> GetDaftarNilaiWilayahAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                async Task<List<string>> AmbilAsync(string kolom, string? awalan)
                {
                    var rows = await _connection.QueryAsync<string>(new CommandDefinition(
                        $"SELECT DISTINCT TRIM({kolom}) FROM Warga " +
                        $"WHERE {kolom} IS NOT NULL AND TRIM({kolom}) <> '' " +
                        $"ORDER BY TRIM({kolom})",
                        cancellationToken: cancellationToken));

                    var hasilKolom = new List<string>();
                    foreach (var v in rows)
                    {
                        if (string.IsNullOrWhiteSpace(v)) continue;
                        var teks = v.Trim();
                        // Awalan "RT"/"RW" dipakai halaman Data Warga untuk memisahkan
                        // nilai RT dan RW yang keduanya sering hanya berupa angka.
                        hasilKolom.Add(awalan == null ? teks : awalan + " " + teks);
                    }
                    return hasilKolom;
                }

                var hasil = new List<string>();
                hasil.AddRange(await AmbilAsync("RT", "RT"));
                hasil.AddRange(await AmbilAsync("RW", "RW"));
                hasil.AddRange(await AmbilAsync("Dusun", null));

                return hasil
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil daftar nilai wilayah warga");
                return Array.Empty<string>();
            }
        }

        public async Task<Dictionary<int, int>> GetJumlahSuratSemuaWargaAsync(CancellationToken cancellationToken = default)
        {
            var hasil = new Dictionary<int, int>();
            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var baris = await _connection.QueryAsync(new CommandDefinition(
                    @"SELECT ID_Warga, COUNT(*) AS Jumlah
                      FROM Surat
                      WHERE ID_Warga IS NOT NULL AND ID_Warga > 0
                      GROUP BY ID_Warga",
                    cancellationToken: cancellationToken));

                foreach (var b in baris)
                {
                    int id = Convert.ToInt32(b.ID_Warga);
                    if (id > 0) hasil[id] = Convert.ToInt32(b.Jumlah);
                }
            }
            catch (Exception ex)
            {
                // Kolom "Jumlah Surat" hanyalah pelengkap ekspor: kegagalan hitung
                // tidak boleh menggagalkan seluruh berkas, jadi cukup dicatat.
                _logger.LogError(ex, "Gagal menghitung jumlah surat per warga");
            }

            return hasil;
        }


    }
}
