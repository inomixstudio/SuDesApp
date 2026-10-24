using Google.Apis.Util.Store;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities;

/// <summary>
/// Penyimpanan token OAuth Google yang hanya dapat dibuka oleh akun Windows
/// saat ini. Token lama dari <see cref="FileDataStore"/> dipindahkan saat
/// pertama kali dibaca, supaya pembaruan tidak memutus sesi yang sudah ada.
/// </summary>
internal sealed class ProtectedGoogleTokenDataStore : IDataStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SuDesApp.GoogleOAuthToken.v1");

    private readonly string _root;
    private readonly string _protectedRoot;
    private readonly FileDataStore _legacy;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedGoogleTokenDataStore(string root)
    {
        _root = Path.GetFullPath(root);
        _protectedRoot = Path.Combine(_root, "protected");
        Directory.CreateDirectory(_protectedRoot);
        _legacy = new FileDataStore(_root, fullPath: true);
    }

    public async Task StoreAsync<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string json = JsonConvert.SerializeObject(value);
        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
        string path = PathFor<T>(key);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        string path = PathFor<T>(key);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    byte[] encrypted = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    byte[] clear = ProtectedData.Unprotect(
                        encrypted, Entropy, DataProtectionScope.CurrentUser);
                    return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(clear))!;
                }
                catch (Exception ex) when (ex is CryptographicException or JsonException)
                {
                    // Token rusak/tidak dapat dibuka: hapus dan minta login ulang,
                    // bukan membuat pengguna terjebak pada sesi yang tidak valid.
                    File.Delete(path);
                }
            }

            // Migrasi kompatibel dari Google FileDataStore versi lama (JSON
            // plaintext di folder token yang sama).
            T? legacy;
            try
            {
                legacy = await _legacy.GetAsync<T>(key).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // FileDataStore lama menyimpan JSON plaintext. Jika file rusak,
                // buang hanya entri token itu dan lanjutkan ke alur login baru.
                await _legacy.DeleteAsync<T>(key).ConfigureAwait(false);
                return default!;
            }

            if (legacy is not null)
            {
                string json = JsonConvert.SerializeObject(legacy);
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
                await WriteEncryptedAsync(path, encrypted).ConfigureAwait(false);
                await _legacy.DeleteAsync<T>(key).ConfigureAwait(false);
            }

            return legacy!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string path = PathFor<T>(key);
            if (File.Exists(path)) File.Delete(path);
            await _legacy.DeleteAsync<T>(key).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_protectedRoot))
            {
                foreach (string file in Directory.EnumerateFiles(_protectedRoot))
                    File.Delete(file);
            }

            // FileDataStore owns the root files; the encrypted subfolder is
            // kept separate so its ClearAsync implementation cannot remove it.
            foreach (string file in Directory.EnumerateFiles(_root))
                File.Delete(file);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PathFor<T>(string key)
    {
        string storedKey = FileDataStore.GenerateStoredKey(key, typeof(T));
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedKey))) + ".bin";
        return Path.Combine(_protectedRoot, name);
    }

    private static async Task WriteEncryptedAsync(string path, byte[] encrypted)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
