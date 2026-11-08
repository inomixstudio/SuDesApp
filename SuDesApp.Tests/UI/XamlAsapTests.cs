using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using SuDesApp.Wpf;
using SuDesApp.Wpf.Views;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Uji asap XAML: memuat kamus resource aplikasi dan seluruh view
    /// ber-konstruktor tanpa parameter di thread STA. Uji ViewModel tidak menyentuh XAML sama sekali,
    /// sehingga kunci resource yang hilang atau BAML yang rusak baru ketahuan
    /// saat aplikasi dijalankan manual. Uji ini menangkap regresi itu di CI.
    /// </summary>
    public sealed class XamlAsapTests
    {
        [Fact]
        public void Halaman_dan_kamus_resource_dapat_dimuat()
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

                    // Semua halaman/jendela ber-konstruktor tanpa parameter dimuat
                    // sungguhan (InitializeComponent menjalankan parser BAML dan
                    // resolusi StaticResource). Satu halaman yang rusak tidak boleh
                    // hanya lolos karena bukan bagian dari daftar pendek.
                    var jenisHalaman = typeof(App).Assembly.GetTypes()
                        .Where(t => t.Namespace == "SuDesApp.Wpf.Views"
                            && typeof(FrameworkElement).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && t.GetConstructor(Type.EmptyTypes) is not null)
                        .OrderBy(t => t.Name)
                        .ToArray();

                    Assert.NotEmpty(jenisHalaman);

                    var gagal = new List<string>();
                    foreach (var jenis in jenisHalaman)
                    {
                        try
                        {
                            Assert.NotNull(Activator.CreateInstance(jenis));
                        }
                        catch (Exception ex)
                        {
                            gagal.Add($"{jenis.Name}: {ex.GetType().Name}: {ex.Message} | dalam: {ex.InnerException?.GetType().Name} {ex.InnerException?.Message}");
                        }
                    }

                    Assert.True(
                        gagal.Count == 0,
                        $"XAML gagal dimuat pada {gagal.Count} halaman:\n" + string.Join("\n", gagal));

                    // Kunci dari App.xaml harus benar-benar terdaftar, bukan
                    // hanya tidak melempar saat halaman dibuat.
                    Assert.NotNull(Application.Current!.Resources["BoolToVis"]);
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
                throw new Xunit.Sdk.XunitException("XAML gagal dimuat: " + galat);
        }
    }
}
