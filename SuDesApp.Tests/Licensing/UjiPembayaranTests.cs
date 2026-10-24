using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Aturan <see cref="KonfigurasiMidtrans"/>: satu sumber pembacaan untuk
/// ServerKey, sandbox/production, dan URL notifikasi transaksi uji.
///
/// <para>Yang dijaga di sini bukan hanya "nilai terbaca", tapi juga arah
/// prioritasnya: environment variable menang atas appsettings, dan nilai
/// KOSONG berarti "belum diisi" — bukan "kunci kosong". Salah baca di sini
/// berakibat server production diam-diam bicara ke sandbox, atau verifikasi
/// signature mati (dan setiap pembayaran nyata ditolak).</para>
/// </summary>
[Collection("MidtransEnvironment")]
public sealed class KonfigurasiMidtransTests
{
    private static IConfiguration Config(params (string Kunci, string? Nilai)[] pasangan) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pasangan.Select(p =>
                new KeyValuePair<string, string?>(p.Kunci, p.Nilai)))
            .Build();

    [Fact]
    public void Url_dasar_ditambahkan_dengan_path_webhook()
    {
        Assert.Equal("https://tunnel.example/webhook-pembayaran",
            KonfigurasiMidtrans.BangunUrlWebhook("https://tunnel.example"));
    }

    [Fact]
    public void Url_yang_sudah_menunjuk_webhook_dipakai_apa_adanya()
    {
        Assert.Equal("https://tunnel.example/webhook-pembayaran",
            KonfigurasiMidtrans.BangunUrlWebhook("https://tunnel.example/webhook-pembayaran"));
    }

    [Fact]
    public void Trailing_slash_tidak_membuat_dua_path()
    {
        Assert.Equal("https://tunnel.example/webhook-pembayaran",
            KonfigurasiMidtrans.BangunUrlWebhook("https://tunnel.example/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bukan-url")]
    [InlineData("ftp://tunnel.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/webhook-pembayaran")]
    public void Url_yang_tidak_bisa_dipakai_ditolak(string url)
    {
        Assert.Null(KonfigurasiMidtrans.BangunUrlWebhook(url));
    }

    [Fact]
    public void ServerKey_kosong_berarti_belum_diisi()
    {
        var config = Config(("Midtrans:ServerKey", "   "));
        Assert.Equal("", KonfigurasiMidtrans.ServerKey(config));
    }

    [Fact]
    public void ServerKey_dari_appsettings_dibaca_bila_env_kosong()
    {
        var envLama = Environment.GetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey);
        try
        {
            // Env yang ADA tetapi KOSONG harus diabaikan, bukan menimpa nilai
            // appsettings dengan string kosong.
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey, "");
            var config = Config(("Midtrans:ServerKey", "kunci-dari-appsettings"));
            Assert.Equal("kunci-dari-appsettings", KonfigurasiMidtrans.ServerKey(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey, envLama);
        }
    }

    [Fact]
    public void Env_menang_atas_appsettings_untuk_server_key()
    {
        var envLama = Environment.GetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey);
        try
        {
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey, "kunci-dari-env");
            var config = Config(("Midtrans:ServerKey", "kunci-dari-appsettings"));
            Assert.Equal("kunci-dari-env", KonfigurasiMidtrans.ServerKey(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvServerKey, envLama);
        }
    }

    [Fact]
    public void UseSandbox_false_berarti_production()
    {
        var config = Config(("Midtrans:UseSandbox", "false"));
        Assert.False(KonfigurasiMidtrans.PakaiSandbox(config));
    }

    [Fact]
    public void Tanpa_konfigurasi_defaultnya_sandbox()
    {
        // Default sandbox: server yang salah setelan lebih mungkin gagal diuji
        // daripada diam-diam memakai uang sungguhan.
        Assert.True(KonfigurasiMidtrans.PakaiSandbox(Config()));
    }

    [Fact]
    public void Env_sandbox_kosong_tidak_membalikkan_pilihan_appsettings()
    {
        var envLama = Environment.GetEnvironmentVariable(KonfigurasiMidtrans.EnvSandbox);
        try
        {
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvSandbox, "");
            var config = Config(("Midtrans:UseSandbox", "false"));
            Assert.False(KonfigurasiMidtrans.PakaiSandbox(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable(KonfigurasiMidtrans.EnvSandbox, envLama);
        }
    }
}

/// <summary>
/// Pelacak notifikasi webhook: jejak "notifikasi sungguhan datang atau tidak".
///
/// <para>Yang dijaga: jejak tidak boleh hilang diam-diam (antrean berbatas),
/// urutannya harus terbaru-lebih-dulu, dan penyaringan per order harus tepat
/// — kalau tidak, operator akan melihat bukti yang salah dan menyimpulkan
/// alurnya jalan padahal tidak.</para>
/// </summary>
public sealed class PelacakWebhookTests
{
    private static CatatanNotifikasi Catat(string order, DateTime waktu, bool diterima = true) =>
        new(waktu, order, "settlement", "150000.00", null, "other_qris", true,
            200, diterima, false, diterima ? 30 : null, "selesai", "1.2.3.4", "Veritrans/1.0");

    [Fact]
    public void Catatan_terbaru_dilebih_dahulukan()
    {
        var pelacak = new PelacakWebhook();
        var t0 = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        pelacak.Catat(Catat("ORDER-1", t0));
        pelacak.Catat(Catat("ORDER-2", t0.AddMinutes(1)));

        var semua = pelacak.Semua();
        Assert.Equal(2, semua.Count);
        Assert.Equal("ORDER-2", semua[0].OrderId);
        Assert.Equal("ORDER-1", semua[1].OrderId);
    }

    [Fact]
    public void Jejak_dibatasi_supaya_memori_tidak_membengkak()
    {
        var pelacak = new PelacakWebhook();
        var t0 = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < PelacakWebhook.MaksCatatan + 25; i++)
            pelacak.Catat(Catat("ORDER-" + i, t0.AddSeconds(i)));

        var semua = pelacak.Semua();
        Assert.Equal(PelacakWebhook.MaksCatatan, semua.Count);
        // Yang tertinggal adalah yang TERBARU — jejak lama justru yang boleh dibuang.
        Assert.Equal("ORDER-" + (PelacakWebhook.MaksCatatan + 24), semua[0].OrderId);
    }

    [Fact]
    public void Pencarian_per_order_hanya_mengembalikan_notifikasi_order_itu()
    {
        var pelacak = new PelacakWebhook();
        var t0 = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        pelacak.Catat(Catat("ORDER-A", t0));
        pelacak.Catat(Catat("ORDER-B", t0.AddMinutes(1)));

        var untukA = pelacak.Untuk("ORDER-A");
        Assert.Single(untukA);
        Assert.Equal("ORDER-A", untukA[0].OrderId);
    }

    [Fact]
    public void Pencarian_order_kosong_tidak_mengembalikan_semua_catatan()
    {
        var pelacak = new PelacakWebhook();
        pelacak.Catat(Catat("ORDER-A", DateTime.UtcNow));

        Assert.Empty(pelacak.Untuk(""));
        Assert.Empty(pelacak.Untuk("   "));
        Assert.Empty(pelacak.Untuk(null));
    }
}

/// <summary>
/// Aturan <see cref="LayananUjiPembayaran"/>: jalur uji tetap tunduk pada aturan
/// uang yang sama dengan jalur pembayaran pelanggan.
///
/// <para>Yang dijaga di sini: harga SELALU dari katalog server (klien tak
/// boleh menentukan nominal), desa uji boleh dibuat otomatis, gateway yang
/// tidak aktif ditolak dengan pesan jelas, dan notifikasi transaksi uji boleh
/// diarahkan ke URL khusus lewat header.</para>
/// </summary>
public sealed class LayananUjiPembayaranTests
{
    private sealed class GatewayPalsu : IPembuatTagihan
    {
        public bool Aktif { get; set; } = true;
        public bool Gagal { get; set; }
        public string? UrlNotifikasi { get; private set; }
        public long Nominal { get; private set; }
        public string? NamaBarang { get; private set; }

        public Task<TagihanGateway?> BuatTagihanAsync(
            string orderId, long nominal, string namaBarang, string? urlNotifikasi = null)
        {
            UrlNotifikasi = urlNotifikasi;
            Nominal = nominal;
            NamaBarang = namaBarang;
            if (Gagal) return Task.FromResult<TagihanGateway?>(null);
            return Task.FromResult<TagihanGateway?>(
                new TagihanGateway(orderId, nominal, "https://snap.example/" + orderId, "token-uji"));
        }
    }

    private sealed class StatusPalsu : IPenyediaStatusPembayaran
    {
        public bool Aktif => true;
        public HasilStatusMidtrans Status { get; set; } =
            HasilStatusMidtrans.TidakDiketahui("belum ada di gateway.");

        public Task<HasilStatusMidtrans> AmbilStatusAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(Status);
    }

    private static IConfiguration Config(params (string Kunci, string? Nilai)[] pasangan) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pasangan.Select(p =>
                new KeyValuePair<string, string?>(p.Kunci, p.Nilai)))
            .Build();

    private static async Task<(LayananUjiPembayaran Layanan, LanggananMemori Repo, GatewayPalsu Gateway)>
        SusunAsync(OpsiUjiPembayaran? opsi = null)
    {
        var repo = new LanggananMemori();
        await repo.InitializeAsync();

        var gateway = new GatewayPalsu();
        var status = new StatusPalsu();
        var katalog = new KatalogPaket(Config());
        var admin = new LayananAdmin(repo, NullLogger<LayananAdmin>.Instance);
        var layanan = new LayananUjiPembayaran(
            repo, katalog, gateway, status, new PelacakWebhook(), admin,
            opsi ?? new OpsiUjiPembayaran("https://tunnel.example/webhook-pembayaran", false),
            NullLogger<LayananUjiPembayaran>.Instance);

        return (layanan, repo, gateway);
    }

    [Fact]
    public async Task Harga_uji_diambil_dari_katalog_server_bukan_dari_permintaan()
    {
        var (layanan, _, gateway) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("DESAUJI", "tahunan"));

        Assert.Equal(200, hasil.Status);
        Assert.Equal(1_200_000, gateway.Nominal);
        Assert.Equal(1_200_000, hasil.Nominal);
        Assert.Equal(365, hasil.Hari);
    }

    [Fact]
    public async Task Paket_tak_dikenal_jatuh_ke_paket_bawaan_bukan_paket_gratis()
    {
        var (layanan, _, gateway) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("DESAUJI", "paket-ngawur"));

        Assert.Equal(200, hasil.Status);
        Assert.Equal(150_000, gateway.Nominal);   // paket bawaan (Bulanan)
        Assert.True(gateway.Nominal > 0);
    }

    [Fact]
    public async Task Desa_uji_didaftarkan_otomatis_bila_kodenya_belum_ada()
    {
        var (layanan, repo, _) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("DESAUJI-BARU"));

        Assert.True(hasil.DesaBaru);
        var desa = await repo.CariByKodeAsync("DESAUJI-BARU");
        Assert.NotNull(desa);
    }

    [Fact]
    public async Task Gateway_tidak_aktif_ditolak_dengan_pesan_yang_jelas()
    {
        var (layanan, _, gateway) = await SusunAsync();
        gateway.Aktif = false;

        var hasil = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("DESAUJI"));

        Assert.Equal(503, hasil.Status);
        Assert.Contains("ServerKey", hasil.Pesan ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Kode_aktivasi_kosong_ditolak()
    {
        var (layanan, _, _) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("   "));

        Assert.Equal(400, hasil.Status);
    }

    [Fact]
    public async Task Notifikasi_diarahkan_ke_url_khusus_bila_diminta()
    {
        var (layanan, _, gateway) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(
            new PermintaanTagihanUji("DESAUJI", null, OverrideNotifikasi: true));

        Assert.Equal("https://tunnel.example/webhook-pembayaran", gateway.UrlNotifikasi);
        Assert.True(hasil.NotifikasiDialihkan);
    }

    [Fact]
    public async Task Notifikasi_boleh_dibiarkan_ikut_URL_dashboard()
    {
        var (layanan, _, gateway) = await SusunAsync();

        var hasil = await layanan.BuatTagihanAsync(
            new PermintaanTagihanUji("DESAUJI", null, OverrideNotifikasi: false));

        Assert.Null(gateway.UrlNotifikasi);
        Assert.False(hasil.NotifikasiDialihkan);
    }

    [Fact]
    public async Task Status_order_yang_tidak_dikenal_server_adalah_null()
    {
        var (layanan, _, _) = await SusunAsync();

        Assert.Null(await layanan.AmbilStatusAsync("ORDER-TIDAK-ADA"));
    }

    [Fact]
    public async Task Status_transaksi_uji_menampilkan_keadaan_gateway_dan_jejak_webhook()
    {
        var (layanan, _, _) = await SusunAsync();

        var dibuat = await layanan.BuatTagihanAsync(new PermintaanTagihanUji("DESAUJI"));
        Assert.NotNull(dibuat.OrderId);

        var status = await layanan.AmbilStatusAsync(dibuat.OrderId);

        Assert.NotNull(status);
        Assert.Equal(dibuat.OrderId, status!.OrderId);
        Assert.Equal("DESAUJI", status.KodeAktivasi);
        Assert.False(status.GatewayDitemukan);   // gateway palsu belum tahu order ini
        Assert.False(status.WebhookMasuk);
        Assert.Equal("production", status.Lingkungan);
    }
}

/// <summary>
/// Jejak notifikasi webhook yang dicatat <see cref="LayananWebhook"/>.
///
/// <para>Yang dijaga: notifikasi yang DITOLAK pun harus terlihat (bukti
/// "signature salah" sama pentingnya dengan bukti "lunas"), dan catatan harus
/// memuat jawaban server apa adanya — karena itulah yang dilihat operator
/// untuk memutuskan apakah alurnya benar.</para>
/// </summary>
public sealed class JejakWebhookTests
{
    private const string Kunci = "kunci-uji-midtrans";

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Midtrans:ServerKey"] = Kunci,
                ["Midtrans:UseSandbox"] = "true"
            })
            .Build();

    private static string Signature(string orderId, string statusCode, string gross) =>
        Convert.ToHexString(SHA512.HashData(
            Encoding.UTF8.GetBytes(orderId + statusCode + gross + Kunci))).ToLowerInvariant();

    private static async Task<(LayananWebhook Layanan, PelacakWebhook Pelacak, LanggananMemori Repo)>
        SusunAsync()
    {
        var repo = new LanggananMemori();
        await repo.InitializeAsync();
        var pelanggan = await repo.DaftarkanAsync("DESAUJI", "Desa Uji");
        await repo.SimpanTagihanAsync(
            pelanggan.Id, "SUDES-UJI-1", new PaketLangganan("bulanan", "Bulanan", 150_000, 30));

        var midtrans = new MidtransService(Config(), NullLogger<MidtransService>.Instance);
        var pelacak = new PelacakWebhook();
        var layanan = new LayananWebhook(repo, midtrans, NullLogger<LayananWebhook>.Instance, pelacak);
        return (layanan, pelacak, repo);
    }

    [Fact]
    public async Task Notifikasi_lunas_tercatat_sebagai_diterima()
    {
        var (layanan, pelacak, _) = await SusunAsync();
        var gross = "150000.00";
        var body = $$"""
        {"order_id":"SUDES-UJI-1","transaction_status":"settlement","status_code":"200",
         "gross_amount":"{{gross}}","payment_type":"other_qris",
         "signature_key":"{{Signature("SUDES-UJI-1", "200", gross)}}"}
        """;

        var hasil = await layanan.ProsesAsync(body, "1.2.3.4", "Veritrans/1.0");

        Assert.Equal(200, hasil.Status);
        Assert.True(hasil.Diterima);
        Assert.Equal(30, hasil.HariDitambah);

        var catatan = Assert.Single(pelacak.Untuk("SUDES-UJI-1"));
        Assert.True(catatan.SignatureValid == true);
        Assert.True(catatan.Diterima);
        Assert.Equal(30, catatan.HariDitambah);
        Assert.Equal(200, catatan.StatusBalasan);
        Assert.Equal("other_qris", catatan.PaymentType);
        Assert.Equal("1.2.3.4", catatan.AlamatIp);
        Assert.Equal("Veritrans/1.0", catatan.UserAgent);
    }

    [Fact]
    public async Task Notifikasi_signature_salah_tercatat_sebagai_ditolak()
    {
        var (layanan, pelacak, _) = await SusunAsync();
        const string body = """
        {"order_id":"SUDES-UJI-1","transaction_status":"settlement","status_code":"200",
         "gross_amount":"150000.00","signature_key":"bukan-signature-asli"}
        """;

        var hasil = await layanan.ProsesAsync(body, "9.9.9.9", "penyerang");

        Assert.Equal(403, hasil.Status);
        Assert.False(hasil.Diterima);

        var catatan = Assert.Single(pelacak.Untuk("SUDES-UJI-1"));
        Assert.True(catatan.SignatureValid == false);
        Assert.False(catatan.Diterima);
        Assert.Equal(403, catatan.StatusBalasan);
    }

    [Fact]
    public async Task Notifikasi_yang_tak_dapat_diturai_tetap_tercatat()
    {
        var (layanan, pelacak, _) = await SusunAsync();

        var hasil = await layanan.ProsesAsync("bukan json");

        Assert.Equal(400, hasil.Status);
        var catatan = Assert.Single(pelacak.Semua());
        Assert.True(catatan.SignatureValid is null);   // belum dinilai — bukan "sah"
        Assert.Equal(400, catatan.StatusBalasan);
    }
}

/// <summary>
/// Pembacaan status transaksi dari Core API Midtrans.
///
/// <para>Yang dijaga di sini adalah bentuk jawabannya, bukan hanya "dapat
/// jawaban": Core API Midtrans membalas <b>HTTP 200</b> untuk order yang
/// belum ada, dengan badannya
/// <c>{"status_code":"404","status_message":"Transaction doesn't exist."}</c>.
/// Kalau HTTP itu dianggap "transaksi ditemukan", operator melihat transaksi
/// fiktif di halaman uji, dan rekonsiliasi bisa-too-early menganggap ada
/// catatan pembayaran.</para>
///
/// <para>Jadi dua aturan: jawaban tanpa <c>transaction_status</c> berarti
/// "tidak bisa dipastikan" (bukan "ditemukan"), dan jawaban yang benar-benar
/// memuat status diteruskan apa adanya — <c>gross_amount</c> tetap TEKS
/// asli, bukan angka yang di-parse ulang.</para>
/// </summary>
public sealed class StatusMidtransTests
{
    private sealed class HandlerPalsu : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public HandlerPalsu(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jawaban = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(jawaban);
        }
    }

    private static MidtransService Servis(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Midtrans:ServerKey"] = "kunci-uji-midtrans",
                ["Midtrans:UseSandbox"] = "true"
            })
            .Build();
        return new MidtransService(config, NullLogger<MidtransService>.Instance, new HttpClient(handler));
    }

    [Fact]
    public async Task Order_yang_belum_ada_di_midtrans_bukan_berarti_ditemukan()
    {
        // Bentuk jawaban sungguhan dari api.midtrans.com untuk order Snap
        // yang belum dibuka pembeli: HTTP 200, status_code "404" di dalam body.
        const string body = """
        {"status_code":"404","status_message":"Transaction doesn't exist.","id":"9fbd5947"}
        """;
        var layanan = Servis(new HandlerPalsu(HttpStatusCode.OK, body));

        var hasil = await layanan.AmbilStatusAsync("SUDES-UJI-1");

        Assert.False(hasil.Ditemukan);
        Assert.Null(hasil.TransactionStatus);
        Assert.Contains("404", hasil.Pesan);
    }

    [Fact]
    public async Task Transaksi_yang_sungguhan_ada_dibaca_apa_adanya()
    {
        const string body = """
        {"transaction_status":"settlement","gross_amount":"150000.00",
         "fraud_status":"accept","status_code":"200","order_id":"SUDES-UJI-1"}
        """;
        var layanan = Servis(new HandlerPalsu(HttpStatusCode.OK, body));

        var hasil = await layanan.AmbilStatusAsync("SUDES-UJI-1");

        Assert.True(hasil.Ditemukan);
        Assert.Equal("settlement", hasil.TransactionStatus);
        Assert.Equal("150000.00", hasil.GrossAmount);   // teks asli, bukan 150000
        Assert.Equal("accept", hasil.FraudStatus);
    }

    [Fact]
    public async Task Http_404_tetap_diperlakukan_sebagai_tidak_diketahui()
    {
        var layanan = Servis(new HandlerPalsu(HttpStatusCode.NotFound, "{}"));

        var hasil = await layanan.AmbilStatusAsync("SUDES-UJI-1");

        Assert.False(hasil.Ditemukan);
        Assert.Null(hasil.TransactionStatus);
    }
}
