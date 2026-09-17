using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

        public NotificationItem(NotificationType type, string title, string message)
        {
            Type = type;
            Title = title;
            Message = message;
            CreatedAt = DateTime.Now;
            IsRead = false;

            Icon = type switch
            {
                NotificationType.Success => "\u2714",   // ✔
                NotificationType.Warning => "\u26A0",   // ⚠
                NotificationType.Error => "\u2715",     // ✕
                _ => "\u2139"                            // ℹ
            };
        }
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
        /// Dibangkitkan setiap ada notifikasi baru (di UI thread) — dipakai
        /// MainWindow untuk menganimasikan lonceng status bar berayun.
        /// </summary>
        public event EventHandler? NotificationAdded;

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

        /// <summary>
        /// Tambah notifikasi baru ke puncak daftar. Aman dipanggil dari thread
        /// mana pun (polling WA, auto-proses) — pekerjaan UI dijadwalkan dengan
        /// BeginInvoke sehingga pemanggil tidak pernah menunggu UI (ringan).
        /// </summary>
        public void Add(NotificationType type, string title, string message)
        {
            var item = new NotificationItem(type, title, message);
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

            NotificationAdded?.Invoke(this, EventArgs.Empty);
        }

        public void Info(string title, string message) => Add(NotificationType.Info, title, message);
        public void Success(string title, string message) => Add(NotificationType.Success, title, message);
        public void Warning(string title, string message) => Add(NotificationType.Warning, title, message);
        public void Error(string title, string message) => Add(NotificationType.Error, title, message);

        /// <summary>
        /// Tandai seluruh notifikasi sudah dibaca (badge lonceng kembali kosong).
        /// Dipanggil otomatis saat popup lonceng dibuka.
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
            });
        }
    }
}
