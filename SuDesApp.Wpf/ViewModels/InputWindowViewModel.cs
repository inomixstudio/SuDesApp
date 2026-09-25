using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Services;
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
        private readonly IPeringatanDataDesaContoh? _peringatan;
        private readonly SuratSaveService _simpanService;
        private readonly ILogger<InputWindowViewModel> _logger;

        private ISuratInput? _input;
        private object? _inputView;
        private string _title = "Input Surat";
        private string _templateName = string.Empty;
        private bool _isBusy;
        private bool _isEditMode;
        private int _editSuratId;
        private string _editOriginalNomor = string.Empty;
        private bool _tampilPeringatanDataContoh;
        private string _pesanPeringatanDataContoh = string.Empty;

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
            SuDesApp.Services.SuratSaveService simpanService,
            ILogger<InputWindowViewModel> logger,
            IPeringatanDataDesaContoh? peringatan = null)
        {
            _inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _simpanService = simpanService ?? throw new ArgumentNullException(nameof(simpanService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _peringatan = peringatan;
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new AsyncRelayCommand(CancelAsync);
            BukaPengaturanSuratCommand = new RelayCommand(BukaPengaturanSurat);
        }

        // =================================================================
        // Pemberitahuan inline (pengganti dialog popup)
        // =================================================================

        /// <summary>
        /// Kabar halaman yang tampil di dalam form (bukan dialog popup): hasil simpan,
        /// data yang belum valid, dan kegagalan membuat PDF. Kartu berkunci sama saling
        /// menimpa, jadi menekan Simpan berulang tidak menumpuk kartu.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        // =================================================================
        // Peringatan data desa contoh (ditampilkan di atas form, bukan dialog)
        // =================================================================

        /// <summary>
        /// Benar bila data desa masih contoh sehingga surat ini akan mencetak data
        /// contoh pada kop dan tanda tangan. Muncul sendiri saat form dibuka.
        /// </summary>
        public bool TampilPeringatanDataContoh
        {
            get => _tampilPeringatanDataContoh;
            private set => SetProperty(ref _tampilPeringatanDataContoh, value);
        }

        /// <summary>Pesan peringatan data desa contoh untuk banner di atas form surat.</summary>
        public string PesanPeringatanDataContoh
        {
            get => _pesanPeringatanDataContoh;
            private set => SetProperty(ref _pesanPeringatanDataContoh, value ?? string.Empty);
        }

        /// <summary>Tombol banner: langsung ke Pengaturan Surat (bagian Data Desa).</summary>
        public RelayCommand BukaPengaturanSuratCommand { get; }

        /// <summary>
        /// Periksa data desa contoh lalu tampilkan/matikan banner peringatan.
        /// Dipanggil saat form dibuka supaya pengguna tahu keadaannya sebelum
        /// menekan Simpan (penjaga kedua tetap aktif saat menyimpan).
        /// </summary>
        public async Task PeriksaPeringatanDataContohAsync()
        {
            if (_peringatan == null)
            {
                return;
            }

            var keadaan = await _peringatan.PeriksaAsync();
            TampilPeringatanDataContoh = keadaan.MasihContoh;
            PesanPeringatanDataContoh = keadaan.MasihContoh
                ? $"Data desa masih contoh: {keadaan.RingkasField}. Surat ini akan mencetak data contoh itu pada kop dan tanda tangan."
                : string.Empty;
        }

        private void BukaPengaturanSurat()
        {
            try
            {
                var setelan = _serviceProvider.GetRequiredService<SetelanViewModel>();
                setelan.TampilkanBagian(0);   // bagian 1: Data Desa
                _navigation.Navigate(setelan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka Pengaturan Surat dari banner peringatan data contoh");
            }
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

        /// <summary>
        /// Nama tampilan untuk judul host: label blanko NTCR yang ramah ("N1 — Surat
        /// Pengantar Nikah") bila template-nya blanko NTCR; selain itu nama template
        /// huruf biasa.
        /// </summary>
        private static string FriendlyTypeName(string templateName)
        {
            var blanko = NtcrKatalog.Cari(templateName);
            if (blanko != null)
            {
                return blanko.Judul;
            }

            return string.IsNullOrWhiteSpace(templateName) ? templateName : templateName.Replace('_', ' ');
        }

        /// <summary>Konfigurasi form untuk surat baru.</summary>
        public void Configure(string templateName)
        {
            _templateName = templateName;
            _isEditMode = false;
            _editSuratId = 0;
            _editOriginalNomor = string.Empty;
            (_input, var view) = _inputFactory.Create(templateName);
            InputView = view;
            Title = $"Input Surat — {FriendlyTypeName(templateName)}";
            OnPropertyChanged(nameof(SaveButtonText));

            if (_input is BaseSuratInputViewModel baseVm)
            {
                baseVm.SetEditMode(false);
                _ = InitializeAsync(baseVm);
            }

            // Peringatan data desa contoh muncul sendiri saat form dibuka, supaya
            // pengguna tahu sebelum menekan Simpan (bukan hanya saat disimpan).
            _ = PeriksaPeringatanDataContohAsync();
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
            Title = $"Edit Surat — {FriendlyTypeName(_templateName)} (No. {_editOriginalNomor})";
            OnPropertyChanged(nameof(SaveButtonText));

            if (_input is BaseSuratInputViewModel baseVm)
            {
                baseVm.SetEditMode(true);
                baseVm.SetNomorSurat(_editOriginalNomor);
                await baseVm.InitializeAsync();
                await baseVm.FillDataAsync(existing);
            }

            await PeriksaPeringatanDataContohAsync();
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
                    var nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(jenis.KodeJenis!);
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

            // Data desa masih contoh: surat resmi akan keluar dengan nama desa/pejabat
            // contoh. Beri kesempatan menghentikan penyimpanan lebih dulu.
            if (_peringatan != null)
            {
                string kegiatan = _isEditMode
                    ? "Perubahan surat ini akan disimpan dan dicetak ulang."
                    : "Surat ini akan disimpan dan dicetak.";

                if (!await _peringatan.BolehLanjutAsync(kegiatan, _messageService))
                {
                    await PeriksaPeringatanDataContohAsync();
                    return;
                }
            }

            IsBusy = true;
            try
            {
                if (_isEditMode)
                {
                    await SaveEditAsync();
                }
                else
                {
                    await SaveNewAsync(ModeSimpan.Aktif);
                }
            }
            catch (ValidationException ex)
            {
                Pesan.Peringatan("Isian surat belum lengkap", ex.Message, "simpan-surat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan surat {Template}", _templateName);
                Pesan.Galat("Gagal menyimpan surat", $"Surat belum tersimpan. Penyebabnya: {ex.Message}",
                    "simpan-surat");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Alur surat baru: kumpul → validasi sesuai mode (draft eksplisit) →
        /// insert atomik → generate PDF → pratinjau. Semua keputusan simpan
        /// di SuratSaveService; VM hanya menyiapkan objek dan menampilkan hasil.
        /// </summary>
        private async Task SaveNewAsync(ModeSimpan mode)
        {
            await EnsureNomorSuratAsync();

            var suratData = new SuratData(
                _unitOfWork.SuratRepository,
                _unitOfWork.WargaRepository,
                _unitOfWork.DesaRepository,
                _unitOfWork.JenisSuratRepository,
                _serviceProvider.GetService<ILogger<SuratData>>());

            var hasil = await _simpanService.SimpanBaruAsync(
                suratData,
                mode,
                s => _input!.CollectDataAsync(s, mode));

            await suratData.EnsureDesaDataLoadedAsync(_unitOfWork.DesaRepository);

            var pdfPath = await GeneratePdfAsync(suratData, hasil.ID_Surat);
            if (pdfPath != null)
            {
                _logger.LogInformation("PDF dibuat: {Path}", pdfPath);
                var preview = _previewFactory(
                    $"Surat {suratData.NamaJenis} — {suratData.NomorSurat}", pdfPath, hasil.ID_Surat);
                _navigation.Navigate(preview);
            }

            if (hasil.Status == "Draft")
            {
                Pesan.Sukses("Surat tersimpan",
                    $"Surat disimpan sebagai DRAFT (No. {suratData.NomorSurat}). " +
                    "Lengkapi datanya lewat klik kanan surat di Register → Edit.",
                    "simpan-surat");
            }
            else
            {
                Pesan.Sukses("Surat berhasil disimpan",
                    $"Surat bernomor {suratData.NomorSurat} sudah tersimpan, dan pratinjaunya dibuka.",
                    "simpan-surat");
            }
            SuratSaved?.Invoke(hasil.ID_Surat);

            // Tidak memanggil RequestClose di sini: saat form dipasang di content
            // host utama (bukan modal), navigasi ke pratinjau sudah mengganti konten.
            // RequestClose hanya dipakai jalur batal/penyimpanan draft.
        }

        /// <summary>
        /// Alur edit: muat baris yang ada → kumpul sesuai mode → UpdateAsync → PDF baru.
        /// Draft yang belum lengkap tetap tersimpan; yang sudah lengkap naik ke Aktif.
        /// </summary>
        private async Task SaveEditAsync()
        {
            var existing = await _unitOfWork.SuratRepository.GetByIdAsync(_editSuratId);
            if (existing == null)
            {
                Pesan.Galat("Surat tidak ditemukan",
                    $"Surat #{_editSuratId} mungkin sudah dihapus, jadi perubahannya tidak dapat disimpan.",
                    "simpan-surat");
                RequestClose?.Invoke();
                return;
            }

            var statusAsli = existing.Status;
            var wasDraft = string.Equals(statusAsli, "Draft", StringComparison.OrdinalIgnoreCase);
            var mode = wasDraft ? ModeSimpan.Draft : ModeSimpan.Aktif;

            try
            {
                var hasil = await _simpanService.PerbaruiAsync(existing, mode,
                    s => _input!.CollectDataAsync(s, mode));

                await existing.EnsureDesaDataLoadedAsync(_unitOfWork.DesaRepository);

                var pdfPath = await GeneratePdfAsync(existing, existing.ID_Surat);
                if (pdfPath != null)
                {
                    _logger.LogInformation("PDF hasil edit dibuat: {Path}", pdfPath);
                    var preview = _previewFactory(
                        $"Surat {existing.NamaJenis} — {existing.NomorSurat}", pdfPath, existing.ID_Surat);
                    _navigation.Navigate(preview);
                }

                Pesan.Sukses("Perubahan surat tersimpan",
                    $"Surat nomor {existing.NomorSurat} sudah diperbarui, dan pratinjaunya dibuka ulang.",
                    "simpan-surat");
                SuratSaved?.Invoke(existing.ID_Surat);
            }
            catch (ValidationException ex)
            {
                Pesan.Peringatan("Perubahan belum tersimpan",
                    ex.Message.Replace("Surat belum lengkap: ", "Lengkapi dulu isian berikut:\n").Replace("; ", "\n"),
                    "simpan-surat");
            }
            catch (InvalidOperationException ex)
            {
                Pesan.Galat("Gagal menyimpan perubahan",
                    "Perubahan surat tidak tersimpan di database. Coba ulangi sebentar lagi.",
                    "simpan-surat");
                _logger.LogError(ex, "Gagal update surat #{Id}", _editSuratId);
            }
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
            string? nik = null, nama = null;
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
                Pesan.Galat("Gagal menyimpan draft",
                    $"Isian surat tidak jadi disimpan sebagai draft. Penyebabnya: {ex.Message}",
                    "simpan-surat");
            }
        }

        /// <summary>
        /// Simpan isian saat ini sebagai surat DRAFT (boleh belum lengkap).
        /// Bila data ternyata sudah lengkap/valid, surat langsung dianggap Aktif.
        /// </summary>
        private async Task SaveSuratAsDraftAsync()
        {
            await SaveNewAsync(ModeSimpan.Draft);

            // Jalur batal/penyimpanan draft menutup form (paritas perilaku lama).
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
                    Pesan.Peringatan("Surat tersimpan, PDF gagal dibuat",
                        $"Jenis {_templateName} belum berhasil dibuatkan dokumennya. " +
                        "Detail penyebab ada di log aplikasi (error.log).",
                        "pdf-surat");
                }
                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF untuk surat #{Id}", idSurat);
                Pesan.Peringatan("Surat tersimpan, PDF gagal dibuat",
                    $"Suratnya aman di register, hanya dokumen PDF-nya yang belum jadi. " +
                    $"Penyebabnya: {ex.Message}",
                    "pdf-surat");
                return null;
            }
        }
    }
}
