using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace SuDesApp.Data.Queries
{
    public class QueryProvider
    {
        private readonly Dictionary<string, string> _queries;
        private readonly string _filePath;
        private readonly IOptions<QueryProviderOptions>? _options;
        private readonly ILogger<QueryProvider> _logger;

        public QueryProvider(string filePath, ILogger<QueryProvider>? logger = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _logger = logger!;
            _queries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadQueries();
        }

        public QueryProvider(IOptions<QueryProviderOptions> options, ILogger<QueryProvider>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger!;
            _queries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Use the file path from options
            var configuredPath = _options.Value.SuratQueryFilePath;
            // Jika relatif, resolve terhadap folder output (AppContext.BaseDirectory)
            // sehingga berfungsi baik saat aplikasi dijalankan dari build output
            // maupun via `dotnet run` (working directory berbeda).
            _filePath = string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : Path.IsPathRooted(configuredPath) ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                throw new InvalidOperationException("SuratQueryFilePath not configured in QueryProviderOptions");
            }

            LoadQueries();
        }

        public string GetQuery(string queryName)
        {
            if (string.IsNullOrWhiteSpace(queryName))
                throw new ArgumentException("Query name cannot be empty.", nameof(queryName));

            if (_queries == null)
                throw new InvalidOperationException("Queries have not been initialized.");

            if (_queries.TryGetValue(queryName, out var query))
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    throw new InvalidOperationException($"Query '{queryName}' found but empty. Check your query file.");
                }
                return query;
            }

            var availableQueries = _queries.Keys.Any() ? string.Join(", ", _queries.Keys) : "No queries loaded";
            throw new KeyNotFoundException($"Query '{queryName}' not found. Available queries: {availableQueries}");
        }

        private void LoadQueries()
        {
            if (string.IsNullOrWhiteSpace(_filePath))
                throw new InvalidOperationException("Query file path not specified.");

            if (!File.Exists(_filePath))
                throw new FileNotFoundException($"Query file '{Path.GetFullPath(_filePath)}' not found.");

            var lines = File.ReadAllLines(_filePath);
            var currentQueryName = string.Empty;
            var currentQuery = new StringBuilder();

            _logger?.LogDebug("Loading queries from file: {FilePath}", Path.GetFullPath(_filePath));

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("--"))
                {
                    if (!string.IsNullOrEmpty(currentQueryName))
                    {
                        var queryText = CleanQuery(currentQuery.ToString().Trim());
                        ValidateQuery(currentQueryName, queryText);
                        _queries[currentQueryName] = queryText;
                        _logger?.LogDebug("Loaded query '{QueryName}': {QueryText}", currentQueryName, queryText);
                        currentQuery.Clear();
                    }
                    var parts = trimmedLine.TrimStart('-').Trim().Split(':');
                    if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                    {
                        _logger?.LogError("Invalid query name format in line: {Line}", line);
                        throw new FormatException($"Invalid query name format in line: {line}");
                    }
                    currentQueryName = parts[0].Trim();
                }
                else
                {
                    currentQuery.AppendLine(line);
                }
            }

            if (!string.IsNullOrEmpty(currentQueryName))
            {
                var queryText = CleanQuery(currentQuery.ToString().Trim());
                ValidateQuery(currentQueryName, queryText);
                _queries[currentQueryName] = queryText;
                _logger?.LogDebug("Loaded query '{QueryName}': {QueryText}", currentQueryName, queryText);
            }
        }

        private string CleanQuery(string query)
        {
            var lines = query.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanedLines = lines.Select(line =>
            {
                var commentIndex = line.IndexOf("--");
                return commentIndex >= 0 ? line.Substring(0, commentIndex).TrimEnd() : line;
            }).Where(line => !string.IsNullOrWhiteSpace(line));
            return string.Join(" ", cleanedLines).Trim();
        }

        private void ValidateQuery(string queryName, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException($"Query '{queryName}' is empty.");

            if (query.Contains("@"))
            {
                var hasValidClause = query.Contains("WHERE") || query.Contains("VALUES") || query.Contains("SET");
                if (!hasValidClause)
                    throw new InvalidOperationException($"Query '{queryName}' contains parameters but no WHERE, VALUES, or SET clause.");
            }
        }
        public class QueryProviderOptions
        {
            public string SuratQueryFilePath { get; set; } = "Data/Queries/SuratQueries.sql";
            //public string WargaQueryFilePath { get; set; } = "Queries/v1/WargaQueries.sql";
            //public string DesaQueryFilePath { get; set; } = "Queries/v1/DesaQueries.sql";
            //public string JenisSuratQueryFilePath { get; set; } = "Queries/v1/JenisSuratQueries.sql";
        }
    }
}
