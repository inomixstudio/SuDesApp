using System.Windows;
using System.Windows.Input;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Helper statis yang menyatukan perilaku chrome jendela bertema:
    /// drag-move header, drag di atas layar = maximize, klik ganda header =
    /// maximize/restore, dan tombol minimize/maximize/close.
    /// Dipakai bersama template chrome di Themes/ThemeStyles.xaml.
    /// </summary>
    public static class ChromeWindowBehavior
    {
        public static void Attach(Window window)
        {
            if (window == null) return;

            window.CommandBindings.Add(new CommandBinding(AppChromeCommands.Minimize,
                (_, _) => window.WindowState = WindowState.Minimized));
            window.CommandBindings.Add(new CommandBinding(AppChromeCommands.MaximizeRestore,
                (_, _) => window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized));
            window.CommandBindings.Add(new CommandBinding(AppChromeCommands.Close,
                (_, _) => window.Close()));

            window.StateChanged += (_, _) => UpdateStateProps(window);
            window.Loaded += (_, _) => UpdateStateProps(window);
        }

        private static void UpdateStateProps(Window window)
        {
            bool maximized = window.WindowState == WindowState.Maximized;
            window.SetValue(IsMaximizedProp, maximized);
            window.SetValue(IsNormalProp, !maximized);
        }

        public static readonly DependencyProperty IsMaximizedProp = DependencyProperty.RegisterAttached(
            "IsMaximized", typeof(bool), typeof(ChromeWindowBehavior), new PropertyMetadata(false));

        public static readonly DependencyProperty IsNormalProp = DependencyProperty.RegisterAttached(
            "IsNormal", typeof(bool), typeof(ChromeWindowBehavior), new PropertyMetadata(true));

        public static bool GetIsMaximized(DependencyObject obj) => (bool)obj.GetValue(IsMaximizedProp);
        public static void SetIsMaximized(DependencyObject obj, bool value) => obj.SetValue(IsMaximizedProp, value);

        public static bool GetIsNormal(DependencyObject obj) => (bool)obj.GetValue(IsNormalProp);
        public static void SetIsNormal(DependencyObject obj, bool value) => obj.SetValue(IsNormalProp, value);
    }
}
