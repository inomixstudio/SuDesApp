using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.ViewModels;

/// <summary>Defines the built-in letter choices and resolves names used by quick links.</summary>
internal static class BuatSuratCatalog
{
    public static IReadOnlyList<(string Label, string Template)> MenuSurat { get; } =
    [
        ("SKD Umum", SuratConstants.SKD_UMUM),
        ("Domisili Warga", SuratConstants.DOMISILI_WARGA),
        ("Domisili Instansi", SuratConstants.INSTANSI),
        ("SKU", SuratConstants.SKU),
        ("Pengantar SKCK", SuratConstants.PENGANTAR_SKCK),
        ("Izin Suami / Orang Tua", SuratConstants.IZIN_ORTU),
        ("SKTM", SuratConstants.SKTM),
        ("Belum Terbit", SuratConstants.SK_BELUM_TERBIT),
        ("Garapan Sawah", SuratConstants.GARAPAN_SAWAH),
        ("Kematian", SuratConstants.KEMATIAN),
        ("Beda Nama", SuratConstants.BEDANAMA),
        ("Kenal Lahir", SuratConstants.KENAL_LAHIR),
        ("Tinggal Sementara", SuratConstants.IJIN_TINGGAL),
        ("Ahli Waris", SuratConstants.AHLI_WARIS),
        ("Pengantar Adminduk", SuratConstants.PENGANTAR_ADMINDUK),
        ("Surat Keterangan Pindah", SuratConstants.SK_PINDAH),
        ("SK Tidak Punya Rumah", SuratConstants.SK_TIDAK_PUNYA_RUMAH),
        ("Izin Keramaian", SuratConstants.IZIN_KERAMAIAN)
    ];

    private static readonly IReadOnlyDictionary<string, string> TemplateByDisplay =
        MenuSurat.ToDictionary(item => item.Label, item => item.Template, StringComparer.OrdinalIgnoreCase)
            .Concat(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IZIN ORTU / SUAMI"] = SuratConstants.IZIN_ORTU,
                ["NUMPANG NIKAH"] = SuratConstants.NTCR_N8,
                ["IJIN TINGGAL"] = SuratConstants.IJIN_TINGGAL
            })
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    public static string ResolveTemplate(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return TemplateByDisplay.TryGetValue(displayName.Trim(), out var template)
            ? template
            : displayName;
    }
}
