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

        // =================================================================
        // Alur persetujuan, scan, cetakan, dan kode verifikasi.
        // =================================================================

        public async Task<bool> SimpanPersetujuanAsync(SuratData surat, CancellationToken cancellationToken = default)
        {
            if (surat == null) throw new ArgumentNullException(nameof(surat));
            if (surat.ID_Surat <= 0)
                throw new ArgumentException("ID_Surat harus diisi.", nameof(surat));

            await EnsureConnectionOpenAsync(cancellationToken);

            // Validasi nilai status agar salah ketik tidak masuk arsip.
            if (!StatusPersetujuanSurat.Valid(surat.StatusPersetujuan))
                throw new ArgumentException($"Status persetujuan '{surat.StatusPersetujuan}' tidak dikenal.");

            const string sql = @"
                UPDATE Surat SET
                    StatusPersetujuan  = @StatusPersetujuan,
                    VerifikasiOleh     = @VerifikasiOleh,
                    VerifikasiPada     = @VerifikasiPada,
                    DitandatanganiOleh = @DitandatanganiOleh,
                    DitandatanganiPada = @DitandatanganiPada,
                    CatatanPersetujuan = @CatatanPersetujuan,
                    UpdatedAt          = @Sekarang
                WHERE ID_Surat = @ID_Surat;";

            var rows = await _connection.ExecuteAsync(sql, new
            {
                surat.ID_Surat,
                StatusPersetujuan = string.IsNullOrWhiteSpace(surat.StatusPersetujuan)
                    ? null
                    : surat.StatusPersetujuan!.Trim().ToUpperInvariant(),
                surat.VerifikasiOleh,
                VerifikasiPada = FormatWaktu(surat.VerifikasiPada),
                surat.DitandatanganiOleh,
                DitandatanganiPada = FormatWaktu(surat.DitandatanganiPada),
                surat.CatatanPersetujuan,
                Sekarang = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            if (rows > 0)
            {
                _cachedSuratData[surat.ID_Surat] = surat;
                await _cacheService.SetAsync($"Surat_{surat.ID_Surat}", surat, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(10),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                }, cancellationToken);
                await ClearListCacheAsync();
            }

            return rows > 0;
        }

        public async Task<bool> SimpanScanSuratAsync(int idSurat, string? namaBerkas, CancellationToken cancellationToken = default)
        {
            if (idSurat <= 0) return false;

            await EnsureConnectionOpenAsync(cancellationToken);

            // Scan tidak boleh lolos jalur relatif (".." / berakar) — nama berkas saja.
            if (!string.IsNullOrWhiteSpace(namaBerkas) &&
                (Path.IsPathRooted(namaBerkas) || namaBerkas.Contains("..")))
            {
                throw new ArgumentException("Nama berkas scan tidak boleh memuat jalur.", nameof(namaBerkas));
            }

            const string sql = "UPDATE Surat SET FileScanSurat = @Berkas, UpdatedAt = @Sekarang WHERE ID_Surat = @ID_Surat;";
            var rows = await _connection.ExecuteAsync(sql, new
            {
                ID_Surat = idSurat,
                Berkas = string.IsNullOrWhiteSpace(namaBerkas) ? null : namaBerkas!.Trim(),
                Sekarang = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            if (rows > 0 && _cachedSuratData.TryGetValue(idSurat, out var cached))
                cached.FileScanSurat = string.IsNullOrWhiteSpace(namaBerkas) ? null : namaBerkas!.Trim();

            return rows > 0;
        }

        public async Task<int> CatatCetakAsync(int idSurat, CancellationToken cancellationToken = default)
        {
            if (idSurat <= 0) return 0;

            await EnsureConnectionOpenAsync(cancellationToken);

            // Satu pernyataan: naikkan lalu baca hasilnya — tidak ada jendela
            // antara UPDATE dan SELECT yang bisa menghitung ganda.
            const string sql = @"
                UPDATE Surat SET JumlahCetak = COALESCE(JumlahCetak, 0) + 1, UpdatedAt = @Sekarang
                WHERE ID_Surat = @ID_Surat;
                SELECT COALESCE(JumlahCetak, 0) FROM Surat WHERE ID_Surat = @ID_Surat;";

            var jumlah = await _connection.ExecuteScalarAsync<int>(sql, new
            {
                ID_Surat = idSurat,
                Sekarang = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            if (_cachedSuratData.TryGetValue(idSurat, out var cached))
                cached.JumlahCetak = jumlah;

            return jumlah;
        }

        public async Task<bool> SimpanVerifikasiAsync(int idSurat, string kode, string hash, CancellationToken cancellationToken = default)
        {
            if (idSurat <= 0)
                throw new ArgumentException("ID_Surat harus diisi.", nameof(idSurat));
            if (string.IsNullOrWhiteSpace(kode))
                throw new ArgumentException("Kode verifikasi tidak boleh kosong.", nameof(kode));

            await EnsureConnectionOpenAsync(cancellationToken);

            const string sql = @"
                UPDATE Surat SET
                    KodeVerifikasi  = @Kode,
                    HashVerifikasi  = @Hash,
                    UpdatedAt       = @Sekarang
                WHERE ID_Surat = @ID_Surat;";

            var rows = await _connection.ExecuteAsync(sql, new
            {
                ID_Surat = idSurat,
                Kode = kode.Trim().ToUpperInvariant(),
                Hash = string.IsNullOrWhiteSpace(hash) ? null : hash.Trim(),
                Sekarang = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            if (rows > 0 && _cachedSuratData.TryGetValue(idSurat, out var cached))
            {
                cached.KodeVerifikasi = kode.Trim().ToUpperInvariant();
                cached.HashVerifikasi = string.IsNullOrWhiteSpace(hash) ? null : hash!.Trim();
            }

            return rows > 0;
        }

        public async Task<SuratData?> GetByKodeVerifikasiAsync(string kode, CancellationToken cancellationToken = default)
        {
            var rapi = Services.KodeVerifikasiSurat.Normalisasi(kode);
            if (string.IsNullOrWhiteSpace(rapi)) return null;

            await EnsureConnectionOpenAsync(cancellationToken);

            // Cari ID lewat kolom baku (huruf besar/kecil diabaikan), lalu baca
            // suratnya lewat GetByIdAsync supaya data warga/instansi ikut utuh.
            var id = await _connection.ExecuteScalarAsync<int?>(
                "SELECT ID_Surat FROM Surat WHERE UPPER(KodeVerifikasi) = @Kode LIMIT 1",
                new { Kode = rapi.ToUpperInvariant() });

            return id.HasValue ? await GetByIdAsync(id.Value, null, cancellationToken) : null;
        }

        public async Task<Dictionary<string, int>> CountPerStatusPersetujuanAsync(CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);

            string ekspresiStatus = "COALESCE(NULLIF(UPPER(TRIM(StatusPersetujuan)), ''), 'TANPA_ALUR')";
            var rows = await _connection.QueryAsync<(string? Status, int Jumlah)>(
                $"SELECT {ekspresiStatus} AS Status, COUNT(*) AS Jumlah " +
                "FROM Surat " +
                $"GROUP BY {ekspresiStatus}");

            // Kunci selalu lengkap (termasuk yang berjumlah nol) supaya pemanggil
            // bisa membaca langsung tanpa berurusan dengan kunci muncul-hilang.
            var hasil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var status in StatusPersetujuanSurat.Semua)
                hasil[status] = 0;
            hasil[StatusPersetujuanSurat.TanpaAlur] = 0;

            foreach (var (status, jumlah) in rows)
            {
                var kunci = string.IsNullOrWhiteSpace(status) ? StatusPersetujuanSurat.TanpaAlur : status!;
                hasil[kunci] = hasil.GetValueOrDefault(kunci) + jumlah;
            }

            return hasil;
        }

        public async Task<List<BarisRekapPersetujuan>> GetRekapPersetujuanPerBulanAsync(int tahun, CancellationToken cancellationToken = default)
        {
            if (tahun < 1 || tahun > 9999)
                throw new ArgumentOutOfRangeException(nameof(tahun), tahun, "Tahun tidak sah.");

            await EnsureConnectionOpenAsync(cancellationToken);

            // TanggalSurat tersimpan ISO "yyyy-MM-dd", jadi substr memakai indeks
            // dan tetap memakai indeks tanggal. Status kosong dinormalisasi ke
            // TANPA_ALUR supaya satu kunci tunggal untuk surat biasa.
            var rows = await _connection.QueryAsync<BarisRekapPersetujuan>(@"
                SELECT substr(TanggalSurat, 1, 7) AS Bulan,
                       COALESCE(NULLIF(UPPER(TRIM(StatusPersetujuan)), ''), 'TANPA_ALUR') AS Status,
                       COUNT(*) AS Jumlah
                FROM Surat
                WHERE TanggalSurat >= @Mulai AND TanggalSurat < @Selesai
                GROUP BY 1, 2",
                new
                {
                    Mulai = new DateTime(tahun, 1, 1).ToString("yyyy-MM-dd"),
                    Selesai = new DateTime(tahun + 1, 1, 1).ToString("yyyy-MM-dd")
                });

            return rows.ToList();
        }

        /// <summary>Format waktu opsional untuk kolom teks SQLite (ISO, tanpa waktu = null).</summary>
        private static string? FormatWaktu(DateTime? waktu) =>
            waktu.HasValue ? waktu.Value.ToString("yyyy-MM-dd HH:mm:ss") : null;

    }
}
