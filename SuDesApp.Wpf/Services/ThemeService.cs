using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pengatur tema WPF — padanan penuh ThemeManager (WinForms).
    /// Mendukung 6 tema: Light, Dark, Blue, Green, Pink, Slate, Auto.
    /// Menukar ResourceDictionary warna tema global (styles tetap di
    /// Themes/ThemeStyles.xaml) dan menyimpan pilihan ke
    /// %APPDATA%\SuDesApp\theme.config — format yang sama dengan WinForms
    /// sehingga preferensi tema dibagikan antar aplikasi.
    /// </summary>
    public class ThemeService
    {
        public const string Light = "Light";
        public const string Dark = "Dark";
        public const string Blue = "Blue";
        public const string Green = "Green";
        public const string Pink = "Pink";
        public const string Slate = "Slate";
        public const string Auto = "Auto";

        /// <summary>Tema default — hijau segar khas desa, terang dan profesional.</summary>
        public const string DefaultTheme = Green;

        private static readonly Uri LightUri = new("pack://application:,,,/Themes/LightTheme.xaml");
        private static readonly Uri DarkUri = new("pack://application:,,,/Themes/DarkTheme.xaml");
        private static readonly Uri BlueUri = new("pack://application:,,,/Themes/BlueTheme.xaml");
        private static readonly Uri GreenUri = new("pack://application:,,,/Themes/GreenTheme.xaml");
        private static readonly Uri PinkUri = new("pack://application:,,,/Themes/PinkTheme.xaml");
        private static readonly Uri SlateUri = new("pack://application:,,,/Themes/SlateTheme.xaml");

        private readonly ILogger<ThemeService> _logger;
        private string _current = DefaultTheme;
        private bool _isApplying = false;

        public ThemeService(ILogger<ThemeService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Restore();
        }

        public string Current => _current;

        private string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SuDesApp", "theme.config");

        /// <summary>Daftar tema yang tersedia (padanan GetAvailableThemes WinForms).</summary>
        public static string[] GetAvailableThemes() => new[]
        {
            Light, Dark, Blue, Green, Pink, Slate, Auto
        };

        /// <summary>Nama tampilan tema (padanan GetThemeDisplayName WinForms).</summary>
        public static string GetDisplayName(string theme) => theme switch
        {
            Light => "Terang",
            Dark => "Gelap",
            Blue => "Biru",
            Green => "Hijau",
            Pink => "Pink",
            Slate => "Abu-abu",
            Auto => "Otomatis",
            _ => theme
        };

        /// <summary>Selesaikan tema "Auto" mengikuti mode gelap sistem Windows.</summary>
        public static string ResolveEffective(string theme)
        {
            if (!Auto.Equals(theme, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
            return IsSystemDarkMode() ? Dark : Light;
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int value)
                {
                    return value == 0;
                }
            }
            catch
            {
                // abaikan — fallback ke tema terang
            }
            return false;
        }

        /// <summary>Muat tema tersimpan dari file config (dipanggil saat startup).</summary>
        public void Restore()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var saved = File.ReadAllText(ConfigPath).Trim();
                    // Nama tema lama (mis. "Emerald" atau "Google") tidak lagi dikenali
                    // dan otomatis jatuh ke tema default di bawah ini.
                    if (IsValidTheme(saved))
                    {
                        ApplyCore(saved);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca tema tersimpan");
            }
            ApplyCore(DefaultTheme);
        }

        /// <summary>Terapkan tema lalu simpan ke config. Nilai: Light/Dark/Blue/Green/Pink/Slate/Auto.</summary>
        public void Apply(string theme)
        {
            if (!IsValidTheme(theme))
            {
                theme = DefaultTheme;
            }

            ApplyCore(theme);

            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(ConfigPath, _current);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyimpan tema");
            }
        }

        private static bool IsValidTheme(string? theme) => theme is not null && (
            Light.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Dark.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Blue.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Green.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Pink.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Slate.Equals(theme, StringComparison.OrdinalIgnoreCase) ||
            Auto.Equals(theme, StringComparison.OrdinalIgnoreCase));

        private void ApplyCore(string theme)
        {
            if (_isApplying) return;
            _isApplying = true;
            try
            {
                var effective = ResolveEffective(theme);

                var dictionaries = Application.Current?.Resources?.MergedDictionaries;
                if (dictionaries == null)
                {
                    _current = theme;
                    return;
                }

                var targetUri = effective switch
                {
                    Dark => DarkUri,
                    Blue => BlueUri,
                    Green => GreenUri,
                    Pink => PinkUri,
                    Slate => SlateUri,
                    _ => LightUri
                };

                // Cari dictionary warna tema yang sudah ada (bukan ThemeStyles.xaml).
                ResourceDictionary? themeDictionary = null;
                foreach (var dict in dictionaries)
                {
                    if (dict.Source == null) continue;
                    var src = dict.Source.ToString();
                    if (src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) &&
                        !src.EndsWith("ThemeStyles.xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        themeDictionary = dict;
                        break;
                    }
                }

                if (themeDictionary == null)
                {
                    themeDictionary = new ResourceDictionary { Source = targetUri };
                    dictionaries.Add(themeDictionary);
                }
                else
                {
                    themeDictionary.Source = targetUri;
                }

                _current = theme;
                _logger.LogInformation("Tema diterapkan: {Theme}", theme);
            }
            finally
            {
                _isApplying = false;
            }
        }
    }
}