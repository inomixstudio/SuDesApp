using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel untuk halaman Pengaturan/Setelan Desa (padanan SetelanForm WinForms).
    /// Memuat, memvalidasi (field wajib, kodepos 5 digit, NIP camat >= 9 digit),
    /// lalu menyimpan data desa. Feedback sukses/gagal ditampilkan ke pengguna
    /// seperti pada WinForms (MessageService).
    /// </summary>
    public class SetelanViewModel : ObservableObject
    {
        private static readonly Regex KodeposRegex = new(@"^\d{5}$", RegexOptions.Compiled);

        /// <summary>Dipicu setelah pengaturan berhasil disimpan (mis. refresh status bar utama).</summary>
        public static event Action? SettingsSaved;

        private readonly SettingsManager _settingsManager;
        private readonly IMessageService _messageService;
        private readonly ILogger<SetelanViewModel> _logger;
        private readonly NavigationService _navigation;

        /// <summary>Nilai tersimpan terakhir — pembanding untuk HasChanges (paritas HasDataChanged WinForms).</summary>
        private DesaData _snapshot = new();

        private string _namaDesa = string.Empty;
        private string _kecamatan = string.Empty;
        private string _kabupaten = string.Empty;
        private string _alamat = string.Empty;
        private string _kodepos = string.Empty;
        private string _kepalaDesa = string.Empty;
        private string _sekdes = string.Empty;
        private string _namaCamat = string.Empty;
        private string _nipCamat = string.Empty;
        private string _golCamat = string.Empty;
        private bool _isLoading;
        private bool _hasChanges;

        public SetelanViewModel(SettingsManager settingsManager, IMessageService messageService, ILogger<SetelanViewModel> logger, NavigationService navigation)
        {
            _settingsManager = settingsManager;
            _messageService = messageService;
            _logger = logger;
            _navigation = navigation;
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasChanges && !IsLoading);
            CancelCommand = new AsyncRelayCommand(CancelAsync);
            GantiLogoCommand = new AsyncRelayCommand(GantiLogoAsync);
            PakaiLogoBawaanCommand = new AsyncRelayCommand(PakaiLogoBawaanAsync);
            _ = LoadAsync();
        }

        // =================================================================
        // Pengaturan cetak (ukuran kertas & logo kop). Disimpan langsung ke
        // preferensi aplikasi, jadi tidak ikut tombol "Simpan" data desa.
        // =================================================================

        private UkuranKertasSurat _ukuranKertas = UkuranKertasSurat.A4;
        private string _jalurLogo = string.Empty;

        /// <summary>Ukuran kertas A4 (bawaan aplikasi).</summary>
        public bool UkuranKertasA4
        {
            get => _ukuranKertas == UkuranKertasSurat.A4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.A4); }
        }

        /// <summary>Ukuran kertas F4/Folio (8,5 x 13 inci).</summary>
        public bool UkuranKertasF4
        {
            get => _ukuranKertas == UkuranKertasSurat.F4;
            set { if (value) TerapkanUkuranKertas(UkuranKertasSurat.F4); }
        }

        /// <summary>Jalur gambar logo kop yang sedang dipakai (untuk pratinjau).</summary>
        public string JalurLogo
        {
            get => _jalurLogo;
            private set
            {
                if (SetProperty(ref _jalurLogo, value))
                {
                    OnPropertyChanged(nameof(AdaLogo));
                    OnPropertyChanged(nameof(KeteranganLogo));
                }
            }
        }

        public bool AdaLogo => !string.IsNullOrWhiteSpace(_jalurLogo);

        /// <summary>Keterangan asal logo: bawaan aplikasi atau pilihan pengguna.</summary>
        public string KeteranganLogo
        {
            get
            {
                if (!AdaLogo)
                {
                    return "Belum ada gambar logo — kop surat akan dicetak tanpa logo.";
                }

                return !string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo())
                    ? $"Logo pilihan Anda: {System.IO.Path.GetFileName(_jalurLogo)}"
                    : "Logo bawaan aplikasi.";
            }
        }

        public AsyncRelayCommand GantiLogoCommand { get; }
        public AsyncRelayCommand PakaiLogoBawaanCommand { get; }

        /// <summary>Simpan pilihan ukuran kertas; langsung berlaku untuk PDF berikutnya.</summary>
        private void TerapkanUkuranKertas(UkuranKertasSurat ukuran)
        {
            if (_ukuranKertas == ukuran)
            {
                return;
            }

            _ukuranKertas = ukuran;
            PengaturanCetak.SetUkuranKertas(ukuran);
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
            _logger.LogInformation("Ukuran kertas PDF diubah menjadi {Ukuran}", ukuran);
        }

        /// <summary>Muat pengaturan cetak yang tersimpan.</summary>
        private void MuatPengaturanCetak()
        {
            _ukuranKertas = PengaturanCetak.GetUkuranKertas();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
        }

        /// <summary>Pilih gambar logo baru dari komputer, lalu salin ke folder data aplikasi.</summary>
        private async Task GantiLogoAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih gambar logo kop surat",
                Filter = "Gambar (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string? tersimpan = PengaturanCetak.SimpanLogoPengguna(dialog.FileName);
            if (tersimpan == null)
            {
                await _messageService.ShowErrorAsync("Gambar logo gagal dipakai. Pastikan berkasnya masih ada dan bisa dibaca.");
                return;
            }

            JalurLogo = tersimpan;
            _logger.LogInformation("Logo kop surat diganti: {Jalur}", tersimpan);
            await _messageService.ShowInfoAsync("Logo kop surat berhasil diganti dan langsung dipakai pada surat berikutnya.");
        }

        /// <summary>Kembalikan logo kop ke logo bawaan aplikasi.</summary>
        private async Task PakaiLogoBawaanAsync()
        {
            PengaturanCetak.HapusLogoPengguna();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            await _messageService.ShowInfoAsync("Logo kop surat dikembalikan ke logo bawaan aplikasi.");
        }

        public string NamaDesa { get => _namaDesa; set { if (SetProperty(ref _namaDesa, value)) OnFieldChanged(); } }
        public string Kecamatan { get => _kecamatan; set { if (SetProperty(ref _kecamatan, value)) OnFieldChanged(); } }
        public string Kabupaten { get => _kabupaten; set { if (SetProperty(ref _kabupaten, value)) OnFieldChanged(); } }
        public string Alamat { get => _alamat; set { if (SetProperty(ref _alamat, value)) OnFieldChanged(); } }
        public string Kodepos { get => _kodepos; set { if (SetProperty(ref _kodepos, value)) OnFieldChanged(); } }
        public string KepalaDesa { get => _kepalaDesa; set { if (SetProperty(ref _kepalaDesa, value)) OnFieldChanged(); } }
        public string Sekdes { get => _sekdes; set { if (SetProperty(ref _sekdes, value)) OnFieldChanged(); } }
        public string NamaCamat { get => _namaCamat; set { if (SetProperty(ref _namaCamat, value)) OnFieldChanged(); } }
        public string NipCamat { get => _nipCamat; set { if (SetProperty(ref _nipCamat, value)) OnFieldChanged(); } }
        public string GolCamat { get => _golCamat; set { if (SetProperty(ref _golCamat, value)) OnFieldChanged(); } }

        public bool IsLoading
        {
            get => _isLoading;
            private set { if (SetProperty(ref _isLoading, value)) SaveCommand.RaiseCanExecuteChanged(); }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            private set { if (SetProperty(ref _hasChanges, value)) SaveCommand.RaiseCanExecuteChanged(); }
        }

        private string _statusPesan = "Siap";

        /// <summary>Pesan singkat untuk statusbar bawah halaman (bukan dialog).</summary>
        public string StatusPesan
        {
            get => _statusPesan;
            private set => SetProperty(ref _statusPesan, value);
        }

        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand CancelCommand { get; }

        /// <summary>Hitung ulang HasChanges dengan membandingkan nilai form terhadap snapshot tersimpan.</summary>
        private void OnFieldChanged()
        {
            HasChanges = HasDataChanged();
        }

        /// <summary>Padanan HasDataChanged WinForms: bandingkan (trim + ignore case) terhadap snapshot.</summary>
        private bool HasDataChanged()
        {
            return !string.Equals(_namaDesa?.Trim(), _snapshot.NamaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kecamatan?.Trim(), _snapshot.Kecamatan, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kabupaten?.Trim(), _snapshot.Kabupaten, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_alamat?.Trim(), _snapshot.Alamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kodepos?.Trim(), _snapshot.Kodepos, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kepalaDesa?.Trim(), _snapshot.KepalaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_sekdes?.Trim(), _snapshot.SekretarisDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_namaCamat?.Trim(), _snapshot.NamaCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipCamat?.Trim(), _snapshot.NipCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_golCamat?.Trim(), _snapshot.GolCamat, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                var settings = await _settingsManager.GetSettingsAsync();
                _snapshot = settings ?? new DesaData();

                _namaDesa = _snapshot.NamaDesa ?? string.Empty;
                _kecamatan = _snapshot.Kecamatan ?? string.Empty;
                _kabupaten = _snapshot.Kabupaten ?? string.Empty;
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
                _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
                _namaCamat = _snapshot.NamaCamat ?? string.Empty;
                _nipCamat = _snapshot.NipCamat ?? string.Empty;
                _golCamat = _snapshot.GolCamat ?? string.Empty;

                OnPropertyChanged(nameof(NamaDesa));
                OnPropertyChanged(nameof(Kecamatan));
                OnPropertyChanged(nameof(Kabupaten));
                OnPropertyChanged(nameof(Alamat));
                OnPropertyChanged(nameof(Kodepos));
                OnPropertyChanged(nameof(KepalaDesa));
                OnPropertyChanged(nameof(Sekdes));
                OnPropertyChanged(nameof(NamaCamat));
                OnPropertyChanged(nameof(NipCamat));
                OnPropertyChanged(nameof(GolCamat));
                MuatPengaturanCetak();
                HasChanges = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal memuat pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Batal: buang seluruh perubahan yang belum disimpan, kembalikan form
        /// ke nilai tersimpan terakhir (snapshot) — tanpa menyentuh database —
        /// lalu tutup halaman kembali ke tampilan default.
        /// </summary>
        private async Task CancelAsync()
        {
            try
            {
                IsLoading = true;

                _namaDesa = _snapshot.NamaDesa ?? string.Empty;
                _kecamatan = _snapshot.Kecamatan ?? string.Empty;
                _kabupaten = _snapshot.Kabupaten ?? string.Empty;
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
                _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
                _namaCamat = _snapshot.NamaCamat ?? string.Empty;
                _nipCamat = _snapshot.NipCamat ?? string.Empty;
                _golCamat = _snapshot.GolCamat ?? string.Empty;

                OnPropertyChanged(nameof(NamaDesa));
                OnPropertyChanged(nameof(Kecamatan));
                OnPropertyChanged(nameof(Kabupaten));
                OnPropertyChanged(nameof(Alamat));
                OnPropertyChanged(nameof(Kodepos));
                OnPropertyChanged(nameof(KepalaDesa));
                OnPropertyChanged(nameof(Sekdes));
                OnPropertyChanged(nameof(NamaCamat));
                OnPropertyChanged(nameof(NipCamat));
                OnPropertyChanged(nameof(GolCamat));
                HasChanges = false;
                _navigation.ShowDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membatalkan perubahan pengaturan desa");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveAsync()
        {
            if (!ValidateRequiredFields())
            {
                return;
            }

            try
            {
                IsLoading = true;
                var desaData = new DesaData
                {
                    NamaDesa = NamaDesa?.Trim(),
                    Kecamatan = Kecamatan?.Trim(),
                    Kabupaten = Kabupaten?.Trim(),
                    Alamat = Alamat?.Trim(),
                    Kodepos = Kodepos?.Trim(),
                    KepalaDesa = KepalaDesa?.Trim(),
                    SekretarisDesa = Sekdes?.Trim(),
                    NamaCamat = NamaCamat?.Trim(),
                    NipCamat = NipCamat?.Trim(),
                    GolCamat = GolCamat?.Trim()
                };
                await _settingsManager.SaveSettingsAsync(desaData);
                _snapshot = desaData;
                HasChanges = false;
                SettingsSaved?.Invoke();
                _logger.LogInformation("Pengaturan desa berhasil disimpan");

                // Konfirmasi cukup lewat statusbar halaman ini — dialog yang muncul
                // setiap kali Simpan terlalu mengganggu alur kerja.
                StatusPesan = $"Pengaturan desa tersimpan — {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal menyimpan pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Padanan ValidateRequiredFields WinForms: field wajib terisi, kodepos 5
        /// digit (opsional), NIP camat minimal 9 digit (opsional). Mengembalikan
        /// true bila valid; bila tidak, menampilkan peringatan berisi daftar kesalahan.
        /// </summary>
        private bool ValidateRequiredFields()
        {
            var errorMessages = new List<string>();

            ValidateField(NamaDesa, "Nama Desa", errorMessages);
            ValidateField(Alamat, "Alamat Desa", errorMessages);
            ValidateField(Kecamatan, "Kecamatan", errorMessages);
            ValidateField(Kabupaten, "Kabupaten", errorMessages);
            ValidateField(KepalaDesa, "Kepala Desa", errorMessages);
            ValidateField(Sekdes, "Sekretaris Desa", errorMessages);

            if (!string.IsNullOrWhiteSpace(Kodepos) && !KodeposRegex.IsMatch(Kodepos.Trim()))
            {
                errorMessages.Add("Kodepos harus terdiri dari 5 digit angka");
            }

            if (!string.IsNullOrWhiteSpace(NipCamat) && NipCamat.Trim().Length < 9)
            {
                errorMessages.Add("NIP Camat minimal 9 digit");
            }

            if (errorMessages.Count > 0)
            {
                var errorMessage = "Perbaiki data berikut:\n\u2022 " + string.Join("\n\u2022 ", errorMessages);
                _ = _messageService.ShowWarningAsync(errorMessage);
                return false;
            }

            return true;
        }

        private static void ValidateField(string? fieldValue, string fieldName, List<string> errorMessages)
        {
            if (!Validator.ValidateRequired(fieldValue!, fieldName, out var msg))
            {
                errorMessages.Add(msg);
            }
        }
    }
}
