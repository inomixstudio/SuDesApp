using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.Configuration;

/// <summary>
/// Installer WAJIB membawa <c>desa.db.sql</c>. Berkas itu satu-satunya sumber
/// skema yang dibaca <c>DatabaseInitializer.ExecuteDatabaseSchemaAsync</c> untuk
/// membuat seluruh tabel; kalau tidak ada di folder aplikasi, instalasi baru
/// hanya punya tabel yang dibuat kode (Warga, JenisSurat, InfoDesa, ...) dan
/// startup melapor "no such table: main.Surat" / "no such table: IjinTinggal".
///
/// <para>Gejala ini pernah nyata: pengecualian Inno <c>desa.db*</c> — yang
/// maksudnya menahan berkas basis data pengguna agar tidak tertimpa paket —
/// juga menelan <c>desa.db.sql</c> karena pola itu cocok dengan nama apa pun
/// yang diawali "desa.db". Tes ini mengunci dua arah: skema harus ikut
/// terpasang, database pengguna harus tetap tidak ikut terpasang.</para>
/// </summary>
public sealed class InstallerSkemaDatabaseTests
{
    private static readonly string AkarRepo = CariAkarRepo();
    private static readonly string Iss = Path.Combine(AkarRepo, "installer", "SuDesApp.iss");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    /// <summary>Semua pola pada baris Excludes pertama di bagian [Files].</summary>
    private static string[] PolaPengecualian()
    {
        var teks = File.ReadAllText(Iss);
        var mulai = teks.IndexOf("[Files]", StringComparison.Ordinal);
        Assert.True(mulai >= 0, "[Files] tidak ditemukan di SuDesApp.iss");

        var potong = teks.Substring(mulai);
        var akhir = potong.IndexOf(Environment.NewLine + "[", StringComparison.Ordinal);
        if (akhir > 0) potong = potong.Substring(0, akhir);

        var excludes = Regex.Match(potong, @"Excludes:\s*""(?<pola>[^""]+)""");
        Assert.True(excludes.Success, "Tidak ada baris Excludes di bagian [Files].");
        return excludes.Groups["pola"].Value.Split(',')
            .Select(p => p.Trim().Replace("\\", "/")).Where(p => p.Length > 0).ToArray();
    }

    /// <summary>Cocokan pola wildcard Inno (case-insensitive, * dan ?) pada NAMA berkas.</summary>
    private static bool Cocok(string pola, string nama)
    {
        // Pola berisi "/" adalah pola jalur (mis. Templates/*), bukan nama berkas.
        if (pola.Contains('/')) return false;
        var rx = "^" + Regex.Escape(pola).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(nama, rx, RegexOptions.IgnoreCase);
    }

    private static bool Dikecualikan(string nama) => PolaPengecualian().Any(p => Cocok(p, nama));

    [Fact]
    public void Skema_desa_db_sql_tidak_kecualian_installer()
    {
        Assert.False(Dikecualikan("desa.db.sql"),
            "desa.db.sql tersaring Excludes installer — instalasi baru tidak akan bisa membuat " +
            "tabel (gejala: 'no such table: main.Surat' saat startup). Pola: " +
            string.Join(", ", PolaPengecualian()));
    }

    [Theory]
    [InlineData("desa.db")]
    [InlineData("desa.db-wal")]
    [InlineData("desa.db-shm")]
    [InlineData("desa.db-journal")]
    [InlineData("desa_backup_2026_11_17.db")]
    public void Berkas_database_pengguna_tetap_kecualian(string nama)
    {
        // Data desa tidak boleh ditimpa isi paket build.
        Assert.True(Dikecualikan(nama),
            $"'{nama}' tidak dikecualikan — installer akan menimpa basis data pengguna. Pola: " +
            string.Join(", ", PolaPengecualian()));
    }

    [Fact]
    public void Proyek_wpf_menyalin_skema_ke_folder_output()
    {
        var csproj = File.ReadAllText(Path.Combine(AkarRepo, "SuDesApp.Wpf", "SuDesApp.Wpf.csproj"));
        Assert.Contains("desa.db.sql", csproj, StringComparison.Ordinal);
        Assert.Matches(@"desa\.db\.sql""[^>]*CopyToOutputDirectory", csproj);
    }

    [Fact]
    public void Berkas_skema_memuat_seluruh_tabel_surat()
    {
        var sql = File.ReadAllText(Path.Combine(AkarRepo, "desa.db.sql"));
        var dibuat = Regex.Matches(sql, @"CREATE TABLE\s+(?:IF NOT EXISTS\s+)?""?(?<t>[A-Za-z_]+)""?",
                RegexOptions.IgnoreCase)
            .Select(m => m.Groups["t"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(dibuat.Count >= 20,
            $"desa.db.sql hanya membangun {dibuat.Count} tabel — periksa apakah isinya terpotong.");

        // Tabel yang selalu ditanya kode saat startup; kalau salah satu hilang,
        // pemakai melihat error "no such table" seperti laporan operator.
        foreach (var tabel in new[]
                 {
                     "Surat", "IjinTinggal", "Garapan", "Kematian", "BedaNama", "KenalLahir",
                     "NTCR", "SKTM", "SKU", "Instansi", "Pindah", "Keramaian", "AhliWaris",
                     "IZIN", "PerangkatDesa", "InfoDesa", "JenisSurat",
                 })
        {
            Assert.Contains(tabel, dibuat);
        }
    }
}
