using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel halaman Pencadangan Database.
    ///
    /// Satu halaman dengan dua kartu: <b>Ekspor</b> (membuat salinan cadangan
    /// database ke berkas pilihan pengguna) dan <b>Impor</b> (memulihkan database
    /// dari berkas cadangan). Semua kabar, kesalahan, dan konfirmasi impor
    /// ditampilkan di dalam halaman lewat <see cref="Pesan"/> — halaman ini tidak
    /// memakai dialog popup sama sekali.
    /// </summary>
    public class ExImdbViewModel : ObservableObject
    {
        private readonly DatabaseImportExportService _service;
        private readonly ILogger<ExImdbViewModel> _logger;

        private string _statusText = "Siap";
        private string _exportPath = string.Empty;
        private string _importPath = string.Empty;
        private bool _isExporting;
        private bool _isImporting;
        private bool _adaKonfirmasiImpor;
        private AboutBagianViewModel _bagianAktif = null!;

        public ExImdbViewModel(
            DatabaseImportExportService service,
            ILogger<ExImdbViewModel> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            EksporCommand = new AsyncRelayCommand(EksporAsync, () => !IsExporting && !IsImporting);
            ImporCommand = new AsyncRelayCommand(ImporAsync, () => !IsExporting && !IsImporting);
            PilihTujuanEksporCommand = new RelayCommand(PilihTujuanEkspor, () => !IsExporting && !IsImporting);
            PilihSumberImporCommand = new RelayCommand(PilihSumberImpor, () => !IsExporting && !IsImporting);
            KonfirmasiImporCommand = new AsyncRelayCommand(JalankanImporAsync, () => !IsImporting && !IsExporting);
            BatalImporCommand = new RelayCommand(() => { AdaKonfirmasiImpor = false; StatusText = "Impor dibatalkan."; });
            BukaFolderDatabaseCommand = new RelayCommand(BukaFolderDatabase);
            TutupTerakhirCommand = new RelayCommand(() => { AdaPesan = false; PesanRincian.Clear(); });

            // "Informasi penting" TIDAK menjadi bagian navigasi — kartunya tampil
            // tetap di bawah konten tanpa perlu diklik (lihat ExImdbView).
            Bagian = new List<AboutBagianViewModel>
            {
                new("Ringkasan", "\U0001F4BE", "Database aktif",
                    "Pencadangan Database — lokasi, ukuran, dan waktu perubahan database yang sedang dipakai."),
                new("Ekspor", "\U0001F4E4", "Ekspor — cadangkan",
                    "Pencadangan Database — buat salinan cadangan .db ke lokasi pilihan Anda."),
                new("Impor", "\U0001F4E5", "Impor — pulihkan",
                    "Pencadangan Database — pulihkan seluruh data dari berkas cadangan .db."),
            };

            _bagianAktif = Bagian[0];
            Bagian[0].IsTerpilih = true;

            MuatInfoDatabase();
            IsiNamaBerkasEksporDefault();
        }

        /// <summary>Daftar bagian untuk panel navigasi kiri.</summary>
        public IReadOnlyList<AboutBagianViewModel> Bagian { get; private set; } = null!;

        /// <summary>Bagian yang sedang ditampilkan.</summary>
        public AboutBagianViewModel BagianAktif
        {
            get => _bagianAktif;
            private set => SetProperty(ref _bagianAktif, value);
        }

        /// <summary>Dipanggil nav kiri (code-behind) saat bagian dipilih.</summary>
        public void PilihBagian(AboutBagianViewModel bagian)
        {
            if (bagian == null || ReferenceEquals(bagian, BagianAktif))
            {
                return;
            }

            foreach (var item in Bagian)
            {
                item.IsTerpilih = ReferenceEquals(item, bagian);
            }

            BagianAktif = bagian;
        }

        // =====================================================================
        // Keadaan halaman
        // =====================================================================

        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool IsExporting
        {
            get => _isExporting;
            private set { if (SetProperty(ref _isExporting, value)) RaiseCommandStates(); }
        }

        public bool IsImporting
        {
            get => _isImporting;
            private set { if (SetProperty(ref _isImporting, value)) RaiseCommandStates(); }
        }

        /// <summary>Benar bila kartu konfirmasi impor sedang menunggu jawaban pengguna.</summary>
        public bool AdaKonfirmasiImpor
        {
            get => _adaKonfirmasiImpor;
            private set
            {
                if (SetProperty(ref _adaKonfirmasiImpor, value))
                {
                    OnPropertyChanged(nameof(TampilTombolImpor));
                }
            }
        }

        /// <summary>Tombol "Pulihkan Database" hanya tampil bila kartu konfirmasi belum terbuka.</summary>
        public bool TampilTombolImpor => !_adaKonfirmasiImpor && !_isImporting;

        public bool AdaKesibukan => IsExporting || IsImporting;

        // =====================================================================
        // Ekspor (kartu 1)
        // =====================================================================

        /// <summary>Lokasi berkas cadangan yang akan dibuat.</summary>
        public string ExportPath
        {
            get => _exportPath;
            set
            {
                if (SetProperty(ref _exportPath, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(KeteranganTujuanEkspor));
                }
            }
        }

        public string KeteranganTujuanEkspor
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0)
                {
                    return "Belum ada tujuan. Tekan Pilih Lokasi untuk menentukan nama & folder berkas cadangan.";
                }

                var dir = Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(dir)
                    ? "Tujuan cadangan belum lengkap — pilih folder lewat tombol Pilih Lokasi."
                    : "Cadangan akan disimpan di folder: " + dir;
            }
        }

        // =====================================================================
        // Impor (kartu 2)
        // =====================================================================

        /// <summary>Berkas cadangan yang akan dipakai memulihkan database.</summary>
        public string ImportPath
        {
            get => _importPath;
            set
            {
                if (SetProperty(ref _importPath, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(KeteranganSumberImpor));
                    OnPropertyChanged(nameof(SumberImporDikenali));
                }
            }
        }

        public bool SumberImporDikenali => _importPath.Trim().Length > 0;

        public string KeteranganSumberImpor
        {
            get
            {
                var path = _importPath.Trim();
                if (path.Length == 0)
                {
                    return "Belum ada berkas cadangan dipilih. Tekan Pilih Berkas untuk menunjuk berkas .db hasil ekspor sebelumnya.";
                }

                try
                {
                    var berkas = new FileInfo(path);
                    return berkas.Exists
                        ? $"Berkas {berkas.Name} — {FormulirTemplateUkuran(berkas.Length)}, terakhir diubah {berkas.LastWriteTime:dd MMM yyyy HH:mm}."
                        : "Berkas yang ditunjuk tidak ditemukan. Periksa kembali lokasinya.";
                }
                catch (Exception ex)
                {
                    return "Berkas tidak dapat diperiksa: " + ex.Message;
                }
            }
        }

        private static string FormulirTemplateUkuran(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return Math.Max(1, byteCount / 1024) + " KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

        // =====================================================================
        // Informasi database yang sedang dipakai
        // =====================================================================

        private string _lokasiDatabase = "-";
        private string _ukuranDatabase = "-";
        private string _diubahDatabase = "-";

        public string LokasiDatabase { get => _lokasiDatabase; private set => SetProperty(ref _lokasiDatabase, value); }
        public string UkuranDatabase { get => _ukuranDatabase; private set => SetProperty(ref _ukuranDatabase, value); }
        public string DiubahDatabase { get => _diubahDatabase; private set => SetProperty(ref _diubahDatabase, value); }

        /// <summary>Baca lokasi, ukuran, dan waktu ubah database yang sedang aktif.</summary>
        public void MuatInfoDatabase()
        {
            try
            {
                var path = _service.GetDatabasePath();
                LokasiDatabase = path;

                var berkas = new FileInfo(path);
                UkuranDatabase = berkas.Exists ? FormulirTemplateUkuran(berkas.Length) : "belum ada berkas";
                DiubahDatabase = berkas.Exists ? berkas.LastWriteTime.ToString("dd MMM yyyy HH:mm") : "-";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca info database");
                LokasiDatabase = "-";
                UkuranDatabase = "-";
                DiubahDatabase = "-";
            }
        }

        private void IsiNamaBerkasEksporDefault()
        {
            if (_exportPath.Trim().Length > 0) return;

            try
            {
                var dir = Path.GetDirectoryName(_service.GetDatabasePath());
                var folder = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)
                    ? dir!
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                ExportPath = Path.Combine(folder, $"desa_export_{DateTime.Now:yyyyMMddHHmmss}.db");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nama berkas ekspor bawaan tidak dapat disusun");
            }
        }

        // =====================================================================
        // Pemberitahuan inline (pengganti dialog popup)
        // =====================================================================

        private bool _adaPesan;
        private string _pesanJudul = string.Empty;
        private string _pesanIsi = string.Empty;
        private bool _pesanBerhasil = true;

        /// <summary>Benar bila kartu hasil/kesalahan terakhir perlu ditampilkan.</summary>
        public bool AdaPesan
        {
            get => _adaPesan;
            private set => SetProperty(ref _adaPesan, value);
        }

        public string PesanJudul { get => _pesanJudul; private set => SetProperty(ref _pesanJudul, value ?? string.Empty); }
        public string PesanIsi { get => _pesanIsi; private set => SetProperty(ref _pesanIsi, value ?? string.Empty); }

        /// <summary>True = kabar berhasil (kartu hijau), false = gagal/peringatan (kartu merah).</summary>
        public bool PesanBerhasil { get => _pesanBerhasil; private set => SetProperty(ref _pesanBerhasil, value); }

        /// <summary>Baris rincian tambahan yang ditampilkan di dalam kartu kabar.</summary>
        public ObservableCollection<string> PesanRincian { get; } = new();

        public ICommand TutupTerakhirCommand { get; }

        private void TampilkanPesan(string judul, string isi, bool berhasil, params string[] rincian)
        {
            PesanJudul = judul;
            PesanIsi = isi;
            PesanBerhasil = berhasil;
            PesanRincian.Clear();

            foreach (var baris in rincian.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                PesanRincian.Add(baris);
            }

            AdaPesan = true;
        }

        // =====================================================================
        // Perintah
        // =====================================================================

        public ICommand EksporCommand { get; }
        public ICommand ImporCommand { get; }
        public ICommand PilihTujuanEksporCommand { get; }
        public ICommand PilihSumberImporCommand { get; }
        public ICommand KonfirmasiImporCommand { get; }
        public ICommand BatalImporCommand { get; }

        /// <summary>Buka folder database di Windows Explorer — berguna untuk mencari berkas hasil ekspor/backup.</summary>
        public ICommand BukaFolderDatabaseCommand { get; }

        private void BukaFolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(_service.GetDatabasePath());
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    StatusText = "Folder database tidak ditemukan.";
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                StatusText = "Folder database dibuka: " + folder;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder database");
                StatusText = "Folder database tidak dapat dibuka dari sini.";
            }
        }

        private void RaiseCommandStates()
        {
            OnPropertyChanged(nameof(AdaKesibukan));
            OnPropertyChanged(nameof(TampilTombolImpor));
            (EksporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PilihTujuanEksporCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PilihSumberImporCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (KonfirmasiImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Pilih lokasi berkas cadangan baru (SaveFileDialog — pemilih berkas, bukan popup kabar).</summary>
        private void PilihTujuanEkspor()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Simpan salinan cadangan database",
                Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                FileName = Path.GetFileName(_exportPath.Trim().Length > 0
                    ? _exportPath
                    : $"desa_export_{DateTime.Now:yyyyMMddHHmmss}.db"),
                OverwritePrompt = true,
                InitialDirectory = FolderAwal(_exportPath),
            };

            if (dialog.ShowDialog() == true)
            {
                ExportPath = dialog.FileName;
                StatusText = "Tujuan cadangan siap: " + Path.GetFileName(dialog.FileName);
            }
        }

        /// <summary>Pilih berkas cadangan yang akan dipulihkan (OpenFileDialog).</summary>
        private void PilihSumberImpor()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pilih berkas cadangan database",
                Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = FolderAwal(_importPath),
            };

            if (dialog.ShowDialog() == true)
            {
                ImportPath = dialog.FileName;
                AdaKonfirmasiImpor = false;
                StatusText = "Berkas cadangan dipilih: " + Path.GetFileName(dialog.FileName);
            }
        }

        private string FolderAwal(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path.Trim());
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;

                dir = Path.GetDirectoryName(_service.GetDatabasePath());
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch
            {
                // jatuh ke folder Dokumen
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Langkah 1 impor: periksa berkas lalu minta persetujuan di dalam halaman.</summary>
        private async Task ImporAsync()
        {
            if (IsImporting || IsExporting) return;

            if (!ValidasiSumberImpor(out var pesanSalah))
            {
                StatusText = "Impor belum bisa dijalankan.";
                TampilkanPesan("Berkas cadangan belum siap", pesanSalah!, berhasil: false);
                return;
            }

            AdaPesan = false;

            // Konfirmasi ditampilkan sebagai kartu di halaman (bukan dialog popup),
            // karena impor menimpa database yang sedang dipakai.
            AdaKonfirmasiImpor = true;
            StatusText = "Menunggu konfirmasi impor.";
            await Task.CompletedTask;
        }

        /// <summary>Langkah 2 impor: jalankan setelah pengguna menyetujui di kartu konfirmasi.</summary>
        private async Task JalankanImporAsync()
        {
            if (IsImporting || IsExporting) return;

            AdaKonfirmasiImpor = false;
            var path = _importPath.Trim();

            IsImporting = true;
            StatusText = "Memulihkan database dari cadangan...";
            try
            {
                _logger.LogInformation("Memulai impor database dari: {Path}", path);
                await _service.ImportDatabaseAsync(path);
                MuatInfoDatabase();

                StatusText = "Database berhasil dipulihkan dari cadangan.";
                TampilkanPesan(
                    "Database berhasil dipulihkan",
                    "Seluruh data sekarang memakai isi berkas cadangan yang dipilih.",
                    berhasil: true,
                    "Salinan database lama sebelum impor disimpan di folder database dengan nama " +
                    "desa_backup_before_import_<tanggal>.db — bisa dipakai bila ingin kembali ke keadaan sebelumnya.",
                    $"Lokasi database: {LokasiDatabase}",
                    "Semua data di aplikasi sudah memakai hasil impor — buka ulang halaman lain bila perlu.");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Berkas cadangan tidak ditemukan: {Path}", path);
                StatusText = "Impor gagal: berkas cadangan tidak ditemukan.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Periksa kembali lokasi berkas cadangan, lalu pilih berkasnya lagi.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Berkas cadangan tidak valid: {Path}", path);
                StatusText = "Impor gagal: berkas cadangan tidak valid.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Pastikan berkas berasal dari ekspor aplikasi ini dan belum rusak.",
                    "Database yang sekarang dipakai tidak berubah.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impor database gagal: {Path}", path);
                StatusText = "Impor gagal.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Database yang sekarang dipakai tidak berubah — coba lagi atau pilih berkas cadangan lain.");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private bool ValidasiSumberImpor(out string? pesanSalah)
        {
            var path = _importPath.Trim();

            if (path.Length == 0)
            {
                pesanSalah = "Pilih dulu berkas cadangan (.db) yang ingin dipulihkan.";
                return false;
            }

            if (!File.Exists(path))
            {
                pesanSalah = "Berkas cadangan tidak ditemukan pada lokasi tersebut.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                pesanSalah = "Berkas cadangan harus berekstensi .db (hasil ekspor aplikasi ini).";
                return false;
            }

            pesanSalah = null;
            return true;
        }

        /// <summary>Buat salinan cadangan database ke berkas tujuan.</summary>
        private async Task EksporAsync()
        {
            if (IsExporting || IsImporting) return;

            var path = _exportPath.Trim();
            if (path.Length == 0)
            {
                StatusText = "Tujuan cadangan belum diisi.";
                TampilkanPesan("Tujuan cadangan belum diisi", "Tentukan dulu lokasi berkas cadangan.", berhasil: false,
                    "Tekan tombol Pilih Lokasi untuk memilih folder dan nama berkas cadangan.");
                return;
            }

            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Nama berkas cadangan harus berakhiran .db.";
                TampilkanPesan("Nama berkas belum sesuai", "Berkas cadangan harus berekstensi .db.", berhasil: false,
                    "Contoh: desa_export_20260921.db — ubah nama berkasnya lewat tombol Pilih Lokasi.");
                return;
            }

            AdaKonfirmasiImpor = false;
            IsExporting = true;
            StatusText = "Membuat salinan cadangan database...";
            try
            {
                _logger.LogInformation("Memulai ekspor database ke: {Path}", path);
                await _service.ExportDatabaseAsync(path);

                var ukuran = "-";
                try
                {
                    var berkas = new FileInfo(path);
                    if (berkas.Exists) ukuran = FormulirTemplateUkuran(berkas.Length);
                }
                catch
                {
                    // ukuran hanya keterangan tambahan
                }

                StatusText = "Cadangan database berhasil dibuat.";
                TampilkanPesan(
                    "Cadangan berhasil dibuat",
                    "Database tersimpan sebagai berkas cadangan yang bisa dipulihkan kapan saja.",
                    berhasil: true,
                    $"Berkas: {path}",
                    $"Ukuran: {ukuran}",
                    "Simpan berkas ini di tempat lain (flashdisk/Drive) supaya aman bila komputer bermasalah.");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Database aktif tidak ditemukan saat ekspor");
                StatusText = "Cadangan gagal: database aktif tidak ditemukan.";
                TampilkanPesan("Cadangan gagal", ex.Message, berhasil: false,
                    "Pastikan aplikasi masih memakai database seperti biasa, lalu coba lagi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ekspor database gagal: {Path}", path);
                StatusText = "Cadangan gagal dibuat.";
                TampilkanPesan("Cadangan gagal", ex.Message, berhasil: false,
                    "Pastikan folder tujuan bisa ditulis (bukan folder sistem yang dilindungi).",
                    "Coba pilih lokasi lain, misalnya folder Dokumen.");
            }
            finally
            {
                IsExporting = false;
            }
        }
    }
}
