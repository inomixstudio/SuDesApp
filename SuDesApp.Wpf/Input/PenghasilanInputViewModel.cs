using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Formulir Keterangan Penghasilan. Pola sama dengan SKTM (data pemohon
    /// lengkap + keterangan + penandatangan) ditambah kartu Data Penghasilan.
    /// Rincian penghasilan disimpan sebagai JSON pada SuratData.AdditionalData —
    /// kolom itu sudah tertulis otomatis pada insert/update SuratRepository,
    /// sehingga tanpa tabel dan handler baru isian tetap muncul saat diedit.
    /// </summary>
    public class PenghasilanInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Keterangan Penghasilan";
        private const string NamaJenisPenghasilan = "PENGHASILAN";

        private string _pekerjaanUsaha = string.Empty;
        private string _penghasilanPerBulan = string.Empty;
        private string _jumlahTanggungan = string.Empty;

        public PenghasilanInputViewModel(
            ILogger<PenghasilanInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string PekerjaanUsaha
        {
            get => _pekerjaanUsaha;
            set => SetProperty(ref _pekerjaanUsaha, value);
        }

        public string PenghasilanPerBulan
        {
            get => _penghasilanPerBulan;
            set => SetProperty(ref _penghasilanPerBulan, value);
        }

        public string JumlahTanggungan
        {
            get => _jumlahTanggungan;
            set => SetProperty(ref _jumlahTanggungan, value);
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

            Keterangan = suratData.Keterangan ?? await GetDefaultKeteranganAsync();

            // Rincian penghasilan dibaca dari AdditionalData; data lama yang
            // JSON-nya rusak diperlakukan kosong agar surat tetap bisa diedit.
            var data = suratData.Penghasilan ?? PenghasilanData.DariJson(suratData.AdditionalData);
            PekerjaanUsaha = data?.PekerjaanUsaha ?? string.Empty;
            PenghasilanPerBulan = data?.PenghasilanPerBulan ?? string.Empty;
            JumlahTanggungan = data?.JumlahTanggungan ?? string.Empty;

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
            suratData.NamaJenis = NamaJenisPenghasilan;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            var data = new PenghasilanData
            {
                PekerjaanUsaha = PekerjaanUsaha.Trim(),
                PenghasilanPerBulan = PenghasilanPerBulan.Trim(),
                JumlahTanggungan = JumlahTanggungan.Trim()
            };
            suratData.Penghasilan = data;
            suratData.AdditionalData = data.KeJson();

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = NamaJenisPenghasilan;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            await Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(PekerjaanUsaha))
                errors.Add("Pekerjaan / Usaha harus diisi");
            if (string.IsNullOrWhiteSpace(PenghasilanPerBulan))
                errors.Add("Penghasilan per Bulan harus diisi");
            if (string.IsNullOrWhiteSpace(Keterangan))
                errors.Add("Keterangan harus diisi");
        }

        private async Task<string> GetDefaultKeteranganAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var desa = desaData?.NamaDesa ?? (string.IsNullOrWhiteSpace(Desa) ? "-" : Desa.Trim());
                var kec = desaData?.Kecamatan ?? (string.IsNullOrWhiteSpace(Kecamatan) ? "-" : Kecamatan.Trim());
                var kab = desaData?.Kabupaten ?? (string.IsNullOrWhiteSpace(Kabupaten) ? "-" : Kabupaten.Trim());

                return $"Benar nama tersebut di atas adalah warga Desa {desa} " +
                       $"Kecamatan {kec} " +
                       $"Kabupaten {kab} " +
                       "dan penghasilan yang dinyatakan di atas adalah benar-benar nyata " +
                       "berdasarkan pengakuan pemohon dan pemeriksaan kami.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default Penghasilan");
                return "Keterangan tidak dapat dimuat";
            }
        }
    }
}
