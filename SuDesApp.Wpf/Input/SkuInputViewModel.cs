using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    public class SkuInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Persyaratan";
        private const string NamaJenisSku = "SKU";

        private string _bidangUsaha = string.Empty;
        private string _sejakTahun = string.Empty;
        private string _lokasiUsaha = string.Empty;

        public SkuInputViewModel(
            ILogger<SkuInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisSku;

        public string BidangUsaha
        {
            get => _bidangUsaha;
            set => SetProperty(ref _bidangUsaha, value);
        }

        public string SejakTahun
        {
            get => _sejakTahun;
            set => SetProperty(ref _sejakTahun, value);
        }

        public string LokasiUsaha
        {
            get => _lokasiUsaha;
            set => SetProperty(ref _lokasiUsaha, value);
        }

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

            suratData.SKU ??= new SKUData();
            BidangUsaha = suratData.SKU.BidangUsaha ?? string.Empty;
            SejakTahun = suratData.SKU.SejakTahun == 0
                ? string.Empty
                : suratData.SKU.SejakTahun.ToString();
            LokasiUsaha = suratData.SKU.LokasiUsaha ?? string.Empty;

            Keterangan = suratData.Keterangan ?? await GetDefaultKeteranganAsync();

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                IsSekdesSelected = true;
            else
                IsKadesSelected = true;
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.SKU ??= new SKUData();
            suratData.SKU.BidangUsaha = BidangUsaha.Trim();
            suratData.SKU.SejakTahun = int.TryParse(SejakTahun.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var tahun) ? tahun : 0;
            suratData.SKU.LokasiUsaha = LokasiUsaha.Trim();

            suratData.Keterangan = Keterangan.Trim();
            suratData.Keperluan = DefaultKeperluan;
            suratData.NamaJenis = NamaJenisSku;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = NamaJenisSku;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
                errors.Add("Keterangan harus diisi");

            if (string.IsNullOrWhiteSpace(BidangUsaha))
                errors.Add("Bidang usaha harus diisi");

            if (string.IsNullOrWhiteSpace(SejakTahun) || SejakTahun.Trim().Length != 4 ||
                !int.TryParse(SejakTahun.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                errors.Add("Tahun mulai usaha harus berupa 4 digit angka");
            }
        }

        private async Task<string> GetDefaultKeteranganAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var desa = desaData?.NamaDesa ?? (string.IsNullOrWhiteSpace(Desa) ? "-" : Desa.Trim());
                var kec = desaData?.Kecamatan ?? (string.IsNullOrWhiteSpace(Kecamatan) ? "-" : Kecamatan.Trim());
                var kab = desaData?.Kabupaten ?? (string.IsNullOrWhiteSpace(Kabupaten) ? "-" : Kabupaten.Trim());

                return $"Adalah benar warga Desa {desa} " +
                       $"Kecamatan {kec} " +
                       $"Kabupaten {kab} " +
                       "dan menurut sepengetahuan kami orang tersebut mempunyai usaha:";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default SKU");
                return "Keterangan tidak dapat dimuat";
            }
        }
    }
}
