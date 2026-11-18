using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Tests.Configuration;

/// <summary>
/// Paket mode lisensi lokal WAJIB membawa server lisensi lokal. Aplikasi
/// terpasang tidak pernah menjalankan server dari repositori: ia mencari
/// <c>&lt;folder aplikasi&gt;\server\SuDesApp.Server.exe</c>
/// (<c>ServerLisensiLokal.CariTarget</c>). Selama folder itu tidak ikut
/// terpasang, instalasi baru menampilkan "lisensi server tidak terhubung"
/// tanpa sebab yang terlihat — server memang tidak ada di paket.
///
/// <para>Tes ini mengunci rantai pengemasan: skrip publish menerbitkan
/// <c>SuDesApp.Server</c> ke <c>artifacts/publish/server</c>, menyalin kunci
/// penandatangan yang sama dengan <c>Lisensi:KunciPublik</c> (kunci berbeda
/// membuat setiap token ditolak), dan installer tidak mengecualikan folder
/// <c>server</c> dari entry recursesubdirs.</para>
/// </summary>
public sealed class PaketServerLisensiTests
{
    private static readonly string AkarRepo = CariAkarRepo();
    private static readonly string PublishScript =
        Path.Combine(AkarRepo, "Tools", "publish-release.ps1");
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

    private static string Baca(params string[] bagian) => File.ReadAllText(Path.Combine(new[] { AkarRepo }.Concat(bagian).ToArray()));

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

    [Fact]
    public void Publish_release_menerbitkan_server_lisensi_ke_folder_server()
    {
        var skrip = Baca("Tools", "publish-release.ps1");

        Assert.Contains("SuDesApp.Server.csproj", skrip, StringComparison.Ordinal);
        Assert.Contains("Join-Path $publishDir 'server'", skrip, StringComparison.Ordinal);
        Assert.Contains("-o $serverDir", skrip, StringComparison.Ordinal);
        // Server dibutuhkan aplikasi untuk melayani lisensi; publish yang gagal
        // tidak boleh diam-diam melanjutkan paket tanpa server.
        Assert.Contains("throw \"SuDesApp.Server.exe tidak ditemukan", skrip, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_release_menyalin_kunci_penandatangan_yang_sama()
    {
        var skrip = Baca("Tools", "publish-release.ps1");

        Assert.Contains("SuDesApp.Server/keys/lisensi-signing.key", skrip, StringComparison.Ordinal);
        Assert.Contains("lisensi-signing.key", skrip, StringComparison.Ordinal);
        // LisensiManager menolak menimpa kunci tertanam dengan kunci yang
        // disajikan server, jadi kunci wajib hadir — bukan opsional.
        Assert.Contains("throw ('Publish dibatalkan: SuDesApp.Server/keys/lisensi-signing.key tidak ditemukan.",
            skrip, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_tidak_mengecualikan_folder_server()
    {
        // Entry recursesubdirs pada [Files] mengemas seluruh isi artifacts/publish,
        // jadi server ikut selama tidak ada pola Excludes yang menelannya.
        foreach (var pola in PolaPengecualian())
        {
            Assert.False(
                string.Equals(pola, "server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pola, "server/*", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pola, "server/**", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pola, "server\\**", StringComparison.OrdinalIgnoreCase),
                $"'{pola}' mengecualikan folder server lisensi — aplikasi terpasang tidak akan menemukan servernya.");
        }
    }

    [Fact]
    public void Installer_mengemas_seluruh_isi_publish_secara_rekursif()
    {
        var teks = File.ReadAllText(Iss);
        var mulai = teks.IndexOf("[Files]", StringComparison.Ordinal);
        Assert.True(mulai >= 0, "[Files] tidak ditemukan di SuDesApp.iss");

        var potong = teks.Substring(mulai);
        Assert.Contains("Source: \"..\\artifacts\\publish\\*\"", potong, StringComparison.Ordinal);
        Assert.Contains("recursesubdirs", potong, StringComparison.Ordinal);
    }
}
