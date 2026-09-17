using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input Surat Keterangan Garapan Sawah (GARAPAN_SAWAH) — setara GarapanInput (WinForms).
    /// Memuat satu baris utama (suratData.Garapan) dan daftar rincian garapan inline
    /// (suratData.RincianGarapans) menggantikan dialog FormInputRincianGarapan.
    /// </summary>
    public class GarapanInputViewModel : BaseSuratInputViewModel
    {
        private const string DefaultKeperluan = "Pengajuan Pinjaman";
        private static readonly string[] OpsiKeteranganGarapan = { "Garap", "Sewa", "Hak Milik" };

        private string _lokasi = string.Empty;
        private string _luas = string.Empty;
        private string _nomorPersil = string.Empty;
        private string _keteranganGarapan = OpsiKeteranganGarapan[0];
        private string _keperluan = DefaultKeperluan;

        public GarapanInputViewModel(
            ILogger<GarapanInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
            AddRincianCommand = new RelayCommand(AddRincian);
            RemoveRincianCommand = new RelayCommand<RincianGarapanItem>(RemoveRincian);
            ClearRincianCommand = new RelayCommand(ClearRincian);
        }

        public string NamaJenis => SuratConstants.GARAPAN_SAWAH;

        public IReadOnlyList<string> KeteranganGarapanOptions => OpsiKeteranganGarapan;
        public ObservableCollection<RincianGarapanItem> RincianItems { get; } = new();

        public RelayCommand AddRincianCommand { get; }
        public RelayCommand<RincianGarapanItem> RemoveRincianCommand { get; }
        public RelayCommand ClearRincianCommand { get; }

        private RincianGarapanItem? _selectedRincian;

        /// <summary>Baris rincian yang sedang dipilih di DataGrid (untuk tombol Hapus & scroll-into-view).</summary>
        public RincianGarapanItem? SelectedRincian
        {
            get => _selectedRincian;
            set => SetProperty(ref _selectedRincian, value);
        }

        // Baris utama garapan (detail tanah garapan -> suratData.Garapan)
        public string Lokasi { get => _lokasi; set => SetProperty(ref _lokasi, value); }
        public string Luas { get => _luas; set => SetProperty(ref _luas, value); }
        public string NomorPersil { get => _nomorPersil; set => SetProperty(ref _nomorPersil, value); }
        public string KeteranganGarapan { get => _keteranganGarapan; set => SetProperty(ref _keteranganGarapan, value); }
        public string Keperluan { get => _keperluan; set => SetProperty(ref _keperluan, value); }

        private void AddRincian()
        {
            var item = new RincianGarapanItem { KeteranganGarapan = OpsiKeteranganGarapan[0] };
            RincianItems.Add(item);
            // Pilih baris baru agar DataGrid otomatis meng-scroll ke sana dan langsung terlihat.
            SelectedRincian = item;
        }

        private void RemoveRincian(RincianGarapanItem? item)
        {
            if (item == null) return;
            RincianItems.Remove(item);
            if (ReferenceEquals(SelectedRincian, item)) SelectedRincian = null;
        }

        private void ClearRincian()
        {
            RincianItems.Clear();
            SelectedRincian = null;
        }

        protected override Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keperluan))
            {
                Keperluan = DefaultKeperluan;
            }
            return Task.CompletedTask;
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            Keperluan = string.IsNullOrWhiteSpace(suratData.Keperluan) ? DefaultKeperluan : suratData.Keperluan;

            // Model data: rincian[0] = tanah pertama (ditampilkan di "Detail Tanah Garapan"),
            // rincian[1..] = tanah tambahan (ditampilkan di grid rincian).
            RincianItems.Clear();
            var rincian = suratData.RincianGarapans;
            if (rincian != null && rincian.Count > 0)
            {
                var first = rincian[0];
                Lokasi = first.Lokasi;
                NomorPersil = first.NomorPersil;
                Luas = first.Luas > 0
                    ? first.Luas.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                KeteranganGarapan = NormalizeKeterangan(first.KeteranganGarapan);

                foreach (var item in rincian.Skip(1))
                {
                    RincianItems.Add(ToItem(item));
                }
            }
            else if (suratData.Garapan != null && !string.IsNullOrWhiteSpace(suratData.Garapan.Lokasi))
            {
                // Data lama tanpa rincian: cukup isi Detail Tanah Garapan.
                Lokasi = suratData.Garapan.Lokasi ?? string.Empty;
                NomorPersil = suratData.Garapan.NomorPersil ?? string.Empty;
                Luas = suratData.Garapan.Luas > 0
                    ? suratData.Garapan.Luas.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                KeteranganGarapan = NormalizeKeterangan(suratData.Garapan.KeteranganGarapan);
            }

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

        protected override Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return Task.CompletedTask;

            if (string.IsNullOrWhiteSpace(Keperluan))
            {
                throw new ValidationException("Keperluan harus diisi");
            }

            suratData.Keperluan = Keperluan.Trim();
            suratData.NamaJenis = SuratConstants.GARAPAN_SAWAH;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            var mainGarapan = BuildMainGarapan(suratData.Warga?.Nama);
            suratData.Garapan = mainGarapan;

            // Tanah pertama dari Detail Tanah Garapan, lalu tiap baris grid = tanah tambahan.
            // Baris grid yang sama sekali kosong dilewati agar tidak tersimpan sampah.
            suratData.RincianGarapans = new List<GarapanData>();
            if (!string.IsNullOrWhiteSpace(mainGarapan.Lokasi) || mainGarapan.Luas > 0)
            {
                suratData.RincianGarapans.Add(mainGarapan);
            }
            foreach (var item in RincianItems)
            {
                var lokasi = item.Lokasi.Trim();
                var luas = ParseLuas(item.Luas);
                if (string.IsNullOrWhiteSpace(lokasi) && luas <= 0 && string.IsNullOrWhiteSpace(item.NomorPersil))
                    continue;

                suratData.RincianGarapans.Add(new GarapanData
                {
                    Lokasi = lokasi,
                    NomorPersil = item.NomorPersil.Trim(),
                    Luas = luas,
                    KeteranganGarapan = NormalizeKeterangan(item.KeteranganGarapan),
                    PemilikTanah = suratData.Warga?.Nama
                });
            }

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = SuratConstants.GARAPAN_SAWAH;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            return Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Keperluan))
            {
                errors.Add("Keperluan harus diisi");
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

            // Tanah pertama (Detail Tanah Garapan) selalu wajib.
            if (string.IsNullOrWhiteSpace(Lokasi))
                errors.Add("Lokasi tanah harus diisi");
            if (ParseLuas(Luas) <= 0)
                errors.Add("Luas tanah harus lebih dari 0");

            // Baris grid (tanah ke-2 dst.): validasi hanya baris yang benar-benar diisi.
            foreach (var item in RincianItems)
            {
                var kosong = string.IsNullOrWhiteSpace(item.Lokasi) &&
                             ParseLuas(item.Luas) <= 0 &&
                             string.IsNullOrWhiteSpace(item.NomorPersil);
                if (kosong) continue;

                if (string.IsNullOrWhiteSpace(item.Lokasi))
                    errors.Add("Lokasi tanah harus diisi untuk semua bidang tambahan");
                if (ParseLuas(item.Luas) <= 0)
                    errors.Add("Luas tanah harus lebih dari 0 untuk semua bidang tambahan");
            }
        }

        public override string GetKeteranganTextBox() => Keperluan.Trim();

        private GarapanData BuildMainGarapan(string? pemilikTanah) => new GarapanData
        {
            Lokasi = Lokasi.Trim(),
            NomorPersil = NomorPersil.Trim(),
            Luas = ParseLuas(Luas),
            KeteranganGarapan = NormalizeKeterangan(KeteranganGarapan),
            PemilikTanah = pemilikTanah
        };

        private static RincianGarapanItem ToItem(GarapanData g) => new RincianGarapanItem
        {
            Lokasi = g.Lokasi ?? string.Empty,
            NomorPersil = g.NomorPersil ?? string.Empty,
            Luas = g.Luas > 0 ? g.Luas.ToString(CultureInfo.InvariantCulture) : string.Empty,
            KeteranganGarapan = NormalizeKeterangan(g.KeteranganGarapan),
            PemilikTanah = g.PemilikTanah ?? string.Empty
        };

        private static double ParseLuas(string? value)
        {
            return double.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var luas)
                ? luas
                : 0;
        }

        private static string NormalizeKeterangan(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                OpsiKeteranganGarapan.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return value;
            }
            return OpsiKeteranganGarapan[0];
        }
    }

    /// <summary>Baris rincian garapan yang dapat diedit inline — setara satu baris di FormInputRincianGarapan.</summary>
    public class RincianGarapanItem : ObservableObject
    {
        private string _luas = string.Empty;
        private string _lokasi = string.Empty;
        private string _pemilikTanah = string.Empty;
        private string _nomorPersil = string.Empty;
        private string _keteranganGarapan = "Garap";

        public string Luas { get => _luas; set => SetProperty(ref _luas, value); }
        public string Lokasi { get => _lokasi; set => SetProperty(ref _lokasi, value); }
        public string PemilikTanah { get => _pemilikTanah; set => SetProperty(ref _pemilikTanah, value); }
        public string NomorPersil { get => _nomorPersil; set => SetProperty(ref _nomorPersil, value); }
        public string KeteranganGarapan { get => _keteranganGarapan; set => SetProperty(ref _keteranganGarapan, value); }
    }
}