using System.ComponentModel.DataAnnotations;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using Validator = SuDesApp.Utilities.Validator;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel dasar untuk form input surat (WPF) — setara BaseSuratInput (WinForms).
    /// Memuat field umum: NIK, Nama, Tempat/Tanggal Lahir, Pekerjaan, Alamat,
    /// Keterangan, serta pejabat penandatangan (Kades/Sekdes).
    /// </summary>
    public abstract class BaseSuratInputViewModel : ObservableObject, ISuratInput
    {
        protected const int NikLength = 16;
        protected const string DateFormatUi = "dd-MM-yyyy";
        protected const string DateFormatDb = "yyyy-MM-dd";

        protected static readonly string[] ValidJenisKelaminOptions = { "Laki-laki", "Perempuan" };
        protected static readonly string[] ValidAgamaOptions = { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" };
        protected static readonly string[] ValidStatusPerkawinanOptions = { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" };
        protected static readonly string[] ValidKewarganegaraanOptions = { "WNI", "WNA" };

        // Field umum (binding dua arah)
        private string _nomorSurat = string.Empty;
        private string _nik = string.Empty;
        private string _nama = string.Empty;
        private string _tempatLahir = string.Empty;
        private string _tanggalLahir = string.Empty;
        private string _pekerjaan = string.Empty;
        private string _pendidikan = string.Empty;
        private string _dusun = string.Empty;
        private string _desa = string.Empty;
        private string _kecamatan = string.Empty;
        private string _kabupaten = string.Empty;
        private string _keterangan = string.Empty;
        private string _jenisKelamin = string.Empty;
        private string _agama = string.Empty;
        private string _statusPerkawinan = string.Empty;
        private string _kewarganegaraan = string.Empty;
        private bool _isKades = true;
        private bool _nomorSuratEnabled = true;

        protected readonly ILogger _logger;
        protected readonly AppConfig _appConfig;
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly IMessageService _messageService;
        protected bool _isEditMode;

        protected BaseSuratInputViewModel(
            ILogger logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        }

        // ===== Observable Properties =====
        public string NomorSurat { get => _nomorSurat; set => SetProperty(ref _nomorSurat, value); }
        public bool NomorSuratEnabled { get => _nomorSuratEnabled; set => SetProperty(ref _nomorSuratEnabled, value); }

        /// <summary>Keterangan kotak nomor surat; ditimpa alur yang mengisinya sendiri.</summary>
        public virtual string NomorSuratTooltip => "Nomor surat (otomatis jika dikelola register)";

        public string Nik { get => _nik; set => SetProperty(ref _nik, value); }
        public string Nama { get => _nama; set => SetProperty(ref _nama, value); }
        public string TempatLahir { get => _tempatLahir; set => SetProperty(ref _tempatLahir, value); }
        public string TanggalLahir { get => _tanggalLahir; set => SetProperty(ref _tanggalLahir, value); }
        public string Pekerjaan { get => _pekerjaan; set => SetProperty(ref _pekerjaan, value); }
        public string Pendidikan { get => _pendidikan; set => SetProperty(ref _pendidikan, value); }
        public string Dusun { get => _dusun; set => SetProperty(ref _dusun, value); }
        public string Desa { get => _desa; set => SetProperty(ref _desa, value); }
        public string Kecamatan { get => _kecamatan; set => SetProperty(ref _kecamatan, value); }
        public string Kabupaten { get => _kabupaten; set => SetProperty(ref _kabupaten, value); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value); }

        public string JenisKelamin
        {
            get => _jenisKelamin;
            set => SetProperty(ref _jenisKelamin, value);
        }
        public string Agama { get => _agama; set => SetProperty(ref _agama, value); }
        public string StatusPerkawinan { get => _statusPerkawinan; set => SetProperty(ref _statusPerkawinan, value); }
        public string Kewarganegaraan { get => _kewarganegaraan; set => SetProperty(ref _kewarganegaraan, value); }

        public bool IsKadesSelected
        {
            get => _isKades;
            set
            {
                if (SetProperty(ref _isKades, value))
                {
                    if (value) { _isSekdes = false; OnPropertyChanged(nameof(IsSekdesSelected)); }
                    OnPropertyChanged(nameof(IsSekdesSelected));
                    OnPropertyChanged(nameof(PejabatLabel));
                }
            }
        }

        private bool _isSekdes;
        public bool IsSekdesSelected
        {
            get => _isSekdes;
            set
            {
                if (SetProperty(ref _isSekdes, value))
                {
                    if (value) { _isKades = false; OnPropertyChanged(nameof(IsKadesSelected)); }
                    OnPropertyChanged(nameof(IsKadesSelected));
                    OnPropertyChanged(nameof(PejabatLabel));
                }
            }
        }

        public string PejabatLabel => IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

        public IReadOnlyList<string> JenisKelaminOptions => ValidJenisKelaminOptions;
        public IReadOnlyList<string> AgamaOptions => ValidAgamaOptions;
        public IReadOnlyList<string> StatusPerkawinanOptions => ValidStatusPerkawinanOptions;
        public IReadOnlyList<string> KewarganegaraanOptions => ValidKewarganegaraanOptions;

        // ===== Kait untuk alur turunan =====
        protected virtual Task InitializeSpecificAsync() => Task.CompletedTask;
        protected virtual Task FillSpecificDataAsync(SuratData? suratData) => Task.CompletedTask;
        protected virtual Task CollectSpecificDataAsync(SuratData? suratData) => Task.CompletedTask;
        protected virtual void ValidateSpecificFields(List<string> errors) { }

        /// <summary>
        /// Mode alur "Batal → simpan sebagai DRAFT": true berarti isian yang belum
        /// lengkap tetap dikumpulkan tanpa validasi ketat dan tanpa menyentuh tabel
        /// Warga (dibuat nanti saat insert oleh repository). Inti draft adalah data
        /// boleh belum lengkap. Diatur oleh host alur (InputWindowViewModel), bukan
        /// oleh tipe form — jadi NTCR berperilaku sama dengan jenis surat lain.
        /// </summary>
        public bool DraftToleran { get; set; }

        /// <summary>Dipanggil saat form dibuka (surat baru).</summary>
        public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeAlamatAsync(cancellationToken);
            await InitializeSpecificAsync();
        }

        protected async Task InitializeAlamatAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Desa) && !string.IsNullOrWhiteSpace(Kecamatan) && !string.IsNullOrWhiteSpace(Kabupaten))
                    return;

                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData == null) return;

                if (string.IsNullOrWhiteSpace(Desa)) Desa = desaData.NamaDesa ?? string.Empty;
                if (string.IsNullOrWhiteSpace(Kecamatan)) Kecamatan = desaData.Kecamatan ?? string.Empty;
                if (string.IsNullOrWhiteSpace(Kabupaten)) Kabupaten = desaData.Kabupaten ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengisi alamat default: {Message}", ex.Message);
            }
        }

        // ===== ISuratInput =====
        public virtual string GetNIK() => Nik.Trim();
        public virtual string GetNama() => Nama.Trim();
        public virtual string GetTempatLahir() => TempatLahir.Trim();
        public virtual string GetTanggalLahir() => TanggalLahir.Trim();
        public virtual string GetJenisKelamin() => string.IsNullOrWhiteSpace(JenisKelamin) ? "Laki-laki" : JenisKelamin.Trim();
        public virtual string GetAgama() => string.IsNullOrWhiteSpace(Agama) ? "Islam" : Agama.Trim();
        public virtual string GetPekerjaan() => Pekerjaan.Trim();
        public virtual string GetStatusPerkawinan() => string.IsNullOrWhiteSpace(StatusPerkawinan) ? "Kawin" : StatusPerkawinan.Trim();
        public virtual string GetPendidikan() => Pendidikan.Trim();
        public virtual string GetKewarganegaraan() => string.IsNullOrWhiteSpace(Kewarganegaraan) ? "WNI" : Kewarganegaraan.Trim();

        public virtual string GetAlamat() =>
            $"{CleanComponent(Dusun)}, {CleanComponent(Desa)}, {CleanComponent(Kecamatan)}, {CleanComponent(Kabupaten)}";

        public virtual string GetKeteranganTextBox() => Keterangan.Trim();
        public virtual string GetTanggalSurat() => DateTime.Now.ToString(DateFormatUi);
        public virtual string GetNomorSurat() => NomorSurat.Trim();

        public virtual void SetNomorSurat(string nomorSurat)
        {
            NomorSurat = nomorSurat;
            NomorSuratEnabled = !(_isEditMode && !string.IsNullOrEmpty(nomorSurat));
        }

        public virtual void SetEditMode(bool isEditMode)
        {
            _isEditMode = isEditMode;
            NomorSuratEnabled = !isEditMode;
        }

        public virtual bool ValidateInput(out DateTime tglLahir)
        {
            tglLahir = DateTime.MinValue;
            var errors = new List<string>();

            ValidateBasicFields(errors, out tglLahir);
            ValidateComboBoxFields(errors);
            ValidateAlamatFields(errors);
            ValidateSpecificFields(errors);

            if (tglLahir > DateTime.Now)
                errors.Add("Tanggal lahir tidak boleh di masa depan");
            else if (tglLahir < new DateTime(1900, 1, 1))
                errors.Add("Tanggal lahir tidak valid (terlalu lampau)");

            if (errors.Count == 0) return true;

            _ = _messageService.ShowErrorAsync(string.Join(Environment.NewLine, errors));
            return false;
        }

        private void ValidateBasicFields(List<string> errors, out DateTime tglLahir)
        {
            tglLahir = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(Nik))
                errors.Add("NIK tidak boleh kosong");
            else if (!Validator.ValidateNik(Nik.Trim(), out var nikError))
                errors.Add(nikError);

            if (string.IsNullOrWhiteSpace(Nama))
                errors.Add("Nama tidak boleh kosong");
            else if (!Validator.ValidateNama(Nama.Trim(), out var namaError))
                errors.Add(namaError);

            if (string.IsNullOrWhiteSpace(TempatLahir))
                errors.Add("Tempat Lahir tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(TanggalLahir))
                errors.Add("Tanggal Lahir tidak boleh kosong");
            else if (!Validator.ValidateTanggalLahir(TanggalLahir.Trim(), "Tanggal Lahir", out var tglError))
                errors.Add(tglError);
            else if (!DateTime.TryParseExact(TanggalLahir.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglLahir))
                errors.Add("Format Tanggal Lahir tidak valid (DD-MM-YYYY)");
        }

        private void ValidateComboBoxFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(JenisKelamin))
                errors.Add("Jenis Kelamin harus dipilih");
            else if (!ValidJenisKelaminOptions.Contains(JenisKelamin.Trim(), StringComparer.OrdinalIgnoreCase))
                errors.Add("Jenis Kelamin tidak valid");
        }

        private void ValidateAlamatFields(List<string> errors)
        {
            AddIfEmpty(errors, Dusun, "Dusun/Jalan tidak boleh kosong");
            AddIfEmpty(errors, Desa, "Desa tidak boleh kosong");
            AddIfEmpty(errors, Kecamatan, "Kecamatan tidak boleh kosong");
            AddIfEmpty(errors, Kabupaten, "Kabupaten tidak boleh kosong");
        }

        private static void AddIfEmpty(List<string> errors, string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add(message);
        }

        // ===== Alur data =====
        public virtual async Task FillDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                ClearControls();
                await InitializeAlamatAsync();
                return;
            }

            SetControlText(ref _nomorSurat, suratData.NomorSurat);

            await FillAlamatAsync(suratData.Warga?.Dusun, suratData.Warga?.Desa,
                suratData.Warga?.Kecamatan, suratData.Warga?.Kabupaten);

            Nik = suratData.Warga?.NIK ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(Nik))
            {
                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(Nik);
                if (warga != null) await FillFieldsFromWargaAsync(warga);
                else FillFromSuratData(suratData);
            }
            else
            {
                FillFromSuratData(suratData);
            }

            await FillSpecificDataAsync(suratData);
            OnPropertyChanged(null);
        }

        protected virtual async Task FillAlamatAsync(string? dusun, string? desa, string? kecamatan, string? kabupaten, CancellationToken cancellationToken = default)
        {
            Dusun = dusun ?? string.Empty;
            Desa = desa ?? string.Empty;
            Kecamatan = kecamatan ?? string.Empty;
            Kabupaten = kabupaten ?? string.Empty;

            if (string.IsNullOrWhiteSpace(desa) || string.IsNullOrWhiteSpace(kecamatan) || string.IsNullOrWhiteSpace(kabupaten))
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData != null)
                {
                    if (string.IsNullOrWhiteSpace(Desa)) Desa = desaData.NamaDesa ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(Kecamatan)) Kecamatan = desaData.Kecamatan ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(Kabupaten)) Kabupaten = desaData.Kabupaten ?? string.Empty;
                }
            }
        }

        protected void FillFromSuratData(SuratData suratData)
        {
            if (suratData?.Warga == null) return;
            FillWargaFields(suratData.Warga);
        }

        protected void FillWargaFields(WargaData warga)
        {
            Nama = warga.Nama ?? string.Empty;
            TempatLahir = warga.TempatLahir ?? string.Empty;
            TanggalLahir = ParseTanggalLahirToUiFormat(warga.TanggalLahir);
            Pekerjaan = warga.Pekerjaan ?? string.Empty;
            Pendidikan = warga.Pendidikan ?? string.Empty;

            JenisKelamin = warga.JenisKelamin ?? string.Empty;
            Agama = warga.Agama ?? string.Empty;
            StatusPerkawinan = warga.StatusPerkawinan ?? string.Empty;
            Kewarganegaraan = warga.Kewarganegaraan ?? string.Empty;
        }

        protected async Task FillFieldsFromWargaAsync(WargaData warga)
        {
            if (warga == null) return;
            FillWargaFields(warga);
            await FillAlamatAsync(warga.Dusun, warga.Desa, warga.Kecamatan, warga.Kabupaten);
        }

        public virtual async Task CollectDataAsync(SuratData? suratData)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));

            suratData.Warga = CollectBasicWargaData();

            if (DraftToleran)
            {
                // Alur draft: kumpulkan isian tanpa memblokir — data yang belum
                // lengkap diukur kemudian oleh pemanggil (ValidateAsync pada
                // SuratData), dan warga baru dibuat saat insert (paritas jalur
                // non-draft lewat GetOrCreateWargaAsync).
                await CollectSuratSpecificData(suratData);
                return;
            }

            if (!ValidateInput(out _))
                throw new ValidationException("Validasi input gagal");

            await SaveWargaData(suratData);
            await CollectSuratSpecificData(suratData);
        }

        protected virtual WargaData CollectBasicWargaData() => new WargaData
        {
            NIK = GetNIK(),
            Nama = GetNama(),
            TempatLahir = GetTempatLahir(),
            TanggalLahir = ParseTanggalLahirToDbFormat(GetTanggalLahir()),
            JenisKelamin = GetJenisKelamin(),
            Agama = GetAgama(),
            Pekerjaan = GetPekerjaan(),
            StatusPerkawinan = GetStatusPerkawinan(),
            Pendidikan = GetPendidikan(),
            Kewarganegaraan = GetKewarganegaraan(),
            Dusun = Dusun.Trim(),
            Desa = Desa.Trim(),
            Kecamatan = Kecamatan.Trim(),
            Kabupaten = Kabupaten.Trim()
        };

        private async Task SaveWargaData(SuratData suratData)
        {
            if (_unitOfWork.WargaRepository == null)
                throw new InvalidOperationException("WargaRepository tidak tersedia");

            int idWarga = await _unitOfWork.WargaRepository.AddOrUpdateWargaAndGetIdAsync(suratData.Warga!);
            suratData.Warga!.ID_Warga = idWarga;
        }

        private async Task CollectSuratSpecificData(SuratData suratData)
        {
            suratData.NomorSurat = GetNomorSurat();
            suratData.TanggalSurat = (_isEditMode && suratData.TanggalSurat != default) ? suratData.TanggalSurat : DateTime.Now;

            await GetNamaPejabatPenandatanganAsync(suratData);
            await CollectSpecificDataAsync(suratData);
        }

        protected virtual async Task GetNamaPejabatPenandatanganAsync(SuratData suratData)
        {
            try
            {
                if (_unitOfWork.DesaRepository == null) return;

                if (suratData.Desa == null || string.IsNullOrWhiteSpace(suratData.Desa.NamaDesa))
                    suratData.Desa = await _unitOfWork.DesaRepository.GetInfoDesaAsync() ?? new DesaData();

                bool isKades = IsKadesSelected;
                suratData.PejabatPenandatangan = isKades ? "Kepala Desa" : "Sekretaris Desa";
                suratData.NamaPejabatPenandatangan = isKades
                    ? suratData.Desa?.KepalaDesa ?? "NAMA KEPALA DESA BELUM DISET"
                    : suratData.Desa?.SekretarisDesa ?? "NAMA SEKRETARIS DESA BELUM DISET";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetNamaPejabatPenandatanganAsync");
                suratData.NamaPejabatPenandatangan = "NAMA PEJABAT TIDAK TERSEDIA";
            }
        }

        // ===== Pencarian warga via NIK =====
        public virtual async Task OnNikLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = Nik?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearDataWarga();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearDataWarga();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    await FillFieldsFromWargaAsync(warga);
                }
                // Jika tidak ditemukan, biarkan pengguna mengisi manual (paritas WinForms).
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK: {Nik}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK");
            }
        }

        // ===== Utility =====
        private void SetControlText(ref string field, string? value) => field = value ?? string.Empty;

        protected virtual void ClearControls()
        {
            NomorSurat = string.Empty;
            Nik = string.Empty;
            Nama = string.Empty;
            TempatLahir = string.Empty;
            TanggalLahir = string.Empty;
            Pekerjaan = string.Empty;
            Pendidikan = string.Empty;
            Dusun = string.Empty;
            Desa = string.Empty;
            Kecamatan = string.Empty;
            Kabupaten = string.Empty;
            Keterangan = string.Empty;
            JenisKelamin = string.Empty;
            Agama = string.Empty;
            StatusPerkawinan = string.Empty;
            Kewarganegaraan = string.Empty;
            IsKadesSelected = true;
        }

        protected virtual void ClearDataWarga()
        {
            Nama = string.Empty;
            TempatLahir = string.Empty;
            TanggalLahir = string.Empty;
            Pekerjaan = string.Empty;
            Pendidikan = string.Empty;
            Agama = string.Empty;
            JenisKelamin = string.Empty;
            StatusPerkawinan = string.Empty;
            Kewarganegaraan = string.Empty;
            Dusun = string.Empty;
        }

        private static string CleanComponent(string? component) => component?.Replace(",", "").Trim() ?? string.Empty;

        protected static string ParseTanggalLahirToUiFormat(string? dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue)) return string.Empty;
            if (DateTime.TryParseExact(dbValue, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString(DateFormatUi);
            if (DateTime.TryParse(dbValue, CultureInfo.GetCultureInfo("id-ID"), DateTimeStyles.None, out var dt2))
                return dt2.ToString(DateFormatUi);
            return dbValue;
        }

        protected static string ParseTanggalLahirToDbFormat(string? uiValue)
        {
            if (string.IsNullOrWhiteSpace(uiValue)) return string.Empty;
            if (DateTime.TryParseExact(uiValue, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString(DateFormatDb);
            return uiValue.Trim();
        }
    }
}