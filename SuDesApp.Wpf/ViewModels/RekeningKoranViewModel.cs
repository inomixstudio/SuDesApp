using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Controllers.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel form Permohonan Rekening Koran — migrasi 1:1 dari
    /// Views/Tambahan/PermohonanRekeningKoran.cs (WinForms).
    /// Menghasilkan PDF Permohonan Print Out Rekening Koran via RekeningKoranGenerator.
    /// </summary>
    public class RekeningKoranViewModel : ObservableObject
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly RekeningKoranGenerator _pdfGenerator;
        private readonly AppConfig _appConfig;
        private readonly ILogger<RekeningKoranViewModel> _logger;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;

        private string _nomorSurat = string.Empty;
        private string _namaKades = string.Empty;
        private string _jabatan = string.Empty;
        private string _alamatKades = string.Empty;
        private string _namaRekening = string.Empty;
        private string _nomorRekening = string.Empty;
        private string _bank = string.Empty;
        private string _kcp = string.Empty;
        private DateTime _tanggalMulai = DateTime.Now;
        private DateTime _tanggalSelesai = DateTime.Now;
        private bool _isBusy;

        public event Action? RequestClose;

        public RekeningKoranViewModel(
            IUnitOfWork unitOfWork,
            RekeningKoranGenerator pdfGenerator,
            AppConfig appConfig,
            ILogger<RekeningKoranViewModel> logger,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            IMessageService messageService)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _pdfGenerator = pdfGenerator ?? throw new ArgumentNullException(nameof(pdfGenerator));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(Cancel);

            _ = InitializeAsync();
        }

        // ===== Properti ter-observasi =====
        public string NomorSurat { get => _nomorSurat; set => SetProperty(ref _nomorSurat, value); }
        public string NamaKades { get => _namaKades; set => SetProperty(ref _namaKades, value); }
        public string Jabatan { get => _jabatan; set => SetProperty(ref _jabatan, value); }
        public string AlamatKades { get => _alamatKades; set => SetProperty(ref _alamatKades, value); }
        public string NamaRekening { get => _namaRekening; set => SetProperty(ref _namaRekening, value); }
        public string NomorRekening { get => _nomorRekening; set => SetProperty(ref _nomorRekening, value); }
        public string Bank { get => _bank; set => SetProperty(ref _bank, value); }
        public string KCP { get => _kcp; set => SetProperty(ref _kcp, value); }
        public DateTime TanggalMulai { get => _tanggalMulai; set => SetProperty(ref _tanggalMulai, value); }
        public DateTime TanggalSelesai { get => _tanggalSelesai; set => SetProperty(ref _tanggalSelesai, value); }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        private async Task InitializeAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData == null)
                {
                    desaData = new DesaData
                    {
                        NamaDesa = string.Empty,
                        Kecamatan = string.Empty,
                        Kabupaten = string.Empty,
                        Alamat = string.Empty,
                        Kodepos = string.Empty,
                        KepalaDesa = string.Empty
                    };
                }

                NomorSurat = $"130/NOMOR/Ds/{DateTime.Now:yyyy}";
                NamaKades = desaData.KepalaDesa?.ToUpperInvariant() ?? string.Empty;
                var namaDesa = desaData.NamaDesa ?? string.Empty;
                Jabatan = "Kepala Desa " + (namaDesa.Length > 0
                    ? char.ToUpper(namaDesa[0]) + namaDesa.Substring(1).ToLower()
                    : namaDesa);
                NamaRekening = "PEMERINTAH DESA " + namaDesa.ToUpperInvariant();
                NomorRekening = string.Empty;
                Bank = string.Empty;
                KCP = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data awal Rekening Koran");
            }
        }

        private bool ValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(NomorSurat) ||
                string.IsNullOrWhiteSpace(NamaKades) ||
                string.IsNullOrWhiteSpace(Jabatan) ||
                string.IsNullOrWhiteSpace(AlamatKades) ||
                string.IsNullOrWhiteSpace(NamaRekening) ||
                string.IsNullOrWhiteSpace(NomorRekening) ||
                string.IsNullOrWhiteSpace(Bank) ||
                string.IsNullOrWhiteSpace(KCP))
            {
                error = "Semua kolom wajib diisi!";
                return false;
            }

            if (TanggalSelesai < TanggalMulai)
            {
                error = "Tanggal akhir tidak boleh lebih awal dari tanggal mulai.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private RekeningKoranData CreateData()
        {
            return new RekeningKoranData
            {
                NomorSurat = NomorSurat,
                Perihal = "Permohonan Print Out Rekening Koran",
                TanggalSurat = DateTime.Now,
                NamaPejabat = NamaKades,
                Jabatan = Jabatan,
                AlamatPejabat = AlamatKades,
                NamaPemegangRekening = NamaRekening,
                NomorRekening = NomorRekening,
                PeriodeRekening = $"{TanggalMulai:dd-MM-yyyy} s/d {TanggalSelesai:dd-MM-yyyy}",
                Bank = Bank,
                KCP = KCP,
                Desa = new DesaData
                {
                    NamaDesa = Jabatan.StartsWith("Kepala Desa ", StringComparison.OrdinalIgnoreCase)
                        ? Jabatan.Substring("Kepala Desa ".Length)
                        : string.Empty,
                    Kecamatan = string.Empty,
                    Kabupaten = string.Empty,
                    Alamat = string.Empty,
                    Kodepos = string.Empty,
                    KepalaDesa = NamaKades
                }
            };
        }

        private async Task SaveAsync()
        {
            if (IsBusy) return;

            if (!ValidateInputs(out var error))
            {
                await _messageService.ShowWarningAsync(error);
                return;
            }

            IsBusy = true;
            try
            {
                var data = CreateData();
                _logger.LogInformation("Membuat PDF Rekening Koran NomorSurat={NomorSurat}", data.NomorSurat);

                using var memoryStream = new MemoryStream();
                await _pdfGenerator.GeneratePdfAsync(memoryStream, data);
                var pdfBytes = memoryStream.ToArray();

                if (pdfBytes.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal menghasilkan PDF atau PDF kosong.");
                    return;
                }

                var outputFolder = _appConfig.PdfOutputPath;
                Directory.CreateDirectory(outputFolder);
                var outputPath = Path.Combine(
                    outputFolder, $"Permohonan_Rekening_Koran_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(outputPath, pdfBytes);

                var preview = _previewFactory(
                    $"Permohonan Rekening Koran — {data.NomorSurat}", outputPath);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF Rekening Koran");
                await _messageService.ShowErrorAsync($"Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Cancel() => RequestClose?.Invoke();
    }
}
