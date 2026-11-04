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
        /// <summary>Konstanta jenis (SuratConstants) — nilai yang diterima BukaSurat.</summary>
        public string Jenis { get; init; } = string.Empty;

        /// <summary>Nama tampilan dari MenuSuratKatalog.</summary>
        public string Nama { get; init; } = string.Empty;
        public string Ikon { get; init; } = "\uD83D\uDCC4";
        public int Jumlah { get; init; }
        public string JumlahTeks => Jumlah > 0 ? $"{Jumlah} surat" : "belum pernah";
        public ICommand? Buka { get; init; }
    }

    /// <summary>
    /// Halaman pembuka (Beranda): ringkasan pekerjaan surat desa — statistik dari
    /// register surat (tabel Surat), jumlah warga terdata, pintasan jenis surat yang
    /// paling sering dipakai, aktivitas terbaru, dan aksi cepat ke halaman lain.
    /// Halaman ini selalu tampil pertama setelah login sehingga area konten tidak
    /// kosong.
    /// </summary>
    public class BerandaViewModel : ObservableObject
    {
        private readonly ISuratRepository _surat;
        private readonly IWargaRepository _warga;
        private readonly ILogger<BerandaViewModel> _logger;

        /// <summary>Diminta membuat surat jenis tertentu (diteruskan MainWindow).</summary>
        public event Action<string>? BuatSuratDiminta;

        /// <summary>Diminta membuka halaman lain: REGISTER, RIWAYAT, PENGATURAN, TEMPLATE.</summary>
        public event Action<string>? BukaHalamanDiminta;

        public BerandaViewModel(
            ISuratRepository surat,
            IWargaRepository warga,
            ILogger<BerandaViewModel> logger)
        {
            _surat = surat ?? throw new ArgumentNullException(nameof(surat));
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
        public ObservableCollection<BarisAktivitasRegister> Terbaru { get; } = new();

        public ICommand MuatUlangCommand { get; }
        public ICommand BuatSuratCommand { get; }
        public ICommand BukaHalamanCommand { get; }

        /// <summary>Jumlah pintasan yang ditampilkan pada kartu Beranda.</summary>
        private const int JumlahPintasan = 6;

        /// <summary>
        /// Set bawaan bila register masih kosong: enam jenis yang paling lazim
        /// dipakai desa, agar menu stabil sebelum ada data.
        /// </summary>
        private static readonly string[] PintasanBawaan =
        {
            SuratConstants.SKD_UMUM,
            SuratConstants.SKTM,
            SuratConstants.SKU,
            SuratConstants.DOMISILI_WARGA,
            SuratConstants.PENGANTAR_SKCK,
            SuratConstants.KEMATIAN
        };

        /// <summary>
        /// Susun pintasan cepat dari katalog menu (satu sumber dengan sidebar)
        /// dan hitungan register: jenis paling sering dipakai di depan, seri
        /// diurut menurut urut katalog. Register kosong → set bawaan.
        /// Label diambil dari katalog sehingga tidak bisa kedaluwarsa seperti
        /// "SKD UMUM" dulu.
        /// </summary>
        internal static List<(string Jenis, string Nama, string Ikon, int Jumlah)> SusunPintasan(
            IReadOnlyDictionary<string, int> jumlahPerJenis)
        {
            int Hitung(string jenis) =>
                jumlahPerJenis.TryGetValue(jenis, out var n) ? n : 0;

            IEnumerable<PilihanSurat> kandidat =
                jumlahPerJenis.Values.All(n => n <= 0)
                    ? PintasanBawaan.Select(jenis => MenuSuratKatalog.Semua.First(p => p.Jenis == jenis))
                    : MenuSuratKatalog.Semua.OrderByDescending(p => Hitung(p.Jenis));

            return kandidat
                .Take(JumlahPintasan)
                .Select(p => (p.Jenis, p.Label, IkonJenis(p.Jenis), Hitung(p.Jenis)))
                .ToList();
        }

        /// <summary>Emoji pintasan; jenis tanpa ikon khusus memakai ikon surat.</summary>
        private static string IkonJenis(string jenis) => jenis switch
        {
            SuratConstants.SKD_UMUM => "\uD83D\uDCC4",
            SuratConstants.SKTM => "\uD83D\uDCB0",
            SuratConstants.SKU => "\uD83C\uDFEA",
            SuratConstants.DOMISILI_WARGA => "\uD83C\uDFE0",
            SuratConstants.PENGANTAR_SKCK => "\uD83D\uDD0E",
            SuratConstants.KEMATIAN => "\uD83D\uDD4A",
            _ => "\uD83D\uDCC4"
        };

        /// <summary>Muat statistik, pintasan, dan aktivitas terbaru dari register & data warga.</summary>
        public async Task MuatAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            PesanStatus = "Memuat ringkasan…";

            try
            {
                // Ringkasan register dihitung di database (COUNT/GROUP BY + 5 baris
                // terbaru). Sumbernya tabel Surat — bukan buku agenda masuk/keluar —
                // supaya surat yang dibuat lewat Buat Surat benar-benar muncul di sini.
                var ringkasan = await _surat.GetRingkasanRegisterAsync(5);

                TotalSurat = ringkasan.Total;
                SuratHariIni = ringkasan.HariIni;
                SuratBulanIni = ringkasan.BulanIni;

                IsiTerbaru(ringkasan.Terbaru);
                IsiPintasan(ringkasan.PerJenis);
                TotalWarga = await _warga.CountWargaAsync();

                WaktuMuat = DateTime.Now.ToString("HH:mm:ss");
                PesanStatus = ringkasan.Total == 0
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

        private void IsiTerbaru(List<BarisAktivitasRegister> surat)
        {
            // Baris terbaru sudah diurutkan oleh database (Id menurun).
            Terbaru.Clear();
            foreach (var item in surat)
            {
                Terbaru.Add(new BarisAktivitasRegister
                {
                    TanggalSurat = item.TanggalSurat,
                    JenisSurat = LabelJenis(item.JenisSurat),
                    NomorSurat = item.NomorSurat,
                    Perihal = item.Perihal
                });
            }
        }

        /// <summary>Nama tampilan jenis surat untuk daftar aktivitas Beranda.</summary>
        private static string LabelJenis(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis)) return "-";

            var pilihan = MenuSuratKatalog.Semua.FirstOrDefault(p => p.Jenis == namaJenis);
            if (pilihan != null) return pilihan.Label;

            return namaJenis switch
            {
                SuratConstants.REKENING_KORAN => "Permohonan Rekening Koran",
                SuratConstants.TEMPLATE_SURAT => "Template Surat",
                _ => namaJenis.Replace('_', ' ')
            };
        }

        private void IsiPintasan(IReadOnlyDictionary<string, int> jumlahPerJenis)
        {
            Pintasan.Clear();
            foreach (var (jenis, nama, ikon, jumlah) in SusunPintasan(jumlahPerJenis))
            {
                Pintasan.Add(new PintasanSurat
                {
                    Jenis = jenis,
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
