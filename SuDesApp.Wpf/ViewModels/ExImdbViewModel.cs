using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel halaman Pencadangan Database.
    ///
    /// Satu halaman dengan empat bagian: <b>Ringkasan</b> (info database + aksi
    /// cepat), <b>Cadangan</b> (riwayat .db di folder database), <b>Ekspor</b>
    /// (membuat salinan cadangan ke berkas pilihan pengguna), dan <b>Impor</b>
    /// (memulihkan database dari berkas cadangan). Semua kabar, kesalahan, dan
    /// konfirmasi impor ditampilkan di dalam halaman lewat <see cref="Pesan"/> —
    /// halaman ini tidak memakai dialog popup sama sekali.
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
            MuatCadanganCommand = new RelayCommand(SegarkanData);
            PulihkanCadanganCommand = new RelayCommand<CadanganTampil>(PulihkanCadangan,
                c => c is not null && !IsExporting && !IsImporting);
            BuatCadanganCepatCommand = new AsyncRelayCommand(BuatCadanganCepatAsync,
                () => !IsExporting && !IsImporting && DatabaseAda);
            SalinLokasiDatabaseCommand = new RelayCommand(SalinLokasiDatabase);

            // Panduan singkat bukan bagian navigasi sendiri: ia menempel di
            // Ringkasan supaya pengguna tidak perlu satu klik tambahan.
            Bagian = new List<AboutBagianViewModel>
            {
                new("Ringkasan", "\U0001F4BE", "Database aktif",
                    "Pencadangan Database — lokasi, ukuran, dan waktu perubahan database yang sedang dipakai."),
                new("Cadangan", "\U0001F5C3", "Cadangan tersedia",
                    "Pencadangan Database — daftar cadangan .db di folder database, siap dipulihkan sekali klik."),
                new("Ekspor", "\U0001F4E4", "Ekspor — cadangkan",
                    "Pencadangan Database — buat salinan cadangan .db ke lokasi pilihan Anda."),
                new("Impor", "\U0001F4E5", "Impor — pulihkan",
                    "Pencadangan Database — pulihkan seluruh data dari berkas cadangan .db."),
            };

            _bagianAktif = Bagian[0];
            Bagian[0].IsTerpilih = true;

            MuatInfoDatabase();
            IsiNamaBerkasEksporDefault();
            MuatCadanganTerbaru();
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
        // Cadangan terbaru di folder database
        // =====================================================================

        /// <summary>
        /// Cadangan .db terbaru di folder database (selain database aktif).
        /// Dipakai agar pemulihan bisa dimulai dari daftar ini tanpa membuka
        /// dialog berkas — termasuk cadangan otomatis sebelum impor.
        /// </summary>
        public ObservableCollection<CadanganTampil> CadanganTerbaru { get; } = new();

        public bool AdaCadanganTerbaru => CadanganTerbaru.Count > 0;

        public bool TidakAdaCadangan => CadanganTerbaru.Count == 0;

        /// <summary>Jumlah cadangan yang tampil — dipakai petak KPI dan label bagian.</summary>
        public string JumlahCadangan => CadanganTerbaru.Count == 0
            ? "Belum ada"
            : CadanganTerbaru.Count + " berkas";

        /// <summary>Waktu cadangan terbaru, atau ajakan membuat cadangan pertama.</summary>
        public string CadanganTerakhir => CadanganTerbaru.Count == 0
            ? "Belum pernah"
            : CadanganTerbaru[0].Diubah;

        public string KeteranganCadangan => CadanganTerbaru.Count == 0
            ? "Belum ada berkas cadangan di folder database. Buat cadangan pertama lewat bagian Ekspor."
            : $"{CadanganTerbaru.Count} cadangan terbaru di folder database, terbaru lebih dulu. "
              + "Klik Pulihkan pada salah satu kartu untuk memakai berkas itu.";

        public ICommand MuatCadanganCommand { get; }
        public ICommand PulihkanCadanganCommand { get; }

        /// <summary>Buat cadangan tanpa dialog, langsung di folder database.</summary>
        public ICommand BuatCadanganCepatCommand { get; }

        public ICommand SalinLokasiDatabaseCommand { get; }

        /// <summary>Baca ulang info database sekaligus pindai ulang berkas cadangan.</summary>
        private void SegarkanData()
        {
            MuatInfoDatabase();
            MuatCadanganTerbaru();
            StatusText = "Informasi database dan daftar cadangan disegarkan.";
        }

        /// <summary>Pindai ulang folder database dan segarkan daftar cadangan.</summary>
        public void MuatCadanganTerbaru()
        {
            CadanganTerbaru.Clear();

            try
            {
                var aktif = _service.GetDatabasePath();
                var folder = Path.GetDirectoryName(aktif) ?? string.Empty;

                foreach (var cadangan in CadanganDatabase.Pindai(folder, aktif))
                {
                    CadanganTerbaru.Add(new CadanganTampil(
                        cadangan.Path,
                        cadangan.Nama,
                        FormulirTemplateUkuran(cadangan.UkuranByte),
                        cadangan.Diubah.ToString("dd MMM yyyy HH:mm"),
                        cadangan.SebelumImpor ? "Otomatis sebelum impor" : "Cadangan ekspor",
                        cadangan.SebelumImpor ? "\U0001F6DF" : "\U0001F5C4"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Daftar cadangan tidak dapat dipindai");
            }

            OnPropertyChanged(nameof(AdaCadanganTerbaru));
            OnPropertyChanged(nameof(TidakAdaCadangan));
            OnPropertyChanged(nameof(JumlahCadangan));
            OnPropertyChanged(nameof(CadanganTerakhir));
            OnPropertyChanged(nameof(KeteranganCadangan));
            (BuatCadanganCepatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Mulai pemulihan dari salah satu kartu cadangan: isi jalur impor,
        /// pindah ke bagian Impor, lalu buka kartu konfirmasi. Berkas yang
        /// sudah hilang tidak pernah masuk ke jalur impor.
        /// </summary>
        private void PulihkanCadangan(CadanganTampil? cadangan)
        {
            if (cadangan is null || IsImporting || IsExporting)
            {
                return;
            }

            if (!File.Exists(cadangan.Path))
            {
                TampilkanPesan("Berkas cadangan tidak ditemukan",
                    "Berkas " + cadangan.Nama + " sudah tidak ada di folder database.", berhasil: false,
                    "Daftar cadangan sudah disegarkan — pilih berkas lain atau telusuri lewat tombol Pilih Berkas.");
                MuatCadanganTerbaru();
                return;
            }

            ImportPath = cadangan.Path;
            AdaPesan = false;

            var bagianImpor = Bagian.FirstOrDefault(b => b.Kunci == "Impor");
            if (bagianImpor != null)
            {
                PilihBagian(bagianImpor);
            }

            AdaKonfirmasiImpor = true;
            StatusText = "Menunggu konfirmasi impor: " + cadangan.Nama;
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
                    OnPropertyChanged(nameof(ExportNamaBerkas));
                    OnPropertyChanged(nameof(ExportFolder));
                }
            }
        }

        /// <summary>Nama berkas tujuan, dipakai petak ringkas di kartu Ekspor.</summary>
        public string ExportNamaBerkas
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0) return "Belum ditentukan";
                try
                {
                    var nama = Path.GetFileName(path);
                    return nama.Length == 0 ? "Belum ditentukan" : nama;
                }
                catch (ArgumentException)
                {
                    return "Nama berkas tidak valid";
                }
            }
        }

        /// <summary>Folder tujuan, dipakai petak ringkas di kartu Ekspor.</summary>
        public string ExportFolder
        {
            get
            {
                var path = _exportPath.Trim();
                if (path.Length == 0) return "-";
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    return string.IsNullOrEmpty(dir) ? "-" : dir!;
                }
                catch (ArgumentException)
                {
                    return "-";
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
                    PerbaruiInfoSumberImpor();
                }
            }
        }

        public bool SumberImporDikenali => _importPath.Trim().Length > 0;

        // Ringkasan berkas yang dipilih — dipakai petak KPI di kartu Impor.
        private string _sumberImporUkuran = "-";
        private string _sumberImporDiubah = "-";
        private string _sumberImporStatus = "Belum dipilih";
        private bool _sumberImporSiap;

        public string SumberImporUkuran { get => _sumberImporUkuran; private set => SetProperty(ref _sumberImporUkuran, value); }
        public string SumberImporDiubah { get => _sumberImporDiubah; private set => SetProperty(ref _sumberImporDiubah, value); }
        public string SumberImporStatus { get => _sumberImporStatus; private set => SetProperty(ref _sumberImporStatus, value); }
        public bool SumberImporSiap { get => _sumberImporSiap; private set => SetProperty(ref _sumberImporSiap, value); }

        private void PerbaruiInfoSumberImpor()
        {
            var path = _importPath.Trim();
            if (path.Length == 0)
            {
                SumberImporUkuran = "-";
                SumberImporDiubah = "-";
                SumberImporStatus = "Belum dipilih";
                SumberImporSiap = false;
                return;
            }

            try
            {
                var berkas = new FileInfo(path);
                if (berkas.Exists)
                {
                    SumberImporUkuran = FormulirTemplateUkuran(berkas.Length);
                    SumberImporDiubah = berkas.LastWriteTime.ToString("dd MMM yyyy HH:mm");
                    SumberImporStatus = "Siap dipulihkan";
                    SumberImporSiap = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Info berkas cadangan tidak dapat dibaca");
            }

            SumberImporUkuran = "-";
            SumberImporDiubah = "-";
            SumberImporStatus = "Berkas tidak ditemukan";
            SumberImporSiap = false;
        }

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
        private string _namaDatabase = "-";
        private string _ukuranDatabase = "-";
        private string _diubahDatabase = "-";
        private string _statusDatabase = "Tidak ditemukan";
        private bool _databaseAda;

        public string LokasiDatabase { get => _lokasiDatabase; private set => SetProperty(ref _lokasiDatabase, value); }
        public string NamaDatabase { get => _namaDatabase; private set => SetProperty(ref _namaDatabase, value); }
        public string UkuranDatabase { get => _ukuranDatabase; private set => SetProperty(ref _ukuranDatabase, value); }
        public string DiubahDatabase { get => _diubahDatabase; private set => SetProperty(ref _diubahDatabase, value); }
        public string StatusDatabase { get => _statusDatabase; private set => SetProperty(ref _statusDatabase, value); }
        public bool DatabaseAda { get => _databaseAda; private set => SetProperty(ref _databaseAda, value); }

        /// <summary>Baca lokasi, ukuran, dan waktu ubah database yang sedang aktif.</summary>
        public void MuatInfoDatabase()
        {
            try
            {
                var path = _service.GetDatabasePath();
                LokasiDatabase = path;
                NamaDatabase = Path.GetFileName(path);

                var berkas = new FileInfo(path);
                DatabaseAda = berkas.Exists;
                StatusDatabase = berkas.Exists ? "Terbaca" : "Tidak ditemukan";
                UkuranDatabase = berkas.Exists ? FormulirTemplateUkuran(berkas.Length) : "belum ada berkas";
                DiubahDatabase = berkas.Exists ? berkas.LastWriteTime.ToString("dd MMM yyyy HH:mm") : "-";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca info database");
                LokasiDatabase = "-";
                NamaDatabase = "-";
                UkuranDatabase = "-";
                DiubahDatabase = "-";
                StatusDatabase = "Tidak ditemukan";
                DatabaseAda = false;
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

                ExportPath = Path.Combine(folder, CadanganDatabase.NamaEksporBawaan(DateTime.Now));
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

        private void SalinLokasiDatabase()
        {
            try
            {
                if (LokasiDatabase == "-" || LokasiDatabase.Length == 0)
                {
                    StatusText = "Lokasi database belum terbaca.";
                    return;
                }

                System.Windows.Clipboard.SetText(LokasiDatabase);
                StatusText = "Lokasi database disalin ke papan klip.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lokasi database tidak dapat disalin");
                StatusText = "Lokasi database tidak dapat disalin.";
            }
        }

        /// <summary>
        /// Cadangan sekali klik: berkas dibuat di folder database dengan nama
        /// bertanggal. Nama yang sudah terpakai tidak ditimpa — diberi akhiran
        /// angka supaya cadangan sebelumnya tetap utuh.
        /// </summary>
        private async Task BuatCadanganCepatAsync()
        {
            if (IsExporting || IsImporting) return;

            string folder;
            try
            {
                folder = Path.GetDirectoryName(_service.GetDatabasePath()) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Folder database tidak terbaca untuk cadangan cepat");
                TampilkanPesan("Cadangan gagal", "Folder database tidak dapat dibaca.", berhasil: false,
                    "Gunakan bagian Ekspor lalu pilih lokasi cadangan secara manual.");
                return;
            }

            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                StatusText = "Folder database tidak ditemukan.";
                TampilkanPesan("Cadangan gagal", "Folder database tidak ditemukan.", berhasil: false,
                    "Gunakan bagian Ekspor untuk memilih folder cadangan lain.");
                return;
            }

            var dasar = CadanganDatabase.NamaEksporBawaan(DateTime.Now);
            var path = Path.Combine(folder, dasar);
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(folder, Path.GetFileNameWithoutExtension(dasar) + "_" + i + ".db");
            }

            ExportPath = path;
            await JalankanEksporAsync(path);
        }

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
            (PulihkanCadanganCommand as RelayCommand<CadanganTampil>)?.RaiseCanExecuteChanged();
            (BuatCadanganCepatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
                    : CadanganDatabase.NamaEksporBawaan(DateTime.Now)),
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
                MuatCadanganTerbaru();

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
                    "Contoh: desa_cadangan_2026-09-21_1430.db — ubah nama berkasnya lewat tombol Pilih Lokasi.");
                return;
            }

            await JalankanEksporAsync(path);
        }

        /// <summary>Inti ekspor: satu jalur untuk tombol Ekspor dan cadangan sekali klik.</summary>
        private async Task JalankanEksporAsync(string path)
        {
            if (IsExporting || IsImporting) return;

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

                MuatCadanganTerbaru();
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

    /// <summary>Satu baris cadangan yang siap ditampilkan di halaman.</summary>
    public sealed class CadanganTampil
    {
        public CadanganTampil(string path, string nama, string ukuran, string diubah, string jenis, string ikon)
        {
            Path = path;
            Nama = nama;
            Ukuran = ukuran;
            Diubah = diubah;
            Jenis = jenis;
            Ikon = ikon;
        }

        public string Path { get; }
        public string Nama { get; }
        public string Ukuran { get; }
        public string Diubah { get; }
        public string Jenis { get; }
        public string Ikon { get; }
    }
}
