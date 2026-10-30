using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace SuDesApp.Tests.Security
{
    /// <summary>
    /// Penegakan izin Core juga dipakai backend Api, yang melayani banyak
    /// pengguna dalam satu proses. Uji ini menjaga dua sifat yang paling mudah
    /// rusak tanpa terlihat: peran hanya berlaku di aliran permintaannya sendiri
    /// (tidak bocor ke permintaan lain yang berjalan bersamaan), dan setelah
    /// lingkup selesai nilainya kembali seperti semula.
    /// </summary>
    public class SessionContextTests
    {
        [Fact]
        public void Administrator_Boleh_KelolaPengguna()
        {
            using var _ = SessionContext.MulaiSesiPermintaan(
                "admin", "Admin Desa", PeranPengguna.Administrator);

            // Tidak melempar: Administrator memegang izin ini.
            SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

            Assert.True(SessionContext.Boleh(IzinAplikasi.KelolaPengguna));
            Assert.Equal(PeranPengguna.Administrator, SessionContext.Peran);
            Assert.Equal("admin", SessionContext.CurrentUser);
            Assert.Equal("Admin Desa", SessionContext.NamaPanggil);
            Assert.True(SessionContext.PenegakanAktif);
        }

        [Fact]
        public void Operator_Ditolak_Untuk_KelolaPengguna()
        {
            using var _ = SessionContext.MulaiSesiPermintaan(
                "operator", "Operator Desa", PeranPengguna.Operator);

            var ditolak = Assert.Throws<IzinDitolakException>(
                () => SessionContext.Wajib(IzinAplikasi.KelolaPengguna));

            Assert.Equal(IzinAplikasi.KelolaPengguna, ditolak.Izin);

            // Izin yang memang milik Operator tetap lolos — penolakan bukan
            // karena penegakan memblokir semua hal.
            SessionContext.Wajib(IzinAplikasi.BuatSurat);
            Assert.True(SessionContext.Boleh(IzinAplikasi.KelolaWarga));
            Assert.False(SessionContext.Boleh(IzinAplikasi.KelolaPengguna));
        }

        [Fact]
        public async Task Peran_Tidak_Bocor_Antar_Aliran()
        {
            var tugas = new List<Task>();

            for (var i = 0; i < 64; i++)
            {
                var administrator = i % 2 == 1;

                tugas.Add(Task.Run(async () =>
                {
                    using var _ = SessionContext.MulaiSesiPermintaan(
                        administrator ? "admin" : "operator",
                        administrator ? "Admin" : "Operator",
                        administrator ? PeranPengguna.Administrator : PeranPengguna.Operator);

                    // Titik jeda supaya aliran benar-benar saling berselang-seling.
                    await Task.Yield();

                    if (administrator)
                        SessionContext.Wajib(IzinAplikasi.KelolaPengguna);
                    else
                        Assert.Throws<IzinDitolakException>(
                            () => SessionContext.Wajib(IzinAplikasi.KelolaPengguna));

                    Assert.Equal(
                        administrator ? PeranPengguna.Administrator : PeranPengguna.Operator,
                        SessionContext.Peran);
                    Assert.Equal(administrator ? "admin" : "operator", SessionContext.CurrentUser);
                }));
            }

            await Task.WhenAll(tugas);
        }

        [Fact]
        public void Lingkup_Bersarang_Memulihkan_Lingkup_Luar()
        {
            using var luar = SessionContext.MulaiSesiPermintaan(
                "operator", "Operator Desa", PeranPengguna.Operator);

            using (SessionContext.MulaiSesiPermintaan(
                       "admin", "Admin Desa", PeranPengguna.Administrator))
            {
                Assert.Equal(PeranPengguna.Administrator, SessionContext.Peran);
                SessionContext.Wajib(IzinAplikasi.KelolaPengguna);
            }

            Assert.Equal(PeranPengguna.Operator, SessionContext.Peran);
            Assert.Equal("operator", SessionContext.CurrentUser);
            Assert.Throws<IzinDitolakException>(
                () => SessionContext.Wajib(IzinAplikasi.KelolaPengguna));
        }

        [Fact]
        public void Setelah_Lingkup_Selesai_Nilai_Global_Kembali()
        {
            var peranAwal = SessionContext.Peran;
            var penggunaAwal = SessionContext.CurrentUser;
            var tegakAwal = SessionContext.PenegakanAktif;

            using (SessionContext.MulaiSesiPermintaan(
                       "admin", "Admin Desa", PeranPengguna.Administrator))
            {
                Assert.Equal(PeranPengguna.Administrator, SessionContext.Peran);
            }

            Assert.Equal(peranAwal, SessionContext.Peran);
            Assert.Equal(penggunaAwal, SessionContext.CurrentUser);
            Assert.Equal(tegakAwal, SessionContext.PenegakanAktif);
        }
    }
}
