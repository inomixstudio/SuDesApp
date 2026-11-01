using System;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuDesApp.Services
{
    /// <summary>
    /// Keadaan masa berlaku sertifikat TLS situs IDM pusat menurut waktu saat
    /// diperiksa. <see cref="TidakDiketahui"/> berarti belum pernah terbaca
    /// (bukan berarti bermasalah) — peringatan hanya muncul untuk dua keadaan
    /// yang benar-benar mendesak.
    /// </summary>
    public enum StatusSertifikatIdm
    {
        /// <summary>Belum pernah diperiksa atau pemeriksaannya gagal (situs tak terjangkau).</summary>
        TidakDiketahui = 0,

        /// <summary>Sertifikat masih berlaku lebih lama dari batas peringatan dini.</summary>
        Sehat = 1,

        /// <summary>Sertifikat berakhir dalam beberapa hari ke depan — waktunya melapor sebelum rusak.</summary>
        AkanKedaluwarsa = 2,

        /// <summary>Sertifikat sudah lewat masa berlakunya.</summary>
        Kedaluwarsa = 3,
    }

    /// <summary>
    /// Logika murni pemantauan masa berlaku sertifikat situs IDM
    /// (<c>idm.kemendesa.go.id</c>). Terpisah dari klien jaringan supaya
    /// peringatan yang tampil di halaman dan notifikasi lonceng pemantau latar
    /// dihitung dari aturan yang sama, dan aturan itu bisa diuji tanpa internet.
    ///
    /// Latar belakangnya kejadian nyata: sertifikat situs itu kedaluwarsa pada
    /// 15 Agustus 2026, dan halaman IDM mendadak kosong sampai jalur cadangan
    /// HTTP dipasang. Pemeriksaan ini memberi peringatan jauh sebelum titik itu.
    /// </summary>
    public static class IdmSertifikatStatus
    {
        /// <summary>
        /// Batas peringatan dini: sertifikat yang berakhir dalam 30 hari ke depan
        /// sudah dianggap <see cref="StatusSertifikatIdm.AkanKedaluwarsa"/>,
        /// sehingga kantor desa punya waktu melapor ke Kemendesa.
        /// </summary>
        public const int HariPeringatanDini = 30;

        private static readonly CultureInfo Budaya = new("id-ID");

        /// <summary>Nilai keadaan sertifikat pada waktu <paramref name="kini"/>.</summary>
        public static StatusSertifikatIdm Nilai(DateTime? berlakuSampai, DateTime kini)
        {
            if (berlakuSampai is not { } sampai) return StatusSertifikatIdm.TidakDiketahui;
            if (sampai <= kini) return StatusSertifikatIdm.Kedaluwarsa;

            return (sampai - kini).TotalDays <= HariPeringatanDini
                ? StatusSertifikatIdm.AkanKedaluwarsa
                : StatusSertifikatIdm.Sehat;
        }

        /// <summary>Judul notifikasi lonceng pemantau latar (kosong bila tidak ada peringatan).</summary>
        public static string Judul(StatusSertifikatIdm status) => status switch
        {
            StatusSertifikatIdm.Kedaluwarsa => "Sertifikat situs IDM kedaluwarsa",
            StatusSertifikatIdm.AkanKedaluwarsa => "Sertifikat situs IDM segera berakhir",
            _ => "",
        };

        /// <summary>
        /// Kalimat peringatan untuk halaman IDM dan notifikasi — kosong bila
        /// sertifikat sehat atau belum pernah diperiksa, jadi tampilan tidak
        /// pernah menampilkan peringatan tanpa sebab.
        /// </summary>
        public static string Pesan(StatusSertifikatIdm status, DateTime? berlakuSampai, DateTime kini)
        {
            if (berlakuSampai is not { } sampai) return "";

            return status switch
            {
                StatusSertifikatIdm.Kedaluwarsa =>
                    "Sertifikat situs IDM sudah kedaluwarsa sejak "
                    + sampai.ToString("d MMMM yyyy", Budaya)
                    + ". Mulai saat itu data IDM hanya bisa diambil lewat HTTP tanpa enkripsi — "
                    + "laporkan ke Kemendesa agar sertifikatnya diperbarui.",

                StatusSertifikatIdm.AkanKedaluwarsa =>
                    "Sertifikat situs IDM berakhir dalam "
                    + Math.Max(1, (int)Math.Ceiling((sampai - kini).TotalDays))
                    + " hari (" + sampai.ToString("d MMMM yyyy", Budaya) + "). "
                    + "Bila diperbarui terlambat, halaman IDM hanya bisa memuat data tanpa enkripsi — "
                    + "segera laporkan ke Kemendesa.",

                _ => "",
            };
        }
    }

    /// <summary>Hasil pemeriksaan sertifikat TLS situs IDM.</summary>
    /// <param name="BerlakuDari">Tanggal mulai berlaku sertifikat (NotBefore).</param>
    /// <param name="BerlakuSampai">Tanggal akhir berlaku sertifikat (NotAfter) — inti pemantauan.</param>
    /// <param name="Subjek">Subjek sertifikat, untuk bahan pelaporan ke pihak pusat.</param>
    public sealed record SertifikatIdmInfo(DateTime BerlakuDari, DateTime BerlakuSampai, string Subjek);

    /// <summary>Sumber pemeriksaan masa berlaku sertifikat situs IDM.</summary>
    public interface IIdmSertifikatKlien
    {
        /// <summary>
        /// Baca masa berlaku sertifikat TLS situs pusat. Mengembalikan null bila
        /// situs tidak terjangkau — catatan lama tetap dipakai untuk peringatan.
        /// </summary>
        Task<SertifikatIdmInfo?> PeriksaAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Pembaca masa berlaku sertifikat TLS <c>idm.kemendesa.go.id</c>.
    ///
    /// Sengaja memakai <see cref="SslStream"/> dengan validasi yang menerima
    /// sertifikat apa pun: yang dibaca justru tanggal berlakunya, dan sertifikat
    /// yang SUDAH kedaluwarsa adalah kasus yang paling harus terbaca — validasi
    /// baku akan menolaknya tepat pada saat peringatan paling dibutuhkan.
    /// Tidak pernah melempar: kegagalan apa pun menghasilkan null supaya
    /// pemeriksaan ini tidak pernah mengganggu alur yang memakainya.
    /// </summary>
    public sealed class IdmSertifikatKlien : IIdmSertifikatKlien
    {
        /// <summary>Situs yang diperiksa — sama dengan klien data IDM.</summary>
        public const string Host = IdmKemendesaKlien.Host;

        private const int PortBawaan = 443;
        private static readonly TimeSpan BatasWaktu = TimeSpan.FromSeconds(20);

        private readonly string _host;
        private readonly int _port;
        private readonly string? _targetHost;
        private readonly ILogger<IdmSertifikatKlien> _logger;

        /// <param name="host">Override host (untuk uji dengan situs TLS lokal).</param>
        /// <param name="port">Override port (untuk uji dengan situs TLS lokal).</param>
        /// <param name="targetHost">Bila diset, pakai ini sebagai TargetHost TLS; bila null, hostname verification dimatikan (untuk uji dengan sertifikat self-signed).</param>
        public IdmSertifikatKlien(string? host = null, int port = PortBawaan,
            ILogger<IdmSertifikatKlien>? logger = null, string? targetHost = null)
        {
            _host = string.IsNullOrWhiteSpace(host) ? Host : host;
            _port = port;
            _logger = logger ?? NullLogger<IdmSertifikatKlien>.Instance;
            _targetHost = targetHost;
        }

        public async Task<SertifikatIdmInfo?> PeriksaAsync(CancellationToken ct = default)
        {
            try
            {
                using var batas = CancellationTokenSource.CreateLinkedTokenSource(ct);
                batas.CancelAfter(BatasWaktu);

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_host, _port, batas.Token).ConfigureAwait(false);

                // Callback ini menerima sertifikat apa pun (termasuk yang sudah
                // kedaluwarsa) — tujuan pembacaan adalah tanggal berlakunya,
                // bukan mempercayai sertifikat untuk mengambil data.
                using var aliran = new SslStream(tcp.GetStream(), false,
                    (_, _, _, _) => true);

                await aliran.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _targetHost ?? _host,
                }, batas.Token).ConfigureAwait(false);

                var mentah = aliran.RemoteCertificate;
                if (mentah == null)
                {
                    _logger.LogInformation("Situs {Host} tidak menyertakan sertifikat TLS.", _host);
                    return null;
                }

                // RemoteCertificate berbasis X509Certificate (tanpa tanggal);
                // X509CertificateLoader adalah jalur resmi .NET 9+ untuk
                // mendapatkan versi X509Certificate2-nya tanpa API usang.
                using var sertifikat = X509CertificateLoader.LoadCertificate(
                    mentah.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

                return new SertifikatIdmInfo(sertifikat.NotBefore, sertifikat.NotAfter, sertifikat.Subject);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Tak terjangkau / gagal jabatan TLS: bukan kegagalan yang perlu
                // diteruskan — pemeriksaan ini pelengkap, catatan lama tetap berlaku.
                _logger.LogInformation(ex, "Pemeriksaan sertifikat situs {Host} gagal.", _host);
                return null;
            }
        }
    }
}
