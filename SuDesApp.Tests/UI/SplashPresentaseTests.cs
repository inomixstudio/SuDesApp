using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuDesApp.Wpf;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Animasi presentase jendela splash: angkanya naik dari 0% menuju 100%
    /// seiring waktu tunggu, berhenti di 100% (tidak pernah melewati), dan
    /// jendelanya memuat bilah presentase serta identitas versi aplikasi.
    /// </summary>
    public class SplashPresentaseTests
    {
        [Fact]
        public void PersentaseNaikSeiringWaktuDanBatasSeratus()
        {
            Assert.Equal(0d, SplashStartupWindow.PersentaseSetelah(0));
            Assert.Equal(
                50d,
                SplashStartupWindow.PersentaseSetelah(TimeSpan.FromSeconds(6).TotalMilliseconds));
            Assert.Equal(
                100d,
                SplashStartupWindow.PersentaseSetelah(TimeSpan.FromSeconds(12).TotalMilliseconds));
            Assert.Equal(
                100d,
                SplashStartupWindow.PersentaseSetelah(TimeSpan.FromSeconds(30).TotalMilliseconds));
        }

        [Fact]
        public void PersentaseNegatifTetapNol()
        {
            Assert.Equal(0d, SplashStartupWindow.PersentaseSetelah(-1000));
        }

        /// <summary>
        /// Asap: bangun jendela splash sungguhan pada utas STA dan pastikan
        /// bilah presentase, angka persennya, dan identitas versi ada di pohon.
        /// </summary>
        [Fact]
        public void JendelaSplash_memuatPresentaseDanVersi()
        {
            Exception? galat = null;

            var utas = new Thread(() =>
            {
                try
                {
                    if (Application.Current is null)
                    {
                        var app = new App();
                        app.InitializeComponent();
                    }

                    var splash = new SplashStartupWindow();
                    splash.Measure(new Size(500, 300));
                    splash.Arrange(new Rect(0, 0, 500, 300));

                    var teks = SemuaTextBlock(splash).Select(t => t.Text ?? string.Empty).ToArray();
                    Assert.Contains(teks, t => t.EndsWith("%", StringComparison.Ordinal));
                    Assert.Contains(teks, t => t.Contains(IdentitasAplikasi.VersiSingkat, StringComparison.Ordinal));
                }
                catch (Exception ex)
                {
                    galat = ex;
                }
            });

            utas.SetApartmentState(ApartmentState.STA);
            utas.Start();
            utas.Join();

            if (galat is not null)
                throw new Xunit.Sdk.XunitException("Splash gagal dimuat: " + galat);
        }

        private static IEnumerable<TextBlock> SemuaTextBlock(DependencyObject akar)
        {
            foreach (var anak in LogicalTreeHelper.GetChildren(akar).OfType<DependencyObject>())
            {
                if (anak is TextBlock tb) yield return tb;
                foreach (var c in SemuaTextBlock(anak)) yield return c;
            }
        }
    }
}
