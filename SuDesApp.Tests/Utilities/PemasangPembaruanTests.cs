using System.Text;
using System.Text.RegularExpressions;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Pemasangan pembaruan tidak boleh menampilkan satu pun pertanyaan: installer
/// dijalankan senyap SETELAH aplikasi berakhir, lalu aplikasi dibuka lagi.
/// Skrip pembantu inilah kontraknya, jadi urutannya dikunci di sini.
/// </summary>
public sealed class PemasangPembaruanTests
{
    [Fact]
    public void ArgumenSenyap_TanpaWizard_TetapiProgresPemasanganTerlihat()
    {
        Assert.Contains("/SILENT", PemasangPembaruan.ArgumenSenyap);
        Assert.Contains("/SUPPRESSMSGBOXES", PemasangPembaruan.ArgumenSenyap);
        Assert.Contains("/CLOSEAPPLICATIONS", PemasangPembaruan.ArgumenSenyap);
        Assert.Contains("/NORESTART", PemasangPembaruan.ArgumenSenyap);

        // /VERYSILENT juga menyembunyikan jendela kemajuan — pengguna tidak akan
        // melihat "loading penginstalan" sama sekali, jadi sengaja tidak dipakai.
        Assert.DoesNotContain("/VERYSILENT", PemasangPembaruan.ArgumenSenyap);
    }

    [Fact]
    public void SusunSkrip_MenungguProsesLaluMemasang_TuntasSebelumMembukaAplikasi()
    {
        string skrip = PemasangPembaruan.SusunSkrip(
            4321,
            @"C:\unduhan\SuDesApp_2.9.0_Setup.exe",
            @"C:\Program\SuDesApp.exe",
            batasTungguDetik: 180);

        Assert.StartsWith("Wait-Process -Id 4321 -Timeout 180", skrip);
        Assert.Contains(@"-FilePath 'C:\unduhan\SuDesApp_2.9.0_Setup.exe'", skrip);
        Assert.Contains(
            "-ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS'",
            skrip);

        // Pemasangan harus selesai (-Wait) sebelum aplikasi dibuka kembali; kalau
        // tidak, aplikasi baru memegang berkas saat installer menimpanya.
        int pemasangan = skrip.IndexOf("-Wait", StringComparison.Ordinal);
        int bukaLagi = skrip.LastIndexOf(@"Start-Process -FilePath 'C:\Program\SuDesApp.exe'", StringComparison.Ordinal);
        Assert.True(pemasangan > 0);
        Assert.True(bukaLagi > pemasangan);
    }

    [Fact]
    public void SusunSkrip_TanpaJalurAplikasi_TidakMembukaApaPunSetelahMemasang()
    {
        string skrip = PemasangPembaruan.SusunSkrip(7, @"C:\setup.exe", jalurAplikasi: null, batasTungguDetik: 60);

        Assert.Single(Regex.Matches(skrip, "Start-Process"));
    }

    [Fact]
    public void SusunSkrip_JalurBertandaKutip_TetapSatuLiteralPowerShell()
    {
        string skrip = PemasangPembaruan.SusunSkrip(1, @"C:\O'brien\setup.exe", null, 5);

        Assert.Contains(@"'C:\O''brien\setup.exe'", skrip);
    }

    [Fact]
    public void SusunArgumen_BisaDidekodeKembaliDanTidakMenampilkanJendela()
    {
        const string skrip = "Wait-Process -Id 1";
        string argumen = PemasangPembaruan.SusunArgumen(skrip);

        Assert.Contains("-NoProfile", argumen);
        Assert.Contains("-NonInteractive", argumen);
        Assert.Contains("-WindowStyle Hidden", argumen);

        string terkode = argumen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.Equal(skrip, Encoding.Unicode.GetString(Convert.FromBase64String(terkode)));
    }

    [Fact]
    public void JalankanSetelahKeluar_InstallerTidakAda_TidakMenjalankanProses()
    {
        string hilang = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");

        Assert.False(PemasangPembaruan.JalankanSetelahKeluar(hilang));
    }
}
