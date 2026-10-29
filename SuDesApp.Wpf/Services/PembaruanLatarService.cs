using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Satu pemilik pekerjaan pembaruan di latar belakang: memeriksa rilis terbaru
    /// setelah pengguna masuk, MENGUNDUH paketnya tanpa diminta (tambalan kecil bila
    /// tersedia, installer penuh bila tidak), lalu mengabari lewat lonceng dengan dua
    /// pilihan — <b>Instal Sekarang</b> atau <b>Instal Saat Keluar</b>.
    ///
    /// Pemasangan sendiri tidak punya halaman konfirmasi: pemilihan waktu di
    /// notifikasi adalah satu-satunya keputusan. Sesudah itu aplikasi menutup,
    /// installer berjalan senyap (hanya jendela kemajuan pemasangan), dan aplikasi
    /// dibuka kembali — lihat <see cref="PemasangPembaruan"/>.
    ///
    /// Status "sudah terunduh" disimpan di sini (bukan di halaman Pembaruan) supaya
    /// unduhan latar dan tombol halaman tidak saling menimpa berkas sementara.
    /// Halaman Pembaruan tetap berdiri sendiri untuk unduhan manual.
    /// </summary>
    public sealed class PembaruanLatarService : IDisposable
    {
        private readonly UpdateService _updateService;
        private readonly PatchUpdateService _patchService;
        private readonly NotificationService _notifications;
        private readonly ILogger<PembaruanLatarService> _logger;

        /// <summary>Mengunci keadaan paket yang sudah siap (dibaca dari thread mana pun).</summary>
        private readonly object _kunci = new();

        private readonly CancellationTokenSource _cts = new();

        private bool _berjalan;
        private bool _pemasanganDijadwalkan;

        private TambalanSiap? _tambalanSiap;
        private string? _jalurInstaller;
        private string? _versiSiap;

        /// <summary>Pengguna memilih "Instal Saat Keluar" untuk paket yang siap ini.</summary>
        private bool _pasangSaatKeluar;

        /// <summary>Judul kabar "siap dipasang" — dipakai juga untuk menggantinya saat pilihan berubah.</summary>
        private const string JudulSiap = "Pembaruan siap dipasang";

        public PembaruanLatarService(
            UpdateService updateService,
            PatchUpdateService patchService,
            NotificationService notifications,
            ILogger<PembaruanLatarService> logger)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Dibangkitkan saat ada pembaruan yang perlu diketahui pengguna (siap pasang
        /// maupun hanya tersedia). Jendela utama memakainya untuk menandai menu
        /// Pembaruan dengan "(baru)".
        /// </summary>
        public event EventHandler? PembaruanDitemukan;

        /// <summary>Benar bila sudah ada paket terunduh yang menunggu dipasang.</summary>
        public bool AdaPembaruanSiap
        {
            get
            {
                lock (_kunci)
                {
                    return _tambalanSiap != null || !string.IsNullOrWhiteSpace(_jalurInstaller);
                }
            }
        }

        /// <summary>Versi paket yang menunggu dipasang (kosong bila belum ada).</summary>
        public string VersiSiap
        {
            get { lock (_kunci) return _versiSiap ?? string.Empty; }
        }

        /// <summary>
        /// Jalankan pemeriksaan + unduhan latar satu kali. Aman dipanggil berkali-kali:
        /// panggilan berikutnya tidak memulai pekerjaan kedua.
        /// </summary>
        public async Task MulaiAsync()
        {
            lock (_kunci)
            {
                if (_berjalan) return;
                _berjalan = true;
            }

            try
            {
                await PeriksaDanUnduhAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Aplikasi ditutup / pekerjaan dibatalkan — bukan kegagalan.
            }
            catch (Exception ex)
            {
                // Termasuk tanpa internet atau repo tidak terjangkau: cukup dicatat,
                // pengguna tetap bisa memeriksa manual dari halaman Pembaruan.
                _logger.LogDebug(ex, "Pemeriksaan/unduhan pembaruan latar belakang dilewati.");
            }
            finally
            {
                lock (_kunci)
                {
                    _berjalan = false;
                }
            }
        }

        /// <summary>
        /// Periksa rilis terbaru; bila ada dan diizinkan, unduh di latar belakang lalu
        /// kirim notifikasi dengan pilihan pemasangan. Bila unduhan otomatis dimatikan
        /// (atau paketnya melebihi batas ukuran), kirim notifikasi ketersediaan saja —
        /// persis seperti perilaku lama sebelum unduhan latar ada.
        /// </summary>
        public async Task PeriksaDanUnduhAsync(CancellationToken cancellationToken)
        {
            if (!_updateService.IsAvailable) return;

            var rilis = await _updateService.CheckForUpdatesAsync(cancellationToken);
            if (rilis == null || !Version.TryParse(rilis.Version, out var versiRilis)) return;

            var versiTerpasang = typeof(PembaruanLatarService).Assembly.GetName().Version
                                 ?? new Version(0, 0);
            if (versiRilis <= versiTerpasang) return;

            string versi = rilis.Version!;

            if (!AppPreferenceStore.IsUnduhPembaruanOtomatis())
            {
                KabariTersediaSaja(rilis,
                    "Unduhan otomatis dimatikan di Pengaturan Aplikasi — buka menu Pembaruan untuk mengunduh dan memasangnya.");
                return;
            }

            // Jalur 1: pembaruan kecil (tambalan). Hanya berkas yang berubah yang
            // diunduh, jadi jauh lebih ringan daripada installer penuh.
            var tambalan = await _patchService.AmbilPenawaranAsync(rilis, cancellationToken);
            var rencana = _patchService.SusunRencana(tambalan, versiTerpasang.ToString());

            if (tambalan != null && rencana.BisaDipakai)
            {
                long batasTambalan = (long)AppPreferenceStore.GetBatasUkuranTambalanMb() * 1024 * 1024;
                if (rencana.TotalByte > batasTambalan)
                {
                    KabariTersediaSaja(rilis,
                        $"Paket pembaruan kecilnya {KemajuanUnduhan.Ukuran(rencana.TotalByte)}, melebihi batas " +
                        $"{AppPreferenceStore.GetBatasUkuranTambalanMb()} MB — buka menu Pembaruan untuk memasangnya manual.");
                    return;
                }

                _logger.LogInformation("Mengunduh pembaruan kecil {Versi} di latar belakang...", tambalan.Versi);

                // Paket yang tidak jadi dipasang (mis. pengguna menutup aplikasi tanpa
                // memilih) meninggalkan folder kerja di TEMP. Folder dari hari-hari
                // sebelumnya dibuang agar unduhan latar tidak menumpuk diam-diam.
                BersihkanTambalanLama();

                var siap = await _patchService.UnduhDanSiapkanAsync(
                    rilis, tambalan, rencana, progress: null, cancellationToken);

                lock (_kunci)
                {
                    _tambalanSiap = siap;
                    _jalurInstaller = null;
                    _versiSiap = siap.Versi;
                    _pasangSaatKeluar = false;
                }

                KabariSiap(siap.Versi, siap.TotalByte, "pembaruan kecil");
                return;
            }

            // Jalur 2: installer penuh. Ukurannya dibatasi supaya kuota internet
            // pengguna tidak terpakai tanpa sepengetahuan mereka.
            if (string.IsNullOrWhiteSpace(rilis.DownloadUrl))
            {
                KabariTersediaSaja(rilis, "Rilis ini belum menyediakan tautan unduhan — coba periksa lagi nanti.");
                return;
            }

            long ukuranInstaller = UkuranInstaller(rilis);
            long batasInstaller = (long)AppPreferenceStore.GetBatasUkuranUnduhanMb() * 1024 * 1024;
            if (ukuranInstaller > 0 && ukuranInstaller > batasInstaller)
            {
                KabariTersediaSaja(rilis,
                    $"Installernya {KemajuanUnduhan.Ukuran(ukuranInstaller)}, melebihi batas unduhan otomatis " +
                    $"{AppPreferenceStore.GetBatasUkuranUnduhanMb()} MB — buka menu Pembaruan untuk mengunduhnya.");
                return;
            }

            _logger.LogInformation("Mengunduh installer pembaruan {Versi} di latar belakang...", versi);

            string jalurInstaller;
            var unduhan = _updateService.BuatUnduhanInstaller(rilis);
            if (unduhan != null && SudahTerunduhUtuh(unduhan.Tujuan, rilis.Sha256))
            {
                // Installer versi ini sudah lengkap dari sesi sebelumnya (mis.
                // aplikasi ditutup sebelum dipasang): jangan unduh ~100 MB lagi.
                jalurInstaller = unduhan.Tujuan;
                _logger.LogInformation("Installer {Versi} sudah terunduh sebelumnya; tidak diunduh ulang.", versi);
            }
            else if (unduhan != null)
            {
                // Berkas sementara per versi: bila aplikasi ditutup sebelum selesai,
                // unduhan diteruskan (bukan diulang) pada pembukaan berikutnya.
                await unduhan.UnduhAsync(progress: null, cancellationToken);
                jalurInstaller = unduhan.Tujuan;
            }
            else
            {
                // Sumber tanpa dukungan lanjutan (mis. Google Drive).
                jalurInstaller = await _updateService.DownloadUpdateAsync(
                    rilis, new Progress<int>(), cancellationToken);
            }

            lock (_kunci)
            {
                _tambalanSiap = null;
                _jalurInstaller = jalurInstaller;
                _versiSiap = versi;
                _pasangSaatKeluar = false;
            }

            KabariSiap(versi, ukuranInstaller, "installer penuh");
        }

        /// <summary>
        /// Pasang sekarang juga: aplikasi menutup, pemasangan berjalan otomatis
        /// (senyap, hanya jendela kemajuan), lalu aplikasi terbuka lagi.
        /// </summary>
        public void PasangSekarang()
        {
            lock (_kunci)
            {
                if (_pemasanganDijadwalkan) return; // klik kedua saat aplikasi sudah menutup
            }

            string versi = VersiSiap;
            if (!JadwalkanPemasangan()) return;

            TampilkanLayarPemasangan(versi);
            TutupAplikasi();
        }

        /// <summary>
        /// Catat pilihan pengguna: paket yang sudah siap akan dipasang saat aplikasi
        /// ditutup. Konfirmasi dikirim lewat lonceng supaya pilihannya terlihat jelas.
        /// </summary>
        public void PasangSaatKeluar()
        {
            lock (_kunci)
            {
                if (_tambalanSiap == null && string.IsNullOrWhiteSpace(_jalurInstaller)) return;
                _pasangSaatKeluar = true;
            }

            // Kabar "siap dipasang" diganti kabar konfirmasi: satu pembaruan cukup
            // punya satu baris di lonceng, bukan dua yang saling bertentangan.
            _notifications.HapusPesanLama(JudulSiap);
            _notifications.Success(
                "Pembaruan akan dipasang saat keluar",
                $"Pembaruan {VersiSiap} dipasang otomatis ketika aplikasi ditutup, lalu aplikasi terbuka lagi " +
                "dengan versi terbaru. Tidak ada pertanyaan tambahan saat pemasangan.",
                "pembaruan",
                new[] { new NotificationAction("Instal Sekarang", PasangSekarang, primary: true) });
        }

        /// <summary>
        /// Dipanggil saat jendela utama hendak ditutup. Mengembalikan <c>true</c> bila
        /// pemasangan sudah dijadwalkan (pemanggil harus menutup aplikasi), atau
        /// <c>false</c> bila keluar boleh langsung berjalan seperti biasa.
        /// </summary>
        public bool SiapkanPemasanganSaatKeluar()
        {
            bool siap;
            bool adaTambalan;
            bool dipilih;
            lock (_kunci)
            {
                adaTambalan = _tambalanSiap != null;
                siap = adaTambalan || !string.IsNullOrWhiteSpace(_jalurInstaller);
                dipilih = _pasangSaatKeluar;
            }

            if (!siap) return false;

            if (!dipilih)
            {
                // Setelan lama ("pasang otomatis saat ditutup") hanya berlaku untuk
                // pembaruan kecil — itulah janjinya sejak awal. Installer penuh hanya
                // berjalan saat pengguna benar-benar memilih "Instal Saat Keluar".
                if (!adaTambalan || !AppPreferenceStore.IsPasangOtomatisSaatKeluar()) return false;
            }

            return JadwalkanPemasangan();
        }

        /// <summary>
        /// Jadwalkan pemasangan setelah proses ini berakhir. Menulis riwayat lebih
        /// dulu supaya hasilnya bisa diperiksa di halaman Pembaruan.
        /// </summary>
        private bool JadwalkanPemasangan()
        {
            TambalanSiap? tambalan;
            string? installer;
            string versi;

            lock (_kunci)
            {
                if (_pemasanganDijadwalkan) return true;
                tambalan = _tambalanSiap;
                installer = _jalurInstaller;
                versi = _versiSiap ?? string.Empty;
            }

            try
            {
                if (tambalan != null)
                {
                    // Skrip penerap menulis hasil sebenarnya; riwayat pembaruan
                    // dicatat dari hasil itu saat aplikasi dibuka lagi (satu catatan,
                    // bukan dua yang berbeda).
                    _patchService.JalankanPenerapan(tambalan);
                }
                else if (!string.IsNullOrWhiteSpace(installer))
                {
                    if (!PemasangPembaruan.JalankanSetelahKeluar(installer!, JalurAplikasiUntukDibuka()))
                    {
                        _logger.LogWarning("Installer pembaruan {Versi} tidak bisa dijadwalkan.", versi);
                        _notifications.Error(
                            "Pemasangan pembaruan gagal dimulai",
                            $"Installer pembaruan {versi} tidak dapat dijalankan. Buka menu Pembaruan untuk " +
                            "memasangnya secara manual; aplikasi tetap memakai versi sekarang.",
                            "pembaruan");
                        return false;
                    }

                    // Tanpa catatan riwayat di sini: hasil installer baru diketahui
                    // setelah aplikasi dibuka lagi dengan versi baru.
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjadwalkan pemasangan pembaruan {Versi}.", versi);
                _notifications.Error(
                    "Pemasangan pembaruan gagal dimulai",
                    $"Pembaruan {versi} sudah terunduh, tetapi pemasangannya tidak bisa dijalankan: {ex.Message}\n" +
                    "Buka menu Pembaruan untuk mencoba lagi; aplikasi tetap memakai versi sekarang.",
                    "pembaruan");
                return false;
            }

            lock (_kunci)
            {
                _pemasanganDijadwalkan = true;
            }

            return true;
        }

        /// <summary>
        /// Exe yang dibuka lagi setelah pemasangan — hanya bila memang aplikasi ini
        /// (saat pengembangan, prosesnya <c>dotnet.exe</c> dan tidak boleh dibuka ulang
        /// tanpa argumen). Null berarti aplikasi tidak dibuka lagi.
        /// </summary>
        private static string? JalurAplikasiUntukDibuka()
        {
            var jalur = MulaiUlangAplikasi.JalurProsesSaatIni();
            return jalur != null
                   && Path.GetFileName(jalur).Equals("SuDesApp.exe", StringComparison.OrdinalIgnoreCase)
                ? jalur
                : null;
        }

        /// <summary>
        /// Tampilkan layar "memasang pembaruan" sebentar sebelum aplikasi menutup.
        /// Installer senyap melanjutkan sendiri dengan jendela kemajuannya.
        /// </summary>
        private static void TampilkanLayarPemasangan(string versi)
        {
            try
            {
                if (System.Windows.Application.Current == null) return;

                var layar = new SplashStartupWindow();
                layar.Tahap(string.IsNullOrWhiteSpace(versi)
                    ? "Memasang pembaruan..."
                    : $"Memasang pembaruan {versi}...");
                layar.Show();
            }
            catch
            {
                // Layar informasi tidak boleh menggagalkan pemasangan.
            }
        }

        private static void TutupAplikasi()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            void Tutup()
            {
                // Jendela utama menutup tanpa dialog konfirmasi: pengguna sudah
                // memilih "Instal Sekarang", jadi menanyakan "yakin ingin keluar?"
                // hanya menghalangi pemasangan yang sudah dijadwalkan. Closed →
                // App.Shutdown (ShutdownMode=OnExplicitShutdown).
                if (app.MainWindow is MainWindow utama)
                {
                    utama.TutupUntukPemasangan();
                    return;
                }

                app.Shutdown();
            }

            void JadwalkanTutup()
            {
                // Beri waktu layar "Memasang pembaruan..." tergambar sebelum jendela
                // utama ditutup; tanpa jeda ini layar hanya berkedip.
                var jeda = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(900)
                };
                jeda.Tick += (_, _) =>
                {
                    jeda.Stop();
                    Tutup();
                };
                jeda.Start();
            }

            // Timer memerlukan message pump UI; bila pemanggil bukan thread UI,
            // penutupan dijadwalkan ke sana agar aplikasi tidak menggantung.
            if (app.Dispatcher.CheckAccess()) JadwalkanTutup();
            else app.Dispatcher.BeginInvoke(new Action(JadwalkanTutup));
        }

        private void KabariSiap(string versi, long ukuran, string jenis)
        {
            var aksi = new List<NotificationAction>
            {
                new("Instal Sekarang", PasangSekarang, primary: true),
                new("Instal Saat Keluar", PasangSaatKeluar)
            };

            string ukuranTeks = ukuran > 0 ? $" ({KemajuanUnduhan.Ukuran(ukuran)})" : string.Empty;

            _notifications.Success(
                JudulSiap,
                $"Pembaruan {versi} — {jenis}{ukuranTeks} — sudah terunduh di latar belakang.\n" +
                "Pilih waktunya:\n" +
                "• Instal Sekarang — aplikasi menutup, memasang otomatis, lalu terbuka lagi.\n" +
                "• Instal Saat Keluar — dipasang otomatis ketika aplikasi ditutup.\n" +
                "Pemasangan berjalan tanpa pertanyaan, hanya menampilkan kemajuan.",
                "pembaruan",
                aksi);

            PembaruanDitemukan?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Kabar saat paket belum diunduh (unduhan otomatis mati / melebihi batas).
        /// Sama seperti perilaku halaman Pembaruan lama: pengguna diarahkan ke menu.
        /// </summary>
        private void KabariTersediaSaja(UpdateInfo rilis, string alasan)
        {
            var perbaikan = RingkasPerbaikan(rilis);
            var daftar = perbaikan.Count == 0
                ? string.Empty
                : string.Join("\n", perbaikan.Take(6).Select(p => "• " + p)) + "\n";

            _notifications.Info(
                $"Pembaruan {rilis.Version} tersedia",
                $"{alasan}\n{daftar}",
                "pembaruan");

            PembaruanDitemukan?.Invoke(this, EventArgs.Empty);
        }

        private static List<string> RingkasPerbaikan(UpdateInfo rilis)
        {
            return (rilis.ReleaseNotes ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(b => b.Trim().TrimStart('-', '*', '#'))
                .Where(b => b.Length > 2)
                .Take(12)
                .ToList();
        }

        /// <summary>
        /// Buang folder kerja tambalan yang lebih tua dari sehari. Folder hari ini
        /// dibiarkan — bisa jadi paket yang sedang menunggu dipasang saat keluar.
        /// </summary>
        private static void BersihkanTambalanLama()
        {
            try
            {
                var kedaluwarsa = DateTime.UtcNow.AddDays(-1);
                foreach (var folder in Directory.GetDirectories(Path.GetTempPath(), "SuDesApp_tambalan_*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(folder) < kedaluwarsa)
                        {
                            Directory.Delete(folder, recursive: true);
                        }
                    }
                    catch (IOException)
                    {
                        // Folder masih dipakai proses pemasangan — coba lagi lain waktu.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Bukan milik pengguna ini — biarkan.
                    }
                }
            }
            catch (Exception)
            {
                // Pembersihan bersifat best-effort; tidak boleh menggagalkan unduhan.
            }
        }

        /// <summary>
        /// Benar bila berkas installer versi ini sudah ada DAN sidik jarinya cocok
        /// dengan rilis. Tanpa sha256 resmi kelengkapan tidak bisa dipastikan, jadi
        /// berkas tidak dipakai ulang.
        /// </summary>
        private static bool SudahTerunduhUtuh(string jalur, string? sha256)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(sha256)
                       && File.Exists(jalur)
                       && UpdateService.VerifySha256(jalur, sha256!);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Ukuran installer penuh menurut aset rilis (0 bila tidak diketahui).</summary>
        private static long UkuranInstaller(UpdateInfo rilis)
        {
            var aset = rilis.CariAset(rilis.NamaInstaller ?? string.Empty)
                       ?? rilis.DaftarAset?.FirstOrDefault(a =>
                           a.Nama.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            return aset?.Ukuran ?? 0;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
