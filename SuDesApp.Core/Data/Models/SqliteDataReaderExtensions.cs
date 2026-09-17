using Microsoft.Data.Sqlite;

namespace SuDesApp.Data.Models
{
    public static class SqliteDataReaderExtensions
    {
        public static string GetStringSafe(this SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }

        public static int GetInt32Safe(this SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        public static int? GetInt32NullableSafe(this SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        public static decimal? GetDecimalNullableSafe(this SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
        }

        public static DateTime? GetDateTimeSafe(this SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal)) return null;
            string dateString = reader.GetString(ordinal);
            return DateTime.TryParse(dateString, out var date) ? date : null;
        }
    }
}
