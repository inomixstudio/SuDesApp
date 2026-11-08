using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.ViewModels;

/// <summary>Defines the built-in letter choices and resolves names used by quick links.</summary>
internal static class BuatSuratCatalog
{
    // Daftar pilihannya tinggal di Core (MenuSuratKatalog) supaya desktop dan
    // backend HTTP membaca satu sumber yang sama — menambah jenis surat baru
    // cukup sekali, dan keduanya langsung ikut.
    public static IReadOnlyList<(string Label, string Template)> MenuSurat { get; } =
        MenuSuratKatalog.Semua.Select(p => (p.Label, p.Jenis)).ToList();

    public static string ResolveTemplate(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return MenuSuratKatalog.TryResolveTemplate(displayName, out var template)
            ? template
            : displayName;
    }
}
