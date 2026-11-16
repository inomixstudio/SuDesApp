using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Penjaga halaman pembuka: selama data desa/pejabat masih contoh, aplikasi
    /// membuka Panduan Awal lebih dulu — bukan Beranda — supaya pengguna diarahkan
    /// melengkapi data sebelum surat pertama dicetak. Sumber keputusan itu adalah
    /// <see cref="PanduanAwalViewModel.DataDesaMasihContoh"/>.
    /// </summary>
    public sealed class PanduanAwalDataDesaTests
    {
        [Fact]
        public async Task DataDesaContoh_MembukaPanduanAwal()
        {
            var vm = BuatViewModel(new DesaData
            {
                NamaDesa = DesaContoh.NamaDesa,
                Kecamatan = DesaContoh.Kecamatan,
                Kabupaten = DesaContoh.Kabupaten,
                Alamat = DesaContoh.Alamat,
                KepalaDesa = DesaContoh.KepalaDesa,
                SekretarisDesa = DesaContoh.SekretarisDesa,
            });

            await vm.PeriksaAsync();

            Assert.True(vm.DataDesaMasihContoh);
        }

        [Fact]
        public async Task DataDesaLengkap_TidakMembukaPanduanAwal()
        {
            var vm = BuatViewModel(new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = "Jl. Raya Sumberjaya No. 1",
                KepalaDesa = "Dedi Mulyadi",
                SekretarisDesa = "Siti Aminah",
            });

            await vm.PeriksaAsync();

            Assert.False(vm.DataDesaMasihContoh);
        }

        /// <summary>
        /// Pejabat pun bagian dari data pembuka: kop lengkap tetapi tanda tangan
        /// masih contoh harus tetap mengarahkan pengguna ke Panduan Awal.
        /// </summary>
        [Fact]
        public async Task PejabatContohSaja_TetapMembukaPanduanAwal()
        {
            var vm = BuatViewModel(new DesaData
            {
                NamaDesa = "Sumberjaya",
                Kecamatan = "Tempuran",
                Kabupaten = "Karawang",
                Alamat = "Jl. Raya Sumberjaya No. 1",
                KepalaDesa = DesaContoh.KepalaDesa,
                SekretarisDesa = DesaContoh.SekretarisDesa,
            });

            await vm.PeriksaAsync();

            Assert.True(vm.DataDesaMasihContoh);
        }

        private static PanduanAwalViewModel BuatViewModel(DesaData desa)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "sudes-panduan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string cfg = Path.Combine(tmp, "appsettings.json");
            File.WriteAllText(cfg, $$"""
            {
              "AppConfig": {
                "databaseConnectionString": "Data Source={{Path.Combine(tmp, "desa.db").Replace('\\', '/')}}",
                "templateFolder": "{{Path.Combine(tmp, "Templates").Replace('\\', '/')}}",
                "tempPdfFolder": "{{Path.Combine(tmp, "TempPDF").Replace('\\', '/')}}",
                "pdfOutputPath": "{{Path.Combine(tmp, "Output").Replace('\\', '/')}}"
              }
            }
            """);

            var pengaturan = new SettingsManager(new DesaRepoPalsu(desa), new AppConfig(cfg));
            var penomoran = new PenomoranSuratService(NullLogger<PenomoranSuratService>.Instance);

            var layanan = new ServiceCollection();
            layanan.AddLogging();
            var provider = layanan.BuildServiceProvider();

            var navigasi = new NavigationService(provider, NullLogger<NavigationService>.Instance);

            return new PanduanAwalViewModel(
                pengaturan,
                penomoran,
                new PanduanStorePalsu(),
                provider,
                navigasi,
                NullLogger<PanduanAwalViewModel>.Instance);
        }

        private sealed class PanduanStorePalsu : IPanduanAwalStore
        {
            public bool SudahSelesai => false;
            public void SetSudahSelesai(bool selesai) { }
            public bool PenomoranDiperiksa => false;
            public void SetPenomoranDiperiksa(bool diperiksa) { }
        }

        private sealed class DesaRepoPalsu : IDesaRepository
        {
            private readonly DesaData _desa;

            public DesaRepoPalsu(DesaData desa) => _desa = desa;

            public Task InitializeAsync() => Task.CompletedTask;

            public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(_desa);

            public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

            public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

            public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(_desa);

            public Task InvalidateCacheAsync() => Task.CompletedTask;
        }
    }
}
