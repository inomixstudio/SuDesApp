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
        private readonly int? _suratId;

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

        /// <param name="batalKembali">
        /// Aksi tombol Batal. Kosong = kembali ke tampilan awal (bawaan). Dipakai
        /// pratinjau di dalam wizard yang perlu kembali ke langkah sebelumnya,
        /// bukan menutup halaman utama.
        /// </param>
        public PdfPreviewViewModel(string title, string? pdfPath, NavigationService navigation,
            int? suratId = null, Func<int, InputWindowViewModel>? editViewModelFactory = null,
            IMessageService? messageService = null, Action? batalKembali = null)
        {
            Title = title;
            PdfPath = pdfPath;
            IsMissing = string.IsNullOrEmpty(pdfPath);
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _suratId = suratId;
            _editViewModelFactory = editViewModelFactory;
            _messageService = messageService;
            BatalCommand = new RelayCommand(batalKembali ?? (() => _navigation.ShowDefault()));
            ExportCommand = new RelayCommand(ExportAs);
            EditCommand = new RelayCommand(EditSurat, () => CanEdit);
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
            var raw = !string.IsNullOrWhiteSpace(Title) ? Title : System.IO.Path.GetFileNameWithoutExtension(PdfPath);
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var clean = new string(raw.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Dokumen.pdf" : clean + ".pdf";
        }
    }
}
