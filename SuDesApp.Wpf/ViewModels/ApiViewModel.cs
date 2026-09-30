using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using SuDesApp.Api;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman API Desa: menyalakan/mematikan listener, mengatur port dan batas
    /// laju, mengelola kunci API, serta menampilkan daftar endpoint yang tersedia.
    ///
    /// Pengaturan disimpan di AppPreferenceStore, bukan di database, karena
    /// listener harus bisa membaca port dan status aktif tanpa membuka
    /// koneksi database. Semua nilai punya default aman: API mati, hanya
    /// localhost, dan kunci API tetap wajib ada.
    /// </summary>
    public class ApiViewModel : ObservableObject
    {
        private readonly ApiListener _listener;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<ApiViewModel> _logger;

        // HttpClient dipakai bersama: satu handler sockets cukup untuk seluruh
        // pemeriksaan, dan halaman API bisa dibuka berulang kali.
        private static readonly HttpClient HttpUji = new() { Timeout = TimeSpan.FromSeconds(5) };

        private bool _aktif;
        private bool _izinkanJaringan;
        private bool _catatAktivitas;
        private int _port;
        private int _batasPermintaan;
        private string _kunciApi = string.Empty;
        private bool _kunciTersembunyi = true;
        private string _statusText = string.Empty;
        private string _galatText = string.Empty;
        private string _hasilTes = string.Empty;
        private bool _sedangUji;
        private string _namaKunciCakupan = string.Empty;
        private ApiCakupan _cakupanTerpilih = ApiCakupan.Agregat;

        public ApiViewModel(
            ApiListener listener,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<ApiViewModel> logger)
        {
            _listener = listener ?? throw new ArgumentNullException(nameof(listener));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !SedangUji);
            BuatKunciCommand = new AsyncRelayCommand(BuatKunciAsync, () => !SedangUji);
            SalinKunciCommand = new RelayCommand(SalinKunci, () => AdaKunci);
            TampilkanKunciCommand = new RelayCommand(() => KunciTersembunyi = !KunciTersembunyi);
            UjiKoneksiCommand = new AsyncRelayCommand(UjiKoneksiAsync, () => !SedangUji);
            MuatUlangCommand = new AsyncRelayCommand(() => { MuatUlang(); return Task.CompletedTask; }, () => !SedangUji);
            BuatKunciCakupanCommand = new AsyncRelayCommand(BuatKunciCakupanAsync, () => !SedangUji);
            HapusKunciCakupanCommand = new AsyncRelayCommand<BarisKunciCakupan>(HapusKunciCakupanAsync);
            SalinKunciCakupanCommand = new RelayCommand<BarisKunciCakupan>(SalinKunciCakupan);

            Endpoint = new ObservableCollection<ApiEndpoint>(ApiRute.Semua);
        }

        // ---------- header ----------

        public string HeaderTitle => "API DESA";

        public string HeaderSubtitle =>
            "Buka angka desa, perangkat desa, dan antrean surat ke sistem luar. "
            + "Setiap permintaan harus menyertakan kunci API.";

        // ---------- daftar endpoint ----------

        public ObservableCollection<ApiEndpoint> Endpoint { get; }

        // ---------- status listener ----------

        public bool ListenerBerjalan => _listener.Berjalan;

        public string AlamatDasar => ApiPengaturan.AlamatDasar();

        public string AlamatStatus => ApiPengaturan.AlamatPrefix();

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string GalatText
        {
            get => _galatText;
            private set
            {
                if (SetProperty(ref _galatText, value))
                    OnPropertyChanged(nameof(AdaGalat));
            }
        }

        public bool AdaGalat => !string.IsNullOrWhiteSpace(GalatText);

        public string HasilTes
        {
            get => _hasilTes;
            private set => SetProperty(ref _hasilTes, value);
        }

        public bool SedangUji
        {
            get => _sedangUji;
            private set
            {
                if (SetProperty(ref _sedangUji, value))
                {
                    OnPropertyChanged(nameof(LabelUji));
                    SegarkanPerintah();
                }
            }
        }

        public string LabelUji => SedangUji ? "Menguji..." : "Uji Koneksi";

        // ---------- isian ----------

        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        public bool IzinkanJaringan
        {
            get => _izinkanJaringan;
            set => SetProperty(ref _izinkanJaringan, value);
        }

        public bool CatatAktivitas
        {
            get => _catatAktivitas;
            set => SetProperty(ref _catatAktivitas, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public int BatasPermintaan
        {
            get => _batasPermintaan;
            set => SetProperty(ref _batasPermintaan, value);
        }

        public string KunciApi
        {
            get => _kunciApi;
            private set
            {
                if (SetProperty(ref _kunciApi, value))
                {
                    OnPropertyChanged(nameof(TeksKunci));
                    OnPropertyChanged(nameof(AdaKunci));
                    SegarkanPerintah();
                }
            }
        }

        public bool KunciTersembunyi
        {
            get => _kunciTersembunyi;
            set
            {
                if (SetProperty(ref _kunciTersembunyi, value))
                {
                    OnPropertyChanged(nameof(TeksKunci));
                    OnPropertyChanged(nameof(LabelKunci));
                }
            }
        }

        public string TeksKunci => KunciTersembunyi && AdaKunci
            ? new string('*', Math.Min(KunciApi.Length, 48))
            : KunciApi;

        public string LabelKunci => KunciTersembunyi ? "Tampilkan" : "Sembunyikan";

        public bool AdaKunci => !string.IsNullOrWhiteSpace(KunciApi);

        // ---------- kunci cakupan ----------

        /// <summary>
        /// Pilihan cakupan untuk kunci tambahan. Cakupan Penuh sengaja tidak
        /// muncul di sini: hanya kunci utama yang berwenang penuh.
        /// </summary>
        public IReadOnlyList<OpsiCakupan> PilihanCakupan { get; } = new[]
        {
            new OpsiCakupan { Nilai = ApiCakupan.Agregat, Tampilan = "Agregat — baca statistik & status" },
            new OpsiCakupan { Nilai = ApiCakupan.Permintaan, Tampilan = "Permintaan — kirim & pantau surat online" }
        };

        /// <summary>Daftar kunci tambahan (tanpa kunci utama) untuk ditampilkan di daftar.</summary>
        public ObservableCollection<BarisKunciCakupan> KunciCakupan { get; } = new();

        public ApiCakupan CakupanTerpilih
        {
            get => _cakupanTerpilih;
            set => SetProperty(ref _cakupanTerpilih, value);
        }

        public string NamaKunciCakupan
        {
            get => _namaKunciCakupan;
            set => SetProperty(ref _namaKunciCakupan, value);
        }

        public bool AdaKunciCakupan => KunciCakupan.Count > 0;

        public bool TidakAdaKunciCakupan => KunciCakupan.Count == 0;

        // ---------- perintah ----------

        public AsyncRelayCommand SimpanCommand { get; }
        public AsyncRelayCommand BuatKunciCommand { get; }
        public RelayCommand SalinKunciCommand { get; }
        public RelayCommand TampilkanKunciCommand { get; }
        public AsyncRelayCommand UjiKoneksiCommand { get; }
        public AsyncRelayCommand MuatUlangCommand { get; }
        public AsyncRelayCommand BuatKunciCakupanCommand { get; }
        public AsyncRelayCommand<BarisKunciCakupan> HapusKunciCakupanCommand { get; }
        public RelayCommand<BarisKunciCakupan> SalinKunciCakupanCommand { get; }

        // ---------- muat ----------

        /// <summary>
        /// Baca nilai tersimpan. Dipanggil saat halaman dibuka supaya isian
        /// mencerminkan keadaan listener yang sedang hidup, bukan sekadar isi
        /// berkas preferensi.
        /// </summary>
        public void MuatUlang()
        {
            try
            {
                Aktif = ApiPengaturan.Aktif;
                Port = ApiPengaturan.Port;
                IzinkanJaringan = ApiPengaturan.IzinkanJaringan;
                CatatAktivitas = ApiPengaturan.CatatAktivitas;
                BatasPermintaan = ApiPengaturan.BatasPermintaan;
                KunciApi = ApiKunci.Muat() ?? string.Empty;
                MuatKunciCakupan();
                GalatText = _listener.Berjalan ? string.Empty : _listener.GalatTerakhir ?? string.Empty;
                SegarkanStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan API");
                GalatText = "Gagal memuat pengaturan: " + ex.Message;
            }
        }

        // ---------- simpan ----------

        private async Task SimpanAsync()
        {
            if (!ApiPengaturan.NormalisasiPort(Port, out var port))
            {
                GalatText = $"Port harus antara {ApiPengaturan.PortMinimum} dan {ApiPengaturan.PortMaksimum}.";
                await _messageService.ShowWarningAsync(GalatText);
                return;
            }

            try
            {
                ApiPengaturan.SetPort(port);
                ApiPengaturan.SetAktif(Aktif);
                ApiPengaturan.SetIzinkanJaringan(IzinkanJaringan);
                ApiPengaturan.SetCatatAktivitas(CatatAktivitas);
                ApiPengaturan.SetBatasPermintaan(BatasPermintaan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan API");
                GalatText = "Gagal menyimpan pengaturan: " + ex.Message;
                await _messageService.ShowErrorAsync(GalatText);
                return;
            }

            // Listener dinyalakan ulang setiap kali pengaturan disimpan: port,
            // mode jaringan, dan kunci API dibaca saat listener mulai, jadi
            // perubahan tidak berlaku pada listener yang sedang hidup.
            _listener.Berhenti();

            if (Aktif && !_listener.Mulai())
            {
                GalatText = _listener.GalatTerakhir ?? "API tidak bisa dinyalakan.";
                await _messageService.ShowWarningAsync(GalatText);
            }
            else
            {
                GalatText = string.Empty;
            }

            SegarkanStatus();

            _activityLog.Log(
                "API", $"Pengaturan API (port {port})", "Ubah",
                $"aktif={Aktif}, jaringan={IzinkanJaringan}, batas={BatasPermintaan}");

            await _messageService.ShowInfoAsync(
                ListenerBerjalan
                    ? "Pengaturan disimpan. API aktif di " + ApiPengaturan.AlamatPrefix()
                    : "Pengaturan disimpan. API tidak berjalan.");
        }

        private async Task BuatKunciAsync()
        {
            var konfirmasi = await _messageService.ShowConfirmationAsync(
                "Buat Kunci API Baru",
                "Kunci yang sekarang tidak akan berlaku lagi, dan sistem luar harus "
                + "diberi kunci baru. Lanjut?");
            if (!konfirmasi) return;

            try
            {
                KunciApi = ApiKunci.BuatBaru();
                KunciTersembunyi = false;
                await _messageService.ShowInfoAsync(
                    "Kunci API baru dibuat. Jangan bagikan ke publik.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat kunci API");
                GalatText = "Gagal membuat kunci API: " + ex.Message;
                await _messageService.ShowErrorAsync(GalatText);
            }
        }

        private void SalinKunci()
        {
            try
            {
                Clipboard.SetText(KunciApi);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin kunci API ke papan klip");
                GalatText = "Gagal menyalin kunci: " + ex.Message;
            }
        }

        // ---------- kunci cakupan ----------

        /// <summary>
        /// Muat ulang daftar kunci cakupan. Listener membaca berkas kunci di
        /// setiap permintaan, jadi kunci baru/lama langsung berlaku tanpa
        /// restart listener.
        /// </summary>
        private void MuatKunciCakupan()
        {
            KunciCakupan.Clear();
            foreach (var kunci in ApiKunci.MuatSemua())
            {
                // Kunci utama (Penuh) sudah tampil di kotak KUNCI API di atas.
                if (kunci.Cakupan == ApiCakupan.Penuh) continue;
                KunciCakupan.Add(new BarisKunciCakupan(kunci));
            }

            OnPropertyChanged(nameof(AdaKunciCakupan));
            OnPropertyChanged(nameof(TidakAdaKunciCakupan));
            SegarkanPerintah();
        }

        private async Task BuatKunciCakupanAsync()
        {
            try
            {
                var dibuat = ApiKunci.BuatTambahan(CakupanTerpilih, NamaKunciCakupan);
                NamaKunciCakupan = string.Empty;
                MuatKunciCakupan();

                _activityLog.Log(
                    "API", $"Kunci cakupan {dibuat.Cakupan}", "Tambah",
                    string.IsNullOrWhiteSpace(dibuat.Nama) ? "(tanpa nama)" : dibuat.Nama);

                await _messageService.ShowInfoAsync(
                    $"Kunci cakupan {dibuat.Cakupan} dibuat. Salin nilainya dari daftar kunci "
                    + "cakupan, lalu serahkan ke sistem luar.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat kunci cakupan");
                GalatText = "Gagal membuat kunci cakupan: " + ex.Message;
                await _messageService.ShowErrorAsync(GalatText);
            }
        }

        private async Task HapusKunciCakupanAsync(BarisKunciCakupan? baris)
        {
            if (baris == null) return;

            var konfirmasi = await _messageService.ShowConfirmationAsync(
                "Hapus Kunci Cakupan",
                $"Kunci cakupan {baris.Entri.Cakupan} ({baris.NamaTampil}) akan dihapus permanen. "
                + "Sistem luar yang memakainya langsung ditolak. Lanjut?");
            if (!konfirmasi) return;

            try
            {
                var terhapus = ApiKunci.HapusTambahan(baris.Entri.Nilai);
                MuatKunciCakupan();

                if (!terhapus)
                {
                    await _messageService.ShowWarningAsync(
                        "Kunci sudah tidak ada (mungkin sudah dihapus); daftar dimuat ulang.");
                    return;
                }

                _activityLog.Log(
                    "API", $"Kunci cakupan {baris.Entri.Cakupan}", "Hapus", baris.NamaTampil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus kunci cakupan");
                GalatText = "Gagal menghapus kunci cakupan: " + ex.Message;
                await _messageService.ShowErrorAsync(GalatText);
            }
        }

        private void SalinKunciCakupan(BarisKunciCakupan? baris)
        {
            if (baris == null) return;

            try
            {
                Clipboard.SetText(baris.Entri.Nilai);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin kunci cakupan ke papan klip");
                GalatText = "Gagal menyalin kunci: " + ex.Message;
            }
        }

        // ---------- uji koneksi ----------

        private async Task UjiKoneksiAsync()
        {
            if (!_listener.Berjalan)
            {
                await _messageService.ShowWarningAsync(
                    "API belum berjalan, jadi belum ada yang bisa diuji.");
                return;
            }

            if (!AdaKunci)
            {
                await _messageService.ShowWarningAsync("Kunci API belum dibuat.");
                return;
            }

            SedangUji = true;
            HasilTes = string.Empty;

            try
            {
                using var permintaan = new HttpRequestMessage(HttpMethod.Get, AlamatStatus);
                permintaan.Headers.Add("X-Api-Key", KunciApi);

                using var balasan = await HttpUji.SendAsync(permintaan).ConfigureAwait(false);
                var isi = await balasan.Content.ReadAsStringAsync().ConfigureAwait(false);

                HasilTes = balasan.IsSuccessStatusCode
                    ? $"Berhasil. Server menjawab {(int)balasan.StatusCode} {balasan.ReasonPhrase}."
                    : $"Gagal. Server menjawab {(int)balasan.StatusCode} {balasan.ReasonPhrase}. {isi}";
            }
            catch (Exception ex)
            {
                HasilTes = "Gagal menghubungi listener: " + ex.Message;
            }
            finally
            {
                SedangUji = false;
            }
        }

        // ---------- pembantu ----------

        private void SegarkanStatus()
        {
            OnPropertyChanged(nameof(ListenerBerjalan));
            OnPropertyChanged(nameof(AlamatDasar));
            OnPropertyChanged(nameof(AlamatStatus));
            SegarkanPerintah();
        }

        private void SegarkanPerintah()
        {
            SimpanCommand.RaiseCanExecuteChanged();
            BuatKunciCommand.RaiseCanExecuteChanged();
            UjiKoneksiCommand.RaiseCanExecuteChanged();
            MuatUlangCommand.RaiseCanExecuteChanged();
            SalinKunciCommand.RaiseCanExecuteChanged();
            BuatKunciCakupanCommand.RaiseCanExecuteChanged();
            HapusKunciCakupanCommand.RaiseCanExecuteChanged();
            SalinKunciCakupanCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Baris ComboBox pemilihan cakupan saat membuat kunci tambahan.</summary>
    public sealed class OpsiCakupan
    {
        public ApiCakupan Nilai { get; init; } = ApiCakupan.Agregat;

        public string Tampilan { get; init; } = string.Empty;
    }

    /// <summary>Baris tampilan satu kunci cakupan di daftar halaman API.</summary>
    public sealed class BarisKunciCakupan
    {
        public BarisKunciCakupan(ApiKunciEntry entri) => Entri = entri;

        public ApiKunciEntry Entri { get; }

        public string NamaTampil => string.IsNullOrWhiteSpace(Entri.Nama) ? "(tanpa nama)" : Entri.Nama!;

        /// <summary>Ringkasan baris kedua: cakupan, sebagian nilai, dan tanggal pembuatan.</summary>
        public string RingkasTampil =>
            $"{Entri.Cakupan} · {Potong(Entri.Nilai)} · dibuat {Entri.Dibuat:dd-MM-yyyy HH:mm}";

        // Nilai dipotong supaya kunci penuh tidak terbaca dari layar; tombol
        // Salin tetap menyalin nilai utuh untuk diserahkan ke sistem luar.
        private static string Potong(string nilai) => nilai.Length > 14 ? nilai[..10] + "…" : nilai;
    }
}
