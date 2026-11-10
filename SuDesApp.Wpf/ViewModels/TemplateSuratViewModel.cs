using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman daftar Template Surat: surat buatan pengguna sendiri untuk jenis yang
    /// tidak tersedia pada menu pembuatan surat bawaan.
    ///
    /// Dari halaman ini pengguna membuat template baru lewat wizard, menyuntingnya,
    /// membuat salinan, menghapus, atau langsung memakai template untuk mengisi dan
    /// mencetak surat.
    ///
    /// Selain menyusun sendiri lewat wizard, template bisa dibuat dari berkas Word
    /// (.docx) yang sudah dipakai sehari-hari: susunan surat pada berkas dibaca, lalu
    /// dijadikan template yang bisa diisi dan dicetak seperti template biasa.
    ///
    /// Berkas .doc (Word lama) ikut didukung: berkas dikonversi otomatis lebih dulu
    /// memakai Microsoft Word bila terpasang.
    ///
    /// Contoh siap pakai dipasang otomatis sekali bila daftar masih kosong.
    /// </summary>
    public partial class TemplateSuratViewModel : ObservableObject
    {
        /// <summary>Mode pembukaan wizard.</summary>
        public const int ModeBaru = 0;
        public const int ModeEdit = 1;
        public const int ModeDuplikat = 2;

        /// <summary>Buat template baru dari hasil pembacaan berkas Word (bukan lewat wizard dari nol).</summary>
        public const int ModeImpor = 3;

        // Pilihan sumber daftar: seluruh template, hanya yang tersimpan di database,
        // atau hanya contoh aplikasi yang belum disalin.
        public const string FilterSemua = "SEMUA";
        public const string FilterTersimpan = "TERSIMPAN";
        public const string FilterContoh = "CONTOH";

        // Urutan daftar: nama (bawaan), terakhir diubah, atau judul surat.
        public const string UrutNama = "NAMA";
        public const string UrutDiubah = "DIUBAH";
        public const string UrutJudul = "JUDUL";

        private readonly ITemplateSuratRepository _repository;
        private readonly TemplateSuratGenerator _generator;
        private readonly TemplateSuratBawaanService _bawaanService;
        private readonly ITemplateSuratWordImpor _wordImpor;
        private readonly ITemplateSuratPratinjau _pratinjauService;
        private readonly Func<int, TemplateSuratKustom?, TemplateSuratWizardViewModel> _wizardFactory;
        private readonly Func<IsiTemplateSuratViewModel> _isiFactory;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly ILogger<TemplateSuratViewModel> _logger;

        private readonly List<TemplateSuratKustom> _semua = new();
        // Catalog samples not present in the user's database are shown as virtual rows.
        // Their negative IDs keep them selectable without pretending they are saved.
        private readonly Dictionary<int, string> _contohVirtual = new();
        private Task _pemuatanAwal = Task.CompletedTask;

        /// <summary>
        /// Kabar halaman yang tampil di dalam daftar template (bukan dialog popup): hasil
        /// pembacaan berkas Word, sebab sebuah tombol tidak bisa dilanjutkan, dan kegagalan
        /// daftar/pratinjau. Kartu berkunci sama saling menimpa supaya tidak menumpuk.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        /// <summary>Contoh bawaan hanya disiapkan sekali per pemakaian halaman.</summary>
        private bool _sudahSiapkanBawaan;

        private string _pencarian = string.Empty;
        private string _filterSumber = FilterSemua;
        private string _urutan = UrutNama;
        private TemplateSuratKustom? _terpilih;
        private bool _isBusy;
        private string _pesanKosong = string.Empty;
        private string _infoBawaan = string.Empty;

        private readonly PenjagaLisensi? _penjaga;

        public TemplateSuratViewModel(
            ITemplateSuratRepository repository,
            TemplateSuratGenerator generator,
            TemplateSuratBawaanService bawaanService,
            ITemplateSuratWordImpor wordImpor,
            ITemplateSuratPratinjau pratinjauService,
            Func<int, TemplateSuratKustom?, TemplateSuratWizardViewModel> wizardFactory,
            Func<IsiTemplateSuratViewModel> isiFactory,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            IMessageService messageService,
            ILogger<TemplateSuratViewModel> logger,
            PenjagaLisensi? penjaga = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _bawaanService = bawaanService ?? throw new ArgumentNullException(nameof(bawaanService));
            _wordImpor = wordImpor ?? throw new ArgumentNullException(nameof(wordImpor));
            _pratinjauService = pratinjauService ?? throw new ArgumentNullException(nameof(pratinjauService));
            _wizardFactory = wizardFactory ?? throw new ArgumentNullException(nameof(wizardFactory));
            _isiFactory = isiFactory ?? throw new ArgumentNullException(nameof(isiFactory));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _penjaga = penjaga;

            Items = new ObservableCollection<TemplateSuratKustom>();

            BuatBaruCommand = new RelayCommand(BukaWizardBaru, () => !IsBusy);
            EditCommand = new AsyncRelayCommand(() => BukaEditAsync(Terpilih), () => !IsBusy && Terpilih != null);
            DuplikatCommand = new RelayCommand(BukaWizardDuplikat, () => !IsBusy && Terpilih != null);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => !IsBusy && Terpilih?.Id > 0);

            // Contoh bawaan yang belum disalin tetap boleh diisi dan dipratinjau:
            // keduanya hanya membaca definisi template. Yang menuntut template
            // tersimpan hanyalah Hapus, karena tidak ada baris database untuk dihapus.
            IsiCommand = new AsyncRelayCommand(IsiAsync, () => !IsBusy && Terpilih != null);
            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy && Terpilih != null);
            SegarkanCommand = new AsyncRelayCommand(MuatAsync, () => !IsBusy);
            BuatDariWordCommand = new AsyncRelayCommand(BuatDariWordAsync, () => !IsBusy);
            BersihkanPencarianCommand = new RelayCommand(() => Pencarian = string.Empty);
            PilihFilterCommand = new RelayCommand<string>(PilihFilter, nilai => nilai != null);

            _pemuatanAwal = MuatAsync();
        }


    }
}
