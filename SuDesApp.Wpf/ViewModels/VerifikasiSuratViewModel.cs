using Microsoft.Extensions.Logging;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using System.Globalization;
using System.Windows.Input;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman Verifikasi Surat: memasukkan kode yang tercetak di kaki surat
    /// (atau menempelkan seluruh isi QR) lalu menampilkan hasil pemeriksaannya.
    ///
    /// Halaman ini memakai layanan yang sama dengan endpoint
    /// <c>GET /verifikasi/{kode}</c>, sehingga hasil dari dalam aplikasi dan dari
    /// sistem luar tidak mungkin berbeda.
    /// </summary>
    public class VerifikasiSuratViewModel : ObservableObject
    {
        private readonly IVerifikasiSuratService _verifikasi;
        private readonly ILogger<VerifikasiSuratViewModel> _logger;

        private string _kode = string.Empty;
        private bool _sedangMemeriksa;
        private HasilVerifikasiSurat? _hasil;

        public VerifikasiSuratViewModel(
            IVerifikasiSuratService verifikasi,
            ILogger<VerifikasiSuratViewModel>? logger = null)
        {
            _verifikasi = verifikasi ?? throw new ArgumentNullException(nameof(verifikasi));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VerifikasiSuratViewModel>.Instance;

            CekCommand = new AsyncRelayCommand(CekAsync, () => !string.IsNullOrWhiteSpace(Kode) && !SedangMemeriksa);
            BersihkanCommand = new RelayCommand(Bersihkan);
        }

        public string HeaderTitle => "Verifikasi Surat";

        public string HeaderSubtitle =>
            "Periksa keaslian surat dari kode yang tercetak di kaki surat. Kode ini juga bisa dipindai dari gambar QR.";

        /// <summary>Masukan pengguna: kode apa adanya atau seluruh isi QR.</summary>
        public string Kode
        {
            get => _kode;
            set
            {
                SetProperty(ref _kode, value);
                ((AsyncRelayCommand)CekCommand).RaiseCanExecuteChanged();
            }
        }

        public bool SedangMemeriksa
        {
            get => _sedangMemeriksa;
            private set
            {
                SetProperty(ref _sedangMemeriksa, value);
                ((AsyncRelayCommand)CekCommand).RaiseCanExecuteChanged();
            }
        }

        public HasilVerifikasiSurat? Hasil
        {
            get => _hasil;
            private set
            {
                SetProperty(ref _hasil, value);
                OnPropertyChanged(nameof(AdaHasil));
                OnPropertyChanged(nameof(Sah));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PesanHasil));
                OnPropertyChanged(nameof(KodeTampil));
                OnPropertyChanged(nameof(NomorSuratTampil));
                OnPropertyChanged(nameof(JenisSuratTampil));
                OnPropertyChanged(nameof(TanggalSuratTampil));
                OnPropertyChanged(nameof(PemohonTampil));
                OnPropertyChanged(nameof(AdaDataSurat));
            }
        }

        public bool AdaHasil => _hasil != null;

        public bool Sah => _hasil?.Sah == true;

        public string StatusText => _hasil == null
            ? string.Empty
            : _hasil.Sah ? "SAH" : (_hasil.Ditemukan ? "PERLU DIPERIKSA" : "TIDAK TERDAFTAR");

        public string PesanHasil => _hasil?.Pesan ?? string.Empty;

        public string KodeTampil => _hasil?.Kode ?? string.Empty;

        public string NomorSuratTampil => _hasil?.NomorSurat ?? "-";

        public string JenisSuratTampil => string.IsNullOrWhiteSpace(_hasil?.NamaJenis) ? "-" : _hasil!.NamaJenis!;

        public string TanggalSuratTampil => _hasil?.TanggalSurat.HasValue == true
            ? _hasil!.TanggalSurat!.Value.ToString("dd MMMM yyyy", new CultureInfo("id-ID"))
            : "-";

        public string PemohonTampil => string.IsNullOrWhiteSpace(_hasil?.NamaPemohon) ? "-" : _hasil!.NamaPemohon!;

        /// <summary>Detail surat hanya berguna saat kodenya memang terdaftar.</summary>
        public bool AdaDataSurat => _hasil?.Ditemukan == true;

        public ICommand CekCommand { get; }

        public ICommand BersihkanCommand { get; }

        private async Task CekAsync()
        {
            if (string.IsNullOrWhiteSpace(Kode)) return;

            SedangMemeriksa = true;
            try
            {
                Hasil = await _verifikasi.VerifikasiAsync(Kode.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa kode verifikasi surat");
                Hasil = null;
                _pesanGalat = "Gagal memeriksa kode: " + ex.Message;
                OnPropertyChanged(nameof(PesanGalat));
                OnPropertyChanged(nameof(AdaGalat));
            }
            finally
            {
                SedangMemeriksa = false;
            }
        }

        private string _pesanGalat = string.Empty;

        /// <summary>Galat teknis (mis. database terkunci) — dipisah dari hasil verifikasi.</summary>
        public string PesanGalat => _pesanGalat;

        public bool AdaGalat => !string.IsNullOrEmpty(_pesanGalat);

        private void Bersihkan()
        {
            Kode = string.Empty;
            Hasil = null;
            _pesanGalat = string.Empty;
            OnPropertyChanged(nameof(PesanGalat));
            OnPropertyChanged(nameof(AdaGalat));
        }
    }
}
