using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Controllers.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel host untuk satu form input surat — setara InputForm (WinForms).
    /// Menangani alur buka -> isi -> simpan -> generate PDF, serta mode EDIT surat
    /// yang sudah ada (muat ulang data -> UpdateAsync -> regenerate PDF).
    /// </summary>
    public class InputWindowViewModel : ObservableObject
    {
        private readonly InputControlFactory _inputFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageService _messageService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly AppConfig _appConfig;
        private readonly NavigationService _navigation;
        private readonly Func<string, string, int?, PdfPreviewViewModel> _previewFactory;
        private readonly ILogger<InputWindowViewModel> _logger;

        private ISuratInput? _input;
        private object? _inputView;
        private string _title = "Input Surat";
        private string _templateName = string.Empty;
        private bool _isBusy;
        private bool _isEditMode;
        private int _editSuratId;
        private string _editOriginalNomor = string.Empty;

        public event Action? RequestClose;

        /// <summary>
        /// Terpanggil setelah surat tersimpan (baru atau hasil edit) dengan argumen ID surat.
        /// Dipakai pratinjau PDF untuk memuat ulang dokumen secara otomatis.
        /// </summary>
        public static event Action<int>? SuratSaved;

        public InputWindowViewModel(
            InputControlFactory inputFactory,
            IServiceProvider serviceProvider,
            IMessageService messageService,
            IUnitOfWork unitOfWork,
            AppConfig appConfig,
            NavigationService navigation,
            Func<string, string, int?, PdfPreviewViewModel> previewFactory,
            ILogger<InputWindowViewModel> logger)
        {
            _inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new AsyncRelayCommand(CancelAsync);
        }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        /// <summary>Label tombol simpan: "Simpan" (baru) / "Simpan Perubahan" (edit).</summary>
        public string SaveButtonText => _isEditMode ? "Simpan Perubahan" : "Simpan";

        public object? InputView
        {
            get => _inputView;
            private set => SetProperty(ref _inputView, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    SaveCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand CancelCommand { get; }

        /// <summary>Konfigurasi form untuk surat baru.</summary>
        public void Configure(string templateName)
        {
            _templateName = templateName;
            _isEditMode = false;
            _editSuratId = 0;
            _editOriginalNomor = string.Empty;
            (_input, var view) = _inputFactory.Create(templateName);
            InputView = view;
            Title = $"Input Surat — {templateName}";
            OnPropertyChanged(nameof(SaveButtonText));

            if (_input is BaseSuratInputViewModel baseVm)
            {
                baseVm.SetEditMode(false);
                _ = InitializeAsync(baseVm);
            }
        }

        /// <summary>
        /// Konfigurasi form dalam MODE EDIT: muat data surat yang sudah ada, isi form,
        /// dan kunci nomor surat (nomor tidak boleh diubah saat edit).
        /// </summary>
        public async Task ConfigureForEditAsync(int idSurat)
        {
            var existing = await _unitOfWork.SuratRepository.GetByIdAsync(idSurat)
                ?? throw new InvalidOperationException($"Surat #{idSurat} tidak ditemukan.");

            _templateName = existing.NamaJenis
                ?? throw new InvalidOperationException("Jenis surat tidak diketahui.");
            _isEditMode = true;
            _editSuratId = idSurat;
            _editOriginalNomor = existing.NomorSurat ?? string.Empty;

            (_input, var view) = _inputFactory.Create(_templateName);
            InputView = view;
            Title = $"Edit Surat — {_templateName} (No. {_editOriginalNomor})";
            OnPropertyChanged(nameof(SaveButtonText));

            if (_input is BaseSuratInputViewModel baseVm)
            {
                baseVm.SetEditMode(true);
                baseVm.SetNomorSurat(_editOriginalNomor);
                await baseVm.InitializeAsync();
                await baseVm.FillDataAsync(existing);
            }
        }

        /// <summary>Inisialisasi form lalu isi nomor surat otomatis (paritas WinForms).</summary>
        private async Task InitializeAsync(BaseSuratInputViewModel baseVm)
        {
            await baseVm.InitializeAsync();

            if (string.IsNullOrWhiteSpace(baseVm.GetNomorSurat()))
            {
                await FillNomorSuratAsync(baseVm);
            }
        }

        private async Task FillNomorSuratAsync(BaseSuratInputViewModel baseVm)
        {
            try
            {
                var jenis = await _unitOfWork.JenisSuratRepository.GetJenisSuratByNamaAsync(_templateName);
                if (jenis != null)
                {
                    var nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(jenis.KodeJenis);
                    baseVm.SetNomorSurat(nomor);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengisi nomor surat otomatis untuk {Template}", _templateName);
            }
        }

        /// <summary>Mengisi nomor surat otomatis bila form kosong (dipanggil sebelum simpan).</summary>
        private async Task EnsureNomorSuratAsync()
        {
            if (_input is not BaseSuratInputViewModel baseVm)
                return;

            if (!string.IsNullOrWhiteSpace(baseVm.GetNomorSurat()))
                return;

            await FillNomorSuratAsync(baseVm);
        }

        private async Task SaveAsync()
        {
            if (_input == null || IsBusy) return;

            IsBusy = true;
            try
            {
                if (_isEditMode)
                {
                    await SaveEditAsync();
                }
                else
                {
                    await SaveNewAsync();
                }
            }
            catch (ValidationException ex)
            {
                await _messageService.ShowWarningAsync(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan surat {Template}", _templateName);
                await _messageService.ShowErrorAsync($"Gagal menyimpan surat: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Alur surat baru: validasi -> insert -> generate PDF -> pratinjau.</summary>
        private async Task SaveNewAsync()
        {
            await EnsureNomorSuratAsync();

            var suratData = new SuratData(
                _unitOfWork.SuratRepository,
                _unitOfWork.WargaRepository,
                _unitOfWork.DesaRepository,
                _unitOfWork.JenisSuratRepository,
                _serviceProvider.GetService<ILogger<SuratData>>());

            await _input!.CollectDataAsync(suratData);

            var errors = await suratData.ValidateAsync();
            if (errors.Any())
            {
                await _messageService.ShowWarningAsync(
                    "Data surat belum valid:\n" + string.Join("\n", errors));
                return;
            }

            // Status default surat baru: Aktif. (SaveSuratAsDraftAsync mengubahnya
            // menjadi Draft untuk jalur "batal lalu simpan sebagai draft".)
            suratData.Status = "Active";

            int idSurat = await _unitOfWork.SuratRepository.AddSuratAsync(suratData);

            await suratData.EnsureDesaDataLoadedAsync(_unitOfWork.DesaRepository);

            var pdfPath = await GeneratePdfAsync(suratData, idSurat);
            if (pdfPath != null)
            {
                _logger.LogInformation("PDF dibuat: {Path}", pdfPath);
                var preview = _previewFactory(
                    $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}", pdfPath, idSurat);
                _navigation.Navigate(preview);
            }

            await _messageService.ShowInfoAsync($"Surat berhasil disimpan (No. {suratData.NomorSurat}).");
            SuratSaved?.Invoke(idSurat);

            // Tidak lagi memanggil RequestClose di sini: saat form dipasang di content
            // host utama (bukan modal), navigasi ke pratinjau sudah mengganti konten.
            // RequestClose hanya dipakai jalur batal/penyimpanan draft.
        }

        /// <summary>Alur edit: muat baris yang ada -> isi dari form -> UpdateAsync -> PDF baru.</summary>
        private async Task SaveEditAsync()
        {
            var existing = await _unitOfWork.SuratRepository.GetByIdAsync(_editSuratId);
            if (existing == null)
            {
                await _messageService.ShowErrorAsync($"Surat #{_editSuratId} tidak ditemukan (mungkin sudah dihapus).");
                RequestClose?.Invoke();
                return;
            }

            // Kunci nomor: edit tidak boleh mengubah nomor surat (menghindari duplikat).
            var nomorAsli = existing.NomorSurat;
            var statusAsli = existing.Status;
            var wasDraft = string.Equals(statusAsli, "Draft", StringComparison.OrdinalIgnoreCase);

            await _input!.CollectDataAsync(existing);

            // Kembalikan nomor yang terkunci setelah CollectData menimpanya.
            existing.NomorSurat = nomorAsli;
            existing.Status = string.IsNullOrWhiteSpace(statusAsli) ? "Draft" : statusAsli;

            // Ukur kelengkapan dengan validasi ketat (status Aktif sementara agar
            // validator tidak kena short-circuit mode Draft).
            existing.Status = "Active";
            var errors = (await existing.ValidateAsync()).ToList();
            existing.Status = statusAsli ?? "Draft";

            if (wasDraft)
            {
                // Draft yang kini sudah lengkap otomatis naik ke Aktif;
                // yang masih belum lengkap tetap Draft (dapat disimpan tanpa diblokir).
                existing.Status = errors.Any() ? "Draft" : "Active";
            }
            else if (errors.Any())
            {
                await _messageService.ShowWarningAsync(
                    "Data surat belum valid:\n" + string.Join("\n", errors));
                return;
            }

            var ok = await _unitOfWork.SuratRepository.UpdateAsync(existing);
            if (!ok)
            {
                await _messageService.ShowErrorAsync("Gagal menyimpan perubahan surat.");
                return;
            }

            await existing.EnsureDesaDataLoadedAsync(_unitOfWork.DesaRepository);

            var pdfPath = await GeneratePdfAsync(existing, existing.ID_Surat);
            if (pdfPath != null)
            {
                _logger.LogInformation("PDF hasil edit dibuat: {Path}", pdfPath);
                var preview = _previewFactory(
                    $"Surat {existing.NamaJenis} — {existing.NomorSurat}", pdfPath, existing.ID_Surat);
                _navigation.Navigate(preview);
            }

            await _messageService.ShowInfoAsync($"Perubahan surat No. {existing.NomorSurat} berhasil disimpan.");
            SuratSaved?.Invoke(existing.ID_Surat);

            // Tidak lagi memanggil RequestClose di sini: saat form dipasang di content
            // host utama (bukan modal), navigasi ke pratinjau sudah mengganti konten.
            // RequestClose hanya dipakai jalur batal/penyimpanan draft.
        }

        /// <summary>
        /// Tombol Batal: bila pengguna sudah mulai mengisi data (NIK/Nama), tawarkan
        /// menyimpan sebagai DRAFT agar isian tidak hilang; surat baru default-nya Aktif
        /// hanya bila disimpan lewat tombol Simpan. Mode edit: Batal cukup menutup form.
        /// </summary>
        private async Task CancelAsync()
        {
            // Mode edit: tidak ada risiko kehilangan isian (data asli tetap di DB).
            if (_isEditMode)
            {
                RequestClose?.Invoke();
                return;
            }

            // Deteksi isian: NIK atau Nama sudah diisi pada form aktif.
            string nik = null, nama = null;
            if (_input is BaseSuratInputViewModel baseVm)
            {
                nik = baseVm.Nik?.Trim();
                nama = baseVm.Nama?.Trim();
            }
            bool adaIsian = !string.IsNullOrWhiteSpace(nik) || !string.IsNullOrWhiteSpace(nama);

            if (!adaIsian)
            {
                RequestClose?.Invoke();
                return;
            }

            try
            {
                bool simpanDraft = await _messageService.ShowConfirmationAsync(
                    "Surat Belum Disimpan",
                    "Ada data yang sudah diisi (NIK/Nama).\n\n" +
                    "YA = simpan sebagai DRAFT (dilanjutkan nanti lewat klik kanan di Register → Edit)\n" +
                    "TIDAK = buang isian dan tutup form");

                if (simpanDraft)
                {
                    await SaveSuratAsDraftAsync();
                }
                else
                {
                    // Buang isian — tutup tanpa menyimpan.
                    RequestClose?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal pada alur batal/draft surat {Template}", _templateName);
                await _messageService.ShowErrorAsync($"Gagal memproses pembatalan: {ex.Message}");
            }
        }

        /// <summary>
        /// Simpan isian saat ini sebagai surat DRAFT tanpa validasi ketat (boleh belum lengkap).
        /// Bila data ternyata sudah lengkap/valid, surat langsung dianggap Aktif.
        /// </summary>
        private async Task SaveSuratAsDraftAsync()
        {
            await EnsureNomorSuratAsync();

            var suratData = new SuratData(
                _unitOfWork.SuratRepository,
                _unitOfWork.WargaRepository,
                _unitOfWork.DesaRepository,
                _unitOfWork.JenisSuratRepository,
                _serviceProvider.GetService<ILogger<SuratData>>());

            await _input!.CollectDataAsync(suratData);

            // Ukur kelengkapan dengan validasi KETAT: set status Aktif dulu agar
            // validator tidak kena short-circuit mode Draft (Status default-nya "Draft").
            suratData.Status = "Active";
            var errors = (await suratData.ValidateAsync()).ToList();

            // Data belum lengkap → simpan sebagai Draft (validasi ketat dilewati saat insert);
            // data lengkap → tetap Aktif.
            if (errors.Any()) suratData.Status = "Draft";

            int idSurat = await _unitOfWork.SuratRepository.AddSuratAsync(suratData);

            var pdfPath = await GeneratePdfAsync(suratData, idSurat);

            await _messageService.ShowInfoAsync(
                suratData.Status == "Draft"
                    ? $"Surat disimpan sebagai DRAFT (No. {suratData.NomorSurat}). " +
                      "Lengkapi datanya lewat klik kanan surat di Register → Edit."
                    : $"Surat lengkap tersimpan (No. {suratData.NomorSurat}).");

            RequestClose?.Invoke();
        }

        private async Task<string?> GeneratePdfAsync(SuratData suratData, int idSurat)
        {
            try
            {
                var path = await SuratPdfHelper.GeneratePdfAsync(
                    _serviceProvider, _appConfig, suratData, _logger, _templateName);
                if (path == null)
                {
                    await _messageService.ShowWarningAsync(
                        $"Surat tersimpan, tapi PDF gagal dibuat untuk jenis {_templateName}.");
                }
                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF untuk surat #{Id}", idSurat);
                await _messageService.ShowWarningAsync(
                    $"Surat tersimpan, tapi PDF gagal dibuat: {ex.Message}");
                return null;
            }
        }
    }
}
