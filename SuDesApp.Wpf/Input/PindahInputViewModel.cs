using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Pindah (SK_PINDAH): isian pemohon (alamat
    /// asal) + alamat tujuan pindah + alasan pindah. Keterangan diisi otomatis
    /// dari data tersebut dan tetap bisa disunting operator.
    /// </summary>
    public class PindahInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Surat Keterangan Pindah";

        private string _alamatTujuanPindah = string.Empty;
        private string _alasanPindah = string.Empty;

        public PindahInputViewModel(
            ILogger<PindahInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.SK_PINDAH;

        public string AlamatTujuanPindah
        {
            get => _alamatTujuanPindah;
            set => SetProperty(ref _alamatTujuanPindah, value);
        }

        public string AlasanPindah
        {
            get => _alasanPindah;
            set => SetProperty(ref _alasanPindah, value);
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeSpecificAsync();
        }

        protected override async Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = await GenerateKeteranganAsync();
            }
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            AlamatTujuanPindah = string.Empty;
            AlasanPindah = string.Empty;
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            AlamatTujuanPindah = suratData.AlamatTujuanPindah ?? string.Empty;
            AlasanPindah = suratData.AlasanPindah ?? string.Empty;
            Keterangan = suratData.Keterangan ?? string.Empty;

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
            {
                IsSekdesSelected = true;
            }
            else
            {
                IsKadesSelected = true;
            }
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.NamaJenis = SuratConstants.SK_PINDAH;
            suratData.Keperluan = DefaultKeperluan;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            suratData.AlamatTujuanPindah = AlamatTujuanPindah.Trim();
            suratData.AlasanPindah = AlasanPindah.Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = SuratConstants.SK_PINDAH;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            // Keterangan tetap bisa disunting operator; hanya diisi otomatis bila kosong.
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = await GenerateKeteranganAsync();
            }
            suratData.Keterangan = Keterangan.Trim();
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(AlamatTujuanPindah))
            {
                errors.Add("Alamat tujuan pindah harus diisi");
            }

            if (string.IsNullOrWhiteSpace(AlasanPindah))
            {
                errors.Add("Alasan pindah harus diisi");
            }

            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                errors.Add("Keterangan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(Agama) ||
                !ValidAgamaOptions.Contains(Agama.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Agama harus dipilih dari daftar yang tersedia");
            }

            if (string.IsNullOrWhiteSpace(Pekerjaan))
            {
                errors.Add("Pekerjaan tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinan) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan harus dipilih dari daftar yang tersedia");
            }
        }

        private async Task<string> GenerateKeteranganAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var desa = desaData?.NamaDesa ?? (string.IsNullOrWhiteSpace(Desa) ? "-" : Desa.Trim());
                var kec = desaData?.Kecamatan ?? (string.IsNullOrWhiteSpace(Kecamatan) ? "-" : Kecamatan.Trim());
                var kab = desaData?.Kabupaten ?? (string.IsNullOrWhiteSpace(Kabupaten) ? "-" : Kabupaten.Trim());

                return $"Adalah benar nama tersebut diatas adalah warga Desa {desa} " +
                       $"Kecamatan {kec} Kabupaten {kab}, dan yang bersangkutan pindah " +
                       "dari alamat tersebut di atas ke alamat tujuan berikut ini.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default");
                return "Keterangan tidak dapat dimuat";
            }
        }
    }
}
