using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class WaPanelView : UserControl
    {
        public WaPanelView()
        {
            InitializeComponent();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is WaPanelViewModel vm)
            {
                vm.Cleanup();
            }
        }

        /// <summary>
        /// Banner "akun Google belum terhubung": arahkan operator ke halaman
        /// Login dengan Google.
        /// </summary>
        private void OnLoginGoogleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = Application.Current as App;
                if (app?.ServiceProvider == null) return;

                var main = app.ServiceProvider.GetService<MainWindowViewModel>();
                if (main == null) return;

                // Lewat MainWindowViewModel: scope halamannya dikelola
                // NavigationService. Scope lokal yang dibuat code-behind di sini
                // dulu tidak pernah punya pemilik sehingga bocor tiap klik.
                _ = main.ShowGoogleLoginPublicAsync();
            }
            catch (Exception)
            {
                // Biarkan UI tetap responsif bila navigasi gagal.
            }
        }

        /// <summary>
        /// Banner kesiapan: buka Pengaturan Aplikasi fokus ke kartu Google
        /// Formulir & Sheet.
        /// </summary>
        private void OnBukaPengaturanGoogleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = Application.Current as App;
                var main = app?.ServiceProvider?.GetService<MainWindowViewModel>();
                if (main != null)
                {
                    _ = main.OpenSettingsFocusPublicAsync(PengaturanAplikasiView.SectionGoogleSheet);
                }
            }
            catch (Exception)
            {
                // Biarkan UI tetap responsif bila navigasi gagal.
            }
        }
    }
}
