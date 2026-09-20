using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Satu blanko NTCR pada daftar pilihan paket (kotak centang di form paket).
    /// </summary>
    public class NtcrPaketBlanko : ObservableObject
    {
        private readonly Action _berubah;
        private bool _terpilih;

        public NtcrPaketBlanko(NtcrBlanko blanko, bool terpilih, Action berubah)
        {
            Blanko = blanko ?? throw new ArgumentNullException(nameof(blanko));
            _terpilih = terpilih;
            _berubah = berubah;
        }

        public NtcrBlanko Blanko { get; }

        public string NamaJenis => Blanko.NamaJenis;

        /// <summary>Kode blanko resmi: "N1" … "N6".</summary>
        public string Kode => Blanko.Kode;

        public string Judul => Blanko.Judul;
        public string Keterangan => Blanko.Keterangan;

        /// <summary>Blanko ikut dibuat & dicetak pada paket ini.</summary>
        public bool Terpilih
        {
            get => _terpilih;
            set
            {
                if (SetProperty(ref _terpilih, value))
                {
                    _berubah?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Alur paket pernikahan: SEKALI isi data satu pasangan, seluruh blanko NTCR yang
    /// dicentang (N1–N6) disimpan sekaligus ke register lalu dicetak menjadi satu
    /// berkas PDF gabungan yang siap diserahkan ke KUA.
    ///
    /// Mewarisi <see cref="NtcrInputViewModel"/>, jadi seluruh blok form NTCR dipakai
    /// ulang (identitas calon suami/istri, orang tua/wali, data KUA, isbat, kematian).
    /// Yang berbeda: blok yang tampil mengikuti blanko yang dicentang dan validasinya
    /// memeriksa seluruh blanko tersebut sekaligus.
    /// </summary>
    public class NtcrPaketViewModel : NtcrInputViewModel
    {
        private readonly NtcrPaketService _paketService;
        private readonly NavigationService _navigation;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly Dictionary<string, NtcrPaketBlanko> _petaBlanko = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _nomorPerBlanko = new(StringComparer.OrdinalIgnoreCase);

        private bool _isBusy;
        private bool _sedangMenyusunPilihan;
        private string _statusText = string.Empty;
        private string _nomorSuratRingkasan = string.Empty;

        public NtcrPaketViewModel(
            ILogger<NtcrPaketViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService,
            NtcrPaketService paketService,
            NavigationService navigation,
            Func<string, string, PdfPreviewViewModel> previewFactory)
            : base(logger, appConfig, unitOfWork, messageService)
        {
            _paketService = paketService ?? throw new ArgumentNullException(nameof(paketService));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));

            // Bawaan mengikuti paket yang paling sering dipakai (N1–N5); N6
            // dicentang bila memang diperlukan (mis. pernikahan/rujuk karena kematian).
            foreach (var blanko in NtcrPaketService.BlankoTersedia)
            {
                bool terpilih = NtcrKatalog.PaketStandar.Contains(blanko.NamaJenis, StringComparer.OrdinalIgnoreCase);
                var item = new NtcrPaketBlanko(blanko, terpilih, OnBlankoBerubah);
                Blanko.Add(item);
                _petaBlanko[blanko.NamaJenis] = item;
            }

            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !IsBusy);
            BatalCommand = new RelayCommand(() => RequestClose?.Invoke());
            PilihSemuaCommand = new RelayCommand(() => SetPilihan(semuaBlanko: true));
            PilihPaketStandarCommand = new RelayCommand(() => SetPilihan(paketStandar: true));
        }

        /// <summary>Diminta saat pengguna menekan Batal; host mengembalikan tampilan ke halaman awal.</summary>
        public event Action? RequestClose;

        public string Title => "Paket NTCR (N1–N6)";

        public string Subtitle =>
            "Sekali isi data satu pasangan, seluruh blanko yang dicentang (N1–N6) disimpan sekaligus dan dicetak dalam satu berkas PDF.";

        /// <summary>Daftar blanko yang dapat disertakan pada paket.</summary>
        public ObservableCollection<NtcrPaketBlanko> Blanko { get; } = new();

        public AsyncRelayCommand SimpanCommand { get; }
        public RelayCommand BatalCommand { get; }
        public RelayCommand PilihSemuaCommand { get; }
        public RelayCommand PilihPaketStandarCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(TidakSibuk));
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool TidakSibuk => !IsBusy;

        /// <summary>Kotak nomor surat diisi sistem (satu nomor per blanko), jadi tidak diedit manual.</summary>
        public override string NomorSuratTooltip =>
            "Nomor surat terisi otomatis oleh sistem. Tiap blanko yang dicentang mendapat nomornya sendiri " +
            "(daftar lengkapnya ada di panel pilihan blanko).";

        /// <summary>Daftar nomor surat otomatis untuk seluruh blanko terpilih, mis. "N1 474.1/001/Ds/2026 · N2 …".</summary>
        public string NomorSuratRingkasan
        {
            get => _nomorSuratRingkasan;
            private set => SetProperty(ref _nomorSuratRingkasan, value);
        }

        /// <summary>Kabar kemajuan proses simpan & cetak paket.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>Ringkasan blanko terpilih, mis. "5 blanko: N1, N2, N3, N4, N5".</summary>
        public string RingkasanPaket
        {
            get
            {
                var kode = Blanko.Where(b => b.Terpilih).Select(b => b.Blanko.Kode).ToList();
                return kode.Count == 0
                    ? "Belum ada blanko yang dipilih."
                    : $"{kode.Count} blanko: {string.Join(", ", kode)}";
            }
        }

        // ===== Blok form mengikuti blanko yang dicentang =====
        public override bool IsN1 => Terpilih(SuratConstants.NTCR_N1);
        public override bool IsN2 => Terpilih(SuratConstants.NTCR_N2);
        public override bool IsN3 => Terpilih(SuratConstants.NTCR_N3);
        public override bool IsN4 => Terpilih(SuratConstants.NTCR_N4);
        public override bool IsN5 => Terpilih(SuratConstants.NTCR_N5);
        public override bool IsN6 => Terpilih(SuratConstants.NTCR_N6);

        private bool Terpilih(string namaJenis) =>
            _petaBlanko.TryGetValue(namaJenis, out var item) && item.Terpilih;

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await base.InitializeAsync(cancellationToken);
            await PerbaruiNomorSuratAsync(cancellationToken);
            StatusText = RingkasanPaket;
        }

        /// <summary>
        /// Alur paket mengisi nomor per blanko lewat PerbaruiNomorSuratAsync (satu
        /// nomor untuk SETIAP blanko terpilih), jadi pengisian otomatis versi form
        /// per blanko tidak dipakai — mencegah nomor jenis pertama dibuat dua kali.
        /// </summary>
        protected override Task IsiNomorSuratOtomatisAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private void SetPilihan(bool semuaBlanko = false, bool paketStandar = false)
        {
            // Nomor surat disegarkan sekali setelah seluruh centang berubah, bukan tiap kotak.
            _sedangMenyusunPilihan = true;
            try
            {
                foreach (var item in Blanko)
                {
                    item.Terpilih = semuaBlanko
                        || (paketStandar && NtcrKatalog.PaketStandar.Contains(item.NamaJenis, StringComparer.OrdinalIgnoreCase));
                }
            }
            finally
            {
                _sedangMenyusunPilihan = false;
            }

            _ = PerbaruiNomorSuratAsync();
        }

        /// <summary>
        /// Isi kotak "Nomor Surat" secara otomatis: nomor berikutnya menurut penomoran
        /// jenis surat di register untuk SETIAP blanko yang dicentang. Nomor blanko
        /// pertama dipakai sebagai isi kotak, sedangkan daftar lengkapnya ditampilkan
        /// sebagai ringkasan karena satu paket menghasilkan beberapa nomor.
        /// </summary>
        public async Task PerbaruiNomorSuratAsync(CancellationToken cancellationToken = default)
        {
            var terpilih = Blanko.Where(b => b.Terpilih).Select(b => b.NamaJenis).ToList();
            if (terpilih.Count == 0)
            {
                NomorSurat = string.Empty;
                NomorSuratEnabled = false;
                NomorSuratRingkasan = "Belum ada blanko dipilih — nomor surat akan diisi otomatis setelah blanko dicentang.";
                return;
            }

            var baris = new List<(string Kode, string Nomor)>(terpilih.Count);
            foreach (var namaJenis in terpilih)
            {
                string nomor = await NomorBerikutnyaAsync(namaJenis, cancellationToken);
                if (!string.IsNullOrWhiteSpace(nomor))
                {
                    baris.Add((NtcrKatalog.Kode(namaJenis), nomor));
                }
            }

            // Satu paket memuat beberapa nomor (satu per blanko): blanko pertama
            // mengisi kotak nomor surat, seluruhnya diringkas di panel pilihan.
            NomorSurat = baris.Count > 0 ? baris[0].Nomor : string.Empty;
            NomorSuratEnabled = false;
            NomorSuratRingkasan = baris.Count == 0
                ? "Nomor surat akan dibuat otomatis saat paket disimpan."
                : "Nomor otomatis per blanko: " + string.Join("  ·  ", baris.Select(b => $"{b.Kode} {b.Nomor}"));
        }

        /// <summary>Nomor surat berikutnya untuk satu jenis blanko (di-cache selama halaman terbuka).</summary>
        private async Task<string> NomorBerikutnyaAsync(string namaJenis, CancellationToken cancellationToken)
        {
            if (_nomorPerBlanko.TryGetValue(namaJenis, out var tersimpan))
                return tersimpan;

            try
            {
                var jenis = await _unitOfWork.JenisSuratRepository.GetJenisSuratByNamaAsync(namaJenis);
                if (jenis == null) return string.Empty;

                string nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(jenis.KodeJenis!);
                _nomorPerBlanko[namaJenis] = nomor ?? string.Empty;
                return nomor ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyiapkan nomor surat otomatis untuk {NamaJenis}", namaJenis);
                return string.Empty;
            }
        }

        /// <summary>Perbarui blok form, validasi, dan ringkasan setiap centang berubah.</summary>
        private void OnBlankoBerubah()
        {
            OnPropertyChanged(nameof(IsN1));
            OnPropertyChanged(nameof(IsN2));
            OnPropertyChanged(nameof(IsN3));
            OnPropertyChanged(nameof(IsN4));
            OnPropertyChanged(nameof(IsN5));
            OnPropertyChanged(nameof(IsN6));
            OnPropertyChanged(nameof(ButuhDataOrangTua));
            OnPropertyChanged(nameof(ButuhPejabatDesa));
            OnPropertyChanged(nameof(IsSuratPermohonanKua));
            OnPropertyChanged(nameof(RingkasanPaket));

            StatusText = RingkasanPaket;

            if (!_sedangMenyusunPilihan)
            {
                _ = PerbaruiNomorSuratAsync();
            }
        }

        /// <summary>
        /// Simpan semua blanko terpilih dari satu data pasangan, lalu buka berkas PDF
        /// gabungan di pratinjau.
        /// </summary>
        private async Task SimpanAsync()
        {
            if (IsBusy) return;

            var jenis = Blanko.Where(b => b.Terpilih)
                .Select(b => b.NamaJenis)
                .ToList();

            if (jenis.Count == 0)
            {
                await _messageService.ShowWarningAsync("Pilih minimal satu blanko yang akan dibuat pada paket ini.");
                return;
            }

            string daftarKode = string.Join(", ", jenis.Select(NtcrKatalog.Kode));
            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Simpan & cetak paket NTCR",
                $"Buat {jenis.Count} surat sekaligus ({daftarKode}) dari data pasangan ini, " +
                "lalu cetak seluruh blanko tersebut dalam satu berkas PDF?");
            if (!lanjut) return;

            IsBusy = true;
            try
            {
                StatusText = "Memeriksa isian formulir…";

                var master = new SuratData(
                    _unitOfWork.SuratRepository,
                    _unitOfWork.WargaRepository,
                    _unitOfWork.DesaRepository,
                    _unitOfWork.JenisSuratRepository);

                // Validasi form mengikuti blanko yang dicentang (IsN1..IsN6).
                await CollectDataAsync(master);
                master.Keterangan = GetKeteranganTextBox();

                StatusText = "Menyimpan surat & membuat berkas gabungan…";
                var hasil = await _paketService.SimpanDanCetakAsync(master, jenis);

                StatusText = $"Selesai — {hasil.JumlahBlanko} blanko ({hasil.DaftarBlanko}) dalam satu berkas.";
                _logger.LogInformation("Paket NTCR {Blanko} selesai: {Berkas}", hasil.DaftarBlanko, hasil.BerkasPdf);

                _navigation.Navigate(_previewFactory($"Paket NTCR ({hasil.DaftarBlanko})", hasil.BerkasPdf));

                await _messageService.ShowInfoAsync(
                    $"Paket NTCR berhasil disimpan & dicetak.\n\n" +
                    $"Blanko: {hasil.DaftarBlanko}\nNomor surat: {hasil.DaftarNomor}\n\n" +
                    "Berkas gabungan siap cetak sudah dibuka di pratinjau.");
            }
            catch (ValidationException ex)
            {
                // Validasi form sudah menampilkan rincian kesalahan lewat ValidateInput;
                // pesan dari layanan (mis. belum ada blanko dipilih) ditampilkan di sini.
                StatusText = "Paket belum dapat dibuat — periksa isian formulir.";
                if (!string.Equals(ex.Message, "Validasi input gagal", StringComparison.OrdinalIgnoreCase))
                {
                    await _messageService.ShowWarningAsync(ex.Message);
                }
            }
            catch (Exception ex)
            {
                StatusText = "Gagal membuat paket.";
                _logger.LogError(ex, "Gagal menyimpan paket NTCR");
                await _messageService.ShowErrorAsync("Gagal membuat paket NTCR: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
