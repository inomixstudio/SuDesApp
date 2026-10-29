using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SudesPdf.Core;

/// <summary>
/// Handler keamanan standar PDF (ISO 32000-1 §7.6): RC4 40/128-bit dan AES
/// (V2/V3). Dipakai untuk membuka dokumen terlindungi dengan kata sandi
/// pengguna kosong.
/// </summary>
public sealed class PdfEncryption
{
    private static readonly byte[] Padding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    };

    private readonly byte[] _key;
    private readonly int _version;
    private readonly int _revision;
    private readonly bool _aesStrings;
    private readonly bool _aesStreams;
    private readonly bool _encryptStrings;
    private readonly bool _encryptStreams;

    private PdfEncryption(
        byte[] key, int version, int revision, bool aesStrings, bool aesStreams,
        bool encryptStrings, bool encryptStreams, bool encryptMetadata)
    {
        _key = key;
        _version = version;
        _revision = revision;
        _aesStrings = aesStrings;
        _aesStreams = aesStreams;
        _encryptStrings = encryptStrings;
        _encryptStreams = encryptStreams;
        EncryptMetadata = encryptMetadata;
    }

    public bool EncryptMetadata { get; }

    public int Version => _version;

    public int Revision => _revision;

    public bool IsAes => _aesStrings || _aesStreams || _revision >= 5;

    public static PdfEncryption Create(PdfDictionary dict, PdfDocument document)
    {
        int version = (int)(dict.GetNumber("V") ?? 0);
        int revision = (int)(dict.GetNumber("R") ?? 0);
        var owner = document.Resolve(dict.Raw("O")) as PdfString;
        var user = document.Resolve(dict.Raw("U")) as PdfString;
        int permissions = (int)(dict.GetNumber("P") ?? -1);
        bool encryptMetadata = dict.GetBool("EncryptMetadata") ?? true;
        int lengthBits = (int)(dict.GetNumber("Length") ?? 40);
        if (lengthBits is < 40 or > 256) lengthBits = 40;
        int keyLength = lengthBits / 8;
        byte[] id = document.DocumentId;

        if (version >= 5 || revision >= 5)
        {
            if (user is null) throw new PdfException("Dokumen terproteksi tanpa kunci /U.");
            var ue = document.Resolve(dict.Raw("UE")) as PdfString;
            byte[] key = DeriveAes256Key(revision, user.Bytes, ue?.Bytes);
            return new PdfEncryption(key, version, revision, true, true, true, true, encryptMetadata);
        }

        if (owner is null || user is null)
        {
            throw new PdfException("Kamus /Encrypt tidak lengkap (kunci /O atau /U hilang).");
        }

        byte[] fileKey = DeriveStandardKey(revision, keyLength, owner.Bytes, permissions, id, encryptMetadata);
        if (!ValidateStandardKey(revision, fileKey, owner.Bytes, user.Bytes, id))
        {
            throw new PdfPasswordException(
                "Dokumen PDF ini terkunci kata sandi. Buka proteksinya lebih dulu, lalu coba lagi.");
        }

        ReadCryptFilters(
            document, version, out bool aesStrings, out bool aesStreams,
            out bool encryptStrings, out bool encryptStreams);

        return new PdfEncryption(
            fileKey, version, revision, aesStrings, aesStreams, encryptStrings, encryptStreams, encryptMetadata);
    }

    private static void ReadCryptFilters(
        PdfDocument document, int version, out bool aesStrings, out bool aesStreams,
        out bool encryptStrings, out bool encryptStreams)
    {
        aesStrings = false;
        aesStreams = false;
        encryptStrings = true;
        encryptStreams = true;
        if (version < 4) return;

        var encrypt = document.ResolveDictionary(document.Trailer.Raw("Encrypt"));
        if (encrypt is null) return;

        var cf = document.ResolveDictionary(encrypt.Raw("CF"));
        string? stringsName = (document.Resolve(encrypt.Raw("StrF")) as PdfName)?.Value;
        string? streamsName = (document.Resolve(encrypt.Raw("StmF")) as PdfName)?.Value;
        if (stringsName == "Identity") encryptStrings = false;
        if (streamsName == "Identity") encryptStreams = false;

        aesStrings = IsAesFilter(document, cf, stringsName) || IsAesFilter(document, cf, "StdCF");
        aesStreams = IsAesFilter(document, cf, streamsName) || IsAesFilter(document, cf, "StdCF");
    }

    private static bool IsAesFilter(PdfDocument document, PdfDictionary? cf, string? name)
    {
        if (cf is null || string.IsNullOrEmpty(name)) return false;
        var filter = document.ResolveDictionary(cf.Raw(name!)) ?? document.ResolveDictionary(cf.Raw("StdCF"));
        string? method = filter?.GetNameValue("CFM");
        return method is "AESV2" or "AESV3";
    }

    private static byte[] DeriveStandardKey(
        int revision, int keyLength, byte[] owner, int permissions, byte[] id, bool encryptMetadata)
    {
        byte[] padded = PadPassword(Array.Empty<byte>());
        var input = new List<byte>(padded.Length + owner.Length + 8);
        input.AddRange(padded);
        input.AddRange(owner);
        input.AddRange(BitConverter.GetBytes(permissions));
        input.AddRange(id);
        if (revision >= 4 && !encryptMetadata)
        {
            input.Add(0xFF);
            input.Add(0xFF);
            input.Add(0xFF);
            input.Add(0xFF);
        }

        byte[] hash = MD5.HashData(input.ToArray());
        if (revision < 3) return hash[..Math.Min(keyLength, hash.Length)];

        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash[..keyLength]);
        }

        return hash[..keyLength];
    }

    private static bool ValidateStandardKey(
        int revision, byte[] key, byte[] owner, byte[] user, byte[] id)
    {
        if (revision == 2)
        {
            byte[] expected = Rc4(key, PadPassword(Array.Empty<byte>()));
            return Equal(expected, user);
        }

        byte[] data = new byte[Padding.Length + id.Length];
        Array.Copy(Padding, data, Padding.Length);
        Array.Copy(id, 0, data, Padding.Length, id.Length);
        byte[] value = MD5.HashData(data);
        byte[] result = Rc4(key, value);
        for (int i = 1; i <= 19; i++)
        {
            byte[] derived = new byte[key.Length];
            for (int j = 0; j < key.Length; j++) derived[j] = (byte)(key[j] ^ i);
            result = Rc4(derived, result);
        }

        int compare = Math.Min(16, user.Length);
        if (compare == 0) return false;
        for (int i = 0; i < compare; i++)
        {
            if (result[i] != user[i]) return false;
        }

        return true;
    }

    private static byte[] DeriveAes256Key(int revision, byte[] user, byte[]? userEncrypted)
    {
        if (user.Length < 48 || userEncrypted is null)
        {
            throw new PdfException("Kunci /U dokumen AES-256 tidak lengkap.");
        }

        byte[] validationSalt = user[32..40];
        byte[] keySalt = user[40..48];
        byte[] expected = revision == 5
            ? SHA256.HashData(Concat(Array.Empty<byte>(), validationSalt))
            : HardenedHash(Array.Empty<byte>(), validationSalt);
        if (!Equal(expected, user[..32]))
        {
            throw new PdfPasswordException(
                "Dokumen PDF ini terkunci kata sandi. Buka proteksinya lebih dulu, lalu coba lagi.");
        }

        byte[] intermediate = revision == 5
            ? SHA256.HashData(Concat(Array.Empty<byte>(), keySalt))
            : HardenedHash(Array.Empty<byte>(), keySalt);
        return AesCbcDecrypt(intermediate[..32], new byte[16], userEncrypted, stripPadding: false);
    }

    /// <summary>Hash mengeras revisi 6 (ISO 32000-2, algoritma 2.B).</summary>
    private static byte[] HardenedHash(byte[] password, byte[] salt)
    {
        byte[] hash = SHA256.HashData(Concat(password, salt));
        byte[] encrypted = hash;
        for (int round = 0; round < 64 || encrypted[^1] > round - 32; round++)
        {
            int unit = password.Length + hash.Length;
            byte[] block = new byte[unit * 64];
            int position = 0;
            for (int i = 0; i < 64; i++)
            {
                Array.Copy(password, 0, block, position, password.Length);
                position += password.Length;
                Array.Copy(hash, 0, block, position, hash.Length);
                position += hash.Length;
            }

            byte[] key = hash[..16];
            byte[] iv = hash[16..32];
            encrypted = AesCbcEncrypt(key, iv, block);
            ulong sum = 0;
            for (int i = 0; i < 16 && i < encrypted.Length; i++) sum += (ulong)encrypted[i];
            hash = (sum % 3) switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted)
            };
        }

        return hash;
    }

    private static byte[] PadPassword(byte[] password)
    {
        var padded = new byte[32];
        int count = Math.Min(password.Length, 32);
        Array.Copy(password, padded, count);
        Array.Copy(Padding, 0, padded, count, 32 - count);
        return padded;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Array.Copy(a, result, a.Length);
        Array.Copy(b, 0, result, a.Length, b.Length);
        return result;
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    // --------------------------------------------------------- Dekripsi objek

    public byte[] DecryptStream(byte[] data, int objectNumber, int generation)
    {
        if (!_encryptStreams) return data;
        return Decrypt(data, objectNumber, generation, _aesStreams);
    }

    public byte[] DecryptString(byte[] data, int objectNumber, int generation)
    {
        if (!_encryptStrings) return data;
        return Decrypt(data, objectNumber, generation, _aesStrings);
    }

    private byte[] Decrypt(byte[] data, int objectNumber, int generation, bool aes)
    {
        if (data.Length == 0) return data;
        if (_revision >= 5)
        {
            return aes ? AesCbcDecrypt(_key, data.AsSpan(0, 16).ToArray(), data[16..], stripPadding: true) : data;
        }

        byte[] key = ObjectKey(objectNumber, generation);
        if (!aes) return Rc4(key, data);

        if (data.Length < 16) return data;
        byte[] iv = data[..16];
        return AesCbcDecrypt(key, iv, data[16..], stripPadding: true);
    }

    private byte[] ObjectKey(int objectNumber, int generation)
    {
        var input = new byte[_key.Length + 5];
        Array.Copy(_key, input, _key.Length);
        input[_key.Length] = (byte)(objectNumber & 0xFF);
        input[_key.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        input[_key.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        input[_key.Length + 3] = (byte)(generation & 0xFF);
        input[_key.Length + 4] = (byte)((generation >> 8) & 0xFF);
        byte[] hash = MD5.HashData(input);
        int length = Math.Min(_key.Length + 5, 16);
        return hash[..length];
    }

    internal static void DecryptStrings(
        PdfObject value, int objectNumber, int generation, PdfEncryption encryption, PdfDocument document)
    {
        _ = document;
        switch (value)
        {
            case PdfString text:
                if (text.Bytes.Length > 0)
                {
                    text.SetBytes(encryption.DecryptString(text.Bytes, objectNumber, generation));
                }

                break;
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    DecryptStrings(item, objectNumber, generation, encryption, document);
                }

                break;
            case PdfDictionary dict:
                foreach (var pair in new List<KeyValuePair<string, PdfObject>>(dict.Items))
                {
                    DecryptStrings(pair.Value, objectNumber, generation, encryption, document);
                }

                break;
        }
    }

    // --------------------------------------------------------------- Primitif

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = new byte[256];
        for (int i = 0; i < 256; i++) state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var result = new byte[data.Length];
        int a = 0;
        int b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + 1) & 0xFF;
            b = (b + state[a]) & 0xFF;
            (state[a], state[b]) = (state[b], state[a]);
            result[i] = (byte)(data[i] ^ state[(state[a] + state[b]) & 0xFF]);
        }

        return result;
    }

    private static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] data, bool stripPadding)
    {
        if (data.Length == 0) return data;
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        int usable = data.Length - (data.Length % 16);
        if (usable == 0) return Array.Empty<byte>();
        byte[] plain = aes.CreateDecryptor().TransformFinalBlock(data, 0, usable);
        if (stripPadding && plain.Length > 0)
        {
            int pad = plain[^1];
            if (pad > 0 && pad <= 16 && pad <= plain.Length) plain = plain[..^pad];
        }

        return plain;
    }

    private static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        int usable = data.Length - (data.Length % 16);
        if (usable == 0) return Array.Empty<byte>();
        return aes.CreateEncryptor().TransformFinalBlock(data, 0, usable);
    }

    public override string ToString() =>
        $"Keamanan standar V{_version} R{_revision} ({(IsAes ? "AES" : "RC4")})";
}
