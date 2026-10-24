using SuDesApp.Services;
using SuDesApp.Wpf.ViewModels;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SuDesApp.Tests.Apbdes
{
    /// <summary>
    /// Jalur utama panel Riwayat APBDES: menarik anggaran yang sudah
    /// dipublikasikan pemerintah pusat (Prodeskel Kemendagri). Parser harus
    /// menolak halaman yang tidak dikenali — satu angka salah baca berarti
    /// anggaran desa yang salah di layar — lalu pemetaan ke rangkuman tahunan
    /// memakai jalur yang sama dengan impor Excel.
    /// </summary>
    public class ApbdesProdeskelTests
    {
        private const string HalamanLaporan =
            "<html><body><table>" +
            "<tr><td>DAFTAR PENERIMAAN DAN PENGGUNAAN APBDES TAHUN 2024</td></tr>" +
            "<tr><td>Pendapatan Desa</td><td>Rp 1.234.567.890,00</td><td>Rp 1.100.000.000,00</td></tr>" +
            "<tr><td>Pendapatan Transfer</td><td>Rp 500.000.000,00</td></tr>" +
            "<tr><td>Belanja Desa</td><td>Rp 1.200.000.000,00</td><td>Rp 1.050.000.000,00</td></tr>" +
            "<tr><td>Belanja Pegawai</td><td>Rp 300.000.000,00</td></tr>" +
            "<tr><td>Pembiayaan Neto</td><td>Rp 34.567.890,00</td><td>Rp 50.000.000,00</td></tr>" +
            "<tr><td>Dana Desa</td><td>Rp 900.000.000,00</td></tr>" +
            "</table></body></html>";

        [Fact]
        public void Parser_HalamanLaporan_TerdapatSeluruhMetrik()
        {
            var muatan = ApbdesProdeskelBaca.Urai(HalamanLaporan, 2024);

            Assert.Equal(StatusMuatApbdesPusat.Berhasil, muatan.Status);
            var h = Assert.IsType<HasilApbdesProdeskel>(muatan.Hasil);
            Assert.Equal(2024, h.Tahun);
            Assert.Equal(1_234_567_890, h.AngPendapatan);
            Assert.Equal(1_100_000_000, h.RealPendapatan);
            Assert.Equal(1_200_000_000, h.AngBelanja);
            Assert.Equal(1_050_000_000, h.RealBelanja);
            Assert.Equal(34_567_890, h.AngPembiayaan);
            Assert.Equal(50_000_000, h.RealPembiayaan);
            Assert.Equal(900_000_000, h.DanaDesa);
            Assert.Equal(4, h.MetrikTerisi);

            // Baris rincian tidak boleh menimpa ringkasan.
            Assert.NotEqual(500_000_000, h.AngPendapatan);
            Assert.NotEqual(300_000_000, h.AngBelanja);
        }

        [Fact]
        public void Parser_HalamanTanpaLabel_TolakFormat()
        {
            var muatan = ApbdesProdeskelBaca.Urai(
                "<html><body><h1>Pemeliharaan</h1><p>Silakan kembali lagi.</p></body></html>", 2024);

            Assert.Equal(StatusMuatApbdesPusat.FormatTidakDikenal, muatan.Status);
            Assert.Null(muatan.Hasil);
            Assert.False(string.IsNullOrWhiteSpace(muatan.Pesan));
        }

        [Fact]
        public void Parser_AngkaTanpaLabel_TidakDianggapAnggaran()
        {
            // Angka besar tanpa label metrik: tidak boleh dipetakan ke pendapatan/belanja.
            var muatan = ApbdesProdeskelBaca.Urai(
                "<html><body><p>Statistik pengunjung: 9.876.543 kali</p></body></html>", 2024);

            Assert.Equal(StatusMuatApbdesPusat.FormatTidakDikenal, muatan.Status);
            Assert.Null(muatan.Hasil);
        }

        [Fact]
        public void Parser_TandaTidakDitemukan_JadiStatusKosong()
        {
            var muatan = ApbdesProdeskelBaca.Urai(
                "<html><body><p>Data tidak ditemukan untuk kode desa tersebut.</p></body></html>", 2024);

            Assert.Equal(StatusMuatApbdesPusat.TidakDitemukan, muatan.Status);
            Assert.Null(muatan.Hasil);
            Assert.Contains("2024", muatan.Pesan);
        }

        [Fact]
        public void Parser_HanyaBarisRincian_TolakBukanTotal()
        {
            // Tanpa baris total "Pendapatan/Belanja Desa", baris rincian
            // (transfer, pegawai) tidak boleh dijadikan ringkasan.
            var muatan = ApbdesProdeskelBaca.Urai(
                "<table>" +
                "<tr><td>Pendapatan Transfer</td><td>Rp 500.000.000,00</td></tr>" +
                "<tr><td>Belanja Pegawai</td><td>Rp 300.000.000,00</td></tr>" +
                "</table>", 2024);

            Assert.Equal(StatusMuatApbdesPusat.FormatTidakDikenal, muatan.Status);
            Assert.Null(muatan.Hasil);
        }

        [Fact]
        public void Parser_BarisRealisasiMurni_TanpaAnggaran()
        {
            var muatan = ApbdesProdeskelBaca.Urai(
                "<table>" +
                "<tr><td>Pendapatan Desa</td><td>Rp 1.000.000.000,00</td><td>Rp 900.000.000,00</td></tr>" +
                "<tr><td>Belanja Desa</td><td>Rp 800.000.000,00</td><td>Rp 700.000.000,00</td></tr>" +
                "<tr><td>Realisasi Pembiayaan Neto</td><td>Rp 100.000.000,00</td></tr>" +
                "</table>", 2024);

            Assert.Equal(StatusMuatApbdesPusat.Berhasil, muatan.Status);
            var h = Assert.IsType<HasilApbdesProdeskel>(muatan.Hasil);
            Assert.Null(h.AngPembiayaan);
            Assert.Equal(100_000_000, h.RealPembiayaan);
        }

        [Fact]
        public void RangkaiPusat_PetakanKeRangkumanTahunan()
        {
            var hasil = ApbdesProdeskelBaca.Urai(HalamanLaporan, 2024).Hasil!;
            var r = ApbdesViewModel.RangkaiPusat(hasil, "Desa Uji");

            Assert.True(r.Ada);
            Assert.Equal(2024, r.Tahun);
            Assert.Equal("Desa Uji", r.NamaDesa);
            Assert.Equal(1_234_567_890, r.AngPendapatan);
            Assert.Equal(1_100_000_000, r.RealPendapatan);
            Assert.Equal(1_200_000_000, r.AngBelanja);
            Assert.Equal(1_050_000_000, r.RealBelanja);
            Assert.Equal(34_567_890, r.AngPembiayaan);
            Assert.Equal(50_000_000, r.RealPembiayaan);
            Assert.Equal(900_000_000, r.DanaDesa);
        }

        [Fact]
        public async Task Klien_MengirimKodeDanTahunLaluMenguraikanJawaban()
        {
            var handler = new HalamanPalsu(HalamanLaporan);
            var klien = new ApbdesProdeskelKlien(new HttpClient(handler));

            var muatan = await klien.MuatAsync("32.15.02.2001", 2024);

            Assert.NotNull(handler.UrlTerakhir);
            Assert.Contains("kodesa=3215022001", handler.UrlTerakhir);
            Assert.Contains("tahun=2024", handler.UrlTerakhir);
            Assert.Equal(StatusMuatApbdesPusat.Berhasil, muatan.Status);
            Assert.Equal(1_234_567_890, muatan.Hasil!.AngPendapatan);
        }

        [Fact]
        public async Task Klien_KodeDesaBelumLengkap_TolakTanpaJaringan()
        {
            var handler = new HalamanPalsu(HalamanLaporan);
            var klien = new ApbdesProdeskelKlien(new HttpClient(handler));

            var muatan = await klien.MuatAsync("12", 2024);

            Assert.Equal(StatusMuatApbdesPusat.Gagal, muatan.Status);
            Assert.Contains("Kode desa belum lengkap", muatan.Pesan);
            Assert.Null(handler.UrlTerakhir); // tidak ada permintaan keluar
        }

        private sealed class HalamanPalsu : HttpMessageHandler
        {
            private readonly string _html;
            public string? UrlTerakhir { get; private set; }

            public HalamanPalsu(string html) => _html = html;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                UrlTerakhir = request.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_html, Encoding.UTF8, "text/html"),
                });
            }
        }
    }
}
