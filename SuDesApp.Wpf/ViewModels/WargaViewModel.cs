using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Baris tabel Data Warga. Memakai <see cref="WargaData"/> langsung agar
    /// tidak ada salinan yang bisa tidak sinkron dengan database; properties
    /// turunan (alamat siap tampil, status) hanya untuk tampilan.
    /// </summary>
    public class WargaBaris
    {
        public WargaData Data { get; init; } = new();

        public int ID_Warga => Data.ID_Warga;
        public string NIK => Data.NIK ?? string.Empty;
        public string Nama => Data.Nama ?? string.Empty;
        public string NoKK => Data.NoKK ?? string.Empty;
        public string RT => Data.RT ?? string.Empty;
        public string RW => Data.RW ?? string.Empty;
        public string NomorHP => Data.NomorHP ?? string.Empty;

        public string AlamatRingkas
        {
            get
            {
                var bagian = new List<string>();
                if (!string.IsNullOrWhiteSpace(Data.Dusun)) bagian.Add(Data.Dusun!.Trim());
                if (!string.IsNullOrWhiteSpace(Data.RT) || !string.IsNullOrWhiteSpace(Data.RW))
                    bagian.Add($"RT {Data.RT?.Trim() ?? "-"}/RW {Data.RW?.Trim() ?? "-"}");
                if (!string.IsNullOrWhiteSpace(Data.AlamatDetail)) bagian.Add(Data.AlamatDetail!.Trim());
                return bagian.Count == 0 ? "-" : string.Join(", ", bagian);
            }
        }

        public string JenisKelaminRingkas => NormalisasiJenisKelamin(Data.JenisKelamin);

        public string TTL
        {
            get
            {
                var tempat = string.IsNullOrWhiteSpace(Data.TempatLahir) ? "" : Data.TempatLahir.Trim();
                var tanggal = FormatTanggalPendek(Data.TanggalLahir);
                if (tempat.Length == 0 && tanggal.Length == 0) return "-";
                if (tanggal.Length == 0) return tempat;
                if (tempat.Length == 0) return tanggal;
                return $"{tempat}, {tanggal}";
            }
        }

        public string StatusTampil => StatusWargaTipe.Normalisasi(Data.StatusWarga);
        public string StatusDeskripsi => DeskripsiStatus(Data.StatusWarga);
        public string Umur => HitungUmur(Data.TanggalLahir);

        /// <summary>
        /// Status yang dipilih operator di dropdown mutasi. Berasal dari
        /// <see cref="StatusTampil"/> supaya operator tidak perlu memilih lagi
        /// saat hanya ingin mengoreksi keterangan lain.
        /// </summary>
        public string StatusSapuan { get; set; } = StatusWargaTipe.Aktif;

        public static string NormalisasiJenisKelamin(string? jk) =>
            (jk ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "L" or "LAKI-LAKI" or "LAKI LAKI" or "PRIA" => "Laki-laki",
                "P" or "PEREMPUAN" or "WANITA" => "Perempuan",
                _ => string.IsNullOrWhiteSpace(jk) ? "-" : jk!.Trim()
            };

        public static string DeskripsiStatus(string? status) => StatusWargaTipe.Normalisasi(status) switch
        {
            StatusWargaTipe.Aktif => "Tetap tinggal di desa",
            StatusWargaTipe.Baru => "Pendatang baru didaftarkan",
            StatusWargaTipe.Meninggal => "Meninggal",
            StatusWargaTipe.Pindah => "Pindah keluar desa",
            _ => "-"
        };

        /// <summary>
        /// Tanggal lahir bisa tersimpan dalam beberapa format (warisan data lama),
        /// jadi hanya pola yang bisa dibaca yang ditampilkan; lainnya teks aslinya
        /// supaya operator bisa mengoreksinya.
        /// </summary>
        public static string FormatTanggalPendek(string? tanggalLahir)
        {
            if (string.IsNullOrWhiteSpace(tanggalLahir)) return string.Empty;
            var teks = tanggalLahir!.Trim();

            if (DateTime.TryParse(teks, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var iso))
            {
                return iso.ToString("dd-MM-yyyy");
            }

            foreach (var format in new[] { "dd-MM-yyyy", "dd/MM/yyyy", "d MMMM yyyy", "dd MMM yyyy" })
            {
                if (DateTime.TryParseExact(teks, format, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    return parsed.ToString("dd-MM-yyyy");
                }
            }

            return teks;
        }

        /// <summary>Umur dalam tahun lengkap; "-" bila tanggal lahir tidak terbaca.</summary>
        public static string HitungUmur(string? tanggalLahir)
        {
            if (!DateTime.TryParse(tanggalLahir, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var lahir))
            {
                return "-";
            }

            var hariIni = DateTime.Today;
            var usia = hariIni.Year - lahir.Year;
            if (lahir.Date > hariIni.AddYears(-usia)) usia--;
            return usia < 0 ? "-" : $"{usia} th";
        }
    }

    /// <summary>Kartu statistik pada halaman Data Warga.</summary>
    public class WargaStatistikKartu
    {
        public string Label { get; init; } = string.Empty;
        public string Nilai { get; init; } = "0";
        public string Keterangan { get; init; } = string.Empty;
        public bool Tekankan { get; init; }
    }

    /// <summary>
    /// Halaman Data Warga: satu-satunya tempat operator mengelola data
    /// kependudukan desa. Aplikasi sebelumnya menulis <c>Warga</c> hanya
    /// sebagai sampingan pembuatan surat, sehingga tidak ada halaman untuk
    /// melihat, memperbaiki, atau menghitung warga.
    ///
    /// Isi halaman: kartu rekapitulasi, filter (cari/status/RT), tabel warga
    /// berpaginasi, form tambah-ubah, ubah status (mutasi), dan ekspor
    /// Excel/CSV/JSON. Semua angka dihitung di database, bukan dari daftar yang
    /// sudah dimuat, supaya tetap benar saat warga berjumlah ribuan.
    /// </summary>
    public class WargaViewModel : ObservableObject
    {
        private readonly IWargaRepository _warga;
        private readonly IDesaRepository _desa;
        private readonly IImporWargaService _impor;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<WargaViewModel> _logger;

        private bool _isBusy;
        private string _searchText = string.Empty;
        private string _statusText = string.Empty;
        private string _filterStatus = string.Empty;
        private string _filterWilayah = string.Empty;
        private int _halaman = 1;
        private int _totalData;
        private int _totalHalaman = 1;
        private int _jumlahDitampilkan;

        private readonly Dictionary<string, string> _daftarWilayah = new();
        private CancellationTokenSource? _debouncePencarian;

        public WargaViewModel(
            IWargaRepository warga,
            IDesaRepository desa,
            IImporWargaService impor,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<WargaViewModel> logger)
        {
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _desa = desa ?? throw new ArgumentNullException(nameof(desa));
            _impor = impor ?? throw new ArgumentNullException(nameof(impor));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            MuatUlangCommand = new AsyncRelayCommand(() => LoadAsync(true));
            RefreshCommand = new AsyncRelayCommand(() => LoadAsync(true));
            CariCommand = new AsyncRelayCommand(() => { Halaman = 1; return LoadAsync(true); });
            TambahCommand = new AsyncRelayCommand(BukaFormTambahAsync);
            UbahCommand = new AsyncRelayCommand<WargaBaris>(BukaFormUbahAsync, b => b != null);
            BatalFormCommand = new RelayCommand(TutupForm);
            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(FormNik));
            UbahStatusCommand = new AsyncRelayCommand<UbahStatusRequest>(UbahStatusAsync, r => r != null);
            EksporCommand = new AsyncRelayCommand(EksporAsync, () => !IsBusy);
            BukaImporCommand = new AsyncRelayCommand(BukaImporAsync, () => !IsBusy);
            TutupImporCommand = new RelayCommand(TutupImpor);
            PeriksaImporCommand = new AsyncRelayCommand(PeriksaImporAsync,
                () => !IsBusy && !string.IsNullOrWhiteSpace(JalurBerkasImpor));
            JalankanImporCommand = new AsyncRelayCommand(JalankanImporAsync,
                () => !IsBusy && HasilImpor != null && HasilImpor.JumlahBisaDisimpan > 0);
            HalamanSebelumnyaCommand = new AsyncRelayCommand(
                async () => { Halaman = Math.Max(1, Halaman - 1); await LoadAsync(false); },
                () => !IsBusy && Halaman > 1);
            HalamanBerikutnyaCommand = new AsyncRelayCommand(
                async () => { Halaman = Halaman + 1; await LoadAsync(false); },
                () => !IsBusy && Halaman < TotalHalaman);
        }

        // ----- Identitas halaman -----
        public string HeaderTitle => "DATA WARGA";
        public string HeaderSubtitle =>
            "Data kependudukan desa: nomor induk, Kartu Keluarga, alamat RT/RW, dan status tinggal warga.";

        public ObservableCollection<WargaBaris> Items { get; private set; } = new();
        public ObservableCollection<WargaStatistikKartu> KartuStatistik { get; private set; } = new();
        public ObservableCollection<WargaStatistikBaris> RekapRt { get; private set; } = new();
        public ObservableCollection<WargaStatistikBaris> RekapKelompokUsia { get; private set; } = new();

        /// <summary>Pilihan status untuk dropdown filter; item pertama berarti "semua".</summary>
        public IReadOnlyList<string> PilihanStatus { get; } =
            new[] { string.Empty }.Concat(StatusWargaTipe.Semua).ToList();

        /// <summary>Pilihan status untuk form ubah; dipakai <c>UbahStatusCommand</c>.</summary>
        public IReadOnlyList<string> PilihanStatusForm { get; } = StatusWargaTipe.Semua;

        public IReadOnlyList<string> DaftarAgama { get; } =
            new[] { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" };

        public IReadOnlyList<string> DaftarJenisKelamin { get; } = new[] { "Laki-laki", "Perempuan" };

        public IReadOnlyList<string> DaftarStatusPerkawinan { get; } =
            new[] { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" };

        /// <summary>Kedudukan dalam Kartu Keluarga untuk dropdown form warga.</summary>
        public IReadOnlyList<string> DaftarStatusKeluarga { get; } =
            new[] { string.Empty }.Concat(StatusKeluargaTipe.Semua).ToArray();

        public IReadOnlyList<string> DaftarGolonganDarah { get; } =
            new[] { "A", "B", "AB", "O", "Tidak diketahui" };

        public IReadOnlyList<string> DaftarPekerjaanUmum { get; } =
            new[] { "Petani", "Buruh Tani", "Wiraswasta", "Pedagang", "Buruh Pabrik", "ASN/PNS", "TNI/Polri", "Guru/Dosen", "Tenaga Kesehatan", "Pensiunan", "Pelajar/Mahasiswa", "Ibu Rumah Tangga", "Lainnya" };

        // ----- Keadaan -----
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(AdanyaData));
                    SegarkanPerintah();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>Catat pesan singkat tanpa memuat ulang daftar (dipakai halaman).</summary>
        public void CatatStatus(string pesan) => StatusText = pesan;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value)) JadwalkanMuatUlang();
            }
        }

        /// <summary>Filter status; string kosong berarti semua status.</summary>
        public string FilterStatus
        {
            get => _filterStatus;
            set
            {
                if (SetProperty(ref _filterStatus, value ?? string.Empty))
                {
                    Halaman = 1;
                    _ = LoadAsync(true);
                }
            }
        }

        /// <summary>
        /// Filter wilayah dengan format "RT:001" / "RW:002" / "Dusun:Dusun I".
        /// Nilai disimpan apa adanya sebagai "<jenis>:<nilai>" supaya dropdown
        /// RT, RW, dan dusun bisa memakai satu daftar.
        /// </summary>
        public string FilterWilayah
        {
            get => _filterWilayah;
            set
            {
                if (SetProperty(ref _filterWilayah, value ?? string.Empty))
                {
                    Halaman = 1;
                    _ = LoadAsync(true);
                }
            }
        }

        public int Halaman
        {
            get => _halaman;
            private set
            {
                if (SetProperty(ref _halaman, value)) SegarkanPerintah();
            }
        }

        public int TotalData
        {
            get => _totalData;
            private set { if (SetProperty(ref _totalData, value)) OnPropertyChanged(nameof(RingkasanPaginasi)); }
        }

        public int TotalHalaman
        {
            get => _totalHalaman;
            private set { if (SetProperty(ref _totalHalaman, value)) OnPropertyChanged(nameof(RingkasanPaginasi)); }
        }

        public int JumlahDitampilkan
        {
            get => _jumlahDitampilkan;
            private set => SetProperty(ref _jumlahDitampilkan, value);
        }

        public bool AdanyaData => !IsBusy && Items.Count > 0;

        public string RingkasanPaginasi =>
            TotalData == 0
                ? "Tidak ada warga yang sesuai filter."
                : $"Halaman {Halaman} dari {TotalHalaman} • total {TotalData} warga";

        // ----- Form tambah / ubah -----
        private int _formIdWarga;

        public bool IsFormTerbuka
        {
            get => _isFormTerbuka;
            private set => SetProperty(ref _isFormTerbuka, value);
        }
        private bool _isFormTerbuka;
        private bool _isModeUbah;

        public bool IsModeUbah
        {
            get => _isModeUbah;
            private set => SetProperty(ref _isModeUbah, value);
        }

        public string JudulForm => IsModeUbah ? "Ubah Data Warga" : "Tambah Data Warga";

        /// <summary>Nomor HP kepala keluarga: dipakai form dan kartu keluarga.</summary>
        private string _formNoKK = string.Empty;
        public string FormNoKK
        {
            get => _formNoKK;
            set => SetProperty(ref _formNoKK, value ?? string.Empty);
        }

        private string _formNik = string.Empty;
        public string FormNik
        {
            get => _formNik;
            set { if (SetProperty(ref _formNik, value ?? string.Empty)) SegarkanPerintah(); }
        }

        private string _formNama = string.Empty;
        public string FormNama
        {
            get => _formNama;
            set => SetProperty(ref _formNama, value ?? string.Empty);
        }

        private string _formTempatLahir = string.Empty;
        public string FormTempatLahir
        {
            get => _formTempatLahir;
            set => SetProperty(ref _formTempatLahir, value ?? string.Empty);
        }

        private string _formTanggalLahir = string.Empty;
        public string FormTanggalLahir
        {
            get => _formTanggalLahir;
            set => SetProperty(ref _formTanggalLahir, value ?? string.Empty);
        }

        private string _formJenisKelamin = string.Empty;
        public string FormJenisKelamin
        {
            get => _formJenisKelamin;
            set => SetProperty(ref _formJenisKelamin, value ?? string.Empty);
        }

        private string _formAgama = string.Empty;
        public string FormAgama
        {
            get => _formAgama;
            set => SetProperty(ref _formAgama, value ?? string.Empty);
        }

        private string _formStatusPerkawinan = string.Empty;
        public string FormStatusPerkawinan
        {
            get => _formStatusPerkawinan;
            set => SetProperty(ref _formStatusPerkawinan, value ?? string.Empty);
        }

        /// <summary>Kedudukan dalam Kartu Keluarga: Kepala Keluarga/Istri/Anak/Cucu/Family Lain.</summary>
        private string _formStatusKeluarga = string.Empty;
        public string FormStatusKeluarga
        {
            get => _formStatusKeluarga;
            set => SetProperty(ref _formStatusKeluarga, value ?? string.Empty);
        }

        private string _formGolonganDarah = string.Empty;
        public string FormGolonganDarah
        {
            get => _formGolonganDarah;
            set => SetProperty(ref _formGolonganDarah, value ?? string.Empty);
        }

        private string _formPekerjaan = string.Empty;
        public string FormPekerjaan
        {
            get => _formPekerjaan;
            set => SetProperty(ref _formPekerjaan, value ?? string.Empty);
        }

        private string _formPendidikan = string.Empty;
        public string FormPendidikan
        {
            get => _formPendidikan;
            set => SetProperty(ref _formPendidikan, value ?? string.Empty);
        }

        private string _formNamaAyah = string.Empty;
        public string FormNamaAyah
        {
            get => _formNamaAyah;
            set => SetProperty(ref _formNamaAyah, value ?? string.Empty);
        }

        private string _formNamaIbu = string.Empty;
        public string FormNamaIbu
        {
            get => _formNamaIbu;
            set => SetProperty(ref _formNamaIbu, value ?? string.Empty);
        }

        private string _formNomorHP = string.Empty;
        public string FormNomorHP
        {
            get => _formNomorHP;
            set => SetProperty(ref _formNomorHP, value ?? string.Empty);
        }

        private string _formDusun = string.Empty;
        public string FormDusun
        {
            get => _formDusun;
            set => SetProperty(ref _formDusun, value ?? string.Empty);
        }

        private string _formRT = string.Empty;
        public string FormRT
        {
            get => _formRT;
            set => SetProperty(ref _formRT, value ?? string.Empty);
        }

        private string _formRW = string.Empty;
        public string FormRW
        {
            get => _formRW;
            set => SetProperty(ref _formRW, value ?? string.Empty);
        }

        private string _formAlamatDetail = string.Empty;
        public string FormAlamatDetail
        {
            get => _formAlamatDetail;
            set => SetProperty(ref _formAlamatDetail, value ?? string.Empty);
        }

        private string _formStatusWarga = StatusWargaTipe.Aktif;
        public string FormStatusWarga
        {
            get => _formStatusWarga;
            set => SetProperty(ref _formStatusWarga, value ?? string.Empty);
        }

        private string _formTanggalStatus = string.Empty;
        public string FormTanggalStatus
        {
            get => _formTanggalStatus;
            set => SetProperty(ref _formTanggalStatus, value ?? string.Empty);
        }

        private string _formKeterangan = string.Empty;
        public string FormKeterangan
        {
            get => _formKeterangan;
            set => SetProperty(ref _formKeterangan, value ?? string.Empty);
        }

        private string _formGalat = string.Empty;
        public string FormGalat
        {
            get => _formGalat;
            private set => SetProperty(ref _formGalat, value);
        }

        // ----- Perintah -----
        public ICommand MuatUlangCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CariCommand { get; }
        public ICommand TambahCommand { get; }
        public ICommand UbahCommand { get; }
        public ICommand BatalFormCommand { get; }
        public ICommand SimpanCommand { get; }
        public ICommand UbahStatusCommand { get; }
        public ICommand EksporCommand { get; }
        public ICommand BukaImporCommand { get; }
        public ICommand TutupImporCommand { get; }
        public ICommand PeriksaImporCommand { get; }
        public ICommand JalankanImporCommand { get; }
        public ICommand HalamanSebelumnyaCommand { get; }
        public ICommand HalamanBerikutnyaCommand { get; }

        private void SegarkanPerintah()
        {
            (MuatUlangCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (TambahCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SimpanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (EksporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (BukaImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PeriksaImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (JalankanImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (HalamanSebelumnyaCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (HalamanBerikutnyaCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        // =====================================================================
        // Pemuatan data
        // =====================================================================

        /// <summary>Muat daftar warga + rekapitulasi. Dipanggil saat halaman dibuka.</summary>
        public async Task LoadAsync(bool muatUlangRekap = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var filter = SusunFilter();
                var hasil = await _warga.GetWargaPageAsync(filter);

                Items = new ObservableCollection<WargaBaris>(hasil.Items.Select(w => new WargaBaris
                {
                    Data = w,
                    StatusSapuan = StatusWargaTipe.Normalisasi(w.StatusWarga)
                }));
                OnPropertyChanged(nameof(Items));

                TotalData = hasil.Total;
                TotalHalaman = hasil.TotalHalaman;
                Halaman = hasil.Halaman;
                JumlahDitampilkan = Items.Count;
                StatusText = hasil.Ringkasan;

                await MuatDaftarWilayahAsync();
                if (muatUlangRekap) await MuatRekapAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data warga");
                StatusText = "Gagal memuat data warga.";
                await _messageService.ShowErrorAsync("Gagal memuat data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MuatRekapAsync()
        {
            try
            {
                var s = await _warga.GetStatistikWargaAsync();

                KartuStatistik = new ObservableCollection<WargaStatistikKartu>
                {
                    new() { Label = "Warga terdata", Nilai = s.TotalSeluruh.ToString("N0"), Keterangan = $"{s.TotalAktif} tinggal, {s.TotalMeninggal + s.TotalPindah} sudah tidak tinggal", Tekankan = true },
                    new() { Label = "Warga tinggal di desa", Nilai = (s.TotalSeluruh - s.TotalMeninggal - s.TotalPindah).ToString("N0"), Keterangan = "tetap + pendatang" },
                    new() { Label = "Laki-laki", Nilai = s.LakiLaki.ToString("N0"), Keterangan = $"{(s.TotalSeluruh > 0 ? s.LakiLaki * 100.0 / s.TotalSeluruh : 0):0.#}%" },
                    new() { Label = "Perempuan", Nilai = s.Perempuan.ToString("N0"), Keterangan = $"{(s.TotalSeluruh > 0 ? s.Perempuan * 100.0 / s.TotalSeluruh : 0):0.#}%" },
                    new() { Label = "Kepala Keluarga", Nilai = s.JumlahKepalaKeluarga.ToString("N0"), Keterangan = $"{s.KepalaKeluargaLakiLaki} laki-laki, {s.KepalaKeluargaPerempuan} wanita" },
                    new() { Label = "KK Laki-laki", Nilai = s.KepalaKeluargaLakiLaki.ToString("N0"), Keterangan = "Berkepala laki-laki" },
                    new() { Label = "KK Wanita", Nilai = s.KepalaKeluargaPerempuan.ToString("N0"), Keterangan = "Berkepala wanita" },
                    new() { Label = "Kartu Keluarga", Nilai = s.JumlahKartuKeluarga.ToString("N0"), Keterangan = $"{s.TanpaKartuKeluarga} warga belum ada KK" },
                    new() { Label = "Meninggal / pindah", Nilai = (s.TotalMeninggal + s.TotalPindah).ToString("N0"), Keterangan = $"{s.TotalMeninggal} meninggal, {s.TotalPindah} pindah" }
                };
                OnPropertyChanged(nameof(KartuStatistik));

                RekapRt = new ObservableCollection<WargaStatistikBaris>(s.PerRt);
                OnPropertyChanged(nameof(RekapRt));

                RekapKelompokUsia = new ObservableCollection<WargaStatistikBaris>(s.PerKelompokUsia);
                OnPropertyChanged(nameof(RekapKelompokUsia));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat rekapitulasi warga");
                // Rekap hanya INFORMASI: kegagalan di sini tidak boleh menutup
                // halaman karena daftar warga tetap bisa dipakai.
            }
        }

        private async Task MuatDaftarWilayahAsync()
        {
            try
            {
                var nilai = await _warga.GetDaftarNilaiWilayahAsync();
                var baru = BuildDaftarWilayah(nilai);
                if (SamaDengan(_daftarWilayah, baru)) return;

                _daftarWilayah.Clear();
                foreach (var pair in baru) _daftarWilayah[pair.Key] = pair.Value;
                OnPropertyChanged(nameof(DaftarWilayah));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil daftar nilai RT/RW/dusun");
            }
        }

        private static bool SamaDengan(Dictionary<string, string> kiri, Dictionary<string, string> kanan)
        {
            if (kiri.Count != kanan.Count) return false;
            foreach (var pair in kanan)
            {
                if (!kiri.TryGetValue(pair.Key, out var nilai) || nilai != pair.Value) return false;
            }
            return true;
        }

        private static Dictionary<string, string> BuildDaftarWilayah(IEnumerable<string> nilai)
        {
            var hasil = new Dictionary<string, string>(StringComparer.Ordinal);
            var rt = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var rw = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var dusun = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in nilai)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var teks = v.Trim();
                if (teks.Length == 0) continue;

                // Nilai lama bisa berupa "Dusun I" atau "RT 001 / RW 002"; dikelompokkan
                // berdasarkan awalan supaya tidak bercampur dengan dusun bernama serupa.
                if (teks.StartsWith("RT", StringComparison.OrdinalIgnoreCase) || IsNumerik(teks))
                    rt.Add(AmbikAngka(teks));
                else if (teks.StartsWith("RW", StringComparison.OrdinalIgnoreCase))
                    rw.Add(AmbikAngka(teks));
                else
                    dusun.Add(teks);
            }

            foreach (var n in rt) hasil[$"RT:{n}"] = n;
            foreach (var n in rw) hasil[$"RW:{n}"] = n;
            foreach (var n in dusun) hasil[$"DUSUN:{n.ToUpperInvariant()}"] = n;
            return hasil;
        }

        private static bool IsNumerik(string teks) => teks.All(char.IsDigit);

        private static string AmbikAngka(string teks)
        {
            var digit = new string(teks.Where(char.IsDigit).ToArray());
            return digit.Length > 0 ? digit : teks;
        }

        /// <summary>Opsi dropdown wilayah; item pertama berarti "semua wilayah".</summary>
        public IEnumerable<System.Windows.Controls.ComboBoxItem> DaftarWilayah
        {
            get
            {
                yield return new System.Windows.Controls.ComboBoxItem { Content = "Semua wilayah", Tag = string.Empty };
                foreach (var pair in _daftarWilayah.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    yield return new System.Windows.Controls.ComboBoxItem { Content = pair.Value, Tag = pair.Key };
                }
            }
        }

        private WargaFilter SusunFilter()
        {
            var filter = new WargaFilter
            {
                Cari = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Status = string.IsNullOrWhiteSpace(FilterStatus) ? null : FilterStatus,
                Halaman = Halaman,
                UkuranHalaman = 200
            };

            if (!string.IsNullOrWhiteSpace(FilterWilayah) && FilterWilayah.Contains(':'))
            {
                var bagian = FilterWilayah.Split(':', 2);
                switch (bagian[0].ToUpperInvariant())
                {
                    case "RT": filter.RT = bagian[1]; break;
                    case "RW": filter.RW = bagian[1]; break;
                    case "DUSUN": filter.Dusun = bagian[1]; break;
                }
            }

            return filter;
        }

        private void JadwalkanMuatUlang()
        {
            var sebelumnya = _debouncePencarian;
            var baru = new CancellationTokenSource();
            _debouncePencarian = baru;
            sebelumnya?.Cancel();
            sebelumnya?.Dispose();

            _ = MuatTertundaAsync(baru.Token);
        }

        private async Task MuatTertundaAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(300, cancellationToken);
                for (int i = 0; i < 20 && IsBusy; i++)
                {
                    await Task.Delay(100, cancellationToken);
                }

                Halaman = 1;
                await LoadAsync(true);
            }
            catch (OperationCanceledException)
            {
                // Ketikan berikutnya menggantikan jadwal ini.
            }
        }

        // =====================================================================
        // Form tambah / ubah
        // =====================================================================

        private async Task BukaFormTambahAsync()
        {
            _formIdWarga = 0;
            IsModeUbah = false;
            FormGalat = string.Empty;

            var desa = await _desa.GetInfoDesaFromCacheAsync();

            FormNoKK = string.Empty;
            FormNik = string.Empty;
            FormNama = string.Empty;
            FormTempatLahir = string.Empty;
            FormTanggalLahir = string.Empty;
            FormJenisKelamin = string.Empty;
            FormAgama = "Islam";
            FormStatusPerkawinan = "Belum Kawin";
            FormStatusKeluarga = StatusKeluargaTipe.KepalaKeluarga;
            FormGolonganDarah = string.Empty;
            FormPekerjaan = string.Empty;
            FormPendidikan = string.Empty;
            FormNamaAyah = string.Empty;
            FormNamaIbu = string.Empty;
            FormNomorHP = string.Empty;
            FormDusun = string.Empty;
            FormRT = string.Empty;
            FormRW = string.Empty;
            FormStatusKeluarga = string.Empty;
            FormAlamatDetail = string.Empty;
            FormStatusWarga = StatusWargaTipe.Aktif;
            FormTanggalStatus = DateTime.Today.ToString("yyyy-MM-dd");
            FormKeterangan = string.Empty;

            IsFormTerbuka = true;
            OnPropertyChanged(nameof(JudulForm));

            // Desa/kecamatan/kabupaten mengikuti data desa supaya operator tidak
            // perlu mengetik ulang untuk setiap warga.
            _desaBawaan = desa;
        }

        private DesaData _desaBawaan = new();

        private async Task BukaFormUbahAsync(WargaBaris? baris)
        {
            if (baris == null) return;

            try
            {
                var warga = await _warga.GetWargaByIdAsync(baris.ID_Warga);
                if (warga == null)
                {
                    await _messageService.ShowInfoAsync("Data warga tidak ditemukan. Mungkin sudah dihapus.");
                    return;
                }

                _desaBawaan = await _desa.GetInfoDesaFromCacheAsync();

                _formIdWarga = warga.ID_Warga;
                IsModeUbah = true;
                FormGalat = string.Empty;

                FormNoKK = warga.NoKK ?? string.Empty;
                FormNik = warga.NIK ?? string.Empty;
                FormNama = warga.Nama ?? string.Empty;
                FormTempatLahir = warga.TempatLahir ?? string.Empty;
                FormTanggalLahir = warga.TanggalLahir ?? string.Empty;
                FormJenisKelamin = WargaBaris.NormalisasiJenisKelamin(warga.JenisKelamin) == "-" ? "" : WargaBaris.NormalisasiJenisKelamin(warga.JenisKelamin);
                FormAgama = warga.Agama ?? string.Empty;
                FormStatusPerkawinan = warga.StatusPerkawinan ?? string.Empty;
                FormStatusKeluarga = warga.StatusKeluarga ?? string.Empty;
                FormGolonganDarah = warga.GolonganDarah ?? string.Empty;
                FormPekerjaan = warga.Pekerjaan ?? string.Empty;
                FormPendidikan = warga.Pendidikan ?? string.Empty;
                FormNamaAyah = warga.NamaAyah ?? string.Empty;
                FormNamaIbu = warga.NamaIbu ?? string.Empty;
                FormNomorHP = warga.NomorHP ?? string.Empty;
                FormDusun = warga.Dusun ?? string.Empty;
                FormRT = warga.RT ?? string.Empty;
                FormRW = warga.RW ?? string.Empty;
                FormAlamatDetail = warga.AlamatDetail ?? string.Empty;
                FormStatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                FormTanggalStatus = warga.TanggalStatus ?? string.Empty;
                FormKeterangan = warga.KeteranganWarga ?? string.Empty;

                IsFormTerbuka = true;
                OnPropertyChanged(nameof(JudulForm));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form ubah warga");
                await _messageService.ShowErrorAsync("Gagal membuka data warga.\n\n" + ex.Message);
            }
        }

        private void TutupForm()
        {
            IsFormTerbuka = false;
            FormGalat = string.Empty;
            _formIdWarga = 0;
        }

        private async Task SimpanAsync()
        {
            FormGalat = string.Empty;

            var nik = (FormNik ?? string.Empty).Trim();
            if (nik.Length != 16 || !nik.All(char.IsDigit))
            {
                FormGalat = "NIK harus 16 digit angka.";
                return;
            }

            if (string.IsNullOrWhiteSpace(FormNama) || FormNama.Trim().Length < 2)
            {
                FormGalat = "Nama wajib diisi (minimal 2 karakter).";
                return;
            }

            var noKK = (FormNoKK ?? string.Empty).Trim();
            if (noKK.Length > 0 && (noKK.Length != 16 || !noKK.All(char.IsDigit)))
            {
                FormGalat = "Nomor Kartu Keluarga harus 16 digit angka (boleh dikosongkan).";
                return;
            }

            IsBusy = true;
            try
            {
                var model = new WargaData
                {
                    ID_Warga = _formIdWarga,
                    NIK = nik,
                    Nama = FormNama?.Trim(),
                    TempatLahir = FormTempatLahir?.Trim(),
                    TanggalLahir = NormalisasiTanggalLahir(FormTanggalLahir),
                    JenisKelamin = FormJenisKelamin,
                    Agama = KosongkanJikaTidakDipilih(FormAgama),
                    StatusPerkawinan = KosongkanJikaTidakDipilih(FormStatusPerkawinan),
                    StatusKeluarga = KosongkanJikaTidakDipilih(FormStatusKeluarga),
                    GolonganDarah = KosongkanJikaTidakDipilih(FormGolonganDarah),
                    Pekerjaan = FormPekerjaan?.Trim(),
                    Pendidikan = FormPendidikan?.Trim(),
                    NamaAyah = FormNamaAyah?.Trim(),
                    NamaIbu = FormNamaIbu?.Trim(),
                    NomorHP = BersihkanNomorHP(FormNomorHP),
                    NoKK = noKK.Length == 0 ? null : noKK,
                    Dusun = FormDusun?.Trim(),
                    RT = FormRT?.Trim(),
                    RW = FormRW?.Trim(),
                    AlamatDetail = FormAlamatDetail?.Trim(),
                    // Desa/kecamatan/kabupaten selalu mengikuti data desa: surat
                    // resmi desa tidak boleh mencantumkan desa lain.
                    Desa = _desaBawaan?.NamaDesa,
                    Kecamatan = _desaBawaan?.Kecamatan,
                    Kabupaten = _desaBawaan?.Kabupaten,
                    StatusWarga = StatusWargaTipe.Normalisasi(FormStatusWarga),
                    TanggalStatus = FormTanggalStatus?.Trim(),
                    KeteranganWarga = FormKeterangan?.Trim(),
                    Kewarganegaraan = "WNI"
                };

                int id = await _warga.AddOrUpdateWargaAndGetIdAsync(model);

                if (noKK.Length == 16)
                {
                    await SimpanKartuKeluargaJikaBelumAdaAsync(model, noKK);
                }

                _activityLog.Log("WARGA", $"{model.Nama} ({nik})", IsModeUbah ? "Ubah" : "Tambah",
                    IsModeUbah ? $"ID {id} diperbarui" : $"ID {id} ditambahkan");

                TutupForm();
                await LoadAsync(true);
                StatusText = IsModeUbah ? "Data warga diperbarui." : "Warga baru ditambahkan.";
                await _messageService.ShowInfoAsync(IsModeUbah
                    ? "Data warga berhasil diperbarui."
                    : "Warga baru berhasil ditambahkan.");
            }
            catch (ValidationException vex)
            {
                FormGalat = vex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan data warga");
                FormGalat = ex.Message;
                await _messageService.ShowErrorAsync("Gagal menyimpan data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Buat KK untuk warga baru yang NoKK-nya belum terdaftar. KK yang sudah ada
        /// tidak ditimpa supaya alamat yang sudah dikoreksi tidak hilang.
        /// </summary>
        private async Task SimpanKartuKeluargaJikaBelumAdaAsync(WargaData warga, string noKK)
        {
            try
            {
                var ada = await _warga.GetKartuKeluargaAsync(noKK);
                if (ada != null) return;

                await _warga.SaveKartuKeluargaAsync(new KartuKeluargaData
                {
                    NoKK = noKK,
                    NamaKepalaKeluarga = warga.Nama,
                    Alamat = warga.AlamatDetail,
                    RT = warga.RT,
                    RW = warga.RW,
                    Dusun = warga.Dusun,
                    Desa = warga.Desa,
                    Kecamatan = warga.Kecamatan,
                    Kabupaten = warga.Kabupaten,
                    NomorHP = warga.NomorHP
                });
            }
            catch (Exception ex)
            {
                // KK adalah turunan: kegagalan menulis KK tidak boleh membatalkan
                // warga yang sudah tersimpan.
                _logger.LogWarning(ex, "Gagal membuat kartu keluarga otomatis untuk No KK {NoKK}", noKK);
            }
        }

        private static string KosongkanJikaTidakDipilih(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) || nilai!.Trim() == "-" ? string.Empty : nilai.Trim();

        private static string? BersihkanNomorHP(string? nomor)
        {
            if (string.IsNullOrWhiteSpace(nomor)) return null;
            var digit = new string(nomor.Where(char.IsDigit).ToArray());
            if (digit.Length == 0) return null;
            // Nomor Indonesia: 62xxx jadi 0xxx agar format baku.
            if (digit.StartsWith("62", StringComparison.Ordinal) && digit.Length >= 11)
                digit = "0" + digit[2..];
            return digit;
        }

        private static string? NormalisasiTanggalLahir(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null;
            var teks = tanggal!.Trim();
            if (DateTime.TryParse(teks, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd");
            }
            return teks;
        }

        // =====================================================================
        // Mutasi: pindah / meninggal / pendatang
        // =====================================================================

        /// <summary>Permintaan ubah status beserta ketikanya (dipakai sebagai parameter perintah).</summary>
        public class UbahStatusRequest
        {
            public int IdWarga { get; init; }
            public string Nama { get; init; } = string.Empty;
            public string StatusBaru { get; init; } = StatusWargaTipe.Aktif;
            public string Keterangan { get; init; } = string.Empty;
        }

        private async Task UbahStatusAsync(UbahStatusRequest? request)
        {
            if (request == null) return;

            if (!StatusWargaTipe.Valid(request.StatusBaru))
            {
                await _messageService.ShowErrorAsync("Status warga tidak dikenal.");
                return;
            }

            bool benar = await _messageService.ShowConfirmationAsync(
                "Ubah Status Warga",
                $"Ubah status warga \"{request.Nama}\" menjadi {StatusWargaTipe.Normalisasi(request.StatusBaru)}?\n\n" +
                "Data surat yang pernah dibuat tidak ikut berubah.");
            if (!benar) return;

            IsBusy = true;
            try
            {
                await _warga.UbahStatusWargaAsync(
                    request.IdWarga,
                    request.StatusBaru,
                    DateTime.Today.ToString("yyyy-MM-dd"),
                    string.IsNullOrWhiteSpace(request.Keterangan) ? null : request.Keterangan);

                _activityLog.Log("WARGA", request.Nama, "Mutasi",
                    $"Status diubah menjadi {StatusWargaTipe.Normalisasi(request.StatusBaru)}");

                await LoadAsync(true);
                StatusText = $"Status warga diperbarui: {StatusWargaTipe.Normalisasi(request.StatusBaru)}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengubah status warga");
                await _messageService.ShowErrorAsync("Gagal mengubah status warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =====================================================================
        // Impor warga massal dari Excel/CSV
        // =====================================================================

        private bool _isImporTerbuka;
        private string _jalurBerkasImpor = string.Empty;
        private bool _imporPerbaruiYangAda;
        private bool _isProsesImpor;
        private int _progresImpor;
        private string _ringkasanImpor = string.Empty;
        private string _infoKolomAsing = string.Empty;
        private HasilImporWarga? _hasilImpor;

        /// <summary>Panel impor terbuka di atas daftar (pola sama dengan form warga).</summary>
        public bool IsImporTerbuka
        {
            get => _isImporTerbuka;
            private set => SetProperty(ref _isImporTerbuka, value);
        }

        public string JalurBerkasImpor
        {
            get => _jalurBerkasImpor;
            private set
            {
                if (!SetProperty(ref _jalurBerkasImpor, value)) return;

                // Ganti berkas berarti hasil pemeriksaan lama tidak berlaku lagi;
                // menyimpan tanpa memeriksa ulang bisa menulis data yang salah.
                if (HasilImpor != null)
                {
                    HasilImpor = null;
                    PratinjauImpor.Clear();
                    RingkasanImpor = string.Empty;
                }

                SegarkanPerintah();
            }
        }

        /// <summary>Perbarui warga yang NIK-nya sudah ada; tanpa ini baris itu dilewati.</summary>
        public bool ImporPerbaruiYangAda
        {
            get => _imporPerbaruiYangAda;
            set
            {
                if (!SetProperty(ref _imporPerbaruiYangAda, value)) return;

                // Memilih mode perbarui mengubah keputusan baris duplikat, jadi
                // pemeriksaan harus diulang.
                if (HasilImpor != null)
                {
                    HasilImpor = null;
                    PratinjauImpor.Clear();
                    RingkasanImpor = "Mode diubah. Periksa lagi berkas untuk melihat dampaknya.";
                }
            }
        }

        public bool IsProsesImpor
        {
            get => _isProsesImpor;
            private set => SetProperty(ref _isProsesImpor, value);
        }

        public int ProgresImpor
        {
            get => _progresImpor;
            private set => SetProperty(ref _progresImpor, value);
        }

        public string RingkasanImpor
        {
            get => _ringkasanImpor;
            private set => SetProperty(ref _ringkasanImpor, value);
        }

        /// <summary>Judul kolom yang diabaikan, supaya operator tahu isinya tidak terbaca.</summary>
        public string InfoKolomAsing
        {
            get => _infoKolomAsing;
            private set => SetProperty(ref _infoKolomAsing, value);
        }

        public ObservableCollection<BarisImporWarga> PratinjauImpor { get; } = new();

        public HasilImporWarga? HasilImpor
        {
            get => _hasilImpor;
            private set
            {
                if (!SetProperty(ref _hasilImpor, value)) return;
                OnPropertyChanged(nameof(BisaJalankanImpor));
                SegarkanPerintah();
            }
        }

        public bool BisaJalankanImpor =>
            HasilImpor != null && HasilImpor.JumlahBisaDisimpan > 0;

        private void TutupImpor()
        {
            IsImporTerbuka = false;
            IsProsesImpor = false;
            ProgresImpor = 0;
        }

        private async Task BukaImporAsync()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Impor Data Warga",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                DefaultExt = ".xlsx"
            };

            if (ofd.ShowDialog() != true) return;

            TutupImpor();
            RingkasanImpor = string.Empty;
            InfoKolomAsing = string.Empty;
            PratinjauImpor.Clear();
            HasilImpor = null;

            // SetProperty pada JalurBerkasImpor juga membersihkan hasil lama.
            JalurBerkasImpor = ofd.FileName;
            IsImporTerbuka = true;

            // Langsung periksa: operator tidak perlu menekan tombol lain dulu.
            await PeriksaImporAsync();
        }

        /// <summary>Periksa berkas tanpa menyimpan apa pun, lalu tampilkan per baris.</summary>
        private async Task PeriksaImporAsync()
        {
            if (string.IsNullOrWhiteSpace(JalurBerkasImpor)) return;

            IsProsesImpor = true;
            try
            {
                var hasil = await _impor.ValidasiAsync(JalurBerkasImpor, ImporPerbaruiYangAda);

                if (hasil.AdaKesalahanBerkas)
                {
                    RingkasanImpor = hasil.KesalahanBerkas!;
                    InfoKolomAsing = string.Empty;
                    PratinjauImpor.Clear();
                    HasilImpor = null;
                    await _messageService.ShowWarningAsync(hasil.KesalahanBerkas!);
                    return;
                }

                HasilImpor = hasil;
                RingkasanImpor = hasil.Ringkasan;
                InfoKolomAsing = hasil.KolomTidakDikenali.Count > 0
                    ? "Kolom diabaikan: " + string.Join(", ", hasil.KolomTidakDikenali)
                    : string.Empty;

                PratinjauImpor.Clear();
                foreach (var b in hasil.Baris)
                    PratinjauImpor.Add(b);

                StatusText = hasil.Ringkasan;

                if (hasil.JumlahBisaDisimpan == 0)
                {
                    await _messageService.ShowWarningAsync(
                        "Tidak ada baris yang bisa disimpan.\n\n" + hasil.Ringkasan +
                        "\n\nPerbaiki berkas lebih dulu, lalu periksa lagi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa berkas impor warga");
                RingkasanImpor = "Gagal membaca berkas: " + ex.Message;
                HasilImpor = null;
                PratinjauImpor.Clear();
                await _messageService.ShowErrorAsync("Gagal memeriksa berkas impor.\n\n" + ex.Message);
            }
            finally
            {
                IsProsesImpor = false;
            }
        }

        private async Task JalankanImporAsync()
        {
            var hasil = HasilImpor;
            if (hasil == null || hasil.JumlahBisaDisimpan == 0) return;

            string pertanyaan =
                $"{hasil.JumlahAkanDisimpan} warga baru dan {hasil.JumlahAkanDiperbarui} pembaruan" +
                (hasil.JumlahGagal > 0 ? $", {hasil.JumlahGagal} baris dilewati" : "") +
                "\n\nData yang sudah ada tidak dihapus. Lanjutkan?";

            if (!await _messageService.ShowConfirmationAsync("Konfirmasi Impor", pertanyaan))
                return;

            IsProsesImpor = true;
            ProgresImpor = 0;
            try
            {
                var progres = new Progress<int>(n => ProgresImpor = n);
                int tersimpan = await _impor.JalankanAsync(hasil, progres);

                _activityLog.Log("WARGA", $"{tersimpan} baris", "Impor", JalurBerkasImpor);

                int gagalTersimpan = hasil.JumlahBisaDisimpan - tersimpan;
                string pesan = gagalTersimpan > 0
                    ? $"{tersimpan} warga tersimpan, {gagalTersimpan} baris gagal disimpan (lihat daftar).\n\n" +
                      $"{JalurBerkasImpor}"
                    : $"{tersimpan} warga berhasil diimpor dari {Path.GetFileName(JalurBerkasImpor)}.\n\n{JalurBerkasImpor}";

                StatusText = $"{tersimpan} warga diimpor.";
                TutupImpor();
                await LoadAsync(true);
                await _messageService.ShowInfoAsync(pesan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan impor warga");
                await _messageService.ShowErrorAsync("Gagal mengimpor warga.\n\n" + ex.Message);
            }
            finally
            {
                IsProsesImpor = false;
                ProgresImpor = 0;
            }
        }

        // =====================================================================
        // Ekspor
        // =====================================================================

        private async Task EksporAsync()
        {
            var sfd = new SaveFileDialog
            {
                Title = "Ekspor Data Warga",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv|JSON Data (*.json)|*.json",
                FileName = $"Data_Warga_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (sfd.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                var data = await _warga.GetWargaForExportAsync(SusunFilter());
                if (data.Count == 0)
                {
                    await _messageService.ShowInfoAsync("Tidak ada warga yang sesuai filter, jadi berkas ekspor tidak dibuat.");
                    return;
                }

                var jumlahSurat = await _warga.GetJumlahSuratSemuaWargaAsync();

                var extension = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                switch (extension)
                {
                    case ".csv": await TulisCsvAsync(data, jumlahSurat, sfd.FileName); break;
                    case ".json": await TulisJsonAsync(data, jumlahSurat, sfd.FileName); break;
                    default: await TulisExcelAsync(data, jumlahSurat, sfd.FileName); break;
                }

                _activityLog.Log("WARGA", $"{data.Count} baris", "Ekspor", sfd.FileName);
                StatusText = $"{data.Count} warga diekspor ke {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync($"Berhasil mengekspor {data.Count} baris warga.\n\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor data warga");
                await _messageService.ShowErrorAsync("Gagal mengekspor data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static readonly string[] HeaderEkspor =
        {
            "No", "NIK", "Nama", "No Kartu Keluarga", "Jenis Kelamin", "Tempat Lahir",
            "Tanggal Lahir", "Agama", "Golongan Darah", "Status Perkawinan", "Pekerjaan",
            "Pendidikan", "Nama Ayah", "Nama Ibu", "Nomor HP", "RT", "RW", "Dusun",
            "Alamat (jalan/kampung)", "Desa", "Kecamatan", "Kabupaten", "Status Warga",
            "Tanggal Status", "Keterangan", "Jumlah Surat"
        };

        private static object?[] BarisEkspor(WargaData w, int nomor, Dictionary<int, int> jumlahSurat)
        {
            jumlahSurat.TryGetValue(w.ID_Warga, out var surat);
            return new object?[]
            {
                nomor, w.NIK, w.Nama, w.NoKK, WargaBaris.NormalisasiJenisKelamin(w.JenisKelamin),
                w.TempatLahir, WargaBaris.FormatTanggalPendek(w.TanggalLahir), w.Agama, w.GolonganDarah,
                w.StatusPerkawinan, w.Pekerjaan, w.Pendidikan, w.NamaAyah, w.NamaIbu, w.NomorHP,
                w.RT, w.RW, w.Dusun, w.AlamatDetail, w.Desa, w.Kecamatan, w.Kabupaten,
                StatusWargaTipe.Normalisasi(w.StatusWarga), w.TanggalStatus, w.KeteranganWarga, surat
            };
        }

        private static async Task TulisExcelAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Data Warga");

            for (int col = 0; col < HeaderEkspor.Length; col++)
            {
                var cell = ws.Cells[1, col + 1];
                cell.Value = HeaderEkspor[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(37, 99, 235));
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }
            ws.Row(1).Height = 26;

            int rowIdx = 2;
            int nomor = 1;
            foreach (var w in data)
            {
                var isi = BarisEkspor(w, nomor++, jumlahSurat);
                for (int c = 0; c < isi.Length; c++)
                {
                    ws.Cells[rowIdx, c + 1].Value = isi[c];
                }

                if (rowIdx % 2 == 1)
                {
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(248, 250, 252));
                }

                rowIdx++;
            }

            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].AutoFitColumns(10, 45);
            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            await package.SaveAsAsync(new FileInfo(path));
        }

        private static async Task TulisCsvAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            // BOM UTF-8: tanpa itu Excel di Windows salah membaca karakter Indonesia.
            await using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
            await writer.WriteLineAsync(string.Join(",", HeaderEkspor.Select(KutipCsv)));
            int nomor = 1;
            foreach (var w in data)
            {
                var isi = BarisEkspor(w, nomor++, jumlahSurat);
                await writer.WriteLineAsync(string.Join(",", isi.Select(v => KutipCsv(Convert.ToString(v) ?? string.Empty))));
            }
        }

        private static async Task TulisJsonAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            var items = data.Select((w, i) => new
            {
                No = i + 1,
                w.NIK,
                w.Nama,
                NoKartuKeluarga = w.NoKK,
                JenisKelamin = WargaBaris.NormalisasiJenisKelamin(w.JenisKelamin),
                w.TempatLahir,
                w.TanggalLahir,
                w.Agama,
                w.GolonganDarah,
                w.StatusPerkawinan,
                w.Pekerjaan,
                w.Pendidikan,
                w.NamaAyah,
                w.NamaIbu,
                w.NomorHP,
                w.RT,
                w.RW,
                w.Dusun,
                w.AlamatDetail,
                w.Desa,
                w.Kecamatan,
                w.Kabupaten,
                StatusWarga = StatusWargaTipe.Normalisasi(w.StatusWarga),
                w.TanggalStatus,
                w.KeteranganWarga,
                JumlahSurat = jumlahSurat.TryGetValue(w.ID_Warga, out var surat) ? surat : 0
            });
            await File.WriteAllTextAsync(path,
                System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Bungkus nilai CSV bila mengandung koma, kutip, atau baris baru.</summary>
        private static string KutipCsv(string nilai)
        {
            if (string.IsNullOrEmpty(nilai)) return string.Empty;
            var perlu = nilai.Contains(',') || nilai.Contains('"') || nilai.Contains('\n') || nilai.Contains('\r');
            if (!perlu) return nilai;
            return "\"" + nilai.Replace("\"", "\"\"") + "\"";
        }
    }
}
