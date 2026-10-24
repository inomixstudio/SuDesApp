using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pengingat informasi terbaru dari penyedia: menagih
    /// <c>GET /pengumuman</c> sejak aplikasi dibuka lalu tiap 60 detik, dan
    /// untuk setiap pengumuman yang BELUM pernah tampil di perangkat ini:
    ///   1. memunculkan popup (PengumumanPopupWindow) — selalu tampil, sediakan
    ///      sekali per pengumuman, memakai aksen 📌 sesuai jenisnya;
    ///   2. menambah entri lonceng notifikasi (NotificationService) supaya yang
    ///      tertutup popup-nya tetap terbaca di lonceng;
    ///   3. menandai id-nya "terlihat" (PengumumanTerlihatStore) SEKETIKA, jadi
    ///      aplikasi yang ditutup di tengah popup tidak menampilkan pengumuman
    ///      yang sama lagi setelah dibuka.
    ///
    /// "Realtime" di sini berarti dekat: jeda terpanjang antara operator
    /// menerbitkan dan pengguna melihat ≈ 60 detik. Tanpa WebSocket — jalurnya
    /// tetap HTTP publik biasa, dan server yang tidak terjangkau (instalasi
    /// offline) adalah keadaan normal: layanan ini diam, tidak mengganggu.
    /// </summary>
    public class PengumumanLatar
    {
        /// <summary>jeda setelah jendela utama tampil sebelum penagihan pertama.</summary>
        private static readonly TimeSpan JedaAwal = TimeSpan.FromSeconds(15);

        /// <summary>Interval antar penagihan setelah putaran pertama.</summary>
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        private readonly PengumumanKlien _klien;
        private readonly NotificationService _notifications;
        private readonly ILogger<PengumumanLatar> _logger;
        private System.Windows.Threading.DispatcherTimer? _timer;
        private bool _sedangMuat;
        private bool _putaranPertama = true;

        public PengumumanLatar(
            PengumumanKlien klien, NotificationService notifications, ILogger<PengumumanLatar> logger)
        {
            _klien = klien ?? throw new ArgumentNullException(nameof(klien));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Mulai pemantauan. Aman dipanggil berkali-kali (dipanggil dari
        /// OnStartup). Timer hidup di thread UI (DispatcherTimer), sehingga
        /// popup dan lonceng langsung dikerjakan di UI tanpa Invoke tambahan.
        /// </summary>
        public void Mulai()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _logger.LogWarning("Layanan pengumuman tidak dimulai — tak ada Dispatcher (mode uji?).");
                return;
            }

            dispatcher.Invoke(() =>
            {
                if (_timer != null)
                {
                    if (!_timer.IsEnabled) _timer.Start();
                    return;
                }

                // Putaran pertama memakai JedaAwal supaya penagihan tidak
                // ikut memperlambat penggambaran jendela utama yang baru tampil.
                _timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = JedaAwal
                };
                _timer.Tick += async (_, _) => await PeriksaSekaliAsync();
                _timer.Start();
            });
        }

        /// <summary>Berhenti (dipanggil saat aplikasi menutup).</summary>
        public void Berhenti() => _timer?.Stop();

        /// <summary>
        /// Satu putaran: muat → saring yang belum tampil → popup + lonceng →
        /// tandai terlihat. Tidak pernah melempar; guard _sedangMuat mencegah
        /// dua putaran tumpang-tindih bila jawaban lambat.
        /// </summary>
        private async Task PeriksaSekaliAsync()
        {
            if (_sedangMuat)
            {
                return;
            }

            _sedangMuat = true;
            try
            {
                if (_putaranPertama)
                {
                    _putaranPertama = false;
                    if (_timer != null) _timer.Interval = Interval;
                }

                var semua = await _klien.MuatAsync().ConfigureAwait(true);
                var baru = PengumumanPemilih.SaringBelumDitampilkan(
                    semua, PengumumanTerlihatStore.Muat());
                if (baru.Count == 0)
                {
                    return;
                }

                var tampilan = baru.Select(p => new ItemPengumumanTampil(p)).ToList();

                Application.Current?.Dispatcher.Invoke(() => Tampilkan(baru, tampilan));

                // Penandaan dilakukan SEKETIKA setelah popup terkirim, bukan saat popup
                // ditutup — jadi aplikasi ditutup di tengah tampilan tidak
                // menampilkan pengumuman yang sama lagi.
                PengumumanTerlihatStore.TandaiTerlihat(baru.Select(p => p.Id));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Putaran pengumuman gagal.");
            }
            finally
            {
                _sedangMuat = false;
            }
        }

        /// <summary>Isi lonceng + buka popup. Dijalankan di thread UI.</summary>
        private void Tampilkan(List<Pengumuman> baru, List<ItemPengumumanTampil> tampilan)
        {
            foreach (var pasang in baru.Zip(tampilan, (sumber, baris) => (sumber, baris)))
            {
                var peringatan = pasang.sumber.Jenis.Equals("peringatan", StringComparison.OrdinalIgnoreCase);
                if (peringatan)
                {
                    _notifications.Warning(pasang.baris.Judul, pasang.baris.Isi);
                }
                else
                {
                    _notifications.Info(pasang.baris.Judul, pasang.baris.Isi);
                }
            }

            PengumumanPopupWindow.Tampilkan(tampilan);
        }
    }

    /// <summary>Baris tampilan satu pengumuman — popup dan lonceng memakai bentuk yang sama.</summary>
    public sealed class ItemPengumumanTampil
    {
        public ItemPengumumanTampil(Pengumuman p)
        {
            Judul = p.Judul;
            Isi = p.Isi ?? string.Empty;
            WaktuTampil = p.Dibuat.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            AdalahPeringatan = p.Jenis.Equals("peringatan", StringComparison.OrdinalIgnoreCase);
        }

        public string Judul { get; }
        public string Isi { get; }
        public string WaktuTampil { get; }

        /// <summary>True bila jenis pengumuman "peringatan" — baris diberi aksen ⚠/warna.</summary>
        public bool AdalahPeringatan { get; }

        /// <summary>Ikon tampilan menurut jenis.</summary>
        public string Ikon => AdalahPeringatan ? "⚠" : "ℹ";
    }
}
