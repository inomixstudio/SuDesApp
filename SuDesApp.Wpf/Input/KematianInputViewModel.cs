using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using Validator = SuDesApp.Utilities.Validator;

namespace SuDesApp.Wpf.Input
{
    public class KematianInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluanKematian = "Persyaratan";
        private const string NamaJenisKematian = "KEMATIAN";
        private const string TimeFormatUi = "HH:mm";

        // ===== Data kematian =====
        private string _hariKematian = string.Empty;
        private string _tanggalKematian = string.Empty;
        private string _pukulKematian = string.Empty;
        private string _penyebabKematian = string.Empty;

        // ===== Data pelapor =====
        private string _nikPelapor = string.Empty;
        private string _namaPelapor = string.Empty;
        private string _umurPelapor = string.Empty;
        private string _agamaPelapor = string.Empty;
        private string _pekerjaanPelapor = string.Empty;
        private string _hubunganPelapor = string.Empty;
        private string _dusunPelapor = string.Empty;
        private string _desaPelapor = string.Empty;
        private string _kecPelapor = string.Empty;
        private string _kabPelapor = string.Empty;

        public KematianInputViewModel(
            ILogger<KematianInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisKematian;

        public string HariKematian { get => _hariKematian; set => SetProperty(ref _hariKematian, value); }

        public string TanggalKematian
        {
            get => _tanggalKematian;
            set
            {
                if (SetProperty(ref _tanggalKematian, value))
                {
                    UpdateHariKematianFromTanggal();
                }
            }
        }

        public string PukulKematian { get => _pukulKematian; set => SetProperty(ref _pukulKematian, value); }

        public string PenyebabKematian { get => _penyebabKematian; set => SetProperty(ref _penyebabKematian, value); }

        public IReadOnlyList<string> PenyebabKematianOptions { get; } = new[]
        {
            "Sakit",
            "Usia Lanjut",
            "Kecelakaan",
            "Kecelakaan Lalu Lintas",
            "Meninggal Mendadak",
            "Jatuh",
            "Tenggelam",
            "Kebakaran",
            "Akibat Bencana Alam",
            "Lainnya"
        };

        public IReadOnlyList<string> HubunganPelaporOptions { get; } = new[]
        {
            "Suami",
            "Istri",
            "Ayah Kandung",
            "Ibu Kandung",
            "Anak Kandung",
            "Kakak Kandung",
            "Adik Kandung",
            "Saudara",
            "Keponakan",
            "Cucu",
            "Keluarga Lainnya",
            "Tetangga",
            "Lainnya"
        };

        public string NIKPelapor { get => _nikPelapor; set => SetProperty(ref _nikPelapor, value); }
        public string NamaPelapor { get => _namaPelapor; set => SetProperty(ref _namaPelapor, value); }
        public string UmurPelapor { get => _umurPelapor; set => SetProperty(ref _umurPelapor, value); }
        public string AgamaPelapor { get => _agamaPelapor; set => SetProperty(ref _agamaPelapor, value); }
        public string PekerjaanPelapor { get => _pekerjaanPelapor; set => SetProperty(ref _pekerjaanPelapor, value); }
        public string HubunganPelapor { get => _hubunganPelapor; set => SetProperty(ref _hubunganPelapor, value); }
        public string DusunPelapor { get => _dusunPelapor; set => SetProperty(ref _dusunPelapor, value); }
        public string DesaPelapor { get => _desaPelapor; set => SetProperty(ref _desaPelapor, value); }
        public string KecPelapor { get => _kecPelapor; set => SetProperty(ref _kecPelapor, value); }
        public string KabPelapor { get => _kabPelapor; set => SetProperty(ref _kabPelapor, value); }

        protected override Task InitializeSpecificAsync()
        {
            JenisKelamin = "Laki-laki";
            Agama = "Islam";
            AgamaPelapor = "Islam";
            IsKadesSelected = true;
            return Task.CompletedTask;
        }

        public async Task OnNikPelaporLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = NIKPelapor?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearPelaporFields();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearPelaporFields();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    NamaPelapor = warga.Nama ?? string.Empty;
                    AgamaPelapor = IsValidOption(warga.Agama, ValidAgamaOptions) ? warga.Agama : "Islam";
                    PekerjaanPelapor = warga.Pekerjaan ?? string.Empty;
                    if (!string.IsNullOrEmpty(warga.TanggalLahir))
                    {
                        UmurPelapor = ParseTanggalLahirToUiFormat(warga.TanggalLahir);
                    }
                    DusunPelapor = warga.Dusun ?? string.Empty;
                    DesaPelapor = warga.Desa ?? string.Empty;
                    KecPelapor = warga.Kecamatan ?? string.Empty;
                    KabPelapor = warga.Kabupaten ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK pelapor: {Nik}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK pelapor");
            }
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData?.Kematian == null) return;

            var km = suratData.Kematian;

            TanggalKematian = ParseTanggalLahirToUiFormat(km.TanggalKematian);
            HariKematian = km.HariKematian ?? string.Empty;
            PukulKematian = km.PukulKematian ?? string.Empty;
            PenyebabKematian = !string.IsNullOrWhiteSpace(km.PenyebabKematian)
                ? km.PenyebabKematian
                : (suratData.Keterangan ?? string.Empty);

            NIKPelapor = km.NIKPelapor ?? string.Empty;
            NamaPelapor = km.NamaPelapor ?? string.Empty;
            UmurPelapor = ParseTanggalLahirToUiFormat(km.UmurPelapor);
            AgamaPelapor = IsValidOption(km.AgamaPelapor, ValidAgamaOptions) ? km.AgamaPelapor : "Islam";
            PekerjaanPelapor = km.PekerjaanPelapor ?? string.Empty;
            HubunganPelapor = km.HubunganPelapor ?? string.Empty;

            await ParseAlamatPelaporAsync(km.AlamatPelapor);

            IsKadesSelected = true;
        }

        protected override Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            suratData.Kematian ??= new KematianData();
            suratData.NamaJenis = NamaJenisKematian;
            suratData.Keperluan = DefaultKeperluanKematian;
            suratData.PejabatPenandatangan = "Kepala Desa";
            suratData.Keterangan = PenyebabKematian.Trim();

            var km = suratData.Kematian;
            km.HariKematian = HariKematian.Trim();
            km.TanggalKematian = ParseTanggalLahirToDbFormat(TanggalKematian);
            km.PukulKematian = PukulKematian.Trim();
            km.PenyebabKematian = PenyebabKematian.Trim();

            km.NIKPelapor = NIKPelapor.Trim();
            km.NamaPelapor = NamaPelapor.Trim();
            km.UmurPelapor = UmurPelapor.Trim();
            km.AgamaPelapor = GetComboValueOrDefault(AgamaPelapor, ValidAgamaOptions, "Islam");
            km.PekerjaanPelapor = PekerjaanPelapor.Trim();
            km.HubunganPelapor = HubunganPelapor.Trim();
            km.AlamatPelapor = $"{DusunPelapor.Trim()} Desa {DesaPelapor.Trim()} Kec. {KecPelapor.Trim()} Kab. {KabPelapor.Trim()}".Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = suratData.NamaJenis;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = true;
            }

            return Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(PenyebabKematian))
            {
                errors.Add("Penyebab Kematian tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(HariKematian))
            {
                errors.Add("Hari kematian harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TanggalKematian))
            {
                errors.Add("Tanggal kematian harus diisi (DD-MM-YYYY)");
            }
            else if (!DateTime.TryParseExact(TanggalKematian.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tanggalKematian))
            {
                errors.Add("Format tanggal kematian tidak valid (DD-MM-YYYY)");
            }
            else if (tanggalKematian > DateTime.Now)
            {
                errors.Add("Tanggal kematian tidak boleh di masa depan");
            }

            if (string.IsNullOrWhiteSpace(PukulKematian) ||
                !DateTime.TryParseExact(PukulKematian.Trim(), TimeFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("Format waktu kematian tidak valid (HH:mm)");
            }

            if (!string.IsNullOrWhiteSpace(NIKPelapor) && NIKPelapor.Trim().Length != NikLength)
            {
                errors.Add("NIK pelapor harus 16 digit atau kosong");
            }

            if (string.IsNullOrWhiteSpace(NamaPelapor))
            {
                errors.Add("Nama pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(UmurPelapor))
            {
                errors.Add("Tgl lahir pelapor harus diisi");
            }
            else if (!DateTime.TryParseExact(UmurPelapor.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tglLahirPelapor))
            {
                errors.Add("Format Tgl lahir pelapor tidak valid (DD-MM-YYYY)");
            }
            else if (tglLahirPelapor > DateTime.Now)
            {
                errors.Add("Tgl lahir pelapor tidak boleh di masa depan");
            }

            if (string.IsNullOrWhiteSpace(PekerjaanPelapor))
            {
                errors.Add("Pekerjaan pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(HubunganPelapor))
            {
                errors.Add("Hubungan pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(DusunPelapor))
            {
                errors.Add("Dusun pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(DesaPelapor))
            {
                errors.Add("Desa pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KecPelapor))
            {
                errors.Add("Kecamatan pelapor harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KabPelapor))
            {
                errors.Add("Kabupaten pelapor harus diisi");
            }
        }

        private void UpdateHariKematianFromTanggal()
        {
            if (DateTime.TryParseExact(TanggalKematian?.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tanggalKematian))
            {
                HariKematian = tanggalKematian.ToString("dddd", new CultureInfo("id-ID"));
            }
            else if (string.IsNullOrWhiteSpace(TanggalKematian))
            {
                HariKematian = string.Empty;
            }
        }

        private void ClearPelaporFields()
        {
            NamaPelapor = string.Empty;
            UmurPelapor = string.Empty;
            AgamaPelapor = "Islam";
            PekerjaanPelapor = string.Empty;
            HubunganPelapor = string.Empty;
            DusunPelapor = string.Empty;
            DesaPelapor = string.Empty;
            KecPelapor = string.Empty;
            KabPelapor = string.Empty;
        }

        private async Task ParseAlamatPelaporAsync(string? alamatLengkap)
        {
            if (string.IsNullOrWhiteSpace(alamatLengkap))
            {
                DusunPelapor = string.Empty;
                DesaPelapor = string.Empty;
                KecPelapor = string.Empty;
                KabPelapor = string.Empty;
                return;
            }

            var parts = alamatLengkap.Split(new[] { "Desa", "Kec.", "Kab." }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 4)
            {
                DusunPelapor = parts[0].Trim();
                DesaPelapor = parts[1].Trim();
                KecPelapor = parts[2].Trim();
                KabPelapor = parts[3].Trim();
            }
            else
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                DusunPelapor = string.Empty;
                DesaPelapor = desaData?.NamaDesa ?? string.Empty;
                KecPelapor = desaData?.Kecamatan ?? string.Empty;
                KabPelapor = desaData?.Kabupaten ?? string.Empty;
            }
        }

        private static bool IsValidOption(string? value, string[] validOptions)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   validOptions.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private static string GetComboValueOrDefault(string value, string[] validOptions, string defaultValue)
        {
            return IsValidOption(value, validOptions) ? value.Trim() : defaultValue;
        }
    }
}