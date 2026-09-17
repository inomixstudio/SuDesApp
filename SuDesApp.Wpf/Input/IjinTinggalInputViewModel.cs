using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Izin Tinggal (IJIN_TINGGAL) — setara
    /// IjinTinggalInput (WinForms). Alamat asal diambil dari pemohon
    /// (tanpa default otomatis); alamat tujuan diisi manual/dari desa default.
    /// </summary>
    public class IjinTinggalInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Surat Ijin Tinggal";

        // ===== Alamat tujuan =====
        private string _dusunTujuan = string.Empty;
        private string _desaTujuan = string.Empty;
        private string _kecamatanTujuan = string.Empty;
        private string _kabupatenTujuan = string.Empty;

        // ===== Penanggung jawab di alamat tujuan =====
        private string _nikPenanggungJawab = string.Empty;
        private string _namaPenanggungJawab = string.Empty;
        private string _tglLahirPenanggungJawab = string.Empty;
        private string _pekerjaanPenanggungJawab = string.Empty;

        public IjinTinggalInputViewModel(
            ILogger<IjinTinggalInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.IJIN_TINGGAL;

        public string DusunTujuan { get => _dusunTujuan; set => SetProperty(ref _dusunTujuan, value); }
        public string DesaTujuan { get => _desaTujuan; set => SetProperty(ref _desaTujuan, value); }
        public string KecamatanTujuan { get => _kecamatanTujuan; set => SetProperty(ref _kecamatanTujuan, value); }
        public string KabupatenTujuan { get => _kabupatenTujuan; set => SetProperty(ref _kabupatenTujuan, value); }

        public string NikPenanggungJawab { get => _nikPenanggungJawab; set => SetProperty(ref _nikPenanggungJawab, value); }
        public string NamaPenanggungJawab { get => _namaPenanggungJawab; set => SetProperty(ref _namaPenanggungJawab, value); }
        public string TglLahirPenanggungJawab { get => _tglLahirPenanggungJawab; set => SetProperty(ref _tglLahirPenanggungJawab, value); }
        public string PekerjaanPenanggungJawab { get => _pekerjaanPenanggungJawab; set => SetProperty(ref _pekerjaanPenanggungJawab, value); }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeSpecificAsync();
        }

        protected override async Task InitializeSpecificAsync()
        {
            await FillAlamatTujuanWithDefaultAsync();
        }

        public override async Task FillDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                ClearControls();
                await FillAlamatTujuanWithDefaultAsync();
                return;
            }

            await base.FillDataAsync(suratData);
        }

        protected override async Task FillAlamatAsync(
            string? dusun, string? desa, string? kecamatan, string? kabupaten,
            CancellationToken cancellationToken = default)
        {
            Dusun = dusun ?? string.Empty;
            Desa = desa ?? string.Empty;
            Kecamatan = kecamatan ?? string.Empty;
            Kabupaten = kabupaten ?? string.Empty;
            await Task.CompletedTask;
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            DusunTujuan = string.Empty;
            DesaTujuan = string.Empty;
            KecamatanTujuan = string.Empty;
            KabupatenTujuan = string.Empty;
            NikPenanggungJawab = string.Empty;
            NamaPenanggungJawab = string.Empty;
            TglLahirPenanggungJawab = string.Empty;
            PekerjaanPenanggungJawab = string.Empty;
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            Keterangan = await GenerateKeteranganAsync(suratData);

            DusunTujuan = suratData.DusunTujuan ?? string.Empty;
            DesaTujuan = suratData.DesaTujuan ?? string.Empty;
            KecamatanTujuan = suratData.KecTujuan ?? string.Empty;
            KabupatenTujuan = suratData.KabTujuan ?? string.Empty;

            NikPenanggungJawab = suratData.NikPenanggungJawab ?? string.Empty;
            NamaPenanggungJawab = suratData.NamaPenanggungJawab ?? string.Empty;
            TglLahirPenanggungJawab = suratData.TglLahirPenanggungJawab ?? string.Empty;
            PekerjaanPenanggungJawab = suratData.PekerjaanPenanggungJawab ?? string.Empty;

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

            suratData.NamaJenis = SuratConstants.IJIN_TINGGAL;
            suratData.Keperluan = DefaultKeperluan;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            suratData.DusunTujuan = DusunTujuan.Trim();
            suratData.DesaTujuan = DesaTujuan.Trim();
            suratData.KecTujuan = KecamatanTujuan.Trim();
            suratData.KabTujuan = KabupatenTujuan.Trim();

            suratData.NikPenanggungJawab = NikPenanggungJawab.Trim();
            suratData.NamaPenanggungJawab = NamaPenanggungJawab.Trim();
            suratData.TglLahirPenanggungJawab = TglLahirPenanggungJawab.Trim();
            suratData.PekerjaanPenanggungJawab = PekerjaanPenanggungJawab.Trim();

            // Sub-model IjinTinggal dibuat otomatis oleh SuratData dan tetap
            // divalidasi oleh SuratDataValidator; isi alamat asal & tujuan agar valid.
            suratData.IjinTinggal ??= new IjinTinggalData();
            suratData.IjinTinggal.AlamatAsal = $"Dusun {Dusun.Trim()}, Desa {Desa.Trim()}, Kecamatan {Kecamatan.Trim()}, Kabupaten {Kabupaten.Trim()}";
            string dusunTujuanAlamat = string.IsNullOrWhiteSpace(DusunTujuan.Trim())
                ? string.Empty
                : $"Dusun {DusunTujuan.Trim()}, ";
            suratData.IjinTinggal.TujuanTinggal =
                $"{dusunTujuanAlamat}Desa {DesaTujuan.Trim()}, Kecamatan {KecamatanTujuan.Trim()}, Kabupaten {KabupatenTujuan.Trim()}";

            if (suratData.Warga != null)
            {
                suratData.Warga.Dusun = Dusun.Trim();
                suratData.Warga.Desa = Desa.Trim();
                suratData.Warga.Kecamatan = Kecamatan.Trim();
                suratData.Warga.Kabupaten = Kabupaten.Trim();
                suratData.Warga.NamaJenis = SuratConstants.IJIN_TINGGAL;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            Keterangan = await GenerateKeteranganAsync(suratData);
            suratData.Keterangan = Keterangan.Trim();
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                errors.Add("Keterangan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(DesaTujuan))
            {
                errors.Add("Desa tujuan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KecamatanTujuan))
            {
                errors.Add("Kecamatan tujuan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KabupatenTujuan))
            {
                errors.Add("Kabupaten tujuan harus diisi");
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

        private async Task<string> GenerateKeteranganAsync(SuratData? suratData = null)
        {
            try
            {
                string desaAsal, kecAsal, kabAsal;
                string dusunTujuan, desaTujuan, kecTujuan, kabTujuan;

                if (suratData?.Warga != null)
                {
                    desaAsal = suratData.Warga.Desa ?? Desa ?? string.Empty;
                    kecAsal = suratData.Warga.Kecamatan ?? Kecamatan ?? string.Empty;
                    kabAsal = suratData.Warga.Kabupaten ?? Kabupaten ?? string.Empty;

                    dusunTujuan = suratData.DusunTujuan ?? DusunTujuan ?? string.Empty;
                    desaTujuan = suratData.DesaTujuan ?? DesaTujuan ?? string.Empty;
                    kecTujuan = suratData.KecTujuan ?? KecamatanTujuan ?? string.Empty;
                    kabTujuan = suratData.KabTujuan ?? KabupatenTujuan ?? string.Empty;
                }
                else
                {
                    desaAsal = Desa ?? string.Empty;
                    kecAsal = Kecamatan ?? string.Empty;
                    kabAsal = Kabupaten ?? string.Empty;

                    dusunTujuan = DusunTujuan ?? string.Empty;
                    desaTujuan = DesaTujuan ?? string.Empty;
                    kecTujuan = KecamatanTujuan ?? string.Empty;
                    kabTujuan = KabupatenTujuan ?? string.Empty;
                }

                string alamatTujuan = string.IsNullOrWhiteSpace(dusunTujuan)
                    ? $"Desa {desaTujuan} Kecamatan {kecTujuan} Kabupaten {kabTujuan}"
                    : $"{dusunTujuan} Desa {desaTujuan} Kecamatan {kecTujuan} Kabupaten {kabTujuan}";

                return $"Benar nama tersebut diatas adalah warga Desa {desaAsal} Kecamatan {kecAsal} Kabupaten {kabAsal}. " +
                       $"Menurut sepengetahuan kami nama tersebut diatas sampai saat ini " +
                       $"bertempat tinggal sementara di {alamatTujuan}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating default keterangan");
                return "Keterangan tidak dapat dimuat";
            }
        }

        private async Task FillAlamatTujuanWithDefaultAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData != null)
                {
                    DusunTujuan = string.Empty;
                    DesaTujuan = desaData.NamaDesa ?? string.Empty;
                    KecamatanTujuan = desaData.Kecamatan ?? string.Empty;
                    KabupatenTujuan = desaData.Kabupaten ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saat mengisi alamat tujuan default");
            }
        }
    }
}