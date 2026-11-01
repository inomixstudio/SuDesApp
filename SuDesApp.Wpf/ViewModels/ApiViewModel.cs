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
        private string _ipDiizinkan = string.Empty;
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
            SalinContohCommand = new RelayCommand<BarisContohPanggilan>(SalinContoh);
            SalinJawabanContohCommand = new RelayCommand<BarisContohPanggilan>(SalinJawabanContoh);

            Endpoint = new ObservableCollection<ApiEndpoint>(ApiRute.Semua);
        }

        // ---------- header ----------

        public string HeaderTitle => "API DESA";

        public string HeaderSubtitle =>
            "Buka angka desa, perangkat desa, dan pemeriksaan keaslian surat ke sistem luar. "
            + "Setiap permintaan harus menyertakan kunci API.";

        // ---------- daftar endpoint ----------

        public ObservableCollection<ApiEndpoint> Endpoint { get; }

        // ---------- contoh panggilan (dokumentasi curl) ----------

        /// <summary>
        /// Satu baris contoh pemanggilan per endpoint, selalu disusun ulang dari
        /// <see cref="ApiRute.Semua"/> dan pengaturan alamat saat ini supaya
        /// tidak bisa berbeda dari listener yang sungguh berjalan.
        /// </summary>
        public ObservableCollection<BarisContohPanggilan> ContohPanggilan { get; } = new();

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

        public string IpDiizinkan
        {
            get => _ipDiizinkan;
            set => SetProperty(ref _ipDiizinkan, value);
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
            new OpsiCakupan { Nilai = ApiCakupan.Agregat, Tampilan = "Agregat — baca statistik & status" }
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
        public RelayCommand<BarisContohPanggilan> SalinContohCommand { get; }
        public RelayCommand<BarisContohPanggilan> SalinJawabanContohCommand { get; }

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
                IpDiizinkan = string.Join(", ", ApiPengaturan.IpDiizinkan);
                KunciApi = ApiKunci.Muat() ?? string.Empty;
                MuatKunciCakupan();
                GalatText = _listener.Berjalan ? string.Empty : _listener.GalatTerakhir ?? string.Empty;
                SegarkanStatus();
                SusunContohPanggilan();
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
                ApiPengaturan.SetIpDiizinkan(IpDiizinkan);
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
            SusunContohPanggilan(); // alamat pada contoh ikut port yang baru disimpan

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

        private void SalinContoh(BarisContohPanggilan? baris)
        {
            if (baris == null) return;

            try
            {
                Clipboard.SetText(baris.Curl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin contoh panggilan ke papan klip");
                GalatText = "Gagal menyalin contoh: " + ex.Message;
            }
        }

        /// <summary>
        /// Salin bentuk jawaban (JSON) endpoint. Dipisah dari SalinContoh supaya
        /// integrator bisa langsung menempel skema ke parser miliknya tanpa
        /// harus mengurut ulang dari teks curl.
        /// </summary>
        private void SalinJawabanContoh(BarisContohPanggilan? baris)
        {
            if (baris == null) return;

            try
            {
                Clipboard.SetText(baris.Jawaban);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin bentuk jawaban ke papan klip");
                GalatText = "Gagal menyalin bentuk jawaban: " + ex.Message;
            }
        }

        // ---------- susun contoh panggilan ----------

        /// <summary>
        /// Susun contoh pemanggilan curl per endpoint. Alamat dibaca yang sedang
        /// berlaku (port bawaannya kolom PENGATURAN), sedangkan teks jawaban
        /// dibantu-bentuk dari kebiasaan handler yang betul — supaya dokumentasi
        /// di layar tidak bisa berumur lebih tua dari listener yang berjalan.
        /// </summary>
        private void SusunContohPanggilan()
        {
            try
            {
                var alamat = ApiPengaturan.AlamatPrefix();
                var berhunci = $"curl {alamat} -H \"X-Api-Key: <KUNCI_API>\"";
                ContohPanggilan.Clear();
                foreach (var baris in SusunBarisContoh(alamat, berhunci))
                {
                    ContohPanggilan.Add(baris);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyusun contoh panggilan API");
            }
        }

        /// <summary>Baris-baris contoh: satu per endpoint, urut sesuai tabel endpoint.</summary>
        private static IEnumerable<BarisContohPanggilan> SusunBarisContoh(string alamat, string berhunci)
        {
            yield return new BarisContohPanggilan(
                "ATURAN UMUM",
                "Ganti <KUNCI_API> dengan kunci utama atau kunci cakupan. Semua jawaban "
                + "menyatu dalam amplop yang sama: {\"sukses\":true/false, \"kode\", \"pesan\", \"data\"}. "
                + "Bila gagal, kode galatnya stabil: api_key_kosong (503), api_key_salah (401), "
                + "cakupan_tidak_cukup (403), terlalu_banyak_permintaan (429, lihat header Retry-After), "
                + "endpoint_tidak_ada (404), metode_tidak_diizinkan (405, lihat header Allow), "
                + "permintaan_tidak_sah (400), badan_terlalu_besar (413), gagal_di_server (500), "
                + "kode_tidak_dikenal (404, endpoint verifikasi), sesi_diperlukan (401), "
                + "sesi_gagal (401, nama pengguna / kata sandi salah).",
                berhunci,
                "{\n  \"sukses\": false,\n  \"kode\": \"api_key_salah\",\n  \"pesan\": \"Kunci API salah atau tidak dikirim. Kirim header X-Api-Key.\"\n}");

            yield return new BarisContohPanggilan(
                "Status layanan",
                "Cek paling ringan: versi API dan waktu server — dipakai untuk uji koneksi.",
                $"curl {alamat}/status -H \"X-Api-Key: <KUNCI_API>\"",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"aplikasi\": \"SuDesApp\",\n    \"versi\": \"10.2.0\",\n    \"waktu\": \"2026-10-15 09:34:12\"\n  }\n}");

            yield return new BarisContohPanggilan(
                "Masuk aplikasi (klien HP / aplikasi pendamping)",
                "Tukar nama pengguna & kata sandi akun desktop dengan token sesi 12 jam. "
                + "Token lalu dikirim sebagai Authorization: Bearer <TOKEN> pada endpoint "
                + "bertanda sesi. Sesi hilang saat aplikasi desktop ditutup.",
                $"curl -X POST {alamat}/sesi -H \"X-Api-Key: <KUNCI_API>\" "
                + "-H \"Content-Type: application/json\" "
                + "-d '{\"username\":\"operator\",\"kata_sandi\":\"***\"}'",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"token\": \"…\",\n    \"berlakuSampai\": \"2026-11-01T21:00:00\",\n    \"username\": \"operator\",\n    \"nama\": \"Nama Tampilan\",\n    \"peran\": \"Operator\"\n  }\n}");

            yield return new BarisContohPanggilan(
                "Pengaturan surat (butuh sesi)",
                "Kop & pejabat penanda tangan dari Pengaturan Surat desktop — dipakai aplikasi "
                + "pendamping (HP) supaya hasil cetak sama persis. Baca-saja; sumber "
                + "kebenarannya tetap desktop. Butuh kunci API + token sesi.",
                $"curl {alamat}/pengaturan -H \"X-Api-Key: <KUNCI_API>\" "
                + "-H \"Authorization: Bearer <TOKEN_SESI>\"",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"desa\": {\n      \"namaDesa\": \"…\", \"kecamatan\": \"…\", \"kepalaDesa\": \"…\", \"nipKepalaDesa\": \"…\"\n    },\n    \"diambilPada\": \"2026-11-01T08:00:00\"\n  }\n}");

            yield return new BarisContohPanggilan(
                "Statistik desa",
                "Angka agregat tanpa data pribadi: warga per jenis kelamin/RT, kartu keluarga, "
                + "perangkat desa, dan surat per status.",
                $"curl {alamat}/statistik -H \"X-Api-Key: <KUNCI_API>\"",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"totalWarga\": 3521,\n    \"wargaAktif\": 3214,\n    \"jumlahKartuKeluarga\": 812,\n    \"suratTotal\": 23,\n    ...\n  }\n}");

            yield return new BarisContohPanggilan(
                "Daftar perangkat desa",
                "Nama jabatan, pejabat, wilayah, dan masa jabatan aktif — untuk menyelaraskan kop surat di sistem luar.",
                $"curl {alamat}/perangkat-desa -H \"X-Api-Key: <KUNCI_API>\"",
                "{\n  \"sukses\": true,\n  \"data\": [\n    {\"nama\": \"H. Suryana\", \"jabatan\": \"Kepala Desa\", \"kelompok\": \"INTI\", \"wilayah\": \"\", \"status\": \"Aktif\"}\n  ]\n}");

            yield return new BarisContohPanggilan(
                "Rekap laporan bulanan",
                "12 baris Januari–Desember (bawaan tahun berjalan; parameter ?tahun=YYYY), rinci per status persetujuan.",
                $"curl \"{alamat}/rekap-bulanan?tahun=2026\" -H \"X-Api-Key: <KUNCI_API>\"",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"tahun\": 2026,\n    \"jumlahSuratTotal\": 23,\n    \"bulan\": [\n      {\"bulan\": \"2026-01\", \"jumlahSurat\": 2, \"perStatusPersetujuan\": {\"TERBIT\": 2}},\n      ...\n    ]\n  }\n}");

            yield return new BarisContohPanggilan(
                "Verifikasi surat (tanpa kunci)",
                "Satu-satunya endpoint publik: dipakai bank/instansi yang memegang surat. Jawaban hanya kolom "
                + "yang sudah tercetak di kaki surat — tidak ada NIK, alamat, atau nomor HP.",
                $"curl {alamat}/verifikasi/SD-7K3M-9QX2",
                "{\n  \"sukses\": true,\n  \"data\": {\n    \"kode\": \"SD-7K3M-9QX2\", \"sah\": true,\n    \"nomorSurat\": \"800/123/SKTM\",\n    \"jenisSurat\": \"SKTM\",\n    \"tanggalSurat\": \"2026-10-15T00:00:00\",\n    \"pemohonTersamar\": \"BUDI S.\",\n    \"dataBerubah\": false,\n    \"statusPersetujuan\": \"TERBIT\"\n  }\n}");
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
                // AlamatStatus adalah alamat dasar + prefix (/sudes/api/v1);
                // endpoint yang diuji adalah /status — endpoint paling ringan
                // yang memverifikasi kunci API benar-benar diterima listener.
                // Tanpa segmen /status, listener membalas 404 endpoint_tidak_ada.
                using var permintaan = new HttpRequestMessage(
                    HttpMethod.Get, AlamatStatus + "/status");
                permintaan.Headers.Add("X-Api-Key", KunciApi);

                // ConfigureAwait(false) sengaja TIDAK dipakai di sini: kelanjutan
                // harus kembali ke thread UI karena finally di bawah mengubah
                // SedangUji yang dispar RaiseCanExecuteChanged hingga menyentuh
                // tombol WPF (DependencyProperty) — dari thread latar melempar
                // galat lintas-thread "The calling thread cannot access this object".
                using var balasan = await HttpUji.SendAsync(permintaan);
                var isi = await balasan.Content.ReadAsStringAsync();

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
            // Kotak status dulu selalu kosong karena teksnya tidak pernah diisi.
            // Kini diisi dari keadaan listener yang sebenarnya supaya operator
            // langsung tahu API sedang menyala atau belum; alasan kegagalan
            // tetap tampil di GalatText tepat di bawahnya.
            StatusText = _listener.Berjalan
                ? "API aktif dan melayani permintaan."
                : "API tidak berjalan. Centang Aktifkan API, buat kunci, lalu tekan Simpan.";

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

    /// <summary>Baris contoh pemanggilan (curl + amplop jawaban) di halaman API.</summary>
    public sealed class BarisContohPanggilan
    {
        public BarisContohPanggilan(string judul, string keterangan, string curl, string jawaban)
        {
            Judul = judul;
            Keterangan = keterangan;
            Curl = curl;
            Jawaban = jawaban;
        }

        /// <summary>Nama endpoint / judul baris.</summary>
        public string Judul { get; }

        /// <summary>Keterangan singkat: untuk apa dan bait patokkan pentingnya.</summary>
        public string Keterangan { get; }

        /// <summary>Perintah curl siap salin; <c>&lt;KUNCI_API&gt;</c> diganti kunci sendiri.</summary>
        public string Curl { get; }

        /// <summary>Bentuk amplop jawaban yang khas untuk endpoint ini.</summary>
        public string Jawaban { get; }
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
