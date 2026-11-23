using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Parsing rilis GitHub untuk auto-update. Jalur ini sebelumnya tidak punya pagar
/// sama sekali: satu perubahan bentuk aset (mis. berkas <c>.exe.sha256</c> ikut
/// terunggah, atau satu rilis lupa menyertakan Setup.exe) bisa membuat pembaruan
/// mengunduh berkas yang salah lalu menjalankannya, dan kegagalannya baru terlihat
/// di komputer desa.
///
/// <para>Yang dijaga: installer = aset <c>.exe</c> pertama (bukan aset pertama apa
/// pun), SHA-256 diambil dari <c>digest</c> aset terpilih, versi dinormalkan dari
/// tag, rilis tanpa <c>.exe</c> tetap terbaca versinya dengan tautan unduhan
/// kosong (pembaruan kecil tidak butuh installer), dan status HTTP yang dijawab
/// server tidak lagi dilaporkan sebagai gangguan koneksi.</para>
/// </summary>
public sealed class UpdateServiceRilisTests
{
    private const string DigestExe = "cd8a345909b7e5c6df0210912c96d1dda5795a7c8336d7257fe725f367699cd4";
    private const string DigestPatch = "564343a5acf688ad0bebeea4d24120b69af56e8b5c38fb7a84a3798062ef889a";

    private static void Berakhiran(string? aktual, string akhiran, string konteks)
    {
        Assert.False(string.IsNullOrEmpty(aktual), $"{konteks}: tautan kosong.");
        Assert.True(aktual!.EndsWith(akhiran, StringComparison.Ordinal),
            $"{konteks}: harus berakhiran '{akhiran}', aktual '{aktual}'.");
    }

    /// <summary>Bentuk nyata rilis v2.15.1: patch.json + APK + Setup.exe + .sha256 + zip portable.</summary>
    private static string Rilis2151(string tag = "v2.15.1") => $$"""
    {
      "tag_name": "{{tag}}",
      "name": "SuDesApp {{tag}}",
      "body": "## Catatan {{tag}}\r\n- perbaikan",
      "assets": [
        {"name":"patch.json","size":344,"digest":"sha256:{{DigestPatch}}",
         "browser_download_url":"https://github.com/x/SuDesApp/releases/download/{{tag}}/patch.json",
         "url":"https://api.github.com/repos/x/SuDesApp/releases/assets/1001"},
        {"name":"SuDesApp-2.15.1.apk","size":4756850,"digest":"sha256:0453ccb826f99770aa8b134fbe70c0e14ddaa6b72289fc30c79664bcc1212c53",
         "browser_download_url":"https://github.com/x/SuDesApp/releases/download/{{tag}}/SuDesApp-2.15.1.apk",
         "url":"https://api.github.com/repos/x/SuDesApp/releases/assets/1002"},
        {"name":"SuDesApp_2.15.1_Setup.exe.sha256","size":92,"digest":"sha256:caec089c2217bc3260472796b868a4629487dfff71f94e16dd997998d4485466",
         "browser_download_url":"https://github.com/x/SuDesApp/releases/download/{{tag}}/SuDesApp_2.15.1_Setup.exe.sha256",
         "url":"https://api.github.com/repos/x/SuDesApp/releases/assets/1003"},
        {"name":"SuDesApp_2.15.1_Setup.exe","size":123176600,"digest":"sha256:{{DigestExe}}",
         "browser_download_url":"https://github.com/x/SuDesApp/releases/download/{{tag}}/SuDesApp_2.15.1_Setup.exe",
         "url":"https://api.github.com/repos/x/SuDesApp/releases/assets/1004"},
        {"name":"SuDesApp_2.15.1_win-x64.zip","size":162030525,"digest":"sha256:8b3d86a93182f6d0ca8f1b0d0be58e0e51e5b0d4a7f2a9d1c0b9e8f7a6d5c4b3",
         "browser_download_url":"https://github.com/x/SuDesApp/releases/download/{{tag}}/SuDesApp_2.15.1_win-x64.zip",
         "url":"https://api.github.com/repos/x/SuDesApp/releases/assets/1005"}
      ]
    }
    """;

    // ------------------------------------------------------------------
    // Pemilihan aset installer
    // ------------------------------------------------------------------

    [Fact]
    public void RilisNyata_MengambilSetupExeBesertaDigestnya()
    {
        var info = UpdateService.ParseGitHubRelease(Rilis2151());

        Assert.NotNull(info);
        Assert.Equal("2.15.1", info!.Version);
        Assert.Equal("SuDesApp_2.15.1_Setup.exe", info.NamaInstaller);
        Berakhiran(info.DownloadUrl, "SuDesApp_2.15.1_Setup.exe", "DownloadUrl");
        Berakhiran(info.GitHubApiAssetUrl, "/assets/1004", "GitHubApiAssetUrl");
        Assert.Equal(DigestExe, info.Sha256);
        Assert.Contains("2.15.1", info.ReleaseNotes, StringComparison.Ordinal);

        // Semua aset tetap terdaftar: pembaruan kecil mencari patch.json di sini,
        // jadi tidak boleh ikut dibuang hanya karena bukan installer.
        Assert.Equal(5, info.DaftarAset.Count);
        Assert.NotNull(info.CariAset("patch.json"));
        Assert.NotNull(info.CariAset("SuDesApp_2.15.1_win-x64.zip"));
    }

    [Fact]
    public void AsetSha256DiDepanTidakPernahDijadikanInstaller()
    {
        // Urutan aset tidak dijamin GitHub. Nama ".exe.sha256" mengandung ".exe"
        // di tengah, jadi penyaringan harus melihat akhiran, bukan "memuat".
        var json = """
        {
          "tag_name": "v2.16.0",
          "assets": [
            {"name":"SuDesApp_2.16.0_Setup.exe.sha256","size":92,
             "browser_download_url":"https://github.com/x/y/releases/download/v2.16.0/SuDesApp_2.16.0_Setup.exe.sha256"},
            {"name":"SuDesApp_2.16.0_Setup.exe","size":120000000,"digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "browser_download_url":"https://github.com/x/y/releases/download/v2.16.0/SuDesApp_2.16.0_Setup.exe"}
          ]
        }
        """;

        var info = UpdateService.ParseGitHubRelease(json);

        Assert.Equal("SuDesApp_2.16.0_Setup.exe", info!.NamaInstaller);
        Assert.Equal(new string('a', 64), info.Sha256);
        Berakhiran(info.DownloadUrl, "SuDesApp_2.16.0_Setup.exe", "DownloadUrl");
    }

    [Fact]
    public void EkstensiExeHurufBesarTetapDipilih()
    {
        var json = """
        {
          "tag_name": "v2.16.1",
          "assets": [
            {"name":"SuDesApp_2.16.1_WIN-X64.ZIP","size":10,
             "browser_download_url":"https://github.com/x/y/releases/download/v2.16.1/a.zip"},
            {"name":"SuDesApp_2.16.1_SETUP.EXE","size":20,"digest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
             "browser_download_url":"https://github.com/x/y/releases/download/v2.16.1/a.EXE"}
          ]
        }
        """;

        var info = UpdateService.ParseGitHubRelease(json);

        Assert.Equal("SuDesApp_2.16.1_SETUP.EXE", info!.NamaInstaller);
    }

    [Fact]
    public void RilisTanpaSetupExe_VersiTetahTerbacaTautanKosong()
    {
        // Jebakan lama: ada fallback "aset pertama apa pun", sehingga rilis tanpa
        // Setup.exe menyodorkan patch.json/zip sebagai installer — ~100 MB berkas
        // salah diunduh lalu dijalankan sebagai program. Yang benar: versi tetap
        // dikabarkan, tautan unduhan kosong, pembaruan kecil tetap bisa dipakai.
        var json = """
        {
          "tag_name": "v2.17.0",
          "assets": [
            {"name":"patch.json","size":344,"digest":"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
             "browser_download_url":"https://github.com/x/y/releases/download/v2.17.0/patch.json"},
            {"name":"patch-2.17.0.zip","size":300000,"digest":"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
             "browser_download_url":"https://github.com/x/y/releases/download/v2.17.0/patch-2.17.0.zip"}
          ]
        }
        """;

        var info = UpdateService.ParseGitHubRelease(json);

        Assert.NotNull(info);
        Assert.Equal("2.17.0", info!.Version);
        Assert.True(string.IsNullOrWhiteSpace(info.DownloadUrl),
            "Rilis tanpa aset .exe tidak boleh menyodorkan berkas lain sebagai installer.");
        Assert.Null(info.NamaInstaller);
        Assert.Null(info.Sha256);

        Assert.NotNull(info.CariAset("patch.json"));
        Assert.NotNull(info.CariAset("patch-2.17.0.zip"));
    }

    [Fact]
    public void SetupExeTanpaTautanUnduhTidakDipilih()
    {
        var json = """
        {
          "tag_name": "v2.17.1",
          "assets": [
            {"name":"SuDesApp_2.17.1_Setup.exe","size":100,"digest":"sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
             "browser_download_url":""}
          ]
        }
        """;

        var info = UpdateService.ParseGitHubRelease(json);

        Assert.NotNull(info);
        Assert.True(string.IsNullOrWhiteSpace(info!.DownloadUrl));
        Assert.True(string.IsNullOrWhiteSpace(info.Sha256),
            "Tanpa tautan, digest tidak boleh dipakai untuk memverifikasi berkas lain.");
        Assert.Empty(info.DaftarAset);
    }

    [Fact]
    public void RilisTanpaAset_TetapNullSepertiSebelumnya()
    {
        // Perilaku lama dipertahankan: tidak ada aset = tidak ada yang bisa dibaca
        // (pemanggil menampilkan "gagal mendapatkan informasi versi").
        Assert.Null(UpdateService.ParseGitHubRelease("""{"tag_name":"v9.9.9","assets":[]}"""));
        Assert.Null(UpdateService.ParseGitHubRelease("""{"tag_name":"v9.9.9"}"""));
        Assert.Null(UpdateService.ParseGitHubRelease(null));
        Assert.Null(UpdateService.ParseGitHubRelease("   "));
        Assert.Null(UpdateService.ParseGitHubRelease("{ ini bukan json"));
    }

    // ------------------------------------------------------------------
    // Versi dan digest
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("v2.15.1", "2.15.1")]
    [InlineData("2.15.1", "2.15.1")]
    [InlineData("V2.15.1-rc1", "2.15.1")]
    [InlineData("release-2.15.1", "2.15.1")]
    public void TagDinormalkanKeVersiYangBisaDibandingkan(string tag, string harapan)
    {
        var info = UpdateService.ParseGitHubRelease(
            $$"""{"tag_name":"{{tag}}","assets":[{"name":"a.exe","browser_download_url":"https://github.com/x/y/releases/download/{{tag}}/a.exe"}]}""");

        Assert.NotNull(info);
        Assert.Equal(harapan, info!.Version);
        // Gerbang versi di PembaruanLatarService dan PembaruanViewModel memakai
        // Version.TryParse — kalau hasil normalisasi tidak bisa di-parse, pembaruan
        // tidak pernah ditawarkan.
        Assert.True(Version.TryParse(info.Version, out _));
    }

    private static string JsonRilisSatuAset(string? digest)
    {
        var potonganDigest = digest is null
            ? ""
            : ",\"digest\":\"" + digest + "\"";
        return "{\"tag_name\":\"v2.15.1\",\"assets\":[{\"name\":\"a.exe\"" + potonganDigest +
               ",\"browser_download_url\":\"https://github.com/x/y/releases/download/v2.15.1/a.exe\"}]}";
    }

    [Theory]
    [InlineData("sha256:" + DigestExe, DigestExe)]
    [InlineData("SHA256:" + DigestExe, DigestExe)]
    [InlineData("  sha256:" + DigestExe + "  ", DigestExe)]
    [InlineData(DigestExe, DigestExe)]
    [InlineData("tidak ada digest", null)]
    [InlineData("sha256:pendek", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DigestHanyaDipakaiBilaSha256Lengkap(string? digest, string? harapan)
    {
        var info = UpdateService.ParseGitHubRelease(JsonRilisSatuAset(digest));

        Assert.NotNull(info);
        Assert.Equal(harapan, info!.Sha256);
    }

    // ------------------------------------------------------------------
    // Pesan galat status: jangan mengaku "koneksi gagal" kalau server menjawab
    // ------------------------------------------------------------------

    [Fact]
    public void Status404_MenyebutRepoTidakDitemukan()
    {
        var pesan = UpdateService.PesanGalatStatusRilis(HttpStatusCode.NotFound, "a/b");

        Assert.NotNull(pesan);
        Assert.Contains("tidak ditemukan", pesan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a/b", pesan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void StatusRateLimit_JelasBukanGangguanJaringan(HttpStatusCode status)
    {
        var pesan = UpdateService.PesanGalatStatusRilis(status, "a/b");

        Assert.NotNull(pesan);
        Assert.Contains("dibatasi", pesan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tidak bisa menghubungi", pesan, StringComparison.Ordinal);
        Assert.Contains("unggu", pesan, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusTokenDitolak_MenyebutKonfigurasiToken()
    {
        var pesan = UpdateService.PesanGalatStatusRilis(HttpStatusCode.Unauthorized, "a/b");

        Assert.NotNull(pesan);
        Assert.Contains("githubPersonalAccessToken", pesan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void StatusServerGithub_PesanSedangGangguan(HttpStatusCode status)
    {
        var pesan = UpdateService.PesanGalatStatusRilis(status, "a/b");

        Assert.NotNull(pesan);
        Assert.Contains("gangguan", pesan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(((int)status).ToString(), pesan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Moved)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public void StatusLainDiserahkanKePerilakuLama(HttpStatusCode status)
    {
        // null = jangan mengubah perilaku untuk status yang tidak dikenali:
        // EnsureSuccessStatusCode masih melempar seperti sebelum perbaikan ini.
        Assert.Null(UpdateService.PesanGalatStatusRilis(status, "a/b"));
    }

    // ------------------------------------------------------------------
    // Verifikasi berkas (kontrak downloader setelah unduhan selesai)
    // ------------------------------------------------------------------

    [Fact]
    public void VerifikasiSha256_BerkasCocokLulusBerkasSalahGagal()
    {
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesapp-sha-uji-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(jalur, new byte[] { 1, 2, 3, 4 });
            string hex;
            using (var sha = SHA256.Create())
            {
                hex = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(jalur))).ToLowerInvariant();
            }

            Assert.True(UpdateService.VerifySha256(jalur, hex));
            Assert.True(UpdateService.VerifySha256(jalur, hex.ToUpperInvariant()));
            Assert.False(UpdateService.VerifySha256(jalur, new string('0', 64)));
            Assert.False(UpdateService.VerifySha256(jalur, "bukan-hex"));
            Assert.False(UpdateService.VerifySha256(jalur + ".tidak-ada", hex));
        }
        finally
        {
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }
}
