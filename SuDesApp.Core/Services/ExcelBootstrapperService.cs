using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using SuDesApp.Data.Models;
using System.Collections.Concurrent;

namespace SuDesApp.Services
{
    public sealed class ExcelBootstrapperService : IDisposable
    {
        private readonly AppConfig _appConfig;
        private readonly FileService _fileService;
        private readonly ILogger<ExcelBootstrapperService> _logger;
        private readonly ConcurrentDictionary<string, bool> _initializedFiles = new();
        private bool _disposed;

        // Sentralisasi konfigurasi file
        private static readonly Dictionary<string, (string SheetName, string[] Headers)> FileTemplates = new()
        {
            //{
            //    "GarapanData.xlsx",
            //    ("Garapan", ["NIK", "NomorSurat", "JumlahGarapan", "Lokasi", "NomorPersil", "Luas", "KeteranganGarapan", "PemilikTanah", "Timestamp"])
            //},
            {
                "ArsipSurat.xlsx",
                ("Arsip Surat", ["No.", "Jenis Surat", "Nomor Surat", "Tanggal Surat", "Tgl Diterima/Dikirim", "Asal/Tujuan", "Perihal", "Isi Ringkas", "Keterangan"])
            },
            {
                "ArsipKeputusan.xlsx",
                ("Arsip Keputusan", ["No.", "Jenis Keputusan", "Nomor", "Tanggal", "Tentang", "Keterangan", "File Lampiran"])
            },
            //{
            //    "WargaBantu.xlsx",
            //    ("BedaNama_Pembantu", ["Nomor Surat", "Tanggal Surat", "Sumber Data", "NIK", "Nama", "Tempat Lahir", "Tanggal Lahir", "Jenis Kelamin", "Alamat"])
            //},
        };

        public ExcelBootstrapperService(AppConfig appConfig, FileService fileService, ILogger<ExcelBootstrapperService> logger)
        {
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");
            _logger.LogInformation("ExcelBootstrapperService initialized with TemplateFolder: {TemplateFolder}", _appConfig.TemplateFolder);
        }

        public async Task InitializeAllAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExcelBootstrapperService));

            var tasks = new List<Task>();
            foreach (var (fileName, (sheetName, headers)) in FileTemplates)
            {
                tasks.Add(EnsureFileExistsAsync(fileName, sheetName, headers));
            }

            await Task.WhenAll(tasks);
            _logger.LogInformation("All Excel files initialized successfully in {TemplateFolder}", _appConfig.TemplateFolder);
        }

        private async Task EnsureFileExistsAsync(string fileName, string sheetName, string[] headers)
        {
            string filePath = Path.Combine(_appConfig.TemplateFolder, fileName);
            filePath = _fileService.SanitizePath(filePath); // Gunakan FileService untuk sanitasi path
            if (_initializedFiles.ContainsKey(filePath)) return;

            try
            {
                bool needsCreation = !await _fileService.FileExistsAsync(filePath);
                using var package = new ExcelPackage();

                if (needsCreation)
                {
                    CreateWorksheet(package, sheetName, headers);
                    await SaveExcelAsync(package, filePath);
                    _logger.LogInformation("Created new Excel file: {FilePath}", filePath);
                }
                else
                {
                    await ValidateExcelStructureAsync(filePath, package);
                }

                _initializedFiles[filePath] = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
                throw new InvalidOperationException($"Failed to process {fileName}", ex);
            }
        }

        private void CreateWorksheet(ExcelPackage package, string sheetName, string[] headers)
        {
            var ws = package.Workbook.Worksheets.Add(sheetName);
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cells[1, i + 1].Value = headers[i];
            }

            using (var range = ws.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            }
        }

        private async Task ValidateExcelStructureAsync(string filePath, ExcelPackage package)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await package.LoadAsync(stream);

                if (package.Workbook.Worksheets.Count == 0)
                {
                    _logger.LogWarning("Empty workbook detected: {FilePath}", filePath);
                    throw new InvalidDataException("Workbook contains no worksheets");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "File validation failed: {FilePath}", filePath);
                throw new InvalidOperationException($"Corrupted Excel file: {Path.GetFileName(filePath)}", ex);
            }
        }

        public async Task RepairCorruptedFileAsync(string fileName)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExcelBootstrapperService));
            if (!FileTemplates.TryGetValue(fileName, out var template))
                throw new ArgumentException($"Unknown file: {fileName}", nameof(fileName));

            string filePath = Path.Combine(_appConfig.TemplateFolder, fileName);
            filePath = _fileService.SanitizePath(filePath);

            try
            {
                _logger.LogWarning("Attempting to repair file: {FilePath}", filePath);

                // Backup file corrupt
                if (await _fileService.FileExistsAsync(filePath))
                {
                    string backupPath = $"{filePath}.backup_{DateTime.Now:yyyyMMddHHmmss}";
                    await _fileService.CopyFileAsync(filePath, backupPath, true);
                    await _fileService.DeleteFileAsync(filePath);
                    _logger.LogInformation("Backed up corrupted file to: {BackupPath}", backupPath);
                }

                // Buat ulang file
                _initializedFiles.TryRemove(filePath, out _);
                await EnsureFileExistsAsync(fileName, template.SheetName, template.Headers);

                _logger.LogInformation("Successfully repaired file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair file: {FilePath}", filePath);
                throw new InvalidOperationException($"Repair failed for {fileName}", ex);
            }
        }

        private async Task SaveExcelAsync(ExcelPackage package, string filePath)
        {
            try
            {
                using var stream = new MemoryStream();
                await package.SaveAsAsync(stream);
                await _fileService.SaveBytesAsync(stream.ToArray(), filePath); // Gunakan FileService untuk save
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Excel file: {FilePath}", filePath);
                throw new InvalidOperationException($"Failed to save {Path.GetFileName(filePath)}", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _initializedFiles.Clear();
            _disposed = true;
            _logger.LogInformation("ExcelBootstrapperService disposed");
        }

        public async Task<int?> GetLastSuratIdFromExcelAsync(string sheetName)
        {
            string fileName = "WargaBantu.xlsx";
            string filePath = Path.Combine(_appConfig.TemplateFolder, fileName);

            try
            {
                if (!await _fileService.FileExistsAsync(filePath))
                    return null;

                using var package = new ExcelPackage();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await package.LoadAsync(stream);

                var worksheet = package.Workbook.Worksheets[sheetName];
                if (worksheet == null || worksheet.Dimension == null)
                    return null;

                int lastRow = worksheet.Dimension.Rows;
                if (lastRow <= 1) // Hanya header
                    return null;

                // Ambil nomor surat terakhir (format: "BEDA-123")
                string lastSuratNumber = worksheet.Cells[lastRow, 1].Text;
                if (int.TryParse(lastSuratNumber.Split('-').Last(), out int lastId))
                {
                    _logger.LogInformation("Found last SuratId {LastId} from Excel", lastId);
                    return lastId;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read last SuratId from Excel");
                return null;
            }
        }
        public async Task<WargaData> GetWargaFromExcelAsync(string nik, string templateName)
        {
            // Validasi parameter
            if (string.IsNullOrWhiteSpace(nik) || nik.Length != 16 || !nik.All(char.IsDigit))
            {
                _logger.LogWarning("NIK tidak valid: {NIK}. Harus 16 digit numerik.", nik);
                return null!;
            }

            if (!templateName.Equals("BEDANAMA", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Template tidak dikenali: {TemplateName}", templateName);
                throw new ArgumentException($"Template '{templateName}' tidak valid.");
            }

            // Gunakan template yang sudah didefinisikan di FileTemplates
            if (!FileTemplates.TryGetValue("WargaBantu.xlsx", out var template))
            {
                _logger.LogError("Template 'WargaBantu.xlsx' tidak ditemukan.");
                throw new InvalidOperationException("Template tidak terdaftar.");
            }

            string filePath = Path.Combine(_appConfig.TemplateFolder, "WargaBantu.xlsx");
            filePath = _fileService.SanitizePath(filePath);

            try
            {
                if (!await _fileService.FileExistsAsync(filePath))
                {
                    _logger.LogWarning("File Excel tidak ditemukan: {FilePath}", filePath);
                    return null!;
                }

                using var package = new ExcelPackage();
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await package.LoadAsync(stream);
                }

                var worksheet = package.Workbook.Worksheets[template.SheetName];
                if (worksheet?.Dimension == null)
                {
                    _logger.LogWarning("Worksheet '{SheetName}' tidak ditemukan atau kosong.", template.SheetName);
                    return null!;
                }

                // Mapping kolom berdasarkan header
                var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                {
                    string? header = worksheet.Cells[1, col]?.Text?.Trim();
                    if (!string.IsNullOrEmpty(header))
                    {
                        columnMap[header] = col;
                    }
                }

                // Pastikan kolom wajib ada
                if (!columnMap.TryGetValue("NIK", out int nikColumn) || !columnMap.TryGetValue("NAMA", out int namaColumn))
                {
                    _logger.LogError("Kolom NIK/NAMA tidak ditemukan di worksheet.");
                    return null!;
                }

                // Cari data berdasarkan NIK
                for (int row = 2; row <= worksheet.Dimension.Rows; row++)
                {
                    string? currentNik = worksheet.Cells[row, nikColumn]?.Text?.Trim();
                    if (currentNik?.Equals(nik, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.LogInformation("Data ditemukan untuk NIK: {NIK} di baris {Row}", nik, row);
                        return new WargaData
                        {
                            NIK = currentNik,
                            Nama = worksheet.Cells[row, namaColumn]?.Text?.Trim() ?? string.Empty,
                            TempatLahir = columnMap.TryGetValue("TEMPAT LAHIR", out int tempatLahirCol)
                                ? worksheet.Cells[row, tempatLahirCol]?.Text?.Trim() ?? string.Empty
                                : string.Empty,
                            TanggalLahir = columnMap.TryGetValue("TANGGAL LAHIR", out int tglLahirCol)
                                ? worksheet.Cells[row, tglLahirCol]?.Text?.Trim() ?? string.Empty
                                : string.Empty,
                            JenisKelamin = columnMap.TryGetValue("JENIS KELAMIN", out int jkCol)
                                ? worksheet.Cells[row, jkCol]?.Text?.Trim() ?? string.Empty
                                : string.Empty,
                            AlamatLengkap = columnMap.TryGetValue("ALAMAT", out int alamatCol)
                                ? worksheet.Cells[row, alamatCol]?.Text?.Trim() ?? string.Empty
                                : string.Empty
                        };
                    }
                }

                _logger.LogInformation("Data tidak ditemukan untuk NIK: {NIK}", nik);
                return null!;
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "File Excel tidak ditemukan: {FilePath}", filePath);
                return null!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca data dari Excel untuk NIK: {NIK}", nik);
                throw new InvalidOperationException($"Gagal membaca data Excel: {ex.Message}", ex);
            }
        }
    }
}
