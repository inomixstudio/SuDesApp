using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using Validator = SuDesApp.Utilities.Validator;

namespace SuDesApp.Wpf.Input
{
    public class KenalLahirInputViewModel : BaseSuratInputViewModel
    {
        private const string NamaJenisKenalLahir = "KENAL_LAHIR";
        private const string KeperluanSurat = "Persyaratan";

        // ===== Data Ibu (data Ayah memakai field dasar base) =====
        private string _ibuNik = string.Empty;
        private string _ibuNama = string.Empty;
        private string _ibuTempatLahir = string.Empty;
        private string _ibuTanggalLahir = string.Empty;
        private string _ibuAgama = string.Empty;
        private string _ibuPekerjaan = string.Empty;
        private string _ibuDusun = string.Empty;
        private string _ibuDesa = string.Empty;
        private string _ibuKecamatan = string.Empty;
        private string _ibuKabupaten = string.Empty;

        // ===== Data Anak =====
        private string _namaAnak = string.Empty;
        private string _tempatLahirAnak = string.Empty;
        private string _tanggalLahirAnak = string.Empty;
        private int _anakKe = 1;
        private string _lahirDi = string.Empty;
        private string _dusunAnak = string.Empty;
        private string _desaAnak = string.Empty;
        private string _kecamatanAnak = string.Empty;
        private string _kabupatenAnak = string.Empty;

        // ===== Salin alamat =====
        private bool _alamatSamaAyah;
        private bool _anakAlamatSamaAyah;
        private bool _anakAlamatSamaIbu;

        public KenalLahirInputViewModel(
            ILogger<KenalLahirInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => NamaJenisKenalLahir;

        public string IbuNIK { get => _ibuNik; set => SetProperty(ref _ibuNik, value); }
        public string IbuNama { get => _ibuNama; set => SetProperty(ref _ibuNama, value); }
        public string IbuTempatLahir { get => _ibuTempatLahir; set => SetProperty(ref _ibuTempatLahir, value); }
        public string IbuTanggalLahir { get => _ibuTanggalLahir; set => SetProperty(ref _ibuTanggalLahir, value); }
        public string IbuAgama { get => _ibuAgama; set => SetProperty(ref _ibuAgama, value); }
        public string IbuPekerjaan { get => _ibuPekerjaan; set => SetProperty(ref _ibuPekerjaan, value); }
        public string IbuDusun { get => _ibuDusun; set => SetProperty(ref _ibuDusun, value); }
        public string IbuDesa { get => _ibuDesa; set => SetProperty(ref _ibuDesa, value); }
        public string IbuKecamatan { get => _ibuKecamatan; set => SetProperty(ref _ibuKecamatan, value); }
        public string IbuKabupaten { get => _ibuKabupaten; set => SetProperty(ref _ibuKabupaten, value); }

        public string NamaAnak { get => _namaAnak; set => SetProperty(ref _namaAnak, value); }
        public string TempatLahirAnak { get => _tempatLahirAnak; set => SetProperty(ref _tempatLahirAnak, value); }
        public string TanggalLahirAnak { get => _tanggalLahirAnak; set => SetProperty(ref _tanggalLahirAnak, value); }
        public int AnakKe { get => _anakKe; set => SetProperty(ref _anakKe, value); }
        public string LahirDi { get => _lahirDi; set => SetProperty(ref _lahirDi, value); }
        public string DusunAnak { get => _dusunAnak; set => SetProperty(ref _dusunAnak, value); }
        public string DesaAnak { get => _desaAnak; set => SetProperty(ref _desaAnak, value); }
        public string KecamatanAnak { get => _kecamatanAnak; set => SetProperty(ref _kecamatanAnak, value); }
        public string KabupatenAnak { get => _kabupatenAnak; set => SetProperty(ref _kabupatenAnak, value); }

        public bool IbuAlamatSamaAyah
        {
            get => _alamatSamaAyah;
            set
            {
                if (SetProperty(ref _alamatSamaAyah, value) && value)
                {
                    CopyAddress(
                        () => IbuDusun = Dusun, () => IbuDesa = Desa,
                        () => IbuKecamatan = Kecamatan, () => IbuKabupaten = Kabupaten);
                }
            }
        }

        public bool AnakAlamatSamaIbu
        {
            get => _anakAlamatSamaIbu;
            set
            {
                if (SetProperty(ref _anakAlamatSamaIbu, value))
                {
                    if (value)
                    {
                        AnakAlamatSamaAyah = false;
                        CopyAddress(
                            () => DusunAnak = IbuDusun, () => DesaAnak = IbuDesa,
                            () => KecamatanAnak = IbuKecamatan, () => KabupatenAnak = IbuKabupaten);
                    }
                }
            }
        }

        public bool AnakAlamatSamaAyah
        {
            get => _anakAlamatSamaAyah;
            set
            {
                if (SetProperty(ref _anakAlamatSamaAyah, value))
                {
                    if (value)
                    {
                        AnakAlamatSamaIbu = false;
                        CopyAddress(
                            () => DusunAnak = Dusun, () => DesaAnak = Desa,
                            () => KecamatanAnak = Kecamatan, () => KabupatenAnak = Kabupaten);
                    }
                }
            }
        }

        protected override async Task InitializeSpecificAsync()
        {
            JenisKelamin = "Laki-laki";
            IsKadesSelected = true;
            AnakKe = 1;

            await FillDefaultAlamatAsync();
        }

        public async Task OnAyahNikLostFocusAsync(CancellationToken cancellationToken = default)
        {
            await OnNikLostFocusAsync(cancellationToken);
        }

        public async Task OnIbuNikLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = IbuNIK?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearIbuFields();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    IbuNama = warga.Nama ?? string.Empty;
                    IbuTempatLahir = warga.TempatLahir ?? string.Empty;
                    IbuTanggalLahir = ParseTanggalLahirToUiFormat(warga.TanggalLahir);
                    IbuAgama = IsValidOption(warga.Agama, ValidAgamaOptions) ? warga.Agama : string.Empty!;
                    IbuPekerjaan = warga.Pekerjaan ?? string.Empty;
                    ParseAndFillAlamat(warga.AlamatLengkap,
                        (d, de, k, ka) => { IbuDusun = d; IbuDesa = de; IbuKecamatan = k; IbuKabupaten = ka; });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK Ibu: {Nik}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK Ibu");
            }
        }

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData?.KenalLahir == null)
            {
                ClearIbuFields();
                ClearAnakFields();
                await FillDefaultAlamatAsync();
                return;
            }

            var kl = suratData.KenalLahir;

            if (kl.Ibu != null)
            {
                IbuNIK = kl.Ibu.NIK ?? string.Empty;
                IbuNama = kl.Ibu.Nama ?? string.Empty;
                IbuTempatLahir = kl.Ibu.TempatLahir ?? string.Empty;
                IbuTanggalLahir = ParseTanggalLahirToUiFormat(kl.Ibu.TanggalLahir);
                IbuAgama = IsValidOption(kl.Ibu.Agama, ValidAgamaOptions) ? kl.Ibu.Agama : string.Empty!;
                IbuPekerjaan = kl.Ibu.Pekerjaan ?? string.Empty;
                ParseAndFillAlamat(kl.Ibu.AlamatLengkap,
                    (d, de, k, ka) => { IbuDusun = d; IbuDesa = de; IbuKecamatan = k; IbuKabupaten = ka; });
            }

            NamaAnak = kl.NamaAnak ?? string.Empty;
            TempatLahirAnak = kl.TempatLahirAnak ?? string.Empty;
            TanggalLahirAnak = kl.TanggalLahirAnak?.ToString(DateFormatUi) ?? string.Empty;
            AnakKe = kl.AnakKe < 1 ? 1 : kl.AnakKe;
            LahirDi = kl.LahirDi ?? string.Empty;
            ParseAndFillAlamat(kl.AlamatLengkapAnak,
                (d, de, k, ka) => { DusunAnak = d; DesaAnak = de; KecamatanAnak = k; KabupatenAnak = ka; });

            Keterangan = suratData.Keterangan ?? string.Empty;

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                IsSekdesSelected = true;
            else
                IsKadesSelected = true;

            await Task.CompletedTask;
        }

        protected override Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            suratData.KenalLahir ??= new KenalLahirData();
            suratData.NamaJenis = NamaJenisKenalLahir;
            suratData.Keperluan = KeperluanSurat;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            var kl = suratData.KenalLahir;
            kl.Ayah = suratData.Warga!;
            if (kl.Ayah != null)
            {
                kl.Ayah.AlamatLengkap = GetAlamatAyah();
            }

            kl.Ibu = new WargaData
            {
                NIK = IbuNIK.Trim(),
                Nama = IbuNama.Trim(),
                TempatLahir = IbuTempatLahir.Trim(),
                TanggalLahir = ParseTanggalLahirToDbFormat(IbuTanggalLahir),
                Agama = IsValidOption(IbuAgama, ValidAgamaOptions) ? IbuAgama.Trim() : string.Empty,
                Pekerjaan = IbuPekerjaan.Trim(),
                Dusun = IbuDusun.Trim(),
                Desa = IbuDesa.Trim(),
                Kecamatan = IbuKecamatan.Trim(),
                Kabupaten = IbuKabupaten.Trim(),
                AlamatLengkap = GetAlamatIbu(),
                JenisKelamin = "Perempuan",
                StatusPerkawinan = "Kawin",
                Kewarganegaraan = "WNI"
            };

            kl.NamaAnak = NamaAnak.Trim();
            kl.TempatLahirAnak = TempatLahirAnak.Trim();
            kl.LahirDi = LahirDi.Trim();
            kl.AlamatLengkapAnak = GetAlamatAnak();
            kl.AnakKe = AnakKe < 1 ? 1 : AnakKe;

            if (DateTime.TryParseExact(TanggalLahirAnak.Trim(), DateFormatUi,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var tglLahirAnak))
            {
                kl.TanggalLahirAnak = tglLahirAnak;
            }

            return Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            // Ayah (data dasar base) — base menangani NIK/Nama/Tempat/Tgl/JenisKelamin/Alamat.
            if (string.IsNullOrWhiteSpace(Agama) ||
                !ValidAgamaOptions.Contains(Agama.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Ayah: Agama harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(Pekerjaan))
            {
                errors.Add("Ayah: Pekerjaan harus diisi");
            }

            ValidatePersonFields("Ibu", IbuNIK, IbuNama, IbuTempatLahir, IbuTanggalLahir,
                IbuAgama, IbuPekerjaan, IbuDusun, IbuDesa, IbuKecamatan, IbuKabupaten, errors);

            if (string.IsNullOrWhiteSpace(NamaAnak))
            {
                errors.Add("Nama Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TempatLahirAnak))
            {
                errors.Add("Tempat Lahir Anak harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TanggalLahirAnak))
            {
                errors.Add("Tanggal Lahir Anak harus diisi");
            }
            else if (!Validator.ValidateTanggalLahir(TanggalLahirAnak.Trim(), "Tanggal Lahir Anak", out var tglAnakError))
            {
                errors.Add(tglAnakError);
            }
            else if (DateTime.TryParseExact(TanggalLahirAnak.Trim(), DateFormatUi,
                         CultureInfo.InvariantCulture, DateTimeStyles.None, out var anakTglLahir))
            {
                if (anakTglLahir > DateTime.Now)
                {
                    errors.Add("Tanggal Lahir Anak tidak boleh di masa depan");
                }
            }

            if (AnakKe < 1)
            {
                errors.Add("'Anak ke' harus lebih dari 0");
            }

            if (string.IsNullOrWhiteSpace(LahirDi))
            {
                errors.Add("Tempat Kelahiran harus diisi");
            }

            ValidateAddressFields("Anak", DusunAnak, DesaAnak, KecamatanAnak, KabupatenAnak, errors);
        }

        private void ValidatePersonFields(
            string prefix,
            string nik, string nama, string tempatLahir, string tanggalLahir,
            string agama, string pekerjaan,
            string dusun, string desa, string kecamatan, string kabupaten, List<string> errors)
        {
            if (!Validator.ValidateNik(nik.Trim(), out var nikError))
            {
                errors.Add($"{prefix}: {nikError}");
            }

            if (string.IsNullOrWhiteSpace(nama))
            {
                errors.Add($"{prefix}: Nama harus diisi");
            }

            if (string.IsNullOrWhiteSpace(tempatLahir))
            {
                errors.Add($"{prefix}: Tempat Lahir harus diisi");
            }

            if (string.IsNullOrWhiteSpace(tanggalLahir))
            {
                errors.Add($"{prefix}: Tanggal Lahir harus diisi");
            }
            else if (!Validator.ValidateTanggalLahir(tanggalLahir.Trim(), $"Tanggal Lahir {prefix}", out var tglError))
            {
                errors.Add($"{prefix}: {tglError}");
            }

            if (string.IsNullOrWhiteSpace(agama) ||
                !ValidAgamaOptions.Contains(agama.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{prefix}: Agama harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(pekerjaan))
            {
                errors.Add($"{prefix}: Pekerjaan harus diisi");
            }

            ValidateAddressFields(prefix, dusun, desa, kecamatan, kabupaten, errors);
        }

        private static void ValidateAddressFields(
            string prefix, string dusun, string desa, string kecamatan, string kabupaten, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(dusun))
            {
                errors.Add($"{prefix}: Dusun harus diisi");
            }

            if (string.IsNullOrWhiteSpace(desa))
            {
                errors.Add($"{prefix}: Desa harus diisi");
            }

            if (string.IsNullOrWhiteSpace(kecamatan))
            {
                errors.Add($"{prefix}: Kecamatan harus diisi");
            }

            if (string.IsNullOrWhiteSpace(kabupaten))
            {
                errors.Add($"{prefix}: Kabupaten harus diisi");
            }
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            ClearIbuFields();
            ClearAnakFields();
        }

        private void ClearIbuFields()
        {
            IbuNIK = string.Empty;
            IbuNama = string.Empty;
            IbuTempatLahir = string.Empty;
            IbuTanggalLahir = string.Empty;
            IbuAgama = string.Empty;
            IbuPekerjaan = string.Empty;
            IbuDusun = string.Empty;
            IbuDesa = string.Empty;
            IbuKecamatan = string.Empty;
            IbuKabupaten = string.Empty;
        }

        private void ClearAnakFields()
        {
            NamaAnak = string.Empty;
            TempatLahirAnak = string.Empty;
            TanggalLahirAnak = string.Empty;
            AnakKe = 1;
            LahirDi = string.Empty;
            DusunAnak = string.Empty;
            DesaAnak = string.Empty;
            KecamatanAnak = string.Empty;
            KabupatenAnak = string.Empty;
        }

        private async Task FillDefaultAlamatAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData == null) return;

                if (string.IsNullOrWhiteSpace(IbuDesa)) IbuDesa = desaData.NamaDesa ?? string.Empty;
                if (string.IsNullOrWhiteSpace(IbuKecamatan)) IbuKecamatan = desaData.Kecamatan ?? string.Empty;
                if (string.IsNullOrWhiteSpace(IbuKabupaten)) IbuKabupaten = desaData.Kabupaten ?? string.Empty;

                if (string.IsNullOrWhiteSpace(DesaAnak)) DesaAnak = desaData.NamaDesa ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KecamatanAnak)) KecamatanAnak = desaData.Kecamatan ?? string.Empty;
                if (string.IsNullOrWhiteSpace(KabupatenAnak)) KabupatenAnak = desaData.Kabupaten ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengisi alamat default Kenal Lahir: {Message}", ex.Message);
            }
        }

        private static void CopyAddress(Action setDusun, Action setDesa, Action setKecamatan, Action setKabupaten)
        {
            setDusun();
            setDesa();
            setKecamatan();
            setKabupaten();
        }

        private string GetAlamatAyah()
        {
            return string.Join(" ", new[] { Dusun, Desa, Kecamatan, Kabupaten }
                .Where(c => !string.IsNullOrWhiteSpace(c))).Trim();
        }

        private string GetAlamatIbu()
        {
            return string.Join(" ", new[] { IbuDusun, IbuDesa, IbuKecamatan, IbuKabupaten }
                .Where(c => !string.IsNullOrWhiteSpace(c))).Trim();
        }

        private string GetAlamatAnak()
        {
            return string.Join(" ", new[] { DusunAnak, DesaAnak, KecamatanAnak, KabupatenAnak }
                .Where(c => !string.IsNullOrWhiteSpace(c))).Trim();
        }

        private static void ParseAndFillAlamat(string? alamatLengkap, Action<string, string, string, string> setter)
        {
            if (string.IsNullOrEmpty(alamatLengkap)) return;

            alamatLengkap = alamatLengkap.Replace(",", "").Trim();
            var words = alamatLengkap.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int wordCount = words.Length;

            if (wordCount < 4)
            {
                setter(alamatLengkap, string.Empty, string.Empty, string.Empty);
                return;
            }

            string kabupaten = words[wordCount - 1];
            string kecamatan = words[wordCount - 2];
            string desa = words[wordCount - 3];
            string dusun = string.Join(" ", words.Take(wordCount - 3)).Trim();
            setter(dusun, desa, kecamatan, kabupaten);
        }

        private static bool IsValidOption(string? value, string[] validOptions)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   validOptions.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
        }
    }
}