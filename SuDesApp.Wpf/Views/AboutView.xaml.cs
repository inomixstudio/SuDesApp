using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class AboutView : UserControl
    {
        private readonly DispatcherTimer? _uptimeTimer;

        public AboutView()
        {
            InitializeComponent();

            // Uptime pada bagian "Detail sistem" disegarkan tiap 30 detik.
            // Timer hanya jalan saat view dimuat (Unloaded menghentikannya)
            // supaya tidak ada kerja background saat halaman tidak terlihat.
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _uptimeTimer.Tick += (_, _) =>
            {
                if (DataContext is AboutViewModel vm)
                {
                    vm.RefreshUptime();
                }
            };
            Loaded += (_, _) => _uptimeTimer.Start();
            Unloaded += (_, _) => _uptimeTimer.Stop();
        }

        private void NavBagian_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: AboutBagianViewModel bagian } &&
                DataContext is AboutViewModel vm)
            {
                vm.PilihBagian(bagian);
            }
        }
    }
}
