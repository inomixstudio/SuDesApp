
namespace SuDesApp.Data.Repositories
{
    [Serializable]
    public class SuratNotFoundException : Exception
    {
        public SuratNotFoundException()
        {
        }

        public SuratNotFoundException(string? message) : base(message)
        {
        }

        public SuratNotFoundException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
