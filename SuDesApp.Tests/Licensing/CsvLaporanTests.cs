using System.Globalization;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

public sealed class CsvLaporanTests
{
    [Fact]
    public void Angka_dan_waktu_csv_tetap_kanonis_di_lintas_locale()
    {
        var budayaSebelumnya = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var berlaku = new DateTime(2026, 10, 22, 13, 14, 15, DateTimeKind.Utc);
            var desa = new RingkasPelanggan(
                "id", "KODE", "Desa Uji", 12, 3, "bulanan", berlaku);
            var pembayaran = new InfoPembayaran(
                "ORDER", 150_000, "paid", berlaku, "bulanan");

            var csvDesa = CsvLaporan.DaftarDesa([desa], berlaku);
            var csvPembayaran = CsvLaporan.Pembayaran([pembayaran]);

            Assert.Contains(",12,3,", csvDesa, StringComparison.Ordinal);
            Assert.Contains("2026-10-22T13:14:15.0000000Z", csvDesa, StringComparison.Ordinal);
            Assert.Contains(",150000,paid,2026-10-22T13:14:15.0000000Z,", csvPembayaran, StringComparison.Ordinal);
            Assert.Equal(
                "SUDES-DESA1234-20261022-abcdef",
                LayananTagihan.BuatOrderId("DESA123456", berlaku, "abcdef"));
        }
        finally
        {
            CultureInfo.CurrentCulture = budayaSebelumnya;
        }
    }
}
