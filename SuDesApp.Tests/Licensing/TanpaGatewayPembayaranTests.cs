using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Gateway pembayaran Midtrans sudah DIHAPUS (opsi A: jalur otomatis pergi,
/// pembukuan manual tinggal satu-satunya). Uji ini menjaga dua arah:
///
/// 1. jalur gateway tidak boleh muncul lagi — sebagai berkas, endpoint, kunci
///    konfigurasi, maupun medan JSON <c>token</c>/<c>link_pembayaran</c>/<c>mode</c>
///    yang dulu dibaca klien; dan
/// 2. jalan yang tersisa harus tetap ada: <c>/buat-tagihan</c> mencatat tagihan,
///    <c>/admin/pelanggan/{kode}/tandai-lunas</c> memberi hak akses, dan
///    konfigurasi operator lama (izin <c>payments.reconcile</c> /
///    <c>payments.test</c>) tidak boleh membuat server menolak start.
///
/// Pemeriksaan dilakukan pada berkas sumber karena server lisensi adalah proses
/// terpisah yang tidak dijalankan suite ini; aturan yang sama pernah hilang
/// diam-diam lewat refactor, dan gejalanya baru terlihat di komputer pengguna.
/// </summary>
public sealed class TanpaGatewayPembayaranTests
{
    private static readonly string Akar = CariAkarRepo();
    private static readonly string Server = Path.Combine(Akar, "SuDesApp.Server");
    private static readonly string Program = Path.Combine(Server, "Program.cs");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    private static string Baca(string path) => File.ReadAllText(path);

    [Fact]
    public void BerkasGatewayTidakAdaLagi()
    {
        var pola = new[]
        {
            "MidtransService.cs", "KonfigurasiMidtrans.cs", "PembuatTagihan.cs",
            "LayananWebhook.cs", "PelacakWebhook.cs", "LayananRekonsiliasi.cs",
            "RekonsiliasiLatar.cs", "LayananUjiPembayaran.cs", "StatusPembayaran.cs",
            Path.Combine("wwwroot", "checkout.html"), Path.Combine("wwwroot", "checkout-uji.html"),
        };

        var masihAda = pola
            .Select(p => Path.Combine(Server, p))
            .Where(File.Exists)
            .Select(p => Path.GetRelativePath(Akar, p))
            .ToArray();

        Assert.True(masihAda.Length == 0,
            "Berkas gateway pembayaran masih ada: " + string.Join(", ", masihAda));
    }

    [Fact]
    public void EndpointGatewayTidakDidaftarkanLagi()
    {
        var teks = Baca(Program);
        foreach (var jalur in new[]
                 {
                     "app.MapGet(\"/checkout\"", "app.MapGet(\"/checkout-uji\"",
                     "app.MapPost(\"/webhook-pembayaran\"", "app.MapPost(\"/admin/rekonsiliasi\"",
                     "/admin/uji-pembayaran",
                 })
        {
            Assert.False(teks.Contains(jalur, StringComparison.Ordinal),
                "Endpoint gateway masih terdaftar: " + jalur);
        }

        // Kebijakan pembatas laju khusus webhook ikut hilang bersama endpointnya.
        Assert.DoesNotContain("AddPolicy(\"webhook\"", teks, StringComparison.Ordinal);
    }

    [Fact]
    public void KredensialGatewayTidakLagiDibacaDariKonfigurasi()
    {
        var berkas = Directory
            .EnumerateFiles(Server, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Equals("appsettings.lokal.json", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(berkas);
        foreach (var f in berkas)
        {
            var teks = Baca(f);
            Assert.False(teks.Contains("Midtrans", StringComparison.OrdinalIgnoreCase),
                Path.GetFileName(f) + " masih memuat blok konfigurasi Midtrans.");
            Assert.False(teks.Contains("Rekonsiliasi", StringComparison.OrdinalIgnoreCase),
                Path.GetFileName(f) + " masih memuat blok konfigurasi rekonsiliasi.");
        }

        // Penanda lingkungan lama tidak boleh dipakai di mana pun.
        var sisa = Directory.EnumerateFiles(Server, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(Baca)
            .Count(t => t.Contains("MIDTRANS_", StringComparison.Ordinal));
        Assert.Equal(0, sisa);
    }

    [Fact]
    public void BuatTagihanTetadaAda_TanpaMedanGateway()
    {
        var teks = Baca(Program);
        Assert.Contains("app.MapPost(\"/buat-tagihan\"", teks, StringComparison.Ordinal);

        var mulai = teks.IndexOf("app.MapPost(\"/buat-tagihan\"", StringComparison.Ordinal);
        var akhir = teks.IndexOf(".RequireRateLimiting(", mulai, StringComparison.Ordinal);
        Assert.True(akhir > mulai, "Badan /buat-tagihan tidak terbaca.");
        var badan = teks.Substring(mulai, akhir - mulai);

        Assert.DoesNotContain("token = ", badan, StringComparison.Ordinal);
        Assert.DoesNotContain("link_pembayaran", badan, StringComparison.Ordinal);
        Assert.DoesNotContain("mode =", badan, StringComparison.Ordinal);

        // Yang dijanjikan ke klien tetap ada — halaman Langganan membacanya.
        foreach (var medan in new[] { "orderId", "nominal", "hari", "kode_aktivasi" })
            Assert.Contains(medan, badan, StringComparison.Ordinal);
    }

    [Fact]
    public void KlienTidakLagiMembacaTautanPembayaranGateway()
    {
        var klien = Path.Combine(Akar, "SuDesApp.Core", "Services", "LanggananKlien.cs");
        var teks = Baca(klien);
        Assert.DoesNotContain("link_pembayaran", teks, StringComparison.Ordinal);
        Assert.DoesNotContain("LinkPembayaran", teks, StringComparison.Ordinal);

        // Refleksi: rekaman tagihan benar-benar tidak punya medan tautan lagi.
        var properti = typeof(SuDesApp.Services.TagihanLangganan).GetProperties();
        Assert.DoesNotContain("LinkPembayaran", properti.Select(p => p.Name), StringComparer.Ordinal);

        // Desktop: tidak ada lagi pembuka peramban untuk tautan pembayaran.
        var tindakan = Path.Combine(Akar, "SuDesApp.Wpf", "ViewModels", "LanggananViewModel.Tindakan.cs");
        Assert.DoesNotContain("CobaBukaPeramban", Baca(tindakan), StringComparison.Ordinal);
    }

    [Fact]
    public void PembukuanManualTetapMenjadiSatuSatunyaJalanMemberiAkses()
    {
        var teks = Baca(Program);
        Assert.Contains("/admin/pelanggan/{kode}/tandai-lunas", teks, StringComparison.Ordinal);

        var katalog = AdminEndpointCatalog.Routes;
        Assert.Equal(AdminPermissions.PaymentsRecord,
            katalog[("POST", "/admin/pelanggan/{kode}/tandai-lunas")]);
        Assert.False(katalog.ContainsKey(("POST", "/admin/rekonsiliasi")));
        Assert.DoesNotContain(katalog, kv => kv.Key.Route.StartsWith("/admin/uji-pembayaran", StringComparison.Ordinal));
    }

    [Fact]
    public void IzinWarisanLamaTidakMembuatServerMenolakStart()
    {
        // Deployment yang masih menuliskan payments.reconcile / payments.test
        // harus tetap bisa boot: izin itu dikenal tapi tidak memberi akses.
        var konfigurasi = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lisensi:AdminOperators:0:Id"] = "operator-lama",
                ["Lisensi:AdminOperators:0:Name"] = "Operator Lama",
                ["Lisensi:AdminOperators:0:Token"] = new string('k', 40),
                ["Lisensi:AdminOperators:0:Permissions:0"] = AdminPermissions.PaymentsRecord,
                ["Lisensi:AdminOperators:0:Permissions:1"] = "payments.reconcile",
                ["Lisensi:AdminOperators:0:Permissions:2"] = "payments.test",
            })
            .Build();

        var registry = new AdminOperatorRegistry(konfigurasi);

        Assert.Equal(2, registry.IzinWarisan.Count);
        Assert.Contains("payments.reconcile", registry.IzinWarisan);
        Assert.True(AdminPermissions.Dikenal("payments.test"));
        Assert.False(AdminPermissions.All.Contains("payments.test"),
            "Izin warisan tidak boleh lagi muncul sebagai izin aktif.");
        Assert.False(AdminPermissions.Dikenal("payments.mengarang"),
            "Izin yang benar-benar asing harus tetap ditolak (jaga-jaga salah ketik).");
    }

    [Fact]
    public void ServerTidakMenolakStartTanpaKredensialPembayaran()
    {
        // Penjaga lama menolak start bila ServerKey gateway kosong. Setelah
        // gateway pergi, tidak boleh ada lagi penolakan semacam itu di Program.cs.
        var teks = Baca(Program);
        Assert.DoesNotContain("Server refuses start", teks, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerKey", teks, StringComparison.Ordinal);
    }
}
