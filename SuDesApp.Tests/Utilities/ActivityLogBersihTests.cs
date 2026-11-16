using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities
{
    /// <summary>
    /// Pencatatan riwayat aktivitas berjalan di antrean latar belakang. Uji ini
    /// menjaga tindakan "Bersihkan Riwayat" benar-benar mengosongkan tabel:
    /// baris yang sedang menunggu di antrean tidak boleh ditulis ulang setelah
    /// penghapusan, sementara aktivitas baru setelah itu tetap tercatat.
    /// </summary>
    [Collection("PreferensiBersama")]
    public sealed class ActivityLogBersihTests : IDisposable
    {
        private readonly string _folderAkar;
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "sudes-aktivitas-" + Guid.NewGuid().ToString("N"));

        public ActivityLogBersihTests()
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
        public async Task BersihkanRiwayat_mengosongkanTabelSepenuhnya()
        {
            using var layanan = BuatLayanan();

            for (int i = 0; i < 20; i++)
                layanan.Log("UJI", "Dokumen " + i, "Buat");

            await layanan.ClearAsync();

            // Beri penulis latar waktu menguras antrean; batch lama harus dibuang.
            await Task.Delay(2000);

            var sisa = await layanan.GetRecentAsync(500);
            Assert.Empty(sisa);
        }

        [Fact]
        public async Task BersihkanRiwayat_aktivitasBaruTetapTercatat()
        {
            using var layanan = BuatLayanan();

            layanan.Log("UJI", "Lama", "Buat");
            await layanan.ClearAsync();
            layanan.Log("UJI", "Baru", "Buat");

            await Task.Delay(2000);

            var sisa = await layanan.GetRecentAsync(500);
            bool adaBaru = false;
            foreach (var e in sisa)
            {
                Assert.NotEqual("Lama", e.Dokumen);
                if (e.Dokumen == "Baru") adaBaru = true;
            }
            Assert.True(adaBaru, "Aktivitas baru setelah dibersihkan harus tetap tercatat.");
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
    }
}
