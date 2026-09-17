using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using Validator = SuDesApp.Utilities.Validator;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Izin Orang Tua / Suami (IZIN_ORTU) — setara
    /// IzinOrtuInput (WinForms). Warga dasar adalah pemberi izin, dan ada
    /// blok anak ("Yang Diberi Izin") dengan pencarian NIK sendiri.
    /// </summary>
    public class IzinOrtuInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluanIzinOrtu = "Izin Bekerja ke Luar Negeri";

        // ===== Anak (yang diberi izin) =====
        private string _nikAnak = string.Empty;
        private string _namaAnak = string.Empty;
        private string _tempatLahirAnak = string.Empty;
        private string _tanggalLahirAnak = string.Empty;
        private string _jenisKelaminAnak = string.Empty;
        private string _agamaAnak = string.Empty;
        private string _statusPerkawinanAnak = string.Empty;
        private string _pekerjaanAnak = string.Empty;
        private string _dusunAnak = string.Empty;
        private string _desaAnak = string.Empty;
        private string _kecamatanAnak = string.Empty;
        private string _kabupatenAnak = string.Empty;

        // ===== Informasi tujuan =====
        private string _negaraTujuan = string.Empty;
        private string _namaPt = string.Empty;

        public IzinOrtuInputViewModel(
            ILogger<IzinOrtuInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.IZIN_ORTU;

        public string NIKAnak { get => _nikAnak; set => SetProperty(ref _nikAnak, value); }
        public string NamaAnak { get => _namaAnak; set => SetProperty(ref _namaAnak, value); }
        public string TempatLahirAnak { get => _tempatLahirAnak; set => SetProperty(ref _tempatLahirAnak, value); }
        public string TanggalLahirAnak { get => _tanggalLahirAnak; set => SetProperty(ref _tanggalLahirAnak, value); }
        public string JenisKelaminAnak { get => _jenisKelaminAnak; set => SetProperty(ref _jenisKelaminAnak, value); }
        public string AgamaAnak { get => _agamaAnak; set => SetProperty(ref _agamaAnak, value); }
        public string StatusPerkawinanAnak { get => _statusPerkawinanAnak; set => SetProperty(ref _statusPerkawinanAnak, value); }
        public string PekerjaanAnak { get => _pekerjaanAnak; set => SetProperty(ref _pekerjaanAnak, value); }
        public string DusunAnak { get => _dusunAnak; set => SetProperty(ref _dusunAnak, value); }
        public string DesaAnak { get => _desaAnak; set => SetProperty(ref _desaAnak, value); }
        public string KecamatanAnak { get => _kecamatanAnak; set => SetProperty(ref _kecamatanAnak, value); }
        public string KabupatenAnak { get => _kabupatenAnak; set => SetProperty(ref _kabupatenAnak, value); }

        public string NegaraTujuan { get => _negaraTujuan; set => SetProperty(ref _negaraTujuan, value); }
        public string NamaPT { get => _namaPt; set => SetProperty(ref _namaPt, value); }

        public IReadOnlyList<string> JenisKelaminAnakOptions => ValidJenisKelaminOptions;
        public IReadOnlyList<string> AgamaAnakOptions => ValidAgamaOptions;
        public IReadOnlyList<string> StatusPerkawinanAnakOptions => ValidStatusPerkawinanOptions;

        public override string GetAlamat() =>
            $"{Dusun.Trim()} Desa {Desa.Trim()} Kec. {Kecamatan.Trim()} Kab. {Kabupaten.Trim()}".Trim();

        public string GetAlamatAnak() =>
            $"{DusunAnak.Trim()} Desa {DesaAnak.Trim()} Kec. {KecamatanAnak.Trim()} Kab. {KabupatenAnak.Trim()}".Trim();

        protected override WargaData CollectBasicWargaData()
        {
            var warga = base.CollectBasicWargaData();
            warga.AlamatLengkap = GetAlamat();
            return warga;
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            NegaraTujuan = suratData.IzinOrtu?.NegaraTujuan ?? string.Empty;
            NamaPT = suratData.IzinOrtu?.NamaPT ?? string.Empty;

            WargaData? anak = null;
            if (suratData.IzinOrtu?.ID_Anak > 0)
            {
                try
                {
                    anak = await _unitOfWork.WargaRepository.GetWargaByIdAsync(suratData.IzinOrtu.ID_Anak);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal memuat warga anak ID {IdWargaAnak}", suratData.IzinOrtu.ID_Anak);
                }
            }

            if (anak != null)
            {
                await FillDataAnakFromWargaAsync(anak);
            }
            else
            {
                await FillDataAnakFromSuratDataAsync(suratData);
            }

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

            suratData.IzinOrtu ??= new IzinOrtuData();
            suratData.NamaJenis = SuratConstants.IZIN_ORTU;
            suratData.Keperluan = DefaultKeperluanIzinOrtu;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            var anakWarga = new WargaData
            {
                NIK = NIKAnak.Trim(),
                Nama = NamaAnak.Trim(),
                TempatLahir = TempatLahirAnak.Trim(),
                TanggalLahir = ParseTanggalLahirToDbFormat(TanggalLahirAnak),
                JenisKelamin = JenisKelaminAnak.Trim(),
                Agama = AgamaAnak.Trim(),
                StatusPerkawinan = StatusPerkawinanAnak.Trim(),
                Pekerjaan = PekerjaanAnak.Trim(),
                Dusun = DusunAnak.Trim(),
                Desa = DesaAnak.Trim(),
                Kecamatan = KecamatanAnak.Trim(),
                Kabupaten = KabupatenAnak.Trim(),
                AlamatLengkap = GetAlamatAnak(),
                Pendidikan = string.Empty,
                Kewarganegaraan = "WNI",
                NamaJenis = SuratConstants.IZIN_ORTU
            };

            int idWargaAnak = await _unitOfWork.WargaRepository.AddOrUpdateWargaAndGetIdAsync(anakWarga);
            suratData.IzinOrtu.ID_Anak = idWargaAnak;

            suratData.IzinOrtu.NIKAnak = anakWarga.NIK;
            suratData.IzinOrtu.NamaAnak = anakWarga.Nama;
            suratData.IzinOrtu.TempatLahirAnak = anakWarga.TempatLahir;
            suratData.IzinOrtu.TanggalLahirAnak = anakWarga.TanggalLahir;
            suratData.IzinOrtu.JenisKelaminAnak = anakWarga.JenisKelamin;
            suratData.IzinOrtu.AgamaAnak = anakWarga.Agama;
            suratData.IzinOrtu.StatusPerkawinanAnak = anakWarga.StatusPerkawinan;
            suratData.IzinOrtu.PekerjaanAnak = anakWarga.Pekerjaan;
            suratData.IzinOrtu.AlamatAnak = anakWarga.AlamatLengkap;
            suratData.IzinOrtu.NegaraTujuan = NegaraTujuan.Trim();
            suratData.IzinOrtu.NamaPT = NamaPT.Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.AlamatLengkap = GetAlamat();
                suratData.Warga.NamaJenis = SuratConstants.IZIN_ORTU;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            string negaraTujuanDisplay = string.IsNullOrWhiteSpace(suratData.IzinOrtu.NegaraTujuan)
                ? "[NEGARA TUJUAN BELUM DIISI]"
                : suratData.IzinOrtu.NegaraTujuan;
            string namaPtDisplay = string.IsNullOrWhiteSpace(suratData.IzinOrtu.NamaPT)
                ? "[NAMA PT BELUM DIISI]"
                : suratData.IzinOrtu.NamaPT;

            suratData.Keterangan = $"Untuk bekerja sebagai Tenaga Kerja Indonesia (TKI) di luar negeri dengan negara tujuan {negaraTujuanDisplay} melalui PT. {namaPtDisplay} " +
                                   $"sesuai dengan kontrak kerja yang berlaku dan saya tidak akan menggangu gugat dalam bentuk apapun.";
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            // Pemberi izin
            if (string.IsNullOrWhiteSpace(Agama) ||
                !ValidAgamaOptions.Contains(Agama.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Agama Pemberi Izin harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinan) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan Pemberi Izin harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(Pekerjaan))
            {
                errors.Add("Pekerjaan Pemberi Izin harus diisi");
            }

            // Anak
            if (string.IsNullOrWhiteSpace(NIKAnak) || !Validator.ValidateNik(NIKAnak.Trim(), out _))
            {
                errors.Add("NIK Anak harus 16 digit angka");
            }

            if (string.IsNullOrWhiteSpace(NamaAnak))
            {
                errors.Add("Nama Anak harus diisi");
            }
            else if (NamaAnak.Trim().Length < 3)
            {
                errors.Add("Nama Anak minimal 3 karakter");
            }

            if (string.IsNullOrWhiteSpace(TempatLahirAnak))
            {
                errors.Add("Tempat Lahir Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TanggalLahirAnak))
            {
                errors.Add("Tanggal Lahir Anak harus diisi");
            }
            else if (!DateTime.TryParseExact(TanggalLahirAnak.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tglLahirAnak))
            {
                errors.Add("Tanggal Lahir Anak harus dalam format DD-MM-YYYY");
            }
            else if (tglLahirAnak > DateTime.Now)
            {
                errors.Add("Tanggal Lahir Anak tidak boleh di masa depan");
            }

            if (string.IsNullOrWhiteSpace(JenisKelaminAnak) ||
                !ValidJenisKelaminOptions.Contains(JenisKelaminAnak.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Jenis Kelamin Anak harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(AgamaAnak) ||
                !ValidAgamaOptions.Contains(AgamaAnak.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Agama Anak harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinanAnak) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinanAnak.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan Anak harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(DesaAnak))
            {
                errors.Add("Desa Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KecamatanAnak))
            {
                errors.Add("Kecamatan Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(KabupatenAnak))
            {
                errors.Add("Kabupaten Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(NegaraTujuan))
            {
                errors.Add("Negara Tujuan harus diisi");
            }
        }

        public virtual async Task OnNikAnakLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = NIKAnak?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearDataAnak();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearDataAnak();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    await FillDataAnakFromWargaAsync(warga);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK anak: {Nik}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK anak");
            }
        }

        private async Task FillDataAnakFromWargaAsync(WargaData warga)
        {
            if (warga == null) return;

            NIKAnak = warga.NIK ?? string.Empty;
            NamaAnak = warga.Nama ?? string.Empty;
            TempatLahirAnak = warga.TempatLahir ?? string.Empty;
            TanggalLahirAnak = ParseTanggalLahirToUiFormat(warga.TanggalLahir);
            JenisKelaminAnak = warga.JenisKelamin ?? string.Empty;
            AgamaAnak = warga.Agama ?? string.Empty;
            StatusPerkawinanAnak = warga.StatusPerkawinan ?? string.Empty;
            PekerjaanAnak = warga.Pekerjaan ?? string.Empty;
            DusunAnak = warga.Dusun ?? string.Empty;
            DesaAnak = warga.Desa ?? string.Empty;
            KecamatanAnak = warga.Kecamatan ?? string.Empty;
            KabupatenAnak = warga.Kabupaten ?? string.Empty;

            if (string.IsNullOrWhiteSpace(DusunAnak) && string.IsNullOrWhiteSpace(DesaAnak) &&
                string.IsNullOrWhiteSpace(KecamatanAnak) && string.IsNullOrWhiteSpace(KabupatenAnak) &&
                !string.IsNullOrWhiteSpace(warga.AlamatLengkap))
            {
                await ParseFillAlamatAnakAsync(warga.AlamatLengkap);
            }
        }

        private async Task FillDataAnakFromSuratDataAsync(SuratData suratData)
        {
            if (suratData.IzinOrtu == null) return;

            NIKAnak = suratData.IzinOrtu.NIKAnak ?? string.Empty;
            NamaAnak = suratData.IzinOrtu.NamaAnak ?? string.Empty;
            TempatLahirAnak = suratData.IzinOrtu.TempatLahirAnak ?? string.Empty;
            TanggalLahirAnak = ParseTanggalLahirToUiFormat(suratData.IzinOrtu.TanggalLahirAnak);
            JenisKelaminAnak = suratData.IzinOrtu.JenisKelaminAnak ?? string.Empty;
            AgamaAnak = suratData.IzinOrtu.AgamaAnak ?? string.Empty;
            StatusPerkawinanAnak = suratData.IzinOrtu.StatusPerkawinanAnak ?? string.Empty;
            PekerjaanAnak = suratData.IzinOrtu.PekerjaanAnak ?? string.Empty;

            await ParseFillAlamatAnakAsync(suratData.IzinOrtu.AlamatAnak);
        }

        private async Task ParseFillAlamatAnakAsync(string? alamatLengkap)
        {
            DusunAnak = string.Empty;
            var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync() ?? new DesaData();

            if (string.IsNullOrWhiteSpace(alamatLengkap))
            {
                if (string.IsNullOrWhiteSpace(DesaAnak)) DesaAnak = desaData.NamaDesa ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KecamatanAnak)) KecamatanAnak = desaData.Kecamatan ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KabupatenAnak)) KabupatenAnak = desaData.Kabupaten ?? string.Empty;
                return;
            }

            int desaIndex = alamatLengkap.IndexOf("Desa ", StringComparison.OrdinalIgnoreCase);
            int kecIndex = alamatLengkap.IndexOf("Kec.", StringComparison.OrdinalIgnoreCase);
            int kabIndex = alamatLengkap.IndexOf("Kab.", StringComparison.OrdinalIgnoreCase);

            DusunAnak = desaIndex >= 0 ? alamatLengkap.Substring(0, desaIndex).Trim() : alamatLengkap;
            if (desaIndex == -1)
            {
                if (string.IsNullOrWhiteSpace(DesaAnak)) DesaAnak = desaData.NamaDesa ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KecamatanAnak)) KecamatanAnak = desaData.Kecamatan ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KabupatenAnak)) KabupatenAnak = desaData.Kabupaten ?? string.Empty;
                return;
            }

            if (kecIndex > desaIndex && kecIndex <= alamatLengkap.Length)
            {
                DesaAnak = alamatLengkap.Substring(desaIndex + 5, kecIndex - (desaIndex + 5)).Trim();
            }
            else if (string.IsNullOrWhiteSpace(DesaAnak))
            {
                DesaAnak = desaData.NamaDesa ?? string.Empty;
            }

            if (kecIndex >= 0 && kabIndex > kecIndex && kabIndex <= alamatLengkap.Length)
            {
                KecamatanAnak = alamatLengkap.Substring(kecIndex + 4, kabIndex - (kecIndex + 4)).Trim();
            }
            else if (string.IsNullOrWhiteSpace(KecamatanAnak))
            {
                KecamatanAnak = desaData.Kecamatan ?? string.Empty;
            }

            if (kabIndex != -1 && kabIndex + 4 <= alamatLengkap.Length)
            {
                KabupatenAnak = alamatLengkap.Substring(kabIndex + 4).Trim();
            }
            else if (string.IsNullOrWhiteSpace(KabupatenAnak))
            {
                KabupatenAnak = desaData.Kabupaten ?? string.Empty;
            }
        }

        private void ClearDataAnak()
        {
            NamaAnak = string.Empty;
            TempatLahirAnak = string.Empty;
            TanggalLahirAnak = string.Empty;
            JenisKelaminAnak = string.Empty;
            AgamaAnak = string.Empty;
            StatusPerkawinanAnak = string.Empty;
            PekerjaanAnak = string.Empty;
            DusunAnak = string.Empty;
            DesaAnak = string.Empty;
            KecamatanAnak = string.Empty;
            KabupatenAnak = string.Empty;
        }
    }
}