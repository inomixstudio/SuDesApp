using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Utilities;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public partial class SuratRepository : ISuratRepository, ISuratInsertion
    {
        /// diganti (lihat <see cref="SuDesApp.Services.VerifikasiSuratService"/>).
        /// </summary>
        private async Task PastikanKodeVerifikasiAsync(
            SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(suratData.KodeVerifikasi)) return;
            if (suratData.ID_Surat <= 0 || string.IsNullOrWhiteSpace(suratData.NomorSurat)) return;

            var kode = SuDesApp.Services.KodeVerifikasiSurat.BuatKode();
            var hash = SuDesApp.Services.KodeVerifikasiSurat.HitungHash(suratData);

            await _connection.ExecuteAsync(
                "UPDATE Surat SET KodeVerifikasi = @kode, HashVerifikasi = @hash WHERE ID_Surat = @id",
                new { kode, hash, id = suratData.ID_Surat }, transaction);

            suratData.KodeVerifikasi = kode;
            suratData.HashVerifikasi = hash;
        }

        public async Task<List<string>> GetJenisSuratKeteranganDesaAsync()
        {
            return await _jenisSuratRepository.GetJenisSuratKeteranganDesa() ?? new List<string>();
        }

        public async Task RefreshJenisSuratConfigurationAsync()
        {
            await _jenisSuratRepository.RefreshConfigurationAsync();
            await ClearListCacheAsync();
            _logger.LogInformation("JenisSurat configuration refreshed.");
        }

        public Task<int> AddSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            return InsertAsync(entity, transaction, cancellationToken);
        }

        public async Task<IEnumerable<SuratData>> GetAllSuratDataAsync(
    string sortBy = "ID_Surat",
    bool ascending = false,
    int skip = 0,
    int take = 100,
    CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Memuat surat: take={Take}, skip={Skip}, sortBy={SortBy}.", take, skip, sortBy);

            try
            {
                await EnsureConnectionOpenAsync(cancellationToken);

                take = Math.Max(1, Math.Min(take, 1000));
                skip = Math.Max(0, skip);

                var orderBy = GetValidSortColumn(sortBy);
                var direction = ascending ? "ASC" : "DESC";

                // GUNAKAN QUERY YANG LEBIH SEDERHANA DENGAN PEMETAAN MANUAL
                var query = $@"
            SELECT 
                s.ID_Surat, s.ID_Jenis, s.NomorSurat, s.TanggalSurat, 
                s.Keterangan, s.Keperluan, s.AdditionalData, s.Status, 
                s.CreatedAt, s.UpdatedAt,
                w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, 
                w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan, 
                w.Dusun, w.Desa, w.Kecamatan, w.Kabupaten, w.Pendidikan, 
                w.Kewarganegaraan, w.CreatedAt as WargaCreatedAt, w.UpdatedAt as WargaUpdatedAt,
                i.NamaInstansi, i.AlamatInstansi, i.PimpinanInstansi,
                i.NomorSkIzinUsaha, i.NikPimpinan,
                js.NamaJenis, js.KodeJenis, js.Deskripsi
            FROM Surat s
            LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
            INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
            LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
            ORDER BY {orderBy} {direction}
            LIMIT @Take OFFSET @Skip";

                _logger.LogDebug("Executing query: {Query}", query);

                var dynamicResults = await _connection.QueryAsync(query, new { Take = take, Skip = skip });
                var results = new List<SuratData>();

                foreach (var row in dynamicResults)
                {
                    try
                    {
                        var surat = new SuratData
                        {
                            ID_Surat = SafeGetInt(row.ID_Surat),
                            ID_Jenis = SafeGetInt(row.ID_Jenis),
                            NomorSurat = SafeGetString(row.NomorSurat),
                            TanggalSurat = SafeGetDateTime(row.TanggalSurat),
                            Keterangan = SafeGetString(row.Keterangan),
                            Keperluan = SafeGetString(row.Keperluan),
                            AdditionalData = SafeGetString(row.AdditionalData),
                            Status = SafeGetString(row.Status, "Draft"),
                            CreatedAt = SafeGetDateTime(row.CreatedAt, DateTime.Now),
                            UpdatedAt = SafeGetDateTime(row.UpdatedAt, DateTime.Now),
                            KodeJenis = SafeGetString(row.KodeJenis),
                            NamaJenis = SafeGetString(row.NamaJenis)
                        };

                        // Map Warga data jika ada
                        if (SafeGetInt(row.ID_Warga) > 0)
                        {
                            surat.Warga = new WargaData
                            {
                                ID_Warga = SafeGetInt(row.ID_Warga),
                                NIK = SafeGetString(row.NIK),
                                Nama = SafeGetString(row.Nama),
                                TempatLahir = SafeGetString(row.TempatLahir),
                                TanggalLahir = SafeGetString(row.TanggalLahir),
                                JenisKelamin = SafeGetString(row.JenisKelamin),
                                Agama = SafeGetString(row.Agama),
                                StatusPerkawinan = SafeGetString(row.StatusPerkawinan),
                                Pekerjaan = SafeGetString(row.Pekerjaan),
                                Dusun = SafeGetString(row.Dusun),
                                RT = SafeGetString(row.RT),
                                RW = SafeGetString(row.RW),
                                Desa = SafeGetString(row.Desa),
                                Kecamatan = SafeGetString(row.Kecamatan),
                                Kabupaten = SafeGetString(row.Kabupaten),
                                Pendidikan = SafeGetString(row.Pendidikan),
                                Kewarganegaraan = SafeGetString(row.Kewarganegaraan),
                                CreatedAt = SafeGetDateTime(row.WargaCreatedAt, DateTime.Now),
                                UpdatedAt = SafeGetDateTime(row.WargaUpdatedAt, DateTime.Now)
                            };
                        }

                        // Map Instansi jika ada
                        if (!string.IsNullOrEmpty(SafeGetString(row.NamaInstansi)))
                        {
                            surat.Instansi = new Instansi
                            {
                                NamaInstansi = SafeGetString(row.NamaInstansi),
                                AlamatInstansi = SafeGetString(row.AlamatInstansi),
                                PimpinanInstansi = SafeGetString(row.PimpinanInstansi),
                                NomorSkIzinUsaha = SafeGetString(row.NomorSkIzinUsaha),
                                NikPimpinan = SafeGetString(row.NikPimpinan)
                            };
                        }

                        results.Add(surat);
                    }
                    catch (Exception ex)
                    {
                        // Baris ini tidak muncul di register. Menelan galat tanpa
                        // catatan membuat clerk mencari surat yang "hilang" padahal
                        // barisnya ada di database — catat agar bisa ditelusuri.
                        _logger.LogWarning(ex,
                            "Baris surat ID {Id} gagal dipetakan saat memuat register — baris ini tidak tampil.",
                            (object?)row.ID_Surat?.ToString());
                    }
                }

                _logger.LogDebug("Berhasil memuat {Count} surat.", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FAILED to retrieve surat data");
                throw new DataRetrievalException("Gagal mengambil data surat.", ex);
            }
        }


    }
}
