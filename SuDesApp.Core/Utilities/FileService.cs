using Microsoft.Extensions.Logging;
using System.Text;

namespace SuDesApp.Utilities
{
    public class FileService
    {
        private readonly ILogger<FileService> _logger;
        private readonly AppConfig _config;

        public FileService(AppConfig config, ILogger<FileService> logger)
        {
            _config = config;
            _logger = logger;
        }

        private async Task WriteFileWithRetryAsync(string path, byte[] data, int retries = 3, int delayMs = 500)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    await File.WriteAllBytesAsync(path, data);
                    _logger.LogInformation("File successfully written: {Path}", path);
                    return;
                }
                catch (IOException ex)
                {
                    if (i == retries - 1)
                    {
                        _logger.LogError(ex, "Failed to write file {Path} after {Retries} attempts", path, retries);
                        throw new IOException($"Failed to write file {path} after {retries} attempts", ex);
                    }
                    _logger.LogWarning(ex, "Retry {Attempt}/{Retries} writing file: {Path}", i + 1, retries, path);
                    await Task.Delay(delayMs);
                }
            }
        }

        public async Task SaveBytesAsync(byte[] data, string destinationPath)
        {
            try
            {
                string path = SanitizePath(destinationPath);
                await EnsureDirectoryExistsAsync(path);
                await WriteFileWithRetryAsync(path, data); // Gunakan method dengan retry
                _logger.LogInformation("Saved {Length} bytes to {Path}", data.Length, path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save bytes to {DestinationPath}", destinationPath);
                throw;
            }
        }

        public string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogError("File path cannot be null or empty.");
                throw new ArgumentException("File path cannot be null or empty.", nameof(path));
            }

            string normalizedPath = path.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);

            // Separate directory and file name
            string directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
            string fileName = Path.GetFileName(normalizedPath);

            // Remove invalid characters from file name
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitizedFileName = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());

            string combinedPath = Path.Combine(directory, sanitizedFileName);

            string fullPath = Path.IsPathRooted(combinedPath)
                ? Path.GetFullPath(combinedPath)
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, combinedPath));

            _logger.LogInformation("Sanitized path: {FullPath}", fullPath);
            return fullPath;
        }

        public bool FileExists(string path)
        {
            try
            {
                string sanitizedPath = SanitizePath(path);

                // Check exact path first
                if (File.Exists(sanitizedPath))
                {
                    _logger.LogDebug("File exists (exact match): {Path}", sanitizedPath);
                    return true;
                }

                string directory = Path.GetDirectoryName(sanitizedPath);
                string fileName = Path.GetFileName(sanitizedPath);

                if (Directory.Exists(directory))
                {
                    // Check all variations
                    var foundFile = Directory.GetFiles(directory)
                        .FirstOrDefault(file =>
                            string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(file), fileName.Replace("_", " "), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(file), fileName.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase));

                    if (foundFile != null)
                    {
                        _logger.LogDebug("File exists (variation match): {FoundFile} for requested {Path}", foundFile, sanitizedPath);
                        return true;
                    }

                    // Log available files for debugging
                    var availableFiles = Directory.GetFiles(directory).Select(Path.GetFileName).ToArray();
                    _logger.LogDebug("File not found. Requested: {FileName}, Available files: {AvailableFiles}",
                        fileName, string.Join(", ", availableFiles));
                }
                else
                {
                    _logger.LogDebug("Directory does not exist: {Directory}", directory);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking file existence: {Path}", path);
                return false;
            }
        }

        public string FindActualFilePath(string path)
        {
            try
            {
                string sanitizedPath = SanitizePath(path);

                // First try exact match
                if (File.Exists(sanitizedPath))
                {
                    _logger.LogDebug("Found exact file match: {Path}", sanitizedPath);
                    return sanitizedPath;
                }

                string directory = Path.GetDirectoryName(sanitizedPath);
                string fileName = Path.GetFileName(sanitizedPath);

                if (Directory.Exists(directory))
                {
                    // Try case-insensitive match and variations
                    var files = Directory.GetFiles(directory);
                    foreach (var file in files)
                    {
                        string currentFile = Path.GetFileName(file);
                        if (string.Equals(currentFile, fileName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentFile, fileName.Replace("_", " "), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentFile, fileName.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Found matching file: {File} for requested {FileName}", file, fileName);
                            return file;
                        }
                    }

                    // Try partial matching for similar filenames (helpful for timestamped files)
                    var baseFileName = Path.GetFileNameWithoutExtension(fileName);
                    var extension = Path.GetExtension(fileName);

                    // Remove timestamp-like suffixes for better matching
                    string cleanBaseName = baseFileName;
                    if (baseFileName.Contains("_"))
                    {
                        var parts = baseFileName.Split('_');
                        if (parts.Length > 1)
                        {
                            // Try matching without the last timestamp part
                            cleanBaseName = string.Join("_", parts.Take(parts.Length - 1));
                        }
                    }

                    var partialMatch = files.FirstOrDefault(file =>
                    {
                        var fileBaseName = Path.GetFileNameWithoutExtension(file);
                        var fileExtension = Path.GetExtension(file);

                        return string.Equals(fileExtension, extension, StringComparison.OrdinalIgnoreCase) &&
                               (fileBaseName.StartsWith(cleanBaseName, StringComparison.OrdinalIgnoreCase) ||
                                fileBaseName.Contains(cleanBaseName, StringComparison.OrdinalIgnoreCase));
                    });

                    if (partialMatch != null)
                    {
                        _logger.LogInformation("Found partial matching file: {File} for requested {FileName}", partialMatch, fileName);
                        return partialMatch;
                    }

                    // Log available files for debugging
                    var availableFiles = files.Select(Path.GetFileName).ToArray();
                    _logger.LogWarning("No matching file found. Requested: {FileName}, Available: {AvailableFiles}",
                        fileName, string.Join(", ", availableFiles));
                }

                _logger.LogWarning("Returning original sanitized path as no match found: {SanitizedPath}", sanitizedPath);
                return sanitizedPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding actual file path: {Path}", path);
                return path;
            }
        }

        public async Task DeleteFileAsync(string path)
        {
            try
            {
                string sanitizedPath = SanitizePath(path);

                // Use FindActualFilePath to get the correct file path
                string actualPath = FindActualFilePath(sanitizedPath);

                if (await FileExistsAsync(actualPath))
                {
                    await Task.Run(() => File.Delete(actualPath));
                    _logger.LogInformation("Successfully deleted file: {ActualPath}", actualPath);
                }
                else
                {
                    _logger.LogWarning("File not found for deletion: {Path} (resolved to: {ActualPath})", path, actualPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file: {Path}", path);
                throw;
            }
        }

        public void EnsureDirectoryExists(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(SanitizePath(path));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created directory: {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory for path: {Path}", path);
                throw;
            }
        }

        public async Task EnsureDirectoryExistsAsync(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(SanitizePath(path));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    await Task.Run(() => Directory.CreateDirectory(directory));
                    _logger.LogInformation("Created directory: {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory for path: {Path}", path);
                throw;
            }
        }

        public async Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
        {
            try
            {
                string src = SanitizePath(sourcePath);
                string dest = SanitizePath(destinationPath);

                // Use FindActualFilePath to find the correct source file
                string actualSourcePath = FindActualFilePath(src);

                _logger.LogDebug("CopyFileAsync: Original source: {OriginalPath}, Resolved source: {ResolvedPath}",
                    src, actualSourcePath);

                if (!File.Exists(actualSourcePath))
                {
                    _logger.LogError("Source file not found: {SourcePath} (resolved to: {ActualPath})",
                        sourcePath, actualSourcePath);
                    throw new FileNotFoundException($"File sumber tidak ditemukan: {sourcePath}", actualSourcePath);
                }

                if (actualSourcePath.Equals(dest, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Source and destination paths are the same: {Path}", actualSourcePath);
                    throw new ArgumentException("Path sumber dan tujuan tidak boleh sama.", nameof(destinationPath));
                }

                await EnsureDirectoryExistsAsync(dest);

                // Add retry mechanism for file copy
                int maxRetries = 3;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        await Task.Run(() => File.Copy(actualSourcePath, dest, overwrite));
                        _logger.LogInformation("Berhasil salin file dari {Src} ke {Dest}", actualSourcePath, dest);
                        return;
                    }
                    catch (IOException ex) when (i < maxRetries - 1)
                    {
                        _logger.LogWarning(ex, "Retry {Attempt}/{MaxRetries} copying file from {Src} to {Dest}",
                            i + 1, maxRetries, actualSourcePath, dest);
                        await Task.Delay(500); // Wait 500ms before retry
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal salin file dari {SourcePath} ke {DestinationPath}", sourcePath, destinationPath);
                throw;
            }
        }

        public async Task SaveFileAsync(string content, string destinationPath, Encoding? encoding = null)
        {
            try
            {
                if (content == null)
                {
                    throw new ArgumentNullException(nameof(content));
                }

                string path = SanitizePath(destinationPath);
                await EnsureDirectoryExistsAsync(path);

                await File.WriteAllTextAsync(path, content, encoding ?? Encoding.UTF8);
                _logger.LogInformation("Saved file to {Path} with {Length} characters", path, content.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file: {DestinationPath}", destinationPath);
                throw;
            }
        }

        public void SaveFile(string content, string destinationPath, Encoding? encoding = null)
        {
            try
            {
                if (content == null)
                {
                    throw new ArgumentNullException(nameof(content));
                }

                string path = SanitizePath(destinationPath);
                EnsureDirectoryExists(path);

                File.WriteAllText(path, content, encoding ?? Encoding.UTF8);
                _logger.LogInformation("Saved file to {Path} with {Length} characters", path, content.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file: {DestinationPath}", destinationPath);
                throw;
            }
        }

        public async Task<string> ReadFileAsync(string path, Encoding? encoding = null)
        {
            try
            {
                string sanitizedPath = SanitizePath(path);
                string actualPath = FindActualFilePath(sanitizedPath);

                if (!File.Exists(actualPath))
                {
                    throw new FileNotFoundException($"File tidak ditemukan: {path}", actualPath);
                }

                string content = await File.ReadAllTextAsync(actualPath, encoding ?? Encoding.UTF8);
                _logger.LogInformation("Read file from {Path} with {Length} characters", actualPath, content.Length);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file: {Path}", path);
                throw;
            }
        }

        public Task<string> ReadFileAsStringAsync(string path, Encoding? encoding = null) => ReadFileAsync(path, encoding);

        public bool IsValidPdf(string filePath)
        {
            try
            {
                string actualPath = FindActualFilePath(filePath);
                if (!File.Exists(actualPath))
                {
                    _logger.LogWarning("PDF validation failed: file not found {FilePath}", filePath);
                    return false;
                }

                byte[] header = new byte[4];
                using (var fs = File.OpenRead(actualPath))
                    fs.Read(header, 0, 4);

                bool isValid = header[0] == 0x25 && header[1] == 0x50 && // %PDF
                              header[2] == 0x44 && header[3] == 0x46;

                _logger.LogDebug("PDF validation for {FilePath}: {IsValid}", actualPath, isValid);
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating PDF: {FilePath}", filePath);
                return false;
            }
        }

        public async Task<bool> FileExistsAsync(string path)
        {
            return await Task.Run(() => FileExists(path));
        }
    }
}
