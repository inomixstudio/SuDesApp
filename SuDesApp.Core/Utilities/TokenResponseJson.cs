using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2.Responses;

namespace SuDesApp.Utilities;

/// <summary>
/// Konverter System.Text.Json untuk <see cref="TokenResponse"/> yang **setia**
/// dengan bentuk JSON Newtonsoft bawaan Google.Apis (atribut
/// <c>[JsonProperty("access_token")]</c>, dst.). Kesetiaan ini perlu karena:
/// (1) berkas token lama yang sudah ada di perangkat pengguna ditulis Newtonsoft
/// oleh versi aplikasi sebelumnya, dan (2) bila pengguna kembali ke versi lama,
/// Newtonsoft harus tetap bisa membaca berkas yang ditulis konverter ini.
/// Uji pembanding di <c>SuDesApp.Tests/Utilities/ProtectedGoogleTokenDataStoreTests</c>
/// menjaga kedua arah itu tetap cocok.
/// </summary>
internal sealed class TokenResponseJson : JsonConverter<TokenResponse>
{
    public override TokenResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Bentuk token Google harus objek JSON.");

        var token = new TokenResponse();
        bool adaIssuedUtc = false;
        DateTime cadanganIssued = default;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? nama = reader.GetString();
            reader.Read();
            switch (nama?.ToUpperInvariant())
            {
                case "ACCESS_TOKEN": token.AccessToken = reader.GetString(); break;
                case "TOKEN_TYPE": token.TokenType = reader.GetString(); break;
                case "EXPIRES_IN":
                    token.ExpiresInSeconds = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt64();
                    break;
                case "REFRESH_TOKEN": token.RefreshToken = reader.GetString(); break;
                case "SCOPE": token.Scope = reader.GetString(); break;
                case "ID_TOKEN": token.IdToken = reader.GetString(); break;
                case "ISSUEDUTC":
                    token.IssuedUtc = reader.TokenType == JsonTokenType.Null ? default : reader.GetDateTime();
                    adaIssuedUtc = true;
                    break;
                case "ISSUED":
                    // Properti usang Google (dihitung dari IssuedUtc). Dipakai
                    // sebagai cadangan bila IssuedUtc tidak ada di berkas lama.
                    if (reader.TokenType != JsonTokenType.Null) cadanganIssued = reader.GetDateTime();
                    break;
                case "EXPIRETIME":
                    // Properti internal alur impersonasi akun layanan — tidak
                    // pernah terisi pada alur OAuth desktop ini (selalu null),
                    // jadi dibuang saat dibaca dan ditulis null.
                    reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (!adaIssuedUtc && cadanganIssued != default)
            token.IssuedUtc = cadanganIssued.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(cadanganIssued, DateTimeKind.Utc)
                : cadanganIssued.ToUniversalTime();

        return token;
    }

    public override void Write(Utf8JsonWriter writer, TokenResponse value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("access_token", value.AccessToken);
        writer.WriteString("token_type", value.TokenType);
        if (value.ExpiresInSeconds is null) writer.WriteNull("expires_in");
        else writer.WriteNumber("expires_in", value.ExpiresInSeconds.Value);
        writer.WriteString("refresh_token", value.RefreshToken);
        writer.WriteString("scope", value.Scope);
        writer.WriteString("id_token", value.IdToken);
        writer.WriteNull("expireTime");
        // "Issued" (usang, dihitung dari IssuedUtc) ditulis pula supaya bentuknya
        // sama persis dengan keluaran Newtonsoft yang lama.
        writer.WriteString("Issued", value.IssuedUtc == default ? default(DateTime) : value.IssuedUtc.ToLocalTime());
        writer.WriteString("IssuedUtc", value.IssuedUtc);
        writer.WriteEndObject();
    }
}
