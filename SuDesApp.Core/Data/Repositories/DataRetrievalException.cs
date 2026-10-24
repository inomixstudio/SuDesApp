
namespace SuDesApp.Data.Repositories
{
    [Serializable]
    public class DataRetrievalException : Exception
    {
        public DataRetrievalException()
        {
        }

        public DataRetrievalException(string? message) : base(message)
        {
        }

        public DataRetrievalException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
