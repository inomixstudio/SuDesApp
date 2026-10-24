using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using Validator = SuDesApp.Utilities.Validator;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Beda Nama — setara BedaNama (WinForms).
    /// Dua blok warga (data yang keliru / data yang benar) masing-masing
    /// dengan kolom sumber data ("Data Di: ...").
    /// </summary>
    public class BedaNamaInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Perbaikan Data";

        // ===== Sumber data ("Data di: ...") =====
        private string _data1 = string.Empty;
        private string _data2 = string.Empty;

        // ===== Warga kedua (perbandingan / data koreksi) =====
        private string _nik2 = string.Empty;
        private string _nama2 = string.Empty;
        private string _tempatLahir2 = string.Empty;
        private string _tanggalLahir2 = string.Empty;
        private string _jenisKelamin2 = string.Empty;
        private string _dusun2 = string.Empty;
        private string _desa2 = string.Empty;
        private string _kecamatan2 = string.Empty;
        private string _kabupaten2 = string.Empty;

        public BedaNamaInputViewModel(
            ILogger<BedaNamaInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public string NamaJenis => SuratConstants.BEDANAMA;

        public string Data1 { get => _data1; set => SetProperty(ref _data1, value); }
        public string Data2 { get => _data2; set => SetProperty(ref _data2, value); }

        public string NIK2 { get => _nik2; set => SetProperty(ref _nik2, value); }
        public string Nama2 { get => _nama2; set => SetProperty(ref _nama2, value); }
        public string TempatLahir2 { get => _tempatLahir2; set => SetProperty(ref _tempatLahir2, value); }
        public string TanggalLahir2 { get => _tanggalLahir2; set => SetProperty(ref _tanggalLahir2, value); }
        public string JenisKelamin2 { get => _jenisKelamin2; set => SetProperty(ref _jenisKelamin2, value); }
        public string Dusun2 { get => _dusun2; set => SetProperty(ref _dusun2, value); }
        public string Desa2 { get => _desa2; set => SetProperty(ref _desa2, value); }
        public string Kecamatan2 { get => _kecamatan2; set => SetProperty(ref _kecamatan2, value); }
        public string Kabupaten2 { get => _kabupaten2; set => SetProperty(ref _kabupaten2, value); }

        public IReadOnlyList<string> JenisKelamin2Options => ValidJenisKelaminOptions;

        protected override async Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            // Data sumber ("Data di: ...")
            Data1 = suratData.DataSource1 ?? suratData.BedaNama?.SumberDataKoreksi ?? string.Empty;
            Data2 = suratData.DataSource2 ?? suratData.BedaNama?.SumberDataKeliru ?? string.Empty;

            // Warga kedua: prefer WargaKK, fallback ke BedaNama denormalized
            var wargaKK = suratData.WargaKK;
            if (wargaKK == null && suratData.BedaNama != null)
            {
                wargaKK = new WargaData
                {
                    NIK = suratData.BedaNama.NIK2,
                    Nama = suratData.BedaNama.Nama2,
                    TempatLahir = suratData.BedaNama.TempatLahir2,
                    TanggalLahir = suratData.BedaNama.TanggalLahir2,
                    JenisKelamin = suratData.BedaNama.JenisKelamin2,
                    Dusun = suratData.BedaNama.Dusun2,
                    Desa = suratData.BedaNama.Desa2,
                    Kecamatan = suratData.BedaNama.Kecamatan2,
                    Kabupaten = suratData.BedaNama.Kabupaten2
                };
            }
            FillDataWarga2(wargaKK);

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

            suratData.NamaJenis = SuratConstants.BEDANAMA;
            suratData.Keperluan = DefaultKeperluan;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            suratData.WargaKK = CollectWargaData2();
            suratData.DataSource1 = Data1.Trim();
            suratData.DataSource2 = Data2.Trim();

            // Simpan data BedaNama denormalized (warga kedua + sumber data)
            suratData.BedaNama ??= new BedaNamaData();
            suratData.BedaNama.ID_Warga = suratData.Warga?.ID_Warga ?? 0;
            suratData.BedaNama.Warga = suratData.Warga;
            suratData.BedaNama.SumberDataKoreksi = suratData.DataSource1;
            suratData.BedaNama.SumberDataKeliru = suratData.DataSource2;
            suratData.BedaNama.NIK2 = suratData.WargaKK?.NIK;
            suratData.BedaNama.Nama2 = suratData.WargaKK?.Nama;
            suratData.BedaNama.TempatLahir2 = suratData.WargaKK?.TempatLahir;
            suratData.BedaNama.TanggalLahir2 = suratData.WargaKK?.TanggalLahir;
            suratData.BedaNama.JenisKelamin2 = suratData.WargaKK?.JenisKelamin;
            suratData.BedaNama.Dusun2 = suratData.WargaKK?.Dusun;
            suratData.BedaNama.Desa2 = suratData.WargaKK?.Desa;
            suratData.BedaNama.Kecamatan2 = suratData.WargaKK?.Kecamatan;
            suratData.BedaNama.Kabupaten2 = suratData.WargaKK?.Kabupaten;

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = SuratConstants.BEDANAMA;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            if (suratData.WargaKK != null)
            {
                suratData.WargaKK.NamaJenis = SuratConstants.BEDANAMA;
                suratData.WargaKK.IsForInstansi = false;
                suratData.WargaKK.IsForKematian = false;
            }

            await Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(NIK2) || !Validator.ValidateNik(NIK2.Trim(), out _))
            {
                errors.Add("NIK warga kedua tidak valid");
            }

            if (string.IsNullOrWhiteSpace(Nama2))
            {
                errors.Add("Nama warga kedua harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TempatLahir2))
            {
                errors.Add("Tempat lahir warga kedua harus diisi");
            }

            if (string.IsNullOrWhiteSpace(TanggalLahir2))
            {
                errors.Add("Tanggal lahir warga kedua harus diisi");
            }

            if (string.IsNullOrWhiteSpace(JenisKelamin2) ||
                !ValidJenisKelaminOptions.Contains(JenisKelamin2.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Jenis kelamin warga kedua harus dipilih");
            }

            if (string.IsNullOrWhiteSpace(Data1))
            {
                errors.Add("Sumber data pertama harus diisi");
            }

            if (string.IsNullOrWhiteSpace(Data2))
            {
                errors.Add("Sumber data kedua harus diisi");
            }

            if (!string.IsNullOrWhiteSpace(Data1) &&
                string.Equals(Data1.Trim(), Data2.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Sumber data pertama dan kedua tidak boleh sama");
            }
        }

        public virtual async Task OnNik2LostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = NIK2?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearDataWarga2();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearDataWarga2();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    FillDataWarga2(warga);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK warga kedua: {Nik}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK warga kedua");
            }
        }

        private void FillDataWarga2(WargaData? warga)
        {
            if (warga == null)
            {
                ClearDataWarga2();
                return;
            }

            Nama2 = warga.Nama ?? string.Empty;
            TempatLahir2 = warga.TempatLahir ?? string.Empty;
            TanggalLahir2 = ParseTanggalLahirToUiFormat(warga.TanggalLahir);
            JenisKelamin2 = warga.JenisKelamin ?? string.Empty;
            Dusun2 = warga.Dusun ?? string.Empty;
            Desa2 = warga.Desa ?? string.Empty;
            Kecamatan2 = warga.Kecamatan ?? string.Empty;
            Kabupaten2 = warga.Kabupaten ?? string.Empty;
        }

        private void ClearDataWarga2()
        {
            Nama2 = string.Empty;
            TempatLahir2 = string.Empty;
            TanggalLahir2 = string.Empty;
            JenisKelamin2 = string.Empty;
            Dusun2 = string.Empty;
            Desa2 = string.Empty;
            Kecamatan2 = string.Empty;
            Kabupaten2 = string.Empty;
        }

        private WargaData CollectWargaData2()
        {
            return new WargaData
            {
                NIK = NIK2.Trim(),
                Nama = Nama2.Trim(),
                TempatLahir = TempatLahir2.Trim(),
                TanggalLahir = ParseTanggalLahirToDbFormat(TanggalLahir2),
                JenisKelamin = JenisKelamin2.Trim(),
                Dusun = Dusun2.Trim(),
                Desa = Desa2.Trim(),
                Kecamatan = Kecamatan2.Trim(),
                Kabupaten = Kabupaten2.Trim(),
                Agama = "Islam", // Default untuk BedaNama
                StatusPerkawinan = "Kawin" // Default untuk BedaNama
            };
        }
    }
}