using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        private class NtcrRegisterRow
        {
            public int ID_CalonIstri { get; set; }
            public string? NikIstri { get; set; }
            public string? NamaIstri { get; set; }
            public string? TempatLahirIstri { get; set; }
            public string? TanggalLahirIstri { get; set; }
            public string? AgamaIstri { get; set; }
            public string? PekerjaanIstri { get; set; }
            public string? AlamatIstri { get; set; }
            public string? NamaAyahCalonSuami { get; set; }
            public string? NamaIbuCalonSuami { get; set; }
            public string? NamaAyahCalonIstri { get; set; }
            public string? NamaIbuCalonIstri { get; set; }
            public string? StatusPerkawinanIstri { get; set; }
            public string? KeteranganTemuan { get; set; }
            public string? TujuanSurat { get; set; }
        }

        public async Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default)
        {
            string cacheKey = $"Surat_Count_{filters.JenisSurat ?? "null"}_{filters.Tahun ?? "null"}_{filters.Status ?? "null"}_{filters.SearchText ?? "null"}_Ex_{string.Join(",", filters.ExcludeJenisNames ?? new List<string>())}";
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken);
            if (cached.HasValue)
                return cached.Value;

            await EnsureConnectionOpenAsync(cancellationToken);
            var (whereClause, parameters) = await BuildWhereClauseAsync(filters);
            var query = _queryProvider.GetQuery("GetFilteredSuratCount").Replace("{WhereClause}", whereClause);
            var count = await _connection.ExecuteScalarAsync<int>(query, parameters);

            await _cacheService.SetAsync(cacheKey, count, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return count;
        }

        public async Task<Dictionary<string, int>> CountByStatusAsync(FilterConditions filters, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);

            // Statistik status mengabaikan filter status itu sendiri (dipakai untuk
            // menghitung masing-masing status), tetapi tetap mengikuti filter lain.
            var filtersTanpaStatus = filters.CloneTanpaStatus();
            var (whereClause, parameters) = await BuildWhereClauseAsync(filtersTanpaStatus);
            var query = _queryProvider.GetQuery("GetFilteredSuratStatusCounts").Replace("{WhereClause}", whereClause);

            var rows = await _connection.QueryAsync<StatusCountRow>(query, parameters);
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Status))
                .GroupBy(r => r.Status!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Jumlah), StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StatusCountRow
        {
            public string? Status { get; set; }
            public int Jumlah { get; set; }
        }

        public async Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));
            if (string.IsNullOrWhiteSpace(year))
                throw new ArgumentException("Tahun tidak boleh kosong.", nameof(year));

            string cacheKey = $"Surat_Count_{namaJenis.ToUpperInvariant()}_{year}";
            var cached = await _cacheService.GetAsync<int?>(cacheKey, cancellationToken);
            if (cached.HasValue)
                return cached.Value;

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CountSuratByJenisAndYear");
            var parameters = new { NamaJenis = namaJenis.ToUpperInvariant(), Year = year };
            var count = await _connection.ExecuteScalarAsync<int>(query, parameters);

            await _cacheService.SetAsync(cacheKey, count, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(10),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return count;
        }

        public async Task<int> AmbilUrutTerakhirAsync(int tahun, CancellationToken cancellationToken = default)
        {
            if (tahun <= 0)
                throw new ArgumentException("Tahun tidak valid.", nameof(tahun));

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("UrutTerakhirSurat");
            var parameters = new { Year = tahun.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            return await _connection.ExecuteScalarAsync<int>(query, parameters);
        }

        public async Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("GetSuratByDateRange");

            var results = await _connection.QueryAsync<SuratData, string, string, SuratData>(
                query,
                (s, jenisNama, wargaNama) =>
                {
                    s.NamaJenis = jenisNama;
                    if (s.Warga == null) s.Warga = new WargaData();
                    s.Warga.Nama = wargaNama;
                    return s;
                },
                new { TanggalMulai = startDate.ToString("yyyy-MM-dd"), TanggalSelesai = endDate.ToString("yyyy-MM-dd") },
                splitOn: "NamaJenis,Nama"
            );

            return results;
        }

        public async Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default)
        {
            string cacheKey = "Surat_Statistics_Status";
            var cached = await _cacheService.GetAsync<Dictionary<string, int>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;

            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CountSuratByStatus");

            var results = await _connection.QueryAsync<(string Status, int Jumlah)>(query);
            var statistics = results.ToDictionary(r => r.Status ?? "Unknown", r => r.Jumlah);

            await _cacheService.SetAsync(cacheKey, statistics, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                AbsoluteExpirationRelativeToNow = _cacheExpiration
            }, cancellationToken);

            return statistics;
        }

    }
}
