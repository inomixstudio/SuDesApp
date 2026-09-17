using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input surat NTCR (persyaratan pendaftaran pernikahan N1-N4).
    /// Satu VM untuk keempat surat (NTCR_N1..NTCR_N4); NamaJenis di-set oleh
    /// InputControlFactory sesuai template yang dipilih user.
    /// Data calon suami diisi pada form dasar (pemohon), calon istri dan nama
    /// orang tua disimpan di sub-model NtcrData / tabel "NTCR".
    /// </summary>
    public class NtcrInputViewModel : BaseSuratInputViewModel
    {
        private string _namaJenis = SuratConstants.NTCR_N1;

        // ===== Calon istri =====
        private string _nikIstri = string.Empty;
        private string _namaIstri = string.Empty;
        private string _tempatLahirIstri = string.Empty;
        private string _tanggalLahirIstri = string.Empty;
        private string _agamaIstri = string.Empty;
        private string _pekerjaanIstri = string.Empty;
        private string _alamatIstri = string.Empty;

        // ===== Orang tua =====
        private string _namaAyahCalonSuami = string.Empty;
        private string _namaIbuCalonSuami = string.Empty;
        private string _namaAyahCalonIstri = string.Empty;
        private string _namaIbuCalonIstri = string.Empty;

        // ===== Kolom per jenis surat =====
        private string _statusPerkawinanIstri = string.Empty;
        private string _keteranganTemuan = string.Empty;
        private string _tujuanSurat = string.Empty;

        /// <summary>Status perkawinan calon istri (Sudah/Belum Kawin — sumber temuan N2).</summary>
        public string StatusPerkawinanIstri { get => _statusPerkawinanIstri; set => SetProperty(ref _statusPerkawinanIstri, value); }

        /// <summary>Kolom temuan untuk N2: dasar keterangan bila janda/duda (mis. Akta Kematian).</summary>
        public string KeteranganTemuan { get => _keteranganTemuan; set => SetProperty(ref _keteranganTemuan, value); }

        /// <summary>Tujuan surat untuk N3: kepentingan pendaftaran pernikahan di KUA/Kemenag.</summary>
        public string TujuanSurat { get => _tujuanSurat; set => SetProperty(ref _tujuanSurat, value); }

        /// <summary>Apakah ini Surat Keterangan Untuk Nikah (N2) — menampilkan kolom Temuan.</summary>
        public bool IsN2 => string.Equals(NamaJenis, SuratConstants.NTCR_N2, StringComparison.OrdinalIgnoreCase);

        /// <summary>Apakah ini Surat Persetujuan Calon Mempelai (N3) — menampilkan kolom Tujuan Surat.</summary>
        public bool IsN3 => string.Equals(NamaJenis, SuratConstants.NTCR_N3, StringComparison.OrdinalIgnoreCase);

        /// <summary>Daftar pilihan status perkawinan calon istri (mengikuti opsi baku).</summary>
        public IReadOnlyList<string> StatusPerkawinanIstriOptions => ValidStatusPerkawinanOptions;

        /// <summary>Opsi pejabat penandatangan sesuai kop surat desa.</summary>
        public IReadOnlyList<string> PejabatPenandatanganOptions { get; } = new[] { "Kepala Desa", "Sekretaris Desa" };

        /// <summary>Pejabat penandatangan (sinkron dengan pilihan Kepala/Sekretaris Desa).</summary>
        public string SelectedPejabat
        {
            get => IsSekdesSelected ? "Sekretaris Desa" : "Kepala Desa";
            set
            {
                if (string.Equals(value, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                    IsSekdesSelected = true;
                else
                    IsKadesSelected = true;
                OnPropertyChanged(nameof(SelectedPejabat));
            }
        }

        public NtcrInputViewModel(
            ILogger<NtcrInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        /// <summary>NamaJenis dari template yang sedang dibuat/diedit (NTCR_N1..NTCR_N4).</summary>
        public string NamaJenis
        {
            get => _namaJenis;
            set
            {
                if (SetProperty(ref _namaJenis, value))
                {
                    OnPropertyChanged(nameof(IsN2));
                    OnPropertyChanged(nameof(IsN3));
                }
            }
        }

        public string NikIstri { get => _nikIstri; set => SetProperty(ref _nikIstri, value); }
        public string NamaIstri { get => _namaIstri; set => SetProperty(ref _namaIstri, value); }
        public string TempatLahirIstri { get => _tempatLahirIstri; set => SetProperty(ref _tempatLahirIstri, value); }
        public string TanggalLahirIstri { get => _tanggalLahirIstri; set => SetProperty(ref _tanggalLahirIstri, value); }
        public string AgamaIstri { get => _agamaIstri; set => SetProperty(ref _agamaIstri, value); }
        public string PekerjaanIstri { get => _pekerjaanIstri; set => SetProperty(ref _pekerjaanIstri, value); }
        public string AlamatIstri { get => _alamatIstri; set => SetProperty(ref _alamatIstri, value); }

        public string NamaAyahCalonSuami { get => _namaAyahCalonSuami; set => SetProperty(ref _namaAyahCalonSuami, value); }
        public string NamaIbuCalonSuami { get => _namaIbuCalonSuami; set => SetProperty(ref _namaIbuCalonSuami, value); }
        public string NamaAyahCalonIstri { get => _namaAyahCalonIstri; set => SetProperty(ref _namaAyahCalonIstri, value); }
        public string NamaIbuCalonIstri { get => _namaIbuCalonIstri; set => SetProperty(ref _namaIbuCalonIstri, value); }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeSpecificAsync();
        }

        public override async Task FillDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                ClearControls();
                await InitializeAlamatAsync();
                return;
            }

            await base.FillDataAsync(suratData);
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            NikIstri = string.Empty;
            NamaIstri = string.Empty;
            TempatLahirIstri = string.Empty;
            TanggalLahirIstri = string.Empty;
            AgamaIstri = string.Empty;
            PekerjaanIstri = string.Empty;
            AlamatIstri = string.Empty;
            NamaAyahCalonSuami = string.Empty;
            NamaIbuCalonSuami = string.Empty;
            NamaAyahCalonIstri = string.Empty;
            NamaIbuCalonIstri = string.Empty;
            StatusPerkawinanIstri = string.Empty;
            KeteranganTemuan = string.Empty;
            TujuanSurat = string.Empty;
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData?.Ntcr == null) return Task.CompletedTask;

            var ntcr = suratData.Ntcr;
            NikIstri = ntcr.NikIstri ?? string.Empty;
            NamaIstri = ntcr.NamaIstri ?? string.Empty;
            TempatLahirIstri = ntcr.TempatLahirIstri ?? string.Empty;
            TanggalLahirIstri = ntcr.TanggalLahirIstri ?? string.Empty;
            AgamaIstri = ntcr.AgamaIstri ?? string.Empty;
            PekerjaanIstri = ntcr.PekerjaanIstri ?? string.Empty;
            AlamatIstri = ntcr.AlamatIstri ?? string.Empty;

            NamaAyahCalonSuami = ntcr.NamaAyahCalonSuami ?? string.Empty;
            NamaIbuCalonSuami = ntcr.NamaIbuCalonSuami ?? string.Empty;
            NamaAyahCalonIstri = ntcr.NamaAyahCalonIstri ?? string.Empty;
            NamaIbuCalonIstri = ntcr.NamaIbuCalonIstri ?? string.Empty;
            StatusPerkawinanIstri = ntcr.StatusPerkawinanIstri ?? string.Empty;
            KeteranganTemuan = ntcr.KeteranganTemuan ?? string.Empty;
            TujuanSurat = ntcr.TujuanSurat ?? string.Empty;

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

            suratData.NamaJenis = NamaJenis.Trim();
            suratData.Keperluan = GetDefaultKeperluan();
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            suratData.Ntcr ??= new NtcrData();
            suratData.Ntcr.NikIstri = NikIstri.Trim();
            suratData.Ntcr.NamaIstri = NamaIstri.Trim();
            suratData.Ntcr.TempatLahirIstri = TempatLahirIstri.Trim();
            suratData.Ntcr.TanggalLahirIstri = TanggalLahirIstri.Trim();
            suratData.Ntcr.AgamaIstri = AgamaIstri.Trim();
            suratData.Ntcr.PekerjaanIstri = PekerjaanIstri.Trim();
            suratData.Ntcr.AlamatIstri = AlamatIstri.Trim();

            suratData.Ntcr.NamaAyahCalonSuami = NamaAyahCalonSuami.Trim();
            suratData.Ntcr.NamaIbuCalonSuami = NamaIbuCalonSuami.Trim();
            suratData.Ntcr.NamaAyahCalonIstri = NamaAyahCalonIstri.Trim();
            suratData.Ntcr.NamaIbuCalonIstri = NamaIbuCalonIstri.Trim();

            suratData.Ntcr.StatusPerkawinanIstri = StatusPerkawinanIstri.Trim();
            suratData.Ntcr.KeteranganTemuan = KeteranganTemuan.Trim();
            suratData.Ntcr.TujuanSurat = TujuanSurat.Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.Dusun = Dusun.Trim();
                suratData.Warga.Desa = Desa.Trim();
                suratData.Warga.Kecamatan = Kecamatan.Trim();
                suratData.Warga.Kabupaten = Kabupaten.Trim();
                suratData.Warga.NamaJenis = NamaJenis.Trim();
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            await Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(NikIstri))
            {
                errors.Add("NIK calon istri tidak boleh kosong");
            }
            else if (!Validator.ValidateNik(NikIstri.Trim(), out var nikIstriError))
            {
                errors.Add($"NIK calon istri: {nikIstriError}");
            }

            if (string.IsNullOrWhiteSpace(NamaIstri))
            {
                errors.Add("Nama calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(TempatLahirIstri))
            {
                errors.Add("Tempat lahir calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(TanggalLahirIstri))
            {
                errors.Add("Tanggal lahir calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(AgamaIstri) ||
                !ValidAgamaOptions.Contains(AgamaIstri.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Agama calon istri harus dipilih dari daftar yang tersedia");
            }

            if (string.IsNullOrWhiteSpace(PekerjaanIstri))
            {
                errors.Add("Pekerjaan calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinan) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan calon suami harus dipilih dari daftar yang tersedia");
            }

            // Kolom khusus N2: status perkawinan calon istri wajib; temuan wajib bila Sudah Kawin.
            if (IsN2)
            {
                if (string.IsNullOrWhiteSpace(StatusPerkawinanIstri) ||
                    !ValidStatusPerkawinanOptions.Contains(StatusPerkawinanIstri.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add("Status Perkawinan calon istri harus dipilih dari daftar yang tersedia (N2)");
                }
                else if (StatusPerkawinanIstri.Trim().StartsWith("Sudah", StringComparison.OrdinalIgnoreCase) &&
                         string.IsNullOrWhiteSpace(KeteranganTemuan))
                {
                    errors.Add("Kolom Temuan wajib diisi bila status calon istri Sudah Kawin (janda/duda): isi dasarnya, mis. Akta Kematian No. ... Tahun ...");
                }
            }
        }

        private string GetDefaultKeperluan() => NamaJenis switch
        {
            SuratConstants.NTCR_N1 => "Surat Pengantar Nikah (NTCR N1)",
            SuratConstants.NTCR_N2 => "Surat Keterangan Untuk Nikah (NTCR N2)",
            SuratConstants.NTCR_N3 => "Surat Persetujuan Calon Mempelai (NTCR N3)",
            SuratConstants.NTCR_N4 => "Surat Keterangan Orang Tua (NTCR N4)",
            _ => NamaJenis
        };

        public virtual async Task OnNikIstriLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = NikIstri?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearDataWargaIstri();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearDataWargaIstri();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    FillFieldsFromWargaIstri(warga);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK calon istri: {NIK}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK calon istri");
            }
        }

        private void FillFieldsFromWargaIstri(WargaData warga)
        {
            if (warga == null) return;

            NamaIstri = warga.Nama ?? string.Empty;
            TempatLahirIstri = warga.TempatLahir ?? string.Empty;
            StatusPerkawinanIstri = warga.StatusPerkawinan ?? StatusPerkawinanIstri;
            if (!string.IsNullOrWhiteSpace(warga.TanggalLahir))
            {
                if (DateTime.TryParse(warga.TanggalLahir, out var tgl))
                {
                    TanggalLahirIstri = tgl.ToString(DateFormatUi);
                }
                else
                {
                    TanggalLahirIstri = warga.TanggalLahir;
                }
            }
            AgamaIstri = warga.Agama ?? string.Empty;
            PekerjaanIstri = warga.Pekerjaan ?? string.Empty;
            AlamatIstri = warga.Alamat ?? string.Empty;
        }

        private void ClearDataWargaIstri()
        {
            NamaIstri = string.Empty;
            TempatLahirIstri = string.Empty;
            TanggalLahirIstri = string.Empty;
            AgamaIstri = string.Empty;
            PekerjaanIstri = string.Empty;
            AlamatIstri = string.Empty;
        }
    }
}