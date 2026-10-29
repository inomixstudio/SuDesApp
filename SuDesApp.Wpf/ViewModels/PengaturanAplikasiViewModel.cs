using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu baris pengaturan dengan tombol geser (toggle). Perubahan nilai
    /// LANGSUNG disimpan lewat aksi persist yang diberikan — tanpa tombol Simpan.
    /// </summary>
    public class SettingRowVM : ObservableObject
    {
        private readonly Action<bool> _persist;
        private bool _nilai;

        public string Judul { get; }
        public string Keterangan { get; }

        public bool Nilai
        {
            get => _nilai;
            set
            {
                if (SetProperty(ref _nilai, value))
                {
                    try
                    {
                        _persist(value);
                    }
                    catch
                    {
                        // Preferensi non-kritis — kegagalan menyimpan diabaikan.
                    }
                }
            }
        }

        public SettingRowVM(string judul, string keterangan, bool nilaiAwal, Action<bool> persist)
        {
            Judul = judul;
            Keterangan = keterangan;
            _nilai = nilaiAwal;
            _persist = persist;
        }
    }

    /// <summary>
    /// Satu pilihan cepat pada baris pengaturan bernilai angka (mis. Ringkas /
    /// Normal / Lega untuk tinggi maksimum baris tabel). Operator cukup menekan
    /// tombolnya — tidak perlu mengetik angka; angkanya sendiri tetap bisa
    /// disesuaikan lewat kotak isian di sebelahnya.
    /// </summary>
    public class PilihanAngkaVM : ObservableObject
    {
        private bool _aktif;

        public string Judul { get; }

        /// <summary>Nilai yang dipakai saat tombol ini ditekan.</summary>
        public int Nilai { get; }

        /// <summary>Sedang dipakai (nilai baris sama dengan nilai pilihan ini).</summary>
        public bool Aktif
        {
            get => _aktif;
            internal set => SetProperty(ref _aktif, value);
        }

        public RelayCommand PakaiCommand { get; }

        public PilihanAngkaVM(string judul, int nilai, Action<int> pakai)
        {
            Judul = judul;
            Nilai = nilai;
            PakaiCommand = new RelayCommand(() => pakai(nilai));
        }

        public override string ToString() => $"{Judul} ({Nilai})";
    }

    /// <summary>
    /// Baris pengaturan bernilai angka (mis. batas ukuran pembaruan otomatis dalam
    /// MB). Tampil sebagai kotak teks kecil di samping label; nilai di luar rentang
    /// ditolak dan dikembalikan ke nilai semula. Boleh dilengkapi pilihan cepat
    /// supaya nilai yang sering dipakai tidak perlu diketik.
    /// </summary>
    public class SettingRowAngkaVM : ObservableObject
    {
        private readonly Action<int> _persist;
        private readonly int _min;
        private readonly int _maks;
        private string _teks;

        public string Judul { get; }
        public string Keterangan { get; }

        /// <summary>Tombol nilai siap pakai (kosong = baris hanya punya kotak isian).</summary>
        public ObservableCollection<PilihanAngkaVM> PilihanCepat { get; } = new();

        public bool AdaPilihanCepat => PilihanCepat.Count > 0;

        public string TeksNilai
        {
            get => _teks;
            set
            {
                var baru = value?.Trim() ?? string.Empty;
                if (!SetProperty(ref _teks, baru)) return;

                if (int.TryParse(baru, out var angka) && angka >= _min && angka <= _maks)
                {
                    try { _persist(angka); } catch { /* preferensi non-kritis */ }
                }
                else
                {
                    // Kembalikan ke nilai tersimpan bila bukan angka valid.
                    SetProperty(ref _teks, _persistGet().ToString(), nameof(TeksNilai));
                }

                PerbaruiPilihanCepatAktif();
            }
        }

        private readonly Func<int> _persistGet;

        public SettingRowAngkaVM(
            string judul, string keterangan, int nilaiAwal,
            Action<int> persist, Func<int> persistGet,
            int min = 1, int maks = 999,
            IEnumerable<(string Judul, int Nilai)>? pilihanCepat = null)
        {
            Judul = judul;
            Keterangan = keterangan;
            _teks = nilaiAwal.ToString();
            _persist = persist;
            _persistGet = persistGet;
            _min = min;
            _maks = maks;

            if (pilihanCepat != null)
            {
                foreach (var (pilihanJudul, nilai) in pilihanCepat)
                {
                    // Di luar rentang tidak mungkin dipakai — lebih baik tidak ditawarkan.
                    if (nilai < _min || nilai > _maks) continue;
                    PilihanCepat.Add(new PilihanAngkaVM(pilihanJudul, nilai, Pakai));
                }
            }

            PerbaruiPilihanCepatAktif();
        }

        /// <summary>Tekan satu pilihan cepat: kotak isian ikut berubah dan nilainya tersimpan.</summary>
        private void Pakai(int angka)
        {
            if (angka < _min || angka > _maks) return;

            if (SetProperty(ref _teks, angka.ToString(), nameof(TeksNilai)))
            {
                try { _persist(angka); } catch { /* preferensi non-kritis */ }
            }

            PerbaruiPilihanCepatAktif();
        }

        private void PerbaruiPilihanCepatAktif()
        {
            bool adaAngka = int.TryParse(_teks, out var aktif);
            foreach (var pilihan in PilihanCepat)
            {
                pilihan.Aktif = adaAngka && aktif == pilihan.Nilai;
            }
        }
    }

    /// <summary>
    /// Satu pilihan interval pemantauan data IDM (setiap 6 jam / 24 jam / hanya
    /// saat aplikasi dibuka) di halaman Pengaturan Aplikasi. Teksnya diturunkan
    /// dari <see cref="IntervalPemantauanIdmPrefs" /> supaya judul di daftar,
    /// penjelasan, dan jeda yang benar-benar dipakai pemantau tidak melenceng.
    /// </summary>
    public class PilihanIntervalIdmVM
    {
        public PilihanIntervalIdmVM(IntervalPemantauanIdm nilai)
        {
            Nilai = nilai;
            Judul = IntervalPemantauanIdmPrefs.Judul(nilai);
            Keterangan = IntervalPemantauanIdmPrefs.Keterangan(nilai);
        }

        /// <summary>Nilai yang disimpan ke preferensi aplikasi.</summary>
        public IntervalPemantauanIdm Nilai { get; }

        public string Judul { get; }

        /// <summary>Penjelasan akibat pilihan ini pada pemantauan latar.</summary>
        public string Keterangan { get; }

        public override string ToString() => Judul;
    }

    /// <summary>
    /// Satu pilihan kecepatan animasi antarmuka (Lambat/Normal/Cepat) di halaman
    /// Pengaturan Aplikasi. Semua teksnya diturunkan dari aturan di
    /// <see cref="KecepatanAnimasiPrefs"/>, sehingga pilihan kartu, penjelasan, dan
    /// durasi yang benar-benar dipakai animasi tidak bisa saling melenceng.
    /// </summary>
    public class KecepatanAnimasiPilihanVM
    {
        public KecepatanAnimasiPilihanVM(KecepatanAnimasi nilai)
        {
            Nilai = nilai;
            Judul = KecepatanAnimasiPrefs.Judul(nilai);
            FaktorTeks = KecepatanAnimasiPrefs.FaktorTeks(nilai);
            Keterangan = KecepatanAnimasiPrefs.Keterangan(nilai);
            ContohSidebar = "sidebar " + KecepatanAnimasiPrefs.ContohTeks(nilai, KecepatanAnimasiPrefs.DasarSidebar);
            ContohDropdown = "dropdown " + KecepatanAnimasiPrefs.ContohTeks(nilai, KecepatanAnimasiPrefs.DasarDropdown);
            // Glyph Segoe Fluent Icons: pelan = jam, normal = centang, cepat = kilat.
            Ikon = nilai switch
            {
                KecepatanAnimasi.Lambat => IkonMenu.Riwayat,      // \uE81C jam
                KecepatanAnimasi.Cepat => "\uE945",               // kilat ganda
                _ => "\uE73E"                                     // centang
            };
        }

        /// <summary>Nilai yang disimpan ke preferensi aplikasi.</summary>
        public KecepatanAnimasi Nilai { get; }

        public string Judul { get; }

        /// <summary>Pengali dibanding bawaan, mis. "1,9× lebih lambat dari bawaan".</summary>
        public string FaktorTeks { get; }

        /// <summary>Kapan pilihan ini cocok dipakai.</summary>
        public string Keterangan { get; }

        public string Ikon { get; }

        /// <summary>Durasi nyata transisi sidebar pada pilihan ini, mis. "sidebar 0,49 dtk".</summary>
        public string ContohSidebar { get; }

        /// <summary>Durasi nyata dropdown grup pada pilihan ini, mis. "dropdown 0,46 dtk".</summary>
        public string ContohDropdown { get; }

        /// <summary>Ringkasan durasi kedua transisi utama untuk kartu pilihan.</summary>
        public string ContohTeks => ContohSidebar + " · " + ContohDropdown;

        public override string ToString() => Judul;
    }

    /// <summary>
    /// Satu bagian pada navigasi halaman Pengaturan Aplikasi. Hanya bagian yang
    /// aktif yang ditampilkan, sehingga halaman tetap ringkas walau pengaturannya
    /// sudah banyak.
    /// </summary>
    public class PengaturanSectionVM : ObservableObject
    {
        private bool _aktif;

        /// <summary>Kunci bagian; sama dengan Tag kartu untuk fokus dari statusbar.</summary>
        public string Key { get; }
        public string Judul { get; }
        public string Ikon { get; }
        public string Ringkasan { get; }

        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        public PengaturanSectionVM(string key, string judul, string ikon, string ringkasan)
        {
            Key = key;
            Judul = judul;
            Ikon = ikon;
            Ringkasan = ringkasan;
        }
    }

    /// <summary>
    /// Halaman Pengaturan Aplikasi — pusat preferensi perilaku aplikasi
    /// (berbeda dari Pengaturan Surat yang mengelola data desa).
    ///
    /// Isinya dikelompokkan menjadi beberapa bagian dengan navigasi sendiri di
    /// sisi kiri, dan setiap tombol geser tersimpan langsung saat digeser.
    /// </summary>
    public class PengaturanAplikasiViewModel : ObservableObject
    {
        private readonly IServiceProvider _provider;
        private readonly NavigationService _navigation;
        private readonly ILogger<PengaturanAplikasiViewModel> _logger;
        private readonly GoogleDriveService? _googleDrive;
        private readonly AppConfig? _appConfig;
        private readonly IMessageService? _pesan;

        public ObservableCollection<SettingRowVM> Rows { get; } = new();

        /// <summary>Baris pengaturan bernilai angka (mis. batas ukuran MB).</summary>
        public ObservableCollection<SettingRowAngkaVM> RowsAngka { get; } = new();

        // ===== Kecepatan animasi antarmuka (Lambat/Normal/Cepat) =====

        /// <summary>
        /// Pilihan kecepatan animasi untuk kartu pengaturan. Urutannya tetap
        /// Lambat → Normal → Cepat (paling halus ke paling gesit).
        /// </summary>
        public static IReadOnlyList<KecepatanAnimasiPilihanVM> DaftarKecepatanAnimasi()
            => new[]
            {
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Lambat),
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Normal),
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Cepat)
            };

        public IReadOnlyList<KecepatanAnimasiPilihanVM> PilihanKecepatanAnimasi { get; }
            = DaftarKecepatanAnimasi();

        // ===== Interval pemantauan data IDM (6 jam / 24 jam / hanya saat dibuka) =====

        /// <summary>
        /// Pilihan interval pemantauan IDM. Urutannya mengikuti aturan di
        /// <see cref="IntervalPemantauanIdmPrefs"/>: paling sering → harian →
        /// tanpa pengulangan.
        /// </summary>
        public static IReadOnlyList<PilihanIntervalIdmVM> DaftarIntervalIdm()
            => new[]
            {
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.EnamJam),
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.DuaPuluhEmpatJam),
                new PilihanIntervalIdmVM(IntervalPemantauanIdm.HanyaSaatDibuka)
            };

        public IReadOnlyList<PilihanIntervalIdmVM> PilihanIntervalIdm { get; }
            = DaftarIntervalIdm();

        private PilihanIntervalIdmVM _intervalIdmTerpilih;

        /// <summary>
        /// Interval pemantauan IDM yang dipilih. Tersimpan langsung saat dipilih
        /// dan langsung diterapkan ke pemantau latar — tanpa menutup aplikasi.
        /// </summary>
        public PilihanIntervalIdmVM IntervalIdmTerpilih
        {
            get => _intervalIdmTerpilih;
            set
            {
                if (value == null || ReferenceEquals(value, _intervalIdmTerpilih)) return;
                if (!SetProperty(ref _intervalIdmTerpilih, value)) return;

                IntervalPemantauanIdmPrefs.Simpan(value.Nilai);
                OnPropertyChanged(nameof(IntervalIdmKeterangan));
                _statusSesaat.Tampilkan($"Tersimpan — pemantauan IDM: {value.Judul}");

                // Terapkan segera: hentikan pengulangan, atau ganti jeda timer
                // pemantau yang sedang berjalan. Null-safe karena halaman
                // pengaturan juga dipakai di sesi tanpa layanan latar (uji).
                _provider.GetService<PemantauIdmBaru>()?.TerapkanPreferensi();
            }
        }

        /// <summary>Penjelasan pilihan interval pemantauan yang sedang dipilih.</summary>
        public string IntervalIdmKeterangan => _intervalIdmTerpilih.Keterangan;

        private KecepatanAnimasiPilihanVM _kecepatanAnimasiTerpilih;

        /// <summary>
        /// Kecepatan animasi yang dipilih. Tersimpan langsung saat dipilih, dan
        /// langsung berlaku pada animasi berikutnya — tidak perlu menutup aplikasi.
        /// </summary>
        public KecepatanAnimasiPilihanVM KecepatanAnimasiTerpilih
        {
            get => _kecepatanAnimasiTerpilih;
            set
            {
                if (value == null || ReferenceEquals(value, _kecepatanAnimasiTerpilih)) return;
                if (!SetProperty(ref _kecepatanAnimasiTerpilih, value)) return;

                KecepatanAnimasiPrefs.Simpan(value.Nilai);

                OnPropertyChanged(nameof(KecepatanAnimasiContohInfo));
                _statusSesaat.Tampilkan($"Tersimpan — kecepatan animasi: {value.Judul}");
            }
        }

        /// <summary>Keterangan kecepatan yang sedang berlaku beserta durasi nyatanya.</summary>
        public string KecepatanAnimasiContohInfo =>
            $"Contoh di bawah memakai kecepatan {_kecepatanAnimasiTerpilih.Judul}: " +
            $"sidebar {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSidebar)}, " +
            $"dropdown {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarDropdown)}, " +
            $"sorotan menu {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSorotMasuk)}.";

        public PengaturanAplikasiViewModel(
            NavigationService navigation,
            ILogger<PengaturanAplikasiViewModel> logger,
            IServiceProvider provider)
        {
            _navigation = navigation;
            _logger = logger;
            _provider = provider;
            _statusSesaat = new StatusSesaat(p => StatusMessage = p, StatusSiap);

            _googleDrive = _provider.GetService<GoogleDriveService>();
            _appConfig = _provider.GetService<AppConfig>();
            _pesan = _provider.GetService<IMessageService>();

            PilihBerkasDatabaseCommand = new AsyncRelayCommand(PilihBerkasDatabaseAsync);
            PakaiDatabaseBawaanCommand = new RelayCommand(PakaiDatabaseBawaan, () => DatabasePunyaPilihan);
            BukaFolderDatabaseCommand = new RelayCommand(BukaFolderDatabase);
            SimpanMulaiUlangDatabaseCommand = new AsyncRelayCommand(SimpanMulaiUlangDatabaseAsync, () => !DatabaseSibuk);

            // Kredensial OAuth: nilai yang sudah tersimpan dimuat supaya teknisi
            // bisa memeriksa/memperbarui tanpa mengetik ulang dari nol.
            var kredensialTersimpan = GoogleClientCredentials.Load();
            _googleClientId = kredensialTersimpan?.ClientId ?? string.Empty;
            _googleClientSecret = kredensialTersimpan?.ClientSecret ?? string.Empty;
            SimpanKredensialGoogleCommand = new AsyncRelayCommand(SimpanKredensialGoogleAsync);
            UjiKredensialGoogleCommand = new AsyncRelayCommand(UjiKredensialGoogleAsync);

            // Langganan sudah pindah menjadi halaman tersendiri di navigasi menu
            // utama (LanggananViewModel / LanggananView) — tidak lagi di sini.

            // Kecepatan animasi yang sedang berlaku dipasang sebagai pilihan terpilih;
            // nilainya ditulis langsung ke bidang (bukan properti) supaya membuka halaman
            // ini tidak dianggap sebagai perubahan pengguna.
            _kecepatanAnimasiTerpilih = PilihanKecepatanAnimasi
                .First(p => p.Nilai == KecepatanAnimasiPrefs.SaatIni);

            // Sama seperti kecepatan animasi: diisi lewat bidang (bukan properti)
            // supaya membuka halaman ini tidak dianggap sebagai perubahan pengguna.
            _intervalIdmTerpilih = PilihanIntervalIdm
                .First(p => p.Nilai == IntervalPemantauanIdmPrefs.SaatIni);

            SiapkanBagian();

            TambahBaris(new SettingRowVM(
                "Backup otomatis ke Google Drive saat aplikasi ditutup",
                "Mengunggah cadangan database, lampiran, dan template ke folder SuDesApp-Backup di Google Drive setiap aplikasi ditutup (maksimal sekali sehari, hanya bila ada akun Google yang terhubung).",
                AppPreferenceStore.IsBackupDriveOnExitEnabled(),
                v => AppPreferenceStore.SetBackupDriveOnExitEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Backup otomatis formulir ke Google Drive",
                "Menyinkronkan template formulir PDF ke folder SuDesApp-Formulir di Google Drive setiap ada unduhan baru atau template ditambah/dihapus pada Pengaturan Formulir. Berjalan hanya bila ada akun Google yang terhubung.",
                AppPreferenceStore.IsAutoBackupFormulirDriveEnabled(),
                v => AppPreferenceStore.SetAutoBackupFormulirDriveEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Periksa pembaruan aplikasi saat dibuka",
                "Saat aktif, aplikasi memeriksa rilis terbaru di latar belakang ketika dibuka lalu memberitahu lewat lonceng notifikasi apa yang diperbaiki. Perbaikan kecil dipasang sebagai pembaruan tambalan (hanya berkas yang berubah, tanpa installer), perubahan besar memakai installer penuh. Saat nonaktif, pemeriksaan hanya dijalankan dari menu Pembaruan.",
                AppPreferenceStore.IsPeriksaPembaruanSaatMulai(),
                v => AppPreferenceStore.SetPeriksaPembaruanSaatMulai(v)));

            TambahBaris(new SettingRowVM(
                "Unduh pembaruan otomatis di latar belakang",
                "Saat aktif, pembaruan yang ditemukan langsung diunduh di latar belakang tanpa perlu membuka menu Pembaruan. Setelah selesai, lonceng notifikasi menawarkan \"Instal Sekarang\" atau \"Instal Saat Keluar\". Saat nonaktif, pembaruan hanya diberitahukan dan diunduh bila Anda memintanya dari menu Pembaruan.",
                AppPreferenceStore.IsUnduhPembaruanOtomatis(),
                v => AppPreferenceStore.SetUnduhPembaruanOtomatis(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Batas ukuran unduhan otomatis (MB)",
                "Ukuran maksimal installer penuh yang boleh diunduh di latar belakang (10-2048 MB, bawaan 300 MB). Rilis yang lebih besar tetap diberitahukan lewat notifikasi dan hanya diunduh bila Anda memintanya dari menu Pembaruan, sehingga kuota internet tidak terpakai tanpa sepengetahuan Anda.",
                AppPreferenceStore.GetBatasUkuranUnduhanMb(),
                v => AppPreferenceStore.SetBatasUkuranUnduhanMb(v),
                () => AppPreferenceStore.GetBatasUkuranUnduhanMb(),
                min: 10, maks: 2048));

            TambahBaris(new SettingRowVM(
                "Pasang pembaruan kecil otomatis saat aplikasi ditutup",
                "Saat aktif, pembaruan kecil (tambalan: hanya berkas yang berubah, data surat & pengaturan aman) yang sudah terunduh dipasang otomatis ketika aplikasi ditutup — tanpa menanya lagi; aplikasi dibuka kembali dengan versi terbaru. Installer penuh TIDAK ikut alur ini: installer dipasang hanya bila Anda memilih \"Instal Sekarang\" atau \"Instal Saat Keluar\" pada notifikasi. Bila paketnya belum siap, aplikasi keluar seperti biasa dan unduhan diteruskan pada pembukaan berikutnya.",
                AppPreferenceStore.IsPasangOtomatisSaatKeluar(),
                v => AppPreferenceStore.SetPasangOtomatisSaatKeluar(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Batas ukuran pembaruan kecil otomatis (MB)",
                "Ukuran maksimal perubahan yang boleh diunduh & dipasang sebagai pembaruan kecil di latar belakang (1-500 MB, bawaan 25 MB). Paket yang lebih besar tetap ditawarkan lewat notifikasi dan dipasang manual dari menu Pembaruan.",
                AppPreferenceStore.GetBatasUkuranTambalanMb(),
                v => AppPreferenceStore.SetBatasUkuranTambalanMb(v),
                () => AppPreferenceStore.GetBatasUkuranTambalanMb(),
                min: 1, maks: 500));

            TambahBaris(new SettingRowVM(
                "Mode diam-diam saat aplikasi dibuka",
                "Saat aktif, pekerjaan latar yang tidak penting bagi pengguna — pemeriksaan pembaruan online serta pembersihan PDF sementara, hasil ekspor lama, dan draft kedaluwarsa — ditunda beberapa menit setelah aplikasi dibuka, sehingga jendela login dan halaman utama muncul lebih cepat. Rotasi log tetap berjalan sejak dini karena kecil dan diperlukan.",
                AppPreferenceStore.IsStartupDiamDiam(),
                v => AppPreferenceStore.SetStartupDiamDiam(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Jeda mode diam-diam (menit)",
                "Berapa menit pekerjaan latar ditunda setelah aplikasi dibuka (1-60 menit, bawaan 5 menit). Semakin besar jeda, semakin ringan pembukaan aplikasi; pembaruan dan pembersihan tetap berjalan otomatis setelah jeda berlalu.",
                AppPreferenceStore.GetStartupDiamDiamMenit(),
                v => AppPreferenceStore.SetStartupDiamDiamMenit(v),
                () => AppPreferenceStore.GetStartupDiamDiamMenit(),
                min: 1, maks: 60));

            TambahBaris(new SettingRowVM(
                "Riwayat aktivitas",
                "Mencatat siapa (email Google atau admin) yang membuat, mengubah, mengubah status, dan menghapus surat maupun arsip. Catatannya tampil pada menu Riwayat Aktivitas.",
                AppPreferenceStore.IsActivityLoggingEnabled(),
                v => AppPreferenceStore.SetActivityLoggingEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Bersihkan PDF sementara saat aplikasi dibuka",
                "Menghapus berkas pratinjau PDF lama di folder TempPDF agar tidak menumpuk selama pemakaian.",
                AppPreferenceStore.IsCleanupTempPdfEnabled(),
                v => AppPreferenceStore.SetCleanupTempPdfEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Hapus hasil ekspor lama (lebih dari 30 hari)",
                "Berkas PDF hasil ekspor pada folder Output/PDF yang berumur lebih dari 30 hari dihapus otomatis ketika aplikasi dibuka.",
                AppPreferenceStore.IsCleanupOldExportsEnabled(),
                v => AppPreferenceStore.SetCleanupOldExportsEnabled(v)));

            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        /// <summary>Sesi aktif sekarang (dibaca ulang tiap halaman dibuka).</summary>
        public string SesiAktif =>
            $"{SessionContext.Display} — {SessionContext.LoginMethod} — sesi dimulai {SessionContext.LoginTime:dd-MM-yyyy HH:mm}";

        /// <summary>Lokasi penyimpanan preferensi.</summary>
        public string LokasiPreferensi => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp", "login_prefs.json");

        // =====================================================================
        // Lokasi berkas database desa.
        //
        // Database desa boleh disimpan di folder datanya sendiri (mis. drive D
        // atau folder arsip desa) supaya tidak tercampur berkas aplikasi dan
        // tidak perlu disalin ulang setiap aplikasi diperbarui. Pilihannya
        // disimpan di preferensi pengguna lalu dibaca AppConfig saat aplikasi
        // dibuka — jadi baru berlaku setelah aplikasi ditutup lalu dibuka lagi.
        // =====================================================================

        /// <summary>Buka pemilih berkas untuk menentukan lokasi database desa.</summary>
        public AsyncRelayCommand PilihBerkasDatabaseCommand { get; }

        /// <summary>Simpan pilihan lokasi lalu tutup & buka kembali aplikasi sekarang.</summary>
        public AsyncRelayCommand SimpanMulaiUlangDatabaseCommand { get; }

        /// <summary>Kembalikan penggunaan database ke lokasi bawaan (folder aplikasi).</summary>
        public RelayCommand PakaiDatabaseBawaanCommand { get; }

        /// <summary>Buka folder lokasi database yang sedang dipakai di Windows Explorer.</summary>
        public RelayCommand BukaFolderDatabaseCommand { get; }

        /// <summary>Lokasi database yang sedang dipakai aplikasi (dibaca saat start-up).</summary>
        public string LokasiDatabaseAktif => string.IsNullOrWhiteSpace(_appConfig?.DatabasePath)
            ? "(tidak diketahui)"
            : _appConfig!.DatabasePath;

        /// <summary>Lokasi database bawaan dari appsettings (desa.db di folder aplikasi).</summary>
        public string LokasiDatabaseBawaan => string.IsNullOrWhiteSpace(_appConfig?.DatabasePathBawaan)
            ? "(tidak diketahui)"
            : _appConfig!.DatabasePathBawaan;

        /// <summary>True bila lokasi yang sedang dipakai berasal dari pilihan pengguna.</summary>
        public bool DatabaseAktifKustom => _appConfig?.DatabaseKustom ?? false;

        /// <summary>True bila sedang dipakai lokasi bawaan (tombol kembali ke bawaan dimatikan).</summary>
        public bool DatabasePakaiBawaan => !DatabaseAktifKustom && !DatabasePunyaPilihan;

        /// <summary>Ada pilihan lokasi tersimpan — baik sudah berlaku maupun menunggu aplikasi dibuka ulang.</summary>
        public bool DatabasePunyaPilihan => !string.IsNullOrWhiteSpace(AppPreferenceStore.GetJalurDatabase());

        /// <summary>
        /// Keterangan pilihan lokasi yang sudah tersimpan tetapi BELUM berlaku karena
        /// aplikasi belum dibuka ulang; kosong bila tidak ada pilihan menggantung.
        /// </summary>
        public string DatabasePilihanTersimpan
        {
            get
            {
                var jalur = AppPreferenceStore.GetJalurDatabase();
                if (string.IsNullOrWhiteSpace(jalur)) return string.Empty;

                return jalur.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $"Menunggu aplikasi dibuka ulang: {jalur}";
            }
        }

        /// <summary>True bila ada pilihan lokasi yang menunggu aplikasi dibuka ulang.</summary>
        public bool AdanyaDatabasePilihanTersimpan => DatabasePilihanTersimpan.Length > 0;

        private string _infoDatabase = string.Empty;

        /// <summary>Hasil tindakan terakhir pada bagian Database (kosong = belum ada tindakan).</summary>
        public string InfoDatabase
        {
            get => _infoDatabase;
            private set
            {
                if (SetProperty(ref _infoDatabase, value))
                    OnPropertyChanged(nameof(AdanyaInfoDatabase));
            }
        }

        /// <summary>True bila ada keterangan hasil tindakan untuk ditampilkan.</summary>
        public bool AdanyaInfoDatabase => InfoDatabase.Length > 0;

        /// <summary>Pilih berkas database desa lain lewat pemilih berkas Windows.</summary>
        private Task PilihBerkasDatabaseAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pilih berkas database desa",
                    Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false,
                    InitialDirectory = FolderDatabase(),
                };

                if (dialog.ShowDialog() != true) return Task.CompletedTask;

                var jalur = Path.GetFullPath(dialog.FileName);

                if (jalur.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase))
                {
                    InfoDatabase = "Berkas itu sudah dipakai aplikasi saat ini.";
                    return Task.CompletedTask;
                }

                // Periksa lebih dahulu agar berkas yang startup pasti tolak tidak
                // tersimpan sebagai pilihan: kondisi itu bisa mengunci pengguna
                // keluar dari aplikasi sebelum ia sempat memilih database lain.
                switch (BerkasDatabase.Periksa(jalur))
                {
                    case StatusBerkasDatabase.BerkasLamaTerkunci:
                        InfoDatabase =
                            "Berkas ini bukan database SQLite biasa dan tidak didukung " +
                            "(kemungkinan database terenkripsi dari versi SuDesApp lama). " +
                            "Pilihan tidak disimpan. Pilih database lain atau pulihkan dari " +
                            "cadangan .db yang lebih baru.";
                        return Task.CompletedTask;

                    case StatusBerkasDatabase.TidakTerbaca:
                        InfoDatabase =
                            "Berkas SQLite ini tidak dapat dibaca dan kemungkinan rusak atau " +
                            "tidak lengkap. Pilihan tidak disimpan. Pilih berkas lain atau " +
                            "pulihkan dari cadangan.";
                        return Task.CompletedTask;
                }

                AppPreferenceStore.SetJalurDatabase(jalur);
                SegarkanInfoDatabase();
                _statusSesaat.Tampilkan("Lokasi database disimpan — berlaku setelah aplikasi dibuka ulang.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memilih berkas database");
                InfoDatabase = "Berkas database tidak dapat dipilih dari sini.";
            }

            return Task.CompletedTask;
        }

        /// <summary>Kembali memakai database bawaan aplikasi (folder aplikasi).</summary>
        private void PakaiDatabaseBawaan()
        {
            AppPreferenceStore.SetJalurDatabase(null);
            SegarkanInfoDatabase();
            _statusSesaat.Tampilkan(DatabaseAktifKustom
                ? "Pilihan lokasi dihapus — aplikasi memakai database bawaan setelah dibuka ulang."
                : "Sudah memakai database bawaan aplikasi.");
        }

        /// <summary>
        /// Tombol "Simpan & Mulai Ulang Sekarang" pada bagian Database Desa.
        /// Catatan penting tentang urutan data: PilihBerkasDatabaseAsync sudah
        /// menyimpan pilihan ke preferensi SAAAT berkas dipilih (bukan saat tombol
        /// ini ditekan), jadi tombol ini hanya (1) memastikan memang ada perubahan
        /// yang menunggu diterapkan, (2) meminta konfirmasi terakhir, lalu
        /// (3) menutup dan membuka kembali aplikasi.
        /// </summary>
        private async Task SimpanMulaiUlangDatabaseAsync()
        {            // Tidak ada yang perlu diterapkan = jangan buang waktu pengguna
            // dengan mulai ulang sia-sia. Dua keadaan demikian: (1) pilihan tersimpan
            // SUDAH dipakai aplikasi saat ini, (2) tidak ada pilihan dan aplikasi
            // memang sedang memakai lokasi bawaan.
            var pilihan = AppPreferenceStore.GetJalurDatabase();
            var sudahBerlaku = !string.IsNullOrWhiteSpace(pilihan)
                && pilihan.Equals(LokasiDatabaseAktif, StringComparison.OrdinalIgnoreCase);

            if (sudahBerlaku)
            {
                InfoDatabase = "Lokasi database yang tersimpan sudah dipakai aplikasi saat ini — "
                    + "tidak ada perubahan yang menunggu diterapkan.";
                return;
            }

            if (string.IsNullOrWhiteSpace(pilihan) && !DatabaseAktifKustom)
            {
                InfoDatabase = "Pilih berkas database terlebih dahulu (atau tekan \"Kembali ke Bawaan\" "
                    + "bila sedang memakai lokasi lain) — belum ada perubahan yang menunggu diterapkan.";
                return;
            }

            var namaLokasi = DatabaseAktifKustom
                ? Path.GetFileName(LokasiDatabaseAktif)
                : "database bawaan (folder aplikasi)";

            var setuju = await KonfirmasiDatabaseAsync(
                "Simpan & Mulai Ulang Sekarang",
                "Aplikasi akan ditutup lalu dibuka kembali sekarang juga supaya lokasi database baru "
                + "(\"" + namaLokasi + "\") langsung dipakai.\n\n"
                + "Pastikan tidak ada proses cetak atau pengisian surat yang sedang berjalan.");

            if (!setuju) return;

            try
            {
                DatabaseSibuk = true;

                // 1. Pasang penanda "lahir dari mulai ulang" untuk proses baru.
                MulaiUlangAplikasi.PasangPenanda();

                // 2. Jadwalkan pembukaan kembali: proses pembantu menunggu proses ini
                //    benar-benar selesai (termasuk backup Drive di OnExit) sebelum
                //    menjalankan exe yang sama. Gagal jadwal = JANGAN menutup aplikasi.
                if (!MulaiUlangAplikasi.JadwalkanProsesBaru())
                {
                    MulaiUlangAplikasi.BersihkanPenanda();
                    InfoDatabase = "Pembukaan kembali gagal dijadwalkan — aplikasi tidak ditutup. "
                        + "Lokasi database tetap tersimpan dan berlaku pada pembukaan berikutnya.";
                    return;
                }

                InfoDatabase = "Menutup aplikasi untuk mulai ulang…";

                // 3. Tutup jendela utama dengan melewati dialog "Yakin ingin keluar?"
                //    (penutupan ini disengaja) lalu OnExit berjalan normal — backup
                //    Drive saat keluar ikut selesai sebelum proses baru mengambil alih.
                (Application.Current.MainWindow as MainWindow)?.KeluarUntukMulaiUlang();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan mulai ulang untuk penerapan lokasi database");
                MulaiUlangAplikasi.BersihkanPenanda();
                InfoDatabase = "Mulai ulang gagal dijalankan: " + ex.Message;
            }
            finally
            {
                // Shutdown sudah berjalan — ini hanya jaring pengaman bila penutupan
                // dibatalkan (mis. jendela ditutup lewat tombol lain).
                DatabaseSibuk = false;
            }
        }

        /// <summary>Buka folder tempat database yang sedang dipakai berada.</summary>
        private void BukaFolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(LokasiDatabaseAktif);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    InfoDatabase = "Folder database tidak ditemukan.";
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                _statusSesaat.Tampilkan("Folder database dibuka: " + folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder database");
                InfoDatabase = "Folder database tidak dapat dibuka dari sini.";
            }
        }

        /// <summary>Folder awal pemilih berkas: folder database yang sedang dipakai.</summary>
        private string? FolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(LokasiDatabaseAktif);
                return Directory.Exists(folder) ? folder : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Konfirmasi ke pengguna; bila layanan pesan tidak tersedia, pilihan dianggap setuju.</summary>
        private async Task<bool> KonfirmasiDatabaseAsync(string judul, string pesan)
        {
            if (_pesan == null) return true;

            try
            {
                return await _pesan.ShowConfirmationAsync(judul, pesan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dialog konfirmasi database gagal ditampilkan");
                return true;
            }
        }

        /// <summary>Segarkan seluruh tampilan bagian Database setelah pilihan berubah.</summary>
        private void SegarkanInfoDatabase()
        {
            InfoDatabase = DatabasePunyaPilihan
                ? "Lokasi database tersimpan. Tutup lalu buka kembali aplikasi supaya lokasi baru dipakai."
                : "Aplikasi memakai database bawaan di folder aplikasi.";

            OnPropertyChanged(nameof(DatabasePunyaPilihan));
            OnPropertyChanged(nameof(DatabasePakaiBawaan));
            OnPropertyChanged(nameof(DatabasePilihanTersimpan));
            OnPropertyChanged(nameof(AdanyaDatabasePilihanTersimpan));
            PakaiDatabaseBawaanCommand.RaiseCanExecuteChanged();
        }

        // =====================================================================
        // Pesan status halaman.
        //
        // Identitas aplikasi (ikon, nama, versi, pengembang) tidak lagi disimpan di
        // sini — statusbar kaki halaman memakai kontrol bersama AppStatusBar yang
        // membacanya langsung dari atribut assembly.
        // =====================================================================

        /// <summary>Pesan tetap saat tidak ada perubahan yang baru disimpan.</summary>
        private const string StatusSiap = "Semua perubahan tersimpan otomatis — tidak ada yang perlu diklik Simpan.";

        private string _statusMessage = StatusSiap;
        private readonly StatusSesaat _statusSesaat;

        /// <summary>Pesan pada statusbar halaman (berubah sesaat setelah menyimpan).</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>Tambahkan baris sakelar, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBaris(SettingRowVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowVM.Nilai))
                    _statusSesaat.Tampilkan($"Tersimpan — {baris.Judul}");
            };
            Rows.Add(baris);
        }

        /// <summary>Tambahkan baris bernilai angka, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBarisAngka(SettingRowAngkaVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowAngkaVM.TeksNilai))
                    _statusSesaat.Tampilkan($"Tersimpan — {baris.Judul}");
            };
            RowsAngka.Add(baris);
        }

        // =======================================================================
        // Kredensial klien OAuth Google (Client ID & Client Secret).
        //
        // Paket rilis umumnya sudah memuat kredensial tertanam (dibangun dengan
        // berkas rahasia build di luar git). Isian ini tetap wajib ada: paket yang
        // dibangun tanpa berkas rahasia itu (mis. hasil CI) TIDAK memuat kredensial,
        // sehingga teknisi harus bisa menghidupkan login Google di komputer
        // pengguna tanpa membangun ulang installer.
        // =======================================================================
        private string _googleClientId = string.Empty;
        private string _googleClientSecret = string.Empty;
        private string _googleKredensialInfo = string.Empty;
        private int _googleKredensialTone;

        public string GoogleClientId
        {
            get => _googleClientId;
            set => SetProperty(ref _googleClientId, value ?? string.Empty);
        }

        public string GoogleClientSecret
        {
            get => _googleClientSecret;
            set => SetProperty(ref _googleClientSecret, value ?? string.Empty);
        }

        /// <summary>Pesan hasil simpan/uji kredensial di kartu Kredensial Google.</summary>
        public string GoogleKredensialInfo
        {
            get => _googleKredensialInfo;
            private set => SetProperty(ref _googleKredensialInfo, value);
        }

        /// <summary>Warna kotak pesan: 0 netral, 1 sukses, 2 peringatan, 3 galat.</summary>
        public int GoogleKredensialTone
        {
            get => _googleKredensialTone;
            private set => SetProperty(ref _googleKredensialTone, value);
        }

        /// <summary>Lokasi berkas kredensial terenkripsi DPAPI (untuk ditampilkan ke teknisi).</summary>
        public string GoogleKredensialLokasi => GoogleDriveService.CredentialsStorePath;

        /// <summary>Status apakah klien OAuth sudah aktif, sekaligus sumber kredensialnya.</summary>
        public string GoogleKredensialStatus => _googleDrive == null
            ? "Layanan Google tidak tersedia pada sesi ini."
            : _googleDrive.IsOAuthEnabled
                ? "Klien OAuth aktif — sumber kredensial: " + _googleDrive.CredentialSource + "."
                : "Klien OAuth belum aktif — isi Client ID dan Client Secret di bawah, lalu simpan.";

        /// <summary>
        /// Keterangan apakah paket aplikasi ini membawa kredensial OAuth tertanam.
        /// Penting untuk mendiagnosis "login Google gagal" pada instalasi hasil
        /// build CI yang dibangun tanpa berkas rahasia.
        /// </summary>
        /// <summary>Warna kotak keterangan paket: 1 bila kredensial tertanam, 2 bila tidak.</summary>
        public int GoogleKredensialPaketTone => GoogleDriveService.EmbeddedCredentialsAvailable ? 1 : 2;

        public string GoogleKredensialPaketInfo => GoogleDriveService.EmbeddedCredentialsAvailable
            ? "Paket aplikasi ini memuat kredensial OAuth tertanam — login Google siap dipakai di komputer mana pun."
            : "Paket aplikasi ini TIDAK memuat kredensial OAuth tertanam (dibangun tanpa berkas rahasia build). " +
              "Isi Client ID & Client Secret di sini, simpan, lalu uji koneksi pada setiap komputer yang memakainya.";

        public AsyncRelayCommand SimpanKredensialGoogleCommand { get; }

        public AsyncRelayCommand UjiKredensialGoogleCommand { get; }

        /// <summary>
        /// Simpan Client ID & Client Secret terenkripsi DPAPI per user Windows
        /// (bukan plaintext di appsettings.json yang ikut terdistribusi).
        /// </summary>
        private Task SimpanKredensialGoogleAsync()
        {
            var drive = _googleDrive;
            if (drive == null)
            {
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Layanan Google tidak tersedia pada sesi ini.";
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(GoogleClientId) || string.IsNullOrWhiteSpace(GoogleClientSecret))
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Client ID dan Client Secret wajib diisi keduanya.";
                return Task.CompletedTask;
            }

            try
            {
                drive.SaveClientCredentials(GoogleClientId, GoogleClientSecret);
                GoogleKredensialTone = 1;
                GoogleKredensialInfo = "Kredensial tersimpan terenkripsi pada:\n" + GoogleDriveService.CredentialsStorePath +
                    "\n\nLangkah berikutnya: klik \"Uji Koneksi\" untuk membuka login akun Google.";
                OnPropertyChanged(nameof(GoogleKredensialStatus));
                _logger.LogInformation("Kredensial klien OAuth Google disimpan dari halaman Pengaturan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan kredensial klien OAuth Google");
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Gagal menyimpan: " + ex.Message;
            }
            return Task.CompletedTask;
        }

        /// <summary>Uji kredensial dengan membuka alur login Google dan menampilkan email hasilnya.</summary>
        private async Task UjiKredensialGoogleAsync()
        {
            var drive = _googleDrive;
            if (drive == null || !drive.IsOAuthEnabled)
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Simpan Client ID dan Client Secret terlebih dahulu, lalu uji koneksi.";
                return;
            }

            try
            {
                GoogleKredensialTone = 0;
                GoogleKredensialInfo = "Membuka login Google — selesaikan prosesnya di peramban bila diminta.";
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var email = await Task.Run(() => drive.GetAccountEmailAsync(cts.Token), cts.Token);
                if (string.IsNullOrWhiteSpace(email))
                {
                    GoogleKredensialTone = 2;
                    GoogleKredensialInfo = "Login selesai, tetapi email tidak diperoleh. Periksa Google Drive API dan OAuth consent screen.";
                }
                else
                {
                    GoogleKredensialTone = 1;
                    GoogleKredensialInfo = "Terhubung — " + email;
                }
            }
            catch (OperationCanceledException)
            {
                GoogleKredensialTone = 2;
                GoogleKredensialInfo = "Waktu login habis atau dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uji koneksi kredensial Google gagal");
                GoogleKredensialTone = 3;
                GoogleKredensialInfo = "Gagal: " + ex.Message;
            }
        }

        public RelayCommand BatalCommand { get; }

        // =======================================================================
        // Navigasi bagian halaman. Pengaturan aplikasi sudah banyak, jadi isinya
        // dikelompokkan per bagian dengan daftar navigasi di sisi kiri — bukan
        // sederet kartu panjang yang harus digulir.
        // =======================================================================

        public ObservableCollection<PengaturanSectionVM> Sections { get; } = new();

        public PengaturanSectionVM BagianUmum { get; private set; } = null!;
        public PengaturanSectionVM BagianGoogleOauth { get; private set; } = null!;
        public PengaturanSectionVM BagianDatabase { get; private set; } = null!;
        public PengaturanSectionVM BagianInformasi { get; private set; } = null!;

        private PengaturanSectionVM? _selectedSection;

        /// <summary>Bagian yang sedang ditampilkan (dipilih dari daftar navigasi).</summary>
        public PengaturanSectionVM? SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedSection)) return;
                if (!SetProperty(ref _selectedSection, value)) return;

                foreach (var bagian in Sections) bagian.Aktif = ReferenceEquals(bagian, value);

                OnPropertyChanged(nameof(JudulSeksiAktif));
                OnPropertyChanged(nameof(RingkasanSeksiAktif));
            }
        }

        /// <summary>Judul bagian yang sedang tampil (dipakai kepala area isi).</summary>
        public string JudulSeksiAktif => _selectedSection?.Judul ?? string.Empty;

        /// <summary>Penjelasan singkat bagian yang sedang tampil.</summary>
        public string RingkasanSeksiAktif => _selectedSection?.Ringkasan ?? string.Empty;

        /// <summary>Susun daftar bagian + pilih bagian pertama (Umum) saat halaman dibuka.</summary>
        private void SiapkanBagian()
        {
            // Ikon memakai glyph Segoe Fluent Icons (IkonMenu) sehingga mengikuti
            // tema dan tampil konsisten dengan sidebar — bukan emoji berwarna.
            BagianUmum = new PengaturanSectionVM("umum", "Umum & Preferensi", IkonMenu.Pengaturan,
                "Perilaku aplikasi, kecepatan animasi antarmuka, preferensi kerja, dan pembersihan berkas berkala.");
            BagianGoogleOauth = new PengaturanSectionVM("google-oauth", "Kredensial Google", IkonMenu.KataSandi,
                "Client ID & Client Secret klien OAuth aplikasi: pemeriksaan sumber kredensial dan isian " +
                "untuk komputer yang paketnya tidak memuat kredensial tertanam.");
            BagianDatabase = new PengaturanSectionVM("database", "Database Desa", IkonMenu.Cadangkan,
                "Lokasi berkas database desa yang sedang dipakai dan pemindahannya ke folder data sendiri, " +
                "misalnya di drive lain — tanpa menyalin berkas ke folder aplikasi.");
            BagianInformasi = new PengaturanSectionVM("informasi", "Informasi", IkonMenu.Tentang,
                "Sesi login yang sedang aktif dan lokasi berkas pengaturan pada komputer ini.");

            foreach (var bagian in new[]
                     {
                         BagianUmum, BagianGoogleOauth, BagianDatabase, BagianInformasi
                     })
            {
                Sections.Add(bagian);
            }

            SelectedSection = BagianUmum;
        }

        /// <summary>Benar bila mulai ulang (penerapan lokasi database) sedang berjalan — menonaktifkan tombolnya.</summary>
        private bool _databaseSibuk;

        public bool DatabaseSibuk
        {
            get => _databaseSibuk;
            private set
            {
                if (SetProperty(ref _databaseSibuk, value)) SimpanMulaiUlangDatabaseCommand.RaiseCanExecuteChanged();
            }
        }

    }
}
