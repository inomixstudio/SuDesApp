using System.Text.Json;

namespace SuDesApp.Sinkron;

/// <summary>
/// Satu baris antrean surat yang dibuat aplikasi Android dan menunggu aplikasi
/// desktop mengimpornya. Kolom dipetakan persis dari tabel
/// <c>antrean_surat</c> di Supabase.
/// </summary>
/// <param name="Id">Id baris antrean (uuid Supabase).</param>
/// <param name="IdPengguna">Id pengguna desktop yang membuat surat di Android.</param>
/// <param name="IdPerangkat">Id perangkat Android (tabel <c>perangkat_android</c>).</param>
/// <param name="IdJenis">Kode jenis surat, mis. "SKTM".</param>
/// <param name="NomorSementara">Nomor yang dibentuk Android — hanya placeholder.</param>
/// <param name="Nik">NIK pemohon; dipakai mencocokkan warga yang sudah ada.</param>
/// <param name="DataJson">Muatan surat: {"tanggal": "...", "fields": {...}}.</param>
/// <param name="Status">menunggu | sinkron | gagal.</param>
/// <param name="BatasCoba">Berapa kali impor baris ini gagal.</param>
/// <param name="Catatan">Catatan dari Android, mis. pesan galat.</param>
/// <param name="ClientUuid">Kunci idempotensi dibuat Android sekali per surat.
public sealed record AntreanSuratBaris(
    Guid Id,
    string TenantId,
    string IdPengguna,
    Guid? IdPerangkat,
    string IdJenis,
    string? NomorSementara,
    string? Nik,
    JsonElement DataJson,
    string Status,
    short BatasCoba,
    string? Catatan,
    DateTime DibuatPada,
    DateTime DiubahPada,
    Guid? ClientUuid = null)
{
    /// <summary>Tanggal surat dari payload, atau tanggal baris dibuat bila absen/rusak.</summary>
    public DateTime TanggalSurat
    {
        get
        {
            if (DataJson.ValueKind == JsonValueKind.Object &&
                DataJson.TryGetProperty("tanggal", out var t) &&
                t.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(t.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var hasil))
            {
                return hasil;
            }

            return DibuatPada;
        }
    }

    /// <summary>Elemen <c>fields</c> dari payload, atau objek kosong bila absen.</summary>
    public JsonElement Fields =>
        DataJson.ValueKind == JsonValueKind.Object &&
        DataJson.TryGetProperty("fields", out var f) &&
        f.ValueKind == JsonValueKind.Object
            ? f
            : JsonDocument.Parse("{}").RootElement;
}

/// <summary>Hasil satu putaran sinkron — dipakai tampilan dan log.</summary>
public sealed record SinkronHasil(
    int Diambil,
    int Berhasil,
    int Gagal,
    int Dilewati,
    string? JalurBerkas,
    string? PesanGalat)
{
    public static SinkronHasil Kosong(string? pesan = null) => new(0, 0, 0, 0, null, pesan);

    public bool AdaYangDiproses => Berhasil + Gagal + Dilewati > 0;
}
