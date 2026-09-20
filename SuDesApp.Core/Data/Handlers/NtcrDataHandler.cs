using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;

namespace SuDesApp.Data.Handlers
{
    /// <summary>
    /// Handler data NTCR (formulir persyaratan pernikahan N1-N6 sesuai Keputusan
    /// Dirjen Bimas Islam No. 473 Tahun 2020, ditambah surat keterangan numpang
    /// nikah N8).
    /// Satu class handler untuk seluruh jenis; NamaJenis diberikan
    /// melalui konstruktor agar dapat diregistrasi beberapa kali di DI.
    /// Kolom baku tersimpan di tabel "NTCR"; kolom khusus tiap blanko dan
    /// identitas orang tua disimpan sebagai JSON di kolom "DetailJson".
    /// </summary>
    public class NtcrDataHandler : ISuratDataHandler
    {
        private readonly string _namaJenis;
        private readonly ILogger<NtcrDataHandler> _logger;
        private readonly ICacheService _cacheService;

        public NtcrDataHandler(
            string namaJenis,
            ILogger<NtcrDataHandler> logger,
            ICacheService cacheService)
        {
            _namaJenis = namaJenis ?? throw new ArgumentNullException(nameof(namaJenis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public string NamaJenis => _namaJenis;

        public async Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Inserting {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);

            if (suratData == null || suratData.Ntcr == null)
            {
                _logger.LogWarning("SuratData or Ntcr model is empty for ID_Surat={ID_Surat}", idSurat);
                throw new ArgumentException($"SuratData and Ntcr model are required for {_namaJenis} surat");
            }

            try
            {
                await EnsureNtcrColumnsAsync(connection, transaction);

                var validationErrors = await suratData.ValidateAsync();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Validation failed for {NamaJenis}: ID_Surat={ID_Surat}, Errors={Errors}",
                        _namaJenis, idSurat, string.Join("; ", validationErrors));
                    throw new ValidationException($"Validation failed: {string.Join(", ", validationErrors)}");
                }

                const string insertQuery = @"
                    INSERT INTO NTCR (
                        ID_Surat,
                        ID_CalonIstri,
                        NikIstri,
                        NamaIstri,
                        TempatLahirIstri,
                        TanggalLahirIstri,
                        AgamaIstri,
                        PekerjaanIstri,
                        AlamatIstri,
                        NamaAyahCalonSuami,
                        NamaIbuCalonSuami,
                        NamaAyahCalonIstri,
                        NamaIbuCalonIstri,
                        StatusPerkawinanIstri,
                        KeteranganTemuan,
                        TujuanSurat,
                        DetailJson
                    ) VALUES (
                        @ID_Surat,
                        @ID_CalonIstri,
                        @NikIstri,
                        @NamaIstri,
                        @TempatLahirIstri,
                        @TanggalLahirIstri,
                        @AgamaIstri,
                        @PekerjaanIstri,
                        @AlamatIstri,
                        @NamaAyahCalonSuami,
                        @NamaIbuCalonSuami,
                        @NamaAyahCalonIstri,
                        @NamaIbuCalonIstri,
                        @StatusPerkawinanIstri,
                        @KeteranganTemuan,
                        @TujuanSurat,
                        @DetailJson
                    )";

                var ntcr = suratData.Ntcr;
                await connection.ExecuteAsync(insertQuery, new
                {
                    ID_Surat = idSurat,
                    ID_CalonIstri = ntcr.ID_CalonIstri,
                    NikIstri = ntcr.NikIstri,
                    NamaIstri = ntcr.NamaIstri,
                    TempatLahirIstri = ntcr.TempatLahirIstri,
                    TanggalLahirIstri = ntcr.TanggalLahirIstri,
                    AgamaIstri = ntcr.AgamaIstri,
                    PekerjaanIstri = ntcr.PekerjaanIstri,
                    AlamatIstri = ntcr.AlamatIstri,
                    NamaAyahCalonSuami = ntcr.NamaAyahCalonSuami,
                    NamaIbuCalonSuami = ntcr.NamaIbuCalonSuami,
                    NamaAyahCalonIstri = ntcr.NamaAyahCalonIstri,
                    NamaIbuCalonIstri = ntcr.NamaIbuCalonIstri,
                    StatusPerkawinanIstri = ntcr.StatusPerkawinanIstri,
                    KeteranganTemuan = ntcr.KeteranganTemuan,
                    TujuanSurat = ntcr.TujuanSurat,
                    DetailJson = SerializeDetail(ntcr)
                }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully inserted {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);
                throw new DataAccessException($"Failed to insert {_namaJenis} data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction)
        {
            _logger.LogDebug("Deleting {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);

            try
            {
                const string deleteQuery = "DELETE FROM NTCR WHERE ID_Surat = @ID_Surat";
                await connection.ExecuteAsync(deleteQuery, new { ID_Surat = idSurat }, transaction);

                await _cacheService.RemoveAsync<SuratData>(CacheKeys.Surat(idSurat));
                _logger.LogInformation("Successfully deleted {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, idSurat);
                throw new DataAccessException($"Failed to delete {_namaJenis} data for ID_Surat {idSurat}.", ex);
            }
        }

        public async Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection)
        {
            _logger.LogDebug("Loading {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, suratData?.ID_Surat ?? 0);

            if (suratData == null)
            {
                _logger.LogWarning("SuratData is null for ID_Surat={ID_Surat}", suratData?.ID_Surat);
                throw new ArgumentNullException(nameof(suratData));
            }

            try
            {
                await EnsureNtcrColumnsAsync(connection, null);

                const string loadQuery = @"
                    SELECT ID_CalonIstri, NikIstri, NamaIstri, TempatLahirIstri, TanggalLahirIstri,
                           AgamaIstri, PekerjaanIstri, AlamatIstri,
                           NamaAyahCalonSuami, NamaIbuCalonSuami,
                           NamaAyahCalonIstri, NamaIbuCalonIstri,
                           StatusPerkawinanIstri, KeteranganTemuan, TujuanSurat, DetailJson
                    FROM NTCR
                    WHERE ID_Surat = @ID_Surat";

                var row = await connection.QueryFirstOrDefaultAsync<NtcrRow>(loadQuery, new { ID_Surat = suratData.ID_Surat });
                if (row == null)
                {
                    _logger.LogWarning("No {NamaJenis} data found for ID_Surat={ID_Surat}", _namaJenis, suratData.ID_Surat);
                    return;
                }

                suratData.Ntcr ??= new NtcrData();
                suratData.Ntcr.ID_CalonIstri = row.ID_CalonIstri;
                suratData.Ntcr.NikIstri = row.NikIstri;
                suratData.Ntcr.NamaIstri = row.NamaIstri;
                suratData.Ntcr.TempatLahirIstri = row.TempatLahirIstri;
                suratData.Ntcr.TanggalLahirIstri = row.TanggalLahirIstri;
                suratData.Ntcr.AgamaIstri = row.AgamaIstri;
                suratData.Ntcr.PekerjaanIstri = row.PekerjaanIstri;
                suratData.Ntcr.AlamatIstri = row.AlamatIstri;
                suratData.Ntcr.NamaAyahCalonSuami = row.NamaAyahCalonSuami!;
                suratData.Ntcr.NamaIbuCalonSuami = row.NamaIbuCalonSuami!;
                suratData.Ntcr.NamaAyahCalonIstri = row.NamaAyahCalonIstri!;
                suratData.Ntcr.NamaIbuCalonIstri = row.NamaIbuCalonIstri!;
                suratData.Ntcr.StatusPerkawinanIstri = row.StatusPerkawinanIstri;
                suratData.Ntcr.KeteranganTemuan = row.KeteranganTemuan;
                suratData.Ntcr.TujuanSurat = row.TujuanSurat;
                ApplyDetail(suratData.Ntcr, row.DetailJson!);

                _logger.LogInformation("Successfully loaded {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, suratData.ID_Surat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading {NamaJenis} data for ID_Surat={ID_Surat}", _namaJenis, suratData.ID_Surat);
                throw new DataRetrievalException($"Failed to load {_namaJenis} data for ID_Surat {suratData.ID_Surat}.", ex);
            }
        }

        private class NtcrRow
        {
            public int ID_CalonIstri { get; set; }
            public string ?NikIstri { get; set; }
            public string ?NamaIstri { get; set; }
            public string ?TempatLahirIstri { get; set; }
            public string ?TanggalLahirIstri { get; set; }
            public string ?AgamaIstri { get; set; }
            public string ?PekerjaanIstri { get; set; }
            public string ?AlamatIstri { get; set; }
            public string ?NamaAyahCalonSuami { get; set; }
            public string ?NamaIbuCalonSuami { get; set; }
            public string ?NamaAyahCalonIstri { get; set; }
            public string ?NamaIbuCalonIstri { get; set; }
            public string ?StatusPerkawinanIstri { get; set; }
            public string ?KeteranganTemuan { get; set; }
            public string ?TujuanSurat { get; set; }
            public string ?DetailJson { get; set; }
        }

        /// <summary>
        /// Kolom khusus tiap blanko (N1..N6, N8) dan identitas orang tua — disimpan
        /// sebagai satu kolom JSON agar penambahan field baru berikutnya tidak
        /// menambah puluhan kolom database.
        /// </summary>
        private sealed class NtcrDetail
        {
            public string ?KewarganegaraanIstri { get; set; }
            public string ?PihakDiterangkanN1 { get; set; }
            public string ?TujuanKua { get; set; }
            public string ?HariTanggalJamAkad { get; set; }
            public string ?TempatAkad { get; set; }
            public string ?TanggalPenetapanIsbat { get; set; }
            public string ?PengadilanAgama { get; set; }
            public string ?LampiranTambahan { get; set; }
            public string ?TanggalDiterima { get; set; }
            public string ?PihakAnakIzinOrtu { get; set; }
            public string ?PihakMeninggal { get; set; }
            public string ?TanggalMeninggal { get; set; }
            public string ?TempatMeninggal { get; set; }
            public string ?DesaNumpang { get; set; }
            public string ?KecamatanNumpang { get; set; }
            public string ?KabupatenNumpang { get; set; }
            public string ?KecamatanIstri { get; set; }
            public string ?KabupatenIstri { get; set; }
            public NtcrOrangTua ?AyahCalonSuami { get; set; }
            public NtcrOrangTua ?IbuCalonSuami { get; set; }
            public NtcrOrangTua ?AyahCalonIstri { get; set; }
            public NtcrOrangTua ?IbuCalonIstri { get; set; }
        }

        private static string SerializeDetail(NtcrData ntcr)
        {
            if (ntcr == null) return null!;

            var detail = new NtcrDetail
            {
                KewarganegaraanIstri = ntcr.KewarganegaraanIstri,
                PihakDiterangkanN1 = ntcr.PihakDiterangkanN1,
                TujuanKua = ntcr.TujuanKua,
                HariTanggalJamAkad = ntcr.HariTanggalJamAkad,
                TempatAkad = ntcr.TempatAkad,
                TanggalPenetapanIsbat = ntcr.TanggalPenetapanIsbat,
                PengadilanAgama = ntcr.PengadilanAgama,
                LampiranTambahan = ntcr.LampiranTambahan,
                TanggalDiterima = ntcr.TanggalDiterima,
                PihakAnakIzinOrtu = ntcr.PihakAnakIzinOrtu,
                PihakMeninggal = ntcr.PihakMeninggal,
                TanggalMeninggal = ntcr.TanggalMeninggal,
                TempatMeninggal = ntcr.TempatMeninggal,
                DesaNumpang = ntcr.DesaNumpang,
                KecamatanNumpang = ntcr.KecamatanNumpang,
                KabupatenNumpang = ntcr.KabupatenNumpang,
                KecamatanIstri = ntcr.KecamatanIstri,
                KabupatenIstri = ntcr.KabupatenIstri,
                AyahCalonSuami = ntcr.AyahCalonSuami?.Clone(),
                IbuCalonSuami = ntcr.IbuCalonSuami?.Clone(),
                AyahCalonIstri = ntcr.AyahCalonIstri?.Clone(),
                IbuCalonIstri = ntcr.IbuCalonIstri?.Clone()!
            };

            return JsonSerializer.Serialize(detail);
        }

        private void ApplyDetail(NtcrData ntcr, string detailJson)
        {
            if (ntcr == null || string.IsNullOrWhiteSpace(detailJson)) return;

            try
            {
                var detail = JsonSerializer.Deserialize<NtcrDetail>(detailJson);
                if (detail == null) return;

                ntcr.KewarganegaraanIstri = detail.KewarganegaraanIstri ?? ntcr.KewarganegaraanIstri;
                ntcr.PihakDiterangkanN1 = detail.PihakDiterangkanN1 ?? ntcr.PihakDiterangkanN1;
                ntcr.TujuanKua = detail.TujuanKua;
                ntcr.HariTanggalJamAkad = detail.HariTanggalJamAkad;
                ntcr.TempatAkad = detail.TempatAkad;
                ntcr.TanggalPenetapanIsbat = detail.TanggalPenetapanIsbat;
                ntcr.PengadilanAgama = detail.PengadilanAgama;
                ntcr.LampiranTambahan = detail.LampiranTambahan;
                ntcr.TanggalDiterima = detail.TanggalDiterima;
                ntcr.PihakAnakIzinOrtu = detail.PihakAnakIzinOrtu ?? ntcr.PihakAnakIzinOrtu;
                ntcr.PihakMeninggal = detail.PihakMeninggal ?? ntcr.PihakMeninggal;
                ntcr.TanggalMeninggal = detail.TanggalMeninggal;
                ntcr.TempatMeninggal = detail.TempatMeninggal;
                ntcr.DesaNumpang = detail.DesaNumpang;
                ntcr.KecamatanNumpang = detail.KecamatanNumpang;
                ntcr.KabupatenNumpang = detail.KabupatenNumpang;
                ntcr.KecamatanIstri = detail.KecamatanIstri;
                ntcr.KabupatenIstri = detail.KabupatenIstri;
                ntcr.AyahCalonSuami.CopyFrom(detail.AyahCalonSuami!);
                ntcr.IbuCalonSuami.CopyFrom(detail.IbuCalonSuami!);
                ntcr.AyahCalonIstri.CopyFrom(detail.AyahCalonIstri!);
                ntcr.IbuCalonIstri.CopyFrom(detail.IbuCalonIstri!);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "DetailJson NTCR tidak dapat dibaca untuk ID_Surat={ID_Surat}", ntcr.ID_CalonIstri);
            }
        }

        /// <summary>
        /// Self-heal: pastikan kolom hasil penambahan fitur ada pada database lama
        /// (tabel dibuat dari desa.db.sql versi awal tanpa kolom baru).
        /// </summary>
        private async Task EnsureNtcrColumnsAsync(IDbConnection connection, IDbTransaction? transaction)
        {
            try
            {
                foreach (var columnName in new[] { "StatusPerkawinanIstri", "KeteranganTemuan", "TujuanSurat", "DetailJson" })
                {
                    var columnExists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('NTCR') WHERE name = @columnName COLLATE NOCASE",
                        new { columnName }) > 0;

                    if (!columnExists)
                    {
                        _logger.LogInformation("Menambahkan kolom {Column} ke tabel NTCR (self-heal).", columnName);
                        await connection.ExecuteAsync($"ALTER TABLE [NTCR] ADD COLUMN [{columnName}] TEXT", null, transaction);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memastikan kolom baru ada di tabel NTCR (self-heal).");
            }
        }
    }
}