using System;
using System.Linq;
using System.Threading.Tasks;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>Uji hashing kata sandi: hasil tidak pernah menyimpan kata sandi apa adanya.</summary>
    public class KataSandiPenggunaTests
    {
        [Fact]
        public void Hash_TidakPernahSamaDenganKataSandi()
        {
            var salt = KataSandiPengguna.BuatSalt();
            var hash = KataSandiPengguna.HitungHash("rahasia-desa", salt);

            Assert.NotEqual("rahasia-desa", hash);
            Assert.NotEqual(salt, hash);
            Assert.True(KataSandiPengguna.Cocok("rahasia-desa", salt, hash, KataSandiPengguna.IterasiBawaan));
        }

        [Fact]
        public void Salt_Berbeda_TiapKali()
        {
            var semuanya = Enumerable.Range(0, 20).Select(_ => KataSandiPengguna.BuatSalt()).ToHashSet();
            Assert.Equal(20, semuanya.Count);
        }

        [Fact]
        public void KataSandiSalah_TidakCocok()
        {
            var salt = KataSandiPengguna.BuatSalt();
            var hash = KataSandiPengguna.HitungHash("rahasia-desa", salt);

            Assert.False(KataSandiPengguna.Cocok("rahasia-des", salt, hash, KataSandiPengguna.IterasiBawaan));
            Assert.False(KataSandiPengguna.Cocok("RAHASIA-DESA", salt, hash, KataSandiPengguna.IterasiBawaan));
            Assert.False(KataSandiPengguna.Cocok("", salt, hash, KataSandiPengguna.IterasiBawaan));
            Assert.False(KataSandiPengguna.Cocok("rahasia-desa", salt, null!, KataSandiPengguna.IterasiBawaan));
        }

        [Fact]
        public void Validasi_MenolakKataSandiPendekDanSamaDenganUsername()
        {
            Assert.NotEmpty(KataSandiPengguna.Validasi("pendek"));
            Assert.NotEmpty(KataSandiPengguna.Validasi("operator", "operator"));
            Assert.Empty(KataSandiPengguna.Validasi("kata-sandi-baru", "operator"));
        }

        [Fact]
        public void IterasiBawaan_TidakTerlaluRendah()
        {
            Assert.True(KataSandiPengguna.IterasiBawaan >= 100_000,
                "Putaran PBKDF2 minimal 100.000 supaya menebak kata sandi tetap mahal.");
        }
    }

    /// <summary>Uji matriks peran → izin: aturan siapa-boleh-apa hanya ada di satu tempat.</summary>
    public class HakAksesTests
    {
        [Fact]
        public void Administrator_BolehSemuaIzin()
        {
            foreach (var izin in Enum.GetValues<IzinAplikasi>())
                Assert.True(HakAkses.Boleh(PeranPengguna.Administrator, izin), $"Administrator ditolak {izin}.");
        }

        [Fact]
        public void Operator_TidakBolehKelolaPenggunaDanTandaTangan()
        {
            Assert.False(HakAkses.Boleh(PeranPengguna.Operator, IzinAplikasi.KelolaPengguna));
            Assert.False(HakAkses.Boleh(PeranPengguna.Operator, IzinAplikasi.TandaTanganSurat));
            Assert.True(HakAkses.Boleh(PeranPengguna.Operator, IzinAplikasi.BuatSurat));
        }

        [Fact]
        public void Sekdes_BolehPengaturan_TapiTidakKelolaPengguna()
        {
            Assert.True(HakAkses.Boleh(PeranPengguna.Sekdes, IzinAplikasi.PengaturanAplikasi));
            Assert.True(HakAkses.Boleh(PeranPengguna.Sekdes, IzinAplikasi.TandaTanganSurat));
            Assert.False(HakAkses.Boleh(PeranPengguna.Sekdes, IzinAplikasi.KelolaPengguna));
        }

        [Fact]
        public void Kades_DanAuditor_TidakMembuatSurat()
        {
            foreach (var peran in new[] { PeranPengguna.Kades, PeranPengguna.Auditor })
            {
                Assert.False(HakAkses.Boleh(peran, IzinAplikasi.BuatSurat));
                Assert.False(HakAkses.Boleh(peran, IzinAplikasi.KelolaWarga));
                Assert.True(HakAkses.Boleh(peran, IzinAplikasi.VerifikasiSurat));
                Assert.True(HakAkses.Boleh(peran, IzinAplikasi.Laporan));
            }
        }

        [Fact]
        public void PeranTidakDikenal_DianggapOperator()
        {
            Assert.Equal(PeranPengguna.Operator, PeranPengguna.Normalisasi("raja"));
            Assert.True(HakAkses.Boleh("raja", IzinAplikasi.BuatSurat));
            Assert.False(HakAkses.Boleh("raja", IzinAplikasi.KelolaPengguna));
        }
    }

    /// <summary>
    /// Uji layanan pengguna di skema SQLite asli: pembuatan akun, masuk,
    /// penguncian setelah percobaan gagal, dan perlindungan administrator terakhir.
    /// </summary>
    public class PenggunaServiceTests : IClassFixture<CoreTestFixture>
    {
        private readonly CoreTestFixture _fixture;
        private DateTime _sekarang = new(2026, 9, 27, 9, 0, 0);

        public PenggunaServiceTests(CoreTestFixture fixture) => _fixture = fixture;

        private PenggunaService Service() =>
            new(new PenggunaRepository(_fixture.Connection, Microsoft.Extensions.Logging.Abstractions.NullLogger<PenggunaRepository>.Instance),
                null, null, () => _sekarang);

        [Fact]
        public async Task Tambah_LaluMasuk_Berhasil()
        {
            var svc = Service();
            await svc.TambahAsync("operator1", "Budi Santoso", PeranPengguna.Operator, "kata-sandi-1", "admin");

            var hasil = await svc.MasukAsync("operator1", "kata-sandi-1");

            Assert.True(hasil.Berhasil);
            Assert.Equal(PeranPengguna.Operator, hasil.Pengguna!.Peran);
            Assert.Equal("Budi Santoso", hasil.Pengguna.NamaTampilan);
        }

        [Fact]
        public async Task Username_TidakBolehGanda_MeskiBedaHurufBesar()
        {
            var svc = Service();
            await svc.TambahAsync("operator2", "Orang Dua", PeranPengguna.Operator, "kata-sandi-2");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.TambahAsync("Operator2", "Orang Lain", PeranPengguna.Operator, "kata-sandi-3"));

            Assert.Contains("sudah dipakai", ex.Message);
        }

        [Fact]
        public async Task Masuk_KataSandiSalah_TigaKali_AkunTerkunciLimaMenit()
        {
            var svc = Service();
            await svc.TambahAsync("operator3", "Orang Tiga", PeranPengguna.Operator, "kata-sandi-3");

            for (int i = 1; i <= 2; i++)
            {
                var gagal = await svc.MasukAsync("operator3", "salah");
                Assert.False(gagal.Berhasil);
                Assert.False(gagal.Terkunci);
            }

            var ketiga = await svc.MasukAsync("operator3", "salah");
            Assert.False(ketiga.Berhasil);
            Assert.True(ketiga.Terkunci);

            // Walau kata sandinya benar, akun tetap terkunci.
            var saatTerkunci = await svc.MasukAsync("operator3", "kata-sandi-3");
            Assert.False(saatTerkunci.Berhasil);
            Assert.True(saatTerkunci.Terkunci);

            // Kunci tersimpan di database: service baru tetap melihatnya.
            _sekarang = _sekarang.AddMinutes(6);
            var setelahLewat = await Service().MasukAsync("operator3", "kata-sandi-3");
            Assert.True(setelahLewat.Berhasil);
        }

        [Fact]
        public async Task AkunNonaktif_TidakBisaMasuk()
        {
            var svc = Service();
            var akun = await svc.TambahAsync("operator4", "Orang Empat", PeranPengguna.Operator, "kata-sandi-4");
            await svc.UbahAsync(akun.ID, akun.NamaTampilan, akun.Peran, aktif: false);

            var hasil = await svc.MasukAsync("operator4", "kata-sandi-4");

            Assert.False(hasil.Berhasil);
            Assert.Contains("dinonaktifkan", hasil.Pesan);
        }

        [Fact]
        public async Task AdministratorTerakhir_TidakBolehDiturunkanAtauDihapus()
        {
            var svc = Service();
            var admin = await svc.TambahAsync("admin-uji", "Admin Uji", PeranPengguna.Administrator, "kata-sandi-admin");

            // Tidak ada administrator aktif lain di database test.
            var turunkan = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.UbahAsync(admin.ID, admin.NamaTampilan, PeranPengguna.Operator, true));
            Assert.Contains("satu-satunya akun Administrator", turunkan.Message);

            var hapus = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.HapusAsync(admin.ID));
            Assert.Contains("Administrator", hapus.Message);
        }

        [Fact]
        public async Task UbahKataSandi_MemeriksaKataSandiLama()
        {
            var svc = Service();
            var akun = await svc.TambahAsync("operator5", "Orang Lima", PeranPengguna.Operator, "kata-sandi-5");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.UbahKataSandiAsync(akun.ID, "salah", "kata-sandi-baru"));

            Assert.True(await svc.UbahKataSandiAsync(akun.ID, "kata-sandi-5", "kata-sandi-baru"));
            Assert.True((await svc.MasukAsync("operator5", "kata-sandi-baru")).Berhasil);
            Assert.False((await svc.MasukAsync("operator5", "kata-sandi-5")).Berhasil);
        }

        [Fact]
        public async Task KataSandiPendek_DitolakSejakAwal()
        {
            var svc = Service();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.TambahAsync("operator6", "Orang Enam", PeranPengguna.Operator, "pendek"));
        }

        [Fact]
        public async Task UsernameTidakSah_Ditolak()
        {
            var svc = Service();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.TambahAsync("ab", "Terlalu Pendek", PeranPengguna.Operator, "kata-sandi-7"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.TambahAsync("spasi salah", "Ada Spasi", PeranPengguna.Operator, "kata-sandi-7"));
        }

        [Fact]
        public async Task ResetKataSandi_MenghapusKunciPercobaanGagal()
        {
            var svc = Service();
            var akun = await svc.TambahAsync("operator7", "Orang Tujuh", PeranPengguna.Operator, "kata-sandi-7");

            await svc.MasukAsync("operator7", "salah");
            await svc.MasukAsync("operator7", "salah");
            await svc.MasukAsync("operator7", "salah");
            Assert.True((await svc.MasukAsync("operator7", "kata-sandi-7")).Terkunci);

            await svc.ResetKataSandiAsync(akun.ID, "kata-sandi-baru", "admin-uji");

            var hasil = await svc.MasukAsync("operator7", "kata-sandi-baru");
            Assert.True(hasil.Berhasil);
        }
    }
}
