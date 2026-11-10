using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Wizard pembuatan/penyuntingan Template Surat.
    ///
    /// Alurnya mengikuti cara kerja surat disusun dari atas ke bawah:
    ///   1. Identitas   — nama & keterangan template
    ///   2. Elemen      — kop, judul, nomor, tempat &amp; tanggal (dipilih dengan kotak centang)
    ///   3. Teks        — blok teks bebas yang diketik sendiri
    ///   4. Kolom Isian — NIK, nama, dan kolom lain yang akan diisi saat membuat surat
    ///   5. Tata Letak  — grid, tanda tangan, dan teks kaki
    ///   6. Pratinjau   — contoh surat dengan isi contoh; bila kurang, kembali untuk mengedit
    ///
    /// Setelah pratinjau disetujui, template disimpan dan pengguna langsung dibawa
    /// ke formulir pengisian kolom yang sudah dipilih.
    /// </summary>
    public partial class TemplateSuratWizardViewModel : ObservableObject, IJudulHalaman
    {
        /// <summary>
        /// Indeks langkah pratinjau. Langkah penyuntingan 0..3, lalu pratinjau 4:
        /// Identitas → Elemen Surat → Isi Surat → Tata Letak &amp; Kaki → Pratinjau.
        /// </summary>
        private const int LangkahTerakhir = 4;

        /// <summary>Mode saat ini (0 baru, 1 ubah, 2 duplikat) — dipakai untuk judul title bar.</summary>
        private int _modeWizard;

        private readonly ITemplateSuratRepository _repository;
        private readonly TemplateSuratGenerator _generator;
        private readonly Func<string, string, Action?, PdfPreviewViewModel> _previewFactory;
        private readonly IMessageService _messageService;
        private readonly ILogger<TemplateSuratWizardViewModel> _logger;

        private int _langkah;
        private bool _isBusy;
        private string _pesanKesalahan = string.Empty;
        private string _pesanInformasi = string.Empty;
        private PdfPreviewViewModel? _pratinjau;
        private string? _berkasPratinjau;

        // Identitas
        private string _nama = string.Empty;
        private string _deskripsi = string.Empty;
        private bool _formatContoh;
        private int _versiFormat = 1;

        // Elemen
        private bool _pakaiKop = true;
        private bool _pakaiJudul = true;
        private string _judul = string.Empty;
        private string _subJudul = string.Empty;
        private bool _pakaiNomor = true;
        private string _awalanNomor = "470";
        private string _polaNomor = TemplateSuratNomor.PolaBawaan;
        private bool _pakaiTempatTanggal;
        private bool _pakaiBlok = true;
        private bool _pakaiKolom = true;

        // Tata letak & kaki
        private bool _pakaiGrid;
        private bool _pakaiTandaTangan = true;
        private string _jabatanPenandatangan = string.Empty;
        private bool _pakaiTeksKaki;
        private string _teksKaki = string.Empty;
        private bool _nomorManual;
        private bool _penandatanganDariDataDiri;
        private string _kunciPenandatanganDataDiri = string.Empty;

        /// <summary>ID template yang sedang disunting (0 = template baru).</summary>
        private int _idTersunting;

        /// <summary>Data penomoran template yang sedang disunting — dipertahankan saat menyimpan.</summary>
        private int _nomorTerakhir;
        private int _tahunNomor;
        private DateTime _dibuat;
        private string _dibuatOleh = string.Empty;

        private readonly PenjagaLisensi? _penjaga;

        public TemplateSuratWizardViewModel(
            ITemplateSuratRepository repository,
            TemplateSuratGenerator generator,
            Func<string, string, Action?, PdfPreviewViewModel> previewFactory,
            IMessageService messageService,
            ILogger<TemplateSuratWizardViewModel> logger,
            PenjagaLisensi? penjaga = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _penjaga = penjaga;

            LangkahDaftar = new ObservableCollection<LangkahItemViewModel>(new[]
            {
                new LangkahItemViewModel(1, "Identitas Template", "Nama dan keterangan singkat"),
                new LangkahItemViewModel(2, "Elemen Surat", "Kop, judul, nomor, tempat & tanggal"),
                new LangkahItemViewModel(3, "Isi Surat", "Blok teks, data diri, dan kolom isian berurutan"),
                new LangkahItemViewModel(4, "Tata Letak & Kaki", "Grid, tanda tangan, teks kaki"),
                new LangkahItemViewModel(5, "Pratinjau", "Periksa hasil sebelum disimpan")
            });
            PerbaruiPenandaLangkah();

            Blok = new ObservableCollection<BlokTeksItemViewModel>();
            Kolom = new ObservableCollection<KolomItemViewModel>();
            Bagian = new ObservableCollection<BagianWizardItemViewModel>();
            Bagian.CollectionChanged += BagianBerubah;

            LanjutCommand = new AsyncRelayCommand(LanjutAsync, () => !IsBusy);
            KePratinjauCommand = new AsyncRelayCommand(KePratinjauAsync, () => !IsBusy);
            KembaliCommand = new RelayCommand(Kembali, () => !IsBusy && Langkah > 0);
            PilihLangkahCommand = new RelayCommand<LangkahItemViewModel>(PilihLangkah, item => !IsBusy && item != null && item.Nomor - 1 != Langkah);
            SimpanCommand = new AsyncRelayCommand(() => SimpanAsync(lanjutIsi: false), () => !IsBusy);
            SimpanDanIsiCommand = new AsyncRelayCommand(() => SimpanAsync(lanjutIsi: true), () => !IsBusy);
            SegarkanPratinjauCommand = new AsyncRelayCommand(SegarkanPratinjauAsync, () => !IsBusy);
            BatalCommand = new RelayCommand(() => RequestClose?.Invoke());

            TambahBlokCommand = new RelayCommand(TambahBlok, () => !IsBusy);
            TambahBlokUmumCommand = new RelayCommand(TambahBlokUmum, () => !IsBusy);
            HapusBlokCommand = new RelayCommand<BlokTeksItemViewModel>(HapusBlok, b => !IsBusy && b != null);
            NaikBlokCommand = new RelayCommand<BlokTeksItemViewModel>(b => Geser(Blok, b, -1), b => !IsBusy && b != null);
            TurunBlokCommand = new RelayCommand<BlokTeksItemViewModel>(b => Geser(Blok, b, 1), b => !IsBusy && b != null);

            TambahKolomCommand = new RelayCommand(TambahKolom, () => !IsBusy);
            TambahKolomUmumCommand = new RelayCommand(TambahKolomUmum, () => !IsBusy);
            HapusKolomCommand = new RelayCommand<KolomItemViewModel>(HapusKolom, k => !IsBusy && k != null);
            NaikKolomCommand = new RelayCommand<KolomItemViewModel>(k => Geser(Kolom, k, -1), k => !IsBusy && k != null);
            TurunKolomCommand = new RelayCommand<KolomItemViewModel>(k => Geser(Kolom, k, 1), k => !IsBusy && k != null);

            // ===== Susunan berurutan (bagian) =====
            TambahBagianTeksCommand = new RelayCommand(TambahBagianTeks, () => !IsBusy);
            TambahBagianDataDiriCommand = new RelayCommand(TambahBagianDataDiri, () => !IsBusy);
            TambahBagianDataDiriRingkasCommand = new RelayCommand(TambahBagianDataDiriRingkas, () => !IsBusy);
            TambahBagianKolomCommand = new RelayCommand(TambahBagianKolom, () => !IsBusy);
            HapusBagianCommand = new RelayCommand<BagianWizardItemViewModel>(HapusBagian, b => !IsBusy && b != null);
            NaikBagianCommand = new RelayCommand<BagianWizardItemViewModel>(b => Geser(Bagian, b, -1), b => !IsBusy && b != null);
            TurunBagianCommand = new RelayCommand<BagianWizardItemViewModel>(b => Geser(Bagian, b, 1), b => !IsBusy && b != null);

            TambahKolomBagianCommand = new RelayCommand<BagianWizardItemViewModel>(TambahKolomBagian, b => !IsBusy && b != null && b is BagianKolomWizardItemViewModel or BagianDataDiriWizardItemViewModel);
            TambahKolomUmumBagianCommand = new RelayCommand<BagianWizardItemViewModel>(TambahKolomUmumBagian, b => !IsBusy && b != null && b is BagianKolomWizardItemViewModel or BagianDataDiriWizardItemViewModel);
            HapusKolomBagianCommand = new RelayCommand<KolomItemViewModel>(HapusKolomBagian, k => !IsBusy && k != null);
            NaikKolomBagianCommand = new RelayCommand<KolomItemViewModel>(k => GeserKolomBagian(k, -1), k => !IsBusy && k != null);
            TurunKolomBagianCommand = new RelayCommand<KolomItemViewModel>(k => GeserKolomBagian(k, 1), k => !IsBusy && k != null);

            PerbaruiLencana();
        }

        /// <summary>Diminta menutup halaman wizard (batal atau selesai disimpan).</summary>
        public event Action? RequestClose;

        /// <summary>Template selesai disimpan; parameter true bila pengguna ingin langsung mengisi surat.</summary>
        public event Action<TemplateSuratKustom, bool>? Selesai;

        /// <summary>
        /// Siapkan wizard: <paramref name="mode"/> 0 = template baru, 1 = menyunting
        /// template terpilih, 2 = menyalin template terpilih. Bagian ini dulu ada di
        /// jendela wizard; kini wizard ditanam di area konten menu utama.
        /// </summary>
        public void Initialize(int mode, TemplateSuratKustom? template)
        {
            _modeWizard = mode;
            OnPropertyChanged(nameof(JudulHalaman));

            switch (mode)
            {
                case TemplateSuratViewModel.ModeEdit when template != null:
                    _ = MuatAsync(template.Id);
                    break;

                case TemplateSuratViewModel.ModeDuplikat when template != null:
                    SiapkanBaru(template.Clone());
                    break;

                // Hasil pembacaan berkas Word: susunan & nama sudah disiapkan, jadi
                // wizard dibuka apa adanya supaya bisa diperiksa lalu disimpan.
                case TemplateSuratViewModel.ModeImpor when template != null:
                    SiapkanBaru(template.Clone(), dariImporWord: true);
                    break;

                default:
                    SiapkanBaru();
                    break;
            }
        }


    }
}
