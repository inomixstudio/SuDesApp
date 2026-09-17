using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Repositories;
using System.Data;
using System.Threading.Tasks;

namespace SuDesApp.Configuration
{
    public interface IUnitOfWork : IDisposable
    {
        IDesaRepository DesaRepository { get; }
        IWargaRepository WargaRepository { get; }
        ISuratRepository SuratRepository { get; }
        IJenisSuratRepository JenisSuratRepository { get; }
        IIzinOrtuRepository IzinOrtuRepository { get; }

        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();

        IDbTransaction? CurrentTransaction { get; }
        SqliteConnection Connection { get; }
        bool HasActiveTransaction { get; }
        
        T GetRequiredService<T>() where T : notnull;
    }

    public class UnitOfWork : IUnitOfWork
    {
        private readonly SqliteConnection _connection;
        private SqliteTransaction? _transaction;
        private readonly ILogger<UnitOfWork> _logger;
        private readonly IServiceProvider _serviceProvider;
        private bool _disposed = false;

        // Lazy initialization untuk menghindari circular dependency
        private readonly Lazy<IDesaRepository> _desaRepository;
        private readonly Lazy<IWargaRepository> _wargaRepository;
        private readonly Lazy<ISuratRepository> _suratRepository;
        private readonly Lazy<IJenisSuratRepository> _jenisSuratRepository;
        private readonly Lazy<IIzinOrtuRepository> _izinOrtuRepository;

        public UnitOfWork(
            SqliteConnection connection,
            ILogger<UnitOfWork> logger,
            IServiceProvider serviceProvider)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            // Lazy initialization untuk repositories
            _desaRepository = new Lazy<IDesaRepository>(() =>
                _serviceProvider.GetRequiredService<IDesaRepository>());
            _wargaRepository = new Lazy<IWargaRepository>(() =>
                _serviceProvider.GetRequiredService<IWargaRepository>());
            _suratRepository = new Lazy<ISuratRepository>(() =>
                _serviceProvider.GetRequiredService<ISuratRepository>());
            _jenisSuratRepository = new Lazy<IJenisSuratRepository>(() =>
                _serviceProvider.GetRequiredService<IJenisSuratRepository>());
            _izinOrtuRepository = new Lazy<IIzinOrtuRepository>(() =>
                _serviceProvider.GetRequiredService<IIzinOrtuRepository>());
        }

        public IDesaRepository DesaRepository => _desaRepository.Value;
        public IWargaRepository WargaRepository => _wargaRepository.Value;
        public ISuratRepository SuratRepository => _suratRepository.Value;
        public IJenisSuratRepository JenisSuratRepository => _jenisSuratRepository.Value;
        public IIzinOrtuRepository IzinOrtuRepository => _izinOrtuRepository.Value;

        public T GetRequiredService<T>() where T : notnull
        {
            return _serviceProvider.GetRequiredService<T>();
        }

        public IDbTransaction? CurrentTransaction => _transaction;
        public SqliteConnection Connection => _connection;
        public bool HasActiveTransaction => _transaction != null;

        public async Task BeginTransactionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnitOfWork));

            if (HasActiveTransaction)
            {
                _logger.LogWarning("Attempt to begin transaction when one is already active");
                return;
            }

            try
            {
                await EnsureConnectionOpenAsync();
                _transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
                _logger.LogDebug("Transaction started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to begin database transaction");
                throw new InvalidOperationException($"Failed to begin transaction: {ex.Message}", ex);
            }
        }

        public async Task CommitTransactionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnitOfWork));

            if (!HasActiveTransaction)
            {
                _logger.LogWarning("Attempt to commit transaction when none is active");
                return;
            }

            try
            {
                await _transaction!.CommitAsync();
                _logger.LogDebug("Transaction committed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit database transaction");
                await SafeRollbackAsync();
                throw new InvalidOperationException($"Failed to commit transaction: {ex.Message}", ex);
            }
            finally
            {
                DisposeTransaction();
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnitOfWork));

            if (!HasActiveTransaction)
            {
                _logger.LogWarning("Attempt to rollback transaction when none is active");
                return;
            }

            await SafeRollbackAsync();
        }

        private async Task SafeRollbackAsync()
        {
            try
            {
                if (_transaction != null)
                {
                    await _transaction.RollbackAsync();
                    _logger.LogDebug("Transaction rolled back successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback database transaction");
            }
            finally
            {
                DisposeTransaction();
            }
        }

        /// <summary>
        /// Helper method untuk mengeksekusi operasi dalam transaksi dengan proper error handling
        /// </summary>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
        {
            await BeginTransactionAsync();
            try
            {
                var result = await operation();
                await CommitTransactionAsync();
                return result;
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        /// <summary>
        /// Helper method untuk mengeksekusi operasi dalam transaksi tanpa return value
        /// </summary>
        public async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            await BeginTransactionAsync();
            try
            {
                await operation();
                await CommitTransactionAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        private async Task EnsureConnectionOpenAsync()
        {
            if (_connection.State != ConnectionState.Open)
            {
                try
                {
                    await _connection.OpenAsync();
                    _logger.LogDebug("Database connection opened");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open database connection");
                    throw new InvalidOperationException($"Failed to open database connection: {ex.Message}", ex);
                }
            }
        }

        private void DisposeTransaction()
        {
            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (HasActiveTransaction)
                    {
                        _logger.LogWarning("Disposing UnitOfWork with active transaction - rolling back");
                        SafeRollbackAsync().Wait(TimeSpan.FromSeconds(5));
                    }

                    if (_connection.State == ConnectionState.Open)
                    {
                        _connection.Close();
                        _logger.LogDebug("Database connection closed");
                    }

                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while disposing UnitOfWork");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~UnitOfWork()
        {
            Dispose(false);
        }
    }
}
