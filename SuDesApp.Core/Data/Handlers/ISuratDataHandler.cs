using SuDesApp.Data.Models;
using System.Data;

namespace SuDesApp.Data.Handlers
{
    public interface ISuratDataHandler
    {
        string NamaJenis { get; }
        Task InsertRelatedDataAsync(int idSurat, SuratData suratData, IDbConnection connection, IDbTransaction transaction);
        Task DeleteRelatedDataAsync(int idSurat, IDbConnection connection, IDbTransaction transaction);
        Task LoadRelatedDataAsync(SuratData suratData, IDbConnection connection);
    }
}
