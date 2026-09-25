using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Baris tampilan riwayat aktivitas.</summary>
    public class ActivityRow
    {
        public DateTime Waktu { get; set; }
        public string WaktuDisplay => Waktu.ToString("dd-MM-yyyy HH:mm:ss");
        public string Pengguna { get; set; } = "";
        public string JenisDokumen { get; set; } = "";
        public string Dokumen { get; set; } = "";
        public string Aksi { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Halaman Riwayat Aktivitas: siapa (email Google / admin) melakukan apa
    /// terhadap surat dan arsip — buat, edit, ubah status, hapus — lengkap
    /// dengan waktu dan pencarian. Sumber data: tabel ActivityLog.
    /// </summary>
    public class RiwayatViewModel : ObservableObject
    {
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<RiwayatViewModel> _logger;
        private readonly NavigationService _navigation;

        private bool _isBusy;
        private string _statusText = string.Empty;
        private string _searchText = string.Empty;

        /// <summary>Penjadwal muat ulang saat pengguna mengetik di kotak pencarian.</summary>
        private CancellationTokenSource? _debouncePencarian;

        public RiwayatViewModel(
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<RiwayatViewModel> logger,
            NavigationService navigation)
        {
            _activityLog = activityLog;
            _messageService = messageService;
            _logger = logger;
            _navigation = navigation;

            RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
            ClearCommand = new AsyncRelayCommand(ClearAsync, () => !IsBusy && Items.Count > 0);
            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        public string HeaderTitle => "RIWAYAT AKTIVITAS";
        public string HeaderSubtitle => $"Sesi aktif: {SessionContext.Display} ({SessionContext.LoginMethod}) — mencatat siapa membuat/mengubah surat & arsip";

        /// <summary>
        /// Baris riwayat yang tampil. Diganti sebagai satu koleksi utuh setiap kali
        /// dimuat — jauh lebih ringan daripada menambah 500 baris satu per satu ke
        /// koleksi yang sudah terikat ke DataGrid.
        /// </summary>
        public ObservableCollection<ActivityRow> Items { get; private set; } = new();

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetProperty(ref _isBusy, value)) ((AsyncRelayCommand)ClearCommand).RaiseCanExecuteChanged(); }
        }

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        /// <summary>Pencarian bebas: pengguna, jenis, nomor dokumen, aksi, atau detail.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    JadwalkanMuatUlang();
                }
            }
        }

        /// <summary>
        /// Tunda pemuatan ±300 ms setelah ketikan terakhir lalu muat SEKALI —
        /// mengetik tidak lagi memicu satu query database per huruf.
        /// </summary>
        private void JadwalkanMuatUlang()
        {
            var sebelumnya = _debouncePencarian;
            var baru = new CancellationTokenSource();
            _debouncePencarian = baru;
            sebelumnya?.Cancel();
            sebelumnya?.Dispose();

            _ = MuatTertundaAsync(baru.Token);
        }

        private async Task MuatTertundaAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(300, cancellationToken);

                // Bila pemuatan lain sedang berjalan, tunggu sampai selesai supaya
                // hasil pencarian terakhir tidak hilang tertelan penjaga IsBusy.
                for (int i = 0; i < 20 && IsBusy; i++)
                {
                    await Task.Delay(100, cancellationToken);
                }

                await LoadAsync();
            }
            catch (OperationCanceledException)
            {
                // Ketikan berikutnya menggantikan jadwal ini.
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand BatalCommand { get; }

        /// <summary>Muat riwayat (dipanggil saat halaman dibuka dan saat mencari).</summary>
        public async Task LoadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                StatusText = "Memuat riwayat...";
                var entries = await _activityLog.GetRecentAsync(500, string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim());

                var baris = new List<ActivityRow>();
                foreach (var e in entries)
                {
                    baris.Add(new ActivityRow
                    {
                        Waktu = e.Waktu,
                        Pengguna = e.Pengguna,
                        JenisDokumen = e.JenisDokumen,
                        Dokumen = e.Dokumen,
                        Aksi = e.Aksi,
                        Detail = e.Detail
                    });
                }

                // Sekali ganti untuk seluruh baris (bukan ratusan notifikasi ke UI).
                Items = new ObservableCollection<ActivityRow>(baris);
                OnPropertyChanged(nameof(Items));
                StatusText = Items.Count == 0
                    ? "Belum ada aktivitas tercatat."
                    : $"Menampilkan {Items.Count} aktivitas terakhir.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat riwayat aktivitas");
                StatusText = "Gagal memuat riwayat.";
                await _messageService.ShowErrorAsync("Gagal memuat riwayat aktivitas.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ClearAsync()
        {
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Bersihkan Riwayat",
                "Hapus seluruh riwayat aktivitas?\n\nTindakan ini tidak dapat dibatalkan.");
            if (!confirmed) return;

            IsBusy = true;
            try
            {
                await _activityLog.ClearAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membersihkan riwayat aktivitas");
                await _messageService.ShowErrorAsync("Gagal membersihkan riwayat.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
