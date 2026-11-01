using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Mobile;

/// <summary>
/// Katalog jenis surat yang ditawarkan aplikasi Android. Definisi dibaca dari
/// berkas JSON tertanam supaya jenis surat bisa ditambah tanpa mengubah kode
/// formulir atau layar.
/// </summary>
public static class KatalogSurat
{
    private static readonly JsonSerializerOptions Opsi = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private static readonly Lazy<IReadOnlyList<DefinisiSurat>> _semua = new(Muat);

    public static IReadOnlyList<DefinisiSurat> Semua => _semua.Value;

    public static IReadOnlyList<DefinisiSurat> Menu(KelompokMenu kelompok) =>
        Semua.Where(s => s.Kelompok == kelompok).ToList();

    public static DefinisiSurat? Cari(string? kode)
    {
        if (string.IsNullOrWhiteSpace(kode)) return null;
        string dicari = kode.Trim();
        return Semua.FirstOrDefault(s => string.Equals(s.Kode, dicari, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DefinisiSurat> Muat()
    {
        var rakitan = Assembly.GetExecutingAssembly();
        string? nama = rakitan.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("katalog-surat.json", StringComparison.OrdinalIgnoreCase));

        if (nama == null)
        {
            throw new InvalidOperationException("Berkas katalog surat tidak tertanam di aplikasi.");
        }

        using var aliran = rakitan.GetManifestResourceStream(nama)
            ?? throw new InvalidOperationException("Berkas katalog surat tidak dapat dibaca.");

        var daftar = JsonSerializer.Deserialize<List<DefinisiSurat>>(aliran, Opsi)
            ?? throw new InvalidOperationException("Isi katalog surat kosong.");

        var galat = new List<string>();
        foreach (var surat in daftar)
        {
            if (string.IsNullOrWhiteSpace(surat.Kode)) galat.Add("ada jenis surat tanpa kode");
            if (string.IsNullOrWhiteSpace(surat.Nama)) galat.Add($"{surat.Kode}: nama kosong");
            if (string.IsNullOrWhiteSpace(surat.Judul)) surat.Judul = surat.Nama.ToUpperInvariant();

            foreach (var kolom in surat.Kolom)
            {
                kolom.Kunci = FormatSurat.NamaBerkas(kolom.Kunci.Length > 0 ? kolom.Kunci : kolom.Label);
            }

            if (surat.Kolom.Count == 0) galat.Add($"{surat.Kode}: tidak punya kolom isian");
        }

        if (galat.Count > 0)
        {
            throw new InvalidOperationException("Katalog surat rusak: " + string.Join("; ", galat));
        }

        var kembar = daftar.GroupBy(s => s.Kode, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (kembar.Count > 0)
        {
            throw new InvalidOperationException("Kode surat kembar: " + string.Join(", ", kembar));
        }

        return daftar;
    }
}
