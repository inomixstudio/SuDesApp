using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Desa (SKD_UMUM) — setara SKDinput (WinForms).
    /// </summary>
    public class SkdInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Surat Keterangan Desa";
        private const string NamaJenisSkd = "SKD_UMUM";

        public SkdInputViewModel(
            ILogger<SkdInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisSkd;

        protected override async Task InitializeSpecificAsync()
        {
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
            suratData.NamaJenis = NamaJenisSkd;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = NamaJenisSkd;
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

                return $"Adalah benar warga Desa {desa} " +
                       $"Kecamatan {kec} " +
                       $"Kabupaten {kab} " +
                       "dan sampai saat ini masih berdomisili di desa kami dan belum menikah.";
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