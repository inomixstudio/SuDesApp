using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel halaman Pembaruan Aplikasi.
    ///
    /// Dua jalur pembaruan:
    /// - <b>Pembaruan kecil (tambalan)</b> — bila rilis menyertakan <c>patch.json</c>
    ///   dan versi terpasang cocok, aplikasi hanya mengunduh berkas yang berubah
    ///   (beberapa ratus KB - beberapa MB), memverifikasi SHA-256, mengganti berkas
    ///   tersebut, lalu membuka aplikasi kembali. Tidak perlu installer.
    /// - <b>Pembaruan besar (installer penuh)</b> — dipakai bila rilis ditandai besar,
    ///   tanpa patch.json, atau versi terpasang terlalu jauh dari versi asal tambalan.
    ///
    /// Seluruh kabar dan keputusan (hasil pemeriksaan, konfirmasi pemasangan,
    /// kegagalan unduh) ditampilkan <b>di dalam halaman</b> melalui koleksi
    /// <see cref="Pesan"/> — halaman ini tidak memakai dialog popup sama sekali.
    /// </summary>
    public partial class PembaruanViewModel : ObservableObject
    {
        /// <summary>
        /// Jumlah entri riwayat pembaruan yang ditampilkan di halaman Pembaruan — cukup
        /// satu pemberitahuan terbaru agar halaman tetap ringkas. Riwayat yang lebih
        /// lama tetap tersimpan di berkas berkapasitas
        /// <see cref="RiwayatPembaruanStore.BatasEntri"/>.
        /// </summary>
        public const int MaksRiwayat = 1;

        private readonly UpdateService _updateService;
        private readonly PatchUpdateService _patchService;
        private readonly ILogger<PembaruanViewModel> _logger;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Pembatalan untuk kegiatan yang sedang berjalan (unduhan installer atau
        /// pemasangan tambalan). Dibuat baru setiap kegiatan, jadi tombol Jeda/Batal
        /// tidak "merusak" percobaan berikutnya. Ditautkan ke _cts (umur ViewModel).
        /// </summary>
        private CancellationTokenSource? _ctsOperasi;

        /// <summary>
        /// Unduhan installer yang sedang berjalan atau dijeda. Bagian yang sudah
        /// terunduh disimpan di berkas sementara, jadi objek ini bisa dilanjutkan
        /// kapan saja — termasuk setelah aplikasi dibuka lagi.
        /// </summary>
        private UnduhanBerkasBerlanjut? _unduhanInstaller;

        private bool _unduhanDijeda;
        private string _catatanUnduhan = string.Empty;

        /// <summary>
        /// Pelaksana coba ulang unduhan yang terakhir dipakai — menyimpan berapa kali
        /// koneksi terputus, untuk dilaporkan di lonceng notifikasi.
        /// </summary>
        private UnduhanUlangOtomatis? _pengulangUnduhan;

        /// <summary>
        /// Berapa kali unduhan diulang sendiri saat koneksi terputus beserta jeda yang
        /// bertambah. Bisa diganti pemanggil (mis. pengujian memakai jeda sangat pendek)
        /// supaya tidak perlu menunggu jeda sungguhan.
        /// </summary>
        public KebijakanUnduhanUlang KebijakanUlang { get; set; } = new();

        /// <summary>
        /// Lonceng notifikasi aplikasi. Boleh null (mis. pada uji): unduhan tetap
        /// berjalan, hanya pemberitahuannya yang dilewati.
        /// </summary>
        private readonly NotificationService? _notifications;

        private string _currentVersionText = string.Empty;
        private string _statusText = "Siap memeriksa pembaruan";
        private bool _isChecking;
        private bool _isDownloading;
        private int _downloadPercent;

        private UpdateInfo? _updateInfo;
        private TambalanInfo? _tambalan;
        private RencanaTambalan? _rencana;

        private string _jenisPembaruanText = string.Empty;
        private string _rincianPembaruanText = string.Empty;
        private string _alasanInstallerText = string.Empty;
        private bool _penawaranTersedia;

        public PembaruanViewModel(
            UpdateService updateService,
            PatchUpdateService patchService,
            ILogger<PembaruanViewModel> logger,
            NotificationService? notifications = null)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifications = notifications;

            // Bentuk versi tampilan disamakan dengan status bar ("v2.5.4") — bukan
            // 2.5.4.0 — supaya laporan pengguna mudah dicocokkan antar halaman.
            // GetCurrentVersion() (empat komponen) tetap dipakai untuk membandingkan versi.
            _currentVersionText = $"Versi Saat Ini: {IdentitasAplikasi.VersiDenganPrefiks}";

            Perbaikan = new ObservableCollection<string>();
            Riwayat = new ObservableCollection<BarisRiwayatPembaruan>();
            MuatRiwayat();

            CheckCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking && !IsDownloading && !MenungguJawaban);
            PerbaruiTambalanCommand = new AsyncRelayCommand(TerapkanTambalanAsync, () => BisaTambalan && !IsDownloading && !IsChecking && !MenungguJawaban);
            UnduhInstallerCommand = new AsyncRelayCommand(UnduhInstallerAsync, () => BisaInstaller && !IsDownloading && !IsChecking && !MenungguJawaban);
            JedaLanjutCommand = new RelayCommand(JedaAtauLanjut, () => JedaLanjutTersedia);
            BatalCommand = new RelayCommand(Batal, () => IsChecking || IsDownloading || UnduhanDijeda);
            BersihkanRiwayatCommand = new AsyncRelayCommand(BersihkanRiwayatAsync, () => RiwayatTersedia && !MenungguJawaban);

            // Kartu keputusan yang menunggu jawaban mengunci tombol halaman, jadi
            // keadaan tombol harus diperbarui setiap isi kumpulan kartu berubah.
            Pesan.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(AdaPesan));
                OnPropertyChanged(nameof(MenungguJawaban));
                RaiseCommandStates();
            };
        }

        /// <summary>Versi aplikasi yang sedang dipakai.</summary>
        public Version GetCurrentVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version("1.0.0.0");

        public string CurrentVersionText { get => _currentVersionText; private set => SetProperty(ref _currentVersionText, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (SetProperty(ref _isChecking, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    OnPropertyChanged(nameof(ProgressVisible));
                    OnPropertyChanged(nameof(JedaLanjutTersedia));
                    RaiseCommandStates();
                }
            }
        }

        public int DownloadPercent { get => _downloadPercent; private set => SetProperty(ref _downloadPercent, value); }

        /// <summary>
        /// Area kemajuan tampil selama unduhan berjalan <b>maupun</b> saat dijeda,
        /// supaya persentase yang sudah dicapai tetap terlihat dan tombol Lanjutkan
        /// tidak menghilang.
        /// </summary>
        public bool ProgressVisible => IsDownloading || UnduhanDijeda;

        /// <summary>Benar bila unduhan installer sedang dijeda dan siap dilanjutkan.</summary>
        public bool UnduhanDijeda
        {
            get => _unduhanDijeda;
            private set
            {
                if (!SetProperty(ref _unduhanDijeda, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(ProgressVisible));
                OnPropertyChanged(nameof(TeksTombolJeda));
                OnPropertyChanged(nameof(JedaLanjutTersedia));
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Benar bila tombol Jeda/Lanjutkan berguna sekarang — hanya unduhan installer
        /// yang memakai berkas sementara bisa dijeda dan dilanjutkan.
        /// </summary>
        public bool JedaLanjutTersedia => (IsDownloading && _unduhanInstaller != null) || UnduhanDijeda;

        /// <summary>
        /// Benar bila masih ada sisa unduhan installer yang bisa dilanjutkan tanpa
        /// mengunduh ulang bagian yang sudah ada.
        /// </summary>
        public bool AdaUnduhanTersisa => _unduhanInstaller?.AdaSisa == true;

        /// <summary>Ringkasan sisa unduhan, mis. "6,4 MB dari 15,2 MB (42%)" (kosong bila tidak ada).</summary>
        public string InfoSisaUnduhan => AdaUnduhanTersisa && _unduhanInstaller != null
            ? _unduhanInstaller.Ringkasan
            : string.Empty;

        /// <summary>Keterangan yang bergerak selama unduhan, mis. "6,4 MB dari 15,2 MB (42%)".</summary>
        public string CatatanUnduhan
        {
            get => _catatanUnduhan;
            private set => SetProperty(ref _catatanUnduhan, value ?? string.Empty);
        }

        /// <summary>Label tombol unduhan installer: menawarkan melanjutkan bila ada sisanya.</summary>
        public string TeksTombolInstaller
        {
            get
            {
                if (!AdaUnduhanTersisa || _unduhanInstaller == null)
                {
                    return "Unduh Installer";
                }

                return _unduhanInstaller.Persen > 0
                    ? $"Lanjutkan Unduhan ({_unduhanInstaller.Persen}%)"
                    : "Lanjutkan Unduhan";
            }
        }

        /// <summary>Label tombol jeda/lanjutkan pada area kemajuan.</summary>
        public string TeksTombolJeda => UnduhanDijeda ? "Lanjutkan" : "Jeda";

        /// <summary>Judul penawaran, mis. "Pembaruan kecil tersedia: 2.4.5".</summary>
        public string JenisPembaruanText { get => _jenisPembaruanText; private set => SetProperty(ref _jenisPembaruanText, value); }

        /// <summary>Keterangan singkat ukuran & jumlah berkas, mis. "3 berkas • 1,2 MB".</summary>
        public string RincianPembaruanText { get => _rincianPembaruanText; private set => SetProperty(ref _rincianPembaruanText, value); }

        /// <summary>Alasan memakai installer penuh (kosong bila tambalan bisa dipakai).</summary>
        public string AlasanInstallerText { get => _alasanInstallerText; private set => SetProperty(ref _alasanInstallerText, value); }

        /// <summary>Benar bila ada penawaran pembaruan yang bisa ditampilkan.</summary>
        public bool PenawaranTersedia { get => _penawaranTersedia; private set => SetProperty(ref _penawaranTersedia, value); }

        /// <summary>Daftar perbaikan/penambahan yang dibawa pembaruan ini.</summary>
        public ObservableCollection<string> Perbaikan { get; }

        /// <summary>Riwayat pembaruan yang pernah dicoba dipasang (terbaru dulu).</summary>
        public ObservableCollection<BarisRiwayatPembaruan> Riwayat { get; }

        /// <summary>Benar bila pembaruan bisa dipasang sebagai tambalan kecil.</summary>
        public bool BisaTambalan => _tambalan != null && _rencana != null && _rencana.BisaDipakai;

        /// <summary>Benar bila pembaruan harus memakai installer penuh.</summary>
        public bool BisaInstaller => _updateInfo != null && !BisaTambalan && !string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl);

        public string TeksTombolTambalan => BisaTambalan
            ? $"Perbarui Sekarang (tanpa installer)"
            : "Perbarui Sekarang";

        public ICommand CheckCommand { get; }
        public ICommand PerbaruiTambalanCommand { get; }
        public ICommand UnduhInstallerCommand { get; }

        /// <summary>Menghentikan sementara unduhan yang berjalan, atau meneruskannya.</summary>
        public ICommand JedaLanjutCommand { get; }

        public ICommand BatalCommand { get; }

        /// <summary>Buang seluruh riwayat pembaruan (dengan konfirmasi di dalam halaman).</summary>
        public ICommand BersihkanRiwayatCommand { get; }


    }
}
