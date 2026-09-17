using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SuDesApp.Data.Queries
{
    public class QueryInterceptor
    {
        private readonly ILogger<QueryInterceptor> _logger;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _fastQueryTimeout = TimeSpan.FromSeconds(5);

        public QueryInterceptor(ILogger<QueryInterceptor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteWithLogging<T>(Func<Task<T>> queryFunc, string queryName, object parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var timeout = queryName.Contains("GetAll") || queryName.Contains("Report") ? _defaultTimeout : _fastQueryTimeout;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var result = await queryFunc().WaitAsync(cts.Token);

                stopwatch.Stop();

                if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
                {
                    _logger.LogWarning("Slow query detected: {QueryName} took {ElapsedMs}ms, Parameters: {Parameters}",
                        queryName, stopwatch.ElapsedMilliseconds, parameters);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogError("Query {QueryName} timed out after {ElapsedMs}ms, Parameters: {Parameters}",
                    queryName, stopwatch.ElapsedMilliseconds, parameters);
                throw new TimeoutException($"Query {queryName} timed out after {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error executing query {QueryName}: {Message}, Parameters: {Parameters}",
                    queryName, ex.Message, parameters);
                throw;
            }
            finally
            {
                _logger.LogDebug("Query {QueryName} executed in {ElapsedMs}ms", queryName, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
