using System.Windows.Input;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Perintah global untuk chrome jendela bertema (title bar kustom):
    /// minimize, maximize/restore, dan close. Template chrome di
    /// Themes/ThemeStyles.xaml memanggil perintah ini; setiap Window
    /// yang memakai chrome mendaftarkan CommandBinding-nya lewat
    /// <see cref="ChromeWindowBehavior"/>.
    /// </summary>
    public static class AppChromeCommands
    {
        public static readonly RoutedCommand Minimize = new("MinimizeWindow", typeof(AppChromeCommands));
        public static readonly RoutedCommand MaximizeRestore = new("MaximizeRestoreWindow", typeof(AppChromeCommands));
        public static readonly RoutedCommand Close = new("CloseWindow", typeof(AppChromeCommands));
    }
}
