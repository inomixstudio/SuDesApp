using SuDesApp.Data.Models;
using Xunit;

namespace SuDesApp.Tests.Laporan
{
    /// <summary>
    /// Laporan penduduk kini punya tabel "Warga Menurut Pekerjaan". Uji memastikan
    /// tabelnya ikut tersusun, jumlah totalnya cocok, dan tidak muncul bila
    /// pekerjaan memang tidak ada satu pun.
    /// </summary>
    public sealed class LaporanPekerjaanTests
    {
        private static WargaStatistikBaris Baris(string kunci, int jumlah) =>
            new() { Kunci = kunci, Jumlah = jumlah };

        [Fact]
        public void TabelPekerjaanTersusunDenganJumlahDanTotal()
        {
            var ringkasan = new WargaStatistikRingkasan
            {
                TotalSeluruh = 7,
                PerPekerjaan = new[]
                {
                    Baris("Petani", 4),
                    Baris("Buruh harian lepas", 2),
                    Baris("(tidak diisi)", 1),
                },
            };

            var data = LaporanPendudukBuilder.Susun(ringkasan, new DesaData());

            var pekerjaan = data.Tabel[JenisTabelLaporan.Pekerjaan];
            Assert.Equal("Warga Menurut Pekerjaan", pekerjaan.Judul);
            Assert.Equal(3, pekerjaan.Baris.Count);
            // Urutan: jumlah terbanyak dulu, baris "(tidak diisi)" selalu di akhir.
            Assert.Equal("Petani", pekerjaan.Baris[0][0]);
            Assert.Equal("4", pekerjaan.Baris[0][1]);
            Assert.Equal("(tidak diisi)", pekerjaan.Baris[^1][0]);
            var barisTotal = pekerjaan.BarisTotal;
            Assert.NotNull(barisTotal);
            Assert.Equal("7", barisTotal![1]);
        }

        [Fact]
        public void TanpaPekerjaanTabelTidakDicetak()
        {
            var ringkasan = new WargaStatistikRingkasan
            {
                TotalSeluruh = 0,
                PerPekerjaan = Array.Empty<WargaStatistikBaris>(),
            };

            var data = LaporanPendudukBuilder.Susun(ringkasan, new DesaData());

            // Tabel pekerjaan kosong tidak masuk daftar tabel yang layak dicetak.
            Assert.DoesNotContain(
                data.TabelTerisi,
                t => t.Judul == "Warga Menurut Pekerjaan");
        }
    }
}
