using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

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
    public partial class WargaViewModel : ObservableObject
    {
        /// <summary>Budaya tampilan angka untuk kartu statistik. Dipatok ke
        /// id-ID supaya jumlah dan persentase selalu tampil sama di setiap
        /// komputer, apa pun setelan locale Windows yang dipakai.</summary>
        private static readonly CultureInfo Budaya = CultureInfo.GetCultureInfo("id-ID");

        private readonly IWargaRepository _warga;
        private readonly IDesaRepository _desa;
        private readonly IImporWargaService _impor;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<WargaViewModel> _logger;
        private readonly AppConfig _appConfig;
        private readonly DaftarWargaGenerator _daftarGenerator;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;

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

        private readonly PenjagaLisensi? _penjaga;

        public WargaViewModel(
            IWargaRepository warga,
            IDesaRepository desa,
            IImporWargaService impor,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<WargaViewModel> logger,
            AppConfig appConfig,
            DaftarWargaGenerator daftarGenerator,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            PenjagaLisensi? penjaga = null)
        {
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _desa = desa ?? throw new ArgumentNullException(nameof(desa));
            _impor = impor ?? throw new ArgumentNullException(nameof(impor));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _daftarGenerator = daftarGenerator ?? throw new ArgumentNullException(nameof(daftarGenerator));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _penjaga = penjaga;

            MuatUlangCommand = new AsyncRelayCommand(() => LoadAsync(true));
            RefreshCommand = new AsyncRelayCommand(() => LoadAsync(true));
            CariCommand = new AsyncRelayCommand(() => { Halaman = 1; return LoadAsync(true); });
            TambahCommand = new AsyncRelayCommand(BukaFormTambahAsync);
            UbahCommand = new AsyncRelayCommand<WargaBaris>(BukaFormUbahAsync, b => b != null);
            BatalFormCommand = new RelayCommand(TutupForm);
            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(FormNik));
            UbahStatusCommand = new AsyncRelayCommand<UbahStatusRequest>(UbahStatusAsync, r => r != null);
            EksporCommand = new AsyncRelayCommand(EksporAsync, () => !IsBusy);
            CetakPdfCommand = new AsyncRelayCommand(CetakPdfAsync, () => !IsBusy);
            BuatTemplateCommand = new AsyncRelayCommand(BuatTemplateAsync, () => !IsBusy);
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


    }
}
