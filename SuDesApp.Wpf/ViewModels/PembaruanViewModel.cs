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
    public class PembaruanViewModel : ObservableObject
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

        // =====================================================================
        // Pemberitahuan inline (pengganti dialog popup)
        // =====================================================================

        /// <summary>
        /// Pemberitahuan yang tampil di dalam halaman, terbaru di atas. Memakai kumpulan
        /// kartu bersama <see cref="KumpulanPesanInline"/> supaya bentuk dan perilakunya
        /// sama dengan halaman lain yang juga meninggalkan dialog popup.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        /// <summary>Benar bila ada pemberitahuan untuk ditampilkan.</summary>
        public bool AdaPesan => Pesan.Ada;

        /// <summary>Benar bila ada kartu keputusan yang menunggu jawaban pengguna.</summary>
        public bool MenungguJawaban => Pesan.MenungguJawaban;

        /// <summary>
        /// Tambah pemberitahuan inline. Kartu dengan <paramref name="kunci"/> yang sama
        /// mengganti kartu sebelumnya supaya kabar sejenis tidak menumpuk (mis. beberapa
        /// kali menekan Periksa Pembaruan hanya menyisakan hasil terakhir).
        /// </summary>
        private PesanInline TampilkanPesan(JenisPesanInline jenis, string judul, string isi, string kunci = "")
            => Pesan.Tampilkan(jenis, judul, isi, kunci);

        /// <summary>Tutup satu pemberitahuan (tombol ✕ pada kartunya).</summary>
        public void TutupPesan(PesanInline? pesan) => Pesan.Tutup(pesan);

        /// <summary>
        /// Buang seluruh kabar lama — dipakai saat memulai pemeriksaan baru.
        /// Kartu keputusan yang masih menunggu jawaban tidak ikut dibuang.
        /// </summary>
        private void BersihkanPesan() => Pesan.Bersihkan();

        /// <summary>
        /// Tanyakan keputusan penting di dalam halaman (bukan popup) dan tunggu
        /// jawabannya. Selama menunggu, tombol-tombol lain dinonaktifkan supaya
        /// kartu keputusan tidak hilang begitu saja.
        /// </summary>
        private async Task<bool> TanyaAsync(
            string judul, string rincian, string pertanyaan, string teksYa, string teksTidak, string kunci = "konfirmasi")
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var kartu = TampilkanPesan(JenisPesanInline.Pertanyaan, judul, rincian, kunci);
            kartu.SiapkanPertanyaan(pertanyaan, teksYa, teksTidak, jawaban => tcs.TrySetResult(jawaban));

            try
            {
                return await tcs.Task;
            }
            finally
            {
                TutupPesan(kartu);
            }
        }

        // =====================================================================
        // Riwayat pembaruan
        // =====================================================================

        /// <summary>Benar bila ada riwayat pembaruan untuk ditampilkan.</summary>
        public bool RiwayatTersedia { get => _riwayatTersedia; private set => SetProperty(ref _riwayatTersedia, value); }

        private bool _riwayatTersedia;
        private int _jumlahRiwayatTersimpan;

        /// <summary>
        /// Keterangan jumlah entri di kanan judul: halaman hanya menampilkan
        /// <see cref="MaksRiwayat"/> terbaru, sementara sisanya tetap tersimpan di
        /// berkas — angka ini yang menjelaskan tombol Bersihkan Riwayat.
        /// </summary>
        public string RingkasanRiwayat
        {
            get
            {
                if (RiwayatTidakTerbaca)
                {
                    return "berkas tidak terbaca";
                }

                if (_jumlahRiwayatTersimpan <= MaksRiwayat)
                {
                    return _jumlahRiwayatTersimpan.ToString() + " entri";
                }

                return $"{Riwayat.Count} dari {_jumlahRiwayatTersimpan} entri tersimpan";
            }
        }

        /// <summary>
        /// Benar bila berkas riwayat ada tetapi isinya tidak dapat dibaca — daftarnya
        /// kosong, jadi halaman menampilkan penjelasan singkat sebagai ganti kartu entri.
        /// </summary>
        public bool RiwayatTidakTerbaca { get => _riwayatTidakTerbaca; private set => SetProperty(ref _riwayatTidakTerbaca, value); }

        private bool _riwayatTidakTerbaca;

        /// <summary>Muat (atau muat ulang) riwayat dari disk — dipanggil saat halaman dibuka.</summary>
        public void MuatRiwayat()
        {
            Riwayat.Clear();

            try
            {
                var tersimpan = RiwayatPembaruanStore.Muat();
                _jumlahRiwayatTersimpan = tersimpan.Count;

                // Halaman hanya menampilkan sejumlah entri terbaru (lihat MaksRiwayat);
                // seluruhnya tetap tersimpan di berkas RiwayatPembaruanStore.
                foreach (var entri in tersimpan.Take(MaksRiwayat))
                {
                    Riwayat.Add(new BarisRiwayatPembaruan(entri));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan tidak dapat dimuat.");
                _jumlahRiwayatTersimpan = 0;
            }

            // Bila berkasnya ada tetapi isinya tidak terbaca, kartu tetap ditampilkan
            // supaya pengguna punya jalan membuang berkas itu dari halaman ini.
            RiwayatTidakTerbaca = Riwayat.Count == 0 && RiwayatPembaruanStore.AdaBerkas;
            RiwayatTersedia = Riwayat.Count > 0 || RiwayatTidakTerbaca;
            OnPropertyChanged(nameof(RingkasanRiwayat));
            RaiseCommandStates();
        }

        /// <summary>
        /// Buang seluruh riwayat pembaruan (berkas riwayat dihapus). Bersifat permanen,
        /// jadi keputusannya ditanyakan lewat kartu di dalam halaman — bukan dialog
        /// terpisah — dan hanya menyentuh riwayat, tidak menyentuh data desa.
        /// </summary>
        private async Task BersihkanRiwayatAsync()
        {
            if (!RiwayatTersedia)
            {
                return;
            }

            bool lanjut = await TanyaAsync(
                "Bersihkan Riwayat Pembaruan",
                $"Seluruh {_jumlahRiwayatTersimpan} catatan riwayat pembaruan akan dibuang dari komputer ini. " +
                "Surat, database, template, dan pengaturan Anda tidak ikut terhapus.",
                "Buang seluruh riwayat sekarang?",
                "Ya, bersihkan",
                "Batal",
                "bersihkan-riwayat");

            if (!lanjut)
            {
                StatusText = "Riwayat pembaruan tidak dibuang.";
                return;
            }

            try
            {
                RiwayatPembaruanStore.Kosongkan();
                MuatRiwayat();

                TampilkanPesan(
                    JenisPesanInline.Sukses,
                    "Riwayat pembaruan dibersihkan",
                    "Riwayat pembaruan sudah dibuang. Catatan baru akan muncul lagi di sini setiap ada pembaruan yang dicoba dipasang.",
                    "bersihkan-riwayat");
                StatusText = "Riwayat pembaruan sudah dibuang.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membersihkan riwayat pembaruan");
                TampilkanPesan(
                    JenisPesanInline.Galat,
                    "Riwayat pembaruan tidak dapat dibuang",
                    ex.Message,
                    "bersihkan-riwayat");
            }
        }

        /// <summary>Catat percobaan pemasangan yang gagal sebelum skrip penerap sempat menulis hasilnya.</summary>
        private void CatatRiwayatGagal(string versi, string jenis, string pesan)
        {
            try
            {
                RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                {
                    Versi = versi,
                    Jenis = jenis,
                    JumlahBerkas = 0,
                    Berhasil = false,
                    Pesan = pesan,
                    DariVersi = GetCurrentVersion().ToString(),
                    Waktu = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan gagal dicatat.");
            }
        }

        /// <summary>Baris siap tampil pada daftar riwayat pembaruan.</summary>
        public class BarisRiwayatPembaruan
        {
            public BarisRiwayatPembaruan(EntriRiwayatPembaruan entri)
            {
                Versi = string.IsNullOrWhiteSpace(entri.Versi) ? "-" : entri.Versi;
                Tanggal = entri.Waktu.ToString("dd MMM yyyy HH:mm");
                Hasil = entri.Berhasil ? "Berhasil" : "Gagal";
                Berhasil = entri.Berhasil;
                Pesan = string.IsNullOrWhiteSpace(entri.Pesan) ? "-" : entri.Pesan;

                Jenis = entri.Jenis switch
                {
                    "Installer" => "Installer penuh",
                    "Tambalan" => "Pembaruan kecil",
                    var lain => lain
                };

                var dari = string.IsNullOrWhiteSpace(entri.DariVersi) ? string.Empty : $" dari {entri.DariVersi}";
                Berkas = entri.JumlahBerkas > 0
                    ? $"{entri.JumlahBerkas} berkas{dari}"
                    : (string.IsNullOrWhiteSpace(entri.DariVersi) ? "-" : $"dari versi {entri.DariVersi}");
            }

            public string Versi { get; }
            public string Jenis { get; }
            public string Tanggal { get; }
            public string Hasil { get; }
            public bool Berhasil { get; }
            public string Berkas { get; }
            public string Pesan { get; }
        }

        // =====================================================================
        // Pemeriksaan
        // =====================================================================

        private async Task CheckForUpdatesAsync()
        {
            IsChecking = true;
            StatusText = "Memeriksa koneksi internet...";
            KosongkanPenawaran();
            BersihkanPesan();

            try
            {
                if (!await IsInternetAvailableAsync())
                {
                    StatusText = "Tidak ada koneksi internet";
                    TampilkanPesan(    JenisPesanInline.Peringatan,
                        "Tidak ada koneksi internet",
                        "Pemeriksaan pembaruan memerlukan koneksi internet. Periksa sambungan jaringan Anda, lalu periksa lagi.",
                        "hasil-periksa");
                    return;
                }

                StatusText = "Memeriksa pembaruan tersedia...";
                _updateInfo = await _updateService.CheckForUpdatesAsync();

                if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.Version))
                {
                    StatusText = "Gagal mendapatkan informasi versi";
                    return;
                }

                if (!IsNewerVersionAvailable())
                {
                    StatusText = $"Sudah menggunakan versi terbaru ({IdentitasAplikasi.VersiSingkat})";
                    TampilkanPesan(    JenisPesanInline.Sukses,
                        "Sudah versi terbaru",
                        $"Anda memakai versi {IdentitasAplikasi.VersiSingkat}. Tidak ada pembaruan yang perlu dipasang saat ini.",
                        "hasil-periksa");
                    return;
                }

                StatusText = $"Versi baru ditemukan: {_updateInfo.Version}";
                await SusunPenawaranAsync(_updateInfo);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pemeriksaan dibatalkan";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal cek pembaruan");
                StatusText = "Pemeriksaan pembaruan gagal — lihat keterangan di halaman.";
                TampilkanPesan(    JenisPesanInline.Galat,
                    "Gagal memeriksa pembaruan",
                    ex.Message + "\nPemeriksaan bisa diulang kapan saja dengan tombol Periksa Pembaruan.",
                    "hasil-periksa");
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Tentukan jenis pembaruan: tambalan kecil (bila rilis menyediakannya dan
        /// versi terpasang cocok) atau installer penuh, beserta alasannya.
        /// </summary>
        public async Task SusunPenawaranAsync(UpdateInfo rilis)
        {
            _tambalan = null;
            _rencana = null;
            Perbaikan.Clear();

            _tambalan = await _patchService.AmbilPenawaranAsync(rilis);

            if (_tambalan == null)
            {
                AlasanInstallerText =
                    "Rilis ini tidak menyediakan pembaruan kecil, sehingga dipasang memakai installer penuh.";
            }
            else
            {
                _rencana = _patchService.SusunRencana(_tambalan, GetCurrentVersion().ToString());

                if (!_rencana.BisaDipakai)
                {
                    AlasanInstallerText = _rencana.AlasanTidakBisa;
                }
                else
                {
                    AlasanInstallerText = string.Empty;
                    RincianPembaruanText = RangkumRencana(_tambalan, _rencana);
                }
            }

            JenisPembaruanText = BisaTambalan
                ? $"Pembaruan kecil tersedia: {_tambalan!.Versi} — tanpa installer"
                : $"Pembaruan tersedia: {rilis.Version} — perlu installer penuh";

            foreach (var baris in AmbilDaftarPerbaikan(rilis, _tambalan?.Ringkasan ?? new List<string>()))
            {
                Perbaikan.Add(baris);
            }

            PenawaranTersedia = true;
            OnPropertyChanged(nameof(BisaTambalan));
            OnPropertyChanged(nameof(BisaInstaller));
            OnPropertyChanged(nameof(TeksTombolTambalan));
            RaiseCommandStates();

            // Unduhan installer yang terhenti sebelumnya (dijeda, koneksi putus, atau
            // aplikasi ditutup) bisa diteruskan tanpa mengunduh ulang bagiannya.
            PeriksaUnduhanTersisa();

            _logger.LogInformation("Penawaran pembaruan {Versi}: tambalan={Tambalan} ({Alasan})",
                rilis.Version, BisaTambalan, BisaTambalan ? "-" : AlasanInstallerText);
        }

        private static string RangkumRencana(TambalanInfo tambalan, RencanaTambalan rencana)
        {
            var ukuran = FormatUkuran(rencana.TotalByte);
            var teks = $"{rencana.BerkasDiganti.Count} berkas berubah • {ukuran} • dari versi " +
                       $"{string.Join(", ", tambalan.DariVersi)} ke {tambalan.Versi}";

            if (rencana.BerkasSudahSama > 0) teks += $" • {rencana.BerkasSudahSama} berkas sudah sama";
            if (rencana.BerkasDilindungi.Count > 0) teks += $" • {rencana.BerkasDilindungi.Count} berkas data pengguna tidak diganti";

            return teks;
        }

        /// <summary>
        /// Daftar perbaikan yang ditampilkan: dari <c>ringkasan</c> patch.json bila ada,
        /// kalau tidak dari catatan rilis GitHub.
        /// </summary>
        private static IEnumerable<string> AmbilDaftarPerbaikan(UpdateInfo rilis, IReadOnlyList<string> ringkasanManifest)
        {
            if (ringkasanManifest != null && ringkasanManifest.Count > 0) return ringkasanManifest;

            return (rilis?.ReleaseNotes ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(b => b.Trim().TrimStart('-', '*', '#'))
                .Where(b => b.Length > 2)
                .Take(20)
                .ToList();
        }

        private static string FormatUkuran(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return $"{Math.Max(1, byteCount / 1024)} KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

        private async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync("https://www.google.com", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private bool IsNewerVersionAvailable()
        {
            if (_updateInfo == null || string.IsNullOrEmpty(_updateInfo.Version)) return false;
            if (!Version.TryParse(_updateInfo.Version, out var latestVersion)) return false;

            return latestVersion > GetCurrentVersion();
        }

        private void KosongkanPenawaran()
        {
            _tambalan = null;
            _rencana = null;
            Perbaikan.Clear();
            PenawaranTersedia = false;
            JenisPembaruanText = string.Empty;
            RincianPembaruanText = string.Empty;
            AlasanInstallerText = string.Empty;
            OnPropertyChanged(nameof(BisaTambalan));
            OnPropertyChanged(nameof(BisaInstaller));
            RaiseCommandStates();
        }

        private void RaiseCommandStates()
        {
            (CheckCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PerbaruiTambalanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (UnduhInstallerCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (JedaLanjutCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatalCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BersihkanRiwayatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        // =====================================================================
        // Unduhan installer: jeda & lanjutkan tanpa kehilangan bagian terunduh
        // =====================================================================

        /// <summary>
        /// Periksa sisa unduhan installer dari percobaan sebelumnya (dijeda, koneksi
        /// putus, atau aplikasi ditutup). Sisanya sengaja tidak dihapus, jadi pengguna
        /// bisa melanjutkan tanpa mengunduh ulang bagian yang sudah ada. Dipanggil
        /// setiap penawaran pembaruan disusun.
        /// </summary>
        public void PeriksaUnduhanTersisa()
        {
            if (IsDownloading)
            {
                return;
            }

            _unduhanInstaller = null;
            UnduhanDijeda = false;
            CatatanUnduhan = string.Empty;

            if (_updateInfo != null)
            {
                try
                {
                    var unduhan = _updateService.BuatUnduhanInstaller(_updateInfo);
                    if (unduhan != null && unduhan.SelarasDenganDisk() > 0)
                    {
                        _unduhanInstaller = unduhan;
                        UnduhanDijeda = true;
                        CatatanUnduhan = unduhan.Ringkasan;
                        DownloadPercent = unduhan.Persen;

                        TampilkanPesan(    JenisPesanInline.Info,
                            "Unduhan installer bisa dilanjutkan",
                            $"Installer pembaruan {_updateInfo.Version} sudah terunduh sebagian ({unduhan.Ringkasan}). " +
                            "Tekan Lanjutkan Unduhan untuk meneruskannya — bagian yang sudah ada tidak diunduh ulang.",
                            "sisa-unduhan");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sisa unduhan installer tidak dapat diperiksa.");
                }
            }

            OnPropertyChanged(nameof(AdaUnduhanTersisa));
            OnPropertyChanged(nameof(InfoSisaUnduhan));
            OnPropertyChanged(nameof(TeksTombolInstaller));
            OnPropertyChanged(nameof(JedaLanjutTersedia));
            RaiseCommandStates();
        }

        /// <summary>Tombol Jeda/Lanjutkan: hentikan sementara, atau teruskan unduhan.</summary>
        private void JedaAtauLanjut()
        {
            if (IsDownloading)
            {
                JedaUnduhan();
                return;
            }

            if (UnduhanDijeda)
            {
                _ = UnduhInstallerAsync();
            }
        }

        /// <summary>
        /// Hentikan unduhan sementara. Bagian yang sudah terunduh tetap tersimpan di
        /// berkas sementara, jadi meneruskan tidak mengulang dari awal.
        /// </summary>
        private void JedaUnduhan()
        {
            UnduhanDijeda = true;
            StatusText = "Menghentikan unduhan sementara...";
            _ctsOperasi?.Cancel();
        }

        // =====================================================================
        // Unduhan yang melanjutkan sendiri saat koneksi terputus
        // =====================================================================

        /// <summary>
        /// Siapkan pelaksana coba ulang: unduhan yang putus di tengah jalan diulang
        /// otomatis dengan jeda bertambah (3 dtk, 6 dtk, 12 dtk, 24 dtk, 48 dtk — lalu
        /// batas tertinggi 1 menit), sampai enam percobaan. Jeda & Batal pengguna tidak
        /// ikut diulang karena bukan gangguan koneksi.
        /// </summary>
        private UnduhanUlangOtomatis BuatPengulangUnduhan()
        {
            _pengulangUnduhan = new UnduhanUlangOtomatis(KebijakanUlang, _logger);
            return _pengulangUnduhan;
        }

        /// <summary>
        /// Kabarkan percobaan ulang di halaman: statusbar menyebut sisa jedanya dan
        /// sebuah kartu menjelaskan bahwa bagian yang sudah terunduh tidak diulang.
        /// </summary>
        private void LaporkanPercobaanUlang(PercobaanUnduhanUlang percobaan)
        {
            StatusText = percobaan.Teks;

            if (percobaan.Menyerah)
            {
                // Kartu kegagalannya ditampilkan penangan galat supaya hanya ada satu kartu.
                return;
            }

            var sisa = CatatanUnduhan.Length == 0 ? string.Empty : $" ({CatatanUnduhan})";
            TampilkanPesan(JenisPesanInline.Peringatan,
                "Koneksi terputus — unduhan dilanjutkan sendiri",
                $"{percobaan.Teks}.\nUnduhan melanjutkan dari bagian yang sudah tersimpan{sisa} — " +
                "tidak diulang dari awal. Halaman boleh ditinggalkan; unduhan tetap berjalan dan hasilnya " +
                "diberitahukan lewat lonceng notifikasi.",
                "coba-ulang-unduhan");
        }

        /// <summary>
        /// Lonceng: unduhan pembaruan selesai (disebut berapa kali koneksi terputus bila ada).
        /// </summary>
        private void KabariUnduhanSelesai(string jenis, string versi, string lokasi, int percobaanUlang)
        {
            try
            {
                _notifications?.Success(
                    "Unduhan pembaruan selesai",
                    $"{jenis} pembaruan {versi} berhasil diunduh" +
                    (percobaanUlang > 0
                        ? $" setelah {percobaanUlang} percobaan ulang karena koneksi terputus."
                        : ".") +
                    $"\nTersimpan di: {lokasi}",
                    "pembaruan");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notifikasi unduhan selesai tidak bisa dikirim.");
            }
        }

        /// <summary>
        /// Lonceng: unduhan benar-benar gagal (sudah dicoba berulang). Menyebut sisa
        /// unduhan yang masih tersimpan supaya pengguna tahu melanjutkan nanti tidak
        /// mengunduh dari awal.
        /// </summary>
        private void KabariUnduhanGagal(
            string jenis, string versi, string alasan, int percobaanDipakai, UnduhanBerkasBerlanjut? sisa = null)
        {
            try
            {
                var keteranganSisa = sisa is { AdaSisa: true }
                    ? $"\nBagian yang sudah terunduh ({sisa.Persen}% • {sisa.Ringkasan}) tetap tersimpan, " +
                      "jadi melanjutkan nanti tidak mengulang dari awal."
                    : string.Empty;

                _notifications?.Error(
                    "Unduhan pembaruan gagal",
                    $"{jenis} pembaruan {versi} tidak bisa diunduh setelah {Math.Max(1, percobaanDipakai)} percobaan.\n" +
                    $"Penyebab: {alasan}" + keteranganSisa +
                    "\nPeriksa sambungan internet, lalu buka halaman Pembaruan untuk mencoba lagi.",
                    "pembaruan");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notifikasi unduhan gagal tidak bisa dikirim.");
            }
        }

        /// <summary>
        /// Galat akhir unduhan untuk dilaporkan: menyebut berapa percobaan yang sudah
        /// dicoba bila memang lebih dari satu — kalau percobaan pertama langsung gagal
        /// karena sebab yang pasti, pesan aslinya lebih jelas bagi pengguna.
        /// </summary>
        private static IOException GagalSetelahPercobaan(UnduhanUlangOtomatis pengulang)
        {
            var asli = pengulang.GalatTerakhir;

            return pengulang.PercobaanDipakai <= 1 || asli == null
                ? new IOException(asli?.Message ?? "Unduhan gagal.", asli)
                : new IOException(
                    $"Unduhan tetap gagal setelah {pengulang.PercobaanDipakai} percobaan: {asli.Message}", asli);
        }

        /// <summary>Perbarui persentase & keterangan byte di halaman.</summary>
        private void LaporkanKemajuan(int persen, string? teksUkuran = null)
        {
            DownloadPercent = persen;
            if (!string.IsNullOrEmpty(teksUkuran))
            {
                CatatanUnduhan = teksUkuran!;
            }
        }

        // =====================================================================
        // Jalur 1: pembaruan kecil (tambalan)
        // =====================================================================

        private async Task TerapkanTambalanAsync()
        {
            if (!BisaTambalan || _updateInfo == null || _tambalan == null || _rencana == null) return;

            var ringkasanPerbaikan = Perbaikan.Count == 0
                ? "Tidak ada rincian perbaikan."
                : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

            // Rincian ditampilkan di kartu keputusan dalam halaman — bukan dialog popup.
            var rincian =
                $"Pembaruan kecil {_tambalan.Versi} siap dipasang.\n\n" +
                $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                $"{RincianPembaruanText}\n" +
                "Aplikasi akan ditutup sebentar, berkas diganti, lalu dibuka kembali otomatis. " +
                "Data surat, database, dan pengaturan tidak diubah.";

            bool lanjut = await TanyaAsync(
                "Pembaruan Kecil",
                rincian,
                $"Pasang pembaruan kecil {_tambalan.Versi} sekarang?",
                "Pasang Sekarang",
                "Nanti");

            if (!lanjut)
            {
                TampilkanPesan(    JenisPesanInline.Info,
                    "Pembaruan tidak dipasang",
                    $"Pembaruan kecil {_tambalan.Versi} masih tersedia. Tekan tombol Perbarui Sekarang kapan saja " +
                    "bila sudah siap memasangnya — tidak ada yang berubah pada aplikasi Anda.",
                    "hasil-pasang");
                return;
            }

            IsDownloading = true;
            DownloadPercent = 0;
            StatusText = "Mengunduh paket pembaruan kecil...";

            var progress = new Progress<int>(persen =>
            {
                DownloadPercent = persen;
                StatusText = $"Mengunduh paket pembaruan kecil: {persen}%";
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ctsOperasi = cts;

            try
            {
                // Unduhan paket tambalan juga diulang sendiri bila koneksi terputus
                // (setiap percobaan mengunduh ulang paketnya, jadi berkas tidak bercampur).
                var pengulang = BuatPengulangUnduhan();
                var laporanPercobaan = new Progress<PercobaanUnduhanUlang>(LaporkanPercobaanUlang);

                TambalanSiap siap = null!;
                bool tuntas = await pengulang.JalankanAsync(
                    async ct => siap = await _patchService.UnduhDanSiapkanAsync(
                        _updateInfo, _tambalan, _rencana, progress, ct),
                    laporanPercobaan, cts.Token);

                if (!tuntas)
                {
                    throw GagalSetelahPercobaan(pengulang);
                }

                DownloadPercent = 100;
                StatusText = $"Menutup aplikasi untuk memasang {_tambalan.Versi}...";

                // Aplikasi harus benar-benar tertutup sebelum berkasnya diganti —
                // skrip penerap menunggu proses ini berakhir, lalu membuka aplikasi lagi.
                _patchService.JalankanPenerapan(siap);

                await Task.Delay(400);
                Application.Current?.Shutdown();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pembaruan dibatalkan";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Pemasangan dibatalkan",
                    "Pembaruan kecil tidak jadi dipasang dan aplikasi tetap memakai versi sekarang. " +
                    "Data surat dan pengaturan tidak berubah.",
                    "hasil-pasang");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memasang pembaruan kecil");
                StatusText = "Gagal memasang pembaruan kecil";

                // Gagal sebelum skrip penerap jalan — skrip hanya mencatat bila sudah
                // dieksekusi, jadi kegagalan unduhan/verifikasi dicatat di sini.
                CatatRiwayatGagal(_tambalan.Versi, "Tambalan", ex.Message);

                // Benar-benar gagal (bukan batal pengguna): beri tahu lewat lonceng,
                // karena halaman bisa saja sudah ditinggalkan saat percobaan ulang berjalan.
                KabariUnduhanGagal(
                    "Paket pembaruan kecil", _tambalan.Versi, ex.Message,
                    _pengulangUnduhan?.PercobaanDipakai ?? 0);

                TampilkanPesan(    JenisPesanInline.Galat,
                    "Gagal memasang pembaruan kecil",
                    $"{ex.Message}\nSilakan coba lagi, atau pasang lewat installer penuh (tombol Unduh Installer). " +
                    "Aplikasi tetap berjalan normal dengan versi sekarang.",
                    "hasil-pasang");
            }
            finally
            {
                if (ReferenceEquals(_ctsOperasi, cts))
                {
                    _ctsOperasi = null;
                }

                IsDownloading = false;
                DownloadPercent = 0;
            }
        }

        // =====================================================================
        // Jalur 2: installer penuh
        // =====================================================================

        private async Task UnduhInstallerAsync()
        {
            if (_updateInfo == null || string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Tautan unduhan belum tersedia",
                    "Informasi rilis ini belum memuat tautan unduhan installer. Coba periksa pembaruan lagi " +
                    "beberapa saat lagi, atau pasang pembaruan kecil bila tersedia.",
                    "hasil-pasang");
                return;
            }

            // Melanjutkan unduhan yang sudah dijeda: keputusan mengunduh sudah dijawab
            // sebelumnya, jadi tidak perlu kartu keputusan lagi.
            bool melanjutkan = _unduhanInstaller?.AdaSisa == true;

            if (!melanjutkan)
            {
                var ringkasanPerbaikan = Perbaikan.Count == 0
                    ? "Tidak ada rincian perbaikan."
                    : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

                // Keputusan ditanyakan lewat kartu di dalam halaman — bukan dialog popup.
                var rincian = $"Pembaruan {_updateInfo.Version} memakai installer penuh.\n\n" +
                              $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                              (string.IsNullOrWhiteSpace(AlasanInstallerText) ? string.Empty : AlasanInstallerText + "\n") +
                              "Installer akan diunduh lalu dijalankan. Unduhan bisa dijeda dan dilanjutkan kapan saja.";

                bool lanjut = await TanyaAsync(
                    "Pembaruan Installer Penuh",
                    rincian,
                    $"Unduh installer pembaruan {_updateInfo.Version} sekarang?",
                    "Unduh Installer",
                    "Nanti");

                if (!lanjut)
                {
                    TampilkanPesan(    JenisPesanInline.Info,
                        "Unduhan installer tidak dijalankan",
                        $"Installer pembaruan {_updateInfo.Version} belum diunduh. Tekan Unduh Installer kapan saja " +
                        "bila sudah siap — tidak ada yang berubah pada aplikasi Anda.",
                        "hasil-installer");
                    return;
                }
            }

            IsDownloading = true;
            UnduhanDijeda = false;
            if (_unduhanInstaller != null) DownloadPercent = _unduhanInstaller.Persen;
            CatatanUnduhan = _unduhanInstaller?.Ringkasan ?? string.Empty;
            StatusText = melanjutkan
                ? $"Melanjutkan unduhan installer dari {CatatanUnduhan}..."
                : "Mempersiapkan download installer...";

            var progress = new Progress<KemajuanUnduhan>(k =>
            {
                LaporkanKemajuan(k.Persen, k.Teks);
                StatusText = (melanjutkan ? "Melanjutkan unduhan installer: " : "Download installer: ") + k.Teks;
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ctsOperasi = cts;

            try
            {
                string downloadPath;

                // Koneksi putus bukan lagi kegagalan akhir: unduhan diulang sendiri di
                // latar belakang dengan jeda yang bertambah, dan setiap percobaan
                // melanjutkan dari bagian yang sudah tersimpan (tidak mengulang dari awal).
                var pengulang = BuatPengulangUnduhan();
                var laporanPercobaan = new Progress<PercobaanUnduhanUlang>(LaporkanPercobaanUlang);

                if (_unduhanInstaller != null)
                {
                    // Berkas sementara dipakai lagi: yang sudah terunduh tidak diulang.
                    var unduhan = _unduhanInstaller;
                    bool tuntas = await pengulang.JalankanAsync(
                        ct => unduhan.UnduhAsync(progress, ct), laporanPercobaan, cts.Token);

                    if (!tuntas)
                    {
                        // Sisa unduhan tetap tersimpan (bisa dilanjutkan kapan saja);
                        // penangan galat di bawah yang mencatat riwayat & kartu halaman.
                        throw GagalSetelahPercobaan(pengulang);
                    }

                    downloadPath = unduhan.Tujuan;
                    _unduhanInstaller = null;
                }
                else
                {
                    // Sumber tanpa dukungan lanjutan (mis. Google Drive): jalur lama.
                    var progressPersen = new Progress<int>(persen => LaporkanKemajuan(persen));
                    string hasil = string.Empty;
                    bool tuntas = await pengulang.JalankanAsync(
                        async ct => { hasil = await _updateService.DownloadUpdateAsync(_updateInfo, progressPersen, ct); },
                        laporanPercobaan, cts.Token);

                    if (!tuntas)
                    {
                        throw GagalSetelahPercobaan(pengulang);
                    }

                    downloadPath = hasil;
                }

                StatusText = $"Installer tersimpan di: {downloadPath}";
                DownloadPercent = 100;

                // Selesai (walau sempat terputus berkali-kali): beri tahu lewat lonceng
                // supaya pengguna tahu walau sudah berpindah halaman.
                KabariUnduhanSelesai(
                    "Installer", _updateInfo.Version ?? "-", downloadPath, pengulang.PercobaanUlang);

                // Unduhan selesai: jalur berkasnya ditampilkan di halaman (bukan popup)
                // supaya bisa dibaca/disalin kapan saja sebelum memutuskan menjalankannya.
                var jalankan = await TanyaAsync(
                    "Unduhan installer selesai",
                    $"Installer tersimpan di:\n{downloadPath}\n" +
                    "Jalankan installer sekarang untuk menuntaskan pemasangan, atau nanti " +
                    "(berkasnya tetap ada dan bisa dijalankan sendiri kapan saja).",
                    $"Jalankan installer {_updateInfo.Version} sekarang?",
                    "Jalankan Installer",
                    "Nanti",
                    kunci: "hasil-installer");

                if (!jalankan)
                {
                    // Berkasnya tetap ada: jalur simpan ditampilkan di halaman supaya
                    // bisa dijalankan sendiri nanti tanpa perlu popup apa pun.
                    TampilkanPesan(    JenisPesanInline.Info,
                        "Installer siap dijalankan kapan saja",
                        $"Installer pembaruan {_updateInfo.Version} tersimpan di:\n{downloadPath}",
                        "hasil-installer");
                }
                else
                {
                    // Catat di riwayat: installer dijalankan pengguna. Hasil akhirnya
                    // tergantung installer; versi terpasang akan berubah bila selesai.
                    RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                    {
                        Versi = _updateInfo.Version ?? "-",
                        Jenis = "Installer",
                        JumlahBerkas = 0,
                        Berhasil = true,
                        Pesan = "Installer penuh dijalankan dari halaman Pembaruan.",
                        DariVersi = GetCurrentVersion().ToString(),
                        Waktu = DateTime.Now
                    });

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadPath,
                        UseShellExecute = true
                    });
                    Application.Current?.Shutdown();
                }
            }
            catch (OperationCanceledException) when (UnduhanDijeda)
            {
                // Jeda — bukan batal: berkas sementara tetap ada dan unduhan bisa
                // diteruskan dari titik ini, bahkan setelah aplikasi dibuka lagi.
                StatusText = $"Unduhan dijeda di {DownloadPercent}% — tekan Lanjutkan untuk meneruskan.";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Unduhan installer dijeda",
                    $"Installer pembaruan {_updateInfo.Version} baru terunduh sebagian" +
                    (CatatanUnduhan.Length == 0 ? "." : $" ({CatatanUnduhan}).") +
                    "\nTekan Lanjutkan Unduhan untuk meneruskannya — bagian yang sudah terunduh tidak diulang, " +
                    "dan sisanya tetap tersimpan walau aplikasi ditutup.",
                    "hasil-installer");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Download dibatalkan";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Unduhan dibatalkan",
                    "Installer tidak jadi diunduh. Aplikasi tetap memakai versi sekarang; unduhan bisa dimulai lagi kapan saja.",
                    "hasil-installer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal download installer");
                StatusText = "Unduhan installer gagal — lihat keterangan di halaman.";
                CatatRiwayatGagal(_updateInfo.Version ?? "-", "Installer", "Unduhan installer gagal: " + ex.Message);

                // Benar-benar gagal (sudah dicoba berulang, bukan batal pengguna):
                // beri tahu lewat lonceng beserta sisa unduhan yang masih tersimpan.
                KabariUnduhanGagal(
                    "Installer", _updateInfo.Version ?? "-", ex.Message,
                    _pengulangUnduhan?.PercobaanDipakai ?? 0,
                    _unduhanInstaller?.AdaSisa == true ? _unduhanInstaller : null);
                TampilkanPesan(    JenisPesanInline.Galat,
                    "Installer gagal diunduh",
                    $"{ex.Message}\nPeriksa koneksi internet lalu coba lagi; kegagalan ini juga tercatat pada Riwayat Pembaruan di bawah.",
                    "hasil-installer");
            }
            finally
            {
                if (ReferenceEquals(_ctsOperasi, cts))
                {
                    _ctsOperasi = null;
                }

                IsDownloading = false;

                // Saat dijeda persentasenya dipertahankan supaya pengguna melihat
                // sampai mana unduhan berhenti.
                if (!UnduhanDijeda)
                {
                    DownloadPercent = 0;
                }

                OnPropertyChanged(nameof(AdaUnduhanTersisa));
                OnPropertyChanged(nameof(InfoSisaUnduhan));
                OnPropertyChanged(nameof(TeksTombolInstaller));
                OnPropertyChanged(nameof(JedaLanjutTersedia));
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Batalkan kegiatan yang sedang berjalan. Untuk unduhan installer, sisa
        /// unduhan yang tersimpan juga dibuang — berbeda dengan Jeda yang menyimpannya
        /// supaya bisa dilanjutkan.
        /// </summary>
        private void Batal()
        {
            BuangSisaUnduhan();
            // Hanya kegiatan yang sedang berjalan yang dihentikan — bukan seluruh
            // ViewModel — supaya mencoba lagi setelah Batal tetap bisa berhasil.
            _ctsOperasi?.Cancel();
            StatusText = "Membatalkan...";
        }

        /// <summary>Buang berkas sisa unduhan installer (dipakai tombol Batal).</summary>
        private void BuangSisaUnduhan()
        {
            if (_unduhanInstaller == null)
            {
                return;
            }

            _unduhanInstaller.Bersihkan();
            _unduhanInstaller = null;
            UnduhanDijeda = false;
            CatatanUnduhan = string.Empty;
        }

        public void Dispose()
        {
            _ctsOperasi?.Cancel();
            _ctsOperasi?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
