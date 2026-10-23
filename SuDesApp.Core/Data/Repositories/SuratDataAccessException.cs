
namespace SuDesApp.Data.Repositories
{
    [Serializable]
    public class SuratDataAccessException : Exception
    {
        public SuratDataAccessException()
        {
        }

        public SuratDataAccessException(string? message) : base(message)
        {
        }

        public SuratDataAccessException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
