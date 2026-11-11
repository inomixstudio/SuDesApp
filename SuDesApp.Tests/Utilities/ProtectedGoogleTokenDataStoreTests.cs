using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Menjaga kesetiaan format <see cref="TokenResponseJson"/> terhadap bentuk JSON
/// Newtonsoft bawaan Google.Apis: token lawas harus tetap terbaca setelah migrasi
/// ke System.Text.Json, dan berkas yang ditulis sekarang harus tetap terbaca oleh
/// serializer Newtonsoft (untuk skenario pengguna kembali ke versi aplikasi lama).
/// </summary>
public sealed class ProtectedGoogleTokenDataStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly ProtectedGoogleTokenDataStore _store;

    public ProtectedGoogleTokenDataStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sudesapp-token-google", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _store = new ProtectedGoogleTokenDataStore(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* sisa berkas uji */ }
    }

    private static TokenResponse Contoh() => new()
    {
        AccessToken = "ya29.contoh",
        TokenType = "Bearer",
        RefreshToken = "1//contoh",
        ExpiresInSeconds = 3599,
        IdToken = "eyJ.contoh",
        Scope = "https://www.googleapis.com/auth/drive.file",
        IssuedUtc = new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc),
    };

    private static void Samakan(TokenResponse asli, TokenResponse kembali)
    {
        Assert.Equal(asli.AccessToken, kembali.AccessToken);
        Assert.Equal(asli.TokenType, kembali.TokenType);
        Assert.Equal(asli.RefreshToken, kembali.RefreshToken);
        Assert.Equal(asli.ExpiresInSeconds, kembali.ExpiresInSeconds);
        Assert.Equal(asli.IdToken, kembali.IdToken);
        Assert.Equal(asli.Scope, kembali.Scope);
        Assert.Equal(asli.IssuedUtc, kembali.IssuedUtc);
    }

    [Fact]
    public async Task SimpanDanBaca_UlangBulat_TanpaKehilanganIsi()
    {
        await _store.StoreAsync("kunci", Contoh());
        var kembali = await _store.GetAsync<TokenResponse>("kunci");
        Samakan(Contoh(), kembali);
    }

    [Fact]
    public async Task BerkasTokenNewtonsoft_Lama_TetapTerbaca()
    {
        // Bentuk persis yang ditulis versi lama aplikasi (JsonConvert bawaan
        // Google/Newtonsoft), termasuk "expireTime":null dan "Issued".
        string jsonLama = JsonConvert.SerializeObject(Contoh());
        Assert.Contains("\"expireTime\":null", jsonLama);

        var legacy = new FileDataStore(_folder, fullPath: true);
        await legacy.StoreAsync("kunci", Contoh());

        var kembali = await _store.GetAsync<TokenResponse>("kunci");
        Samakan(Contoh(), kembali);
    }

    [Fact]
    public async Task BerkasYangDitulisSekarang_TetapTerbacaNewtonsoft()
    {
        await _store.StoreAsync("kunci", Contoh());

        // Baca ulang dengan cara versi lama: dekripsi lalu JsonConvert.
        string storedKey = FileDataStore.GenerateStoredKey("kunci", typeof(TokenResponse));
        string nama = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(storedKey))) + ".bin";
        byte[] terenkripsi = await File.ReadAllBytesAsync(
            Path.Combine(_folder, "protected", nama));
        byte[] jernih = System.Security.Cryptography.ProtectedData.Unprotect(
            terenkripsi, Encoding.UTF8.GetBytes("SuDesApp.GoogleOAuthToken.v1"),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);

        var kembali = JsonConvert.DeserializeObject<TokenResponse>(Encoding.UTF8.GetString(jernih))!;
        Samakan(Contoh(), kembali);
    }

    [Fact]
    public async Task TokenRusak_DibuangDanMintaLoginUlang()
    {
        // Tulis blob "terenkripsi" yang isinya bukan JSON valid.
        string storedKey = FileDataStore.GenerateStoredKey("kunci", typeof(TokenResponse));
        string nama = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(storedKey))) + ".bin";
        string path = Path.Combine(_folder, "protected", nama);
        byte[] acak = System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes("bukan json"), Encoding.UTF8.GetBytes("SuDesApp.GoogleOAuthToken.v1"),
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, acak);

        var kembali = await _store.GetAsync<TokenResponse>("kunci");
        Assert.Null(kembali);
        Assert.False(File.Exists(path), "Berkas token rusak harus dihapus agar pengguna login ulang.");
    }

    [Fact]
    public void Konverter_MenulisNamaJsonPropertyGoogle()
    {
        // Guard bentuk kontrak: nama kunci JSON harus persis milik Google
        // ("access_token", "expires_in", ...), bukan PascalCase hasil default STJ.
        string json = System.Text.Json.JsonSerializer.Serialize(Contoh(), new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new TokenResponseJson() },
        });
        foreach (string kunci in new[] { "access_token", "token_type", "expires_in", "refresh_token", "scope", "id_token", "IssuedUtc" })
        {
            Assert.Contains($"\"{kunci}\":", json);
        }
    }
}
