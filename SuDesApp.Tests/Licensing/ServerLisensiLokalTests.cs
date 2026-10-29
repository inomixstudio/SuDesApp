using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Server lisensi lokal hidup dan mati bersama aplikasi. Dua hal yang dijaga
/// di sini adalah bagian yang tidak bisa dilihat dari UI:
///
/// 1. <b>Pembersihan sisa proses</b> — aplikasi yang mati mendadak meninggalkan
///    server yang masih memegang port 5110. Pembukaan berikutnya harus
///    mematikannya, TETAPI hanya proses yang benar-benar cocok dengan penanda
///    (pid + waktu mulai): pid yang sudah dipakai ulang oleh program lain tidak
///    boleh ikut dibunuh.
/// 2. <b>Penentuan berkas yang dijalankan</b> — exe hasil publish, dll, atau
///    proyek .csproj saat pengembangan. Salah menentukan di sini berarti server
///    tidak pernah menyala, atau aplikasi menjalankan berkas yang salah.
/// </summary>
public sealed class ServerLisensiLokalTests : IDisposable
{
    private readonly string _folder;

    public ServerLisensiLokalTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sudesapp-server-lokal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* sisa berkas uji */ }
    }

    private string BerkasPenanda => Path.Combine(_folder, "server.pid.json");

    private static OpsiServerLokal Opsi(int port = 5110) => new(
        port,
        FolderAplikasi: Path.Combine(Path.GetTempPath(), "sudesapp-aplikasi-uji"),
        FolderData: Path.Combine(Path.GetTempPath(), "sudesapp-data-uji"),
        JalurData: Path.Combine(Path.GetTempPath(), "sudesapp-data-uji", "langganan.json"),
        JalurLog: Path.Combine(Path.GetTempPath(), "sudesapp-data-uji", "server.log"),
        JalurPenanda: Path.Combine(Path.GetTempPath(), "sudesapp-data-uji", "server.pid.json"));

    private static ServerLisensiLokal Layanan(OpsiServerLokal? opsi = null) =>
        new(NullLogger<ServerLisensiLokal>.Instance, opsi ?? Opsi());

    // ------------------------------------------------------------ penentuan target

    [Fact]
    public void AlamatMemakaiLoopbackYangSamaDenganKonfigurasiAplikasi()
    {
        // Alamat server harus berbicara tentang host+port yang SAMA dengan
        // Lisensi:UrlServer di appsettings (127.0.0.1:5110). "localhost" bukan
        // alamat yang salah, tetapi menyimpan dua bentuk alamat membuat alamat
        // yang dipakai halaman Langganan bisa berbeda dari yang dijalankan.
        Assert.Equal("http://127.0.0.1:5110", Layanan().Alamat);
    }

    [Fact]
    public void CariTargetMemilihExeHasilPublishBilaAda()
    {
        string aplikasi = Path.Combine(_folder, "aplikasi");
        string server = Path.Combine(aplikasi, "server");
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(server, "SuDesApp.Server.csproj"), "<Project/>");
        string exe = Path.Combine(server, "SuDesApp.Server.exe");
        File.WriteAllText(exe, string.Empty);

        var target = ServerLisensiLokal.CariTarget(jalurPreferensi: null, folderAplikasi: aplikasi);

        Assert.NotNull(target);
        Assert.Equal(exe, target!.Value.Berkas);
        Assert.Equal(string.Empty, target.Value.Argumen);
        Assert.Equal(server, target.Value.FolderKerja);
    }

    [Fact]
    public void CariTargetMemilihDllLaluMenjalankannyaLewatDotnet()
    {
        string aplikasi = Path.Combine(_folder, "aplikasi");
        string server = Path.Combine(aplikasi, "server");
        Directory.CreateDirectory(server);
        string dll = Path.Combine(server, "SuDesApp.Server.dll");
        File.WriteAllText(dll, string.Empty);

        var target = ServerLisensiLokal.CariTarget(null, aplikasi);

        Assert.NotNull(target);
        Assert.Equal("dotnet", target!.Value.Berkas);
        Assert.Contains(dll, target.Value.Argumen, StringComparison.Ordinal);
    }

    [Fact]
    public void CariTargetMenemukanProyekSaatPengembangan()
    {
        string aplikasi = Path.Combine(_folder, "aplikasi");
        string server = Path.Combine(aplikasi, "server");
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(server, "SuDesApp.Server.csproj"), "<Project/>");

        var target = ServerLisensiLokal.CariTarget(null, aplikasi);

        Assert.NotNull(target);
        Assert.Equal("dotnet", target!.Value.Berkas);
        Assert.Contains("run --project", target.Value.Argumen, StringComparison.Ordinal);
        Assert.Equal(server, target.Value.FolderKerja);
    }

    [Fact]
    public void CariTargetKosongBilaTidakAdaBerkasServer()
    {
        string aplikasi = Path.Combine(_folder, "aplikasi");
        Directory.CreateDirectory(aplikasi);

        // Folder sementara tidak berada di dalam salinan repositori, jadi
        // pencarian ke atas tidak menemukan proyek SuDesApp.Server.
        Assert.Null(ServerLisensiLokal.CariTarget(null, aplikasi));
    }

    // ------------------------------------------------------- pembersihan sisa proses

    [Fact]
    public void SisaProsesDariSesiSebelumnyaDimatikanSaatAplikasiDibuka()
    {
        using var proses = JalankanProsesLama();
        TulisPenanda(proses);

        ServerLisensiLokal.BersihkanSisa(BerkasPenanda, NullLogger.Instance, port: 5110);

        Assert.False(File.Exists(BerkasPenanda));
        Assert.True(proses.WaitForExit(10_000), "server sisa seharusnya dimatikan");
    }

    [Fact]
    public void ProsesAsingDenganPidSamaTidakIkutDimatikan()
    {
        using var proses = JalankanProsesLama();

        // Penanda menunjuk pid yang sama tetapi waktu mulai berbeda jauh:
        // artinya pid sudah dipakai ulang oleh program lain. Program itu TIDAK
        // boleh dibunuh — hanya penandanya yang dibuang.
        TulisPenanda(proses, mulai: proses.StartTime.ToUniversalTime().AddMinutes(-5));

        ServerLisensiLokal.BersihkanSisa(BerkasPenanda, NullLogger.Instance, port: 5110);

        Assert.False(File.Exists(BerkasPenanda));
        Assert.False(proses.HasExited, "proses asing tidak boleh dibunuh");

        try { proses.Kill(entireProcessTree: true); } catch { /* sudah berhenti */ }
    }

    [Fact]
    public void MatikanTanpaProsesTerdaftarMembuangPenandaDanTidakMelempar()
    {
        File.WriteAllText(BerkasPenanda, "{\"Pid\":0,\"Mulai\":\"2000-01-01T00:00:00Z\",\"Alamat\":\"\"}");

        var layanan = new ServerLisensiLokal(
            NullLogger<ServerLisensiLokal>.Instance,
            Opsi() with { JalurPenanda = BerkasPenanda });

        layanan.Matikan();

        Assert.False(File.Exists(BerkasPenanda));
        Assert.False(layanan.Berjalan);
    }

    // ------------------------------------------------------------------- bantuan

    private static Process JalankanProsesLama()
    {
        var proses = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping -n 60 127.0.0.1 >nul",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Assert.NotNull(proses);
        return proses!;
    }

    private void TulisPenanda(Process proses, DateTime? mulai = null)
    {
        var penanda = new
        {
            Pid = proses.Id,
            Mulai = mulai ?? proses.StartTime.ToUniversalTime(),
            Alamat = "http://127.0.0.1:5110"
        };

        File.WriteAllText(BerkasPenanda, JsonSerializer.Serialize(penanda));
    }
}
