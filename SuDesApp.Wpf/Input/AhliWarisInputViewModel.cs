using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Ahli Waris (AHLI_WARIS) — setara AhliWarisInput (WinForms).
    /// Data almarhum/ah mengisi Warga (NIK dummy + IsForKematian), daftar anak diubah inline
    /// (menggantikan dialog AhliWarisAnak), dan Waris dibangun dari pasangan + anak + penerima kuasa.
    /// </summary>
    public class AhliWarisInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Pengurusan Klaim Asuransi";

        // Kematian almarhum
        private string _tanggalKematian = string.Empty;
        private string _hariKematian = string.Empty;

        // Pasangan
        private string _namaPasangan = string.Empty;
        private string _tempatLahirPasangan = string.Empty;
        private string _tanggalLahirPasangan = string.Empty;
        private string _jenisKelaminPasangan = string.Empty;
        private string _dusunPasangan = string.Empty;
        private string _desaPasangan = string.Empty;
        private string _kecamatanPasangan = string.Empty;
        private string _kabupatenPasangan = string.Empty;
        private bool _alamatPasanganSamaAlmarhum;

        // Penerima kuasa (otomatis anak pertama bila tidak diisi)
        private string _namaPenerimaKuasa = string.Empty;
        private string _tempatLahirPenerimaKuasa = string.Empty;
        private string _tanggalLahirPenerimaKuasa = string.Empty;
        private string _jenisKelaminPenerimaKuasa = string.Empty;
        private string _alamatPenerimaKuasa = string.Empty;

        public AhliWarisInputViewModel(
            ILogger<AhliWarisInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
            AddAnakCommand = new RelayCommand(AddAnak);
            RemoveAnakCommand = new RelayCommand<AnakWarisItem>(RemoveAnak);
        }

        public string NamaJenis => SuratConstants.AHLI_WARIS;

        public ObservableCollection<AnakWarisItem> AnakItems { get; } = new();

        private AnakWarisItem? _selectedAnak;

        /// <summary>Baris anak yang dipilih di grid — dipakai tombol Hapus toolbar.</summary>
        public AnakWarisItem? SelectedAnak
        {
            get => _selectedAnak;
            set => SetProperty(ref _selectedAnak, value);
        }

        public RelayCommand AddAnakCommand { get; }
        public RelayCommand<AnakWarisItem> RemoveAnakCommand { get; }

        public string TanggalKematian { get => _tanggalKematian; set => SetProperty(ref _tanggalKematian, value); }
        public string HariKematian { get => _hariKematian; set => SetProperty(ref _hariKematian, value); }

        public string NamaPasangan { get => _namaPasangan; set => SetProperty(ref _namaPasangan, value); }
        public string TempatLahirPasangan { get => _tempatLahirPasangan; set => SetProperty(ref _tempatLahirPasangan, value); }
        public string TanggalLahirPasangan { get => _tanggalLahirPasangan; set => SetProperty(ref _tanggalLahirPasangan, value); }
        public string JenisKelaminPasangan { get => _jenisKelaminPasangan; set => SetProperty(ref _jenisKelaminPasangan, value); }
        public string DusunPasangan { get => _dusunPasangan; set => SetProperty(ref _dusunPasangan, value); }
        public string DesaPasangan { get => _desaPasangan; set => SetProperty(ref _desaPasangan, value); }
        public string KecamatanPasangan { get => _kecamatanPasangan; set => SetProperty(ref _kecamatanPasangan, value); }
        public string KabupatenPasangan { get => _kabupatenPasangan; set => SetProperty(ref _kabupatenPasangan, value); }

        public bool AlamatPasanganSamaAlmarhum
        {
            get => _alamatPasanganSamaAlmarhum;
            set
            {
                if (SetProperty(ref _alamatPasanganSamaAlmarhum, value))
                {
                    if (value)
                    {
                        DusunPasangan = Dusun;
                        DesaPasangan = Desa;
                        KecamatanPasangan = Kecamatan;
                        KabupatenPasangan = Kabupaten;
                    }
                    OnPropertyChanged(nameof(IsAlamatPasanganEditable));
                }
            }
        }

        public bool IsAlamatPasanganEditable => !AlamatPasanganSamaAlmarhum;

        protected override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            if (AlamatPasanganSamaAlmarhum &&
                propertyName is nameof(Dusun) or nameof(Desa) or nameof(Kecamatan) or nameof(Kabupaten))
            {
                switch (propertyName)
                {
                    case nameof(Dusun): DusunPasangan = Dusun; break;
                    case nameof(Desa): DesaPasangan = Desa; break;
                    case nameof(Kecamatan): KecamatanPasangan = Kecamatan; break;
                    case nameof(Kabupaten): KabupatenPasangan = Kabupaten; break;
                }
            }
        }

        public string NamaPenerimaKuasa { get => _namaPenerimaKuasa; set => SetProperty(ref _namaPenerimaKuasa, value); }
        public string TempatLahirPenerimaKuasa { get => _tempatLahirPenerimaKuasa; set => SetProperty(ref _tempatLahirPenerimaKuasa, value); }
        public string TanggalLahirPenerimaKuasa { get => _tanggalLahirPenerimaKuasa; set => SetProperty(ref _tanggalLahirPenerimaKuasa, value); }
        public string JenisKelaminPenerimaKuasa { get => _jenisKelaminPenerimaKuasa; set => SetProperty(ref _jenisKelaminPenerimaKuasa, value); }
        public string AlamatPenerimaKuasa { get => _alamatPenerimaKuasa; set => SetProperty(ref _alamatPenerimaKuasa, value); }

        private void AddAnak()
        {
            var baru = new AnakWarisItem();
            AnakItems.Add(baru);
            // Pilih otomatis: baris baru langsung terlihat & siap diedit di grid.
            SelectedAnak = baru;
        }

        private void RemoveAnak(AnakWarisItem? item)
        {
            if (item != null) AnakItems.Remove(item);
        }

        // Almarhum selalu memakai NIK dummy (paritas WinForms: tidak ada field NIK di form).
        public override string GetNIK() => SuratConstants.NIK_KEMATIAN;

        protected override Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                Keterangan = DefaultKeperluan;
            }
            return Task.CompletedTask;
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            suratData.Kematian ??= new KematianData();
            suratData.AhliWarisData ??= new AhliWarisData();
            suratData.AhliWaris ??= new AhliWaris();

            // Keperluan surat (WinForms meletakkan nilai di txtKeterangan)
            Keterangan = suratData.Keperluan ?? DefaultKeperluan;

            // Kematian
            HariKematian = suratData.Kematian.HariKematian ?? string.Empty;
            TanggalKematian = string.IsNullOrWhiteSpace(suratData.Kematian.TanggalKematian)
                ? string.Empty
                : ParseTanggalLahirToUiFormat(suratData.Kematian.TanggalKematian);

            // Pasangan
            NamaPasangan = suratData.AhliWarisData.NamaPasangan ?? string.Empty;
            TempatLahirPasangan = suratData.AhliWarisData.TempatLahirPasangan ?? string.Empty;
            TanggalLahirPasangan = suratData.AhliWarisData.TanggalLahirPasangan?.ToString(DateFormatUi) ?? string.Empty;
            JenisKelaminPasangan = suratData.AhliWarisData.JenisKelaminPasangan ?? string.Empty;

            // Pasangan alamat — 4 field terpisah (paritas WinForms: txtDusun/Desa/Kec/KabPasangan)
            var alamatPasanganStored = suratData.AhliWarisData.AlamatPasangan ?? string.Empty;
            if (!string.IsNullOrEmpty(alamatPasanganStored))
            {
                var parts = alamatPasanganStored.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    DusunPasangan = parts[0];
                    DesaPasangan = parts[1];
                    KecamatanPasangan = parts[2];
                    KabupatenPasangan = parts[3];
                }
            }

            // Anak (NIK ditarik dari Waris sesuai Nama + Hubungan = "Anak", seperti WinForms)
            AnakItems.Clear();
            if (suratData.AhliWarisData.Anak != null)
            {
                foreach (var anak in suratData.AhliWarisData.Anak)
                {
                    AnakItems.Add(new AnakWarisItem
                    {
                        Nama = anak.Nama ?? string.Empty,
                        TempatLahir = anak.TempatLahir ?? string.Empty,
                        TanggalLahir = anak.TanggalLahir?.ToString(DateFormatUi) ?? string.Empty,
                        JenisKelamin = anak.JenisKelamin ?? string.Empty,
                        Alamat = anak.Alamat ?? string.Empty,
                        NIK = suratData.AhliWaris.Waris
                            ?.FirstOrDefault(w => w.NamaWaris == anak.Nama && w.HubunganWaris == "Anak")?.NIKWaris
                            ?? SuratConstants.NIK_KEMATIAN
                    });
                }
            }

            // Penerima kuasa
            NamaPenerimaKuasa = suratData.AhliWarisData.NamaPenerimaKuasa ?? string.Empty;
            TempatLahirPenerimaKuasa = suratData.AhliWarisData.TempatLahirPenerimaKuasa ?? string.Empty;
            TanggalLahirPenerimaKuasa = suratData.AhliWarisData.TanggalLahirPenerimaKuasa?.ToString(DateFormatUi) ?? string.Empty;
            JenisKelaminPenerimaKuasa = suratData.AhliWarisData.JenisKelaminPenerimaKuasa ?? string.Empty;
            AlamatPenerimaKuasa = suratData.AhliWarisData.AlamatPenerimaKuasa ?? string.Empty;

            // Bila penerima kuasa belum tersimpan, tampilkan turunan dari anak pertama (paritas auto-derive WinForms).
            if (string.IsNullOrWhiteSpace(NamaPenerimaKuasa) && AnakItems.Count > 0)
            {
                FillPenerimaKuasaFromAnak(AnakItems[0]);
            }

            return Task.CompletedTask;
        }

        protected override WargaData CollectBasicWargaData() => new WargaData
        {
            NIK = SuratConstants.NIK_KEMATIAN,
            Nama = GetNama(),
            JenisKelamin = GetJenisKelamin(),
            Dusun = Dusun.Trim(),
            Desa = Desa.Trim(),
            Kecamatan = Kecamatan.Trim(),
            Kabupaten = Kabupaten.Trim(),
            TempatLahir = "Tidak Diketahui",
            TanggalLahir = "01-01-1900",
            IsForKematian = true,
            NamaJenis = SuratConstants.AHLI_WARIS
        };

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.Kematian ??= new KematianData();
            suratData.AhliWarisData ??= new AhliWarisData();
            suratData.AhliWaris ??= new AhliWaris();
            suratData.AhliWaris.Waris ??= new List<Waris>();

            // Keperluan (WinForms memakai txtKeterangan sebagai Keperluan; Keterangan dibuat otomatis)
            suratData.Keperluan = string.IsNullOrWhiteSpace(Keterangan) ? DefaultKeperluan : Keterangan.Trim();
            suratData.Keterangan = await GetDefaultKeteranganAsync();

            // Kematian
            suratData.Kematian.HariKematian = ComputeHariKematian();
            suratData.Kematian.TanggalKematian =
                DateTime.TryParseExact(TanggalKematian.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tglKematian)
                    ? tglKematian.ToString(DateFormatDb)
                    : string.Empty;
            suratData.Kematian.PenyebabKematian = "Tidak diketahui";
            suratData.Kematian.NamaPelapor = "Tidak diketahui";
            suratData.Kematian.AlamatPelapor = "Tidak diketahui";
            suratData.Kematian.HubunganPelapor = "Tidak diketahui";

            // Pasangan
            if (!string.IsNullOrWhiteSpace(NamaPasangan))
            {
                suratData.AhliWarisData.NamaPasangan = NamaPasangan.Trim();
                suratData.AhliWarisData.TempatLahirPasangan = TempatLahirPasangan.Trim();
                suratData.AhliWarisData.TanggalLahirPasangan = ParseTanggalNullable(TanggalLahirPasangan);
                suratData.AhliWarisData.JenisKelaminPasangan = JenisKelaminPasangan.Trim();
                suratData.AhliWarisData.AlamatPasangan = BuildAlamatPasangan();

                suratData.AhliWaris.Waris.Add(new Waris
                {
                    NamaWaris = suratData.AhliWarisData.NamaPasangan,
                    NIKWaris = SuratConstants.NIK_KEMATIAN,
                    HubunganWaris = suratData.AhliWarisData.JenisKelaminPasangan == "Perempuan" ? "Istri" : "Suami"
                });
            }

            // Anak — baris yang sama sekali kosong (tanpa nama) dilewati agar
            // sisa baris tak terpakai tidak menjadi sampah data.
            suratData.AhliWarisData.Anak = new List<AnakAhliWarisData>();
            foreach (var anak in AnakItems.Where(a => !string.IsNullOrWhiteSpace(a.Nama)))
            {
                var anakData = new AnakAhliWarisData
                {
                    Nama = anak.Nama.Trim(),
                    TempatLahir = anak.TempatLahir.Trim(),
                    TanggalLahir = ParseTanggalNullable(anak.TanggalLahir),
                    JenisKelamin = anak.JenisKelamin.Trim(),
                    Alamat = anak.Alamat.Trim(),
                    NIK = string.IsNullOrWhiteSpace(anak.NIK) ? SuratConstants.NIK_KEMATIAN : anak.NIK.Trim()
                };
                suratData.AhliWarisData.Anak.Add(anakData);

                suratData.AhliWaris.Waris.Add(new Waris
                {
                    NamaWaris = anakData.Nama,
                    NIKWaris = anakData.NIK,
                    HubunganWaris = "Anak"
                });
            }

            // Penerima kuasa: otomatis anak pertama yang terisi (baris kosong dilewati)
            var anakPertamaTerisi = AnakItems.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Nama));
            if (anakPertamaTerisi != null)
            {
                if (string.IsNullOrWhiteSpace(NamaPenerimaKuasa))
                {
                    FillPenerimaKuasaFromAnak(anakPertamaTerisi);
                }

                string nikPenerima = anakPertamaTerisi.NIK;
                if (string.IsNullOrWhiteSpace(nikPenerima)) nikPenerima = SuratConstants.NIK_KEMATIAN;

                suratData.AhliWarisData.NamaPenerimaKuasa = NamaPenerimaKuasa.Trim();
                suratData.AhliWarisData.TempatLahirPenerimaKuasa = TempatLahirPenerimaKuasa.Trim();
                suratData.AhliWarisData.TanggalLahirPenerimaKuasa = ParseTanggalNullable(TanggalLahirPenerimaKuasa);
                suratData.AhliWarisData.JenisKelaminPenerimaKuasa = JenisKelaminPenerimaKuasa.Trim();
                suratData.AhliWarisData.AlamatPenerimaKuasa = AlamatPenerimaKuasa.Trim();

                suratData.AhliWaris.Waris.Add(new Waris
                {
                    NamaWaris = suratData.AhliWarisData.NamaPenerimaKuasa,
                    NIKWaris = nikPenerima,
                    HubunganWaris = "Lainnya"
                });
            }

            suratData.NamaJenis = SuratConstants.AHLI_WARIS;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";
        }

        public override bool ValidateInput(out DateTime tglLahir)
        {
            tglLahir = DateTime.MinValue;
            var errors = new List<string>();

            // Almarhum — form Ahli Waris tidak memiliki NIK/Tempat/Tanggal lahir.
            if (string.IsNullOrWhiteSpace(Nama))
                errors.Add("Nama almarhum tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(JenisKelamin))
                errors.Add("Jenis Kelamin almarhum harus dipilih");

            AddAlamatErrors(errors);

            // Kematian
            if (string.IsNullOrWhiteSpace(TanggalKematian))
                errors.Add("Tanggal kematian harus diisi");
            else if (!DateTime.TryParseExact(TanggalKematian.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors.Add("Format tanggal kematian tidak valid (DD-MM-YYYY)");

            // Pasangan
            bool hasPasangan = !string.IsNullOrWhiteSpace(NamaPasangan);
            if (hasPasangan)
            {
                if (string.IsNullOrWhiteSpace(TempatLahirPasangan))
                    errors.Add("Tempat lahir pasangan harus diisi");

                if (string.IsNullOrWhiteSpace(TanggalLahirPasangan))
                    errors.Add("Tanggal lahir pasangan harus diisi");
                else if (!DateTime.TryParseExact(TanggalLahirPasangan.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    errors.Add("Format tanggal lahir pasangan tidak valid (DD-MM-YYYY)");

                if (string.IsNullOrWhiteSpace(JenisKelaminPasangan))
                    errors.Add("Jenis kelamin pasangan harus dipilih");
            }

            // Minimal satu ahli waris
            if (!hasPasangan && AnakItems.Count == 0)
                errors.Add("Harus ada minimal satu ahli waris (pasangan atau anak)");

            if (errors.Count == 0) return true;

            _ = _messageService.ShowErrorAsync(string.Join(Environment.NewLine, errors));
            return false;
        }

        private void AddAlamatErrors(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Dusun)) errors.Add("Dusun/Jalan tidak boleh kosong");
            if (string.IsNullOrWhiteSpace(Desa)) errors.Add("Desa tidak boleh kosong");
            if (string.IsNullOrWhiteSpace(Kecamatan)) errors.Add("Kecamatan tidak boleh kosong");
            if (string.IsNullOrWhiteSpace(Kabupaten)) errors.Add("Kabupaten tidak boleh kosong");
        }

        private void FillPenerimaKuasaFromAnak(AnakWarisItem anak)
        {
            NamaPenerimaKuasa = anak.Nama;
            TempatLahirPenerimaKuasa = anak.TempatLahir;
            TanggalLahirPenerimaKuasa = anak.TanggalLahir;
            JenisKelaminPenerimaKuasa = anak.JenisKelamin;
            AlamatPenerimaKuasa = anak.Alamat;
        }

        private string BuildAlamatPasangan()
        {
            return $"{DusunPasangan?.Trim() ?? ""}, {DesaPasangan?.Trim() ?? ""}, " +
                   $"{KecamatanPasangan?.Trim() ?? ""}, {KabupatenPasangan?.Trim() ?? ""}";
        }

        private string ComputeHariKematian()
        {
            if (DateTime.TryParseExact(TanggalKematian.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tanggal))
            {
                return new CultureInfo("id-ID").DateTimeFormat.GetDayName(tanggal.DayOfWeek);
            }
            return HariKematian.Trim();
        }

        private static DateTime? ParseTanggalNullable(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (DateTime.TryParseExact(text.Trim(), DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tgl))
                return tgl;
            if (DateTime.TryParse(text.Trim(), CultureInfo.GetCultureInfo("id-ID"), DateTimeStyles.None, out var tgl2))
                return tgl2;
            return null;
        }

        private async Task<string> GetDefaultKeteranganAsync()
        {
            if (!string.IsNullOrWhiteSpace(Keterangan)) return Keterangan.Trim();

            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                var namaDesa = desaData?.NamaDesa ?? (string.IsNullOrWhiteSpace(Desa) ? "..." : Desa.Trim());
                return $"Menyatakan bahwa nama tersebut di atas telah meninggal dunia dan memiliki ahli waris seperti " +
                       $"tercantum di dalam Surat Keterangan Ahli Waris Desa {namaDesa}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat keterangan default ahli waris");
                return Keterangan.Trim();
            }
        }
    }

    /// <summary>Baris data anak ahli waris yang dapat diedit inline — setara dialog AhliWarisAnak (WinForms).</summary>
    public class AnakWarisItem : ObservableObject
    {
        private string _nik = string.Empty;
        private string _nama = string.Empty;
        private string _tempatLahir = string.Empty;
        private string _tanggalLahir = string.Empty;
        private string _jenisKelamin = string.Empty;
        private string _alamat = string.Empty;

        public string NIK { get => _nik; set => SetProperty(ref _nik, value); }
        public string Nama { get => _nama; set => SetProperty(ref _nama, value); }
        public string TempatLahir { get => _tempatLahir; set => SetProperty(ref _tempatLahir, value); }
        public string TanggalLahir { get => _tanggalLahir; set => SetProperty(ref _tanggalLahir, value); }
        public string JenisKelamin { get => _jenisKelamin; set => SetProperty(ref _jenisKelamin, value); }
        public string Alamat { get => _alamat; set => SetProperty(ref _alamat, value); }
    }
}