using System.Threading.Tasks;
using System.Windows.Input;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Pratinjau PDF generik (SudesPdf) dengan tombol Cetak/Ekspor/Batal —
    /// digunakan oleh pratinjau formulir, hasil input surat, agenda, dan register.
    /// Untuk pratinjau hasil Buat/Edit Surat, tombol ✏️ Edit ditampilkan agar
    /// pengguna bisa langsung memperbaiki data tanpa mencari surat di Register.
    /// </summary>
    public class PdfPreviewViewModel : ObservableObject
    {
        private readonly NavigationService _navigation;
        private readonly Func<int, InputWindowViewModel>? _editViewModelFactory;
        private readonly IMessageService? _messageService;
        private readonly IPeringatanDataDesaContoh? _peringatan;
        private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;
        private readonly int? _suratId;
        private bool _dokumenTermuat = true;
        private bool _tampilPeringatanDataContoh;
        private string _pesanPeringatanDataContoh = string.Empty;

        public string Title { get; }
        public string? PdfPath { get; private set; }
        public bool IsMissing { get; }

        /// <summary>
        /// True bila dokumen ada di disk. Gerbang tampilan yang sebenarnya adalah
        /// <see cref="BolehEkspor"/> — properti ini hanya komponen "PDF-nya ada".
        /// </summary>
        public bool CanExport => !string.IsNullOrEmpty(PdfPath);

        /// <summary>
        /// True bila dokumen ini adalah surat buatan aplikasi (alur Buat Surat):
        /// tombol ✏️ Edit ditampilkan dan membuka form edit surat terkait.
        /// </summary>
        public bool CanEdit => _suratId.HasValue && _editViewModelFactory != null;

        public ICommand BatalCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand EditCommand { get; }

        /// <summary>Tombol strip peringatan: buka Pengaturan Surat bagian Data Desa.</summary>
        public ICommand BukaPengaturanSuratCommand { get; }

        /// <param name="batalKembali">
        /// Aksi tombol Batal. Kosong = kembali ke tampilan awal (bawaan). Dipakai
        /// pratinjau di dalam wizard yang perlu kembali ke langkah sebelumnya,
        /// bukan menutup halaman utama.
        /// </param>
        /// <param name="peringatan">
        /// Penjaga data desa contoh: sebelum mencetak, pengguna diberi tahu bila kop
        /// masih memakai data contoh. Kosong = dokumen ini tidak diperiksa.
        /// </param>
        public PdfPreviewViewModel(string title, string? pdfPath, NavigationService navigation,
            int? suratId = null, Func<int, InputWindowViewModel>? editViewModelFactory = null,
            IMessageService? messageService = null, Action? batalKembali = null,
            IPeringatanDataDesaContoh? peringatan = null,
            SuDesApp.Services.PenjagaLisensi? penjaga = null)
        {
            Title = title;
            PdfPath = pdfPath;
            IsMissing = string.IsNullOrEmpty(pdfPath);
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _suratId = suratId;
            _editViewModelFactory = editViewModelFactory;
            _messageService = messageService;
            _peringatan = peringatan;
            _penjaga = penjaga;
            BatalCommand = new RelayCommand(batalKembali ?? (() => _navigation.ShowDefault()));
            ExportCommand = new RelayCommand(ExportAs);
            EditCommand = new AsyncRelayCommand(EditSuratAsync, () => CanEdit);
            BukaPengaturanSuratCommand = new RelayCommand(
                () => _navigation.ShowSetelanBagianSurat(0));

            // Tombol Cetak dan Ekspor harus ikut hidup/mati bersama lisensi tanpa
            // menunggu halaman dibuka ulang: penjaganya singleton, halamannya
            // transient.
            if (_penjaga is not null) LanggananLisensiLemah.Daftar(this, _penjaga);

            _ = PeriksaPeringatanDataContohAsync();
        }

        // =================================================================
        // Peringatan data desa contoh sebelum mencetak
        // =================================================================

        /// <summary>Benar bila dokumen ini dibuat dari data desa yang masih contoh.</summary>
        public bool TampilPeringatanDataContoh
        {
            get => _tampilPeringatanDataContoh;
            private set => SetProperty(ref _tampilPeringatanDataContoh, value);
        }

        /// <summary>Pesan strip peringatan pada pratinjau (kosong bila data desa sudah diisi).</summary>
        public string PesanPeringatanDataContoh
        {
            get => _pesanPeringatanDataContoh;
            private set => SetProperty(ref _pesanPeringatanDataContoh, value ?? string.Empty);
        }

        /// <summary>Periksa data desa contoh untuk strip peringatan di halaman pratinjau.</summary>
        public async Task PeriksaPeringatanDataContohAsync()
        {
            if (_peringatan == null)
            {
                return;
            }

            try
            {
                var keadaan = await _peringatan.PeriksaAsync();
                TampilPeringatanDataContoh = keadaan.MasihContoh;
                PesanPeringatanDataContoh = keadaan.MasihContoh
                    ? $"Dokumen ini memakai data desa contoh: {keadaan.RingkasField}. Cetak setelah datanya diganti di Pengaturan Surat → Data Desa."
                    : string.Empty;
            }
            catch
            {
                // Strip peringatan hanya kabar tambahan: kegagalan membacanya tidak
                // boleh mengganggu pratinjau. Penjaga sebelum mencetak tetap berlaku.
            }
        }

        /// <summary>
        /// Penjaga tombol Cetak: dokumen bersurat tidak boleh tercetak memakai nama
        /// desa/pejabat contoh tanpa persetujuan pengguna. True = boleh mencetak.
        ///
        /// ATURAN CETAK PRATINJAU: tombol Cetak ada di SEMUA pratinjau dan boleh
        /// dipakai selama lisensi masih berlaku (trial berjalan atau berlangganan);
        /// yang menguncinya hanya lisensi yang habis/belum aktif. Cek di sini tetap
        /// dipasang walaupun tombol sudah mati oleh <see cref="BolehCetak"/> —
        /// keadaan lisensi bisa berubah di antara penggambaran tombol dan klik
        /// (detak lisensi, aktivasi dari halaman Langganan), jadi kunci tidak
        /// pernah diloloskan hanya karena tampilan yang belum sempat segar.
        /// </summary>
        public async Task<bool> BolehCetakAsync()
        {
            // Trial habis / langganan berakhir: cetak PDF dikunci, KECUALI
            // Daftar Hadir yang tetap boleh. Pratinjau tetap boleh dibuka.
            if (!BolehCetakPdfDokumenIni)
            {
                if (_messageService != null)
                {
                    await _messageService.ShowInfoAsync(PesanCetakDikunci);
                }
                return false;
            }

            if (_peringatan == null)
            {
                return true;
            }

            return await _peringatan.BolehLanjutAsync(
                $"Dokumen \"{Title}\" akan dicetak.", _messageService!);
        }

        /// <summary>
        /// Tombol Cetak dapat dipakai? TRUE = tombol tampil dan hidup.
        ///
        /// Yang boleh menguncinya HANYA lisensi: selama trial berjalan atau
        /// langganan aktif, tombol ini ada di semua pratinjau — termasuk jenis
        /// surat yang bukan Kematian (aturan lama yang menyembunyikan tombol
        /// kecuali Kematian/Daftar Hadir sudah dicabut karena membuat pengguna
        /// berlisensi mengira cetak dihapus). Daftar Hadir tetap boleh dicetak
        /// walau trial habis (pengecualian produk di <see cref="PenjagaLisensi"/>),
        /// dan PDF yang gagal dimuat tidak bisa dicetak (dilaporkan view lewat
        /// <see cref="TerapkanKesiapanDokumen"/>).
        /// </summary>
        public bool BolehCetak =>
            _dokumenTermuat && BolehCetakPdfDokumenIni;

        /// <summary>
        /// Penjelasan di tooltip ketika tombol Cetak mati, supaya "Cetak" yang
        /// abu-abu tidak terbaca sebagai fitur yang hilang (gejala lama: lenyap).
        /// </summary>
        public string TooltipCetak =>
            BolehCetakPdfDokumenIni
                ? "Cetak dokumen ini ke printer"
                : PesanCetakDikunci;

        /// <summary>
        /// Tombol Ekspor dapat dipakai? Aturannya SAMA dengan <see cref="BolehCetak"/>:
        /// tombol ada di semua pratinjau selama lisensi berlaku (trial berjalan atau
        /// berlangganan), mati hanya saat trial habis / langganan berakhir / trial
        /// belum dimulai, dan PDF-nya harus benar-benar ada.
        /// </summary>
        public bool BolehEkspor =>
            _dokumenTermuat && CanExport && BolehCetakPdfDokumenIni;

        /// <summary>
        /// Tooltip tombol Ekspor: menjelaskan fungsinya saat hidup, dan sebab
        /// kuncinya saat tombol mati.
        /// </summary>
        public string TooltipEkspor =>
            BolehCetakPdfDokumenIni
                ? "Simpan salinan PDF ini ke lokasi pilihan Anda"
                : PesanCetakDikunci;

        /// <summary>
        /// Gerbang lisensi untuk dokumen ini, dibaca dari
        /// <see cref="PenjagaLisensi.BolehCetakPdf"/> supaya Cetak, Ekspor, dan kedua
        /// tooltip memakai SATU aturan — termasuk pengecualian Daftar Hadir yang
        /// tetap boleh walau trial habis.
        /// </summary>
        private bool BolehCetakPdfDokumenIni =>
            _penjaga is null || _penjaga.BolehCetakPdf(Title);

        private string PesanCetakDikunci => SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci;

        /// <summary>
        /// View melaporkan hasil muat PDF. Dulu view menyetel
        /// <c>PrintBtn.IsEnabled</c>/<c>ExportBtn.IsEnabled</c> langsung; nilai lokal
        /// itu menimpa binding <see cref="BolehCetak"/> dan <see cref="BolehEkspor"/>.
        /// Sekarang keduanya digabung di sini.
        /// </summary>
        public void TerapkanKesiapanDokumen(bool termuat)
        {
            if (!SetProperty(ref _dokumenTermuat, termuat)) return;
            OnPropertyChanged(nameof(BolehCetak));
            OnPropertyChanged(nameof(BolehEkspor));
            OnPropertyChanged(nameof(TooltipCetak));
            OnPropertyChanged(nameof(TooltipEkspor));
        }

        /// <summary>
        /// Dipanggil langganan lemah saat keadaan lisensi berubah.
        /// Tidak didiapkan ke dispatcher: yang naik hanya notifikasi properti
        /// skalar (Binding WPF sudah merangkai sendiri ke thread UI), sedangkan
        /// BeginInvoke membuat perubahan tertahan bila dispatcher sedang tidak
        /// berputar — tombolnya tetap mati tanpa sebab yang terlihat.
        /// </summary>
        internal void KeadaanLisensiBerubah()
        {
            OnPropertyChanged(nameof(BolehCetak));
            OnPropertyChanged(nameof(TooltipCetak));
            OnPropertyChanged(nameof(BolehEkspor));
            OnPropertyChanged(nameof(TooltipEkspor));
        }

        /// <summary>
        /// Simpan salinan PDF yang sedang dilihat ke lokasi pilihan pengguna.
        /// Padanan tombol "Export PDF" pada pratinjau dokumen.
        /// </summary>
        private void ExportAs()
        {
            try
            {
                // Ekspor PDF dikunci bersama cetak: aturan yang sama dibaca dari
                // BolehCetakPdfDokumenIni. ExportAs sinkron — pesan fire-and-forget.
                if (!BolehCetakPdfDokumenIni)
                {
                    if (_messageService != null)
                    {
                        _ = _messageService.ShowInfoAsync(PesanCetakDikunci);
                    }
                    return;
                }

                if (string.IsNullOrEmpty(PdfPath) || !System.IO.File.Exists(PdfPath))
                {
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Ekspor PDF",
                    Filter = "Dokumen PDF (*.pdf)|*.pdf",
                    FileName = BuildDefaultFileName(),
                    DefaultExt = ".pdf"
                };
                if (dialog.ShowDialog() != true) return;

                System.IO.File.Copy(PdfPath, dialog.FileName, overwrite: true);
            }
            catch (Exception)
            {
                // Gagal ekspor (mis. file terkunci); biarkan pengguna mencoba lagi.
            }
        }

        /// <summary>
        /// Buka form edit untuk surat yang sedang dipratinjau (hanya alur Buat Surat)
        /// di content host utama (bukan modal).
        /// </summary>
        private async Task EditSuratAsync()
        {
            if (!CanEdit || _editViewModelFactory == null || _suratId == null) return;
            try
            {
                var vm = _editViewModelFactory(_suratId.Value);
                // Form edit ditampilkan di content host utama (bukan modal). Bila
                // dibatalkan, kembali ke halaman kosong (default). Setelah tersimpan,
                // InputWindowViewModel sudah menavigasi ke pratinjau PDF yang baru.
                vm.RequestClose += () => _navigation.ShowDefault();
                _navigation.Navigate(vm);
                await vm.ConfigureForEditAsync(_suratId.Value);
            }
            catch (Exception ex)
            {
                _navigation.ShowDefault();
                if (_messageService != null)
                {
                    _ = _messageService.ShowErrorAsync($"Gagal membuka form edit: {ex.Message}");
                }
            }
        }

        private string BuildDefaultFileName()
        {
            // Prioritaskan judul (mis. "Surat SKD_UMUM — 470/.../2026"), fallback nama file temp.
            var raw = !string.IsNullOrWhiteSpace(Title) ? Title : System.IO.Path.GetFileNameWithoutExtension(PdfPath) ?? string.Empty;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var clean = new string(raw.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Dokumen.pdf" : clean + ".pdf";
        }
    }

    /// <summary>
    /// Langganan lemah halaman pratinjau ke <see cref="PenjagaLisensi.Berubah"/>.
    ///
    /// Kenapa perlu sama sekali: tombol Cetak kini dikendalikan keadaan lisensi.
    /// Tanpa notifikasi, halaman yang sudah terbuka saat pengguna mengaktifkan
    /// lisensi (detak lisensi / halaman Langganan) tetap menampilkan tombol mati
    /// — persis gejala "sudah berlisensi tapi tidak bisa cetak" yang dilaporkan.
    /// Kenapa langganan LEMAH: PdfPreviewViewModel transient (satu per dokumen
    /// yang dibuka), penjaganya singleton — langganan kuat menahan setiap
    /// pratinjau yang pernah dibuka sampai aplikasi ditutup. Pola ini meniru
    /// LanggananSambunganLemah untuk SambunganServerLisensi.
    /// </summary>
    internal sealed class LanggananLisensiLemah
    {
        private readonly WeakReference<PdfPreviewViewModel> _halaman;
        private readonly PenjagaLisensi _sumber;

        private LanggananLisensiLemah(PdfPreviewViewModel halaman, PenjagaLisensi sumber)
        {
            _halaman = new WeakReference<PdfPreviewViewModel>(halaman);
            _sumber = sumber;
            _sumber.Berubah += Tangani;
        }

        /// <summary>Daftarkan langganan lemah; umurnya mengikuti halaman.</summary>
        public static void Daftar(PdfPreviewViewModel halaman, PenjagaLisensi sumber) =>
            _ = new LanggananLisensiLemah(halaman, sumber);

        private void Tangani()
        {
            if (_halaman.TryGetTarget(out var halaman))
            {
                halaman.KeadaanLisensiBerubah();
                return;
            }

            // Halaman sudah dibuang: jangan biarkan singleton menahan apa pun.
            _sumber.Berubah -= Tangani;
        }
    }
}
