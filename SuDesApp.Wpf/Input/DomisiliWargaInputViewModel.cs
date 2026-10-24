using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    public class DomisiliWargaInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Surat Keterangan Domisili";
        private const string NamaJenisDomisili = "DOMISILI_WARGA";

        public DomisiliWargaInputViewModel(
            ILogger<DomisiliWargaInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisDomisili;

        protected override async Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = await GetDefaultKeteranganAsync();
            }
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            Keterangan = suratData.Keterangan ?? await GetDefaultKeteranganAsync();

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                IsSekdesSelected = true;
            else
                IsKadesSelected = true;
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.Keterangan = Keterangan.Trim();
            suratData.Keperluan = DefaultKeperluan;
            suratData.NamaJenis = NamaJenisDomisili;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = NamaJenisDomisili;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
                errors.Add("Keterangan harus diisi");

            if (string.IsNullOrWhiteSpace(Agama) ||
                !ValidAgamaOptions.Contains(Agama.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Agama harus dipilih dari daftar yang tersedia");
            }

            if (string.IsNullOrWhiteSpace(Pekerjaan))
                errors.Add("Pekerjaan tidak boleh kosong");
        }

        private async Task<string> GetDefaultKeteranganAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var desa = desaData?.NamaDesa ?? (string.IsNullOrWhiteSpace(Desa) ? "-" : Desa.Trim());
                var kec = desaData?.Kecamatan ?? (string.IsNullOrWhiteSpace(Kecamatan) ? "-" : Kecamatan.Trim());
                var kab = desaData?.Kabupaten ?? (string.IsNullOrWhiteSpace(Kabupaten) ? "-" : Kabupaten.Trim());

                return $"Benar bahwa nama tersebut di atas adalah warga Desa {desa} " +
                       $"Kecamatan {kec} " +
                       $"Kabupaten {kab} " +
                       "dan sampai saat ini masih berdomisili di desa kami.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default Domisili Warga");
                return "Keterangan tidak dapat dimuat";
            }
        }
    }
}
