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
    /// <summary>
    /// Kredensial akun bawaan bersifat TETAP: admin/admin dan
    /// adminpusat/adminpusat11. Uji ini menjaga agar pemasangan baru memakai
    /// nilai itu (bukan sandi acak), login tidak pernah merotasinya otomatis,
    /// dan instalasi lama yang terlanjur memakai sandi acak dipulihkan HANYA
    /// sekali — sesudah itu sandi yang diganti pengguna tidak disentuh lagi.
    /// </summary>
    [Collection("LisensiBerbagiBerkas")]
    public class AkunBawaanTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "sudes-akun-" + Guid.NewGuid().ToString("N"));

        public AkunBawaanTests()
        {
            Directory.CreateDirectory(_folder);

            // Preferensi & berkas akun diarahkan ke folder uji: penanda
            // "kredensial sudah disiapkan" tidak boleh mengotori profil mesin
            // yang menjalankan uji, dan tiap uji mulai dari keadaan bersih.
            AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "prefs.json");
            BerkasLogin.LokasiOverride = () => Path.Combine(_folder, BerkasLogin.NamaBerkas);
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            BerkasLogin.LokasiOverride = null;
            AkunPusat.LokasiBerkasSandiOverride = null;

            try { Directory.Delete(_folder, recursive: true); } catch { /* sisa berkas uji */ }
        }

        private string JalurLogin => Path.Combine(_folder, BerkasLogin.NamaBerkas);

        [Fact]
        public void PemasanganBaru_MemakaiKredensialTetapAdminAdmin()
        {
            _ = new LoginViewModel(new LayananPenggunaPalsu());

            var akun = BerkasLogin.Cari(JalurLogin, LoginViewModel.UsernameAdminBawaan);

            Assert.NotNull(akun);
            Assert.True(BerkasLogin.Cocok(akun!, LoginViewModel.SandiAdminBawaan));
            Assert.False(BerkasLogin.Cocok(akun!, "admin-lama-yang-berbeda"));
        }

        [Fact]
        public async Task PemasanganBaru_MenyemaiAdminBawaanTanpaMembuatSandiAcak()
        {
            var layanan = new LayananPenggunaPalsu();
            var vm = new LoginViewModel(layanan);

            await vm.InisialisasiAsync();

            Assert.Equal(LoginViewModel.SandiAdminBawaan, layanan.SandiDibuat);
            Assert.DoesNotContain(BerkasLogin.Baca(JalurLogin), a => a.Pusat);
            Assert.True(AppPreferenceStore.SudahDisiapkanKredensialBawaan());
        }

        [Fact]
        public async Task InstalasiLama_KredensialBawaanDipulihkanSekaliSaja()
        {
            var layanan = new LayananPenggunaPalsu
            {
                AdaPengguna = true,
                Admin = new Pengguna
                {
                    ID = 7,
                    Username = LoginViewModel.UsernameAdminBawaan,
                    NamaTampilan = "Administrator",
                    Peran = PeranPengguna.Administrator,
                    Aktif = true
                }
            };

            var pertama = new LoginViewModel(layanan);
            await pertama.InisialisasiAsync();

            Assert.Equal((7, LoginViewModel.SandiAdminBawaan), layanan.SandiAwalDipasang);

            // Pembukaan berikutnya TIDAK boleh menimpa lagi: sandi yang sudah
            // diganti pengguna setelah pemulihan ini harus bertahan.
            layanan.SandiAwalDipasang = default;
            var kedua = new LoginViewModel(layanan);
            await kedua.InisialisasiAsync();

            Assert.Equal(default, layanan.SandiAwalDipasang);
        }

        [Fact]
        public void AkunPusat_MemakaiSandiBawaanTanpaProvisioning()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, null);
                AkunPusat.LokasiBerkasSandiOverride = () => Path.Combine(_folder, "tidak-ada.kunci");

                Assert.Equal("adminpusat11", AkunPusat.SandiBawaan);
                Assert.True(AkunPusat.SandiTersedia);
                Assert.True(AkunPusat.CocokSandi("adminpusat11"));
                Assert.True(AkunPusat.CocokSandi("ADMINPUSAT11"));
                Assert.False(AkunPusat.CocokSandi("sandi-lain"));
                Assert.False(AkunPusat.CocokSandi(string.Empty));
            }
            finally
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
            }
        }

        [Fact]
        public void AkunPusat_ProvisioningMenimpaSandiBawaan()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, "sandi-provisioning-uji");

                Assert.True(AkunPusat.CocokSandi("SANDI-PROVISIONING-UJI"));
                Assert.False(AkunPusat.CocokSandi("adminpusat11"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
            }
        }

        [Fact]
        public void AkunPusatDariBerkasProvisioning_MenimpaSandiBawaan()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            string jalur = Path.Combine(_folder, "Pusat.kunci");

            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, null);
                AkunPusat.LokasiBerkasSandiOverride = () => jalur;

                File.WriteAllText(jalur, "kunci-berkas-uji\n");

                Assert.True(AkunPusat.CocokSandi("kunci-berkas-uji"));
                Assert.False(AkunPusat.CocokSandi("adminpusat11"));
            }
            finally
            {
                AkunPusat.LokasiBerkasSandiOverride = null;
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
            }
        }

        [Fact]
        public void AkunPusatModelLama_DihapusDariBerkasAkun()
        {
            string garam = BerkasLogin.BuatGaram();
            BerkasLogin.Tulis(JalurLogin, new[]
            {
                new AkunLogin("admin", garam, BerkasLogin.HitungHash("admin", garam), false),
                new AkunLogin(
                    AkunPusat.Username, garam, BerkasLogin.HitungHash("adminpusat11", garam), true)
            });

            _ = new LoginViewModel(new LayananPenggunaPalsu());

            var akun = BerkasLogin.Baca(JalurLogin);
            Assert.DoesNotContain(akun, a => a.Pusat);
            Assert.Contains(akun, a => a.Username == "admin");
        }

        [Fact]
        public async Task AdminBawaan_BisaMasukLewatDatabaseSungguhan()
        {
            SessionContext.TandaiSesiPusat(false);

            using var koneksi = new SqliteConnection("Data Source=:memory:");
            await koneksi.OpenAsync();
            var repo = new PenggunaRepository(koneksi, NullLogger<PenggunaRepository>.Instance);
            var layanan = new PenggunaService(repo, logger: NullLogger<PenggunaService>.Instance);

            var vm = new LoginViewModel(layanan);
            await vm.InisialisasiAsync();

            var hasil = await layanan.MasukAsync(
                LoginViewModel.UsernameAdminBawaan, LoginViewModel.SandiAdminBawaan);
            Assert.True(hasil.Berhasil, hasil.Pesan);

            // Juga lewat jalur layar login (input dinormalkan ke huruf kecil).
            var berhasil = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LoginSucceeded += () => berhasil.TrySetResult(true);
            vm.Username = "ADMIN";
            vm.Password = "admin";
            vm.LoginCommand.Execute(null);

            Assert.True(await berhasil.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
            Assert.False(SessionContext.SesiPusat);
        }

        [Fact]
        public async Task AkunPusat_BisaMasukDenganSandiBawaan()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            SessionContext.TandaiSesiPusat(false);

            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, null);
                AkunPusat.LokasiBerkasSandiOverride = () => Path.Combine(_folder, "tidak-ada.kunci");

                using var koneksi = new SqliteConnection("Data Source=:memory:");
                await koneksi.OpenAsync();
                var repo = new PenggunaRepository(koneksi, NullLogger<PenggunaRepository>.Instance);
                var layanan = new PenggunaService(repo, logger: NullLogger<PenggunaService>.Instance);

                var vm = new LoginViewModel(layanan);
                await vm.InisialisasiAsync();

                var berhasil = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.LoginSucceeded += () => berhasil.TrySetResult(true);
                vm.Username = "adminpusat";
                vm.Password = "adminpusat11";
                vm.LoginCommand.Execute(null);

                Assert.True(await berhasil.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.True(AkunPusat.SesiIniPusat);
                Assert.Equal("adminpusat", LoginViewModel.CurrentUserName);
            }
            finally
            {
                AkunPusat.LokasiBerkasSandiOverride = null;
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
                SessionContext.TandaiSesiPusat(false);
            }
        }

        /// <summary>Layanan pengguna minimal untuk menguji penyemaian akun awal.</summary>
        private sealed class LayananPenggunaPalsu : IPenggunaService
        {
            public string? SandiDibuat { get; private set; }

            public bool AdaPengguna { get; init; }

            public Pengguna? Admin { get; init; }

            public (int Id, string Sandi)? SandiAwalDipasang { get; set; }

            public Task<bool> AdaPenggunaAsync(CancellationToken ct = default) => Task.FromResult(AdaPengguna);

            public Task<HasilMasukPengguna> MasukAsync(
                string username, string kataSandi, CancellationToken ct = default) =>
                Task.FromResult(HasilMasukPengguna.Gagal("tidak dipakai dalam uji ini"));

            public Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default) =>
                Task.FromResult(new List<Pengguna>());

            public Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default) =>
                Task.FromResult(Admin);

            public Task<Pengguna> TambahAsync(
                string username, string namaTampilan, string peran, string kataSandi,
                string? oleh = null, bool dariMigrasi = false, CancellationToken ct = default)
            {
                SandiDibuat = kataSandi;
                return Task.FromResult(new Pengguna
                {
                    ID = 1,
                    Username = username,
                    NamaTampilan = namaTampilan,
                    Peran = peran
                });
            }

            public Task UbahAsync(
                int id, string namaTampilan, string peran, bool aktif,
                string? oleh = null, CancellationToken ct = default) => Task.CompletedTask;

            public Task ResetKataSandiAsync(
                int id, string kataSandiBaru, string? oleh = null, CancellationToken ct = default) =>
                Task.CompletedTask;

            public Task<bool> PasangKataSandiAwalAsync(
                int id, string kataSandi, CancellationToken ct = default)
            {
                SandiAwalDipasang = (id, kataSandi);
                return Task.FromResult(true);
            }

            public Task<bool> UbahKataSandiAsync(
                int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default) =>
                Task.FromResult(true);

            public Task HapusAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
