using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.ViewModels;
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
/// 4. Server lisensi lokal WAJIB dinyalakan bila alamat proses ini loopback,
///    meski preferensi belum tersimpan — berkas preferensi bisa hilang, dan
///    alamat loopback tanpa server lokal berarti tidak ada yang menjawab.
/// 5. Alasan kegagalan menyalakan harus ikut terbaca di GalatTerakhir (baris
///    keluaran server terakhir), bukan hanya "berhenti sendiri" di log.
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

    // ------------------------------------------------ 4. kapan server lokal WAJIB nyala

    [Fact]
    public void AlamatLoopbackTidakTerkunci_MenuntutServerLokalDinyalakan()
    {
        // Berkas preferensi (login_prefs.json) bisa hilang: profil baru,
        // bersih-bersih, instalasi ulang. Kalau keputusan menyalakan server hanya
        // bersumber pada preferensi itu, aplikasi berhenti menyalakan servernya
        // sendiri dan pengguna hanya melihat chip "Belum terjangkau" — persis
        // gejala "tombol Ubah alamat hilang, server lisensi mati".
        Assert.True(PenentuAlamatLisensi.ServerLokalDibutuhkan("http://127.0.0.1:5110", terkunci: false));
        Assert.True(PenentuAlamatLisensi.ServerLokalDibutuhkan("http://localhost:5000", terkunci: false));
    }

    [Theory]
    [InlineData("https://lisensi.contoh.go.id", true)]  // produksi terkunci: server lokal tidak boleh nyala
    [InlineData("https://lisensi.contoh.go.id", false)] // produksi tanpa kunci: tetap bukan server lokal
    [InlineData("http://lisensi.contoh.go.id", false)]  // server tetangga
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ServerLokalTidakDibutuhkan_UntukAlamatSelainKomputerSendiri(string? alamat, bool terkunci)
    {
        Assert.False(PenentuAlamatLisensi.ServerLokalDibutuhkan(alamat, terkunci));
    }

    [Fact]
    public void AppStartupMenyalakanServerLokalKarenaAlamatnya_LoopbackBukanHanyaPreferensi()
    {
        // Kontrak sumber: selama App.Startup memutuskan sendiri lewat
        // IsServerLisensiLokalAktif(), hilangnya berkas preferensi membuat
        // server lisensi tidak pernah nyala. Perluasan ke ServerLokalDibutuhkan
        // adalah satu-satunya jalan keluar — jangan dihapus.
        var tubuh = TubuhMetode("SuDesApp.Wpf", "App.Startup.cs", "private void MulaiServerLisensiLokal()");
        Assert.Contains("ServerLokalDibutuhkan", tubuh, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!AppPreferenceStore.IsServerLisensiLokalAktif()) return;", tubuh, StringComparison.Ordinal);
        // Alamat yang dikunci konfigurasi produksi tetap harus ditolak.
        Assert.Contains("UrlServerTerkunci", tubuh, StringComparison.Ordinal);
    }

    // ------------------------------------------------ 5. alasan kegagalan harus terbaca

    [Fact]
    public void RangkumBarisTerakhir_MenyebutBarisTerbaruLebihDulu()
    {
        var rangkum = ServerLisensiLokal.RangkumBarisTerakhir(new[]
        {
            "info: mulai",
            "ServerKey Midtrans belum diatur dan aplikasi tidak berjalan dalam mode Development.",
            "   "
        });
        Assert.StartsWith("ServerKey Midtrans belum diatur", rangkum, StringComparison.Ordinal);
        Assert.Contains("info: mulai", rangkum, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", rangkum, StringComparison.Ordinal);
    }

    [Fact]
    public void RangkumBarisTerakhirDipotongSaatTerlaluPanjang()
    {
        var rangkum = ServerLisensiLokal.RangkumBarisTerakhir(
            new[] { new string('x', 900) }, maksPanjang: 400);
        Assert.Equal(400, rangkum.Length);
        Assert.EndsWith("\u2026", rangkum, StringComparison.Ordinal);
    }

    [Fact]
    public void GalatSaatServerBerhentiSendiriMemuatAlasanDariLog()
    {
        // Tanpa alasan dari keluaran server, pesan "berhenti sendiri (kode 0)"
        // membuat operator mengira bug koneksi, padahal server menolak start
        // dengan pesan yang jelas (mis. ServerKey Midtrans belum diisi).
        var tubuh = TubuhMetode("SuDesApp.Core/Services", "ServerLisensiLokal.cs", "private async Task<bool> TungguSiapAsync");
        Assert.Contains("BarisTerakhirServer()", tubuh, StringComparison.Ordinal);
        Assert.Contains("Alasan dari server", tubuh, StringComparison.Ordinal);
    }

    // ------------------------------------------------ 6. ciri yang dibaca pengguna:
    // tombol "Ubah alamat" hanya ada selama server terbukti menjawab

    private sealed class ServerPaket : HttpMessageHandler
    {
        private readonly int _tundaDetik;
        public ServerPaket(int tundaDetik = 150) => _tundaDetik = tundaDetik;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Kontruktor VM langsung memanggil katalog; jeda kecil membuat
            // keadaan "belum terhubung" sempat diperiksa lebih dulu.
            await Task.Delay(_tundaDetik, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"kode\":\"bulanan\",\"nama\":\"Bulanan\",\"harga\":150000,\"hari\":30}]",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task TombolUbahAlamatMunculSesudahServerMenjawab_DanHilangSesudahDibukaKunci()
    {
        // Operator memakai satu gejala untuk mengenali server lisensi mati:
        // tombol "Ubah alamat" hilang. Tombol itu memang hanya tampil saat
        // alamat terkunci OLEH KONEKSI YANG TERBUKTI — jadi uji ini mengunci
        // hubungan dua arah itu supaya "ciri" tadi tidak berubah jadi dusta.
        var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance, new HttpClient(new ServerPaket()))
        {
            UrlServer = "http://127.0.0.1:5110"
        };
        var klien = new LanggananKlien(lisensi, new HttpClient(new ServerPaket()), NullLogger<LanggananKlien>.Instance);
        var penjaga = new PenjagaLisensi(lisensi, new TrialLokal(), NullLogger<PenjagaLisensi>.Instance, klien);

        var vm = new LanggananViewModel(
            NullLogger<LanggananViewModel>.Instance, lisensi, penjaga, klien,
            new SambunganServerLisensi());

        Assert.False(vm.TampilBukaKunciServer);
        Assert.True(vm.UrlServerDapatDiubah);

        var batas = DateTime.UtcNow.AddSeconds(5);
        while (vm.ServerChipTeks != "Terhubung" && DateTime.UtcNow < batas)
        {
            await Task.Delay(25);
        }

        Assert.Equal("Terhubung", vm.ServerChipTeks);
        Assert.False(vm.ServerBelumTersambung);
        Assert.True(vm.UrlServerTerkunci);
        Assert.True(vm.TampilBukaKunciServer);
        Assert.False(vm.UrlServerDapatDiubah);

        // Setelah dibuka secara sadar, tombolnya hilang lagi dan kotak alamat
        // bisa diketik — kalau tidak, pengguna tidak bisa mengganti alamat sama sekali.
        vm.BukaKunciServerCommand.Execute(null);
        Assert.False(vm.TampilBukaKunciServer);
        Assert.True(vm.UrlServerDapatDiubah);
    }

    /// <summary>Potong badan satu metode dari berkas sumber, untuk uji kontrak.</summary>
    private static string TubuhMetode(string folder, string berkas, string penanda)
    {
        var teks = File.ReadAllText(Path.Combine(CariAkarRepositori(), folder.Replace('/', Path.DirectorySeparatorChar), berkas));
        var mulai = teks.IndexOf(penanda, StringComparison.Ordinal);
        Assert.True(mulai >= 0, $"{penanda} tidak ditemukan di {berkas} — jangan ganti namanya tanpa memperbarui uji kontrak ini.");

        var kurung = teks.IndexOf('{', mulai);
        int dalam = 0;
        for (var i = kurung; i < teks.Length; i++)
        {
            if (teks[i] == '{') dalam++;
            else if (teks[i] == '}')
            {
                dalam--;
                if (dalam == 0) return teks.Substring(mulai, i - mulai + 1);
            }
        }
        return teks.Substring(mulai);
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
