using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    // Modul status MainWindowViewModel; state bersama tetap di berkas utama.
    public partial class MainWindowViewModel
    {
        /// <summary>Cadangan data IDM (untuk status IDM di status bar); null bila tidak disediakan.</summary>
        private readonly SuDesApp.Services.IIdmSimpananStore? _idmSimpanan;

        /// <summary>Data wilayah (penentu kode desa) untuk mencocokkan cadangan IDM; null bila tidak disediakan.</summary>
        private readonly SuDesApp.Wilayah.WilayahDataStore? _wilayah;
        private readonly SettingsManager _settingsManager;
        private readonly NotificationService _notifications;
        private readonly PembaruanLatarService _pembaruanLatar;

        /// <summary>Judul tetap notifikasi peringatan data desa contoh (dipakai untuk mencarinya lagi).</summary>
        private const string JudulNotifDataDesaContoh = "Data desa masih contoh — ganti dengan data Anda";

        /// <summary>Peringatan data desa contoh sudah dikirim pada sesi ini.</summary>
        private bool _sudahIngatkanDataDesaContoh;

        /// <summary>Penanda pemberitahuan sambutan (cukup sekali seumur pemasangan).</summary>
        private readonly IPemberitahuanSambutanStore _sambutanStore;

        private string _villageInfo = "DESA ... KECAMATAN ... KABUPATEN ...";
        private string _userName = "Operator";
        private string _screenInfo = string.Empty;
        private string _statusIdmTampil = StatusIdmBar.BelumAdaData;

        // ==== Indikator status langganan di statusbar (Full Service / Demo / Baca-saja) ====
        private string _langgananStatus = string.Empty;
        private string _langgananStatusTooltip = string.Empty;
        private bool _langgananChipVisible;
        private int _langgananTone;

        /// <summary>
        /// Kirim pemberitahuan sambutan bila belum pernah ditampilkan. Penandanya ditulis
        /// saat notifikasi dikirim, sehingga membuka aplikasi berikutnya — atau setelah
        /// daftar notifikasi dibersihkan pengguna — tidak memunculkannya kembali.
        ///
        /// Terbuka untuk uji supaya aturannya bisa diperiksa tanpa membangun seluruh
        /// jendela utama (yang butuh tema, database, dan layanan aplikasi lain).
        /// </summary>
        /// <returns>True bila notifikasi sambutan benar-benar dikirim.</returns>
        public static bool KirimPemberitahuanSambutan(
            IPemberitahuanSambutanStore store, NotificationService notifikasi, string namaPengguna)
        {
            if (store is null || notifikasi is null || !store.AmbilSekali())
            {
                return false;
            }

            notifikasi.Info(
                "Notifikasi aktif",
                $"Selamat datang, {namaPengguna}. Pemberitahuan aplikasi akan muncul di sini.");

            return true;
        }

        /// <summary>Pusat notifikasi lonceng status bar (ala Visual Studio).</summary>
        public NotificationService Notifications => _notifications;

        /// <summary>
        /// Pemilik pekerjaan pembaruan di latar belakang (periksa, unduh, notifikasi
        /// dua pilihan, dan pemasangan senyap). Jendela utama memakainya untuk
        /// memutuskan pemasangan saat ditutup.
        /// </summary>
        public PembaruanLatarService PembaruanLatar => _pembaruanLatar;

        public string VillageInfo
        {
            get => _villageInfo;
            set => SetProperty(ref _villageInfo, value);
        }

        /// <summary>Ringkasan resolusi layar untuk chip status bar (diisi ScreenResolutionService saat startup).</summary>
        public string ScreenInfo
        {
            get => _screenInfo;
            set => SetProperty(ref _screenInfo, value);
        }

        // ==== Status IDM di status bar (pojok kiri, di samping info desa) ====

        /// <summary>
        /// Status IDM desa di status bar — teks BIASA (bukan chip, permintaan
        /// tampilan): "Status Desa (IDM): Maju  •  0,7444 (2024)". Sumbernya
        /// cadangan halaman IDM (hasil terakhir yang berhasil diambil dari
        /// idm.kemendesa.go.id untuk desa pada Pengaturan Surat); belum pernah
        /// ada → "Status Desa (IDM): belum ada data".
        /// </summary>
        public string StatusIdmTampil
        {
            get => _statusIdmTampil;
            private set => SetProperty(ref _statusIdmTampil, value);
        }

        /// <summary>Baca ulang status IDM dari cadangan (startup, simpan pengaturan, halaman IDM memuat data).</summary>
        private void SegarkanStatusIdm() => _ = SegarkanStatusIdmAsync();

        internal async Task SegarkanStatusIdmAsync()
        {
            try
            {
                if (_idmSimpanan == null || _wilayah == null)
                {
                    StatusIdmTampil = StatusIdmBar.BelumAdaData;
                    if (_featureNavigation.IndeksDesa != null) _featureNavigation.IndeksDesa.StatusText = "Data IDM belum dimuat";
                    return;
                }

                string kode = await IdmDesaAktif.KodeAsync(_settingsManager, _wilayah);
                var cadangan = _idmSimpanan.Muat();
                StatusIdmTampil = StatusIdmBar.Susun(cadangan, kode);
                if (_featureNavigation.IndeksDesa != null)
                {
                    _featureNavigation.IndeksDesa.StatusText = SusunRingkasanIdmSidebar(cadangan, kode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyusun status IDM untuk status bar.");
            }
        }

        private static string SusunRingkasanIdmSidebar(SuDesApp.Services.IdmSimpanan? cadangan, string kodeDesa)
        {
            if (cadangan == null
                || cadangan.Hasil is not { Count: > 0 }
                || string.IsNullOrWhiteSpace(kodeDesa)
                || !string.Equals(cadangan.KodeDesa, kodeDesa, StringComparison.Ordinal)
                || cadangan.WaktuMuat is not DateTime waktuMuat)
            {
                return "Data IDM belum dimuat";
            }

            int tahun = cadangan.Hasil.Max(hasil => hasil.Ringkasan.Tahun);
            string tanggal = waktuMuat.ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
            return $"Data {tahun} · Diperbarui {tanggal}";
        }

        /// <summary>
        /// Cocokkan cadangan IDM dengan kode desa pada Pengaturan Surat (lewat data
        /// wilayah, sama seperti halaman IDM), lalu susun teksnya. Tanpa cadangan,
        /// kode tidak cocok, atau desa belum dipilih → "Status Desa (IDM): belum ada data".
        /// </summary>
        internal async Task<string> HitungStatusIdmAsync()
        {
            string kosong = StatusIdmBar.BelumAdaData;
            if (_idmSimpanan == null || _wilayah == null) return kosong;

            string kode = await IdmDesaAktif.KodeAsync(_settingsManager, _wilayah);
            if (kode.Length == 0) return kosong;

            return StatusIdmBar.Susun(_idmSimpanan.Muat(), kode);
        }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        /// <summary>Teks chip langganan di statusbar: "Full Service", "Demo", atau "Baca-saja" —
        /// dilengkapi sisa hari selama masih ada, supaya masa aktif terbaca langsung di chip
        /// tanpa perlu menahan kursor ke tooltip.</summary>
        public string LanggananStatus
        {
            get => _langgananStatus;
            private set => SetProperty(ref _langgananStatus, value);
        }

        public string LanggananStatusTooltip
        {
            get => _langgananStatusTooltip;
            private set => SetProperty(ref _langgananStatusTooltip, value);
        }

        /// <summary>Chip tampil selama penjaga lisensi tersedia (selalu, pada aplikasi normal).</summary>
        public bool LanggananChipVisible
        {
            get => _langgananChipVisible;
            private set => SetProperty(ref _langgananChipVisible, value);
        }

        /// <summary>Nada warna chip: 1 = aktif (hijau), 2 = Demo (kuning), 3 = baca-saja (merah).</summary>
        public int LanggananTone
        {
            get => _langgananTone;
            private set => SetProperty(ref _langgananTone, value);
        }

        /// <summary>
        /// Segarkan chip status langganan dari penjaga lisensi — satu sumber
        /// dengan gerbang fitur, jadi chip tidak bisa berbeda dari perilaku
        /// aplikasinya: "Full Service" (langganan sah), "Demo" (belum
        /// berlangganan), "Baca-saja" (langganan berakhir).
        /// </summary>
        private void TerapkanChipLisensi(SuDesApp.Services.PenjagaLisensi? penjaga)
        {
            LanggananChipVisible = penjaga is not null;
            if (penjaga is null)
            {
                LanggananStatus = string.Empty;
                LanggananStatusTooltip = string.Empty;
                _featureNavigation.TerapkanKunciLisensi(false, null);
                return;
            }

            // Gembok di sidebar mengikuti aturan yang sama dengan gerbang rute:
            // menu vital ditandai SEBELUM diklik, dan kalimat tooltip-nya
            // menyesuaikan jalan keluar yang tersedia (trial vs pembayaran).
            bool terkunci = penjaga.Terkunci;
            _featureNavigation.TerapkanKunciLisensi(
                terkunci,
                terkunci ? SuDesApp.Services.PenjagaLisensi.TooltipTerkunci(penjaga.Keadaan) : null);

            // Teks & nada berasal dari kebijakan Core (teruji); di sini hanya
            // kalimat tooltip yang khas UI. Sisa hari memakai sumber yang tepat:
            // trial lokal punya hitungannya sendiri, trial server & langganan
            // memakai masa tenggang token.
            int sisaHari = penjaga.Keadaan == SuDesApp.Services.KeadaanLisensi.Trial
                ? penjaga.SisaHariTrial
                : penjaga.Status?.SisaHariTenggang ?? 0;

            // Trial server sah sebagai token, tetapi bukan "Full Service" yang
            // sudah dibayar — chip harus tetap berbunyi Trial selama paket trial.
            var keadaanTampil = SuDesApp.Services.PenjagaLisensi.KeadaanTampil(
                penjaga.Keadaan, penjaga.Status?.Paket);
            LanggananStatus = SuDesApp.Services.PenjagaLisensi.LabelChip(keadaanTampil, sisaHari);
            LanggananTone = SuDesApp.Services.PenjagaLisensi.NadaKeadaan(keadaanTampil);

            var sampai = penjaga.Status?.BerlakuSampai?.ToLocalTime().ToString("dd MMM yyyy");
            var trialSampai = penjaga.TrialBerakhirUtc?.ToLocalTime().ToString("dd MMM yyyy");
            LanggananStatusTooltip = keadaanTampil switch
            {
                SuDesApp.Services.KeadaanLisensi.Berlangganan =>
                    "Langganan desa aktif" +
                    (sampai is null ? "" : $" s.d. {sampai}") +
                    ". Klik untuk membuka halaman Langganan.",

                // Trial lokal dan trial server dibedakan supaya tanggal yang
                // disebut benar-benar tanggal berakhir trial yang sedang berlaku.
                SuDesApp.Services.KeadaanLisensi.Trial when penjaga.Keadaan
                    == SuDesApp.Services.KeadaanLisensi.Trial =>
                    "Trial lokal 30 hari aktif" +
                    (trialSampai is null ? "" : $" sampai {trialSampai}") +
                    $" (sisa {penjaga.SisaHariTrial} hari). Klik untuk membuka halaman Langganan.",

                SuDesApp.Services.KeadaanLisensi.Trial =>
                    "Trial server aktif" +
                    (sampai is null ? "" : $" s.d. {sampai}") +
                    $" (sisa {sisaHari} hari). Setelah berakhir, selesaikan pembayaran di halaman Langganan.",

                SuDesApp.Services.KeadaanLisensi.BacaSaja =>
                    SuDesApp.Services.PenjagaLisensi.PesanTrialHabis +
                    " Klik untuk membuka halaman Langganan.",

                _ =>
                    "Mode uji coba belum dimulai: menu vital terkunci. Klik untuk memulai " +
                    "trial 30 hari atau mengaktifkan langganan."
            };
        }

        /// <summary>
        /// Laporkan hasil pemasangan pembaruan kecil yang dijalankan skrip penerap saat
        /// aplikasi ditutup. Hasilnya ditulis ke berkas di %LOCALAPPDATA% dan dibaca
        /// satu kali di sini supaya pengguna tahu apakah pembaruan benar-benar masuk.
        /// </summary>
        private Task LaporkanHasilTambalanAsync()
        {
            try
            {
                var hasil = PatchUpdateService.AmbilHasilTerakhir();
                if (hasil == null) return Task.CompletedTask;

                // Catat ke riwayat pembaruan agar bisa diperiksa kapan saja dari
                // halaman Pembaruan (versi, tanggal, jumlah berkas, hasil).
                try
                {
                    RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                    {
                        Versi = hasil.Versi,
                        Jenis = "Tambalan",
                        JumlahBerkas = hasil.JumlahBerkas,
                        Berhasil = hasil.Berhasil,
                        Pesan = hasil.Pesan,
                        Waktu = hasil.Waktu == default ? DateTime.Now : hasil.Waktu
                    });
                }
                catch (Exception exRiwayat)
                {
                    _logger.LogDebug(exRiwayat, "Riwayat pembaruan gagal dicatat.");
                }

                if (hasil.Berhasil)
                {
                    _notifications.Success(
                        $"Pembaruan {hasil.Versi} berhasil dipasang",
                        hasil.Pesan + " Aplikasi sudah memakai versi terbaru.",
                        "pembaruan");
                }
                else
                {
                    _notifications.Error(
                        $"Pembaruan {hasil.Versi} gagal dipasang",
                        hasil.Pesan + "\nCoba ulangi dari menu Pembaruan, atau pakai installer penuh (menu Pembaruan).",
                        "pembaruan");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hasil pemasangan pembaruan tidak terbaca.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Mulai pemantauan pembaruan di latar belakang setelah pengguna masuk.
        /// Pemeriksaan, unduhan, notifikasi, dan pemasangan seluruhnya dimiliki
        /// <see cref="PembaruanLatarService"/> — di sini hanya pengaturan waktu
        /// (boleh dimatikan / ditunda oleh mode diam-diam) supaya jaringan tidak
        /// berebut dengan pemuatan awal halaman.
        /// </summary>
        private async Task PeriksaPembaruanLatarAsync()
        {
            try
            {
                // Bisa dimatikan di Pengaturan Aplikasi (mis. aplikasi dipakai luring).
                if (!AppPreferenceStore.IsPeriksaPembaruanSaatMulai()) return;

                // Mode diam-diam startup: tunda pemeriksaan online sampai jeda menit
                // berlalu — jaringan tidak berebut dengan pemuatan awal halaman.
                if (AppPreferenceStore.IsStartupDiamDiam())
                {
                    await Task.Delay(TimeSpan.FromMinutes(AppPreferenceStore.GetStartupDiamDiamMenit()));
                }

                await _pembaruanLatar.MulaiAsync();
            }
            catch (Exception ex)
            {
                // Termasuk tanpa internet / repo tidak terjangkau: cukup dicatat.
                _logger.LogDebug(ex, "Pemeriksaan pembaruan latar belakang dilewati.");
            }
        }

        /// <summary>Tandai menu Pembaruan bahwa ada versi baru yang menunggu dipasang.</summary>
        private void TandaiMenuPembaruan()
        {
            var dispatcher = Application.Current?.Dispatcher;
            Action update = () =>
            {
                var pembaruan = _featureNavigation.Pembaruan;
                if (pembaruan == null) return;
                if (!pembaruan.Title.Contains("(baru)", StringComparison.Ordinal))
                {
                    pembaruan.Title = "Pembaruan (baru)";
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                _ = dispatcher.BeginInvoke(update);
            }
        }

        /// <summary>Segarkan badge akun Google di statusbar (bisa dipanggil dari luar).</summary>
        public Task RefreshGoogleBadgePublicAsync() => RefreshGoogleBadgeAsync();

        private async Task RefreshGoogleBadgeAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drive = scope.ServiceProvider.GetRequiredService<GoogleDriveService>();
                string? email = null;
                if (drive.IsOAuthEnabled && drive.HasStoredToken())
                {
                    try
                    {
                        email = await drive.GetAccountEmailAsync(default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal memeriksa status login Google");
                    }
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                Action update = () =>
                {
                    var google = _featureNavigation.Google;
                    if (google == null) return;
                    google.Title = string.IsNullOrWhiteSpace(email)
                        ? "Login dengan Google"
                        : ShortSidebarAccount(email);
                };
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    update();
                }
                else
                {
                    _ = dispatcher.BeginInvoke(update);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memperbarui badge Login Google");
            }
        }

        private static string ShortSidebarAccount(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "Google";
            return email.Length <= 22 ? email : email.Substring(0, 21) + "…";
        }

        /// <summary>
        /// Ringkasan info desa untuk status bar — huruf BIASA (bukan kapital semua,
        /// permintaan tampilan); dipisah dari pemuatan supaya bisa diuji unit.
        /// </summary>
        internal static string SusunInfoDesa(string namaDesa, string kecamatan, string kabupaten) =>
            "Desa " + namaDesa + "  •  Kec. " + kecamatan + "  •  Kab. " + kabupaten;

        public async Task LoadVillageInfoAsync()
        {
            try
            {
                var settings = await _settingsManager.GetSettingsAsync();
                if (settings != null && !string.IsNullOrWhiteSpace(settings.NamaDesa))
                {
                    VillageInfo = SusunInfoDesa(settings.NamaDesa, settings.Kecamatan ?? string.Empty, settings.Kabupaten ?? string.Empty);
                }

                IngatkanDataDesaContoh(settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat info desa untuk status bar.");
            }
        }

        /// <summary>
        /// Data desa bawaan aplikasi masih berupa contoh ("Nama Desa" dan sejenisnya).
        /// Selama masih contoh, pengguna diingatkan lewat lonceng notifikasi setiap kali
        /// aplikasi dibuka — notifikasi bisa diklik untuk langsung membuka Pengaturan Surat.
        /// Begitu data desa sudah diisi, notifikasi ini tidak dibuat lagi dan yang lama
        /// dibuang dari daftar (lihat PengaturanDesaDisimpan).
        /// </summary>
        private void IngatkanDataDesaContoh(SuDesApp.Data.Models.DesaData? desa)
        {
            var fieldContoh = SuDesApp.Data.Models.DesaContoh.FieldContoh(desa);
            if (fieldContoh.Count == 0)
            {
                // Sudah diisi: cukup pastikan tidak ada sisa peringatan lama.
                HapusPeringatanDataDesaContoh();
                return;
            }

            if (_sudahIngatkanDataDesaContoh) return;   // sekali per sesi saja
            _sudahIngatkanDataDesaContoh = true;

            _notifications.Warning(
                JudulNotifDataDesaContoh,
                $"Data berikut masih berisi contoh bawaan aplikasi: {SuDesApp.Data.Models.DesaContoh.RingkasField(fieldContoh)}." +
                "\nBuka Pengaturan Surat, lalu isi data desa Anda yang sebenarnya supaya surat yang dicetak tidak memakai data contoh.",
                "pengaturan-surat");
        }

        /// <summary>Buang peringatan data desa contoh dari lonceng (tidak berisik lagi).</summary>
        private void HapusPeringatanDataDesaContoh()
        {
            if (!_sudahIngatkanDataDesaContoh) return;
            _sudahIngatkanDataDesaContoh = false;

            var item = _notifications.Items.FirstOrDefault(i =>
                string.Equals(i.Title, JudulNotifDataDesaContoh, StringComparison.Ordinal));
            if (item != null)
            {
                _notifications.Hapus(item);
            }
        }

        /// <summary>
        /// Dipanggil setelah data desa disimpan di Pengaturan Surat: bila data contoh
        /// sudah diganti, peringatan pada lonceng langsung dibuang supaya tidak
        /// mengganggu lagi.
        /// </summary>
        private async Task PeriksaDataDesaSetelahSimpanAsync()
        {
            if (!_sudahIngatkanDataDesaContoh) return;

            try
            {
                var desa = await _settingsManager.GetSettingsAsync();
                if (!SuDesApp.Data.Models.DesaContoh.MasihContoh(desa))
                {
                    HapusPeringatanDataDesaContoh();
                    _logger.LogInformation("Data desa sudah disesuaikan: peringatan data contoh dibuang.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gagal memeriksa data desa setelah disimpan.");
            }
        }

        public void RefreshVillageInfo()
        {
            _ = LoadVillageInfoAsync();
        }
    }
}
