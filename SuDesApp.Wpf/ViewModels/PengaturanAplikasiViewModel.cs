using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

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
    /// Halaman Pengaturan Aplikasi — pusat preferensi perilaku aplikasi
    /// (berbeda dari Pengaturan Surat yang mengelola data desa). Setiap
    /// tombol geser tersimpan langsung saat digeser; efeknya berlaku pada
    /// login berikutnya / penutupan aplikasi berikutnya sesuai fungsinya.
    /// </summary>
    public class PengaturanAplikasiViewModel : ObservableObject
    {
        private readonly IServiceProvider _provider;
        private readonly NavigationService _navigation;
        private readonly ILogger<PengaturanAplikasiViewModel> _logger;
        private readonly GoogleDriveService? _googleDrive;

        public ObservableCollection<SettingRowVM> Rows { get; } = new();

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
                        AppPreferenceStore.SetWaServiceOpen(bersih);
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
                        AppPreferenceStore.SetWaServiceClose(bersih);
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
            => "Gateway aktif: WhatsApp Cloud API (Meta, resmi). Aplikasi hanya mengirim teks/tautan — gratis dalam jendela 24 jam.";

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
                    WizardValidasiInfo = "⚠ Isi access token terlebih dahulu — cara mengambilnya ada di Panduan WhatsApp Bagian A.";
                    return Task.CompletedTask;
                }
                WizardStep = 1;
            }
            else if (WizardStep == 1)
            {
                var phoneId = WaCloudApiPhoneId.Trim();
                if (phoneId.Length == 0 || !phoneId.All(char.IsDigit))
                {
                    WizardValidasiInfo = "⚠ Phone Number ID berupa deretan angka — salin dari Meta for Developers → WhatsApp → API Setup.";
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
            set { if (SetProperty(ref _waSheetFormUrl, value ?? string.Empty)) WaSheetOptions.SetFormUrl(value); }
        }

        private string _waSheetUjiInfo = string.Empty;
        public string WaSheetUjiInfo
        {
            get => _waSheetUjiInfo;
            private set => SetProperty(ref _waSheetUjiInfo, value);
        }

        public AsyncRelayCommand UjiWaSheetCommand { get; }

        // ==== Template Google Sheet per jenis surat ====

        /// <summary>Item pilihan jenis surat untuk dropdown template (binding-safe).</summary>
        public class TemplateJenisVM
        {
            public string Key { get; }
            public string Label { get; }
            public TemplateJenisVM(string key, string label) { Key = key; Label = label; }
            public override string ToString() => Label;
        }

        private static readonly TemplateJenisVM[] TemplateJenisOptions =
        {
            new("SEMUA", "Semua jenis (satu berkas, satu tab per jenis)"),
            new("SKTM", "SKTM"),
            new("SKD_UMUM", "SKD Umum"),
            new("DOMISILI_WARGA", "Domisili Warga"),
            new("PENGANTAR_SKCK", "Pengantar SKCK"),
            new("SKU", "SKU (Keterangan Usaha)"),
            new("IZIN_ORTU", "Izin Orang Tua"),
            new("INSTANSI", "Surat Instansi"),
        };

        /// <summary>Opsi pilihan jenis surat untuk dropdown template.</summary>
        public IReadOnlyList<TemplateJenisVM> TemplateJenisList => TemplateJenisOptions;

        private TemplateJenisVM _templateJenis = TemplateJenisOptions[0];
        /// <summary>Jenis surat yang dipilih untuk template Sheet.</summary>
        public TemplateJenisVM TemplateJenis
        {
            get => _templateJenis;
            set => SetProperty(ref _templateJenis, value);
        }

        private string _waSheetTemplateInfo = string.Empty;
        /// <summary>Hasil pembuatan/unggah template Sheet.</summary>
        public string WaSheetTemplateInfo
        {
            get => _waSheetTemplateInfo;
            private set => SetProperty(ref _waSheetTemplateInfo, value);
        }

        public AsyncRelayCommand BuatTemplateSheetCommand { get; }
        public AsyncRelayCommand BuatDanUnggahTemplateCommand { get; }

        /// <summary>
        /// Membuat template .xlsx Google Sheet sesuai jenis surat terpilih dan
        /// menyimpannya ke lokasi pilihan operator (mode SaveFileDialog).
        /// </summary>
        private async Task BuatTemplateSheetAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Simpan Template Google Sheet",
                    Filter = "Berkas Excel (*.xlsx)|*.xlsx",
                    FileName = $"Template-Sheet-{(TemplateJenis.Key == "SEMUA" ? "Semua" : TemplateJenis.Label.Replace(' ', '-'))}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;

                WaSheetTemplateInfo = "Membuat template…";
                var path = await Task.Run(() => WaSheetTemplateService.BuatTemplateFile(TemplateJenis.Key, Path.GetDirectoryName(dialog.FileName)!));
                if (!string.Equals(path, dialog.FileName, StringComparison.OrdinalIgnoreCase))
                    File.Move(path, dialog.FileName, overwrite: true);

                WaSheetTemplateInfo = "Template dibuat: " + dialog.FileName +
                    "\nUnggah ke Google Drive lalu buka dengan Google Sheets, atau pakai tombol 'Buat + Unggah ke Drive'.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat template Google Sheet");
                WaSheetTemplateInfo = "Gagal: " + ex.Message;
            }
        }

        /// <summary>
        /// Membuat template .xlsx lalu mengunggahnya langsung ke Google Drive
        /// (folder akun yang terhubung). Operator cukup membukanya di Drive dan
        /// memilih "Open with Google Sheets".
        /// </summary>
        private async Task BuatDanUnggahTemplateAsync()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                var drive = app?.ServiceProvider.GetRequiredService<GoogleDriveService>();
                if (drive == null || !drive.IsOAuthEnabled || !drive.HasStoredToken())
                {
                    WaSheetTemplateInfo = "Akun Google belum terhubung. Masuk dengan Akun Google terlebih dahulu, atau pakai tombol simpan ke berkas.";
                    return;
                }

                WaSheetTemplateInfo = "Membuat & mengunggah template…";
                var tempDir = Path.Combine(Path.GetTempPath(), "SuDesApp-SheetTemplate");
                var path = await Task.Run(() => WaSheetTemplateService.BuatTemplateFile(TemplateJenis.Key, tempDir));

                var uploaded = await drive.UploadFileAsync(path, parentId: null,
                    Path.GetFileName(path), null);
                var link = drive.ToWebViewLink(uploaded.Id);

                try { File.Delete(path); } catch { /* biarkan bila terkunci */ }

                WaSheetTemplateInfo = "Template terunggah ke Google Drive ✓\n" + link +
                    "\nBuka tautan itu → File → Save as Google Sheets (bila masih format Excel) → salin URL Sheet-nya ke kolom 'URL Sheet jawaban'.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunggah template Google Sheet");
                WaSheetTemplateInfo = "Gagal: " + ex.Message;
            }
        }

        public RelayCommand BukaPanduanWaCommand { get; }
        public RelayCommand BukaGoogleFormsCommand { get; }

        /// <summary>
        /// Buka Google Forms di browser default untuk membuat formulir baru.
        /// </summary>
        private void BukaGoogleForms()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://forms.google.com",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka Google Forms di browser");
            }
        }

        /// <summary>Ringkasan status mode tautan untuk ditampilkan di kartu pengaturan.</summary>
        public string WaSheetRingkasan
            => WaSheetOptions.IsLinkModeEnabled()
                ? (WaSheetOptions.IsConfigured()
                    ? "Mode aktif: pesan apa pun dari warga dibalas tautan Google Formulir untuk diisi sendiri; jawaban terkumpul di Google Sheet diproses otomatis dan tautan unduh PDF dikirim balik ke WhatsApp."
                    : "Mode aktif tetapi URL Google Formulir/Sheet belum diisi — balasan sementara masih memakai percakapan format.")
                : "Mode nonaktif: alur percakapan format dipakai (warga mengetik data sesuai format per jenis surat).";

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
                WaSheetUjiInfo = "Aplikasi belum siap.";
                return;
            }

            var drive = app.ServiceProvider.GetRequiredService<GoogleDriveService>();
            if (!drive.IsOAuthEnabled || !drive.HasStoredToken())
            {
                WaSheetUjiInfo = "Akun Google belum terhubung. Masuk dengan Akun Google terlebih dahulu (login).";
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
                                ? "Google Formulir ✓ — tautan bisa dibuka warga."
                                : "⚠️ URL terbuka tetapi bukan halaman Google Formulir — periksa kembali URL-nya.");
                        }
                        else
                        {
                            bagian.Add($"⚠️ Google Formulir TIDAK bisa dibuka (HTTP {(int)resp.StatusCode}) — warga tidak akan bisa mengisi. Periksa izin berbagi form.");
                        }
                    }
                    catch (Exception exForm)
                    {
                        bagian.Add("⚠️ Google Formulir tidak terjangkau: " + exForm.Message);
                    }
                }
                else if (string.IsNullOrWhiteSpace(sheetId))
                {
                    bagian.Add("⚠️ URL Formulir kosong dan Sheet tidak terbaca — warga belum bisa menerima tautan form.");
                }
                else
                {
                    bagian.Add("⚠️ URL Formulir kosong — tautan untuk warga akan ditebak dari ID Sheet dan BISA MATI bila Sheet bukan sheet respons Google Form. Disarankan mengisi URL Formulir.");
                }

                WaSheetUjiInfo = string.Join("\n\n", bagian);
            }
            catch (Exception ex)
            {
                WaSheetUjiInfo = "Gagal: " + ex.Message;
            }
        }

        /// <summary>Uji token Cloud API: ambil profil nomor via Graph API GET /{phone-id}.</summary>
        private async Task UjiCloudApiAsync()
        {
            if (string.IsNullOrWhiteSpace(WaCloudApiToken) || string.IsNullOrWhiteSpace(WaCloudApiPhoneId))
            {
                WaUjiKoneksiInfo = "Isi access token dan Phone Number ID terlebih dahulu.";
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
                    ? "Terhubung ✓ — " + body.Trim()
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
            IServiceProvider provider)
        {
            _navigation = navigation;
            _logger = logger;
            _provider = provider;

            _googleDrive = _provider.GetService<GoogleDriveService>();
            var kredensial = _googleDrive == null ? null : GoogleClientCredentials.Load();
            GoogleClientId = kredensial?.ClientId ?? string.Empty;
            GoogleClientSecret = kredensial?.ClientSecret ?? string.Empty;

            SimpanKredensialGoogleCommand = new AsyncRelayCommand(SimpanKredensialGoogleAsync);
            UjiKredensialGoogleCommand = new AsyncRelayCommand(UjiKredensialGoogleAsync);

            UjiCloudApiCommand = new AsyncRelayCommand(UjiCloudApiAsync);
            WizardLanjutCommand = new AsyncRelayCommand(WizardLanjutAsync);
            WizardKembaliCommand = new AsyncRelayCommand(WizardKembaliAsync);
            // Wizard dibuka di langkah akhir bila gateway sudah pernah dikonfigurasi.
            _wizardStep = WaGatewayReady ? 2 : 0;
            UjiWaSheetCommand = new AsyncRelayCommand(UjiWaSheetAsync);
            BuatTemplateSheetCommand = new AsyncRelayCommand(BuatTemplateSheetAsync);
            BuatDanUnggahTemplateCommand = new AsyncRelayCommand(BuatDanUnggahTemplateAsync);
            BukaPanduanWaCommand = new RelayCommand(BukaPanduanWa);
            BukaGoogleFormsCommand = new RelayCommand(BukaGoogleForms);
            MuatHariLayanan();

            Rows.Add(new SettingRowVM(
                "Login otomatis dengan Google",
                "Bila aktif: pengguna yang pernah login Google langsung masuk dengan profil akunnya saat aplikasi dibuka. Bila nonaktif: form username/password selalu tampil lebih dulu — akun Google tetap bisa dipakai lewat tombol 'Masuk dengan Akun Google'.",
                AppPreferenceStore.IsGoogleAutoLoginEnabled(),
                v => AppPreferenceStore.SetGoogleAutoLoginEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Backup otomatis ke Google Drive saat aplikasi ditutup",
                "Membuat cadangan database + lampiran + template ke folder SuDesApp-Backup di Google Drive setiap penutupan aplikasi (maksimal 1x per hari, hanya bila ada akun Google yang terhubung).",
                AppPreferenceStore.IsBackupDriveOnExitEnabled(),
                v => AppPreferenceStore.SetBackupDriveOnExitEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Backup otomatis formulir ke Google Drive",
                "Menyinkronkan template formulir (PDF) ke folder SuDesApp-Formulir di Google Drive setiap ada unduhan baru, atau template ditambah/dihapus di Pengaturan Formulir. Hanya berjalan bila ada akun Google yang terhubung.",
                AppPreferenceStore.IsAutoBackupFormulirDriveEnabled(),
                v => AppPreferenceStore.SetAutoBackupFormulirDriveEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Proses otomatis permintaan WhatsApp",
                "Permintaan surat dari WhatsApp diproses sendiri tanpa menunggu operator: surat dibuat, PDF di-generate dengan generator resmi, lalu dikirim balik ke nomor pemohon. Permintaan yang gagal diproses masuk status PERLU_PERBAIKAN di panel Layanan Online untuk diperiksa operator.",
                AppPreferenceStore.IsWaAutoProcessEnabled(),
                v => AppPreferenceStore.SetWaAutoProcessEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Riwayat aktivitas",
                "Mencatat siapa (email Google atau admin) yang membuat, mengedit, mengubah status, dan menghapus surat maupun arsip — tampil di menu Riwayat Aktivitas.",
                AppPreferenceStore.IsActivityLoggingEnabled(),
                v => AppPreferenceStore.SetActivityLoggingEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Bersihkan PDF sementara saat aplikasi dibuka",
                "Menghapus berkas pratinjau PDF lama di folder TempPDF agar tidak menumpuk sepanjang pemakaian.",
                AppPreferenceStore.IsCleanupTempPdfEnabled(),
                v => AppPreferenceStore.SetCleanupTempPdfEnabled(v)));

            Rows.Add(new SettingRowVM(
                "Hapus hasil ekspor lama (lebih dari 30 hari)",
                "Berkas PDF hasil ekspor di folder Output/PDF yang berumur lebih dari 30 hari dihapus otomatis saat aplikasi dibuka.",
                AppPreferenceStore.IsCleanupOldExportsEnabled(),
                v => AppPreferenceStore.SetCleanupOldExportsEnabled(v)));

            BatalCommand = new RelayCommand(() => _navigation.ShowDefault());
        }

        /// <summary>Sesi aktif sekarang (dibaca ulang tiap halaman dibuka).</summary>
        public string SesiAktif =>
            $"{SessionContext.Display} ({SessionContext.LoginMethod}) — masuk {SessionContext.LoginTime:dd-MM-yyyy HH:mm}";

        /// <summary>Lokasi penyimpanan preferensi.</summary>
        public string LokasiPreferensi => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp", "login_prefs.json");

        // ===== Kredensial klien OAuth Google (Client ID & Client Secret) =====
        private string _googleClientId = string.Empty;
        private string _googleClientSecret = string.Empty;
        private string _googleKredensialInfo = string.Empty;

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

        public string GoogleKredensialInfo
        {
            get => _googleKredensialInfo;
            private set => SetProperty(ref _googleKredensialInfo, value);
        }

        /// <summary>Lokasi berkas kredensial terenkripsi DPAPI (untuk ditampilkan ke teknisi).</summary>
        public string GoogleKredensialLokasi => GoogleDriveService.CredentialsStorePath;

        /// <summary>Status apakah klien OAuth sudah aktif (Client ID & Secret tersedia).</summary>
        public string GoogleKredensialStatus => _googleDrive?.IsOAuthEnabled == true
            ? "Klien OAuth aktif — akun Google sudah bisa dihubungkan."
            : "Klien OAuth belum aktif — isi Client ID & Client Secret di bawah lalu simpan.";

        public AsyncRelayCommand SimpanKredensialGoogleCommand { get; }
        public AsyncRelayCommand UjiKredensialGoogleCommand { get; }

        /// <summary>
        /// Simpan Client ID & Client Secret terenkripsi DPAPI per user Windows
        /// (tidak lagi plaintext di appsettings.json yang ikut terdistribusi).
        /// </summary>
        private Task SimpanKredensialGoogleAsync()
        {
            if (_googleDrive == null)
            {
                GoogleKredensialInfo = "Layanan Google tidak tersedia pada sesi ini.";
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(GoogleClientId) || string.IsNullOrWhiteSpace(GoogleClientSecret))
            {
                GoogleKredensialInfo = "Client ID dan Client Secret wajib diisi.";
                return Task.CompletedTask;
            }

            try
            {
                _googleDrive.SaveClientCredentials(GoogleClientId, GoogleClientSecret);
                GoogleKredensialInfo = "Kredensial tersimpan terenkripsi di:\n" + GoogleDriveService.CredentialsStorePath +
                    "\n\nLangkah berikutnya: klik \"Uji Koneksi\" untuk membuka login akun Google.";
                OnPropertyChanged(nameof(GoogleKredensialStatus));
                _logger.LogInformation("Kredensial klien OAuth Google disimpan dari halaman Pengaturan.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan kredensial klien OAuth Google");
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
                GoogleKredensialInfo = "Simpan Client ID & Client Secret terlebih dahulu, baru uji koneksi.";
                return;
            }

            try
            {
                GoogleKredensialInfo = "Membuka login Google… selesaikan di browser bila diminta.";
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var email = await Task.Run(() => drive.GetAccountEmailAsync(cts.Token), cts.Token);
                GoogleKredensialInfo = string.IsNullOrWhiteSpace(email)
                    ? "Login selesai tetapi email tidak diperoleh. Periksa Google Drive API & OAuth consent screen."
                    : "Terhubung ✓ — " + email;
            }
            catch (OperationCanceledException)
            {
                GoogleKredensialInfo = "Waktu login habis atau dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uji koneksi kredensial Google gagal");
                GoogleKredensialInfo = "Gagal: " + ex.Message;
            }
        }

        public RelayCommand BatalCommand { get; }

        /// <summary>
        /// Kunci seksi yang diminta untuk difokuskan saat halaman dibuka (mis.
        /// dari chip status WA/Sheet di statusbar): "gateway-wa", "google-sheet".
        /// Kosong = tampil normal dari atas. Di-set lewat <see cref="SetFocusSection"/>
        /// SEBELUM halaman dinavigasikan; view membacanya saat DataContext terpasang.
        /// </summary>
        public string? FocusSection { get; private set; }

        /// <summary>Tandai seksi yang akan difokuskan (dipanggil sebelum Navigate).</summary>
        public void SetFocusSection(string? sectionKey) => FocusSection = sectionKey;

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
