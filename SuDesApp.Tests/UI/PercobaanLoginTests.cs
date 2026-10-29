using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>Batas percobaan login: satu pemilik per permukaan, berakhir setelah berhasil.</summary>
    [Collection("LisensiBerbagiBerkas")]
    public class PercobaanLoginTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "sudes-login-" + Guid.NewGuid().ToString("N"));

        private DateTime _sekarang = new(2026, 1, 1, 8, 0, 0);

        public PercobaanLoginTests()
        {
            Directory.CreateDirectory(_folder);
            AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "prefs.json");
            BerkasLogin.LokasiOverride = () => Path.Combine(_folder, BerkasLogin.NamaBerkas);
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            BerkasLogin.LokasiOverride = null;
            AkunPusat.LokasiBerkasSandiOverride = null;
            SessionContext.TandaiSesiPusat(false);
            try { Directory.Delete(_folder, recursive: true); } catch { /* sisa berkas uji */ }
        }

        [Fact]
        public void PembatasLogin_DiresetSetelahBerhasil()
        {
            var pembatas = new PembatasPercobaanLogin(() => _sekarang);

            for (int i = 0; i < PenggunaService.MaksPercobaanGagal; i++) pembatas.CatatGagal();
            Assert.False(pembatas.BolehMencoba);

            _sekarang = _sekarang.AddMinutes(PenggunaService.DurasiKunci.TotalMinutes + 1);
            Assert.True(pembatas.BolehMencoba);

            pembatas.CatatBerhasil();
            pembatas.CatatGagal();
            Assert.True(pembatas.BolehMencoba); // satu salah ketik tidak langsung mengunci lagi
        }

        [Fact]
        public async Task MasaTungguAkunPusat_BerakhirSetelahLoginBerhasil()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, null);
                AkunPusat.LokasiBerkasSandiOverride = () => Path.Combine(_folder, "tidak-ada.kunci");

                var layanan = new LayananLoginPalsu();
                var vm = new LoginViewModel(layanan, waktu: () => _sekarang);
                vm.Username = "adminpusat";
                vm.Password = "salah";

                for (int i = 1; i <= PenggunaService.MaksPercobaanGagal; i++)
                {
                    vm.LoginCommand.Execute(null);
                    await TungguAsync(() => layanan.Panggilan == i);
                }

                // Percobaan berikutnya ditahan sebelum menyentuh layanan.
                vm.LoginCommand.Execute(null);
                await TungguAsync(() => vm.ErrorMessage.StartsWith("Terlalu banyak", StringComparison.Ordinal));
                Assert.Equal(PenggunaService.MaksPercobaanGagal, layanan.Panggilan);

                // Lewat masa tunggu: sandi pusat yang benar diterima dan limiter dibersihkan.
                _sekarang = _sekarang.AddMinutes(PenggunaService.DurasiKunci.TotalMinutes + 1);
                var berhasil = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.LoginSucceeded += () => berhasil.TrySetResult(true);
                vm.Password = "adminpusat11";
                vm.LoginCommand.Execute(null);
                Assert.True(await berhasil.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
            }
        }

        [Fact]
        public async Task KunciAkunPusat_MenetapSetelahRestartTanpaMenyimpanSandi()
        {
            var layanan = new LayananLoginPalsu();
            var pertama = new LoginViewModel(layanan, waktu: () => _sekarang);
            pertama.Username = "adminpusat";
            pertama.Password = "sandi-salah";

            for (int i = 1; i <= PenggunaService.MaksPercobaanGagal; i++)
            {
                pertama.ErrorMessage = string.Empty;
                pertama.LoginCommand.Execute(null);
                await TungguAsync(() =>
                    layanan.Panggilan == i && !string.IsNullOrEmpty(pertama.ErrorMessage));
            }

            string jalurPreferensi = Path.Combine(_folder, "prefs.json");
            string isi = File.ReadAllText(jalurPreferensi);
            Assert.Contains(AppPreferenceStore.KeyKunciPercobaanPusat, isi);
            Assert.DoesNotContain("sandi-salah", isi);

            // "Restart": instance baru membaca keadaan dari preferensi, bukan
            // dari memori instance lama, dan tetap menahan percobaan.
            var setelahRestart = new LayananLoginPalsu();
            var kedua = new LoginViewModel(setelahRestart, waktu: () => _sekarang);
            kedua.Username = "adminpusat";
            kedua.Password = "sandi-salah";
            kedua.LoginCommand.Execute(null);

            await TungguAsync(() =>
                kedua.ErrorMessage.StartsWith("Terlalu banyak", StringComparison.Ordinal));
            Assert.Equal(0, setelahRestart.Panggilan);
        }

        [Fact]
        public async Task SandiBerhurufBesar_BisaMasukLewatDatabaseSungguhan()
        {
            using var koneksi = new SqliteConnection("Data Source=:memory:");
            await koneksi.OpenAsync();
            var repo = new PenggunaRepository(koneksi, NullLogger<PenggunaRepository>.Instance);
            var layanan = new PenggunaService(repo, logger: NullLogger<PenggunaService>.Instance);

            await layanan.TambahAsync(
                "bendahara", "Bendahara Desa", PeranPengguna.Administrator,
                "Rahasia123", oleh: "uji", dariMigrasi: true);

            var vm = new LoginViewModel(layanan);
            var berhasil = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LoginSucceeded += () => berhasil.TrySetResult(true);
            vm.Username = "bendahara";
            vm.Password = "Rahasia123";
            vm.LoginCommand.Execute(null);

            Assert.True(await berhasil.Task.WaitAsync(TimeSpan.FromSeconds(5)), vm.ErrorMessage);
        }

        private static async Task TungguAsync(Func<bool> kondisi)
        {
            for (int i = 0; i < 200 && !kondisi(); i++)
            {
                await Task.Delay(10);
            }

            Assert.True(kondisi(), "Keadaan login tidak tercapai dalam batas waktu.");
        }

        private sealed class LayananLoginPalsu : IPenggunaService
        {
            public int Panggilan { get; private set; }

            public Task<bool> AdaPenggunaAsync(CancellationToken ct = default) => Task.FromResult(true);

            public Task<HasilMasukPengguna> MasukAsync(
                string username, string kataSandi, CancellationToken ct = default)
            {
                Panggilan++;
                return Task.FromResult(HasilMasukPengguna.Gagal("Nama pengguna atau kata sandi salah."));
            }

            public Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default) =>
                Task.FromResult(new List<Pengguna>());

            public Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default) =>
                Task.FromResult<Pengguna?>(null);

            public Task<Pengguna> TambahAsync(
                string username, string namaTampilan, string peran, string kataSandi,
                string? oleh = null, bool dariMigrasi = false, CancellationToken ct = default) =>
                Task.FromResult(new Pengguna { ID = 1, Username = username });

            public Task UbahAsync(
                int id, string namaTampilan, string peran, bool aktif,
                string? oleh = null, CancellationToken ct = default) => Task.CompletedTask;

            public Task ResetKataSandiAsync(
                int id, string kataSandiBaru, string? oleh = null, CancellationToken ct = default) =>
                Task.CompletedTask;

            public Task<bool> PasangKataSandiAwalAsync(
                int id, string kataSandi, CancellationToken ct = default) => Task.FromResult(true);

            public Task<bool> UbahKataSandiAsync(
                int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default) =>
                Task.FromResult(true);

            public Task HapusAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
