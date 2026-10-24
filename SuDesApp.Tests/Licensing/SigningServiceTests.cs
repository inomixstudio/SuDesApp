using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

public sealed class SigningServiceTests
{
    [Fact]
    public void Token_dapat_diverifikasi_setelah_server_muat_ulang_kunci_yang_sama()
    {
        InDirectory(directory =>
        {
            var pertama = BuatLayanan(directory);
            var token = pertama.Tandatangani(KlaimContoh());
            var kunciPublik = pertama.KunciPublikB64Url;

            var setelahRestart = BuatLayanan(directory);

            Assert.Equal(kunciPublik, setelahRestart.KunciPublikB64Url);
            Assert.True(setelahRestart.CobaVerifikasi(token, kunciPublik, out var klaim));
            Assert.NotNull(klaim);
            Assert.Equal("SUDES-0001", klaim.Sub);
            Assert.Equal("Desa Sumberjaya", klaim.Desa);

            Assert.True(SuDesApp.Services.VerifikasiLisensi.CobaVerifikasi(
                token, kunciPublik, out var klaimKlien));
            Assert.NotNull(klaimKlien);
            Assert.Equal(klaim.Sub, klaimKlien.Sub);
        });
    }

    [Fact]
    public void Token_ditolak_jika_payload_diubah()
    {
        InDirectory(directory =>
        {
            var layanan = BuatLayanan(directory);
            var token = layanan.Tandatangani(KlaimContoh());
            var bagian = token.Split('.');
            bagian[1] = (bagian[1][0] == 'A' ? "B" : "A") + bagian[1][1..];

            Assert.False(layanan.CobaVerifikasi(string.Join('.', bagian), layanan.KunciPublikB64Url, out _));
        });
    }

    [Fact]
    public void Token_ditolak_dengan_kunci_publik_yang_berbeda()
    {
        InDirectory(directory =>
        {
            var layananPertama = BuatLayanan(Path.Combine(directory, "pertama"));
            var layananKedua = BuatLayanan(Path.Combine(directory, "kedua"));
            var token = layananPertama.Tandatangani(KlaimContoh());

            Assert.False(layananPertama.CobaVerifikasi(token, layananKedua.KunciPublikB64Url, out _));
        });
    }

    [Fact]
    public void Format_token_yang_tidak_dikenal_ditolak()
    {
        InDirectory(directory =>
        {
            var layanan = BuatLayanan(directory);
            var token = layanan.Tandatangani(KlaimContoh());

            Assert.False(layanan.CobaVerifikasi("v2" + token[2..], layanan.KunciPublikB64Url, out _));
            Assert.False(layanan.CobaVerifikasi("rusak", layanan.KunciPublikB64Url, out _));
        });
    }

    private static SigningService BuatLayanan(string directory)
    {
        Directory.CreateDirectory(directory);
        var konfigurasi = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lisensi:KeyPath"] = Path.Combine(directory, "kunci-penandatangan.key")
            })
            .Build();

        return new SigningService(konfigurasi, NullLogger<SigningService>.Instance, directory);
    }

    private static KlaimLisensi KlaimContoh() => new()
    {
        Sub = "SUDES-0001",
        Desa = "Desa Sumberjaya",
        Paket = "bulanan",
        Kode = "AKTIVASI-UJI",
        Iat = 1_800_000_000,
        Exp = 1_802_592_000,
        Cek = 1_800_000_000
    };

    private static void InDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sudes-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
