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
        // MAIN METHOD IMPLEMENTATION
        public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
        {
            try
            {
                _logger.LogInformation("Getting database statistics...");
                await EnsureConnectionOpenAsync();
                var stats = new Dictionary<string, object>();

                // 1. TOTAL SURAT
                var totalSurat = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Surat");
                stats["Total Surat"] = totalSurat;
                _logger.LogDebug("Total Surat: {Count}", totalSurat);

                // 2. TOTAL WARGA
                var totalWarga = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warga");
                stats["Total Warga"] = totalWarga;
                _logger.LogDebug("Total Warga: {Count}", totalWarga);

                // 3. TOTAL JENIS SURAT
                // NOTE: tanpa filter IsActive agar backward-compatible
                // dengan database lama yang belum punya kolom tersebut.
                var totalJenis = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM JenisSurat");
                stats["Total Jenis Surat"] = totalJenis;

                // 4. SURAT BY STATUS
                try
                {
                    var statusQuery = "SELECT Status, COUNT(*) as Count FROM Surat GROUP BY Status";
                    var statusResults = await _connection.QueryAsync(statusQuery);

                    foreach (var row in statusResults)
                    {
                        var status = SafeGetString(row.Status, "Unknown");
                        var count = SafeGetInt(row.Count);
                        stats[$"Surat {status}"] = count;
                        //_logger.LogDebug("Status {Status}: {Count}", status, count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get status statistics");
                    stats["Surat Status"] = "Status surat tidak terbaca";
                }

                // 5. SURAT BY JENIS (TOP 10)
                try
                {
                    var jenisQuery = @"
                SELECT js.NamaJenis, COUNT(*) as Count 
                FROM Surat s 
                INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis 
                GROUP BY js.NamaJenis 
                ORDER BY Count DESC 
                LIMIT 10";

                    var jenisResults = await _connection.QueryAsync(jenisQuery);

                    foreach (var row in jenisResults)
                    {
                        var namaJenis = SafeGetString(row.NamaJenis, "Unknown");
                        var count = SafeGetInt(row.Count);
                        stats[$"Jenis: {namaJenis}"] = count;
                        //_logger.LogDebug("Jenis {NamaJenis}: {Count}", namaJenis, count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get jenis statistics");
                    stats["Surat by Jenis"] = "Data jenis surat tidak terbaca";
                }

                // 6. SURAT TAHUN INI
                try
                {
                    var currentYear = DateTime.Now.Year.ToString();
                    var yearQuery = "SELECT COUNT(*) FROM Surat WHERE strftime('%Y', TanggalSurat) = @Year";
                    var suratThisYear = await _connection.ExecuteScalarAsync<int>(yearQuery, new { Year = currentYear });
                    stats[$"Surat Tahun {currentYear}"] = suratThisYear;
                    _logger.LogDebug("Surat tahun {Year}: {Count}", currentYear, suratThisYear);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get yearly statistics");
                    stats["Surat Tahun Ini"] = "Data tahun berjalan tidak terbaca";
                }

                // 7. SURAT BULAN INI
                try
                {
                    var currentMonth = DateTime.Now.ToString("yyyy-MM");
                    var monthQuery = "SELECT COUNT(*) FROM Surat WHERE strftime('%Y-%m', TanggalSurat) = @Month";
                    var suratThisMonth = await _connection.ExecuteScalarAsync<int>(monthQuery, new { Month = currentMonth });
                    stats[$"Surat Bulan Ini"] = suratThisMonth;
                    _logger.LogDebug("Surat bulan {Month}: {Count}", currentMonth, suratThisMonth);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get monthly statistics");
                    stats["Surat Bulan Ini"] = "Data bulan berjalan tidak terbaca";
                }

                // 8. DATABASE FILE SIZE (jika perlu)
                try
                {
                    var dbSizeQuery = "SELECT page_count * page_size as size FROM pragma_page_count(), pragma_page_size()";
                    var dbSize = await _connection.ExecuteScalarAsync<long>(dbSizeQuery);
                    var dbSizeMB = Math.Round(dbSize / (1024.0 * 1024.0), 2);
                    stats["Database Size (MB)"] = dbSizeMB;
                    _logger.LogDebug("Database size: {Size} MB", dbSizeMB);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get database size");
                    stats["Database Size"] = "Unknown";
                }

                // 9. LAST SURAT CREATED
                try
                {
                    var lastSuratQuery = @"
                SELECT s.NomorSurat, s.TanggalSurat, js.NamaJenis 
                FROM Surat s 
                INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis 
                ORDER BY s.CreatedAt DESC 
                LIMIT 1";

                    var lastSurat = await _connection.QueryFirstOrDefaultAsync(lastSuratQuery);
                    if (lastSurat != null)
                    {
                        var nomorSurat = SafeGetString(lastSurat.NomorSurat);
                        var jenisNama = SafeGetString(lastSurat.NamaJenis);
                        stats["Surat Terakhir"] = $"{nomorSurat} ({jenisNama})";
                    }
                    else
                    {
                        stats["Surat Terakhir"] = "Tidak ada data";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get last surat info");
                    stats["Surat Terakhir"] = "Surat terakhir tidak terbaca";
                }

                _logger.LogInformation("Database statistics retrieved successfully. Total stats: {Count}", stats.Count);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database statistics");
                return new Dictionary<string, object>
        {
            { "Error", "Gagal mengambil statistik database" },
            { "Exception", ex.Message },
            { "Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
        };
            }
        }

        public async Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.GetWargaByIdAsync(idWarga);
        }

        public async Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.GetWargaByNikAsync(nik);
        }

        public async Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default)
        {
            return await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(wargaData);
        }

        public async Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default)
        {
            var filters = new FilterConditions { JenisSurat = templateName };
            var surats = await GetFilteredAsync(filters, "ID_Surat", false, 0, 1, cancellationToken);
            var lastSurat = surats.FirstOrDefault();
            return lastSurat?.ID_Surat;
        }

    }
}
