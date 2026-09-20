using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    public class SkckInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeteranganSkck = "MELAMAR PEKERJAAN";
        private const string DefaultKeperluanSkck = "Persyaratan";
        private const string NamaJenisSkck = "PENGANTAR_SKCK";

        public SkckInputViewModel(
            ILogger<SkckInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisSkck;

        protected override Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = DefaultKeteranganSkck;
            }

            if (string.IsNullOrWhiteSpace(Kewarganegaraan))
            {
                Kewarganegaraan = "WNI";
            }

            IsKadesSelected = true;
            return Task.CompletedTask;
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                Keterangan = DefaultKeteranganSkck;
                Kewarganegaraan = "WNI";
                IsKadesSelected = true;
                return Task.CompletedTask;
            }

            Keterangan = string.IsNullOrWhiteSpace(suratData.Keterangan)
                ? DefaultKeteranganSkck
                : suratData.Keterangan;

            Kewarganegaraan = suratData.Warga != null &&!
                              ValidKewarganegaraanOptions.Contains(suratData.Warga.Kewarganegaraan?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ? suratData.Warga.Kewarganegaraan
                : "WNI";

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                IsSekdesSelected = true;
            else
                IsKadesSelected = true;

            return Task.CompletedTask;
        }

        protected override Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            suratData.Keterangan = Keterangan.Trim();
            suratData.Keperluan = DefaultKeperluanSkck;
            suratData.NamaJenis = NamaJenisSkck;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            if (suratData.Warga != null)
            {
                suratData.Warga.Pendidikan = Pendidikan.Trim();
                suratData.Warga.Kewarganegaraan =
                    ValidKewarganegaraanOptions.Contains(Kewarganegaraan?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        ? Kewarganegaraan.Trim()
                        : "WNI";
                suratData.Warga.NamaJenis = NamaJenisSkck;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            return Task.CompletedTask;
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

            if (string.IsNullOrWhiteSpace(Pendidikan))
            {
                errors.Add("Pendidikan tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinan) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan harus dipilih dari daftar yang tersedia");
            }

            if (string.IsNullOrWhiteSpace(Kewarganegaraan) ||
                !ValidKewarganegaraanOptions.Contains(Kewarganegaraan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Kewarganegaraan harus dipilih dari daftar yang tersedia");
            }
        }
    }
}