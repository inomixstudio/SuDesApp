using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Wpf.Themes;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pengatur tema WPF — padanan penuh ThemeManager (WinForms).
    /// Mendukung 11 tema warna: Light, Dark, Blue, Green, Pink, Slate,
    /// HighContrast, Orbit, Ember, Kanagawa, Blossom
    /// (plus Auto yang mengikuti mode gelap Windows).
    /// Menukar ResourceDictionary warna tema global (styles tetap di
    /// Themes/ThemeStyles.xaml) dan menyimpan pilihan ke
    /// %APPDATA%\SuDesApp\theme.config — format yang sama dengan WinForms
    /// sehingga preferensi tema dibagikan antar aplikasi.
    /// </summary>
    public class ThemeService
    {
        public const string Light = ThemeCatalog.Light;
        public const string Dark = ThemeCatalog.Dark;
        public const string Blue = ThemeCatalog.Blue;
        public const string Green = ThemeCatalog.Green;
        public const string Pink = ThemeCatalog.Pink;
        public const string Slate = ThemeCatalog.Slate;

        /// <summary>Tema kontras tinggi opsional (WCAG AAA) untuk pengguna low vision.</summary>
        public const string HighContrast = ThemeCatalog.HighContrast;

        // — Tema adaptasi preset desain (berkas dihasilkan Tools/gen-tema-preset.py;
        //   palet dikutip dari preset aslinya). —
        /// <summary>Tema gelap-oranye (preset "Orbit").</summary>
        public const string Orbit = ThemeCatalog.Orbit;
        /// <summary>Tema gelap-merah (preset "Ember").</summary>
        public const string Ember = ThemeCatalog.Ember;
        /// <summary>Tema gelap-indigo (preset "Kanagawa").</summary>
        public const string Kanagawa = ThemeCatalog.Kanagawa;
        /// <summary>Tema terang-pink (preset "Blossom").</summary>
        public const string Blossom = ThemeCatalog.Blossom;

        public const string Auto = ThemeCatalog.Auto;

        /// <summary>Tema default — hijau segar khas desa, terang dan profesional.</summary>
        public const string DefaultTheme = ThemeCatalog.DefaultTheme;

        private readonly ILogger<ThemeService> _logger;
        private string _current = DefaultTheme;
        private bool _isApplying = false;

        /// <summary>Dinyalakan setelah tema benar-benar diterapkan ke ResourceDictionary
        /// aplikasi. Dipakai jendela untuk memperbarui ikonnya agar ikut warna tema.</summary>
        public event Action? TemaBerubah;

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
        public static string[] GetAvailableThemes() => ThemeCatalog.GetAvailableThemes();

        /// <summary>Nama tampilan tema (padanan GetThemeDisplayName WinForms).</summary>
        public static string GetDisplayName(string theme) => ThemeCatalog.GetDisplayName(theme);

        /// <summary>Deskripsi tema untuk tooltip dan bantuan aksesibilitas.</summary>
        public static string GetDescription(string theme) => ThemeCatalog.GetDescription(theme);

        /// <summary>Nama aksesibilitas tombol pilihan tema.</summary>
        public static string GetAccessibilityName(string theme) => ThemeCatalog.GetAccessibilityName(theme);

        /// <summary>
        /// Warna swatch (titik warna) untuk daftar menu Tema — gaya pemilih tema
        /// aplikasi: setiap tema diwakili satu titik warna aksennya,
        /// berbeda dari tema AKTIF sehingga pengguna melihat palet tiap tema
        /// sebelum memilihnya. Karena mewakili tema LAIN, warna ini memang tidak
        /// boleh mengikuti tema aktif — jadi dikodekan sebagai konstanta di sini,
        /// bukan kunci tema.
        /// </summary>
        public static System.Windows.Media.Color WarnaSwatch(string theme) =>
            ThemeCatalog.GetSwatch(theme); // Auto/unknown → biru

        /// <summary>Selesaikan tema "Auto" mengikuti mode gelap sistem Windows.</summary>
        public static string ResolveEffective(string theme)
        {
            if (!Auto.Equals(theme, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
            return IsSystemDarkMode() ? Dark : Light;
        }

        /// <summary>
        /// Baca tema tersimpan dari config TANPA menyentuh ResourceDictionary
        /// aplikasi. Dipakai jendela splash yang tampil sebelum ResourceDictionary
        /// tema dimuat, agar warnanya langsung sesuai pilihan pengguna.
        /// </summary>
        internal static string BacaTemaTersimpan()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SuDesApp", "theme.config");
                if (File.Exists(path))
                {
                    string saved = File.ReadAllText(path).Trim();
                    if (IsValidTheme(saved))
                    {
                        return saved;
                    }
                }
            }
            catch
            {
                // abaikan — jatuh ke tema default
            }
            return DefaultTheme;
        }

        /// <summary>URI ResourceDictionary sebuah tema (sudah melalui ResolveEffective bila perlu).</summary>
        internal static Uri UriTema(string theme)
        {
            // Ordinal lookup matches the old switch: a noncanonical case falls
            // back to Light even though saved theme validation ignores case.
            var namaBerkas = ThemeCatalog.ResourceName(theme);
            return PemuatanTema.Alamat($"Themes/{namaBerkas}Theme.xaml");
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

        /// <summary>Terapkan tema lalu simpan ke config. Nilai: Light/Dark/Blue/Green/Pink/Slate/HighContrast/Orbit/Ember/Kanagawa/Blossom/Auto.</summary>
        /// <summary>
        /// Terapkan tema dan simpan pilihannya. Mengembalikan false bila tema
        /// sudah berlaku tetapi GAGAL disimpan — pilihannya tidak bertahan sampai
        /// aplikasi dibuka lagi, dan pemanggil wajib memberi tahu pengguna
        /// (tanpa itu pengguna hanya melihat centang yang menyesatkan).
        /// </summary>
        public bool Apply(string theme)
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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyimpan tema");
                return false;
            }
        }

        private static bool IsValidTheme(string? theme) => ThemeCatalog.IsValid(theme);

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

                var targetUri = UriTema(effective);

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
                TemaBerubah?.Invoke();
            }
            finally
            {
                _isApplying = false;
            }
        }
    }
}
