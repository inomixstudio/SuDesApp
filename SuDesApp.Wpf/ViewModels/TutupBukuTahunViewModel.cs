using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman tutup buku tahunan: memeriksa keutuhan penomoran satu tahun,
    /// menutup atau membukanya kembali, dan mencetak arsip register.
    ///
    /// Menutup buku tidak menghapus apa pun dan selalu bisa dibalik, jadi
    /// halaman ini sengaja tidak punya tombol hapus.
    /// </summary>
    public class TutupBukuTahunViewModel : ObservableObject
    {
        private readonly IVerifikasiPenomoranService _verifikasi;
        private readonly ITutupBukuTahunService _tutupBuku;
        private readonly IArsipRegisterTahunanService _arsip;
        private readonly IMessageService _messageService;
        private readonly ILogger<TutupBukuTahunViewModel> _logger;

        private bool _menyelaraskanTahun;
        private int _tahunTerpilih = DateTime.Now.Year;
        private TutupBukuTahun _statusBuku = new();
        private HasilVerifikasiNomor? _verifikasiHasil;
        private bool _isLoading;
        private string _catatan = string.Empty;
        private bool _paksa;
        private string _ringkasan = string.Empty;
        private string _pesanTolak = string.Empty;

        public TutupBukuTahunViewModel(
            IVerifikasiPenomoranService verifikasi,
            ITutupBukuTahunService tutupBuku,
            IArsipRegisterTahunanService arsip,
            IMessageService messageService,
            ILogger<TutupBukuTahunViewModel> logger)
        {
            _verifikasi = verifikasi;
            _tutupBuku = tutupBuku;
            _arsip = arsip;
            _messageService = messageService;
            _logger = logger;

            MuatCommand = new AsyncRelayCommand(MuatAsync);
            TutupCommand = new AsyncRelayCommand(TutupAsync);
            BukaCommand = new AsyncRelayCommand(BukaAsync);
            CetakExcelCommand = new AsyncRelayCommand(() => CetakAsync("xlsx"));
            CetakPdfCommand = new AsyncRelayCommand(() => CetakAsync("pdf"));

            _ = MuatAsync();
        }

        // ---------- status ----------

        public ObservableCollection<DeretNomor> Deret { get; } = new();
        public ObservableCollection<string> DaftarCatatan { get; } = new();

        /// <summary>Tahun yang punya surat, terbaru lebih dulu.</summary>
        public ObservableCollection<int> TahunTersedia { get; } = new();

        public int TahunTerpilih
        {
            get => _tahunTerpilih;
            set
            {
                if (_tahunTerpilih == value) return;
                _tahunTerpilih = value;
                OnPropertyChanged();

                // MuatAsync() sendiri bisa mengoreksi tahun pilihan; tanpa penanda ini
                // perubahan tahun akan memicu MuatAsync kedua yang tumpang tindih.
                if (!_menyelaraskanTahun) _ = MuatAsync();
            }
        }

        public TutupBukuTahun StatusBuku
        {
            get => _statusBuku;
            private set
            {
                _statusBuku = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusTertutup));
                OnPropertyChanged(nameof(LabelStatus));
                OnPropertyChanged(nameof(BisaTutup));
                OnPropertyChanged(nameof(BisaBuka));
                TutupCommand.RaiseCanExecuteChanged();
                BukaCommand.RaiseCanExecuteChanged();
            }
        }

        public bool StatusTertutup => StatusBuku.Tertutup;

        public string LabelStatus => StatusBuku.Tertutup
            ? $"TERTUTUP {StatusBuku.TanggalTutup}"
            : "TERBUKA";

        public bool BisaTutup => !IsLoading && !StatusBuku.Tertutup;
        public bool BisaBuka => !IsLoading && StatusBuku.Tertutup;

        public HasilVerifikasiNomor? HasilVerifikasi
        {
            get => _verifikasiHasil;
            private set
            {
                _verifikasiHasil = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AdaMasalahBlocking));
                OnPropertyChanged(nameof(JumlahSurat));
                TutupCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Masalah yang harus diperbaiki atau dipaksa saat menutup buku.</summary>
        public bool AdaMasalahBlocking => HasilVerifikasi?.AdaMasalahBlocking ?? false;

        public int JumlahSurat => HasilVerifikasi?.JumlahSeluruhSurat ?? 0;

        public string Ringkasan
        {
            get => _ringkasan;
            private set { _ringkasan = value; OnPropertyChanged(); }
        }

        public string PesanTolak
        {
            get => _pesanTolak;
            private set
            {
                _pesanTolak = value;
                OnPropertyChanged(nameof(AdaPesanTolak));
            }
        }

        public bool AdaPesanTolak => !string.IsNullOrWhiteSpace(PesanTolak);

        public string CatatanTutup
        {
            get => _catatan;
            set { _catatan = value; OnPropertyChanged(); }
        }

        /// <summary>Menutup dengan paksa tetap harus dicatat alasannya.</summary>
        public bool Paksa
        {
            get => _paksa;
            set { _paksa = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BukanLoading));
                OnPropertyChanged(nameof(BisaTutup));
                OnPropertyChanged(nameof(BisaBuka));
            }
        }

        public bool BukanLoading => !IsLoading;

        /// <summary>True saat penomoran rapi dan tidak ada catatan yang perlu dibaca.</summary>
        public bool TampilkanTanpaCatatan => DaftarCatatan.Count == 0;

        // ---------- perintah ----------

        public AsyncRelayCommand MuatCommand { get; }
        public AsyncRelayCommand TutupCommand { get; }
        public AsyncRelayCommand BukaCommand { get; }
        public AsyncRelayCommand CetakExcelCommand { get; }
        public AsyncRelayCommand CetakPdfCommand { get; }

        // ---------- aksi ----------

        private async Task MuatAsync()
        {
            try
            {
                IsLoading = true;
                PesanTolak = string.Empty;

                TahunTersedia.Clear();
                foreach (int tahun in await _verifikasi.AmbilTahunTersediaAsync())
                {
                    TahunTersedia.Add(tahun);
                }

                // Koreksi tahun terpilih dilakukan tanpa memicu muat ulang kedua.
                _menyelaraskanTahun = true;
                try
                {
                    if (TahunTersedia.Count == 0)
                    {
                        // Database masih kosong: tahun berjalan tetap bisa dipilih supaya
                        // operator bisa melihat status bukunya (terbuka, nol surat).
                        TahunTersedia.Add(DateTime.Now.Year);
                    }
                    else if (!TahunTersedia.Contains(TahunTerpilih))
                    {
                        TahunTerpilih = TahunTersedia[0];
                    }
                }
                finally
                {
                    _menyelaraskanTahun = false;
                }

                StatusBuku = await _tutupBuku.AmbilStatusAsync(TahunTerpilih);
                HasilVerifikasi = await _verifikasi.PeriksaAsync(TahunTerpilih);

                Deret.Clear();
                foreach (var d in HasilVerifikasi.Deret) Deret.Add(d);

                DaftarCatatan.Clear();
                foreach (string ket in HasilVerifikasi.Peringatan()) DaftarCatatan.Add(ket);
                foreach (string ket in HasilVerifikasi.NomorTahunTidakCocok) DaftarCatatan.Add(ket);
                OnPropertyChanged(nameof(TampilkanTanpaCatatan));

                Ringkasan = HasilVerifikasi.Ringkasan;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data tutup buku tahun {Tahun}", TahunTerpilih);
                await _messageService.ShowErrorAsync("Gagal memuat data tahun " + TahunTerpilih + ": " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task TutupAsync()
        {
            if (string.IsNullOrWhiteSpace(CatatanTutup) && Paksa)
            {
                await _messageService.ShowWarningAsync(
                    "Menutup dengan paksa wajib disertai alasan. Tuliskan alasan pada kolom catatan " +
                    "supaya auditor bisa tahu kenapa buku ditutup meski ada penyimpangan.");
                return;
            }

            if (Paksa)
            {
                bool setuju = await _messageService.ShowConfirmationAsync(
                    "Tutup buku dengan paksa?",
                    "Penomoran tahun " + TahunTerpilih
                        + " bermasalah. Buku tetap akan ditutup dan alasannya dicatat.");
                if (!setuju) return;
            }

            try
            {
                IsLoading = true;
                var hasil = await _tutupBuku.TutupAsync(TahunTerpilih, CatatanTutup, "Operator", Paksa);

                if (!hasil.Berhasil)
                {
                    PesanTolak = hasil.Pesan ?? "Buku tidak dapat ditutup.";
                    return;
                }

                await _messageService.ShowMessageAsync(
                    hasil.Data!.JumlahSurat + " surat, " + hasil.Data.JumlahDeret
                        + " deret. Buku masih bisa dibuka kembali.",
                    "Buku tahun " + TahunTerpilih + " ditutup",
                    AppMessageButton.Ok, AppMessageIcon.Info);

                Paksa = false;
                await MuatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menutup buku tahun {Tahun}", TahunTerpilih);
                await _messageService.ShowErrorAsync("Gagal menutup buku: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task BukaAsync()
        {
            bool setuju = await _messageService.ShowConfirmationAsync(
                "Buka kembali buku tahun " + TahunTerpilih + "?",
                "Tidak ada data yang dihapus. Status kembali terbuka dan nomor surat bisa terbit lagi.");
            if (!setuju) return;

            try
            {
                IsLoading = true;
                var hasil = await _tutupBuku.BukaAsync(TahunTerpilih, CatatanTutup, "Operator");

                if (!hasil.Berhasil)
                {
                    PesanTolak = hasil.Pesan ?? "Buku tidak dapat dibuka.";
                    return;
                }

                await _messageService.ShowMessageAsync(
                    "Statusnya sekarang terbuka.",
                    "Buku tahun " + TahunTerpilih + " dibuka kembali",
                    AppMessageButton.Ok, AppMessageIcon.Info);

                await MuatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka buku tahun {Tahun}", TahunTerpilih);
                await _messageService.ShowErrorAsync("Gagal membuka buku: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CetakAsync(string ekstensi)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Simpan Arsip Register " + TahunTerpilih,
                Filter = ekstensi == "pdf"
                    ? "PDF (*.pdf)|*.pdf"
                    : "Microsoft Excel (*.xlsx)|*.xlsx",
                FileName = _arsip.NamaBerkas(TahunTerpilih, ekstensi),
                DefaultExt = "." + ekstensi
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsLoading = true;

                byte[] isi = ekstensi == "pdf"
                    ? await _arsip.BuatPdfAsync(TahunTerpilih)
                    : await _arsip.BuatExcelAsync(TahunTerpilih);

                await File.WriteAllBytesAsync(dialog.FileName, isi);
                _logger.LogInformation("Arsip register {Tahun} disimpan ke {Jalur}", TahunTerpilih, dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan arsip register {Tahun}", TahunTerpilih);
                await _messageService.ShowErrorAsync("Gagal menyimpan arsip: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
