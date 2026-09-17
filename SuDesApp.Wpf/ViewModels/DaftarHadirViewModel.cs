using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Baris peserta yang bisa diedit langsung di grid Daftar Hadir.</summary>
    public class DaftarHadirPesertaRow : ObservableObject
    {
        private int _no;
        private string _nama = string.Empty;
        private string _jenisKelamin = string.Empty;
        private string _jabatan = string.Empty;
        private string _nip = string.Empty;
        private string _nik = string.Empty;
        private string _noHp = string.Empty;
        private string _alamat = string.Empty;
        private string _keterangan = string.Empty;

        public int No { get => _no; set => SetProperty(ref _no, value); }
        public string Nama { get => _nama; set => SetProperty(ref _nama, value ?? string.Empty); }

        /// <summary>Jenis kelamin singkat: "L" atau "P".</summary>
        public string JenisKelamin { get => _jenisKelamin; set => SetProperty(ref _jenisKelamin, value ?? string.Empty); }

        public string Jabatan { get => _jabatan; set => SetProperty(ref _jabatan, value ?? string.Empty); }
        public string Nip { get => _nip; set => SetProperty(ref _nip, value ?? string.Empty); }
        public string Nik { get => _nik; set => SetProperty(ref _nik, value ?? string.Empty); }
        public string NoHp { get => _noHp; set => SetProperty(ref _noHp, value ?? string.Empty); }
        public string Alamat { get => _alamat; set => SetProperty(ref _alamat, value ?? string.Empty); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value ?? string.Empty); }

        public bool IsKosong =>
            string.IsNullOrWhiteSpace(Nama) &&
            string.IsNullOrWhiteSpace(JenisKelamin) &&
            string.IsNullOrWhiteSpace(Jabatan) &&
            string.IsNullOrWhiteSpace(Nip) &&
            string.IsNullOrWhiteSpace(Nik) &&
            string.IsNullOrWhiteSpace(NoHp) &&
            string.IsNullOrWhiteSpace(Alamat) &&
            string.IsNullOrWhiteSpace(Keterangan);

        public DaftarHadirPeserta ToPeserta() => new()
        {
            Nama = Nama?.Trim() ?? string.Empty,
            JenisKelamin = JenisKelamin?.Trim() ?? string.Empty,
            Jabatan = Jabatan?.Trim() ?? string.Empty,
            Nip = Nip?.Trim() ?? string.Empty,
            Nik = Nik?.Trim() ?? string.Empty,
            NoHp = NoHp?.Trim() ?? string.Empty,
            Alamat = Alamat?.Trim() ?? string.Empty,
            Keterangan = Keterangan?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    /// Halaman Daftar Hadir: judul bisa diganti lewat textbox, kolom yang dicetak
    /// dipilih via checkbox (No., Nama, Jabatan, dan Tanda Tangan aktif default),
    /// hari &amp; tanggal terisi otomatis saat cetak, kop surat dirender di PDF,
    /// dan blok tanda tangan Kepala Desa bisa dinyalakan/dimatikan.
    /// </summary>
    public class DaftarHadirViewModel : ObservableObject
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly DaftarHadirGenerator _pdfGenerator;
        private readonly AppConfig _appConfig;
        private readonly ILogger<DaftarHadirViewModel> _logger;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;

        private string _judulSurat = "DAFTAR HADIR";
        private string _pukul = "09.00 WIB s/d Selesai";
        private string _tempat = string.Empty;
        private string _hariTanggal = string.Empty;

        /// <summary>Penanda saat nilai diisi program (bukan oleh pengguna).</summary>
        private bool _mengisiHariTanggal;

        /// <summary>True bila pengguna pernah mengubah Hari/Tanggal sendiri.</summary>
        private bool _hariTanggalDisentuh;
        private bool _tampilkanJenisKelamin;
        private bool _tampilkanJabatan = true;
        private bool _tampilkanNip;
        private bool _tampilkanNik;
        private bool _tampilkanNoHp;
        private bool _tampilkanAlamat;
        private bool _tampilkanKeterangan;
        private bool _tampilkanFooterKepalaDesa = true;
        private bool _isBusy;
        private DaftarHadirPesertaRow? _selectedPeserta;
        private DesaData _desa = new();

        public DaftarHadirViewModel(
            IUnitOfWork unitOfWork,
            DaftarHadirGenerator pdfGenerator,
            AppConfig appConfig,
            ILogger<DaftarHadirViewModel> logger,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            IMessageService messageService)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _pdfGenerator = pdfGenerator ?? throw new ArgumentNullException(nameof(pdfGenerator));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));

            CetakCommand = new AsyncRelayCommand(CetakAsync);
            TambahPesertaCommand = new RelayCommand(AddPeserta);
            HapusPesertaCommand = new RelayCommand(RemovePeserta, () => SelectedPeserta != null);
            BatalCommand = new RelayCommand(() => RequestClose?.Invoke());

            // Hari/Tanggal terisi tanggal hari ini, tetapi tetap berupa textbox
            // sehingga bisa diganti; tombol "Hari ini" mengembalikannya.
            HariIniCommand = new RelayCommand(() => IsiHariTanggalHariIni());
            IsiHariTanggalHariIni();

            // Beberapa baris awal agar grid langsung bisa diisi.
            for (int i = 0; i < 5; i++)
            {
                Peserta.Add(new DaftarHadirPesertaRow());
            }
            Renumber();

            _ = InitializeAsync();
        }

        /// <summary>Diminta saat pengguna menekan Batal — host kembali ke halaman default.</summary>
        public event Action? RequestClose;

        // ==== Identitas dokumen ====
        /// <summary>
        /// Judul surat yang bisa diganti bebas oleh pengguna — boleh beberapa baris
        /// (tombol Enter) dan akan dicetak sebagai judul berbaris.
        /// </summary>
        public string JudulSurat { get => _judulSurat; set => SetProperty(ref _judulSurat, value ?? string.Empty); }

        /// <summary>Waktu kegiatan, mis. "09.00 WIB" — bisa diedit. Kosong berarti tidak dicetak.</summary>
        public string Pukul { get => _pukul; set => SetProperty(ref _pukul, value ?? string.Empty); }

        /// <summary>Tempat kegiatan, mis. "Aula Kantor Desa Sumberjaya" — bisa diedit, terisi otomatis dari nama desa.</summary>
        public string Tempat { get => _tempat; set => SetProperty(ref _tempat, value ?? string.Empty); }

        /// <summary>
        /// Hari &amp; tanggal yang tercetak — terisi tanggal hari ini saat halaman
        /// dibuka, tetapi bisa diedit bebas. Dikosongkan berarti barisnya tidak dicetak.
        /// </summary>
        public string HariTanggal
        {
            get => _hariTanggal;
            set
            {
                if (SetProperty(ref _hariTanggal, value ?? string.Empty) && !_mengisiHariTanggal)
                {
                    _hariTanggalDisentuh = true;
                }
            }
        }

        /// <summary>Isi ulang Hari/Tanggal dengan tanggal hari ini.</summary>
        public RelayCommand HariIniCommand { get; }

        private void IsiHariTanggalHariIni()
        {
            _mengisiHariTanggal = true;
            HariTanggal = DaftarHadirData.HariTanggalIndo(DateTime.Now);
            _hariTanggalDisentuh = false;
            _mengisiHariTanggal = false;
        }

        // ==== Pilihan kolom yang dicetak ====
        // No., Nama, dan Tanda Tangan wajib ada pada dokumen: checkbox-nya dikunci
        // tercentang (tidak bisa dimatikan) sehingga tidak punya setter.

        /// <summary>Kolom No. — wajib dicetak.</summary>
        public bool TampilkanNo => true;

        /// <summary>Kolom Nama — wajib dicetak.</summary>
        public bool TampilkanNama => true;

        /// <summary>Kolom jenis kelamin singkat (L/P) — opsional.</summary>
        public bool TampilkanJenisKelamin { get => _tampilkanJenisKelamin; set => SetProperty(ref _tampilkanJenisKelamin, value); }

        public bool TampilkanJabatan { get => _tampilkanJabatan; set => SetProperty(ref _tampilkanJabatan, value); }

        /// <summary>Kolom Tanda Tangan — wajib dicetak.</summary>
        public bool TampilkanTandaTangan => true;

        public bool TampilkanNip { get => _tampilkanNip; set => SetProperty(ref _tampilkanNip, value); }
        public bool TampilkanNik { get => _tampilkanNik; set => SetProperty(ref _tampilkanNik, value); }
        public bool TampilkanNoHp { get => _tampilkanNoHp; set => SetProperty(ref _tampilkanNoHp, value); }
        public bool TampilkanAlamat { get => _tampilkanAlamat; set => SetProperty(ref _tampilkanAlamat, value); }
        public bool TampilkanKeterangan { get => _tampilkanKeterangan; set => SetProperty(ref _tampilkanKeterangan, value); }

        /// <summary>Cetak blok tanda tangan Kepala Desa di bagian bawah atau tidak.</summary>
        public bool TampilkanFooterKepalaDesa
        {
            get => _tampilkanFooterKepalaDesa;
            set => SetProperty(ref _tampilkanFooterKepalaDesa, value);
        }

        /// <summary>Pilihan jenis kelamin singkat untuk kolom L/P.</summary>
        private static readonly string[] ValidJenisKelaminOptions = { "L", "P" };

        /// <summary>Daftar pilihan kolom Jenis Kelamin (L/P) pada grid peserta.</summary>
        public IReadOnlyList<string> JenisKelaminOptions => ValidJenisKelaminOptions;

        public ObservableCollection<DaftarHadirPesertaRow> Peserta { get; } = new();

        public DaftarHadirPesertaRow? SelectedPeserta
        {
            get => _selectedPeserta;
            set
            {
                if (SetProperty(ref _selectedPeserta, value))
                {
                    HapusPesertaCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public AsyncRelayCommand CetakCommand { get; }
        public RelayCommand TambahPesertaCommand { get; }
        public RelayCommand HapusPesertaCommand { get; }
        public RelayCommand BatalCommand { get; }

        private async Task InitializeAsync()
        {
            try
            {
                _desa = await _unitOfWork.DesaRepository.GetInfoDesaAsync() ?? new DesaData();

                // Tempat disesuaikan nama desa dari Pengaturan Surat → Desa
                // (mis. "Aula Kantor Desa Sumberjaya") — hanya diisi bila pengguna
                // belum mengetik apa pun, dan tetap bisa diubah bebas.
                if (string.IsNullOrWhiteSpace(Tempat) && !string.IsNullOrWhiteSpace(_desa.NamaDesa))
                {
                    Tempat = "Aula Kantor Desa " + _desa.NamaDesa;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data desa untuk Daftar Hadir");
            }
        }

        private void AddPeserta()
        {
            Peserta.Add(new DaftarHadirPesertaRow());
            Renumber();
        }

        private void RemovePeserta()
        {
            if (SelectedPeserta == null) return;
            int index = Peserta.IndexOf(SelectedPeserta);
            if (index < 0) return;
            Peserta.RemoveAt(index);
            Renumber();
            if (Peserta.Count > 0)
            {
                SelectedPeserta = Peserta[Math.Min(index, Peserta.Count - 1)];
            }
        }

        private void Renumber()
        {
            for (int i = 0; i < Peserta.Count; i++)
            {
                Peserta[i].No = i + 1;
            }
        }

        private async Task CetakAsync()
        {
            // No., Nama, dan Tanda Tangan selalu ikut dicetak, jadi dokumen
            // tidak pernah kosong kolom.
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var data = BuildData();
                using var memoryStream = new MemoryStream();
                _pdfGenerator.GeneratePdf(memoryStream, data);
                var pdfBytes = memoryStream.ToArray();
                if (pdfBytes.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal menghasilkan PDF atau PDF kosong.");
                    return;
                }

                var outputFolder = _appConfig.PdfOutputPath;
                Directory.CreateDirectory(outputFolder);
                var outputPath = Path.Combine(
                    outputFolder, $"Daftar_Hadir_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(outputPath, pdfBytes);

                string judul = string.IsNullOrWhiteSpace(JudulSurat) ? "Daftar Hadir" : JudulSurat.Trim();
                _navigation.Navigate(_previewFactory($"Daftar Hadir — {judul}", outputPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF Daftar Hadir");
                await _messageService.ShowErrorAsync("Gagal membuat PDF Daftar Hadir: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private DaftarHadirData BuildData()
        {
            // Halaman yang dibiarkan terbuka semalaman tidak ikut basi: bila hari/tanggal
            // belum pernah diedit pengguna, isinya disegarkan ke tanggal hari ini.
            if (!_hariTanggalDisentuh)
            {
                IsiHariTanggalHariIni();
            }

            return new DaftarHadirData
        {
            Judul = string.IsNullOrWhiteSpace(JudulSurat) ? "DAFTAR HADIR" : JudulSurat.Trim(),
            HariTanggal = HariTanggal?.Trim() ?? string.Empty,
            TanggalCetak = DateTime.Now,
            Pukul = Pukul?.Trim() ?? string.Empty,
            Tempat = Tempat?.Trim() ?? string.Empty,
            Peserta = Peserta.Select(p => p.ToPeserta()).ToList(),
            TampilkanNo = TampilkanNo,
            TampilkanNama = TampilkanNama,
            TampilkanJenisKelamin = TampilkanJenisKelamin,
            TampilkanJabatan = TampilkanJabatan,
            TampilkanTandaTangan = TampilkanTandaTangan,
            TampilkanNip = TampilkanNip,
            TampilkanNik = TampilkanNik,
            TampilkanNoHp = TampilkanNoHp,
            TampilkanAlamat = TampilkanAlamat,
            TampilkanKeterangan = TampilkanKeterangan,
            TampilkanFooterKepalaDesa = TampilkanFooterKepalaDesa,
            Desa = _desa
        };
        }
    }
}
