using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Interface;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Baris grid permintaan surat online (WhatsApp). Implementasikan
    /// INotifyPropertyChanged agar status/catatan terbaru langsung terlihat
    /// di grid dan panel detail tanpa membangun ulang daftar.
    /// </summary>
    public class PermintaanWaRow : ObservableObject
    {
        public PermintaanWaRow(PermintaanWa data)
        {
            Data = data;
        }

        /// <summary>
        /// Data terbaru dari database. DIPERBARUI setiap kali daftar disegarkan:
        /// panel detail membaca IdSurat/Catatan/PesanMentah dari sini, jadi data
        /// lama akan menampilkan status yang sudah tidak berlaku (mis. surat
        /// sudah dibuat tetapi belum tertulis "terhubung").
        /// </summary>
        public PermintaanWa Data { get; private set; }
        public int Id => Data.ID_Permintaan;

        private string _kode = string.Empty;
        public string Kode { get => _kode; private set => SetProperty(ref _kode, value); }

        private string _tanggal = string.Empty;
        public string Tanggal { get => _tanggal; private set => SetProperty(ref _tanggal, value); }

        private string _nama = string.Empty;
        public string Nama { get => _nama; private set => SetProperty(ref _nama, value); }

        private string _nik = string.Empty;
        public string Nik { get => _nik; private set => SetProperty(ref _nik, value); }

        private string _jenis = string.Empty;
        public string Jenis { get => _jenis; private set => SetProperty(ref _jenis, value); }

        private string _status = string.Empty;
        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        private string _nomorWa = string.Empty;
        public string NomorWa { get => _nomorWa; private set => SetProperty(ref _nomorWa, value); }

        private string _sumber = string.Empty;
        /// <summary>Asal permintaan: WhatsApp (percakapan) atau Google Sheet (form).</summary>
        public string Sumber { get => _sumber; private set => SetProperty(ref _sumber, value); }

        private bool _isRead;
        public bool IsRead { get => _isRead; private set => SetProperty(ref _isRead, value); }

        /// <summary>Menyalin ulang nilai dari database dan menaikkan perubahan properti.</summary>
        public void UpdateFrom(PermintaanWa data)
        {
            Data = data;
            Kode = data.KodePermintaan;
            Tanggal = data.TanggalPermintaan.ToString("dd-MM-yyyy HH:mm");
            Nama = data.NamaWarga ?? string.Empty;
            Nik = data.NIK ?? string.Empty;
            Jenis = WaFormatParser.TampilanJenis(data.NamaJenis);
            Status = data.Status;
            NomorWa = data.NomorWA;
            Sumber = WaRequestStatus.TampilanSumber(data.Sumber);
            IsRead = data.IsRead;
            OnPropertyChanged(nameof(DetailPesan));
            OnPropertyChanged(nameof(CatatanTampil));
            OnPropertyChanged(nameof(AdaCatatan));
            OnPropertyChanged(nameof(RingkasanDetail));
        }

        public string CatatanTampil =>
            string.IsNullOrWhiteSpace(Data.Catatan) ? string.Empty : "Catatan: " + Data.Catatan;

        public bool AdaCatatan => !string.IsNullOrWhiteSpace(Data.Catatan);

        public string DetailPesan => Data.PesanMentah ?? string.Empty;

        public string RingkasanDetail
        {
            get
            {
                var s = "Kode     : " + Kode + Environment.NewLine +
                        "Tanggal  : " + Tanggal + Environment.NewLine +
                        "Nama     : " + Nama + Environment.NewLine +
                        "NIK      : " + Nik + Environment.NewLine +
                        "Jenis    : " + Jenis + Environment.NewLine +
                        "Status   : " + Status + Environment.NewLine +
                        "No. WA   : " + NomorWa + Environment.NewLine +
                        "Sumber   : " + Sumber;
                if (Data.IdSurat.HasValue && Data.IdSurat.Value > 0)
                    s += Environment.NewLine + "Surat    : terhubung (ID " + Data.IdSurat.Value + ") — PDF siap dikirim.";
                if (Data.TanggalDiproses.HasValue)
                    s += Environment.NewLine + "Diproses : " + Data.TanggalDiproses.Value.ToString("dd-MM-yyyy HH:mm");
                return s;
            }
        }
    }

    /// <summary>
    /// Panel operator untuk permintaan surat online via WhatsApp — padanan
    /// WaPanelUserControl (WinForms): daftar permintaan, setujui (buat surat),
    /// tolak, tandai dibaca, dan kirim tautan unduh.
    /// </summary>
    public class WaPanelViewModel : ObservableObject
    {
        private static readonly string[] StatusFilter =
        {
            "SEMUA",
            WaRequestStatus.BARU,
            WaRequestStatus.DIPROSES,
            WaRequestStatus.MENUNGGU_FORM,
            WaRequestStatus.SELESAI,
            WaRequestStatus.DITOLAK,
            WaRequestStatus.PERLU_PERBAIKAN
        };

        private readonly IServiceProvider _provider;
        private readonly WaEngine _engine;
        private readonly IMessageService _messageService;
        private readonly ILogger<WaPanelViewModel> _logger;

        private string _selectedFilter = "SEMUA";
        private string _ringkasan = string.Empty;
        private PermintaanWaRow? _selected;
        private bool _isLoading;
        private bool _refreshPending;

        public WaPanelViewModel(
            IServiceProvider provider,
            WaEngine engine,
            IMessageService messageService,
            ILogger<WaPanelViewModel> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            foreach (var status in StatusFilter)
            {
                FilterOptions.Add(status);
            }

            SegarkanCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
            SetujuiCommand = new AsyncRelayCommand(SetujuiDanBuatSuratAsync, () => !IsLoading && Selected != null);
            TolakCommand = new AsyncRelayCommand(TolakAsync, () => !IsLoading && Selected != null);
            TandaiDibacaCommand = new AsyncRelayCommand(TandaiDibacaAsync, () => !IsLoading && Selected != null);
            KirimPdfCommand = new AsyncRelayCommand(KirimTautanAsync, () => !IsLoading && Selected != null);

            _engine.OutboundSent += OnOutboundSent;
            _engine.RequestCreated += OnRequestCreated;
        }

        /// <summary>Dinaikkan setelah daftar di-refresh (untuk pembaruan badge menu).</summary>
        public event Action? UnreadCountChanged;

        public ObservableCollection<string> FilterOptions { get; } = new();
        public ObservableCollection<PermintaanWaRow> Items { get; } = new();
        public ObservableCollection<string> RiwayatLog { get; } = new();

        public AsyncRelayCommand SegarkanCommand { get; }
        public AsyncRelayCommand SetujuiCommand { get; }
        public AsyncRelayCommand TolakCommand { get; }
        public AsyncRelayCommand TandaiDibacaCommand { get; }
        /// <summary>Kirim tautan unduh PDF (teks/link saja, tanpa lampiran berkas).</summary>
        public AsyncRelayCommand KirimPdfCommand { get; }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (SetProperty(ref _selectedFilter, value))
                {
                    _ = RefreshAsync();
                }
            }
        }

        public string Ringkasan
        {
            get => _ringkasan;
            private set => SetProperty(ref _ringkasan, value);
        }

        // ==== Pemberitahuan kesiapan layanan online ====

        private bool _googleRequiredNotConnected;
        /// <summary>
        /// True bila mode tautan (Google Formulir/Sheet) aktif tetapi akun Google
        /// belum terhubung — layanan online belum bisa berfungsi: tautan form
        /// tidak bisa dibuat dan jawaban Sheet tidak bisa dibaca.
        /// </summary>
        public bool GoogleRequiredNotConnected
        {
            get => _googleRequiredNotConnected;
            private set => SetProperty(ref _googleRequiredNotConnected, value);
        }

        /// <summary>Teks banner kesiapan (tampil bila GoogleRequiredNotConnected).</summary>
        public string GoogleRequiredMessage =>
            "⚠️ Layanan online belum berfungsi penuh — akun Google belum terhubung.\n" +
            "Alur ini memakai Google Formulir (tautan untuk warga), Google Sheet (jawaban yang dibaca aplikasi), dan Google Drive (penyimpan PDF surat). " +
            "Masuk lewat tombol 'Masuk dengan Akun Google' di halaman login, lalu klik Segarkan.";

        private bool _waGatewayNotReady;
        /// <summary>True bila gateway WhatsApp aktif tetapi belum dikonfigurasi (token kosong).</summary>
        public bool WaGatewayNotReady
        {
            get => _waGatewayNotReady;
            private set => SetProperty(ref _waGatewayNotReady, value);
        }

        /// <summary>Teks banner gateway belum siap (tampil bila WaGatewayNotReady).</summary>
        public string WaGatewayMessage =>
            "⚠️ Gateway WhatsApp Cloud API terpilih tetapi access token / Phone Number ID masih kosong.\n" +
            "Isi di Pengaturan → Pengaturan Aplikasi → Gateway WhatsApp — pesan ke warga belum bisa terkirim.";

        /// <summary>Hitung status kesiapan layanan (dipanggil tiap refresh panel).</summary>
        private void HitungKesiapanLayanan(IServiceProvider sp)
        {
            try
            {
                var drive = sp.GetRequiredService<GoogleDriveService>();
                var googleOk = drive.IsOAuthEnabled && drive.HasStoredToken();
                GoogleRequiredNotConnected = WaSheetOptions.IsLinkModeEnabled() && !googleOk;

                WaGatewayNotReady = string.IsNullOrWhiteSpace(AppPreferenceStore.GetWaCloudApiToken()) ||
                    string.IsNullOrWhiteSpace(AppPreferenceStore.GetWaCloudApiPhoneId());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghitung kesiapan layanan online");
                GoogleRequiredNotConnected = false;
                WaGatewayNotReady = false;
            }
        }

        public PermintaanWaRow? Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    RaiseRowCommands();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaiseRowCommands();
                }
            }
        }

        private void RaiseRowCommands()
        {
            ((AsyncRelayCommand)SegarkanCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SetujuiCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)TolakCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)TandaiDibacaCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)KirimPdfCommand).RaiseCanExecuteChanged();
        }

        /// <summary>Melepas langganan event engine saat panel ditutup.</summary>
        public void Cleanup()
        {
            _engine.OutboundSent -= OnOutboundSent;
            _engine.RequestCreated -= OnRequestCreated;
        }

        /// <summary>Pemuatan awal (dipanggil navigasi) — padanan RefreshAsync() di InitializeUi WinForms.</summary>
        public async Task LoadAsync()
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            // Guard race: refresh bisa terpicu bersamaan (ganti filter, permintaan
            // baru masuk dari engine, klik Segarkan). Jika sudah berjalan, tandai
            // saja agar diulang sekali lagi setelah selesai — hasil terbaru yang
            // menang, tanpa dua loop menulis ke Items secara bersamaan.
            if (IsLoading)
            {
                _refreshPending = true;
                return;
            }

            try
            {
                IsLoading = true;
                using var scope = _provider.CreateScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IPermintaanWaRepository>();
                var filter = SelectedFilter == "SEMUA" ? null : SelectedFilter;
                var items = await repo.GetAllAsync(filter, default);
                var unread = await repo.CountUnreadAsync(default);

                // Sinkronisasi in-place: baris lama di-update (status terbaru tetap
                // terlihat, dan Selected tidak menghilang saat refresh), baris baru
                // ditambahkan, baris yang tidak ada lagi dibuang.
                var byId = new System.Collections.Generic.Dictionary<int, PermintaanWaRow>();
                foreach (var r in Items) byId[r.Id] = r;

                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (!byId.ContainsKey(Items[i].Id)) Items.RemoveAt(i);
                }
                byId.Clear();
                foreach (var r in Items) byId[r.Id] = r;

                foreach (var p in items)
                {
                    if (byId.TryGetValue(p.ID_Permintaan, out var existing))
                    {
                        existing.UpdateFrom(p);
                    }
                    else
                    {
                        var row = new PermintaanWaRow(p);
                        Items.Add(row);
                        byId[row.Id] = row;
                    }
                }

                Ringkasan = $"{items.Count} permintaan terlihat — {unread} belum dibaca.";
                HitungKesiapanLayanan(sp);
                UnreadCountChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar permintaan");
                await _messageService.ShowErrorAsync(ex.Message);
            }
            finally
            {
                IsLoading = false;
                if (_refreshPending)
                {
                    _refreshPending = false;
                    await RefreshAsync();
                }
            }
        }

        private async Task SetujuiDanBuatSuratAsync()
        {
            var row = Selected;
            if (row == null)
            {
                await _messageService.ShowWarningAsync("Pilih permintaan terlebih dahulu.");
                return;
            }

            using var scope = _provider.CreateScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IPermintaanWaRepository>();

            // Guard proses ganda: pipeline otomatis bisa sudah/sedang memproses
            // permintaan ini. Jangan buat surat dua kali.
            var saatIni = await repo.GetByIdAsync(row.Id, default);
            if (saatIni != null && saatIni.Status == WaRequestStatus.SELESAI)
            {
                await _messageService.ShowInfoAsync(
                    "Permintaan ini sudah diproses otomatis.\n" +
                    "Gunakan 'Kirim PDF via WA' bila perlu mengirim ulang berkasnya.");
                await RefreshAsync();
                return;
            }
            if (saatIni != null && saatIni.Status == WaRequestStatus.DIPROSES)
            {
                await _messageService.ShowWarningAsync(
                    "Permintaan ini sedang diproses otomatis.\n" +
                    "Tunggu beberapa saat lalu tekan Segarkan; bila gagal, statusnya berubah menjadi PERLU_PERBAIKAN dan bisa disetujui manual.");
                return;
            }

            await repo.UpdateStatusAsync(row.Id, WaRequestStatus.DIPROSES, "Sedang diproses", null, null, default);

            WaSuratProcessor.Hasil hasil;
            try
            {
                var processor = sp.GetRequiredService<WaSuratProcessor>();
                hasil = await processor.ProsesAsync(row.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat surat dari permintaan {Id}", row.Id);

                // Rollback status: jangan biarkan permintaan terjebak di DIPROSES.
                try
                {
                    await repo.UpdateStatusAsync(row.Id, WaRequestStatus.PERLU_PERBAIKAN,
                        "Gagal membuat surat: " + ex.Message, null, null, default);
                }
                catch (Exception exStatus)
                {
                    _logger.LogWarning(exStatus, "Gagal mengembalikan status permintaan {Id}", row.Id);
                }

                // Kabari warga agar tidak menunggu balasan yang tidak pernah datang.
                try
                {
                    var p = await repo.GetByIdAsync(row.Id, default);
                    var gateway = sp.GetRequiredService<IWhatsAppGateway>();
                    if (p != null && !string.IsNullOrWhiteSpace(p.NomorWA))
                        await gateway.SendTextAsync(p.NomorWA,
                            "Mohon maaf, permohonan " + WaFormatParser.TampilanJenis(p.NamaJenis) +
                            " (" + p.KodePermintaan + ") belum dapat diproses otomatis.\n" +
                            "Operator akan memeriksanya. Silakan datang ke kantor desa bila mendesak.", default);
                }
                catch (Exception exWa)
                {
                    _logger.LogWarning(exWa, "Gagal kirim balasan gagal proses ke warga");
                }

                await _messageService.ShowErrorAsync($"Gagal membuat surat: {ex.Message}\n\nPermintaan dikembalikan ke status PERLU_PERBAIKAN.");
                await RefreshAsync();
                return;
            }

            var permintaan = await repo.GetByIdAsync(row.Id, default);
            string balasan = $"✅ Surat {WaFormatParser.TampilanJenis(hasil.NamaJenis)} Anda siap.\n" +
                             $"Nomor: {hasil.NomorSurat}\n\n" +
                             "Silakan ambil di kantor desa, atau operator dapat mengirimkan file PDF melalui menu panel ini.";
            try
            {
                var gateway = sp.GetRequiredService<IWhatsAppGateway>();
                if (permintaan != null && !string.IsNullOrWhiteSpace(permintaan.NomorWA))
                    await gateway.SendTextAsync(permintaan.NomorWA, balasan, default);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengirim notifikasi WA siap");
            }

            await _messageService.ShowInfoAsync(
                $"Surat dibuat.\nNomor: {hasil.NomorSurat}\n\nPermintaan ditandai SELESAI.");
            await RefreshAsync();
        }

        private async Task TolakAsync()
        {
            var row = Selected;
            if (row == null)
            {
                await _messageService.ShowWarningAsync("Pilih permintaan terlebih dahulu.");
                return;
            }

            using var scope = _provider.CreateScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IPermintaanWaRepository>();
            var p = await repo.GetByIdAsync(row.Id, default);
            if (p == null) return;

            // Tolak harus disengaja: minta konfirmasi dulu.
            var yakin = await _messageService.ShowConfirmationAsync("Tolak Permintaan",
                "Tolak permohonan " + WaFormatParser.TampilanJenis(p.NamaJenis) +
                " dari " + (p.NamaWarga ?? "warga") + " (" + p.KodePermintaan + ")?\n" +
                "Pesan penolakan akan dikirim ke nomor WhatsApp pemohon.");
            if (!yakin) return;

            if (p.Status == WaRequestStatus.DITOLAK)
            {
                await _messageService.ShowInfoAsync("Permintaan ini sudah berstatus DITOLAK.");
                return;
            }
            if (p.Status == WaRequestStatus.SELESAI)
            {
                await _messageService.ShowWarningAsync(
                    "Permintaan ini sudah SELESAI (surat sudah dibuat dan terkirim).\n" +
                    "Gunakan 'Tolak' hanya pada permintaan yang belum selesai.");
                return;
            }

            await repo.UpdateStatusAsync(row.Id, WaRequestStatus.DITOLAK, "Ditolak operator",
                null, "Maaf, permohonan Anda ditolak. Silakan hubungi kantor desa untuk keterangan lebih lanjut.", default);

            try
            {
                var gateway = sp.GetRequiredService<IWhatsAppGateway>();
                await gateway.SendTextAsync(p.NomorWA, "Maaf, permohonan Anda ditolak oleh operator.", default);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal kirim notifikasi tolak");
            }

            await _messageService.ShowInfoAsync("Permintaan " + p.KodePermintaan + " ditolak.");
            await RefreshAsync();
        }

        private async Task TandaiDibacaAsync()
        {
            using var scope = _provider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
            var row = Selected;
            if (row != null)
            {
                await repo.MarkReadAsync(row.Id, default);
            }
            else
            {
                await repo.MarkReadAllAsync(default);
            }

            await RefreshAsync();
        }

        /// <summary>
        /// Kirim ulang tautan unduh ke warga: generate ulang PDF, unggah ke
        /// folder "Surat Online" di Google Drive, lalu kirim LINK via WhatsApp
        /// (teks saja — aplikasi tidak mengirim berkas).
        /// </summary>
        private async Task KirimTautanAsync()
        {
            var row = Selected;
            if (row == null)
            {
                await _messageService.ShowWarningAsync("Pilih permintaan terlebih dahulu.");
                return;
            }

            string filePath = string.Empty;
            try
            {
                using var scope = _provider.CreateScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IPermintaanWaRepository>();
                var p = await repo.GetByIdAsync(row.Id, default);
                if (p == null) return;
                if (!p.IdSurat.HasValue || p.IdSurat.Value <= 0)
                {
                    await _messageService.ShowWarningAsync("Surat untuk permintaan ini belum dibuat. Klik 'Setujui & Buat Surat' dulu.");
                    return;
                }

                var drive = sp.GetRequiredService<GoogleDriveService>();
                if (!drive.IsOAuthEnabled || !drive.HasStoredToken())
                {
                    await _messageService.ShowWarningAsync("Akun Google belum terhubung — tautan unduh tidak bisa dibuat. Masuk dengan Akun Google terlebih dahulu.");
                    return;
                }

                var suratRepo = sp.GetRequiredService<ISuratRepository>();
                var suratData = await suratRepo.GetByIdAsync(p.IdSurat.Value, default) ??
                    throw new InvalidOperationException("Data surat tidak ditemukan.");

                var generator = SuDesApp.Wpf.Services.SuratPdfHelper.ResolveGenerator(sp, p.NamaJenis);
                if (generator == null)
                {
                    await _messageService.ShowWarningAsync($"Generator PDF untuk '{WaFormatParser.TampilanJenis(p.NamaJenis)}' belum tersedia di WPF.");
                    return;
                }

                filePath = Path.Combine(Path.GetTempPath(), $"Surat_{p.KodePermintaan}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                using (var stream = File.Create(filePath))
                {
                    await generator.GeneratePdfAsync(stream, suratData, suratData.Keterangan);
                }

                var folderId = await drive.FindOrCreateFolderAsync("Surat Online", null, default);
                var uploaded = await drive.UploadFileAsync(filePath, folderId, Path.GetFileName(filePath), null, default);
                var link = drive.ToWebViewLink(uploaded.Id);

                var gateway = sp.GetRequiredService<IWhatsAppGateway>();
                var pesan = "✅ Surat Anda sudah siap.\n" +
                            $"Jenis   : {WaFormatParser.TampilanJenis(p.NamaJenis)}\n" +
                            $"Nomor   : {suratData.NomorSurat}\n\n" +
                            "Unduh PDF surat Anda di tautan berikut:\n" +
                            link + "\n\n" +
                            "Terima kasih.";
                var send = await gateway.SendTextAsync(p.NomorWA, pesan);
                if (!send.Success)
                    await _messageService.ShowWarningAsync($"Gagal mengirim tautan: {send.Status} {send.Detail}");
                else
                    await _messageService.ShowInfoAsync("Tautan unduh surat terkirim ke " + p.NomorWA + ".");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengirim tautan unduh");
                await _messageService.ShowErrorAsync(ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    try { if (File.Exists(filePath)) File.Delete(filePath); }
                    catch { /* file mungkin masih terpakai */ }
                }
            }
        }

        private void OnOutboundSent(object? sender, WaOutboundLogEntry e)
        {
            string line = string.IsNullOrEmpty(e.FilePath)
                ? $"[{e.SentAt:HH:mm:ss}] {e.To}: {e.Text}"
                : $"[{e.SentAt:HH:mm:ss}] {e.To}: \uD83D\uDCC4 {e.FilePath}";

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                RiwayatLog.Add(line);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => RiwayatLog.Add(line)));
            }
        }

        private void OnRequestCreated(object? sender, PermintaanWa p)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                _ = RefreshAsync();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
            }
        }
    }
}