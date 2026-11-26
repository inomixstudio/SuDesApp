using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Izin Keramaian (IZIN_KERAMAIAN): isian pemohon
    /// (penanggung jawab kegiatan) + jenis kegiatan, tanggal pelaksanaan, dan
    /// lokasi kegiatan.
    /// </summary>
    public class KeramaianInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Izin Keramaian";
        private const string FormatTanggalUi = "dd-MM-yyyy";

        private string _jenisKegiatan = string.Empty;
        private string _tanggalKegiatan = string.Empty;
        private string _lokasiKegiatan = string.Empty;

        public KeramaianInputViewModel(
            ILogger<KeramaianInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.IZIN_KERAMAIAN;

        public string JenisKegiatan
        {
            get => _jenisKegiatan;
            set => SetProperty(ref _jenisKegiatan, value);
        }

        public string TanggalKegiatan
        {
            get => _tanggalKegiatan;
            set => SetProperty(ref _tanggalKegiatan, value);
        }

        public string LokasiKegiatan
        {
            get => _lokasiKegiatan;
            set => SetProperty(ref _lokasiKegiatan, value);
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeSpecificAsync();
        }

        protected override async Task InitializeSpecificAsync()
        {
            // Form baru pernah tampil dengan kotak Keterangan kosong sementara
            // surat lain (SKTM, domisili, keterangan terbit) langsung menyajikan
            // kalimat bakunya. Keterangan tetap bisa disunting; isian ini hanya
            // memulai dari teks yang sama dengan yang dicetak PDF.
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = await GenerateKeteranganAsync();
            }
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            JenisKegiatan = string.Empty;
            TanggalKegiatan = string.Empty;
            LokasiKegiatan = string.Empty;
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            JenisKegiatan = suratData.JenisKegiatan ?? string.Empty;
            TanggalKegiatan = suratData.TanggalKegiatan ?? string.Empty;
            LokasiKegiatan = suratData.LokasiKegiatan ?? string.Empty;
            Keterangan = suratData.Keterangan ?? await GenerateKeteranganAsync();

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
            {
                IsSekdesSelected = true;
            }
            else
            {
                IsKadesSelected = true;
            }

            await Task.CompletedTask;
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.NamaJenis = SuratConstants.IZIN_KERAMAIAN;
            suratData.Keperluan = DefaultKeperluan;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            suratData.JenisKegiatan = JenisKegiatan.Trim();
            suratData.TanggalKegiatan = TanggalKegiatan.Trim();
            suratData.LokasiKegiatan = LokasiKegiatan.Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = SuratConstants.IZIN_KERAMAIAN;
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
            if (string.IsNullOrWhiteSpace(JenisKegiatan))
            {
                errors.Add("Jenis kegiatan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TanggalKegiatan))
            {
                errors.Add("Tanggal pelaksanaan harus diisi");
            }
            else if (!DateTime.TryParseExact(TanggalKegiatan.Trim(), FormatTanggalUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("Tanggal pelaksanaan harus berformat DD-MM-YYYY");
            }

            if (string.IsNullOrWhiteSpace(LokasiKegiatan))
            {
                errors.Add("Tempat/lokasi kegiatan harus diisi");
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
                if (desaData != null)
                {
                    return GeneratorPdf.KeramaianGenerator.KeteranganTeks(desaData);
                }

                // Data desa belum ada (database baru): pakai alamat yang tampil di
                // form, tetapi tetap lewat pembangun yang sama supaya kalimatnya tidak
                // menyimpang dari yang akan dicetak.
                return GeneratorPdf.KeramaianGenerator.KeteranganTeks(new DesaData
                {
                    NamaDesa = Desa.Trim(),
                    Kecamatan = Kecamatan.Trim(),
                    Kabupaten = Kabupaten.Trim()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default");
                return "Keterangan tidak dapat dimuat";
            }
        }
    }
}
