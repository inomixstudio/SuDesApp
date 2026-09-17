using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>Satu baris jawaban dari Google Sheet.</summary>
    public class WaSheetRow
    {
        /// <summary>Nomor baris sebenarnya di Sheet (1-based, termasuk header).</summary>
        public int RowNumber { get; set; }
        /// <summary>Header (baris 1) yang telah dinormalkan: huruf kecil, tanpa spasi/garis bawah.</summary>
        public string[] Headers { get; set; } = Array.Empty<string>();
        /// <summary>Isi sel baris ini, sejajar dengan Headers (aman diakses via Get()).</summary>
        public string[] Values { get; set; } = Array.Empty<string>();

        /// <summary>Ambil nilai kolom berdasar nama header (case-insensitive, abaikan spasi).</summary>
        public string? Get(string header)
        {
            var key = Normalize(header);
            for (int i = 0; i < Headers.Length; i++)
            {
                if (Normalize(Headers[i]) == key)
                    return i < Values.Length ? Values[i] : null;
            }
            return null;
        }

        internal static string Normalize(string? s)
            => new((s ?? string.Empty).ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    /// <summary>
    /// Wrapper Google Sheets API memakai kredensial & token OAuth yang sama dengan
    /// GoogleDriveService (user aplikasi login sekali via jendela Google). Dipakai
    /// mode layanan online Google Sheet/Form: membaca baris jawaban warga dan
    /// menulis status pemrosesan.
    /// </summary>
    public class GoogleSheetsService
    {
        private readonly GoogleDriveService _drive;
        private readonly ILogger<GoogleSheetsService> _logger;
        private readonly SemaphoreSlim _clientLock = new(1, 1);
        private SheetsService? _service;

        public GoogleSheetsService(GoogleDriveService drive, ILogger<GoogleSheetsService> logger)
        {
            _drive = drive ?? throw new ArgumentNullException(nameof(drive));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Drive membuang klien saat token dihapus (ganti akun) atau kredensial
            // klien berubah — cache Sheets ikut dibuang agar tidak memakai akun lama.
            _drive.ClientInvalidated += ResetClient;
        }

        private async Task<SheetsService> GetClientAsync(CancellationToken ct)
        {
            if (_service != null) return _service;

            await _clientLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_service != null) return _service;
                var initializer = await _drive.BuildSharedInitializerAsync(ct).ConfigureAwait(false);
                _service = new SheetsService(initializer);
                return _service;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        /// <summary>Pesan kesalahan ramah bila Sheets API belum diaktifkan di proyek Cloud.</summary>
        public static string SheetApiHint =>
            "Google Sheets API belum aktif di proyek Google Cloud yang dipakai aplikasi.\n\n" +
            "Cara mengaktifkan (sekali saja, gratis):\n" +
            "1. Buka https://console.cloud.google.com/apis/library\n" +
            "2. Pilih proyek yang dipakai Client ID aplikasi.\n" +
            "3. Cari \"Google Sheets API\" lalu klik Enable.\n\n" +
            "Detail teknis: {0}";

        /// <summary>Mengambil satu rentang (range) Sheet sebagai daftar baris teks.</summary>
        public async Task<IList<IList<object>>> GetValuesAsync(string sheetId, string range, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var request = client.Spreadsheets.Values.Get(sheetId, range!);
            var response = await request.ExecuteAsync(ct).ConfigureAwait(false);
            return response.Values ?? new List<IList<object>>();
        }

        /// <summary>Membaca seluruh baris jawaban (tab) termasuk header baris 1.</summary>
        public async Task<List<WaSheetRow>> GetRowsAsync(string sheetId, string tabName, CancellationToken ct = default)
        {
            var values = await GetValuesAsync(sheetId, $"'{tabName.Replace("'", "''")}'", ct).ConfigureAwait(false);

            var rows = new List<WaSheetRow>();
            if (values.Count == 0) return rows;

            var headers = values[0].Select(c => c?.ToString() ?? string.Empty).ToArray();
            for (int i = 1; i < values.Count; i++)
            {
                var line = values[i].Select(c => c?.ToString() ?? string.Empty).ToArray();
                rows.Add(new WaSheetRow
                {
                    RowNumber = i + 1, // indeks API 0-based + header = baris Sheet sebenarnya
                    Headers = headers,
                    Values = line
                });
            }
            return rows;
        }

        /// <summary>Menulis satu sel (mis. kolom Status) pada baris tertentu.</summary>
        public async Task WriteCellAsync(string sheetId, string tabName, int rowNumber, string columnName, string value, CancellationToken ct = default)
        {
            var client = await GetClientAsync(ct).ConfigureAwait(false);
            var range = $"'{tabName.Replace("'", "''")}'!{columnName}{rowNumber}";
            var body = new ValueRange { Values = new List<IList<object>> { new List<object> { value ?? string.Empty } } };
            var request = client.Spreadsheets.Values.Update(body, sheetId, range);
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await request.ExecuteAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Status Sheet ditulis: {SheetId} {Tab} baris {Row} kolom {Kolom} = {Nilai}",
                sheetId, tabName, rowNumber, columnName, value);
        }

        /// <summary>
        /// Memastikan kolom status ada di baris header (A1). Bila belum ada, dibuat
        /// baru setelah kolom terakhir yang terisi, dan mengembalikan huruf kolomnya.
        /// </summary>
        public async Task<string> EnsureStatusColumnAsync(string sheetId, string tabName, string columnName, CancellationToken ct = default)
        {
            var values = await GetValuesAsync(sheetId, $"'{tabName.Replace("'", "''")}'!1:1", ct).ConfigureAwait(false);
            var header = values.Count > 0 ? values[0] : new List<object>();
            var headerText = header.Select(c => c?.ToString() ?? string.Empty).ToList();

            for (int i = 0; i < headerText.Count; i++)
            {
                if (WaSheetRow.Normalize(headerText[i]) == WaSheetRow.Normalize(columnName))
                    return ColumnLetter(i);
            }

            // Belum ada → buat setelah kolom terakhir yang terisi (skip kolom kosong interkalasi).
            var lastFilled = -1;
            for (int i = headerText.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(headerText[i])) { lastFilled = i; break; }
            }
            var newIdx = lastFilled + 1;

            await WriteCellAsync(sheetId, tabName, 1, ColumnLetter(newIdx), columnName, ct).ConfigureAwait(false);
            _logger.LogInformation("Kolom status Sheet dibuat: {Kolom} (kolom {Huruf})", columnName, ColumnLetter(newIdx));
            return ColumnLetter(newIdx);
        }

        /// <summary>
        /// Buang klien Sheets yang di-cache (dipicu <see cref="GoogleDriveService.ClientInvalidated"/>
        /// saat token dihapus / ganti akun / kredensial klien berubah).
        /// </summary>
        public void ResetClient()
        {
            var old = _service;
            _service = null;
            old?.Dispose();
        }

        /// <summary>Konversi indeks 0-based ke huruf kolom (0→A, 25→Z, 26→AA).</summary>
        public static string ColumnLetter(int index)
        {
            var name = string.Empty;
            var n = index;
            while (n >= 0)
            {
                name = (char)('A' + n % 26) + name;
                n = n / 26 - 1;
            }
            return name;
        }

        /// <summary>Cek ringan ketersediaan Sheets API + akses baca (dipakai tombol Uji Koneksi).</summary>
        public async Task<(bool Ok, string Pesan)> TestConnectionAsync(string sheetId, string tabName, CancellationToken ct = default)
        {
            try
            {
                var rows = await GetRowsAsync(sheetId, tabName, ct).ConfigureAwait(false);
                return (true, $"Terhubung ✓ — {rows.Count} baris jawaban terbaca dari tab \"{tabName}\".");
            }
            catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden
                || gex.HttpStatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogWarning(gex, "Sheets API tidak tersedia/akses ditolak saat uji koneksi");
                return (false, SheetApiHint.Replace("{0}", gex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Uji koneksi Google Sheet gagal");
                return (false, "Gagal: " + ex.Message);
            }
        }
    }
}
