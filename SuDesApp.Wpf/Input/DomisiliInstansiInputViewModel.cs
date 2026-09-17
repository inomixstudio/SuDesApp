using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    public class DomisiliInstansiInputViewModel : BaseSuratInputViewModel
    {
        private string _namaInstansi = string.Empty;
        private string _alamatInstansi = string.Empty;

        public DomisiliInstansiInputViewModel(
            ILogger<DomisiliInstansiInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.INSTANSI;

        public string NamaInstansi
        {
            get => _namaInstansi;
            set => SetProperty(ref _namaInstansi, value);
        }

        public string AlamatInstansi
        {
            get => _alamatInstansi;
            set => SetProperty(ref _alamatInstansi, value);
        }

        public override async Task FillDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                ClearControls();
                return;
            }

            NomorSurat = suratData.NomorSurat ?? string.Empty;

            if (suratData.Instansi != null)
            {
                NamaInstansi = suratData.Instansi.NamaInstansi ?? string.Empty;
                AlamatInstansi = suratData.Instansi.AlamatInstansi ?? string.Empty;
            }

            Keterangan = suratData.Keterangan ?? string.Empty;

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                IsSekdesSelected = true;
            else
                IsKadesSelected = true;

            await FillSpecificDataAsync(suratData);
            OnPropertyChanged(null);
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData?.Instansi != null)
            {
                NamaInstansi = suratData.Instansi.NamaInstansi ?? string.Empty;
                AlamatInstansi = suratData.Instansi.AlamatInstansi ?? string.Empty;
                Keterangan = suratData.Keterangan ?? string.Empty;
            }

            return Task.CompletedTask;
        }

        public override async Task CollectDataAsync(SuratData? suratData)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));

            if (!ValidateInput(out _))
                throw new ValidationException("Validasi input gagal");

            suratData.Warga = null;

            suratData.NomorSurat = GetNomorSurat();
            suratData.TanggalSurat = _isEditMode && suratData.TanggalSurat != default
                ? suratData.TanggalSurat
                : DateTime.Now;

            await GetNamaPejabatPenandatanganAsync(suratData);
            await CollectSpecificDataAsync(suratData);
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.Instansi ??= new Instansi();
            suratData.Instansi.NamaInstansi = NamaInstansi.Trim();
            suratData.Instansi.AlamatInstansi = AlamatInstansi.Trim();
            suratData.Keterangan = Keterangan.Trim();
            suratData.Keperluan = "Surat Keterangan Domisili";
            suratData.NamaJenis = SuratConstants.INSTANSI;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";
        }

        public override bool ValidateInput(out DateTime tglLahir)
        {
            tglLahir = DateTime.MinValue;
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(NomorSurat))
                errors.Add("Nomor Surat tidak boleh kosong");
            else if (NomorSurat.Trim().Length > 50)
                errors.Add("Nomor Surat maksimal 50 karakter");

            if (string.IsNullOrWhiteSpace(NamaInstansi))
                errors.Add("Nama Instansi tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(AlamatInstansi))
                errors.Add("Alamat Instansi tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(Keterangan))
                errors.Add("Keterangan tidak boleh kosong");

            if (!IsKadesSelected && !IsSekdesSelected)
                errors.Add("Pilih pejabat penandatangan (Kepala Desa atau Sekretaris Desa)");

            if (errors.Count == 0) return true;

            _ = _messageService.ShowErrorAsync(string.Join(Environment.NewLine, errors));
            return false;
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            ClearInstansiControls();
        }

        private void ClearInstansiControls()
        {
            NamaInstansi = string.Empty;
            AlamatInstansi = string.Empty;
        }
    }
}
