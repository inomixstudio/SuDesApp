using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Mengunci salinan skema untuk Supabase CLI terhadap skema yang dijalankan
/// server.
///
/// <para>Server menjalankan <c>SuDesApp.Server/schema.sql</c> sendiri saat
/// start, sedangkan CLI menerapkan <c>supabase/migrations/</c>. Dua berkas itu
/// DDL-nya harus sama persis: kalau berbeda, "skema sudah benar di
/// database" dan "skema yang dijalankan server" jadi dua kenyataan yang
/// berbeda, dan bug RLS bisa muncul hanya di salah satu jalur.</para>
///
/// <para>Salinan itu dibuat ulang oleh
/// <c>python Tools/sinkron-migrasi-supabase.py</c>; uji ini yang memastikan
/// langkah itu tidak terlupakan.</para>
/// </summary>
public sealed class MigrasiSupabaseTests
{
    private const string Penanda = "-- ==================== AWAL ISI SALINAN ====================";

    /// <summary>Nama berkas migrasi yang ditanam di assembly uji.</summary>
    private const string NamaMigrasi = "20261022000000_skema_lisensi.sql";

    private static string BacaBenda(Assembly asm, string akhiran)
    {
        var nama = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith(akhiran, StringComparison.Ordinal));
        Assert.NotNull(nama);

        using var stream = asm.GetManifestResourceStream(nama!)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Migrasi_cli_sama_persis_dengan_skema_yang_dijalankan_server()
    {
        var skema = BacaBenda(typeof(SuDesApp.Server.SupabaseStore).Assembly, "schema.sql");
        var migrasi = BacaBenda(typeof(MigrasiSupabaseTests).Assembly, NamaMigrasi);

        Assert.Contains(Penanda, migrasi, StringComparison.Ordinal);

        var isiMigrasi = migrasi[(migrasi.IndexOf(Penanda, StringComparison.Ordinal) + Penanda.Length)..];

        // Samakan gaya baris akhir: Windows dan Linux menulis "\r\n" dan "\n"
        // berbeda, dan itu bukan perbedaan DDL.
        Assert.Equal(
            Normalisasi(skema),
            Normalisasi(isiMigrasi));
    }

    [Fact]
    public void Nama_berkas_migrasi_sesuai_kaidah_cli()
    {
        // CLI hanya menerima berkas "<14 digit>_<nama>.sql" (versi migrasi
        // berurutan). Nama lain diabaikan diam-diam, jadi skema tak pernah
        // ikut terpasang.
        Assert.Matches(new Regex(@"^\d{14}_[A-Za-z0-9\-_]+\.sql$"), NamaMigrasi);
    }

    private static string Normalisasi(string teks)
        => teks.Replace("\r\n", "\n", StringComparison.Ordinal)
               .Replace('\r', '\n')
               .Trim('\n', ' ', '\t');
}
