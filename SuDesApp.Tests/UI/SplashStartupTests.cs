using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;
using Xunit;

namespace SuDesApp.Tests.UI
{
    public class SplashStartupTests
    {
        /// <summary>
        /// Asap: bangun jendela splash sungguhan pada utas STA dan pastikan
        /// identitas versi aplikasi ada di pohon visual.
        /// </summary>
        [Fact]
        public void JendelaSplash_memuatVersi()
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
