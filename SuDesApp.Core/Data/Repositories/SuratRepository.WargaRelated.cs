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

        public async Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var query = _queryProvider.GetQuery("CheckNomorSuratExists");
            var count = await _connection.ExecuteScalarAsync<int>(query, new { NomorSurat = nomorSurat, ID_Surat = excludeId });
            return count > 0;
        }

        private async Task<int> GetOrCreateWargaAsync(SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            if (suratData.Warga == null)
                return await _wargaRepository.GetOrCreateDummyWargaAsync(_connection, transaction);

            // WAJIB lewat transaksi yang sama. Sebelumnya AddOrUpdateWargaAndGetIdAsync
            // dipanggil tanpa transaksi: kalau insert surat berikutnya gagal dan
            // rollback, baris Warga yang sudah tertulis TETAP ADA -> residents
            // yatim permanen di master list. Penulisan dan pembacaan juga lewat
            // jalur berbeda, jadi dua operasi bersamaan untuk NIK yang sama bisa
            // saling menimpa.
            var warga = await _wargaRepository.GetWargaByNikAsync(
                suratData.Warga.NIK ?? string.Empty, _connection, transaction);
            var wargaId = warga?.ID_Warga
                ?? await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(
                    suratData.Warga, _connection, transaction);
            suratData.Warga.ID_Warga = wargaId;
            return wargaId;
        }

        private async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(suratData.NamaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.InsertRelatedDataAsync(idSurat, suratData, _connection, transaction);
        }

        /// <summary>
        /// Insert data terkait dengan toleransi DRAFT: surat berstatus Draft boleh
        /// belum punya data rincian (mis. garapan baru terisi NIK/Nama), sehingga
        /// kegagalan handler karena data belum lengkap tidak menggagalkan simpan.
        /// </summary>
        private async Task InsertRelatedDataSafeAsync(int idSurat, SuratData suratData, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var isDraft = string.Equals(suratData.Status, "Draft", StringComparison.OrdinalIgnoreCase);
            try
            {
                await InsertRelatedDataAsync(idSurat, suratData, transaction, cancellationToken);
            }
            catch (Exception ex) when (isDraft && ex is ArgumentException or ValidationException)
            {
                _logger.LogWarning(ex,
                    "DRAFT ID_Surat={ID_Surat}: rincian belum lengkap ({Message}); surat tetap disimpan.",
                    idSurat, ex.Message);
            }
        }

        private async Task DeleteRelatedDataAsync(int idSurat, string namaJenis, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(namaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.DeleteRelatedDataAsync(idSurat, _connection, transaction);
        }

        private static string GetValidSortColumn(string sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return "s.ID_Surat";

            return sortBy.ToLowerInvariant() switch
            {
                "id_surat" => "s.ID_Surat",
                "nomorsurat" => "s.NomorSurat",
                "nama" => "COALESCE(w.Nama, i.NamaInstansi)",
                "jenissurat" => "js.NamaJenis",
                "tanggal" => "s.TanggalSurat",
                "status" => "s.Status",
                "createdat" => "s.CreatedAt",
                _ => "s.ID_Surat"
            };
        }

        private async Task ClearListCacheAsync()
        {
            await _cacheService.RemoveByPrefixAsync("Surat_Filtered_", default);
            await _cacheService.RemoveByPrefixAsync("Surat_Count_", default);
            await _cacheService.RemoveByPrefixAsync("Surat_Statistics_", default);

            // Register nomor terbit & register surat ikut basi setiap ada
            // penulisan surat — angka tutup buku tidak boleh mencerminkan
            // keadaan sebelum surat ini tersimpan.
            await _jenisSuratRepository.TandaiRegisterBerubahAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Buat kode verifikasi + cap dokumen untuk satu surat dan simpan dalam
        /// transaksi yang sedang berjalan. Kode yang sudah ada TIDAK pernah

    }
}
