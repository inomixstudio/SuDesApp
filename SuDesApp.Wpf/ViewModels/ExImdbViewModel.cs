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
    public partial class ExImdbViewModel : ObservableObject
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
