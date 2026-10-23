using System.Collections;

namespace SuDesApp.Server;

/// <summary>
/// Pemetaan environment variable operator admin yang didokumentasikan
/// (<c>SUDES_ADMIN_OPERATORS__&lt;indeks&gt;__*</c>) ke kunci konfigurasi yang
/// dibaca <see cref="AdminOperatorRegistry"/> (<c>Lisensi:AdminOperators:&lt;indeks&gt;:*</c>).
///
/// Dipisah dari <c>Program.cs</c> supaya bisa diuji tanpa menjalankan server.
/// Tanpa pemetaan ini, provider environment menghasilkan kunci
/// <c>SUDES_ADMIN_OPERATORS:0:ID</c> yang tidak pernah dilihat registry —
/// server tampak sehat tetapi semua login admin ditolak 503.
/// </summary>
public static class OperatorAdminEnvironment
{
    public const string Awalan = "SUDES_ADMIN_OPERATORS__";
    public const string Tujuan = "Lisensi:AdminOperators:";

    public static IEnumerable<KeyValuePair<string, string?>> Petakan(IDictionary environment)
    {
        foreach (DictionaryEntry item in environment)
        {
            string nama = item.Key?.ToString() ?? string.Empty;
            if (!nama.StartsWith(Awalan, StringComparison.OrdinalIgnoreCase)) continue;

            string kunci = Tujuan
                + nama[Awalan.Length..].Replace("__", ":", StringComparison.Ordinal);
            yield return new KeyValuePair<string, string?>(kunci, item.Value?.ToString());
        }
    }
}
