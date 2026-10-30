using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.ViewModels;

/// <summary>Defines the built-in letter choices and resolves names used by quick links.</summary>
internal static class BuatSuratCatalog
{
    // Daftar pilihannya tinggal di Core (MenuSuratKatalog) supaya menu desktop
    // dan menu web membaca satu sumber yang sama — menambah jenis surat baru
    // cukup sekali, dan kedua platform langsung ikut.
    public static IReadOnlyList<(string Label, string Template)> MenuSurat { get; } =
        MenuSuratKatalog.Semua.Select(p => (p.Label, p.Jenis)).ToList();

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
