using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.Services
{
    /// <summary>Jenis notifikasi — menentukan ikon &amp; warna aksen pada popup.</summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>Satu item notifikasi pada popup lonceng.</summary>
    public class NotificationItem : ObservableObject
    {
        private bool _isRead;

        public NotificationType Type { get; }
        public string Icon { get; }
        public string Title { get; }
        public string Message { get; }
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Kunci halaman tujuan bila notifikasi diklik (mis. "pembaruan",
        /// "layanan-online"). Kosong = tidak bisa diklik untuk navigasi.
        /// Dipetakan oleh MainWindowViewModel.BukaTujuanNotifikasiAsync.
        /// </summary>
        public string TujuanMenu { get; }

        /// <summary>True bila mengklik notifikasi ini membuka halaman terkait.</summary>
        public bool BisaDiklik => !string.IsNullOrEmpty(TujuanMenu);

        public bool IsRead
        {
            get => _isRead;
            set => SetProperty(ref _isRead, value);
        }

        public string TimeDisplay
        {
            get
            {
                var elapsed = DateTime.Now - CreatedAt;
                if (elapsed.TotalMinutes < 1) return "baru saja";
                if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} mnt lalu";
                if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} jam lalu";
                return CreatedAt.ToString("dd/MM/yyyy HH:mm");
            }
        }

        private IReadOnlyList<string>? _messageLines;

        /// <summary>
        /// Pesan terpecah per baris — dirender sebagai baris terpisah (multi-line)
        /// tanpa ScrollViewer di dalam item.
        /// </summary>
        public IReadOnlyList<string> MessageLines =>
            _messageLines ??= (Message ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n')
                .SkipWhile(l => l.Trim().Length == 0)
                .ToArray();

        public NotificationItem(NotificationType type, string title, string message, string? tujuanMenu = null)
        {
            Type = type;
            Title = title;
            Message = message;
            CreatedAt = DateTime.Now;
            IsRead = false;
            TujuanMenu = tujuanMenu ?? string.Empty;

            Icon = type switch
            {
                NotificationType.Success => "\u2714",   // ✔
                NotificationType.Warning => "\u26A0",   // ⚠
                NotificationType.Error => "\u2715",     // ✕
                _ => "\u2139"                            // ℹ
            };
        }

        /// <summary>Segarkan tampilan waktu relatif (dipanggil timer layanan).</summary>
        public void SegarkanWaktu() => OnPropertyChanged(nameof(TimeDisplay));
    }

    /// <summary>
    /// Pusat notifikasi in-app (ikon lonceng pada status bar, ala Visual Studio):
    /// modul lain cukup memanggil Add/Info/Success/Warning/Error untuk mengirim
    /// pemberitahuan; badge angka pada lonceng = jumlah notifikasi belum dibaca.
    /// </summary>
    public class NotificationService : ObservableObject
    {
        private const int MaxItems = 50;

        private readonly ILogger<NotificationService> _logger;

        public NotificationService(ILogger<NotificationService> logger)
        {
            _logger = logger;
        }

        /// <summary>Daftar notifikasi (terbaru di atas), dibatasi MaxItems.</summary>
        public ObservableCollection<NotificationItem> Items { get; } = new();

        /// <summary>
        /// Dibangkitkan setiap ada notifikasi baru (di UI thread) membawa itemnya —
        /// dipakai MainWindow untuk menganimasikan lonceng berayun dan menampilkan
        /// toast untuk jenis Warning/Error.
        /// </summary>
        public event EventHandler<NotificationItem>? NotificationAdded;

        private int _unreadCount;

        /// <summary>Jumlah notifikasi yang belum dibaca — angka pada badge lonceng.</summary>
        public int UnreadCount
        {
            get => _unreadCount;
            private set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    OnPropertyChanged(nameof(HasUnread));
                    OnPropertyChanged(nameof(UnreadCountText));
                }
            }
        }

        public bool HasUnread => UnreadCount > 0;

        /// <summary>Teks badge: kosong bila tidak ada, angka bila ada (99+ bila lebih).</summary>
        public string UnreadCountText => UnreadCount > 99 ? "99+" : UnreadCount > 0 ? UnreadCount.ToString() : "";

        private DispatcherTimer? _timerWaktu;

        /// <summary>
        /// Timer 30 detik yang menyegarkan waktu relatif ("baru saja", "5 mnt lalu")
        /// pada seluruh item — tanpa ini teks waktu membeku pada nilai saat dibuat.
        /// Berhenti sendiri saat daftar kosong.
        /// </summary>
        private void PastikanTimerWaktu()
        {
            if (_timerWaktu != null)
            {
                if (!_timerWaktu.IsEnabled) _timerWaktu.Start();
                return;
            }

            _timerWaktu = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timerWaktu.Tick += (_, _) =>
            {
                foreach (var item in Items)
                {
                    item.SegarkanWaktu();
                }

                if (Items.Count == 0) _timerWaktu.Stop();
            };
            _timerWaktu.Start();
        }

        /// <summary>
        /// Tambah notifikasi baru ke puncak daftar. Aman dipanggil dari thread
        /// mana pun (polling WA, auto-proses) — pekerjaan UI dijadwalkan dengan
        /// BeginInvoke sehingga pemanggil tidak pernah menunggu UI (ringan).
        /// <paramref name="tujuanMenu"/> opsional: kunci halaman yang dibuka saat
        /// notifikasi diklik (lihat MainWindowViewModel.BukaTujuanNotifikasiAsync).
        /// </summary>
        public void Add(NotificationType type, string title, string message, string? tujuanMenu = null)
        {
            var item = new NotificationItem(type, title, message, tujuanMenu);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                AddCore(item);
            }
            else
            {
                _ = dispatcher.BeginInvoke(new Action(() => AddCore(item)));
            }

            // Log di luar antrean UI: I/O log tidak ikut membebani UI thread.
            _logger.LogInformation("Notifikasi ({Type}): {Title} — {Message}", type, title, message);
        }

        private void AddCore(NotificationItem item)
        {
            PastikanTimerWaktu();

            var removedUnread = false;
            Items.Insert(0, item);
            if (Items.Count > MaxItems)
            {
                var last = Items[Items.Count - 1];
                removedUnread = !last.IsRead;
                Items.RemoveAt(Items.Count - 1);
            }

            // Inkremental: jumlah belum-baca dihitung tanpa memindai seluruh daftar.
            if (!removedUnread) UnreadCount++;

            NotificationAdded?.Invoke(this, item);
        }

        public void Info(string title, string message, string? tujuanMenu = null) => Add(NotificationType.Info, title, message, tujuanMenu);
        public void Success(string title, string message, string? tujuanMenu = null) => Add(NotificationType.Success, title, message, tujuanMenu);
        public void Warning(string title, string message, string? tujuanMenu = null) => Add(NotificationType.Warning, title, message, tujuanMenu);
        public void Error(string title, string message, string? tujuanMenu = null) => Add(NotificationType.Error, title, message, tujuanMenu);

        /// <summary>
        /// Cari notifikasi lama yang sudah tidak berlaku lagi: judulnya sama, dan — bila
        /// <paramref name="memuat"/> diberikan — isinya memuat teks tertentu.
        ///
        /// Dipisah sebagai fungsi murni supaya aturan "kabar mana yang sudah usang" bisa
        /// diperiksa tanpa menyentuh UI.
        /// </summary>
        public static List<NotificationItem> CariPesanLama(
            IEnumerable<NotificationItem> daftar, string judul, string? memuat = null)
            => (daftar ?? Array.Empty<NotificationItem>())
                .Where(i => i != null
                            && string.Equals(i.Title, judul, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrEmpty(memuat)
                                || i.Message.Contains(memuat!, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        /// <summary>
        /// Hapus kabar lama yang sudah tidak berlaku — mis. "surat warga ditahan" untuk
        /// permintaan yang kini sudah dilanjutkan sendiri. Tanpa ini, lonceng terus
        /// meminta operator mengerjakan sesuatu yang sudah beres.
        /// </summary>
        /// <returns>Jumlah kabar lama yang cocok (dan dihapus bila UI tersedia).</returns>
        public int HapusPesanLama(string judul, string? memuat = null)
        {
            var usang = CariPesanLama(Items, judul, memuat);
            if (usang.Count == 0) return 0;

            // Aplikasi berjalan normal: penghapusan dijadwalkan ke thread UI seperti Add.
            var dispatcher = Application.Current?.Dispatcher;
            void Hapus()
            {
                foreach (var item in usang)
                {
                    if (Items.Remove(item) && !item.IsRead) UnreadCount--;
                }

                if (Items.Count == 0) _timerWaktu?.Stop();
            }

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Hapus();
            }
            else
            {
                _ = dispatcher.BeginInvoke(new Action(Hapus));
            }

            return usang.Count;
        }

        /// <summary>Hapus satu notifikasi dari daftar (tombol ✕ per item).</summary>
        public void Hapus(NotificationItem item)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Items.Remove(item) && !item.IsRead)
                {
                    UnreadCount--;
                }

                if (Items.Count == 0) _timerWaktu?.Stop();
            });
        }

        /// <summary>
        /// Tandai seluruh notifikasi sudah dibaca (badge lonceng kembali kosong).
        /// Dipanggil saat flyout DITUTUP — selama flyout terbuka titik belum-baca
        /// tetap terlihat sehingga pengguna tahu mana yang baru.
        /// </summary>
        public void MarkAllRead()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (UnreadCount == 0) return; // cepat: tidak ada pekerjaan
                foreach (var item in Items)
                {
                    if (!item.IsRead) item.IsRead = true;
                }
                UnreadCount = 0;
            });
        }

        /// <summary>Bersihkan seluruh daftar notifikasi.</summary>
        public void Clear()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Items.Clear();
                UnreadCount = 0;
                _timerWaktu?.Stop();
            });
        }
    }
}
