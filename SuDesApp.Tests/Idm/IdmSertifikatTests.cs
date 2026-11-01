using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Pemantauan masa berlaku sertifikat situs IDM pusat. Situs itu pernah
/// kedaluwarsa (Agustus 2026) dan halaman IDM langsung kosong; pemeriksaan ini
/// memberi peringatan jauh sebelum titik itu. Tiga hal yang dijaga:
/// sertifikat yang SUDAH kedaluwarsa tetap terbaca (validasi baku akan
/// menolaknya tepat saat peringatan paling dibutuhkan), logika status
/// murni tanpa jaringan, dan catatan berkas yang tidak pernah melempar.
/// </summary>
public sealed class IdmSertifikatTests
{
    // ===================== Logika status (murni) =====================

    [Fact]
    public void Tanpa_catatan_adalah_tidak_diketahui_dan_tanpa_pesan()
    {
        var status = IdmSertifikatStatus.Nilai(null, new DateTime(2026, 10, 30));

        Assert.Equal(StatusSertifikatIdm.TidakDiketahui, status);
        Assert.Equal("", IdmSertifikatStatus.Pesan(status, null, new DateTime(2026, 10, 30)));
        Assert.Equal("", IdmSertifikatStatus.Judul(status));
    }

    [Fact]
    public void Sertifikat_lewat_tanggal_adalah_kedaluwarsa_dan_pesan_menyebut_tanggal()
    {
        var kini = new DateTime(2026, 10, 30, 9, 0, 0);
        var sampai = kini.AddDays(-15);

        var status = IdmSertifikatStatus.Nilai(sampai, kini);
        var pesan = IdmSertifikatStatus.Pesan(status, sampai, kini);

        Assert.Equal(StatusSertifikatIdm.Kedaluwarsa, status);
        Assert.Contains("kedaluwarsa sejak", pesan);
        Assert.Contains("15 Oktober 2026", pesan);
        Assert.Contains("tanpa enkripsi", pesan);
        Assert.Contains("laporkan ke Kemendesa", pesan);
        Assert.Equal("Sertifikat situs IDM kedaluwarsa", IdmSertifikatStatus.Judul(status));
    }

    [Theory]
    [InlineData(1)]   // baru saja habis
    [InlineData(10)]  // masih ada waktu
    [InlineData(30)]  // batas peringatan dini — masih termasuk
    public void Sertifikat_dalam_30_hari_adalah_akan_kedaluwarsa(int hari)
    {
        var kini = new DateTime(2026, 10, 30, 9, 0, 0);
        var sampai = kini.AddDays(hari);

        var status = IdmSertifikatStatus.Nilai(sampai, kini);
        var pesan = IdmSertifikatStatus.Pesan(status, sampai, kini);

        Assert.Equal(StatusSertifikatIdm.AkanKedaluwarsa, status);
        Assert.Contains($"berakhir dalam {hari} hari", pesan);
        Assert.Contains("laporkan ke Kemendesa", pesan);
        Assert.Contains("segera berakhir", IdmSertifikatStatus.Judul(status));
    }

    [Fact]
    public void Sertifikat_lebih_dari_30_hari_adalah_sehat_dan_tanpa_pesan()
    {
        var kini = new DateTime(2026, 10, 30, 9, 0, 0);
        var sampai = kini.AddDays(31);

        var status = IdmSertifikatStatus.Nilai(sampai, kini);

        Assert.Equal(StatusSertifikatIdm.Sehat, status);
        Assert.Equal("", IdmSertifikatStatus.Pesan(status, sampai, kini));
        Assert.Equal("", IdmSertifikatStatus.Judul(status));
    }

    [Fact]
    public void Tepat_saat_kedaluwarsa_sudah_dihitung_kedaluwarsa()
    {
        var kini = new DateTime(2026, 10, 30, 9, 0, 0);

        Assert.Equal(StatusSertifikatIdm.Kedaluwarsa, IdmSertifikatStatus.Nilai(kini, kini));
    }

    // ===================== Catatan berkas =====================

    [Fact]
    public void Catatan_sertifikat_tersimpan_dan_terbaca_kembali()
    {
        string folder = TempFolder("simpan");
        IdmSertifikatStore.FolderOverride = folder;
        try
        {
            var store = new IdmSertifikatStore();
            Assert.Null(store.Muat()); // belum pernah disimpan

            var sampai = new DateTime(2026, 11, 9, 12, 0, 0);
            store.Simpan(new IdmSertifikatCatatan
            {
                BerlakuDari = new DateTime(2025, 11, 9, 12, 0, 0),
                BerlakuSampai = sampai,
                Subjek = "CN=idm.kemendesa.go.id",
                WaktuDiperiksa = new DateTime(2026, 10, 30, 9, 0, 0),
            });

            var hasil = store.Muat();
            Assert.NotNull(hasil);
            Assert.Equal(sampai, hasil!.BerlakuSampai);
            Assert.Equal(new DateTime(2025, 11, 9, 12, 0, 0), hasil.BerlakuDari);
            Assert.Equal("CN=idm.kemendesa.go.id", hasil.Subjek);
            Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), hasil.WaktuDiperiksa);
        }
        finally
        {
            IdmSertifikatStore.FolderOverride = null;
            Bersihkan(folder);
        }
    }

    [Fact]
    public void Berkas_rusak_menghasilkan_catatan_kosong_bukan_galat()
    {
        string folder = TempFolder("rusak");
        IdmSertifikatStore.FolderOverride = folder;
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "idm-sertifikat.json"), "{ bukan json");

            Assert.Null(new IdmSertifikatStore().Muat());
        }
        finally
        {
            IdmSertifikatStore.FolderOverride = null;
            Bersihkan(folder);
        }
    }

    // ===================== Klien TLS =====================

    [Fact]
    public async Task Sertifikat_lokal_terbaca_dengan_benar_status_akan_kedaluwarsa()
    {
        // Validasi baku mematikan sertifikat self-signed dan yang kedaluwarsa.
        // Karena Windows menolak server TLS memakai sertifikat kedaluwarsa, uji
        // lokal pakai sertifikat berlaku dengan NotAfter mendekati (status
        // AkanKedaluwarsa) — menguji bahwa klien membaca NotAfter, Subjek, dan
        // status dengan benar meski validasi dinonaktifkan (akanKedaluwarsa=true).
        using var situs = new SitusTlsLokal(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(10));

        var info = await new IdmSertifikatKlien("127.0.0.1", situs.Port, targetHost: null).PeriksaAsync();

        Assert.NotNull(info);
        Assert.Contains("idm.kemendesa.go.id", info!.Subjek);
        Assert.True(
            (info.BerlakuSampai - situs.Sertifikat.NotAfter).Duration() < TimeSpan.FromSeconds(2),
            $"NotAfter terbaca {info.BerlakuSampai:O}, situs menyajikan {situs.Sertifikat.NotAfter:O}");
        Assert.Equal(
            StatusSertifikatIdm.AkanKedaluwarsa,
            IdmSertifikatStatus.Nilai(info.BerlakuSampai, DateTime.Now));
    }

    [Fact]
    public async Task Sertifikat_lokal_terbaca_dengan_benar_status_sehat()
    {
        using var situs = new SitusTlsLokal(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(365));

        var info = await new IdmSertifikatKlien("127.0.0.1", situs.Port, targetHost: null).PeriksaAsync();

        Assert.NotNull(info);
        Assert.Contains("idm.kemendesa.go.id", info!.Subjek);
        Assert.True(
            (info.BerlakuSampai - situs.Sertifikat.NotAfter).Duration() < TimeSpan.FromSeconds(2),
            $"NotAfter terbaca {info.BerlakuSampai:O}, situs menyajikan {situs.Sertifikat.NotAfter:O}");
        Assert.Equal(
            StatusSertifikatIdm.Sehat,
            IdmSertifikatStatus.Nilai(info.BerlakuSampai, DateTime.Now));
    }

    [Fact]
    public async Task Situs_tak_terjangkau_menghasilkan_null_bukan_galat()
    {
        // Port yang pernah mendengarkan lalu ditutup — sambungan pasti ditolak.
        var penjaga = new TcpListener(IPAddress.Loopback, 0);
        penjaga.Start();
        int port = ((IPEndPoint)penjaga.LocalEndpoint).Port;
        penjaga.Stop();

        var info = await new IdmSertifikatKlien("127.0.0.1", port).PeriksaAsync();

        Assert.Null(info);
    }

    // ===================== Pembantu =====================

    private static string TempFolder(string nama) =>
        Path.Combine(Path.GetTempPath(), $"sudesapp-sertifikat-{nama}-{Guid.NewGuid():N}");

    private static void Bersihkan(string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// Server TLS loopback dengan sertifikat self-signed berumur bebas.
    /// Validasi klien sengaja dimatikan oleh <see cref="IdmSertifikatKlien"/>,
    /// jadi sertifikat kedaluwarsa pun tetap disajikan dan terbaca.
    /// </summary>
    private sealed class SitusTlsLokal : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly RSA _kunci;
        private readonly CancellationTokenSource _batal = new();

        public X509Certificate2 Sertifikat { get; }
        public int Port { get; }

        public SitusTlsLokal(DateTimeOffset berlakuDari, DateTimeOffset berlakuSampai)
        {
            _kunci = RSA.Create(2048);
            var permintaan = new CertificateRequest(
                "CN=idm.kemendesa.go.id", _kunci, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            // Prolongs EKU Server Authentication supaya Schannel bisa memakai
            // sertifikat ini untuk autentikasi server (bukan sekadar signing).
            permintaan.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            var sesudahBuat = permintaan.CreateSelfSigned(berlakuDari, berlakuSampai);

            // Kunci ephemeral dari CreateSelfSigned tidak didukung Schannel untuk
            // autentikasi server; ekspor ke PFX lalu muat ulang supaya kuncinya
            // persisten (bisa dipakai sebagai kredensial server Schannel).
            var pfx = sesudahBuat.Export(X509ContentType.Pfx, "sudesapp-uji-sertifikat");
#pragma warning disable SYSLIB0057 // Load certificate from byte array is obsolete.
            Sertifikat = new X509Certificate2(pfx, "sudesapp-uji-sertifikat", X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = LayaniAsync();
        }

        private async Task LayaniAsync()
        {
            try
            {
                while (!_batal.IsCancellationRequested)
                {
                    var tcp = await _listener.AcceptTcpClientAsync(_batal.Token);
                    _ = TanganiAsync(tcp);
                }
            }
            catch (Exception)
            {
                // Listener dihentikan oleh Dispose — akhir normal uji, bukan kegagalan.
            }
        }

        private async Task TanganiAsync(TcpClient tcp)
        {
            using (tcp)
            {
                try
                {
                    using var aliran = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
                    await aliran.AuthenticateAsServerAsync(
                        Sertifikat, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

                    // Tahan sampai klien menutup setelah membaca tanggal berlaku.
                    var baca = new byte[1];
                    while (!_batal.IsCancellationRequested && await aliran.ReadAsync(baca, _batal.Token) > 0)
                    {
                        // Membuang sisa aliran sampai EOF.
                    }
                }
                catch (Exception)
                {
                    // Klien menutup lebih dulu / server dihentikan — wajar pada uji.
                }
            }
        }

        public void Dispose()
        {
            _batal.Cancel();
            _listener.Stop();
            Sertifikat.Dispose();
            _kunci.Dispose();
            _batal.Dispose();
        }
    }
}
