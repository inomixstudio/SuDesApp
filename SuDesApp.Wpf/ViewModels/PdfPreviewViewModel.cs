using System.Threading.Tasks;
using System.Windows.Input;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Pratinjau PDF generik (PDFium) dengan tombol Cetak/Ekspor/Batal —
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
        private readonly int? _suratId;
        private bool _tampilPeringatanDataContoh;
        private string _pesanPeringatanDataContoh = string.Empty;

        public string Title { get; }
        public string? PdfPath { get; private set; }
        public bool IsMissing { get; }

        /// <summary>True bila tombol Ekspor ditampilkan (dokumen ada di disk).</summary>
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
            IPeringatanDataDesaContoh? peringatan = null)
        {
            Title = title;
            PdfPath = pdfPath;
            IsMissing = string.IsNullOrEmpty(pdfPath);
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _suratId = suratId;
            _editViewModelFactory = editViewModelFactory;
            _messageService = messageService;
            _peringatan = peringatan;
            BatalCommand = new RelayCommand(batalKembali ?? (() => _navigation.ShowDefault()));
            ExportCommand = new RelayCommand(ExportAs);
            EditCommand = new RelayCommand(EditSurat, () => CanEdit);
            BukaPengaturanSuratCommand = new RelayCommand(
                () => _navigation.ShowSetelanBagianSurat(0));

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
        /// </summary>
        public async Task<bool> BolehCetakAsync()
        {
            if (_peringatan == null)
            {
                return true;
            }

            return await _peringatan.BolehLanjutAsync(
                $"Dokumen \"{Title}\" akan dicetak.", _messageService!);
        }

        /// <summary>
        /// Simpan salinan PDF yang sedang dilihat ke lokasi pilihan pengguna.
        /// Padanan tombol "Export PDF" pada pratinjau dokumen.
        /// </summary>
        private void ExportAs()
        {
            try
            {
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
        private async void EditSurat()
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
}
