using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;

namespace SuDesApp.db
{
    public class SuratService
    {
        private readonly AppConfig _config;
        private readonly ILogger<DatabaseService> _logger;
        private readonly JenisSuratService _jenisSuratService;
        private readonly WargaService _wargaService;
        private readonly IzinOrtuService _izinOrtuService;
        private readonly ConcurrentDictionary<int, SuratData> _cachedSuratData;

        public SuratService(
            AppConfig config,
            ILogger<DatabaseService> logger, // Atau ILogger<SuratService> jika dependensi diubah
            JenisSuratService jenisSuratService,
            WargaService wargaService,
            IzinOrtuService izinOrtuService,
            ConcurrentDictionary<int, SuratData> cachedSuratData)
        {
            _config = config;
            _logger = logger;
            _jenisSuratService = jenisSuratService;
            _wargaService = wargaService;
            _izinOrtuService = izinOrtuService;
            _cachedSuratData = cachedSuratData;
        }

        public async Task<int> AddSuratAsync(SuratData suratData)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));
            _logger.LogInformation("AddSuratAsync dimulai: NomorSurat={NomorSurat}, NamaJenis={NamaJenis}", suratData.NomorSurat, suratData.NamaJenis);

            var validationErrors = await suratData.ValidateAsync();
            if (validationErrors.Any())
            {
                throw new ValidationException($"Validasi gagal: {string.Join("; ", validationErrors)}");
            }

            var jenisSurat = await _jenisSuratService.GetJenisSuratByNamaAsync(suratData.NamaJenis.ToUpperInvariant())
                ?? throw new ArgumentException($"Jenis surat '{suratData.NamaJenis}' tidak valid.");
            suratData.ID_Jenis = jenisSurat.ID_Jenis;
            suratData.SetJenisFromNamaJenis(suratData.NamaJenis); // Pastikan enum Jenis di SuratData terisi

            if (string.IsNullOrWhiteSpace(suratData.NomorSurat))
            {
                suratData.NomorSurat = await _jenisSuratService.GenerateNomorSuratAsync(jenisSurat.KodeJenis);
            }

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 1. Simpan/Update Warga Pemberi Izin (Orang Tua)
                if (suratData.Warga == null && suratData.NamaJenis?.ToUpperInvariant() != "INSTANSI")
                {
                    throw new InvalidOperationException("Data Warga (pemberi izin/pemohon) tidak boleh kosong.");
                }

                int idPemberiIzinWarga = 0; // ID Warga yang akan disimpan di tabel Surat
                if (suratData.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                {
                    idPemberiIzinWarga = await _wargaService.GetOrCreateDummyWargaAsync(connection, transaction);
                    // Jika ada penanggung jawab instansi, data penanggung jawab disimpan terpisah di tabel Warga
                    if (suratData.Warga != null && !string.IsNullOrWhiteSpace(suratData.Warga.NIK) && suratData.Warga.NIK != "9999999999999999")
                    {
                        suratData.Warga.isForInstansi = true;
                        await _wargaService.AddOrUpdateWargaAndGetIdAsync(suratData.Warga, connection, transaction);
                        // ID_Warga di tabel Surat tetap merujuk ke dummy instansi
                    }
                }
                else if (suratData.Warga != null)
                {
                    suratData.Warga.isForKematian = (suratData.NamaJenis?.ToUpperInvariant() == "KEMATIAN");
                    idPemberiIzinWarga = await _wargaService.AddOrUpdateWargaAndGetIdAsync(suratData.Warga, connection, transaction);
                    suratData.Warga.ID_Warga = idPemberiIzinWarga; // Update ID_Warga di objek suratData.Warga
                }
                if (idPemberiIzinWarga <= 0 && suratData.NamaJenis?.ToUpperInvariant() != "INSTANSI") // Untuk instansi, bisa jadi 0 jika dummy belum dibuat, tapi GetOrCreateDummyWargaAsync harusnya handle
                {
                    throw new InvalidOperationException("Gagal mendapatkan ID Warga untuk pemohon/pemberi izin.");
                }


                // 2. Simpan Data Surat Utama
                var suratId = await InsertSuratDataInternalAsync(connection, transaction, suratData, idPemberiIzinWarga);
                suratData.ID_Surat = suratId; // Set ID_Surat yang baru dibuat

                // 3. Simpan Data Terkait Spesifik Jenis Surat
                await InsertRelatedDataAsync(connection, transaction, suratId, suratData);

                await transaction.CommitAsync();
                _cachedSuratData[suratId] = suratData;
                _logger.LogInformation("Surat berhasil disimpan: ID_Surat={SuratId}, NomorSurat={NomorSurat}", suratId, suratData.NomorSurat);
                return suratId;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains("UNIQUE constraint failed: Surat.NomorSurat"))
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "NomorSurat {NomorSurat} sudah ada. Mencoba generate ulang.", suratData.NomorSurat);
                // Opsi: coba generate ulang dan panggil AddSuratAsync lagi (hati-hati dengan rekursi tak terbatas)
                // atau lempar exception agar UI bisa menangani (misal, minta pengguna konfirmasi nomor atau edit)
                throw new DataAccessException($"Nomor surat '{suratData.NomorSurat}' sudah digunakan. Silakan coba lagi atau periksa nomor surat.", ex);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Gagal menyimpan surat: NomorSurat={NomorSurat}", suratData.NomorSurat);
                throw; // Lempar ulang exception asli
            }
        }

        private async Task<int> InsertSuratDataInternalAsync(SqliteConnection connection, IDbTransaction transaction, SuratData suratData, int idWargaUntukSurat)
        {
            // Metode ini hanya untuk INSERT ke tabel Surat
            _logger.LogDebug("Internal Insert Surat: NomorSurat={NomorSurat}, ID_Jenis={ID_Jenis}, ID_Warga_FK_Surat={IDWargaUntukSurat}",
               suratData.NomorSurat, suratData.ID_Jenis, idWargaUntukSurat);

            var query = @"INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keterangan, Keperluan, ID_Warga)
                          VALUES (@ID_Jenis, @NomorSurat, @TanggalSurat, @Keterangan, @Keperluan, @ID_Warga);
                          SELECT last_insert_rowid();";
            var suratId = await connection.ExecuteScalarAsync<int>(query, new
            {
                suratData.ID_Jenis,
                suratData.NomorSurat,
                TanggalSurat = suratData.TanggalSurat.ToString("yyyy-MM-dd"),
                suratData.Keterangan,
                suratData.Keperluan,
                ID_Warga = idWargaUntukSurat // ID Warga yang akan disimpan di tabel Surat (Pemberi Izin/Pemohon/Dummy)
            }, transaction);

            if (suratId <= 0)
            {
                throw new DataAccessException("Gagal menyimpan surat utama, ID_Surat tidak valid setelah insert.");
            }
            return suratId;
        }

        public async Task<int> UpdateSuratAsync(int idSurat, SuratData suratData)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));
            _logger.LogInformation("UpdateSuratAsync dimulai: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                idSurat, suratData.NamaJenis, suratData.Warga?.Nama ?? "null", suratData.Warga?.NIK ?? "null");

            var validationErrors = await suratData.ValidateAsync();
            if (validationErrors.Any())
            {
                throw new ValidationException($"Validasi gagal: {string.Join("; ", validationErrors)}");
            }

            var jenisSurat = await _jenisSuratService.GetJenisSuratByNamaAsync(suratData.NamaJenis.ToUpperInvariant())
                ?? throw new ArgumentException($"Jenis surat '{suratData.NamaJenis}' tidak valid.");
            suratData.ID_Jenis = jenisSurat.ID_Jenis;
            suratData.SetJenisFromNamaJenis(suratData.NamaJenis);

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(suratData.NomorSurat))
                {
                    var existingSuratHeader = await connection.QueryFirstOrDefaultAsync<SuratData>(
                        "SELECT NomorSurat FROM Surat WHERE ID_Surat = @ID_Surat", new { ID_Surat = idSurat });
                    suratData.NomorSurat = existingSuratHeader?.NomorSurat ?? await _jenisSuratService.GenerateNomorSuratAsync(jenisSurat.KodeJenis);
                }

                // 1. Update Warga Pemberi Izin (Orang Tua/Pemohon)
                int idPemberiIzinWarga = 0;
                if (suratData.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                {
                    idPemberiIzinWarga = await _wargaService.GetOrCreateDummyWargaAsync(connection, transaction);
                    if (suratData.Warga != null && !string.IsNullOrWhiteSpace(suratData.Warga.NIK) && suratData.Warga.NIK != "9999999999999999")
                    {
                        suratData.Warga.isForInstansi = true;
                        var penanggungJawabId = await _wargaService.AddOrUpdateWargaAndGetIdAsync(suratData.Warga, connection, transaction);
                        _logger.LogDebug("Penanggung jawab INSTANSI: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}",
                            penanggungJawabId, suratData.Warga.Nama, suratData.Warga.NIK);
                    }
                }
                else if (suratData.Warga != null && suratData.Warga.ID_Warga > 0)
                {
                    suratData.Warga.isForKematian = (suratData.NamaJenis?.ToUpperInvariant() == "KEMATIAN");
                    await _wargaService.UpdateWargaAsync(suratData.Warga.ID_Warga, suratData.Warga, connection, transaction, false, suratData.Warga.isForKematian);
                    idPemberiIzinWarga = suratData.Warga.ID_Warga;
                    _logger.LogDebug("Updated Warga: ID_Warga={ID_Warga}, Nama={Nama}, NIK={NIK}", idPemberiIzinWarga, suratData.Warga.Nama, suratData.Warga.NIK);
                }
                else if (suratData.Warga != null)
                {
                    _logger.LogWarning("ID_Warga tidak ada pada objek SuratData saat update. Mencoba AddOrUpdate. ID_Surat: {ID_Surat}", idSurat);
                    suratData.Warga.isForKematian = (suratData.NamaJenis?.ToUpperInvariant() == "KEMATIAN");
                    idPemberiIzinWarga = await _wargaService.AddOrUpdateWargaAndGetIdAsync(suratData.Warga, connection, transaction);
                    suratData.Warga.ID_Warga = idPemberiIzinWarga;
                }
                else
                {
                    throw new InvalidOperationException("Data Warga (pemohon/pemberi izin) tidak boleh kosong untuk jenis surat selain INSTANSI.");
                }

                if (idPemberiIzinWarga <= 0 && suratData.NamaJenis?.ToUpperInvariant() != "INSTANSI")
                {
                    throw new InvalidOperationException("Gagal mendapatkan ID Warga untuk pemohon/pemberi izin saat update.");
                }

                // 2. Update Data Surat Utama
                var rowsAffectedSurat = await connection.ExecuteAsync(
                    @"UPDATE Surat
              SET ID_Jenis = @ID_Jenis, ID_Warga = @ID_Warga, NomorSurat = @NomorSurat,
                  TanggalSurat = @TanggalSurat, Keterangan = @Keterangan, Keperluan = @Keperluan
              WHERE ID_Surat = @ID_Surat_Param",
                    new
                    {
                        ID_Surat_Param = idSurat,
                        suratData.ID_Jenis,
                        ID_Warga = idPemberiIzinWarga,
                        suratData.NomorSurat,
                        TanggalSurat = suratData.TanggalSurat.ToString("yyyy-MM-dd"),
                        suratData.Keterangan,
                        suratData.Keperluan
                    },
                    transaction);

                _logger.LogInformation("Tabel Surat diperbarui untuk ID_Surat: {ID_Surat}, Baris terpengaruh: {RowsAffected}", idSurat, rowsAffectedSurat);

                // 3. Update Data Terkait Spesifik Jenis Surat
                await DeleteRelatedDataAsync(connection, transaction, idSurat, suratData.NamaJenis);
                await InsertRelatedDataAsync(connection, transaction, idSurat, suratData);

                // 4. Update IzinOrtu jika jenis surat IZIN_ORTU
                if (suratData.NamaJenis?.ToUpperInvariant() == "IZIN_ORTU" && suratData.IzinOrtu != null)
                {
                    await _izinOrtuService.UpdateIzinOrtuAsync(suratData.IzinOrtu, connection, transaction);
                    _logger.LogDebug("Updated IzinOrtu: ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}, NamaAnak={NamaAnak}",
                        idSurat, suratData.IzinOrtu.ID_Warga_Anak, suratData.IzinOrtu.NamaAnak ?? "null");
                }

                await transaction.CommitAsync();
                suratData.ID_Surat = idSurat;
                _cachedSuratData.TryRemove(idSurat, out _); // Hapus cache lama
                _cachedSuratData[idSurat] = suratData; // Simpan data baru ke cache
                _logger.LogInformation("Surat berhasil diperbarui: ID_Surat={ID_Surat}, NomorSurat={NomorSurat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                    idSurat, suratData.NomorSurat, suratData.Warga?.Nama ?? "null", suratData.Warga?.NIK ?? "null");
                return rowsAffectedSurat;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains("UNIQUE constraint failed: Surat.NomorSurat"))
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "NomorSurat {NomorSurat} sudah ada saat update untuk ID_Surat {ID_Surat}. Proses dibatalkan.", suratData.NomorSurat, idSurat);
                throw new DataAccessException($"Nomor surat '{suratData.NomorSurat}' sudah digunakan oleh surat lain.", ex);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Gagal memperbarui surat: ID_Surat={ID_Surat}, Warga.Nama={WargaNama}", idSurat, suratData.Warga?.Nama ?? "null");
                throw;
            }
        }

        private async Task<int> InsertSuratDataAsync(SqliteConnection connection, IDbTransaction transaction, SuratData suratData)
        {
            if (suratData.ID_Jenis <= 0 || string.IsNullOrWhiteSpace(suratData.NomorSurat) || suratData.TanggalSurat == default)
            {
                _logger.LogError("Invalid SuratData for insert: ID_Jenis={ID_Jenis}, NomorSurat={NomorSurat}, TanggalSurat={TanggalSurat}",
                    suratData.ID_Jenis, suratData.NomorSurat, suratData.TanggalSurat);
                throw new ArgumentException("Data surat tidak lengkap untuk insert.");
            }
            // ID_Warga di tabel Surat harus merujuk ke Warga yang relevan (pemberi izin atau dummy instansi)
            if (suratData.Warga == null || suratData.Warga.ID_Warga <= 0)
            {
                _logger.LogError("Invalid Warga data (ID_Warga) in SuratData for insert: WargaIsNull={WargaIsNull}, ID_Warga={ID_Warga}",
                   suratData.Warga == null, suratData.Warga?.ID_Warga);
                // Untuk INSTANSI, ID_Warga akan merujuk ke dummy. Untuk yang lain, harus ID_Warga yang valid.
                if (suratData.NamaJenis?.ToUpperInvariant() != "INSTANSI")
                {
                    throw new ArgumentException("Data Warga (pemberi izin) tidak valid untuk insert.");
                }
                // Jika INSTANSI dan ID_Warga belum diset (misalnya dari GetOrCreateDummyWargaAsync), ini problem.
                // Namun, logika di AddSuratAsync dan UpdateSuratAsync seharusnya sudah memastikan suratData.Warga.ID_Warga terisi dengan benar.
            }


            _logger.LogDebug("Inserting Surat: NomorSurat={NomorSurat}, ID_Jenis={ID_Jenis}, ID_Warga (FK in Surat Table)={ID_Warga_FK}",
                suratData.NomorSurat, suratData.ID_Jenis, suratData.Warga.ID_Warga);

            var query = @"INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keterangan, Keperluan, ID_Warga)
                          VALUES (@ID_Jenis, @NomorSurat, @TanggalSurat, @Keterangan, @Keperluan, @ID_Warga);
                          SELECT last_insert_rowid();";
            var suratId = await connection.ExecuteScalarAsync<int>(query, new
            {
                suratData.ID_Jenis,
                suratData.NomorSurat,
                TanggalSurat = suratData.TanggalSurat.ToString("yyyy-MM-dd"),
                suratData.Keterangan,
                suratData.Keperluan,
                ID_Warga = suratData.Warga.ID_Warga // Ini adalah ID_Warga yang akan disimpan di tabel Surat
            }, transaction);

            if (suratId <= 0)
            {
                _logger.LogError("Failed to insert Surat: ID_Surat={SuratId}, NomorSurat={NomorSurat}", suratId, suratData.NomorSurat);
                throw new DataAccessException("Gagal menyimpan surat, ID_Surat tidak valid setelah insert.");
            }
            return suratId;
        }

        public async Task<SuratData?> GetSuratDataAsync(int idSurat)
        {
            if (_cachedSuratData.TryGetValue(idSurat, out var cachedData))
            {
                _logger.LogInformation("Cache hit for ID_Surat={idSurat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                    idSurat, cachedData.Warga?.Nama ?? "null", cachedData.Warga?.NIK ?? "null");
                if (cachedData.Desa == null || string.IsNullOrWhiteSpace(cachedData.Desa.NamaDesa))
                {
                    await cachedData.EnsureDesaDataLoadedAsync();
                }
                return cachedData;
            }

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            _logger.LogInformation("Cache miss for ID_Surat={idSurat}. Fetching from database.", idSurat);

            string sqlSuratDesaWarga = @"
        SELECT
            s.ID_Surat, s.ID_Jenis, UPPER(js.NamaJenis) AS NamaJenis, s.NomorSurat, s.TanggalSurat, s.Keterangan, s.Keperluan,
            s.ID_Warga AS ID_Warga_Pemohon_FK,
            d.NamaDesa, d.Kecamatan, d.Kabupaten, d.Alamat,
            d.Kodepos, d.KepalaDesa, d.SekretarisDesa, d.NamaCamat, d.NipCamat, d.GolCamat,
            w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, 
            w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan, w.Alamat, w.Pendidikan, w.Kewarganegaraan,
            i.NamaInstansi, i.AlamatInstansi
        FROM Surat s
        INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
        LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
        LEFT JOIN InfoDesa d ON 1=1
        LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat AND js.NamaJenis = 'INSTANSI'
        WHERE s.ID_Surat = @idSurat;";

            try
            {
                SuratData suratDataResult = null;
                var result = await connection.QueryAsync<SuratData, DesaData, WargaData, Instansi, SuratData>(
                    sqlSuratDesaWarga,
                    (surat, desa, warga, instansi) =>
                    {
                        _logger.LogDebug("Mapping Surat: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}, Instansi.NamaInstansi={NamaInstansi}",
                            surat.ID_Surat, surat.NamaJenis, warga?.Nama ?? "null", warga?.NIK ?? "null", instansi?.NamaInstansi ?? "null");
                        surat.Desa = desa ?? new DesaData();
                        surat.Warga = warga;
                        if (surat.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                        {
                            surat.Instansi = instansi ?? new Instansi { NamaInstansi = "[No Instansi]", AlamatInstansi = "[No Address]" };
                        }
                        surat.SetJenisFromNamaJenis(surat.NamaJenis);
                        return surat;
                    },
                    new { idSurat },
                    splitOn: "ID_Jenis,NamaDesa,ID_Warga,NamaInstansi"
                );
                suratDataResult = result.FirstOrDefault();

                if (suratDataResult != null)
                {
                    suratDataResult.Kematian ??= new KematianData();
                    suratDataResult.SKU ??= new SKUData();
                    suratDataResult.Instansi ??= new Instansi();
                    suratDataResult.IzinOrtu ??= new IzinOrtuData();
                    suratDataResult.RincianGarapans ??= new List<GarapanData>();

if (suratDataResult.Jenis == SuratData.JenisSuratEnum.IzinOrtu)
                    {
                        suratDataResult.IzinOrtu = await _izinOrtuService.GetIzinOrtuAsync(idSurat);
                        _logger.LogDebug("IzinOrtu data: ID_Surat={ID_Surat}, ID_Warga_Anak={ID_Warga_Anak}, NamaAnak={NamaAnak}",
                            idSurat, suratDataResult.IzinOrtu?.ID_Warga_Anak ?? 0, suratDataResult.IzinOrtu?.NamaAnak ?? "null");
                        if (suratDataResult.IzinOrtu == null)
                        {
                            _logger.LogWarning("Data IzinOrtu tidak ditemukan untuk ID_Surat={ID_Surat}.", idSurat);
                            suratDataResult.IzinOrtu = new IzinOrtuData();
                        }
                    }
                    else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "KEMATIAN")
                    {
                        var kematianDetail = await connection.QueryFirstOrDefaultAsync<KematianData>(
                            "SELECT HariKematian, TanggalKematian, PukulKematian, PenyebabKematian, NIKPelapor, NamaPelapor, AgamaPelapor, UmurPelapor, PekerjaanPelapor, AlamatPelapor, HubunganPelapor FROM Kematian WHERE ID_Surat = @idSurat", new { idSurat });
                        if (kematianDetail != null)
                        {
                            suratDataResult.Kematian = kematianDetail;
                            _logger.LogDebug("Kematian data: ID_Surat={ID_Surat}, NamaPelapor={NamaPelapor}, Warga.Nama={WargaNama}",
                                idSurat, kematianDetail.NamaPelapor ?? "null", suratDataResult.Warga?.Nama ?? "null");
                        }
                    }
                    else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "SKU")
                    {
                        var skuData = await connection.QueryFirstOrDefaultAsync<SKUData>(
                            "SELECT BidangUsaha, SejakTahun FROM SKU WHERE ID_Surat = @idSurat", new { idSurat });
                        if (skuData != null) suratDataResult.SKU = skuData;
                    }
                    else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                    {
                        if (suratDataResult.Instansi == null || string.IsNullOrWhiteSpace(suratDataResult.Instansi.NamaInstansi))
                        {
                            _logger.LogWarning("No Instansi data found for ID_Surat={idSurat}. Using default.", idSurat);
                            suratDataResult.Instansi = new Instansi { NamaInstansi = "[No Instansi]", AlamatInstansi = "[No Address]" };
                        }
                        // Jika ada penanggung jawab instansi, ambil data warga penanggung jawab
                        if (suratDataResult.Warga?.NIK != "9999999999999999")
                        {
                            var penanggungJawab = await _wargaService.GetWargaByNikAsync(suratDataResult.Warga.NIK);
                            if (penanggungJawab != null)
                            {
                                suratDataResult.Warga = penanggungJawab;
                                _logger.LogDebug("Penanggung jawab INSTANSI: ID_Surat={idSurat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                                    idSurat, penanggungJawab.Nama, penanggungJawab.NIK);
                            }
                        }
                    }
                    else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "GARAPAN_SAWAH")
                    {
                        var rincianGarapans = await connection.QueryAsync<GarapanData>(
                            "SELECT ID_GarapanItem, Luas, Lokasi, PemilikTanah, NomorPersil, KeteranganGarapan FROM Garapan WHERE ID_Surat = @idSurat", new { idSurat });
                        suratDataResult.RincianGarapans = rincianGarapans.ToList();
                    }
                    else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "BEDANAMA")
                    {
                        var bedaNama = await connection.QueryFirstOrDefaultAsync<dynamic>(
                            "SELECT ID_Warga, SumberDataKoreksi, SumberDataKeliru, AlasanPerbedaan, " +
                            "NIK2, Nama2, TempatLahir2, TanggalLahir2, JenisKelamin2, Dusun2, Desa2, Kecamatan2, Kabupaten2 " +
                            "FROM BedaNama WHERE ID_Surat = @idSurat", new { idSurat });
                        if (bedaNama != null)
                        {
                            suratDataResult.DataSource1 = Convert.ToString(bedaNama.SumberDataKoreksi);
                            suratDataResult.DataSource2 = Convert.ToString(bedaNama.SumberDataKeliru);
                            var alasanPerbedaan = Convert.ToString(bedaNama.AlasanPerbedaan);
                            if (!string.IsNullOrWhiteSpace(alasanPerbedaan))
                                suratDataResult.Keterangan = alasanPerbedaan;
                            suratDataResult.WargaKK = new WargaData
                            {
                                NIK = Convert.ToString(bedaNama.NIK2),
                                Nama = Convert.ToString(bedaNama.Nama2),
                                TempatLahir = Convert.ToString(bedaNama.TempatLahir2),
                                TanggalLahir = Convert.ToString(bedaNama.TanggalLahir2),
                                JenisKelamin = Convert.ToString(bedaNama.JenisKelamin2),
                                Dusun = Convert.ToString(bedaNama.Dusun2),
                                Desa = Convert.ToString(bedaNama.Desa2),
                                Kecamatan = Convert.ToString(bedaNama.Kecamatan2),
                                Kabupaten = Convert.ToString(bedaNama.Kabupaten2)
                            };
                            suratDataResult.WargaKK.Alamat = string.Join(", ",
                                new[] { suratDataResult.WargaKK.Dusun, suratDataResult.WargaKK.Desa, suratDataResult.WargaKK.Kecamatan, suratDataResult.WargaKK.Kabupaten }
                                    .Where(a => !string.IsNullOrWhiteSpace(a)));
                        }
                    }

                    if (suratDataResult.Desa == null || string.IsNullOrWhiteSpace(suratDataResult.Desa.NamaDesa))
                    {
                        await suratDataResult.EnsureDesaDataLoadedAsync();
                    }

                    _cachedSuratData[idSurat] = suratDataResult;
                    _logger.LogInformation("Successfully loaded and cached SuratData for ID_Surat={idSurat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                        idSurat, suratDataResult.Warga?.Nama ?? "null", suratDataResult.Warga?.NIK ?? "null");
                }
                else
                {
                    _logger.LogWarning("No surat data found for ID_Surat={idSurat}.", idSurat);
                }

                return suratDataResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil data surat untuk ID_Surat={idSurat}", idSurat);
                throw new DataRetrievalException($"Gagal mengambil data surat untuk ID_Surat={idSurat}", ex);
            }
        }

        //public async Task<SuratData?> GetSuratDataAsync(int idSurat)
        //{
        //    if (_cachedSuratData.TryGetValue(idSurat, out var cached)) { /* ... return cached ... */ }
        //    using var connection = new SqliteConnection(_config.DatabaseConnectionString);
        //    await connection.OpenAsync();

        //    if (_cachedSuratData.TryGetValue(idSurat, out var cachedData))
        //    {
        //        _logger.LogInformation("Returning cached SuratData for ID_Surat={idSurat}", idSurat);
        //        if (cachedData.Desa == null || string.IsNullOrWhiteSpace(cachedData.Desa.NamaDesa))
        //        {
        //            await cachedData.EnsureDesaDataLoadedAsync();
        //        }
        //        return cachedData;
        //    }
        //    _logger.LogInformation("Cache miss for ID_Surat={idSurat}. Fetching from database.", idSurat);

        //    string sqlSuratDesaWarga = @"
        //    SELECT
        //        s.ID_Surat, s.ID_Jenis, js.NamaJenis, s.NomorSurat, s.TanggalSurat, s.Keterangan, s.Keperluan,
        //        s.ID_Warga AS Warga_ID_Warga_Surat, /* Alias untuk ID_Warga di tabel Surat */
        //        d.NamaDesa, d.Kecamatan, d.Kabupaten, d.Alamat AS Alamat,
        //        d.Kodepos, d.KepalaDesa, d.SekretarisDesa, d.NamaCamat, d.NipCamat, d.GolCamat,
        //        w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, 
        //        w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan, w.Alamat, w.Pendidikan, w.Kewarganegaraan
        //    FROM Surat s
        //    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
        //    LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga /* Ini adalah join ke data Warga yang terkait langsung dengan Surat (misal, pemberi izin) */
        //    LEFT JOIN InfoDesa d ON 1=1
        //    WHERE s.ID_Surat = @idSurat;";

        //    try
        //    {
        //        SuratData suratDataResult = null;
        //        var result = await connection.QueryAsync<SuratData, DesaData, WargaData, SuratData>(
        //            sqlSuratDesaWarga,
        //            (surat, desa, warga) =>
        //            {
        //                surat.Desa = desa ?? new DesaData();
        //                surat.Warga = warga; // Ini adalah Warga yang terkait langsung di tabel Surat (misal pemberi izin/ortu)
        //                return surat;
        //            },
        //            new { idSurat },
        //            splitOn: "ID_Jenis,NamaDesa,ID_Warga" // Kolom ID_Warga di sini adalah dari tabel Warga (w.ID_Warga)
        //        );
        //        suratDataResult = result.FirstOrDefault();

        //        if (suratDataResult != null)
        //        {
        //            if (!string.IsNullOrWhiteSpace(suratDataResult.NamaJenis))
        //            {
        //                suratDataResult.SetJenisFromNamaJenis(suratDataResult.NamaJenis);
        //            }

        //            suratDataResult.Kematian ??= new KematianData();
        //            suratDataResult.SKU ??= new SKUData();
        //            suratDataResult.Instansi ??= new Instansi();
        //            suratDataResult.IzinOrtu ??= new IzinOrtuData();
        //            suratDataResult.RincianGarapans ??= new List<GarapanData>();

        //            // Fetch specific data
        //            if (suratDataResult.Jenis == SuratData.JenisSurat.IzinOrtu)
        //            {
        //                // Ambil data IzinOrtu spesifik, yang akan join dengan Warga (anak)
        //                suratDataResult.IzinOrtu = await _izinOrtuService.GetIzinOrtuAsync(idSurat);
        //                if (suratDataResult.IzinOrtu == null)
        //                {
        //                    _logger.LogWarning("Data IzinOrtu tidak ditemukan untuk surat ID_Surat={ID_Surat} meskipun jenisnya IzinOrtu.", idSurat);
        //                    suratDataResult.IzinOrtu = new IzinOrtuData(); // Pastikan tidak null
        //                }
        //            }
        //            else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "KEMATIAN" && suratDataResult.Warga != null)
        //            {
        //                // Data almarhum/ah ada di Warga yang di-join utama.
        //                // Data Pelapor dan detail kematian ada di tabel Kematian.
        //                var kematianDetail = await connection.QueryFirstOrDefaultAsync<KematianData>(
        //                    "SELECT HariKematian, TanggalKematian, PukulKematian, PenyebabKematian, NIKPelapor, NamaPelapor, AgamaPelapor, UmurPelapor, PekerjaanPelapor, AlamatPelapor, HubunganPelapor FROM Kematian WHERE ID_Surat = @idSurat", new { idSurat });
        //                if (kematianDetail != null) suratDataResult.Kematian = kematianDetail;
        //                // Warga yang di-join adalah almarhum/ah.
        //            }
        //            else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "SKU" && suratDataResult.SKU != null)
        //            {
        //                var skuData = await connection.QueryFirstOrDefaultAsync<SKUData>(
        //                    "SELECT BidangUsaha, SejakTahun FROM SKU WHERE ID_Surat = @idSurat", new { idSurat });
        //                if (skuData != null) suratDataResult.SKU = skuData;
        //            }
        //            else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "INSTANSI")
        //            {
        //                var instansiData = await connection.QueryFirstOrDefaultAsync<Instansi>(
        //                    "SELECT NamaInstansi, AlamatInstansi FROM Instansi WHERE ID_Surat = @idSurat", new { idSurat });
        //                if (instansiData != null)
        //                {
        //                    suratDataResult.Instansi = instansiData;
        //                    _logger.LogDebug("Instansi data loaded for ID_Surat={idSurat}: NamaInstansi={NamaInstansi}, AlamatInstansi={AlamatInstansi}",
        //                        idSurat, instansiData.NamaInstansi, instansiData.AlamatInstansi);
        //                }
        //                else
        //                {
        //                    _logger.LogWarning("No Instansi data found for ID_Surat={idSurat}. Creating default Instansi object.", idSurat);
        //                    suratDataResult.Instansi = new Instansi
        //                    {
        //                        NamaInstansi = "[Nama Instansi Tidak Ditemukan]",
        //                        AlamatInstansi = "[Alamat Instansi Tidak Ditemukan]"
        //                    };
        //                }
        //            }
        //            else if (suratDataResult.NamaJenis?.ToUpperInvariant() == "GARAPAN_SAWAH" && suratDataResult.Garapan != null)
        //            {
        //                var rincianGarapans = await connection.QueryAsync<GarapanData>(
        //                        "SELECT ID_GarapanItem, Luas, Lokasi, PemilikTanah, NomorPersil, KeteranganGarapan FROM Garapan WHERE ID_Surat = @idSurat",
        //                        new { idSurat });
        //                suratDataResult.RincianGarapans = rincianGarapans.ToList();
        //            }
        //            // Untuk SKD_UMUM dan DOMISILI_WARGA, data Warga yang di-join sudah cukup.

        //            if (suratDataResult.Desa == null || string.IsNullOrWhiteSpace(suratDataResult.Desa.NamaDesa))
        //            {
        //                await suratDataResult.EnsureDesaDataLoadedAsync();
        //            }
        //        }

        //        if (suratDataResult == null)
        //        {
        //            _logger.LogWarning("No surat data found for ID_Surat={idSurat} after multi-query.", idSurat);
        //            return null;
        //        }

        //        _cachedSuratData[idSurat] = suratDataResult;
        //        _logger.LogInformation("Successfully loaded and cached SuratData for ID_Surat={idSurat}", idSurat);
        //        return suratDataResult;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Gagal mengambil data surat (multi-query) untuk ID_Surat={idSurat}", idSurat);
        //        throw new DataRetrievalException($"Gagal mengambil data surat (multi-query) untuk ID_Surat={idSurat}", ex);
        //    }
        //}

        private async Task<DesaData> FetchDesaDataAsync(SqliteConnection connection)
        {
            try
            {
                var desa = await connection.QueryFirstOrDefaultAsync<DesaData>(
                    "SELECT NamaDesa, Kecamatan, Kabupaten, Alamat, Kodepos, KepalaDesa, NamaCamat, NipCamat, GolCamat FROM InfoDesa");
                
                // If no data or invalid data, return default desa data
                if (desa == null || !IsDesaDataValid(desa))
                {
                    _logger.LogWarning("FetchDesaDataAsync: Data InfoDesa kosong atau tidak valid, menggunakan data default.");
                    return CreateDefaultDesaData();
                }
                _logger.LogInformation("FetchDesaDataAsync: Data InfoDesa berhasil diambil.");
                return desa;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch DesaData from InfoDesa, using default.");
                return CreateDefaultDesaData();
            }
        }

        private bool IsDesaDataValid(DesaData desa)
        {
            return desa != null &&
                   !string.IsNullOrWhiteSpace(desa.NamaDesa) &&
                   !string.IsNullOrWhiteSpace(desa.Kecamatan) &&
                   !string.IsNullOrWhiteSpace(desa.Kabupaten) &&
                   !string.IsNullOrWhiteSpace(desa.KepalaDesa);
        }

        private DesaData CreateDefaultDesaData()
        {
            return new DesaData
            {
                NamaDesa = "Nama Desa",
                Kecamatan = "Nama Kecamatan",
                Kabupaten = "Nama Kabupaten",
                Alamat = "Alamat Desa",
                Kodepos = "00000",
                KepalaDesa = "Nama Kepala Desa",
                SekretarisDesa = "Nama Sekretaris Desa",
                NamaCamat = "Nama Camat",
                NipCamat = "000000000000000000",
                GolCamat = "IV/a"
            };
        }


        //public async Task<int> CountAllSuratAsync()
        //{
        //    // Implementasi Anda sudah baik
        //    // ... (Kode Anda yang sudah ada untuk CountAllSuratAsync)
        //    using var connection = new SqliteConnection(_config.DatabaseConnectionString);
        //    await connection.OpenAsync();
        //    try
        //    {
        //        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Gagal menghitung jumlah surat.");
        //        throw new DataRetrievalException("Gagal menghitung jumlah surat.", ex);
        //    }
        //}


        private async Task InsertRelatedDataAsync(SqliteConnection connection, IDbTransaction transaction, int suratId, SuratData suratData)
        {
            _logger.LogDebug("Memasukkan data terkait untuk ID_Surat={SuratId}, NamaJenis={NamaJenis}", suratId, suratData.NamaJenis);

            switch (suratData.NamaJenis?.ToUpperInvariant())
            {
                case "KEMATIAN":
                    if (suratData.Kematian != null)
                    {
                        await connection.ExecuteAsync(
                            @"INSERT INTO Kematian
                              (ID_Surat, HariKematian, TanggalKematian, PukulKematian, PenyebabKematian,
                               NIKPelapor, NamaPelapor, AgamaPelapor, UmurPelapor, PekerjaanPelapor,
                               AlamatPelapor, HubunganPelapor)
                              VALUES (@ID_Surat, @HariKematian, @TanggalKematian, @PukulKematian, @PenyebabKematian,
                                      @NIKPelapor, @NamaPelapor, @AgamaPelapor, @UmurPelapor, @PekerjaanPelapor,
                                      @AlamatPelapor, @HubunganPelapor)",
                            new
                            {
                                ID_Surat = suratId,
                                suratData.Kematian.HariKematian,
                                TanggalKematian = suratData.Kematian.TanggalKematian, // Asumsi format sudah benar untuk DB
                                PukulKematian = suratData.Kematian.PukulKematian,
                                suratData.Kematian.PenyebabKematian,
                                suratData.Kematian.NIKPelapor,
                                suratData.Kematian.NamaPelapor,
                                suratData.Kematian.AgamaPelapor,
                                suratData.Kematian.UmurPelapor, // Ini seharusnya Tanggal Lahir Pelapor, bukan umur
                                suratData.Kematian.PekerjaanPelapor,
                                suratData.Kematian.AlamatPelapor,
                                suratData.Kematian.HubunganPelapor
                            },
                            transaction);
                    }
                    break;
                case "SKU":
                    if (suratData.SKU != null)
                        await connection.ExecuteAsync(
                            @"INSERT INTO SKU (ID_Surat, BidangUsaha, SejakTahun)
                              VALUES (@ID_Surat, @BidangUsaha, @SejakTahun)",
                            new { ID_Surat = suratId, suratData.SKU.BidangUsaha, suratData.SKU.SejakTahun }, transaction);
                    break;

                case "IZIN_ORTU":
                    if (suratData.IzinOrtu != null)
                    {
                        if (suratData.IzinOrtu.ID_Warga_Anak <= 0)
                        {
                            throw new InvalidOperationException("ID_Warga_Anak tidak valid untuk menyimpan data Izin Ortu.");
                        }
                        suratData.IzinOrtu.ID_Surat = suratId;
                        // PERUBAHAN DI SINI: Teruskan koneksi dan transaksi
                        await _izinOrtuService.AddIzinOrtuAsync(suratData.IzinOrtu, connection, transaction);
                    }
                    else
                    {
                        _logger.LogWarning("Data IzinOrtu null untuk ID_Surat: {ID_Surat}", suratId);
                    }
                    break;

                case "GARAPAN_SAWAH":
                    if (suratData.RincianGarapans != null && suratData.RincianGarapans.Any())
                    {
                        foreach (var itemGarapan in suratData.RincianGarapans)
                        {
                            if (itemGarapan != null)
                            {
                                await connection.ExecuteAsync(
                                    @"INSERT INTO Garapan (ID_Surat, Luas, Lokasi, PemilikTanah, NomorPersil, KeteranganGarapan)
                                      VALUES (@ID_Surat, @Luas, @Lokasi, @PemilikTanah, @NomorPersil, @KeteranganGarapan)",
                                    new
                                    {
                                        ID_Surat = suratId,
                                        itemGarapan.Luas,
                                        itemGarapan.Lokasi,
                                        itemGarapan.PemilikTanah,
                                        itemGarapan.NomorPersil,
                                        itemGarapan.KeteranganGarapan
                                    }, transaction);
                            }
                        }
                    }
                    break;

                case "INSTANSI":
                    if (suratData.Instansi != null && suratData.Instansi.IsValid())
                    {
                        await connection.ExecuteAsync(
                            @"INSERT INTO INSTANSI (ID_Surat, NamaInstansi, AlamatInstansi)
                            VALUES (@ID_Surat, @NamaInstansi, @AlamatInstansi)",
                            new
                            {
                                ID_Surat = suratId,
                                suratData.Instansi.NamaInstansi,
                                suratData.Instansi.AlamatInstansi
                            }, transaction);
                    }
                    break;

                case "BEDANAMA":
                    await connection.ExecuteAsync(
                        @"INSERT INTO BedaNama
                          (ID_Surat, ID_Warga, SumberDataKoreksi, SumberDataKeliru, AlasanPerbedaan,
                           NIK2, Nama2, TempatLahir2, TanggalLahir2, JenisKelamin2,
                           Dusun2, Desa2, Kecamatan2, Kabupaten2)
                          VALUES (@ID_Surat, @ID_Warga, @SumberDataKoreksi, @SumberDataKeliru, @AlasanPerbedaan,
                                  @NIK2, @Nama2, @TempatLahir2, @TanggalLahir2, @JenisKelamin2,
                                  @Dusun2, @Desa2, @Kecamatan2, @Kabupaten2)",
                        new
                        {
                            ID_Surat = suratId,
                            ID_Warga = suratData.Warga?.ID_Warga ?? 0,
                            SumberDataKoreksi = suratData.DataSource1?.Trim(),
                            SumberDataKeliru = suratData.DataSource2?.Trim(),
                            AlasanPerbedaan = suratData.Keterangan,
                            NIK2 = suratData.WargaKK?.NIK,
                            Nama2 = suratData.WargaKK?.Nama,
                            TempatLahir2 = suratData.WargaKK?.TempatLahir,
                            TanggalLahir2 = suratData.WargaKK?.TanggalLahir,
                            JenisKelamin2 = suratData.WargaKK?.JenisKelamin,
                            Dusun2 = suratData.WargaKK?.Dusun,
                            Desa2 = suratData.WargaKK?.Desa,
                            Kecamatan2 = suratData.WargaKK?.Kecamatan,
                            Kabupaten2 = suratData.WargaKK?.Kabupaten
                        },
                        transaction);
                    break;
            }
        }

        private async Task DeleteRelatedDataAsync(SqliteConnection connection, IDbTransaction transaction, int suratId, string namaJenis)
        {
            // Dapatkan nama tabel berdasarkan namaJenis
            // Untuk IZIN_ORTU, panggil _izinOrtuService.DeleteIzinOrtuAsync
            _logger.LogDebug("Menghapus data terkait untuk ID_Surat={SuratId}, NamaJenis={NamaJenis}", suratId, namaJenis);
            
            if (namaJenis?.ToUpperInvariant() == "IZIN_ORTU")
            {
                await _izinOrtuService.DeleteIzinOrtuAsync(suratId, connection, transaction);
            }
            else
            {
                string tableName = namaJenis?.ToUpperInvariant() switch
                {
                    "KEMATIAN" => "Kematian",
                    "SKU" => "SKU",
                    // "IZIN_ORTU" => "IZIN", // Ditangani oleh IzinOrtuService
                    "GARAPAN_SAWAH" => "Garapan",
                    "INSTANSI" => "INSTANSI",
                    "BEDANAMA" => "BedaNama",
                    _ => null
                };

                if (tableName != null)
                {
                    await connection.ExecuteAsync(
                        $"DELETE FROM {tableName} WHERE ID_Surat = @ID_Surat",
                        new { ID_Surat = suratId },
                        transaction);
                }
            }
        }

        public async Task<List<SuratData>> GetAllSuratDataAsync(
                        string sortBy = "s.ID_Surat",
                        bool ascending = false,
                        int skip = 0,
                        int take = 100)
        {
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();

            var orderBy = GetValidSortColumn(sortBy);
            var direction = ascending ? "ASC" : "DESC";

            var query = $@"
                SELECT
                    s.ID_Surat, s.ID_Jenis, UPPER(js.NamaJenis) AS NamaJenis, s.NomorSurat, s.TanggalSurat, 
                    s.Keterangan, s.Keperluan, 
                    s.ID_Warga AS ID_Warga_Pemohon_FK,
                    d.NamaDesa, d.Kecamatan, d.Kabupaten, d.Alamat,
                    d.Kodepos, d.KepalaDesa, d.SekretarisDesa, d.NamaCamat, d.NipCamat, d.GolCamat,
                    w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, 
                    w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan, w.Alamat,
                    w.Pendidikan, w.Kewarganegaraan,
                    i.NamaInstansi, i.AlamatInstansi
                FROM Surat s
                INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
                LEFT JOIN InfoDesa d ON 1=1
                LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat AND js.NamaJenis = 'INSTANSI'
                ORDER BY {orderBy} {direction} 
                LIMIT @Take OFFSET @Skip";

            try
            {
                var result = await connection.QueryAsync<SuratData, DesaData, WargaData, Instansi, SuratData>(
                    query,
                    (surat, desa, wargaPemohon, instansi) =>
                    {
                        _logger.LogDebug("Mapping Surat: ID_Surat={ID_Surat}, NamaJenis={NamaJenis}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}, Instansi.NamaInstansi={NamaInstansi}",
                            surat.ID_Surat, surat.NamaJenis, wargaPemohon?.Nama ?? "null", wargaPemohon?.NIK ?? "null", instansi?.NamaInstansi ?? "null");
                        surat.Desa = desa ?? new DesaData();
                        surat.Warga = wargaPemohon;
                        if (surat.NamaJenis?.ToUpperInvariant() == "INSTANSI")
                        {
                            surat.Instansi = instansi ?? new Instansi { NamaInstansi = "[No Instansi]", AlamatInstansi = "[No Address]" };
                            if (wargaPemohon?.NIK != "9999999999999999")
                            {
                                _logger.LogDebug("INSTANSI penanggung jawab: ID_Surat={ID_Surat}, Warga.Nama={WargaNama}, Warga.NIK={WargaNIK}",
                                    surat.ID_Surat, wargaPemohon.Nama, wargaPemohon.NIK);
                            }
                        }
                        else if (surat.NamaJenis?.ToUpperInvariant() == "IZIN_ORTU")
                        {
                            // Ambil data anak untuk IZIN_ORTU (akan diambil terpisah jika diperlukan oleh UI)
                            surat.IzinOrtu = new IzinOrtuData(); // Inisialisasi untuk mencegah null
                        }
                        surat.SetJenisFromNamaJenis(surat.NamaJenis);
                        return surat;
                    },
                    new { Take = take, Skip = skip },
                    splitOn: "ID_Jenis,NamaDesa,ID_Warga,NamaInstansi"
                );
                _logger.LogInformation("Retrieved {Count} surat records.", result.Count());
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve all surat data.");
                throw new DataRetrievalException("Gagal mengambil semua data surat.", ex);
            }
        }
        private static string GetValidSortColumn(string sortBy)
        {
            // Implementasi Anda sudah baik
            // ... (Kode Anda yang sudah ada untuk GetValidSortColumn)
            return sortBy.ToLowerInvariant() switch
            {
                "id_surat" => "s.ID_Surat",
                "nomorsurat" => "s.NomorSurat",
                "nama" => "w.Nama", // Jika join dengan tabel warga dan ingin sort by nama warga
                "jenissurat" => "js.NamaJenis", // Jika join dengan JenisSurat
                "tanggal" => "s.TanggalSurat", // Default ke TanggalSurat
                _ => "s.ID_Surat" // Default yang aman
            };
        }

        public async Task<int> CountAllSuratAsync()
        {
            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            try
            {
                return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghitung jumlah semua surat.");
                // Kembalikan 0 atau lempar exception kustom jika diperlukan
                return 0; // Atau throw new DataAccessException("Gagal menghitung jumlah semua surat.", ex);
            }
        }

        public async Task<int> CountSuratByJenisAsync(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
            {
                _logger.LogWarning("SuratService.CountSuratByJenisAsync dipanggil dengan namaJenis kosong atau null.");
                return 0;
            }
            _logger.LogDebug("SuratService.CountSuratByJenisAsync: Menghitung surat untuk jenis: {NamaJenis}", namaJenis);

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            try
            {
                var query = @"
                    SELECT COUNT(s.ID_Surat)
                    FROM Surat s
                    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                    WHERE UPPER(js.NamaJenis) = @NamaJenisParam";
                var count = await connection.ExecuteScalarAsync<int>(query, new { NamaJenisParam = namaJenis.ToUpperInvariant() });
                _logger.LogInformation("SuratService.CountSuratByJenisAsync menemukan {Count} surat untuk jenis: {NamaJenis}", count, namaJenis);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error di SuratService saat menghitung surat berdasarkan jenis tunggal: {NamaJenis}", namaJenis);
                throw new DataAccessException($"Gagal menghitung surat untuk jenis {namaJenis} di SuratService.", ex);
            }
        }

        // Metode untuk menghitung GRUP jenis surat
        public async Task<int> CountSuratByJenisGroupAsync(HashSet<string> groupNamaJenis)
        {
            if (groupNamaJenis == null || !groupNamaJenis.Any())
            {
                _logger.LogWarning("SuratService.CountSuratByJenisGroupAsync dipanggil dengan grup jenis surat kosong atau null.");
                return 0;
            }
            _logger.LogDebug("SuratService.CountSuratByJenisGroupAsync: Menghitung surat untuk grup jenis: [{GroupJenis}]", string.Join(", ", groupNamaJenis));

            using var connection = new SqliteConnection(_config.DatabaseConnectionString);
            await connection.OpenAsync();
            try
            {
                var parameters = new DynamicParameters();
                var namaJenisPlaceholders = new List<string>();
                int i = 0;
                foreach (var jenis in groupNamaJenis)
                {
                    if (!string.IsNullOrWhiteSpace(jenis))
                    {
                        var paramName = $"@jenis{i}";
                        parameters.Add(paramName, jenis.ToUpperInvariant());
                        namaJenisPlaceholders.Add(paramName);
                        i++;
                    }
                }

                if (!namaJenisPlaceholders.Any())
                {
                    _logger.LogWarning("SuratService.CountSuratByJenisGroupAsync: Tidak ada jenis surat valid dalam grup untuk difilter.");
                    return 0;
                }

                var query = $@"
                    SELECT COUNT(s.ID_Surat)
                    FROM Surat s
                    INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                    WHERE UPPER(js.NamaJenis) IN ({string.Join(",", namaJenisPlaceholders)})";

                var count = await connection.ExecuteScalarAsync<int>(query, parameters);
                _logger.LogInformation("SuratService.CountSuratByJenisGroupAsync menemukan {Count} surat untuk grup: [{GroupJenis}]", count, string.Join(", ", groupNamaJenis));
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error di SuratService saat menghitung surat berdasarkan grup jenis: [{GroupJenis}]", string.Join(", ", groupNamaJenis));
                throw new DataAccessException($"Gagal menghitung surat berdasarkan grup jenis di SuratService: {string.Join(", ", groupNamaJenis)}.", ex);
            }
        }
    }
}
