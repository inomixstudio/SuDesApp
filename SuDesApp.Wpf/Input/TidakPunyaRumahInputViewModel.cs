using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Tidak Punya Rumah (SK_TIDAK_PUNYA_RUMAH)
    /// — setara SKDInput: isian pemohon + keterangan.
    /// </summary>
    public class TidakPunyaRumahInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Surat Keterangan Tidak Punya Rumah";

        public TidakPunyaRumahInputViewModel(
            ILogger<TidakPunyaRumahInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.SK_TIDAK_PUNYA_RUMAH;

        protected override async Task InitializeSpecificAsync()
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;

            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = await GetDefaultKeteranganAsync();
            }
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            Keterangan = suratData.Keterangan ?? string.Empty;

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
            {
                IsSekdesSelected = true;
            }
            else
            {
                IsKadesSelected = true;
            }

            return Task.CompletedTask;
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                throw new ValidationException("Keterangan harus diisi");
            }

            suratData.Keterangan = Keterangan.Trim();
            suratData.Keperluan = DefaultKeperluan;
            suratData.NamaJenis = SuratConstants.SK_TIDAK_PUNYA_RUMAH;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = SuratConstants.SK_TIDAK_PUNYA_RUMAH;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
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

        private async Task<string> GetDefaultKeteranganAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var desa = desaData?.NamaDesa ?? Top(Desa);
                var kec = desaData?.Kecamatan ?? Top(Kecamatan);
                var kab = desaData?.Kabupaten ?? Top(Kabupaten);

                return $"Adalah benar nama tersebut diatas adalah warga Desa {desa} " +
                       $"Kecamatan {kec} " +
                       $"Kabupaten {kab} " +
                       "dan yang bersangkutan saat ini tidak memiliki rumah atau tempat tinggal tetap di wilayah desa kami.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default");
                return "Keterangan tidak dapat dimuat";
            }
        }

        private static string Top(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
