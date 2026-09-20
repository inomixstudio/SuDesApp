using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu pintasan jenis surat pada halaman Beranda.</summary>
    public class PintasanSurat
    {
        public string Nama { get; init; } = string.Empty;
        public string Ikon { get; init; } = "\uD83D\uDCC4";
        public int Jumlah { get; init; }
        public string JumlahTeks => Jumlah > 0 ? $"{Jumlah} surat" : "belum pernah";
        public ICommand? Buka { get; init; }
    }

    /// <summary>
    /// Halaman pembuka (Beranda): ringkasan pekerjaan surat desa — statistik surat
    /// dari register keluar/masuk, jumlah warga terdata, pintasan jenis surat yang
    /// paling sering dipakai, aktivitas terbaru, dan aksi cepat ke halaman lain.
    /// Halaman ini selalu tampil pertama setelah login sehingga area konten tidak
    /// kosong.
    /// </summary>
    public class BerandaViewModel : ObservableObject
    {
        private readonly IArsipSuratRepository _arsipSurat;
        private readonly IWargaRepository _warga;
        private readonly ILogger<BerandaViewModel> _logger;

        /// <summary>Diminta membuat surat jenis tertentu (diteruskan MainWindow).</summary>
        public event Action<string>? BuatSuratDiminta;

        /// <summary>Diminta membuka halaman lain: REGISTER, RIWAYAT, PENGATURAN, TEMPLATE.</summary>
        public event Action<string>? BukaHalamanDiminta;

        public BerandaViewModel(
            IArsipSuratRepository arsipSurat,
            IWargaRepository warga,
            ILogger<BerandaViewModel> logger)
        {
            _arsipSurat = arsipSurat ?? throw new ArgumentNullException(nameof(arsipSurat));
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            MuatUlangCommand = new AsyncRelayCommand(MuatAsync);
            BuatSuratCommand = new RelayCommand<string>(BukaSurat);
            BukaHalamanCommand = new RelayCommand<string>(BukaHalaman);
        }

        // ----- Identitas halaman -----
        public string Sapaan
        {
            get
            {
                var jam = DateTime.Now.Hour;
                var waktu = jam switch
                {
                    < 11 => "Selamat pagi",
                    < 15 => "Selamat siang",
                    < 18 => "Selamat sore",
                    _ => "Selamat malam"
                };
                var nama = LoginViewModel.CurrentUserName;
                return string.IsNullOrWhiteSpace(nama) ? waktu : $"{waktu}, {nama}";
            }
        }

        public string TanggalTeks =>
            DateTime.Now.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));

        // ----- Statistik -----
        private int _suratHariIni;
        public int SuratHariIni { get => _suratHariIni; private set => SetProperty(ref _suratHariIni, value); }

        private int _suratBulanIni;
        public int SuratBulanIni { get => _suratBulanIni; private set => SetProperty(ref _suratBulanIni, value); }

        private int _totalSurat;
        public int TotalSurat { get => _totalSurat; private set => SetProperty(ref _totalSurat, value); }

        private int _totalWarga;
        public int TotalWarga { get => _totalWarga; private set => SetProperty(ref _totalWarga, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _pesanStatus = "Muat ulang kapan saja bila data register berubah.";
        public string PesanStatus { get => _pesanStatus; private set => SetProperty(ref _pesanStatus, value); }

        private string _waktuMuat = "-";
        public string WaktuMuat { get => _waktuMuat; private set => SetProperty(ref _waktuMuat, value); }

        // ----- Daftar -----
        public ObservableCollection<PintasanSurat> Pintasan { get; } = new();
        public ObservableCollection<SuratKeluarMasukData> Terbaru { get; } = new();

        public ICommand MuatUlangCommand { get; }
        public ICommand BuatSuratCommand { get; }
        public ICommand BukaHalamanCommand { get; }

        /// <summary>Jenis surat yang ditawarkan sebagai pintasan (kunci sama dengan menu).</summary>
        private static readonly (string Nama, string Ikon)[] JenisPopuler =
        {
            ("SKD UMUM", "\uD83D\uDCC4"),
            ("SKTM", "\uD83D\uDCB0"),
            ("SKU", "\uD83C\uDFEA"),
            ("DOMISILI WARGA", "\uD83C\uDFE0"),
            ("PENGANTAR SKCK", "\uD83D\uDD0E"),
            ("KEMATIAN", "\uD83D\uDD4A")
        };

        /// <summary>Muat statistik, pintasan, dan aktivitas terbaru dari register & data warga.</summary>
        public async Task MuatAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            PesanStatus = "Memuat ringkasan…";

            try
            {
                var surat = await MuatSuratAsync();
                var hariIni = DateTime.Today;

                TotalSurat = surat.Count;
                SuratHariIni = surat.Count(s => s.TanggalSurat.Date == hariIni);
                SuratBulanIni = surat.Count(s =>
                    s.TanggalSurat.Year == hariIni.Year && s.TanggalSurat.Month == hariIni.Month);

                IsiTerbaru(surat);
                IsiPintasan(surat);
                TotalWarga = await HitungWargaAsync();

                WaktuMuat = DateTime.Now.ToString("HH:mm:ss");
                PesanStatus = surat.Count == 0
                    ? "Register surat masih kosong — surat yang dibuat akan tampil di sini."
                    : $"Ringkasan diperbarui pukul {WaktuMuat}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat ringkasan Beranda");
                PesanStatus = "Ringkasan tidak bisa dimuat: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<List<SuratKeluarMasukData>> MuatSuratAsync()
        {
            try
            {
                var data = await _arsipSurat.GetAllAsync();
                return data ?? new List<SuratKeluarMasukData>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca register surat untuk Beranda");
                return new List<SuratKeluarMasukData>();
            }
        }

        private async Task<int> HitungWargaAsync()
        {
            try
            {
                var semua = await _warga.GetAllWargaAsync();
                return semua?.Count() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghitung warga untuk Beranda");
                return 0;
            }
        }

        private void IsiTerbaru(List<SuratKeluarMasukData> surat)
        {
            Terbaru.Clear();
            foreach (var item in surat
                         .OrderByDescending(s => s.TanggalSurat)
                         .ThenByDescending(s => s.IdBarisExcel)
                         .Take(5))
            {
                Terbaru.Add(item);
            }
        }

        private void IsiPintasan(List<SuratKeluarMasukData> surat)
        {
            Pintasan.Clear();
            foreach (var (nama, ikon) in JenisPopuler)
            {
                // Cocokkan longgar: jenis di register bisa berbentuk "SKD" / "Surat
                // Keterangan Domisili", jadi dibandingkan pada kata kunci pertamanya.
                var kunci = nama.Split(' ')[0];
                var jumlah = surat.Count(s =>
                    !string.IsNullOrWhiteSpace(s.JenisSurat) &&
                    s.JenisSurat.ToUpperInvariant().Contains(kunci, StringComparison.Ordinal));

                Pintasan.Add(new PintasanSurat
                {
                    Nama = nama,
                    Ikon = ikon,
                    Jumlah = jumlah,
                    Buka = new RelayCommand<string>(BukaSurat)
                });
            }
        }

        private void BukaSurat(string? jenis)
        {
            if (string.IsNullOrWhiteSpace(jenis)) return;
            BuatSuratDiminta?.Invoke(jenis);
        }

        private void BukaHalaman(string? halaman)
        {
            if (string.IsNullOrWhiteSpace(halaman)) return;
            BukaHalamanDiminta?.Invoke(halaman);
        }
    }
}
