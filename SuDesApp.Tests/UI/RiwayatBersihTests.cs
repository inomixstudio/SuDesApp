using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Utilities;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Halaman Riwayat Aktivitas: menekan "Bersihkan Riwayat" harus mengosongkan
    /// tabel di layar seketika. Penjaga <c>IsBusy</c> pada pemuat inti pernah
    /// membuat muat ulang langsung kembali, sehingga baris lama tetap tampil.
    /// </summary>
    [Collection("PreferensiBersama")]
    public sealed class RiwayatBersihTests : IDisposable
    {
        private readonly string _folderAkar;
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "sudes-riwayat-" + Guid.NewGuid().ToString("N"));

        public RiwayatBersihTests()
        {
            _folderAkar = CariRootRepo() ?? throw new InvalidOperationException("Folder repo tidak ditemukan.");
            Directory.CreateDirectory(_folder);
            AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "prefs.json");
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            try { Directory.Delete(_folder, recursive: true); } catch { /* sisa uji */ }
        }

        [Fact]
        public async Task BersihkanRiwayat_mengosongkanTabelTampilan()
        {
            using var layanan = BuatLayanan();
            var vm = new RiwayatViewModel(
                layanan, new PesanPalsu(), NullLogger<RiwayatViewModel>.Instance, BuatNavigasi());

            for (int i = 0; i < 5; i++)
                layanan.Log("UJI", "Dokumen " + i, "Buat");

            // Penulis latar menulis di antrean; beri waktu sebelum memuat.
            await Task.Delay(1500);

            await vm.LoadAsync();
            Assert.NotEmpty(vm.Items);

            vm.ClearCommand.Execute(null);

            // Perintah async void: tunggu sampai tabel benar-benar kosong.
            for (int i = 0; i < 60 && vm.Items.Count > 0; i++)
                await Task.Delay(100);

            Assert.Empty(vm.Items);
            Assert.True(vm.TidakAdaAktivitas);
        }

        private ActivityLogService BuatLayanan()
        {
            string cfg = Path.Combine(_folder, "appsettings.json");
            string Sep(string p) => p.Replace('\\', '/');
            File.WriteAllText(cfg, $$"""
            {
              "AppConfig": {
                "databaseConnectionString": "Data Source={{Sep(Path.Combine(_folder, "desa.db"))}}",
                "templateFolder": "{{Sep(Path.Combine(_folder, "Templates"))}}",
                "tempPdfFolder": "{{Sep(Path.Combine(_folder, "TempPDF"))}}",
                "pdfOutputPath": "{{Sep(Path.Combine(_folder, "Output"))}}",
                "fontFolder": "{{Sep(Path.Combine(_folderAkar, "Resources", "Fonts"))}}",
                "logoPath": "{{Sep(Path.Combine(_folderAkar, "Resources", "logo.png"))}}",
                "updateCheckUrl": null
              }
            }
            """);

            return new ActivityLogService(new AppConfig(cfg), NullLogger<ActivityLogService>.Instance);
        }

        private static NavigationService BuatNavigasi()
        {
            var layanan = new ServiceCollection();
            layanan.AddLogging();
            var provider = layanan.BuildServiceProvider();
            return new NavigationService(provider, NullLogger<NavigationService>.Instance);
        }

        private static string? CariRootRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private sealed class PesanPalsu : IMessageService
        {
            public Task ShowMessageAsync(string message, string title, AppMessageButton buttons, AppMessageIcon icon)
                => Task.CompletedTask;
            public Task ShowErrorAsync(string message) => Task.CompletedTask;
            public Task ShowWarningAsync(string message) => Task.CompletedTask;
            public Task ShowInfoAsync(string message) => Task.CompletedTask;
            public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);
        }
    }
}
