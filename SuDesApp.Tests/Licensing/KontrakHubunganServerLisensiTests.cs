using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// KONTRAK BEKU "Tombol Hubungkan" — berkas ini dan berkas yang dijaganya
/// adalah hasil penyelidikan panjang atas gejala "server lisensi selalu jadi
/// tidak terhubung setelah pengeditan". Setiap kali kode server lisensi
/// diedit, salah satu invarian di bawah ini yang patah lebih dulu.
/// JANGAN MENGEDIT berkas yang dijaga di sini kecuali tes ini ikut
/// diperbarui DENGAN ALASAN yang ditulis di commit.
///
/// Invarian yang dijaga:
/// 1. LisensiManager bawaan menunjuk http://localhost:5000 — nilai itu HANYA
///    fallback kosong; alamat sungguhan harus datang dari konfigurasi atau
///    preferensi (lihat aturan 1-3 PenentuAlamatLisensi).
/// 2. ServerLisensiLokal.Alamat dan Lisensi:UrlServer di appsettings repositori
///    harus SAMA persis (127.0.0.1:5110). Dua bentuk host untuk mesin yang
///    sama (localhost vs 127.0.0.1) membuat halaman Langganan dan proses server
///    menunjuk alamat berbeda → chip "Tidak terhubung".
/// 3. Konfigurasi produksi (https, non-loopback) SELALU menang dan mengunci;
///    preferensi lama tidak boleh menimpanya. Sebaliknya, alamat loopback dari
///    konfigurasi tidak pernah mengunci, supaya tombol Hubungkan bisa menyalakan
///    dan memakai server lokal.
/// </summary>
public sealed class KontrakHubunganServerLisensiTests
{
    private static LisensiManager BuatLisensi() =>
        new(NullLogger<LisensiManager>.Instance, new HttpClient());

    // ------------------------------------------------ 1. alamat bawaan & bentuk host

    [Fact]
    public void AlamatBawaanLisensiManagerHanyaFallback_KontrakDokumen()
    {
        var lisensi = BuatLisensi();
        Assert.Equal("http://localhost:5000", lisensi.UrlServer);
        Assert.False(lisensi.UrlServerTerkunci);
    }

    [Fact]
    public void AlamatServerLokalDanAppsettingsRepositoriHarusSamaPersis()
    {
        // Inilah akar gejala "setelah diedit selalu tidak terhubung": server
        // dijalankan di 127.0.0.1:5110 tetapi halaman Langganan diarahkan ke
        // localhost:5000 (atau sebaliknya). Tes ini membaca appsettings.json
        // SUNGGUHAN di akar repositori, bukan salinan build, supaya perubahan
        // sekecil apa pun pada kedua pihak di tangkap di CI.
        var root = CariAkarRepositori();
        string jsonPath = Path.Combine(root, "appsettings.json");
        Assert.True(File.Exists(jsonPath), "appsettings.json akar tidak ditemukan dari direktori uji.");

        using var dokumen = JsonDocument.Parse(File.ReadAllText(jsonPath));
        string? urlKonfig = dokumen.RootElement
            .GetProperty("Lisensi").GetProperty("UrlServer").GetString();

        using var server = new ServerLisensiLokal(NullLogger<ServerLisensiLokal>.Instance);
        Assert.Equal(server.Alamat, PenentuAlamatLisensi.Rapikan(urlKonfig));
        Assert.Equal("http://127.0.0.1:5110", server.Alamat);
    }

    // ------------------------------------------------ 2. aturan pemilihan alamat

    [Fact]
    public void ProduksiHttpsMenangDanMengunci_TerhadapPreferensiLama()
    {
        var hasil = PenentuAlamatLisensi.Tentukan(
            "https://lisensi.contoh.go.id", "http://127.0.0.1:5110");
        Assert.Equal("https://lisensi.contoh.go.id", hasil.Url);
        Assert.True(hasil.Terkunci);
    }

    [Fact]
    public void LoopbackKonfigurasiTidakPernahMengunci_SehinggaHubungkanBisaMemakaiServerLokal()
    {
        // Tombol Hubungan menyalakan server lokal lalu MENULIS preferensi
        // 127.0.0.1:5110. Bila konfigurasi loopback ikut mengunci, tulisan itu
        // dibuang dan sesi berikutnya menunjuk alamat lain → "tidak terhubung"
        // lagi. Jangan pernah mengubah ini tanpa alasan rilis yang ditulis di
        // commit.
        var hasil = PenentuAlamatLisensi.Tentukan(
            "http://127.0.0.1:5110", "http://127.0.0.1:5110");
        Assert.False(hasil.Terkunci);

        // Preferensi sah boleh menang atas konfigurasi loopback.
        hasil = PenentuAlamatLisensi.Tentukan("http://127.0.0.1:5110", "http://127.0.0.1:5199");
        Assert.Equal("http://127.0.0.1:5199", hasil.Url);
        Assert.False(hasil.Terkunci);
    }

    [Fact]
    public void KonfigurasiKosongDanPreferensiKosong_JatuhKeAlamatBawaanLoopback()
    {
        var hasil = PenentuAlamatLisensi.Tentukan(null, "  ");
        Assert.Equal(PenentuAlamatLisensi.AlamatBawaan, hasil.Url);
        Assert.False(hasil.Terkunci);
    }

    [Fact]
    public void ProduksiHttpBiasaBukanProduksi_TidakMengunci()
    {
        // Hanya HTTPS non-loopback yang boleh mengunci alamat. Menyetel URL
        // http:// produksi lalu "mengunci" adalah cara lain membuat halaman
        // Langganan tidak bisa lagi diarahkan ke server lokal saat pengujian.
        var hasil = PenentuAlamatLisensi.Tentukan("http://lisensi.contoh.go.id", null);
        Assert.Equal("http://lisensi.contoh.go.id", hasil.Url);
        Assert.False(hasil.Terkunci);
    }

    // ------------------------------------------------ 3. kunci proses server lokal

    [Fact]
    public async Task ServerTerkunciMenolakNyalakanServerLokal()
    {
        // NyalakanDanPakaiAsync adalah satu-satunya jalur "hubungkan ke server
        // lokal". Bila suatu edit menghapus penjagaan Terkunci di sini, build
        // produksi diam-diam bisa dialihkan ke server lokal desa — dan
        // sebaliknya, gejalanya selalu "tidak terhubung".
        var lisensi = BuatLisensi();
        lisensi.UrlServerTerkunci = true;

        using var server = new ServerLisensiLokal(NullLogger<ServerLisensiLokal>.Instance);
        Assert.False(await server.NyalakanDanPakaiAsync(lisensi));
        Assert.False(string.IsNullOrEmpty(server.GalatTerakhir));
        Assert.Equal("http://localhost:5000", lisensi.UrlServer);
    }

    private static string CariAkarRepositori()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("SuDesApp.slnx tidak ditemukan dari direktori uji.");
    }
}
