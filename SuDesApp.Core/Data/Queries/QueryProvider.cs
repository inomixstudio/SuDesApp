using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace SuDesApp.Data.Queries
{
    public class QueryProvider
    {
        /// <summary>
        /// Baris penanda nama query: <c>-- NamaQuery</c> atau
        /// <c>-- NamaQuery: keterangan</c>. Komentar biasa di dalam query
        /// (mis. <c>-- segmen kedua nomor surat ...</c>) tidak boleh dianggap
        /// penanda — dulu setiap baris <c>--</c> dianggap nama query, sehingga
        /// query yang punya komentar lanjutan tersimpan kosong dan gagal saat
        /// dipakai (halaman Sinkron Android tidak bisa dibuka).
        /// </summary>
        private static readonly Regex PenandaQuery = new(
            @"^--\s*([A-Za-z][A-Za-z0-9_]*)\s*(?::.*)?$",
            RegexOptions.Compiled);

        private readonly Dictionary<string, string> _queries;
        private readonly string _filePath;
        private readonly IOptions<QueryProviderOptions>? _options;
        private readonly ILogger<QueryProvider> _logger;

        public QueryProvider(string filePath, ILogger<QueryProvider>? logger = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryProvider>.Instance;
            _queries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadQueries();
        }

        public QueryProvider(IOptions<QueryProviderOptions> options, ILogger<QueryProvider>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryProvider>.Instance;
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
                if (trimmedLine.StartsWith("--", StringComparison.Ordinal))
                {
                    var penanda = PenandaQuery.Match(trimmedLine);
                    if (!penanda.Success)
                    {
                        // Komentar lanjutan milik query yang sedang dibaca.
                        currentQuery.AppendLine(line);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(currentQueryName))
                    {
                        var queryText = CleanQuery(currentQuery.ToString().Trim());
                        ValidateQuery(currentQueryName, queryText);
                        // Definisi ganda: yang TERAKHIR menang (mis. InsertSurat
                        // didefinisikan dua kali di SuratQueries.sql). Ditolak
                        // berisik kini diganti penanda log supaya tidak diam-diam.
                        if (_queries.ContainsKey(currentQueryName))
                            _logger?.LogWarning("Query '{QueryName}' didefinisikan dua kali — definisi terakhir dipakai.", currentQueryName);
                        _queries[currentQueryName] = queryText;
                        _logger?.LogDebug("Loaded query '{QueryName}': {QueryText}", currentQueryName, queryText);
                        currentQuery.Clear();
                    }
                    currentQueryName = penanda.Groups[1].Value;
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
                if (_queries.ContainsKey(currentQueryName))
                    _logger?.LogWarning("Query '{QueryName}' didefinisikan dua kali — definisi terakhir dipakai.", currentQueryName);
                _queries[currentQueryName] = queryText;
                _logger?.LogDebug("Loaded query '{QueryName}': {QueryText}", currentQueryName, queryText);
            }
        }

        private string CleanQuery(string query)
        {
            // Buang komentar -- hanya di luar literal string ('...'): literal
            // yang mengandung "--" tidak boleh ikut terpotong. Baris tetap
            // dipertahankan pemisah spasi agar literal multi-baris/adjoining
            // tidak rusak.
            var hasil = new StringBuilder();
            var diString = false;
            var i = 0;
            while (i < query.Length)
            {
                var c = query[i];
                if (c == '\'')
                {
                    if (diString && i + 1 < query.Length && query[i + 1] == '\'')
                    {
                        hasil.Append(c).Append(query[i + 1]);
                        i += 2;
                        continue;
                    }
                    diString = !diString;
                    hasil.Append(c);
                    i++;
                    continue;
                }
                if (!diString && c == '-' && i + 1 < query.Length && query[i + 1] == '-')
                {
                    // Lewati sampai akhir baris.
                    while (i < query.Length && query[i] != '\n') i++;
                    continue;
                }
                hasil.Append(c);
                i++;
            }
            var tanpaKomentar = hasil.ToString();
            var baris = tanpaKomentar.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));
            return string.Join(" ", baris).Trim();
        }

        private void ValidateQuery(string queryName, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException($"Query '{queryName}' is empty.");

            if (query.Contains("@"))
            {
                // {WhereClause} adalah placeholder dinamis — dihitung sah.
                var hasValidClause = query.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) >= 0
                    || query.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase) >= 0
                    || query.IndexOf("SET", StringComparison.OrdinalIgnoreCase) >= 0
                    || query.IndexOf("{WhereClause}", StringComparison.OrdinalIgnoreCase) >= 0;
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
