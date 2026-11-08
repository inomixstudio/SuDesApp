using System;
using System.Windows;

namespace SuDesApp.Installer
{
    /// <summary>App installer WPF kustom — gaya kartu radius 14 + emerald sama seperti splash/login aplikasi.</summary>
    public partial class App : Application
    {
        public const string Versi = "2.12.0";
        public const string NamaMutex = "SuDesApp_SingletonMutex";
        public static string LokasiBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "SuDesApp");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool modeUninstall = Array.Exists(e.Args, a =>
                string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
            var window = new Views.InstallerWindow(modeUninstall);
            MainWindow = window;
            window.Show();
        }
    }
}
