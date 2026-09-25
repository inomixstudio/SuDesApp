// IRepository.cs
using Dapper;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using System.Data;

namespace SuDesApp.Data.Repositories
{
    public interface IRepository<T> where T : class
    {
    Task<T?> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetPagedAsync(int pageNumber, int pageSize, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<int> InsertAsync(T entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<int> InsertBatchAsync(IEnumerable<T> entities, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(T entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task<int> CountAsync(IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
}

// BaseRepository.cs
public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly IUnitOfWork _uow;
    protected readonly ILogger _logger;

    protected abstract string TableName { get; }
    protected abstract string IdColumnName { get; }

    public BaseRepository(IUnitOfWork uow, ILogger logger)
    {
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public virtual async Task<T?> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var conn = _uow.Connection;
        var trans = transaction ?? _uow.CurrentTransaction;

        try
        {
            return await conn.QuerySingleOrDefaultAsync<T>(
                $"SELECT * FROM {TableName} WHERE {IdColumnName} = @Id",
                new { Id = id },
                trans,
                commandTimeout: 30);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {Entity} with ID {Id}", TableName, id);
            throw;
        }
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var conn = _uow.Connection;
        var trans = transaction ?? _uow.CurrentTransaction;

        try
        {
            return await conn.QueryAsync<T>(
                $"SELECT * FROM {TableName}",
                transaction: trans,
                commandTimeout: 30);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all {Entity}", TableName);
            throw;
        }
    }

    public abstract Task<IEnumerable<T>> GetPagedAsync(int pageNumber, int pageSize, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    public abstract Task<int> InsertAsync(T entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    public abstract Task<int> InsertBatchAsync(IEnumerable<T> entities, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    public abstract Task<bool> UpdateAsync(T entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    public abstract Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    public abstract Task<int> CountAsync(IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
}
}
