using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
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
    /// Baris pengaturan bernilai angka (mis. batas ukuran pembaruan otomatis dalam
    /// MB). Tampil sebagai kotak teks kecil di samping label; nilai di luar rentang
    /// ditolak dan dikembalikan ke nilai semula.
    /// </summary>
    public class SettingRowAngkaVM : ObservableObject
    {
        private readonly Action<int> _persist;
        private readonly int _min;
        private readonly int _maks;
        private string _teks;

        public string Judul { get; }
        public string Keterangan { get; }

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
            }
        }

        private readonly Func<int> _persistGet;

        public SettingRowAngkaVM(
            string judul, string keterangan, int nilaiAwal,
            Action<int> persist, Func<int> persistGet,
            int min = 1, int maks = 999)
        {
            Judul = judul;
            Keterangan = keterangan;
            _teks = nilaiAwal.ToString();
            _persist = persist;
            _persistGet = persistGet;
            _min = min;
            _maks = maks;
        }
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
    /// Satu baris pengaturan penomoran surat: awalan nomor yang bisa diganti
    /// pengguna (mis. SKD 470 → 471) dengan contoh hasil nomornya.
    /// </summary>
    public class PenomoranBarisVM : ObservableObject
    {
        private string _awalan;

        public string NamaJenis { get; }
        public string DisplayName { get; }
        public string KodeJenis { get; }

        /// <summary>Apakah surat ini ikut penomoran bersama (satu urutan dengan SKD).</summary>
        public bool IsSharedNumbering { get; }

        /// <summary>Format bawaan aplikasi — bentuknya dipertahankan saat awalan diganti.</summary>
        public string FormatBawaan { get; }

        public string AwalanBawaan { get; }

        /// <summary>Awalan nomor yang dipakai; kosong = kembali ke bawaan aplikasi.</summary>
        public string Awalan
        {
            get => _awalan;
            set
            {
                if (SetProperty(ref _awalan, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(Contoh));
                    OnPropertyChanged(nameof(Disesuaikan));
                    OnPropertyChanged(nameof(Terubah));
                    OnPropertyChanged(nameof(PesanKesalahan));
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        /// <summary>Awalan yang tersimpan di berkas (acuan perubahan belum disimpan).</summary>
        public string AwalanTersimpan { get; private set; }

        /// <summary>Berbeda dari nilai tersimpan — perlu disimpan.</summary>
        public bool Terubah =>
            !string.Equals(Awalan.Trim(), AwalanTersimpan.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Berbeda dari bawaan aplikasi (boleh dikembalikan lewat tombol Bawaan).</summary>
        public bool Disesuaikan =>
            !string.Equals(Awalan.Trim(), AwalanBawaan, StringComparison.OrdinalIgnoreCase);

        /// <summary>Pesan kesalahan awalan; kosong bila awalan sah (atau dikembalikan ke bawaan).</summary>
        public string PesanKesalahan
        {
            get
            {
                var bersih = Awalan.Trim();
                if (bersih.Length == 0 || string.Equals(bersih, AwalanBawaan, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return SuDesApp.Services.PenomoranSuratService.AwalanValid(bersih, out var pesan)
                    ? string.Empty
                    : pesan;
            }
        }

        public bool AdaKesalahan => PesanKesalahan.Length > 0;

        /// <summary>Contoh nomor surat yang akan tercetak dengan awalan saat ini.</summary>
        public string Contoh => SuDesApp.Services.PenomoranSuratService.Contoh(
            SuDesApp.Services.PenomoranSuratService.BangunFormat(
                FormatBawaan,
                Awalan.Trim().Length == 0 ? AwalanBawaan : Awalan.Trim()));

        /// <summary>Keterangan apakah jenis surat ini berbagi urutan nomor dengan SKD.</summary>
        public string KeteranganUrutan => IsSharedNumbering
            ? "Berbagi urutan nomor dengan SKD dan surat keterangan lain"
            : "Urutan nomor tersendiri";

        public RelayCommand KembalikanBawaanCommand { get; }

        public PenomoranBarisVM(SuDesApp.Services.PenomoranSuratEntri entri)
        {
            NamaJenis = entri.NamaJenis;
            DisplayName = entri.DisplayName;
            KodeJenis = entri.KodeJenis;
            IsSharedNumbering = entri.IsSharedNumbering;
            FormatBawaan = entri.FormatBawaan;
            AwalanBawaan = entri.AwalanBawaan;
            _awalan = entri.Awalan;
            AwalanTersimpan = entri.Awalan;
            KembalikanBawaanCommand = new RelayCommand(() => Awalan = AwalanBawaan);
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
        private readonly SuDesApp.Services.PenomoranSuratService? _penomoranSurat;

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
                TampilkanStatusSementara($"Tersimpan — kecepatan animasi: {value.Judul}");
            }
        }

        /// <summary>Keterangan kecepatan yang sedang berlaku beserta durasi nyatanya.</summary>
        public string KecepatanAnimasiContohInfo =>
            $"Contoh di bawah memakai kecepatan {_kecepatanAnimasiTerpilih.Judul}: " +
            $"sidebar {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSidebar)}, " +
            $"dropdown {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarDropdown)}, " +
            $"sorotan menu {KecepatanAnimasiPrefs.ContohTeks(_kecepatanAnimasiTerpilih.Nilai, KecepatanAnimasiPrefs.DasarSorotMasuk)}.";

        private string _waJamBuka = AppPreferenceStore.GetWaServiceOpen() ?? string.Empty;
        private string _waJamTutup = AppPreferenceStore.GetWaServiceClose() ?? string.Empty;

        /// <summary>Jam buka layanan WA "HH:mm" — kosong = 24 jam. Tersimpan otomatis saat valid.</summary>
        public string WaJamBuka
        {
            get => _waJamBuka;
            set
            {
                var bersih = SanitasiJam(value);
                if (SetProperty(ref _waJamBuka, bersih))
                {
                    if (bersih.Length == 0 || IsValidJam(bersih))
                    {
                        AppPreferenceStore.SetWaServiceOpen(bersih);
                        TampilkanStatusSementara("Tersimpan — jam layanan WhatsApp");
                    }
                    OnPropertyChanged(nameof(WaJamLayananInfo));
                }
            }
        }

        /// <summary>Jam tutup layanan WA "HH:mm" — kosong = 24 jam. Tersimpan otomatis saat valid.</summary>
        public string WaJamTutup
        {
            get => _waJamTutup;
            set
            {
                var bersih = SanitasiJam(value);
                if (SetProperty(ref _waJamTutup, bersih))
                {
                    if (bersih.Length == 0 || IsValidJam(bersih))
                    {
                        AppPreferenceStore.SetWaServiceClose(bersih);
                        TampilkanStatusSementara("Tersimpan — jam layanan WhatsApp");
                    }
                    OnPropertyChanged(nameof(WaJamLayananInfo));
                }
            }
        }

        /// <summary>Rentang jam layanan yang sedang berlaku (untuk ditampilkan).</summary>
        public string WaJamLayananInfo
            => "Layanan otomatis berjalan: " + SuDesApp.WhatsApp.WaServiceHours.RentangTampil(
                AppPreferenceStore.GetWaServiceOpen(), AppPreferenceStore.GetWaServiceClose(),
                AppPreferenceStore.GetWaServiceDays());

        // ==== Hari layanan (7 checkbox; default Senin–Jumat) ====

        private static readonly string[] NamaHariCheckbox =
            { "Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" };

        public ObservableCollection<HariLayananVM> HariLayanan { get; } = new();

        private void MuatHariLayanan()
        {
            var aktif = SuDesApp.WhatsApp.WaServiceHours.ParseDays(AppPreferenceStore.GetWaServiceDays())
                       ?? new HashSet<int> { 0, 1, 2, 3, 4, 5, 6 };
            for (int d = 0; d < 7; d++)
            {
                var idx = d;
                var row = new HariLayananVM(NamaHariCheckbox[d], aktif.Contains(d));
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(HariLayananVM.Aktif)) SimpanHariLayanan();
                };
                HariLayanan.Add(row);
            }
        }

        private void SimpanHariLayanan()
        {
            var aktif = HariLayanan.Where(h => h.Aktif)
                                   .Select(h => Array.IndexOf(NamaHariCheckbox, h.Nama))
                                   .OrderBy(d => d)
                                   .ToList();
            // Semua hari dinonaktifkan → perlakukan sebagai setiap hari (24/7),
            // supaya layanan tidak pernah mati total karena salah centang.
            AppPreferenceStore.SetWaServiceDays(aktif.Count == 0 || aktif.Count == 7
                ? string.Empty
                : string.Join(",", aktif));
            OnPropertyChanged(nameof(WaJamLayananInfo));
            TampilkanStatusSementara("Tersimpan — hari layanan WhatsApp");
        }

        public class HariLayananVM : ObservableObject
        {
            private bool _aktif;
            public string Nama { get; }
            public bool Aktif { get => _aktif; set => SetProperty(ref _aktif, value); }
            public HariLayananVM(string nama, bool aktif) { Nama = nama; _aktif = aktif; }
        }

        private static string SanitasiJam(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var c in input)
            {
                if (char.IsDigit(c)) sb.Append(c);
                else if (c == ':' && !sb.ToString().Contains(':')) sb.Append(c);
                if (sb.Length >= 5) break;
            }
            return sb.ToString();
        }

        private static bool IsValidJam(string s)
        {
            var parts = s.Split(':');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
            return h >= 0 && h <= 23 && m >= 0 && m <= 59;
        }

        // ==== Gateway WhatsApp (Cloud API Meta — resmi) ====

        private const string WaGatewayCloudApiOnly = "cloudapi";
        private string _waGateway = "cloudapi";
        private string _waCloudApiToken = AppPreferenceStore.GetWaCloudApiToken() ?? string.Empty;
        private string _waCloudApiPhoneId = AppPreferenceStore.GetWaCloudApiPhoneId() ?? string.Empty;
        private string _waCloudApiVerifyToken = AppPreferenceStore.GetWaCloudApiVerifyToken() ?? string.Empty;
        private string _waUjiKoneksiInfo = string.Empty;

        /// <summary>Gateway aktif (selalu Cloud API — pilihan lama sudah dihapus).</summary>
        public string WaGateway
        {
            get => _waGateway;
            set { if (SetProperty(ref _waGateway, WaGatewayCloudApiOnly)) OnPropertyChanged(nameof(WaGatewayInfo)); }
        }

        /// <summary>Permanent access token Cloud API (System User Meta Business).</summary>
        public string WaCloudApiToken
        {
            get => _waCloudApiToken;
            set
            {
                if (SetProperty(ref _waCloudApiToken, value ?? string.Empty))
                {
                    AppPreferenceStore.SetWaCloudApiToken(value);
                    OnPropertyChanged(nameof(WaGatewayReady));
                }
            }
        }

        /// <summary>Phone Number ID nomor WhatsApp desa (Meta → WhatsApp → API Setup).</summary>
        public string WaCloudApiPhoneId
        {
            get => _waCloudApiPhoneId;
            set
            {
                if (SetProperty(ref _waCloudApiPhoneId, value ?? string.Empty))
                {
                    AppPreferenceStore.SetWaCloudApiPhoneId(value);
                    OnPropertyChanged(nameof(WaGatewayReady));
                }
            }
        }

        /// <summary>Verify token bebas — harus sama dengan yang diisi di webhook Meta.</summary>
        public string WaCloudApiVerifyToken
        {
            get => _waCloudApiVerifyToken;
            set
            {
                if (SetProperty(ref _waCloudApiVerifyToken, value ?? string.Empty))
                    AppPreferenceStore.SetWaCloudApiVerifyToken(value);
            }
        }

        public bool WaCloudApiEnabled => WaGateway == "cloudapi";

        // BindingRadioButton: selalu terpilih Cloud API.
        public bool WaGatewayCloudApi
        {
            get => true;
            set { /* pilihan tunggal — tidak ada gateway lain */ }
        }

        public string WaGatewayInfo
            => "Gateway aktif: WhatsApp Cloud API (resmi, dari Meta). Aplikasi hanya mengirim teks dan tautan, tanpa berkas lampiran.";

        public string WaUjiKoneksiInfo
        {
            get => _waUjiKoneksiInfo;
            private set => SetProperty(ref _waUjiKoneksiInfo, value);
        }

        public AsyncRelayCommand UjiCloudApiCommand { get; }

        // ==== Wizard setup 3 langkah: token → Phone Number ID → verify token ====

        private int _wizardStep;

        /// <summary>Langkah wizard aktif (0=access token, 1=Phone Number ID, 2=verify token webhook).</summary>
        public int WizardStep
        {
            get => _wizardStep;
            private set
            {
                if (SetProperty(ref _wizardStep, Math.Clamp(value, 0, 2)))
                {
                    OnPropertyChanged(nameof(WizardStepText));
                    OnPropertyChanged(nameof(WizardProgress));
                    OnPropertyChanged(nameof(WizardStep0Visible));
                    OnPropertyChanged(nameof(WizardStep1Visible));
                    OnPropertyChanged(nameof(WizardStep2Visible));
                    OnPropertyChanged(nameof(WizardStep1Aktif));
                    OnPropertyChanged(nameof(WizardStep1Selesai));
                    OnPropertyChanged(nameof(WizardStep2Aktif));
                    OnPropertyChanged(nameof(WizardStep2Selesai));
                    OnPropertyChanged(nameof(WizardStep3Aktif));
                    OnPropertyChanged(nameof(WizardKembaliVisible));
                }
            }
        }

        public bool WizardStep0Visible => WizardStep == 0;
        public bool WizardStep1Visible => WizardStep == 1;
        public bool WizardStep2Visible => WizardStep == 2;

        // Status indikator lingkaran per langkah (untuk trigger warna di XAML).
        public bool WizardStep1Aktif => WizardStep == 0;
        public bool WizardStep1Selesai => WizardStep > 0;
        public bool WizardStep2Aktif => WizardStep == 1;
        public bool WizardStep2Selesai => WizardStep > 1;
        public bool WizardStep3Aktif => WizardStep == 2;

        public bool WizardKembaliVisible => WizardStep > 0;

        public string WizardStepText => WizardStep switch
        {
            0 => "Langkah 1 dari 3 — Access token",
            1 => "Langkah 2 dari 3 — Phone Number ID",
            _ => "Langkah 3 dari 3 — Verify token webhook"
        };

        /// <summary>Progres wizard dalam persen (indikator bar).</summary>
        public double WizardProgress => (WizardStep + 1) * 100.0 / 3.0;

        private string _wizardValidasiInfo = string.Empty;
        /// <summary>Pesan validasi sebelum berpindah langkah.</summary>
        public string WizardValidasiInfo
        {
            get => _wizardValidasiInfo;
            private set => SetProperty(ref _wizardValidasiInfo, value);
        }

        /// <summary>Semua isian inti gateway terisi — gateway siap dipakai.</summary>
        public bool WaGatewayReady =>
            !string.IsNullOrWhiteSpace(WaCloudApiToken) &&
            !string.IsNullOrWhiteSpace(WaCloudApiPhoneId);

        public AsyncRelayCommand WizardLanjutCommand { get; }
        public AsyncRelayCommand WizardKembaliCommand { get; }

        private Task WizardLanjutAsync()
        {
            WizardValidasiInfo = string.Empty;
            if (WizardStep == 0)
            {
                if (string.IsNullOrWhiteSpace(WaCloudApiToken))
                {
                    WizardValidasiInfo = "Isi Access Token terlebih dahulu; cara mengambilnya dijelaskan pada Panduan WhatsApp Bagian A.";
                    return Task.CompletedTask;
                }
                WizardStep = 1;
            }
            else if (WizardStep == 1)
            {
                var phoneId = WaCloudApiPhoneId.Trim();
                if (phoneId.Length == 0 || !phoneId.All(char.IsDigit))
                {
                    WizardValidasiInfo = "Phone Number ID berupa deretan angka; salin dari Meta for Developers → WhatsApp → API Setup.";
                    return Task.CompletedTask;
                }
                WizardStep = 2;
            }
            return Task.CompletedTask;
        }

        private Task WizardKembaliAsync()
        {
            WizardValidasiInfo = string.Empty;
            WizardStep = WizardStep - 1;
            return Task.CompletedTask;
        }

        // ==== Mode tautan Google Form/Sheet (layanan online) ====

        private bool _waSheetMode = WaSheetOptions.IsLinkModeEnabled();
        public bool WaSheetMode
        {
            get => _waSheetMode;
            set
            {
                if (SetProperty(ref _waSheetMode, value))
                {
                    WaSheetOptions.SetLinkModeEnabled(value);
                    OnPropertyChanged(nameof(WaSheetRingkasan));
                }
            }
        }

        private string _waSheetUrl = WaSheetOptions.GetSheetUrl() ?? string.Empty;
        /// <summary>URL Google Sheet tempat jawaban form terkumpul (URL …/edit).</summary>
        public string WaSheetUrl
        {
            get => _waSheetUrl;
            set
            {
                if (SetProperty(ref _waSheetUrl, value ?? string.Empty))
                {
                    WaSheetOptions.SetSheetUrl(value);
                    WaSheetOptions.SetAutoForm(false);
                    OnPropertyChanged(nameof(WaFormAutoSudahAda));
                    OnPropertyChanged(nameof(WaSheetRingkasan));
                }
            }
        }

        private string _waSheetTabName = WaSheetOptions.GetTabName();
        /// <summary>Nama tab (worksheet) jawaban, default "Form Responses 1".</summary>
        public string WaSheetTabName
        {
            get => _waSheetTabName;
            set { if (SetProperty(ref _waSheetTabName, value ?? string.Empty)) WaSheetOptions.SetTabName(value); }
        }

        private string _waSheetFormUrl = WaSheetOptions.GetFormUrl() ?? string.Empty;
        /// <summary>URL Google Form opsional; kosong = dibuat otomatis dari URL Sheet.</summary>
        public string WaSheetFormUrl
        {
            get => _waSheetFormUrl;
            set
            {
                if (SetProperty(ref _waSheetFormUrl, value ?? string.Empty))
                {
                    WaSheetOptions.SetFormUrl(value);
                    WaSheetOptions.SetAutoForm(false);
                    OnPropertyChanged(nameof(WaFormAutoSudahAda));
                }
            }
        }

        private int _waSheetUjiTone = 0;
        /// <summary>Nada hasil uji: 0=info, 1=sukses, 2=peringatan, 3=gagal.</summary>
        public int WaSheetUjiTone
        {
            get => _waSheetUjiTone;
            private set => SetProperty(ref _waSheetUjiTone, value);
        }

        private string _waSheetUjiInfo = string.Empty;
        public string WaSheetUjiInfo
        {
            get => _waSheetUjiInfo;
            private set { if (SetProperty(ref _waSheetUjiInfo, value)) WaSheetUjiTone = CariTone(value); }
        }

        private int _waFormAutoTone = 0;
        /// <summary>Nada hasil pembuatan otomatis: 0=info, 1=sukses, 2=peringatan, 3=gagal.</summary>
        public int WaFormAutoTone
        {
            get => _waFormAutoTone;
            private set => SetProperty(ref _waFormAutoTone, value);
        }

        private string _waFormAutoInfo = string.Empty;
        /// <summary>Hasil pembuatan formulir + Sheet otomatis.</summary>
        public string WaFormAutoInfo
        {
            get => _waFormAutoInfo;
            private set { if (SetProperty(ref _waFormAutoInfo, value)) WaFormAutoTone = CariTone(value); }
        }

        /// <summary>Formulir + Sheet otomatis sudah pernah dibuat (untuk mengunci tombol "Buat ulang").</summary>
        public bool WaFormAutoSudahAda =>
            WaSheetOptions.IsAutoForm()
            && !string.IsNullOrWhiteSpace(WaSheetOptions.GetFormId())
            && !string.IsNullOrWhiteSpace(WaSheetOptions.GetSheetUrl());

        private static int CariTone(string? teks)
        {
            if (string.IsNullOrWhiteSpace(teks)) return 0;
            if (teks.StartsWith("Gagal", StringComparison.Ordinal)) return 3;
            if (teks.Contains("✓", StringComparison.Ordinal)) return 1;
            if (teks.Contains("⚠️", StringComparison.Ordinal)
                || teks.Contains("TIDAK", StringComparison.Ordinal)
                || teks.Contains("belum", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        public AsyncRelayCommand UjiWaSheetCommand { get; }
        public RelayCommand BukaPanduanWaCommand { get; }
        public AsyncRelayCommand BuatFormulirOtomatisCommand { get; }
        public AsyncRelayCommand BuatUlangFormulirCommand { get; }


        /// <summary>Ringkasan status mode tautan untuk ditampilkan di kartu pengaturan.</summary>
        public string WaSheetRingkasan
            => WaSheetOptions.IsLinkModeEnabled()
                ? (WaSheetOptions.IsConfigured()
                    ? "Mode aktif: setiap pesan dari warga dibalas tautan Google Formulir untuk diisi sendiri. Jawaban pada Google Sheet diproses otomatis, lalu tautan unduh PDF dikirim kembali melalui WhatsApp."
                    : "Mode aktif, tetapi URL Google Formulir/Sheet belum diisi — balasan sementara masih memakai alur percakapan format.")
                : "Mode nonaktif: alur percakapan format dipakai, yaitu warga mengetik data sesuai format tiap jenis surat.";

        /// <summary>
        /// Uji konfigurasi mode tautan: (1) akses baca Google Sheet jawaban via
        /// Sheets API, (2) bila URL Formulir diisi, pastikan tautannya benar-benar
        /// bisa dibuka publik oleh warga (inilah yang DITERIMA warga di WhatsApp).
        /// </summary>
        private async Task UjiWaSheetAsync()
        {
            var sheetId = WaSheetOptions.ExtractSheetId(WaSheetUrl);
            if (string.IsNullOrWhiteSpace(sheetId) && string.IsNullOrWhiteSpace(WaSheetFormUrl))
            {
                WaSheetUjiInfo = "Isi URL Google Formulir dan/atau URL Google Sheet jawaban terlebih dahulu.";
                return;
            }

            var app = System.Windows.Application.Current as App;
            if (app == null)
            {
                WaSheetUjiInfo = "Aplikasi belum siap pada sesi ini.";
                return;
            }

            var drive = app.ServiceProvider.GetRequiredService<GoogleDriveService>();
            if (!drive.IsOAuthEnabled || !drive.HasStoredToken())
            {
                WaSheetUjiInfo = "Akun Google belum terhubung. Masuk dengan Akun Google terlebih dahulu melalui halaman login.";
                return;
            }

            try
            {
                WaSheetUjiInfo = "Memeriksa…";
                var bagian = new List<string>();

                // 1) Google Sheet jawaban (sumber data aplikasi) — lewati bila tidak diisi.
                if (!string.IsNullOrWhiteSpace(sheetId))
                {
                    var sheets = app.ServiceProvider.GetRequiredService<GoogleSheetsService>();
                    var (ok, pesan) = await sheets.TestConnectionAsync(sheetId, WaSheetTabName);
                    bagian.Add(pesan);
                    if (!ok)
                    {
                        WaSheetUjiInfo = string.Join("\n\n", bagian);
                        return;
                    }
                }

                // 2) Google Formulir — tautan yang DITERIMA warga; pastikan bisa dibuka publik.
                var formUrl = WaSheetOptions.NormalizeFormUrl(WaSheetFormUrl);
                if (!string.IsNullOrWhiteSpace(formUrl))
                {
                    try
                    {
                        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                        using var resp = await http.GetAsync(formUrl);
                        if (resp.IsSuccessStatusCode)
                        {
                            var html = await resp.Content.ReadAsStringAsync();
                            bagian.Add(html.Contains("FormResponse", StringComparison.OrdinalIgnoreCase) || html.Contains("fbzx", StringComparison.OrdinalIgnoreCase)
                                ? "Google Formulir OK — tautan dapat dibuka oleh warga."
                                : "URL terbuka, tetapi bukan halaman Google Formulir. Periksa kembali URL-nya.");
                        }
                        else
                        {
                            bagian.Add($"Google Formulir tidak dapat dibuka (HTTP {(int)resp.StatusCode}), sehingga warga tidak bisa mengisi. Periksa izin berbagi formulir.");
                        }
                    }
                    catch (Exception exForm)
                    {
                        bagian.Add("Google Formulir tidak terjangkau: " + exForm.Message);
                    }
                }
                else if (string.IsNullOrWhiteSpace(sheetId))
                {
                    bagian.Add("URL Formulir kosong dan Sheet tidak terbaca, sehingga warga belum bisa menerima tautan formulir.");
                }
                else
                {
                    bagian.Add("URL Formulir kosong. Tautan untuk warga akan diturunkan dari ID Sheet dan berisiko tidak berfungsi bila Sheet bukan lembar respons Google Form. Sebaiknya isi URL Formulir.");
                }

                WaSheetUjiInfo = string.Join("\n\n", bagian);
            }
            catch (Exception ex)
            {
                WaSheetUjiInfo = "Gagal: " + ex.Message;
            }
        }

        /// <summary>
        /// Membuat Google Sheet jawaban + Google Formulir lengkap secara otomatis
        /// di akun desa yang terhubung, lalu mengisi pengaturan. Idempoten: bila
        /// sudah pernah dibuat (ganti = false), tidak membuat file baru — cukup
        /// menampilkan kembali konfigurasi yang sudah tersimpan.
        /// </summary>
        private async Task BuatFormulirOtomatisAsync(bool ganti = false)
        {
            var app = System.Windows.Application.Current as App;
            var setup = app?.ServiceProvider.GetRequiredService<WaFormAutoSetupService>();
            if (setup == null)
            {
                WaFormAutoInfo = "Aplikasi belum siap pada sesi ini.";
                return;
            }

            var drive = app!.ServiceProvider.GetRequiredService<GoogleDriveService>();
            if (!drive.IsOAuthEnabled || !drive.HasStoredToken())
            {
                WaFormAutoInfo = "Akun Google belum terhubung. Masuk dengan Akun Google terlebih dahulu melalui halaman login, lalu coba lagi.";
                return;
            }

            // Sudah ada & bukan perintah mengganti → tampilkan konfigurasi tersimpan, jangan buat baru.
            if (!ganti && WaFormAutoSudahAda)
            {
                WaSheetFormUrl = WaSheetOptions.GetFormUrl() ?? string.Empty;
                WaSheetUrl = WaSheetOptions.GetSheetUrl() ?? string.Empty;
                WaSheetTabName = WaSheetOptions.GetTabName();
                WaSheetMode = true;
                WaSheetOptions.SetFormId(WaSheetOptions.GetFormId());
                WaSheetOptions.SetAutoForm(true);
                OnPropertyChanged(nameof(WaSheetRingkasan));
                OnPropertyChanged(nameof(WaFormAutoSudahAda));

                WaFormAutoInfo =
                    "Formulir dan Sheet sudah pernah dibuat, jadi tidak dibuat ulang.\n" +
                    "Tautan formulir warga: " + (WaSheetOptions.GetFormUrl() ?? string.Empty) + "\n" +
                    "Sheet jawaban: " + (WaSheetOptions.GetSheetUrl() ?? string.Empty) + "\n" +
                    "Kelola formulir: https://docs.google.com/forms/d/" + WaSheetOptions.GetFormId() + "/edit\n" +
                    "Untuk menggantinya dengan yang baru, gunakan tombol 'Buat Ulang (Ganti)'.";
                return;
            }

            try
            {
                BuatFormulirOtomatisCommand.RaiseCanExecuteChanged();
                WaFormAutoInfo = "Membuat Formulir dan Sheet… (selesaikan login Google bila jendela login muncul)";
                var hasil = await setup.BuatAsync(ganti: ganti);

                WaSheetFormUrl = hasil.FormUrl;
                WaSheetUrl = hasil.SheetUrl;
                WaSheetTabName = hasil.TabName;
                WaSheetMode = true;
                WaSheetOptions.SetFormId(hasil.FormId);
                WaSheetOptions.SetAutoForm(true);
                OnPropertyChanged(nameof(WaSheetRingkasan));
                OnPropertyChanged(nameof(WaFormAutoSudahAda));

                WaFormAutoInfo =
                    "Selesai — Formulir dan Sheet berhasil dibuat pada akun Google Anda.\n" +
                    "Tautan formulir warga: " + hasil.FormUrl + "\n" +
                    "Sheet jawaban: " + hasil.SheetUrl + " (tab: " + hasil.TabName + ")\n" +
                    "Kelola formulir: " + hasil.EditUrl + "\n" +
                    "Jawaban disalin ke Sheet secara berkala oleh aplikasi.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat formulir otomatis");
                WaFormAutoInfo = "Gagal: " + ex.Message;
            }
            finally
            {
                BuatFormulirOtomatisCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Uji token Cloud API: ambil profil nomor via Graph API GET /{phone-id}.</summary>
        private async Task UjiCloudApiAsync()
        {
            if (string.IsNullOrWhiteSpace(WaCloudApiToken) || string.IsNullOrWhiteSpace(WaCloudApiPhoneId))
            {
                WaUjiKoneksiInfo = "Isi Access Token dan Phone Number ID terlebih dahulu.";
                return;
            }

            try
            {
                WaUjiKoneksiInfo = "Memeriksa…";
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    "https://graph.facebook.com/v21.0/" + WaCloudApiPhoneId.Trim() +
                    "?fields=verified_name,display_phone_number,quality_rating");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", WaCloudApiToken.Trim());
                var resp = await http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                WaUjiKoneksiInfo = resp.IsSuccessStatusCode
                    ? "Terhubung — " + body.Trim()
                    : "Gagal: " + body.Trim();
            }
            catch (Exception ex)
            {
                WaUjiKoneksiInfo = "Gagal: " + ex.Message;
            }
        }

        public PengaturanAplikasiViewModel(
            NavigationService navigation,
            ILogger<PengaturanAplikasiViewModel> logger,
            IServiceProvider provider,
            SuDesApp.Services.PenomoranSuratService? penomoranSurat = null)
        {
            _navigation = navigation;
            _logger = logger;
            _provider = provider;
            _penomoranSurat = penomoranSurat;

            _googleDrive = _provider.GetService<GoogleDriveService>();

            UjiCloudApiCommand = new AsyncRelayCommand(UjiCloudApiAsync);
            WizardLanjutCommand = new AsyncRelayCommand(WizardLanjutAsync);
            WizardKembaliCommand = new AsyncRelayCommand(WizardKembaliAsync);
            // Wizard dibuka di langkah akhir bila gateway sudah pernah dikonfigurasi.
            _wizardStep = WaGatewayReady ? 2 : 0;
            UjiWaSheetCommand = new AsyncRelayCommand(UjiWaSheetAsync);
            BuatFormulirOtomatisCommand = new AsyncRelayCommand(() => BuatFormulirOtomatisAsync(ganti: false));
            BuatUlangFormulirCommand = new AsyncRelayCommand(() => BuatFormulirOtomatisAsync(ganti: true));
            BukaPanduanWaCommand = new RelayCommand(BukaPanduanWa);
            MuatHariLayanan();

            // Kecepatan animasi yang sedang berlaku dipasang sebagai pilihan terpilih;
            // nilainya ditulis langsung ke bidang (bukan properti) supaya membuka halaman
            // ini tidak dianggap sebagai perubahan pengguna.
            _kecepatanAnimasiTerpilih = PilihanKecepatanAnimasi
                .First(p => p.Nilai == KecepatanAnimasiPrefs.SaatIni);

            SiapkanBagian();

            SimpanPenomoranCommand = new AsyncRelayCommand(SimpanPenomoranAsync, () => AdaPerubahanPenomoran && !PenomoranSibuk);
            MuatUlangPenomoranCommand = new RelayCommand(MuatPenomoran);
            MuatPenomoran();

            TambahBaris(new SettingRowVM(
                "Login otomatis dengan Google",
                "Saat aktif, pengguna yang sudah pernah login Google langsung masuk memakai profil akunnya ketika aplikasi dibuka. Saat nonaktif, formulir username dan password selalu tampil lebih dahulu; akun Google tetap dapat digunakan melalui tombol 'Masuk dengan Akun Google'.",
                AppPreferenceStore.IsGoogleAutoLoginEnabled(),
                v => AppPreferenceStore.SetGoogleAutoLoginEnabled(v)));

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
                "Proses otomatis permintaan WhatsApp",
                "Permintaan surat dari WhatsApp diproses tanpa menunggu operator: surat dibuat, PDF dihasilkan memakai generator resmi, lalu tautan unduh dikirim ke pemohon. Permintaan yang gagal masuk status PERLU_PERBAIKAN pada panel Layanan Online untuk diperiksa operator.",
                AppPreferenceStore.IsWaAutoProcessEnabled(),
                v => AppPreferenceStore.SetWaAutoProcessEnabled(v)));

            TambahBaris(new SettingRowVM(
                "Periksa pembaruan aplikasi saat dibuka",
                "Saat aktif, aplikasi memeriksa rilis terbaru di latar belakang ketika dibuka lalu memberitahu lewat lonceng notifikasi apa yang diperbaiki. Perbaikan kecil dipasang sebagai pembaruan tambalan (hanya berkas yang berubah, tanpa installer), perubahan besar memakai installer penuh. Saat nonaktif, pemeriksaan hanya dijalankan dari menu Pembaruan.",
                AppPreferenceStore.IsPeriksaPembaruanSaatMulai(),
                v => AppPreferenceStore.SetPeriksaPembaruanSaatMulai(v)));

            TambahBaris(new SettingRowVM(
                "Pasang pembaruan kecil otomatis saat aplikasi ditutup",
                "Saat aktif, pembaruan kecil (tambalan: hanya berkas yang berubah, data surat & pengaturan aman) diunduh, diverifikasi, dan dipasang otomatis ketika aplikasi ditutup — tanpa menanya lagi; aplikasi dibuka kembali dengan versi terbaru. Hanya pembaruan KECIL yang ikut alur ini: pembaruan besar selalu diminta persetujuan lewat halaman Pembaruan. Paket yang lebih besar dari batas ukuran di bawah tidak pernah dipasang senyap-senyap.",
                AppPreferenceStore.IsPasangOtomatisSaatKeluar(),
                v => AppPreferenceStore.SetPasangOtomatisSaatKeluar(v)));

            TambahBarisAngka(new SettingRowAngkaVM(
                "Batas ukuran pembaruan otomatis (MB)",
                "Ukuran maksimal perubahan yang boleh dipasang otomatis saat aplikasi ditutup (1-500 MB, bawaan 25 MB). Pembaruan kecil yang lebih besar dari batas ini tetap ditawarkan lewat notifikasi dan dipasang manual dari menu Pembaruan.",
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
        // Pesan status halaman.
        //
        // Identitas aplikasi (ikon, nama, versi, pengembang) tidak lagi disimpan di
        // sini — statusbar kaki halaman memakai kontrol bersama AppStatusBar yang
        // membacanya langsung dari atribut assembly.
        // =====================================================================

        /// <summary>Pesan tetap saat tidak ada perubahan yang baru disimpan.</summary>
        private const string StatusSiap = "Semua perubahan tersimpan otomatis — tidak ada yang perlu diklik Simpan.";

        private string _statusMessage = StatusSiap;
        private CancellationTokenSource? _statusTimer;

        /// <summary>Pesan pada statusbar halaman (berubah sesaat setelah menyimpan).</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>Tampilkan pesan status sesaat, lalu kembali ke pesan siap.</summary>
        private void TampilkanStatusSementara(string pesan)
        {
            try
            {
                _statusTimer?.Cancel();
                _statusTimer?.Dispose();
                var sumber = new CancellationTokenSource();
                _statusTimer = sumber;

                StatusMessage = pesan;

                // Kembalikan ke pesan siap setelah jeda; lewat dispatcher tampilan
                // bila ada, supaya pemberitahuan properti tetap di thread UI.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                var konteks = SynchronizationContext.Current;
                _ = Task.Delay(TimeSpan.FromSeconds(3.5), sumber.Token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;

                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.BeginInvoke(new Action(() => StatusMessage = StatusSiap));
                    else if (konteks != null && konteks != SynchronizationContext.Current)
                        konteks.Post(_ => StatusMessage = StatusSiap, null);
                    else
                        StatusMessage = StatusSiap;
                }, TaskScheduler.Default);
            }
            catch
            {
                // Statusbar hanya informasi — jangan pernah mengganggu penyimpanan.
            }
        }

        /// <summary>Tambahkan baris sakelar, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBaris(SettingRowVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowVM.Nilai))
                    TampilkanStatusSementara($"Tersimpan — {baris.Judul}");
            };
            Rows.Add(baris);
        }

        /// <summary>Tambahkan baris bernilai angka, sekaligus melaporkan tiap perubahan ke statusbar.</summary>
        private void TambahBarisAngka(SettingRowAngkaVM baris)
        {
            baris.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingRowAngkaVM.TeksNilai))
                    TampilkanStatusSementara($"Tersimpan — {baris.Judul}");
            };
            RowsAngka.Add(baris);
        }

        // Kredensial klien OAuth Google kini tertanam di aplikasi
        // (GoogleClientCredentialsBawaan) — tidak lagi dikelola dari halaman ini.

        public RelayCommand BatalCommand { get; }

        /// <summary>Buka halaman Tentang Aplikasi (diklik dari identitas di statusbar).</summary>


        // =======================================================================
        // Navigasi bagian halaman. Pengaturan aplikasi sudah banyak, jadi isinya
        // dikelompokkan per bagian dengan daftar navigasi di sisi kiri — bukan
        // sederet kartu panjang yang harus digulir.
        // =======================================================================

        public ObservableCollection<PengaturanSectionVM> Sections { get; } = new();

        public PengaturanSectionVM BagianUmum { get; private set; } = null!;
        public PengaturanSectionVM BagianPenomoran { get; private set; } = null!;
        public PengaturanSectionVM BagianLayananWa { get; private set; } = null!;
        public PengaturanSectionVM BagianGatewayWa { get; private set; } = null!;
        public PengaturanSectionVM BagianGoogleSheet { get; private set; } = null!;
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
            BagianPenomoran = new PengaturanSectionVM("penomoran", "Penomoran Surat", IkonMenu.Dokumen,
                "Awalan nomor surat tiap jenis surat, misalnya SKD 470 menjadi 471.");
            BagianLayananWa = new PengaturanSectionVM("layanan-wa", "Jam Layanan WhatsApp", IkonMenu.LayananOnline,
                "Hari dan jam pemrosesan otomatis permintaan surat dari WhatsApp.");
            BagianGatewayWa = new PengaturanSectionVM("gateway-wa", "Gateway WhatsApp", IkonMenu.LayananOnline,
                "Sambungan WhatsApp Cloud API milik Meta: access token, nomor pengirim, dan uji koneksi.");
            BagianGoogleSheet = new PengaturanSectionVM("google-sheet", "Formulir & Sheet", IkonMenu.Dokumen,
                "Koneksi Google Formulir dan Sheet jawaban, termasuk pembuatan formulir otomatis.");
            BagianInformasi = new PengaturanSectionVM("informasi", "Informasi", IkonMenu.Tentang,
                "Sesi login yang sedang aktif dan lokasi berkas pengaturan pada komputer ini.");

            foreach (var bagian in new[]
                     {
                         BagianUmum, BagianPenomoran, BagianLayananWa, BagianGatewayWa,
                         BagianGoogleSheet, BagianInformasi
                     })
            {
                Sections.Add(bagian);
            }

            SelectedSection = BagianUmum;
        }

        // =======================================================================
        // Pengaturan penomoran surat per jenis (awalan nomor).
        // =======================================================================

        public ObservableCollection<PenomoranBarisVM> Penomoran { get; } = new();

        private string _penomoranInfo = string.Empty;
        private string _penomoranPeringatan = string.Empty;
        private bool _penomoranSibuk;

        /// <summary>Keterangan hasil muat/simpan pengaturan penomoran.</summary>
        public string PenomoranInfo
        {
            get => _penomoranInfo;
            private set => SetProperty(ref _penomoranInfo, value);
        }

        /// <summary>Peringatan awalan kembar (nomor berpotensi sama antar jenis surat).</summary>
        public string PenomoranPeringatan
        {
            get => _penomoranPeringatan;
            private set
            {
                if (SetProperty(ref _penomoranPeringatan, value))
                    OnPropertyChanged(nameof(AdaPeringatanPenomoran));
            }
        }

        public bool AdaPeringatanPenomoran => _penomoranPeringatan.Length > 0;

        public bool PenomoranSibuk
        {
            get => _penomoranSibuk;
            private set
            {
                if (SetProperty(ref _penomoranSibuk, value)) SimpanPenomoranCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Ada awalan pada daftar yang berbeda dari nilai tersimpan.</summary>
        public bool AdaPerubahanPenomoran => Penomoran.Any(b => b.Terubah);

        /// <summary>Berkas penyesuaian penomoran (aman dari pembaruan aplikasi).</summary>
        public string PenomoranLokasiBerkas => _penomoranSurat?.BerkasPenyesuaian ?? "-";

        /// <summary>Berkas bawaan yang memuat daftar jenis surat + format default.</summary>
        public string PenomoranLokasiBawaan => _penomoranSurat?.BerkasBawaan ?? "-";

        public bool PenomoranTersedia => _penomoranSurat != null;

        public AsyncRelayCommand SimpanPenomoranCommand { get; }
        public RelayCommand MuatUlangPenomoranCommand { get; }

        /// <summary>Muat ulang daftar penomoran dari berkas (membuang perubahan belum disimpan).</summary>
        private void MuatPenomoran()
        {
            Penomoran.Clear();

            if (_penomoranSurat == null)
            {
                PenomoranInfo = "Pengaturan penomoran tidak tersedia pada sesi ini.";
                OnPropertyChanged(nameof(AdaPerubahanPenomoran));
                SimpanPenomoranCommand.RaiseCanExecuteChanged();
                return;
            }

            try
            {
                foreach (var entri in _penomoranSurat.MuatSemua())
                {
                    var baris = new PenomoranBarisVM(entri);
                    baris.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(PenomoranBarisVM.Awalan)) return;

                        PerbaruiPeringatanPenomoran();
                        OnPropertyChanged(nameof(AdaPerubahanPenomoran));
                        SimpanPenomoranCommand.RaiseCanExecuteChanged();
                    };
                    Penomoran.Add(baris);
                }

                PerbaruiPeringatanPenomoran();
                PenomoranInfo = $"{Penomoran.Count} jenis surat. Awalan baru berlaku untuk nomor surat " +
                                "yang dibuat setelah pengaturan ini disimpan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan penomoran surat");
                PenomoranInfo = "Gagal memuat pengaturan penomoran: " + ex.Message;
            }

            OnPropertyChanged(nameof(AdaPerubahanPenomoran));
            SimpanPenomoranCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Perbarui peringatan awalan kembar: awalan yang dipakai beberapa jenis
        /// surat dengan urutan nomor sendiri-sendiri bisa menghasilkan nomor sama.
        /// </summary>
        private void PerbaruiPeringatanPenomoran()
        {
            var entri = Penomoran.Select(b => new SuDesApp.Services.PenomoranSuratEntri
            {
                NamaJenis = b.NamaJenis,
                DisplayName = b.DisplayName,
                KodeJenis = b.KodeJenis,
                IsSharedNumbering = b.IsSharedNumbering,
                Awalan = b.Awalan.Trim().Length == 0 ? b.AwalanBawaan : b.Awalan.Trim()
            });

            var bentrok = SuDesApp.Services.PenomoranSuratService.CariBentrok(entri);
            PenomoranPeringatan = bentrok.Count == 0 ? string.Empty : string.Join("\n", bentrok);
        }

        /// <summary>Simpan seluruh awalan yang diubah sekaligus.</summary>
        private async Task SimpanPenomoranAsync()
        {
            if (_penomoranSurat == null || PenomoranSibuk) return;

            var belumBenar = Penomoran.FirstOrDefault(b => b.AdaKesalahan);
            if (belumBenar != null)
            {
                PenomoranInfo = $"Awalan pada '{belumBenar.DisplayName}' belum benar: {belumBenar.PesanKesalahan}";
                return;
            }

            var perubahan = Penomoran.Where(b => b.Terubah)
                .Select(b => new SuDesApp.Services.PerubahanPenomoran
                {
                    NamaJenis = b.NamaJenis,
                    Awalan = b.Awalan
                })
                .ToList();

            if (perubahan.Count == 0)
            {
                PenomoranInfo = "Belum ada awalan yang diubah.";
                return;
            }

            PenomoranSibuk = true;
            try
            {
                var (ok, pesan) = await _penomoranSurat.SimpanAsync(perubahan);
                if (ok) MuatPenomoran();
                PenomoranInfo = pesan;

                if (ok)
                    _logger.LogInformation("Pengaturan penomoran surat diperbarui dari halaman Pengaturan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan penomoran surat");
                PenomoranInfo = "Gagal menyimpan penomoran: " + ex.Message;
            }
            finally
            {
                PenomoranSibuk = false;
            }
        }

        /// <summary>
        /// Kunci seksi yang diminta untuk difokuskan saat halaman dibuka (mis.
        /// dari chip status WA/Sheet di statusbar): "gateway-wa", "google-sheet",
        /// "google-oauth". Kosong = tampil pada bagian pertama. Di-set lewat
        /// <see cref="SetFocusSection"/> SEBELUM halaman dinavigasikan; view
        /// membacanya saat DataContext terpasang untuk menyorot kartunya.
        /// </summary>
        public string? FocusSection { get; private set; }

        /// <summary>
        /// Tandai seksi yang akan difokuskan (dipanggil sebelum Navigate); bagian
        /// navigasi yang memuat seksi tersebut langsung dipilih.
        /// </summary>
        public void SetFocusSection(string? sectionKey)
        {
            FocusSection = sectionKey;
            if (string.IsNullOrWhiteSpace(sectionKey)) return;

            var bagian = Sections.FirstOrDefault(s =>
                string.Equals(s.Key, sectionKey.Trim(), StringComparison.OrdinalIgnoreCase));

            if (bagian != null) SelectedSection = bagian;
        }

        /// <summary>Buka halaman Panduan WhatsApp (Bagian D = mode tautan Google Form/Sheet).</summary>
        private void BukaPanduanWa()
        {
            try
            {
                var vm = _provider.GetRequiredService<PanduanWaViewModel>();
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka panduan WhatsApp dari Pengaturan Aplikasi");
            }
        }
    }
}
