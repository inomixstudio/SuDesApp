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

        private async Task LoadRelatedDataAsync(SuratData surat, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            var handler = _suratDataHandlers.FirstOrDefault(h => h.NamaJenis.Equals(surat.NamaJenis, StringComparison.OrdinalIgnoreCase));
            if (handler != null)
                await handler.LoadRelatedDataAsync(surat, _connection);
        }

        public async Task<IEnumerable<SuratData>> GetFilteredAsync(
    FilterConditions filters,
    string sortBy,
    bool ascending,
    int skip,
    int take,
    CancellationToken cancellationToken = default)
        {
            await EnsureConnectionOpenAsync(cancellationToken);
            var (whereClause, parameters) = await BuildWhereClauseAsync(filters);
            var orderBy = GetValidSortColumn(sortBy);
            var direction = ascending ? "ASC" : "DESC";
            var baseQuery = _queryProvider.GetQuery("GetFilteredSuratData");

            var query = baseQuery
                .Replace("{WhereClause}", whereClause)
                .Replace("{OrderBy}", orderBy)
                .Replace("{Direction}", direction);

            parameters.Add("Take", take);
            parameters.Add("Skip", skip);

            try
            {
                // GUNAKAN PEMETAAN MANUAL DENGAN KONVERSI YANG AMAN
                var dynamicResults = await _connection.QueryAsync(query, parameters);
                var results = new List<SuratData>();
                var gagalDipetakan = 0;

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
                            KodeJenis = SafeGetString(row.KodeJenis),
                            NamaJenis = SafeGetString(row.NamaJenis)
                        };

                        // MAP WARGA DATA DENGAN KONVERSI AMAN
                        if (HasProperty(row, "ID_Warga") && SafeGetInt(row.ID_Warga) > 0)
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
                                CreatedAt = SafeGetDateTime(row.CreatedAt, DateTime.Now),
                                UpdatedAt = SafeGetDateTime(row.UpdatedAt, DateTime.Now)
                            };
                        }

                        // MAP INSTANSI JIKA ADA
                        if (HasProperty(row, "NamaInstansi") && !string.IsNullOrEmpty(SafeGetString(row.NamaInstansi)))
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

                        // MAP NTCR DATA (N1-N4) bila jenis surat termasuk NTCR
                        if (IsNtcrJenis(surat.NamaJenis))
                        {
                            await AttachNtcrDataAsync(surat, cancellationToken);
                        }

                        results.Add(surat);
                    }
                    catch (Exception ex)
                    {
                        // Lewati baris yang gagal dipetakan, TAPI catat dulu.
                        // Polo kosong di sini membuat surat yang ada di database
                        // hilang tanpa jejak: petugas mencari, mendapat "tidak
                        // ditemukan", dan tidak ada satu baris pun untuk menelusuri.
                        // Blok kembar di GetAllSuratDataAsync sudah memakai
                        // LogWarning, jadi kelalaian ini memang tidak disengaja.
                        // row bersifat dynamic. Argumennya harus lebih dulu dikunci ke tipe
                        // statis DAN hasilnya di-cast eksplisit: pemanggilan
                        // fungsi dengan argumen dynamic menghasilkan nilai
                        // dynamic, sehingga seluruh LogWarning ikut dynamic dan
                        // compiler menolak extension method (CS1973).
                        var idGagal = (int)SafeGetInt((object?)row.ID_Surat);
                        _logger.LogWarning(ex,
                            "Baris surat ID {Id} gagal dipetakan - tidak tampil di hasil register.",
                            idGagal);
                        gagalDipetakan++;
                    }
                }

                if (gagalDipetakan > 0)
                {
                    _logger.LogWarning(
                        "GetFilteredAsync: {Gagal} dari {Total} baris register gagal dipetakan dan tidak ditampilkan.",
                        gagalDipetakan, dynamicResults.Count());
                }

                return results;
            }
            catch (SqliteException ex)
            {
                _logger.LogError(ex, "SQLite error in GetFilteredAsync");
                throw;
            }
        }

        // HELPER METHODS UNTUK KONVERSI AMAN
        private static int SafeGetInt(object? value, int defaultValue = 0)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return (int)longValue;

            if (int.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue;
        }

        private static string SafeGetString(object value, string? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue!;

            return value.ToString() ?? defaultValue ?? string.Empty;
        }

        private static string BuildAlamatLengkap(string dusun, string desa, string kecamatan, string kabupaten, string? rt = null, string? rw = null)
        {
            var rtRw = GeneratorPdf.AlamatFormatter.FormatRtRw(rt, rw);
            var parts = new[]
            {
                (dusun ?? string.Empty).Trim(),
                rtRw,
                (desa ?? string.Empty).Trim(),
                (kecamatan ?? string.Empty).Trim(),
                (kabupaten ?? string.Empty).Trim()
            };

            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static DateTime SafeGetDateTime(object value, DateTime? defaultValue = null)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue ?? DateTime.MinValue;

            if (value is DateTime dateTimeValue)
                return dateTimeValue;

            if (DateTime.TryParse(value.ToString(), out var result))
                return result;

            return defaultValue ?? DateTime.MinValue;
        }

        private static bool HasProperty(dynamic obj, string propertyName)
        {
            try
            {
                var dict = obj as IDictionary<string, object>;
                return dict?.ContainsKey(propertyName) ?? false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(string, DynamicParameters)> BuildWhereClauseAsync(FilterConditions filters)
        {
            var conditions = new List<string>();
            var parameters = new DynamicParameters();

            if (!string.IsNullOrEmpty(filters.JenisSurat))
            {
                if (filters.JenisSurat.Equals("GROUP_KETERANGAN_DESA", StringComparison.OrdinalIgnoreCase))
                {
                    var jenisGroup = await _jenisSuratRepository.GetJenisSuratKeteranganDesa();
                    _logger.LogDebug("JenisGroup for GROUP_KETERANGAN_DESA: {@JenisGroup}", jenisGroup);
                    if (jenisGroup?.Any() == true)
                    {
                        conditions.Add($"UPPER(js.NamaJenis) IN @JenisGroup");
                        parameters.Add("JenisGroup", jenisGroup.Select(j => j.ToUpperInvariant()));
                    }
                    else
                    {
                        _logger.LogWarning("No jenis surat found for GROUP_KETERANGAN_DESA, skipping filter.");
                    }
                }
                else if (filters.JenisSurat.Equals("GROUP_NTCR", StringComparison.OrdinalIgnoreCase))
                {
                    var ntcrGroup = SuratConstants.NtcrSemua;
                    conditions.Add("UPPER(js.NamaJenis) IN @JenisGroup");
                    parameters.Add("JenisGroup", ntcrGroup.Select(j => j.ToUpperInvariant()));
                }
                else if (filters.JenisSurat != "ALL_TYPES")
                {
                    conditions.Add("UPPER(js.NamaJenis) = @JenisSurat");
                    parameters.Add("JenisSurat", filters.JenisSurat.ToUpperInvariant());
                }
            }

            if (filters.ExcludeJenisNames?.Any() == true)
            {
                conditions.Add("UPPER(js.NamaJenis) NOT IN @ExcludeJenis");
                parameters.Add("ExcludeJenis", filters.ExcludeJenisNames.Select(n => n.ToUpperInvariant()));
            }

            if (!string.IsNullOrEmpty(filters.Tahun))
            {
                // Rentang tahun, BUKAN strftime('%Y', ...). Fungsi strftime membuat
                // syaratnya tidak bisa memakai indeks (seluruh tabel Surat dipindai
                // setiap kali register dibuka, dan filter tahun adalah filter bawaan).
                // TanggalSurat disimpan ISO "yyyy-MM-dd", jadi perbandingan teks
                // langsung sudah tepat dan indeks tanggal benar-benar terpakai.
                if (int.TryParse(filters.Tahun, out int tahun) && tahun is >= 1 and < 9999)
                {
                    conditions.Add("s.TanggalSurat >= @TahunMulai AND s.TanggalSurat < @TahunSelesai");
                    parameters.Add("TahunMulai", new DateTime(tahun, 1, 1).ToString("yyyy-MM-dd"));
                    parameters.Add("TahunSelesai", new DateTime(tahun + 1, 1, 1).ToString("yyyy-MM-dd"));
                }
                else
                {
                    conditions.Add("strftime('%Y', s.TanggalSurat) = @Tahun");
                    parameters.Add("Tahun", filters.Tahun);
                }
            }

            if (!string.IsNullOrEmpty(filters.Status))
            {
                conditions.Add("s.Status = @Status");
                parameters.Add("Status", filters.Status);
            }

            // Rentang tanggal eksplisit dari panel filter register. Sebelumnya properti
            // ini diisi oleh halaman Register Surat tetapi TIDAK pernah dipakai di
            // syarat query, sehingga filter tanggal tidak berpengaruh apa pun.
            if (filters.TanggalMulai.HasValue)
            {
                conditions.Add("s.TanggalSurat >= @TanggalMulaiFilter");
                parameters.Add("TanggalMulaiFilter", filters.TanggalMulai.Value.ToString("yyyy-MM-dd"));
            }

            if (filters.TanggalSelesai.HasValue)
            {
                conditions.Add("s.TanggalSurat <= @TanggalSelesaiFilter");
                parameters.Add("TanggalSelesaiFilter", filters.TanggalSelesai.Value.ToString("yyyy-MM-dd"));
            }

            if (!string.IsNullOrEmpty(filters.SearchText))
            {
                var searchText = $"%{filters.SearchText}%";

                // AdditionalData ikut dicari: surat dari Template Surat menyimpan seluruh
                // isiannya (nama penerima, NIK, keperluan, isi surat) pada kolom itu,
                // sehingga surat template tetap dapat ditemukan lewat nama/NIK walau
                // kolom warga-nya hanya baris penanda milik aplikasi.
                conditions.Add("(s.NomorSurat LIKE @SearchText OR s.Keperluan LIKE @SearchText OR w.Nama LIKE @SearchText OR i.NamaInstansi LIKE @SearchText OR w.NIK LIKE @SearchText OR s.AdditionalData LIKE @SearchText)");
                parameters.Add("SearchText", searchText);
            }

            var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : " ";
            _logger.LogDebug("Generated WHERE clause: {WhereClause}", whereClause);
            return (whereClause, parameters);
        }

        /// <summary>Apakah NamaJenis termasuk kelompok formulir NTCR (N1-N6).</summary>
        private static bool IsNtcrJenis(string? namaJenis) => SuratConstants.IsNtcr(namaJenis!);

        /// <summary>
        /// Melengkapi data pasangan NTCR untuk baris register (Ntcr tidak ikut query dasar).
        /// Database lama yang belum punya kolom baru otomatis jatuh ke seleksi kolom dasar.
        /// </summary>
        private async Task AttachNtcrDataAsync(SuratData surat, CancellationToken cancellationToken)
        {
            try
            {
                var row = await _connection.QueryFirstOrDefaultAsync<NtcrRegisterRow>(
                    @"SELECT ID_CalonIstri, NikIstri, NamaIstri, TempatLahirIstri, TanggalLahirIstri,
                             AgamaIstri, PekerjaanIstri, AlamatIstri,
                             NamaAyahCalonSuami, NamaIbuCalonSuami,
                             NamaAyahCalonIstri, NamaIbuCalonIstri,
                             StatusPerkawinanIstri, KeteranganTemuan, TujuanSurat
                      FROM NTCR WHERE ID_Surat = @ID_Surat",
                    new { surat.ID_Surat });
                if (row == null) return;

                surat.Ntcr = new NtcrData
                {
                    ID_CalonIstri = row.ID_CalonIstri,
                    NikIstri = row.NikIstri,
                    NamaIstri = row.NamaIstri,
                    TempatLahirIstri = row.TempatLahirIstri,
                    TanggalLahirIstri = row.TanggalLahirIstri,
                    AgamaIstri = row.AgamaIstri,
                    PekerjaanIstri = row.PekerjaanIstri,
                    AlamatIstri = row.AlamatIstri,
                    NamaAyahCalonSuami = row.NamaAyahCalonSuami,
                    NamaIbuCalonSuami = row.NamaIbuCalonSuami,
                    NamaAyahCalonIstri = row.NamaAyahCalonIstri,
                    NamaIbuCalonIstri = row.NamaIbuCalonIstri,
                    StatusPerkawinanIstri = row.StatusPerkawinanIstri,
                    KeteranganTemuan = row.KeteranganTemuan,
                    TujuanSurat = row.TujuanSurat
                };
            }
            catch (SqliteException)
            {
                // Database lama sebelum kolom StatusPerkawinanIstri/KeteranganTemuan/TujuanSurat.
                try
                {
                    var row = await _connection.QueryFirstOrDefaultAsync<NtcrRegisterRow>(
                        @"SELECT ID_CalonIstri, NikIstri, NamaIstri, TempatLahirIstri, TanggalLahirIstri,
                                 AgamaIstri, PekerjaanIstri, AlamatIstri,
                                 NamaAyahCalonSuami, NamaIbuCalonSuami,
                                 NamaAyahCalonIstri, NamaIbuCalonIstri
                          FROM NTCR WHERE ID_Surat = @ID_Surat",
                        new { surat.ID_Surat });
                    if (row == null) return;

                    surat.Ntcr = new NtcrData
                    {
                        ID_CalonIstri = row.ID_CalonIstri,
                        NikIstri = row.NikIstri,
                        NamaIstri = row.NamaIstri,
                        TempatLahirIstri = row.TempatLahirIstri,
                        TanggalLahirIstri = row.TanggalLahirIstri,
                        AgamaIstri = row.AgamaIstri,
                        PekerjaanIstri = row.PekerjaanIstri,
                        AlamatIstri = row.AlamatIstri,
                        NamaAyahCalonSuami = row.NamaAyahCalonSuami,
                        NamaIbuCalonSuami = row.NamaIbuCalonSuami,
                        NamaAyahCalonIstri = row.NamaAyahCalonIstri,
                        NamaIbuCalonIstri = row.NamaIbuCalonIstri!
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal memuat data NTCR (kolom dasar) untuk ID_Surat={ID}", surat.ID_Surat);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat data NTCR untuk ID_Surat={ID}", surat.ID_Surat);
            }
        }

    }
}
