using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Kredensial akun bawaan tidak boleh lagi berupa sandi tetap yang sama di
    /// semua instalasi. Uji ini menjaga generator sandi acak dan pembacaan sandi
    /// provisioning akun pusat tetap berperilaku seperti yang didokumentasikan.
    /// </summary>
    public class AkunBawaanTests
    {
        private const string Alfabet = "abcdefghjkmnpqrstuvwxyz23456789";

        [Fact]
        public void SandiAcak_MemakaiAlfabetAmanDanPanjangYangDiminta()
        {
            var sandi = SandiAcak.Buat();

            Assert.Equal(19, sandi.Length); // 16 karakter + 3 tanda hubung
            Assert.Matches("^[a-z0-9]{4}(-[a-z0-9]{4}){3}$", sandi);
            Assert.All(sandi.Replace("-", string.Empty), huruf => Assert.Contains(huruf, Alfabet));
        }

        [Fact]
        public void SandiAcak_BerbedaSetiapPemanggilan()
        {
            var kumpulan = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < 24; i++)
            {
                Assert.True(kumpulan.Add(SandiAcak.Buat()));
            }
        }

        [Fact]
        public void SandiAcak_MenolakPanjangDiBawahDelapan()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SandiAcak.Buat(7));
        }

        [Fact]
        public void AkunPusat_MembacaSandiDariEnvironmentVariable()
        {
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);
            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, "sandi-provisioning-uji");

                Assert.True(AkunPusat.SandiTersedia);
                Assert.True(AkunPusat.CocokSandi("SANDI-PROVISIONING-UJI"));
                Assert.False(AkunPusat.CocokSandi("sandi-lain"));
                Assert.False(AkunPusat.CocokSandi(string.Empty));
            }
            finally
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
            }
        }

        [Fact]
        public async Task PemasanganBaru_MemakaiSandiAcakDanTidakMenyemaiAkunPusat()
        {
            string folder = Path.Combine(Path.GetTempPath(), "sudes-akun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string jalur = Path.Combine(folder, BerkasLogin.NamaBerkas);
            BerkasLogin.LokasiOverride = () => jalur;

            try
            {
                var layanan = new LayananPenggunaPalsu();
                var vm = new LoginViewModel(layanan);

                var akun = BerkasLogin.Cari(jalur, "admin");
                Assert.NotNull(akun);
                Assert.False(BerkasLogin.Cocok(akun!, "admin"));
                Assert.DoesNotContain(BerkasLogin.Baca(jalur), a => a.Pusat);

                await vm.InisialisasiAsync();

                Assert.False(string.IsNullOrEmpty(layanan.SandiDibuat));
                Assert.True(BerkasLogin.Cocok(akun!, layanan.SandiDibuat!));
                Assert.True(vm.HasInfoKredensial);
                Assert.Contains(layanan.SandiDibuat!, vm.InfoKredensialAwal);
            }
            finally
            {
                BerkasLogin.LokasiOverride = null;
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [Fact]
        public void AkunPusatModelLama_DihapusDariBerkasAkun()
        {
            string folder = Path.Combine(Path.GetTempPath(), "sudes-akun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string jalur = Path.Combine(folder, BerkasLogin.NamaBerkas);
            BerkasLogin.LokasiOverride = () => jalur;

            try
            {
                string garam = BerkasLogin.BuatGaram();
                BerkasLogin.Tulis(jalur, new[]
                {
                    new AkunLogin("admin", garam, BerkasLogin.HitungHash("admin", garam), false),
                    new AkunLogin(
                        AkunPusat.Username, garam, BerkasLogin.HitungHash("adminpusat11", garam), true)
                });

                _ = new LoginViewModel(new LayananPenggunaPalsu());

                var akun = BerkasLogin.Baca(jalur);
                Assert.DoesNotContain(akun, a => a.Pusat);
                Assert.Contains(akun, a => a.Username == "admin");
            }
            finally
            {
                BerkasLogin.LokasiOverride = null;
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [Fact]
        public void AkunPusat_TanpaProvisioning_TidakBisaDipakai()
        {
            string jalur = Path.Combine(
                Path.GetTempPath(), "sudes-pusat-" + Guid.NewGuid().ToString("N") + ".kunci");
            string? lama = Environment.GetEnvironmentVariable(AkunPusat.VariabelSandi);

            try
            {
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, null);
                AkunPusat.LokasiBerkasSandiOverride = () => jalur;

                Assert.False(AkunPusat.SandiTersedia);
                Assert.False(AkunPusat.CocokSandi("apa-saja"));

                File.WriteAllText(jalur, "kunci-berkas-uji\n");
                Assert.True(AkunPusat.SandiTersedia);
                Assert.True(AkunPusat.CocokSandi("kunci-berkas-uji"));

                File.Delete(jalur);
                Assert.False(AkunPusat.SandiTersedia);
            }
            finally
            {
                AkunPusat.LokasiBerkasSandiOverride = null;
                Environment.SetEnvironmentVariable(AkunPusat.VariabelSandi, lama);
                if (File.Exists(jalur))
                {
                    File.Delete(jalur);
                }
            }
        }

        /// <summary>Layanan pengguna minimal untuk menguji penyemaian akun awal.</summary>
        private sealed class LayananPenggunaPalsu : IPenggunaService
        {
            public string? SandiDibuat { get; private set; }

            public Task<bool> AdaPenggunaAsync(CancellationToken ct = default) => Task.FromResult(false);

            public Task<HasilMasukPengguna> MasukAsync(
                string username, string kataSandi, CancellationToken ct = default) =>
                Task.FromResult(HasilMasukPengguna.Gagal("tidak dipakai dalam uji ini"));

            public Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default) =>
                Task.FromResult(new List<Pengguna>());

            public Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default) =>
                Task.FromResult<Pengguna?>(null);

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

            public Task<bool> UbahKataSandiAsync(
                int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default) =>
                Task.FromResult(true);

            public Task HapusAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
