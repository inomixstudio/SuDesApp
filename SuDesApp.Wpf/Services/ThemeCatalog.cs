using System;
using System.Linq;
using System.Windows.Media;

namespace SuDesApp.Wpf.Services;

/// <summary>Data and lookup policy for the available themes, independent of applying one.</summary>
internal static class ThemeCatalog
{
    internal const string Light = "Light";
    internal const string Dark = "Dark";
    internal const string Blue = "Blue";
    internal const string Green = "Green";
    internal const string Pink = "Pink";
    internal const string Slate = "Slate";
    internal const string HighContrast = "HighContrast";
    internal const string Orbit = "Orbit";
    internal const string Ember = "Ember";
    internal const string Kanagawa = "Kanagawa";
    internal const string Blossom = "Blossom";
    internal const string Auto = "Auto";
    internal const string DefaultTheme = Green;

    private sealed record Definition(
        string Name,
        string DisplayName,
        string Description,
        string AccessibilityName,
        Color Swatch);

    private static readonly Definition[] Themes =
    {
        new(Light, "Terang", "Palet terang dengan aksen biru.", "Tema Terang", Color.FromRgb(0x21, 0x96, 0xF3)),
        new(Dark, "Gelap", "Palet gelap dengan aksen hijau mint.", "Tema Gelap", Color.FromRgb(0x34, 0xD3, 0x99)),
        new(Blue, "Biru", "Palet biru dengan tampilan bersih dan fokus.", "Tema Biru", Color.FromRgb(0x25, 0x63, 0xEB)),
        new(Green, "Hijau", "Palet hijau terang sebagai tema standar aplikasi.", "Tema Hijau", Color.FromRgb(0x15, 0x80, 0x3D)),
        new(Pink, "Pink", "Palet merah muda dengan aksen lembut.", "Tema Pink", Color.FromRgb(0xEC, 0x48, 0x99)),
        new(Slate, "Abu-abu", "Palet netral abu-abu dengan aksen jingga.", "Tema Abu-abu", Color.FromRgb(0xF5, 0x9E, 0x0B)),
        new(HighContrast, "Kontras Tinggi", "Kontras tinggi untuk membantu keterbacaan.", "Tema Kontras Tinggi", Color.FromRgb(0xFF, 0xD4, 0x00)),
        new(Orbit, "Orbit", "Palet gelap bernuansa kosmik dengan aksen ungu.", "Tema Orbit", Color.FromRgb(0xC2, 0xAF, 0xF7)),
        new(Ember, "Ember", "Palet gelap dengan aksen jingga hangat.", "Tema Ember", Color.FromRgb(0xF0, 0xA0, 0x66)),
        new(Kanagawa, "Kanagawa", "Palet gelap bernuansa biru dan alam.", "Tema Kanagawa", Color.FromRgb(0x2F, 0x5D, 0x8C)),
        new(Blossom, "Blossom", "Palet cerah dengan aksen merah muda.", "Tema Blossom", Color.FromRgb(0xB4, 0x56, 0x6F)),
    };

    internal static string[] GetAvailableThemes() =>
        Themes.Select(theme => theme.Name).Append(Auto).ToArray();

    internal static string GetDisplayName(string theme) => theme switch
    {
        Auto => "Otomatis",
        _ => Find(theme)?.DisplayName ?? theme
    };

    internal static string GetDescription(string theme) => theme switch
    {
        Auto => "Mengikuti mode terang atau gelap Windows.",
        _ => Find(theme)?.Description ?? string.Empty
    };

    internal static string GetAccessibilityName(string theme) => theme switch
    {
        Auto => "Tema Otomatis",
        _ => Find(theme)?.AccessibilityName ?? GetDisplayName(theme)
    };

    internal static Color GetSwatch(string theme) =>
        Find(theme)?.Swatch ?? Themes[0].Swatch;

    internal static bool IsValid(string? theme) => theme is not null &&
        (Auto.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
         Themes.Any(item => item.Name.Equals(theme, StringComparison.OrdinalIgnoreCase)));

    // Keep the previous ordinal resource lookup: config validation is
    // case-insensitive, but noncanonical casing resolves to the Light resource.
    internal static string ResourceName(string theme) => Find(theme)?.Name ?? Light;

    private static Definition? Find(string theme) =>
        Array.Find(Themes, item => item.Name == theme);
}
