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
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

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
    public class TemplateSuratWizardViewModel : ObservableObject, IJudulHalaman
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

        public TemplateSuratWizardViewModel(
            ITemplateSuratRepository repository,
            TemplateSuratGenerator generator,
            Func<string, string, Action?, PdfPreviewViewModel> previewFactory,
            IMessageService messageService,
            ILogger<TemplateSuratWizardViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

                default:
                    SiapkanBaru();
                    break;
            }
        }

        // =====================================================================
        // Langkah
        // =====================================================================

        public ObservableCollection<LangkahItemViewModel> LangkahDaftar { get; }

        public int Langkah
        {
            get => _langkah;
            private set
            {
                if (SetProperty(ref _langkah, value))
                {
                    OnPropertyChanged(nameof(TampilIdentitas));
                    OnPropertyChanged(nameof(TampilElemen));
                    OnPropertyChanged(nameof(TampilIsi));
                    OnPropertyChanged(nameof(TampilTombolLanjutLangkah));
                    OnPropertyChanged(nameof(TampilLanjutPratinjau));
                    OnPropertyChanged(nameof(JudulHalaman));
                    OnPropertyChanged(nameof(TampilTataLetak));
                    OnPropertyChanged(nameof(TampilPratinjau));
                    OnPropertyChanged(nameof(TampilEditor));
                    OnPropertyChanged(nameof(BisaKembali));
                    OnPropertyChanged(nameof(DiPratinjau));
                    OnPropertyChanged(nameof(TampilTombolLanjut));
                    OnPropertyChanged(nameof(JudulLangkah));
                    OnPropertyChanged(nameof(KeteranganLangkah));
                    OnPropertyChanged(nameof(NomorLangkahTeks));
                    OnPropertyChanged(nameof(TeksTombolLanjut));
                    PerbaruiPenandaLangkah();
                }
            }
        }

        /// <summary>Perbarui penanda langkah aktif/selesai pada daftar langkah.</summary>
        private void PerbaruiPenandaLangkah()
        {
            foreach (var langkah in LangkahDaftar)
            {
                langkah.Aktif = langkah.Nomor - 1 == Langkah;
                langkah.Selesai = langkah.Nomor - 1 < Langkah;
            }
        }

        // =====================================================================
        // Lencana isian per langkah (ringkasan pada navigasi kiri)
        // =====================================================================

        /// <summary>Kolom bagian yang sudah berlangganan perubahan isi (tambah/hapus field).</summary>
        private readonly HashSet<INotifyCollectionChanged> _langgananKolomBagian = new();

        /// <summary>
        /// Perbarui lencana tiap langkah navigasi kiri, mis. "3 teks · 2 kolom",
        /// agar progres isian terlihat tanpa membuka langkahnya.
        /// </summary>
        private void PerbaruiLencana()
        {
            if (LangkahDaftar.Count < 5) return;

            // Langkah 1 — identitas template
            LangkahDaftar[0].Lencana = string.IsNullOrWhiteSpace(_nama) ? "Nama belum diisi" : "Nama terisi";

            // Langkah 2 — jumlah komponen surat yang dicentang
            int komponen = 0;
            if (_pakaiKop) komponen++;
            if (_pakaiJudul) komponen++;
            if (_pakaiNomor) komponen++;
            if (_pakaiTempatTanggal) komponen++;
            if (_pakaiBlok) komponen++;
            if (_pakaiKolom) komponen++;
            if (_pakaiGrid) komponen++;
            if (_pakaiTandaTangan) komponen++;
            if (_pakaiTeksKaki) komponen++;
            LangkahDaftar[1].Lencana = komponen > 0 ? $"{komponen} komponen aktif" : "Tidak ada komponen";

            // Langkah 3 — isi surat (susunan bagian dari atas ke bawah)
            int teks = Bagian.Count(b => b is BagianTeksWizardItemViewModel);
            int dataDiri = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            int kolom = Bagian.Sum(b => b switch
            {
                BagianKolomWizardItemViewModel kelompok => kelompok.Kolom.Count,
                BagianDataDiriWizardItemViewModel orang => orang.Kolom.Count,
                _ => 0
            });
            // Fallback template lama: bila susunan bagian belum memuat, pakai daftar datar cermin.
            if (teks == 0 && Blok.Count > 0) teks = Blok.Count;
            if (kolom == 0 && Kolom.Count > 0) kolom = Kolom.Count;

            var isi = new List<string>(3);
            if (teks > 0) isi.Add($"{teks} teks");
            if (dataDiri > 0) isi.Add($"{dataDiri} data diri");
            if (kolom > 0) isi.Add($"{kolom} kolom");
            LangkahDaftar[2].Lencana = isi.Count > 0 ? string.Join(" · ", isi) : "Kosong";

            // Langkah 4 — tata letak & kaki
            var tata = new List<string>(2);
            if (_pakaiTandaTangan)
            {
                string sumber = _penandatanganDariDataDiri
                    ? "dari Data Diri"
                    : (string.IsNullOrWhiteSpace(_jabatanPenandatangan) ? "Kepala Desa" : _jabatanPenandatangan.Trim());
                tata.Add($"Ttd: {sumber}");
            }
            if (_pakaiTeksKaki) tata.Add("Teks kaki");
            LangkahDaftar[3].Lencana = tata.Count > 0 ? string.Join(" · ", tata) : "Tidak ada";

            // Langkah 5 — pratinjau
            LangkahDaftar[4].Lencana = _pratinjau != null ? "Contoh siap" : "Belum dibuat";
        }

        /// <summary>Bagian bertambah/berkurang: kelola langganan kolomnya, lalu segarkan lencana.</summary>
        private void BagianBerubah(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null) PasangLanggananKolom(e.NewItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null) LepasLanggananKolom(e.OldItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) LepasLanggananKolom(e.OldItems.OfType<BagianWizardItemViewModel>());
                    if (e.NewItems != null) PasangLanggananKolom(e.NewItems.OfType<BagianWizardItemViewModel>());
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var kolom in _langgananKolomBagian)
                    {
                        kolom.CollectionChanged -= KolomBagianBerubah;
                    }
                    _langgananKolomBagian.Clear();
                    PasangLanggananKolom(Bagian);
                    break;
            }

            PerbaruiLencana();
        }

        /// <summary>Kolom di dalam sebuah bagian berubah (tambah/hapus field) → segarkan lencana.</summary>
        private void KolomBagianBerubah(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PerbaruiLencana();
        }

        private void PasangLanggananKolom(IEnumerable<BagianWizardItemViewModel> daftar)
        {
            foreach (var bagian in daftar)
            {
                INotifyCollectionChanged? kolom = bagian switch
                {
                    BagianKolomWizardItemViewModel kelompok => kelompok.Kolom,
                    BagianDataDiriWizardItemViewModel dataDiri => dataDiri.Kolom,
                    _ => null
                };
                if (kolom != null && _langgananKolomBagian.Add(kolom))
                {
                    kolom.CollectionChanged += KolomBagianBerubah;
                }
            }
        }

        private void LepasLanggananKolom(IEnumerable<BagianWizardItemViewModel> daftar)
        {
            foreach (var bagian in daftar)
            {
                INotifyCollectionChanged? kolom = bagian switch
                {
                    BagianKolomWizardItemViewModel kelompok => kelompok.Kolom,
                    BagianDataDiriWizardItemViewModel dataDiri => dataDiri.Kolom,
                    _ => null
                };
                if (kolom != null && _langgananKolomBagian.Remove(kolom))
                {
                    kolom.CollectionChanged -= KolomBagianBerubah;
                }
            }
        }

        public bool TampilIdentitas => Langkah == 0;
        public bool TampilElemen => Langkah == 1;
        public bool TampilIsi => Langkah == 2;
        public bool TampilTataLetak => Langkah == 3;
        public bool TampilPratinjau => Langkah == LangkahTerakhir;

        /// <summary>Tombol "Lanjut" ke langkah berikutnya (bukan menuju pratinjau).</summary>
        public bool TampilTombolLanjutLangkah => Langkah < LangkahTerakhir - 1;

        /// <summary>Tombol "Lanjut ke Pratinjau" hanya di langkah penyuntingan terakhir.</summary>
        public bool TampilLanjutPratinjau => Langkah == LangkahTerakhir - 1;

        /// <summary>Halaman penyusunan (satu halaman): kebalikan dari pratinjau.</summary>
        public bool TampilEditor => Langkah < LangkahTerakhir;

        public bool BisaKembali => Langkah > 0;
        public bool DiPratinjau => Langkah == LangkahTerakhir;

        /// <summary>Tombol "Lanjut" hanya tampil sebelum langkah pratinjau.</summary>
        public bool TampilTombolLanjut => Langkah < LangkahTerakhir;

        /// <summary>Nomor langkah untuk penanda langkah, mis. "Langkah 2 dari 5".</summary>
        public string NomorLangkahTeks => $"Langkah {Langkah + 1} dari {LangkahTerakhir + 1}";

        public string JudulLangkah => LangkahDaftar.ElementAtOrDefault(Langkah)?.Judul ?? string.Empty;
        public string KeteranganLangkah => LangkahDaftar.ElementAtOrDefault(Langkah)?.Keterangan ?? string.Empty;

        /// <summary>
        /// Judul halaman ini di title bar jendela, dibaca MainWindowViewModel lewat
        /// antarmuka <see cref="IJudulHalaman"/> supaya tahap wizard langsung terlihat.
        /// </summary>
        public string JudulHalaman
        {
            get
            {
                if (DiPratinjau)
                {
                    return "Template Surat — Pratinjau";
                }

                string mode = _modeWizard switch
                {
                    TemplateSuratViewModel.ModeEdit => "Ubah Template",
                    TemplateSuratViewModel.ModeDuplikat => "Duplikat Template",
                    _ => "Template Baru"
                };

                return $"Template Surat — {mode} ({NomorLangkahTeks})";
            }
        }

        /// <summary>Label tombol lanjut: menyebut langkah berikutnya supaya jelas tujuannya.</summary>
        public string TeksTombolLanjut => Langkah >= LangkahTerakhir
            ? "Simpan & Isi Surat"
            : $"Lanjut: {LangkahDaftar.ElementAtOrDefault(Langkah + 1)?.Judul ?? "Berikutnya"}";

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePerintah();
                }
            }
        }

        /// <summary>Pesan kesalahan langkah saat ini (kosong bila tidak ada).</summary>
        public string PesanKesalahan
        {
            get => _pesanKesalahan;
            private set
            {
                if (SetProperty(ref _pesanKesalahan, value))
                {
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public bool AdaKesalahan => !string.IsNullOrWhiteSpace(_pesanKesalahan);

        /// <summary>Pesan informasi/netral (mis. hasil pratinjau).</summary>
        public string PesanInformasi
        {
            get => _pesanInformasi;
            private set
            {
                if (SetProperty(ref _pesanInformasi, value))
                {
                    OnPropertyChanged(nameof(AdaInformasi));
                }
            }
        }

        public bool AdaInformasi => !string.IsNullOrWhiteSpace(_pesanInformasi);

        // =====================================================================
        // Data yang diisi pengguna
        // =====================================================================

        public string Nama
        {
            get => _nama;
            set
            {
                if (SetProperty(ref _nama, value))
                {
                    PerbaruiLencana();
                }
            }
        }
        public string Deskripsi { get => _deskripsi; set => SetProperty(ref _deskripsi, value); }

        public bool PakaiKop
        {
            get => _pakaiKop;
            set
            {
                if (SetProperty(ref _pakaiKop, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Menyertakan judul surat (kotak centang komponen).</summary>
        public bool PakaiJudul
        {
            get => _pakaiJudul;
            set
            {
                if (SetProperty(ref _pakaiJudul, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string Judul { get => _judul; set => SetProperty(ref _judul, value); }
        public string SubJudul { get => _subJudul; set => SetProperty(ref _subJudul, value); }

        /// <summary>Menyertakan blok teks bebas pada surat (kotak centang komponen).</summary>
        public bool PakaiBlok
        {
            get => _pakaiBlok;
            set
            {
                if (SetProperty(ref _pakaiBlok, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Menyertakan kolom isian pada surat (kotak centang komponen).</summary>
        public bool PakaiKolom
        {
            get => _pakaiKolom;
            set
            {
                if (SetProperty(ref _pakaiKolom, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiNomor
        {
            get => _pakaiNomor;
            set
            {
                if (SetProperty(ref _pakaiNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                    PerbaruiLencana();
                }
            }
        }

        public string AwalanNomor
        {
            get => _awalanNomor;
            set
            {
                if (SetProperty(ref _awalanNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        public string PolaNomor
        {
            get => _polaNomor;
            set
            {
                if (SetProperty(ref _polaNomor, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        public bool PakaiTempatTanggal
        {
            get => _pakaiTempatTanggal;
            set
            {
                if (SetProperty(ref _pakaiTempatTanggal, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiGrid
        {
            get => _pakaiGrid;
            set
            {
                if (SetProperty(ref _pakaiGrid, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiTandaTangan
        {
            get => _pakaiTandaTangan;
            set
            {
                if (SetProperty(ref _pakaiTandaTangan, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string JabatanPenandatangan
        {
            get => _jabatanPenandatangan;
            set
            {
                if (SetProperty(ref _jabatanPenandatangan, value))
                {
                    PerbaruiLencana();
                }
            }
        }

        public bool PakaiTeksKaki
        {
            get => _pakaiTeksKaki;
            set
            {
                if (SetProperty(ref _pakaiTeksKaki, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        public string TeksKaki { get => _teksKaki; set => SetProperty(ref _teksKaki, value); }

        public ObservableCollection<BlokTeksItemViewModel> Blok { get; }
        public ObservableCollection<KolomItemViewModel> Kolom { get; }

        /// <summary>
        /// Susunan badan surat yang baru: bagian terurut (teks, data diri, kolom isian)
        /// yang menentukan tata letak dari atas ke bawah.
        /// </summary>
        public ObservableCollection<BagianWizardItemViewModel> Bagian { get; }

        /// <summary>Nomor surat diketik manual saat mengisi surat (tanpa hitungan otomatis).</summary>
        public bool NomorManual
        {
            get => _nomorManual;
            set
            {
                if (SetProperty(ref _nomorManual, value))
                {
                    OnPropertyChanged(nameof(ContohNomor));
                }
            }
        }

        /// <summary>Penandatangan memakai salah satu orang dari blok Data Diri (bukan Kepala Desa otomatis).</summary>
        public bool PenandatanganDariDataDiri
        {
            get => _penandatanganDariDataDiri;
            set
            {
                if (SetProperty(ref _penandatanganDariDataDiri, value))
                {
                    OnPropertyChanged(nameof(RingkasanSusunan));
                    PerbaruiLencana();
                }
            }
        }

        /// <summary>Kunci blok Data Diri yang menjadi penandatangan (kosong = Kepala Desa).</summary>
        public string KunciPenandatanganDataDiri { get => _kunciPenandatanganDataDiri; set => SetProperty(ref _kunciPenandatanganDataDiri, value); }

        /// <summary>Daftar blok Data Diri yang bisa dipilih menjadi penandatangan surat.</summary>
        public IReadOnlyList<PenandatanganDataDiriOpsi> DaftarPenandatangan
        {
            get
            {
                var daftar = new List<PenandatanganDataDiriOpsi>();
                int nomor = 0;
                foreach (var bagian in Bagian.OfType<BagianDataDiriWizardItemViewModel>())
                {
                    nomor++;
                    string judul = (bagian.Judul ?? string.Empty).Trim();
                    if (judul.Length == 0)
                    {
                        judul = "Data Diri " + nomor;
                    }
                    daftar.Add(new PenandatanganDataDiriOpsi(bagian.Kunci, judul));
                }
                return daftar;
            }
        }

        /// <summary>Contoh nomor surat dari awalan & pola yang dipilih.</summary>
        public string ContohNomor => TemplateSuratNomor.Bangun(PolaNomor, AwalanNomor, 1, DateTime.Now.Year);

        /// <summary>Ringkasan susunan surat untuk pratinjau & daftar.</summary>
        public string RingkasanSusunan => TemplateDariForm().RingkasanSusunan;

        /// <summary>Pratinjau PDF langkah terakhir.</summary>
        public PdfPreviewViewModel? Pratinjau
        {
            get => _pratinjau;
            private set
            {
                if (SetProperty(ref _pratinjau, value))
                {
                    OnPropertyChanged(nameof(AdaPratinjau));
                    PerbaruiLencana();
                }
            }
        }

        public bool AdaPratinjau => _pratinjau != null;

        // =====================================================================
        // Perintah
        // =====================================================================

        public AsyncRelayCommand LanjutCommand { get; }
        public AsyncRelayCommand KePratinjauCommand { get; }
        public RelayCommand KembaliCommand { get; }
        public RelayCommand<LangkahItemViewModel> PilihLangkahCommand { get; }
        public AsyncRelayCommand SimpanCommand { get; }
        public AsyncRelayCommand SimpanDanIsiCommand { get; }
        public AsyncRelayCommand SegarkanPratinjauCommand { get; }
        public RelayCommand BatalCommand { get; }

        public RelayCommand TambahBlokCommand { get; }
        public RelayCommand TambahBlokUmumCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> HapusBlokCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> NaikBlokCommand { get; }
        public RelayCommand<BlokTeksItemViewModel> TurunBlokCommand { get; }

        public RelayCommand TambahKolomCommand { get; }
        public RelayCommand TambahKolomUmumCommand { get; }
        public RelayCommand<KolomItemViewModel> HapusKolomCommand { get; }
        public RelayCommand<KolomItemViewModel> NaikKolomCommand { get; }
        public RelayCommand<KolomItemViewModel> TurunKolomCommand { get; }

        // ===== Perintah susunan berurutan (bagian) =====

        public RelayCommand TambahBagianTeksCommand { get; }
        public RelayCommand TambahBagianDataDiriCommand { get; }
        public RelayCommand TambahBagianDataDiriRingkasCommand { get; }
        public RelayCommand TambahBagianKolomCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> HapusBagianCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> NaikBagianCommand { get; }
        public RelayCommand<BagianWizardItemViewModel> TurunBagianCommand { get; }

        /// <summary>Tambah satu kolom di dalam bagian kolom isian atau data diri.</summary>
        public RelayCommand<BagianWizardItemViewModel> TambahKolomBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> HapusKolomBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> NaikKolomBagianCommand { get; }
        public RelayCommand<KolomItemViewModel> TurunKolomBagianCommand { get; }

        // =====================================================================
        // Pemuatan data
        // =====================================================================

        /// <summary>Isi wizard untuk template baru, atau salin dari template yang ada (duplikat).</summary>
        public void SiapkanBaru(TemplateSuratKustom? salinan = null)
        {
            _idTersunting = 0;
            if (salinan != null)
            {
                TerapkanKeForm(salinan);
                // Duplikat: nomor mulai dari awal dan namanya diberi penanda.
                _nomorTerakhir = 0;
                _tahunNomor = 0;
                _dibuat = default;
                _dibuatOleh = string.Empty;
                Nama = string.IsNullOrWhiteSpace(Nama) ? "Salinan template" : Nama + " (salinan)";
            }

            Langkah = 0;
            PesanKesalahan = string.Empty;
            PesanInformasi = "Pilih komponen surat dengan kotak centang, lalu tekan Lanjut ke Pratinjau.";
            if (Kolom.Count == 0)
            {
                TambahKolomUmum();
            }
            PerbaruiLencana();
        }

        /// <summary>Muat template yang sudah ada untuk disunting.</summary>
        public async Task MuatAsync(int id)
        {
            try
            {
                var template = await _repository.GetByIdAsync(id);
                if (template == null)
                {
                    await _messageService.ShowWarningAsync("Template tidak ditemukan.");
                    RequestClose?.Invoke();
                    return;
                }

                _idTersunting = template.Id;
                _nomorTerakhir = template.NomorTerakhir;
                _tahunNomor = template.TahunNomor;
                _dibuat = template.Dibuat;
                _dibuatOleh = template.DibuatOleh;
                TerapkanKeForm(template);

                Langkah = 0;
                PesanKesalahan = string.Empty;
                PesanInformasi = "Ubah bagian yang perlu, lalu simpan kembali.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat template surat #{Id}", id);
                await _messageService.ShowErrorAsync($"Gagal memuat template: {ex.Message}");
                RequestClose?.Invoke();
            }
        }

        private void TerapkanKeForm(TemplateSuratKustom template)
        {
            Nama = template.Nama;
            Deskripsi = template.Deskripsi;
            PakaiKop = template.PakaiKop;
            PakaiJudul = !string.IsNullOrWhiteSpace(template.Judul);
            Judul = template.Judul;
            SubJudul = template.SubJudul;
            PakaiNomor = template.PakaiNomor;
            AwalanNomor = template.AwalanNomor;
            PolaNomor = string.IsNullOrWhiteSpace(template.PolaNomor) ? TemplateSuratNomor.PolaBawaan : template.PolaNomor;
            PakaiTempatTanggal = template.PakaiTempatTanggal;
            PakaiGrid = template.PakaiGrid;
            PakaiTandaTangan = template.PakaiTandaTangan;
            JabatanPenandatangan = template.JabatanPenandatangan;
            PakaiTeksKaki = template.PakaiTeksKaki;
            TeksKaki = template.TeksKaki;
            NomorManual = template.NomorManual;
            PenandatanganDariDataDiri = template.PenandatanganDariDataDiri;
            KunciPenandatanganDataDiri = template.KunciPenandatanganDataDiri ?? string.Empty;

            Bagian.Clear();
            if (template.AdaBagian)
            {
                foreach (var bagian in template.Bagian)
                {
                    if (bagian.Tipe == TipeBagianTemplate.Teks)
                    {
                        Bagian.Add(new BagianTeksWizardItemViewModel(new BlokTeksItemViewModel(new BlokTeksTemplateSurat
                        {
                            Isi = bagian.Isi ?? string.Empty,
                            Rata = bagian.Rata,
                            Tebal = bagian.Tebal,
                            Miring = bagian.Miring
                        })));
                    }
                    else if (bagian.Tipe == TipeBagianTemplate.DataDiri)
                    {
                        var dataDiri = new BagianDataDiriWizardItemViewModel(bagian.JudulKelompok ?? string.Empty, bagian.Kunci ?? string.Empty);
                        foreach (var kolom in bagian.Kolom ?? new List<KolomTemplateSurat>())
                        {
                            dataDiri.Kolom.Add(new KolomItemViewModel(kolom));
                        }
                        Bagian.Add(dataDiri);
                    }
                    else
                    {
                        var kelompok = new BagianKolomWizardItemViewModel(bagian.JudulKelompok ?? string.Empty, bagian.Kunci ?? string.Empty)
                        {
                            Grid = bagian.Grid
                        };
                        foreach (var kolom in bagian.Kolom ?? new List<KolomTemplateSurat>())
                        {
                            kelompok.Kolom.Add(new KolomItemViewModel(kolom));
                        }
                        Bagian.Add(kelompok);
                    }
                }
                PerbaruiCermin();
            }

            Blok.Clear();
            foreach (var blok in template.Blok ?? new List<BlokTeksTemplateSurat>())
            {
                Blok.Add(new BlokTeksItemViewModel(blok));
            }
            PakaiBlok = Blok.Count > 0;

            Kolom.Clear();
            foreach (var kolom in template.Kolom ?? new List<KolomTemplateSurat>())
            {
                Kolom.Add(new KolomItemViewModel(kolom));
            }
            PakaiKolom = Kolom.Count > 0;

            if (Bagian.Count == 0)
            {
                // Template lama: susun ulang dari daftar datar supaya tetap bisa disunting
                // sebagai bagian berurutan tanpa mengubah isi.
                Bagian.Clear();
                foreach (var blok in Blok)
                {
                    Bagian.Add(new BagianTeksWizardItemViewModel(blok));
                }
                if (Kolom.Count > 0)
                {
                    Bagian.Add(new BagianKolomWizardItemViewModel(string.Empty, string.Empty) { Grid = template.PakaiGrid }.DenganKolom(Kolom));
                }
                PerbaruiCermin();
            }
        }

        // =====================================================================
        // Daftar blok & kolom
        // =====================================================================

        private void TambahBlok()
        {
            Blok.Add(new BlokTeksItemViewModel());
            PesanInformasi = "Tulis isi blok teks pada kotak yang tersedia.";
        }

        private void TambahBlokUmum()
        {
            foreach (var (isi, rata, miring) in TemplateSuratPreset.BlokUmum)
            {
                Blok.Add(new BlokTeksItemViewModel(new BlokTeksTemplateSurat
                {
                    Isi = isi,
                    Rata = rata,
                    Miring = miring
                }));
            }
            PesanInformasi = "Dua kalimat baku surat keterangan ditambahkan — silakan ubah sesuai kebutuhan.";
        }

        private void HapusBlok(BlokTeksItemViewModel? blok)
        {
            if (blok == null) return;
            Blok.Remove(blok);
        }

        private void TambahKolom()
        {
            Kolom.Add(new KolomItemViewModel());
            PesanInformasi = "Beri nama kolom, pilih tipenya, lalu tandai bila wajib diisi.";
        }

        /// <summary>
        /// Tambahkan kolom identitas yang paling sering dipakai (NIK, nama, alamat,
        /// dan lainnya) tanpa menimpa kolom yang sudah ada.
        /// </summary>
        private void TambahKolomUmum()
        {
            int ditambah = 0;
            foreach (var preset in TemplateSuratPreset.KolomUmum)
            {
                if (Kolom.Any(k => string.Equals((k.Label ?? string.Empty).Trim(), preset.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var kolom = TemplateSuratPreset.BuatKolom(preset, Kolom.Select(k => k.Kunci).Where(k => k.Length > 0));
                Kolom.Add(new KolomItemViewModel(kolom));
                ditambah++;
            }

            PesanInformasi = ditambah == 0
                ? "Seluruh kolom umum sudah ada."
                : $"{ditambah} kolom umum ditambahkan.";
        }

        private void HapusKolom(KolomItemViewModel? kolom)
        {
            if (kolom == null) return;
            Kolom.Remove(kolom);
        }

        private static void Geser<T>(ObservableCollection<T> daftar, T? item, int arah)
        {
            if (item == null) return;
            int posisi = daftar.IndexOf(item);
            int tujuan = posisi + arah;
            if (posisi < 0 || tujuan < 0 || tujuan >= daftar.Count) return;
            daftar.Move(posisi, tujuan);
        }

        // =====================================================================
        // Susunan berurutan (bagian)
        // =====================================================================

        private void TambahBagianTeks()
        {
            var teks = new BlokTeksItemViewModel();
            Bagian.Add(new BagianTeksWizardItemViewModel(teks));
            PerbaruiCermin();
            PesanInformasi = "Blok teks ditambahkan pada urutan terakhir — tulis isinya pada kotak yang tersedia.";
        }

        /// <summary>Bagian Kolom Isian kosong: kolomnya ditambah satu-satu oleh pengguna.</summary>
        private void TambahBagianKolom()
        {
            int urutan = Bagian.Count(b => b is BagianKolomWizardItemViewModel);
            var kelompok = new BagianKolomWizardItemViewModel(string.Empty, $"kolom{urutan}");
            Bagian.Add(kelompok);
            PerbaruiCermin();
            PesanInformasi = "Bagian kolom isian ditambahkan — tambahkan kolom yang harus diisi saat membuat surat.";
        }

        /// <summary>Data diri ringkas: hanya NIK, Nama, Jabatan.</summary>
        private void TambahBagianDataDiriRingkas()
        {
            int urutan = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            var kunci = $"datadiri{urutan}";
            var bagian = new BagianDataDiriWizardItemViewModel($"Data Diri {urutan + 1}", kunci);
            foreach (var preset in TemplateSuratPreset.DataDiri.NamaJabatan)
            {
                bagian.Kolom.Add(new KolomItemViewModel(BuatDataDiriKolom(kunci, preset)));
            }
            Bagian.Add(bagian);
            PerbaruiCermin();
            PesanInformasi = "Blok data diri ditambahkan — pilih orangnya saat mengisi surat.";
        }

        /// <summary>Data diri lengkap (NIK, Nama, Jabatan, TTL, Alamat, dan lainnya).</summary>
        private void TambahBagianDataDiri()
        {
            int urutan = Bagian.Count(b => b is BagianDataDiriWizardItemViewModel);
            var kunci = $"datadiri{urutan}";
            var bagian = new BagianDataDiriWizardItemViewModel($"Data Diri {urutan + 1}", kunci);
            foreach (var preset in TemplateSuratPreset.DataDiri.Lengkap)
            {
                bagian.Kolom.Add(new KolomItemViewModel(BuatDataDiriKolom(kunci, preset)));
            }
            Bagian.Add(bagian);
            PerbaruiCermin();
            PesanInformasi = "Blok data diri lengkap ditambahkan — seluruh field identitas dicetak.";
        }

        private static KolomTemplateSurat BuatDataDiriKolom(string kunci, TemplateSuratPreset.FieldDataDiri preset)
        {
            return new KolomTemplateSurat
            {
                Label = preset.Label,
                Tipe = preset.Tipe,
                Wajib = preset.Wajib,
                Pilihan = preset.Pilihan == null ? new List<string>() : new List<string>(preset.Pilihan),
                Kunci = $"{kunci}_{TemplateSuratKunci.Slug(preset.Label)}",
                Kelompok = "Data Diri"
            };
        }

        private void HapusBagian(BagianWizardItemViewModel? bagian)
        {
            if (bagian == null) return;
            Bagian.Remove(bagian);
            PerbaruiCermin();
        }

        private void TambahKolomBagian(BagianWizardItemViewModel? bagian)
        {
            var kolomItem = new KolomItemViewModel();
            if (bagian is BagianKolomWizardItemViewModel kelompok)
            {
                kelompok.Kolom.Add(kolomItem);
            }
            else if (bagian is BagianDataDiriWizardItemViewModel dataDiri)
            {
                dataDiri.Kolom.Add(kolomItem);
            }
            else
            {
                return;
            }
            PesanInformasi = "Kolom ditambahkan pada bagian ini — beri nama dan tipenya.";
        }

        private void HapusKolomBagian(KolomItemViewModel? kolom)
        {
            if (kolom == null) return;
            var bagian = Bagian
                .FirstOrDefault(b => b is BagianKolomWizardItemViewModel kelompok
                    ? kelompok.Kolom.Contains(kolom)
                    : b is BagianDataDiriWizardItemViewModel dataDiri && dataDiri.Kolom.Contains(kolom));
            switch (bagian)
            {
                case BagianKolomWizardItemViewModel kelompok:
                    kelompok.Kolom.Remove(kolom);
                    break;
                case BagianDataDiriWizardItemViewModel dataDiri:
                    dataDiri.Kolom.Remove(kolom);
                    break;
            }
            PerbaruiCermin();
        }

        private void GeserKolomBagian(KolomItemViewModel? kolom, int arah)
        {
            if (kolom == null) return;
            var bagian = Bagian
                .FirstOrDefault(b => b is BagianKolomWizardItemViewModel kelompok
                    ? kelompok.Kolom.Contains(kolom)
                    : b is BagianDataDiriWizardItemViewModel dataDiri && dataDiri.Kolom.Contains(kolom));
            switch (bagian)
            {
                case BagianKolomWizardItemViewModel kelompok:
                    Geser(kelompok.Kolom, kolom, arah);
                    break;
                case BagianDataDiriWizardItemViewModel dataDiri:
                    Geser(dataDiri.Kolom, kolom, arah);
                    break;
            }
        }

        /// <summary>
        /// Jagalah daftar datar <see cref="Blok"/> dan <see cref="Kolom"/> tetap sejalan
        /// dengan susunan <see cref="Bagian"/> supaya alur lama (cetak, isian, ringkasan)
        /// tetap bekerja tanpa membaca bagian satuan.
        /// </summary>
        private void PerbaruiCermin()
        {
            Blok.Clear();
            foreach (var bagian in Bagian.OfType<BagianTeksWizardItemViewModel>())
            {
                Blok.Add(bagian.Teks);
            }

            Kolom.Clear();
            foreach (var kolom in Bagian
                .OfType<BagianKolomWizardItemViewModel>()
                .SelectMany(b => b.Kolom))
            {
                Kolom.Add(kolom);
            }

            OnPropertyChanged(nameof(DaftarPenandatangan));
            PerbaruiLencana();
        }

        // =====================================================================
        // Navigasi langkah
        // =====================================================================

        /// <summary>
        /// Pindah langkah dari penanda langkah (stepper). Mundur selalu boleh; maju
        /// memeriksa isi langkah sekarang lebih dulu agar galat tetap terlihat pada
        /// halaman yang salah. Mengklik langkah "Pratinjau" memeriksa semua langkah.
        /// </summary>
        private void PilihLangkah(LangkahItemViewModel? item)
        {
            if (item == null) return;
            int tujuan = item.Nomor - 1;

            if (tujuan == LangkahTerakhir)
            {
                _ = KePratinjauAsync();
                return;
            }

            if (tujuan > Langkah && !PeriksaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;
            Langkah = tujuan;
        }

        private void Kembali()
        {
            if (Langkah == 0) return;
            PesanKesalahan = string.Empty;
            Langkah--;
        }

        /// <summary>
        /// Validasi satu langkah. Sejak halaman dijadikan satu layar penyusunan,
        /// tombol "Lanjut ke Pratinjau" memvalidasi seluruh langkah sekaligus.
        /// </summary>
        private bool PeriksaSemuaLangkah(out string pesan)
        {
            int langkahSemula = Langkah;
            for (int i = 0; i < LangkahTerakhir; i++)
            {
                Langkah = i;
                if (!PeriksaLangkah(out pesan))
                {
                    Langkah = langkahSemula;
                    return false;
                }
            }
            Langkah = langkahSemula;
            pesan = string.Empty;
            return true;
        }

        private async Task KePratinjauAsync()
        {
            if (!PeriksaSemuaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;
            Langkah = LangkahTerakhir;
            await SegarkanPratinjauAsync();
        }

        private async Task LanjutAsync()
        {
            if (!PeriksaLangkah(out var pesan))
            {
                PesanKesalahan = pesan;
                return;
            }

            PesanKesalahan = string.Empty;

            if (Langkah < LangkahTerakhir)
            {
                Langkah++;
                if (DiPratinjau)
                {
                    await SegarkanPratinjauAsync();
                }
                return;
            }

            // Di langkah pratinjau: simpan lalu lanjut ke pengisian surat.
            await SimpanAsync(lanjutIsi: true);
        }

        /// <summary>Periksa isi langkah saat ini sebelum berpindah.</summary>
        private bool PeriksaLangkah(out string pesan)
        {
            pesan = string.Empty;

            switch (Langkah)
            {
                case 0:
                    if (string.IsNullOrWhiteSpace(Nama))
                    {
                        pesan = "Nama template wajib diisi — nama inilah yang tampil pada daftar Template Surat.";
                        return false;
                    }
                    break;

                case 1:
                    if (!PakaiKop && !PakaiJudul && !PakaiNomor && !PakaiTempatTanggal)
                    {
                        pesan = "Pilih minimal satu komponen: kop, judul, nomor surat, atau tempat & tanggal.";
                        return false;
                    }
                    if (PakaiNomor)
                    {
                        if (!TemplateSuratNomor.AwalanValid(AwalanNomor, out var pesanAwalan))
                        {
                            pesan = pesanAwalan;
                            return false;
                        }
                        if (!TemplateSuratNomor.PolaValid(PolaNomor, out var pesanPola))
                        {
                            pesan = pesanPola;
                            return false;
                        }
                    }
                    break;

                // Langkah "Isi Surat": blok teks DAN kolom isian diperiksa sekaligus karena
                // keduanya disusun pada satu halaman yang sama.
                case 2:
                    if (PakaiBlok)
                    {
                        if (Bagian.Count > 0)
                        {
                            int nomorTeks = 0;
                            foreach (var bagian in Bagian.OfType<BagianTeksWizardItemViewModel>())
                            {
                                nomorTeks++;
                                if (string.IsNullOrWhiteSpace(bagian.Teks.Isi))
                                {
                                    pesan = $"Blok teks ke-{nomorTeks} masih kosong. Isi teksnya atau hapus blok tersebut.";
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < Blok.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(Blok[i].Isi))
                                {
                                    pesan = $"Blok teks ke-{i + 1} masih kosong. Isi teksnya atau hapus blok tersebut.";
                                    return false;
                                }
                            }
                        }
                    }

                    if (!PakaiKolom) break;
                    if (Bagian.Count > 0)
                    {
                        if (!Bagian.OfType<BagianKolomWizardItemViewModel>().Any() &&
                            !Bagian.OfType<BagianDataDiriWizardItemViewModel>().Any())
                        {
                            pesan = "Tambahkan bagian Kolom Isian atau Data Diri agar surat bisa diisi saat dibuat.";
                            return false;
                        }

                        int nomorBagian = 0;
                        foreach (var bagian in Bagian)
                        {
                            if (bagian is not BagianKolomWizardItemViewModel kelompok &&
                                bagian is not BagianDataDiriWizardItemViewModel)
                            {
                                continue;
                            }

                            nomorBagian++;
                            var daftar = bagian is BagianKolomWizardItemViewModel kb
                                ? kb.Kolom
                                : ((BagianDataDiriWizardItemViewModel)bagian).Kolom;

                            for (int i = 0; i < daftar.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(daftar[i].Label))
                                {
                                    pesan = $"Bagian {nomorBagian}: kolom ke-{i + 1} belum diberi nama.";
                                    return false;
                                }
                                if (daftar[i].PakaiPilihan && daftar[i].PilihanBersih().Count == 0)
                                {
                                    pesan = $"Bagian {nomorBagian}: kolom \"{daftar[i].Label}\" bertipe Pilihan, tetapi daftar pilihannya belum diisi (pisahkan dengan koma).";
                                    return false;
                                }
                            }

                            // Label boleh kembar antar-bagian, tetapi tidak boleh dalam satu kelompok.
                            var kembar = daftar
                                .GroupBy(k => (k.Label ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                                .FirstOrDefault(g => g.Count() > 1);
                            if (kembar != null)
                            {
                                pesan = $"Bagian {nomorBagian}: nama kolom \"{kembar.Key}\" dipakai lebih dari satu kali di bagian ini.";
                                return false;
                            }
                        }
                        break;
                    }

                    if (Kolom.Count == 0)
                    {
                        pesan = "Tambahkan minimal satu kolom isian, misalnya NIK atau Nama.";
                        return false;
                    }
                    for (int i = 0; i < Kolom.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(Kolom[i].Label))
                        {
                            pesan = $"Kolom ke-{i + 1} belum diberi nama.";
                            return false;
                        }
                        if (Kolom[i].PakaiPilihan && Kolom[i].PilihanBersih().Count == 0)
                        {
                            pesan = $"Kolom \"{Kolom[i].Label}\" bertipe Pilihan, tetapi daftar pilihannya belum diisi (pisahkan dengan koma).";
                            return false;
                        }
                    }

                    var kembarFlat = Kolom
                        .GroupBy(k => (k.Label ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(g => g.Count() > 1);
                    if (kembarFlat != null)
                    {
                        pesan = $"Nama kolom \"{kembarFlat.Key}\" dipakai lebih dari satu kali. Samakan hanya untuk keperluan berbeda.";
                        return false;
                    }
                    break;

                case 3:
                    if (!PakaiTandaTangan && !PakaiKop && !PakaiJudul)
                    {
                        pesan = "Surat harus memiliki kop, judul, atau tanda tangan agar tetap dikenali sebagai surat resmi.";
                        return false;
                    }
                    if (PakaiTeksKaki && string.IsNullOrWhiteSpace(TeksKaki))
                    {
                        pesan = "Teks kaki sudah dipilih, tetapi isinya masih kosong.";
                        return false;
                    }
                    break;
            }

            var susunan = TemplateDariForm();
            if (susunan.JumlahElemen == 0)
            {
                pesan = "Belum ada elemen surat yang dipilih.";
                return false;
            }

            return true;
        }

        // =====================================================================
        // Pratinjau
        // =====================================================================

        private async Task SegarkanPratinjauAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var template = TemplateDariForm();
                if (template.JumlahElemen == 0)
                {
                    PesanKesalahan = "Belum ada elemen surat yang bisa dipratinjau.";
                    return;
                }

                var nilaiContoh = TemplateSuratNilai.NilaiContoh(template);
                var pdf = await _generator.BuatPdfAsync(
                    template, nilaiContoh, ContohNomor, DateTime.Now);

                if (pdf.Length == 0)
                {
                    PesanKesalahan = "Pratinjau gagal dibuat: berkas PDF kosong.";
                    return;
                }

                string berkas = Path.Combine(
                    Path.GetTempPath(), "SuDesApp",
                    $"pratinjau-template-{Guid.NewGuid():N}.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(berkas));
                await File.WriteAllBytesAsync(berkas, pdf);

                Pratinjau = _previewFactory($"Pratinjau — {template.NamaTampil}", berkas, Kembali);
                BersihkanBerkasPratinjauLama(berkas);
                _berkasPratinjau = berkas;

                PesanKesalahan = string.Empty;
                PesanInformasi = "Periksa contoh suratnya. Kurang sesuatu? Tekan Kembali untuk menyunting komponennya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau template surat");
                PesanKesalahan = "Gagal membuat pratinjau: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Hapus berkas pratinjau sebelumnya (best-effort; gagal diabaikan).</summary>
        private void BersihkanBerkasPratinjauLama(string berkasBaru)
        {
            try
            {
                string? lama = _berkasPratinjau;
                if (string.IsNullOrEmpty(lama) || lama == berkasBaru) return;

                var info = new FileInfo(lama);
                if (info.Exists)
                {
                    info.Delete();
                }
            }
            catch
            {
                // Pratinjau lama mungkin sedang dibuka penampil PDF; biarkan saja.
            }
        }

        // =====================================================================
        // Simpan
        // =====================================================================

        private async Task SimpanAsync(bool lanjutIsi)
        {
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(Nama))
            {
                Langkah = 0;
                PesanKesalahan = "Nama template wajib diisi.";
                return;
            }

            var template = TemplateDariForm();

            // Periksa seluruh isian sekaligus sebelum menyimpan.
            if (!PeriksaSemuaLangkah(out var pesanValidasi))
            {
                PesanKesalahan = pesanValidasi;
                return;
            }

            IsBusy = true;
            try
            {
                if (_idTersunting > 0)
                {
                    template.Id = _idTersunting;
                    template.NomorTerakhir = _nomorTerakhir;
                    template.TahunNomor = _tahunNomor;
                    template.Dibuat = _dibuat;
                    template.DibuatOleh = _dibuatOleh;
                    await _repository.UpdateAsync(template);
                }
                else
                {
                    template.Dibuat = DateTime.Now;
                    await _repository.AddAsync(template);
                    _idTersunting = template.Id;
                }

                _logger.LogInformation("Template surat disimpan: #{Id} {Nama}", template.Id, template.NamaTampil);
                Selesai?.Invoke(template, lanjutIsi);
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan template surat");
                PesanKesalahan = "Gagal menyimpan template: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Susun definisi template dari isian wizard.</summary>
        private TemplateSuratKustom TemplateDariForm()
        {
            var kunciTerpakai = new List<string>();
            var blok = _pakaiBlok
                ? Blok.Select(b => b.KeModel()).Where(b => b.Isi.Length > 0).ToList()
                : new List<BlokTeksTemplateSurat>();

            var kolom = new List<KolomTemplateSurat>();
            var bagian = new List<BagianTemplateSurat>();

            if (Bagian.Count > 0)
            {
                // Susunan baru: badan surat dari bagian terurut (teks / data diri / kolom).
                foreach (var item in Bagian)
                {
                    if (item is BagianTeksWizardItemViewModel teks)
                    {
                        var model = teks.Teks.KeModel();
                        if (model.Isi.Length == 0) continue;

                        blok.Add(model);
                        bagian.Add(new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.Teks,
                            Isi = model.Isi,
                            Rata = model.Rata,
                            Tebal = model.Tebal,
                            Miring = model.Miring
                        });
                    }
                    else if (item is BagianDataDiriWizardItemViewModel dataDiri)
                    {
                        var kelompok = new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.DataDiri,
                            JudulKelompok = (dataDiri.Judul ?? string.Empty).Trim(),
                            Kunci = dataDiri.Kunci
                        };
                        foreach (var kolomItem in dataDiri.Kolom)
                        {
                            var model = kolomItem.KeModel(kunciTerpakai);
                            if (string.IsNullOrWhiteSpace(model.Label)) continue;

                            kunciTerpakai.Add(model.Kunci);
                            model.Kelompok = kelompok.JudulKelompok;
                            kelompok.Kolom.Add(model);
                            kolom.Add(model);
                        }
                        if (kelompok.Kolom.Count > 0)
                        {
                            bagian.Add(kelompok);
                        }
                    }
                    else if (item is BagianKolomWizardItemViewModel kelompokWizard)
                    {
                        var kelompok = new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.Kolom,
                            JudulKelompok = (kelompokWizard.Judul ?? string.Empty).Trim(),
                            Kunci = kelompokWizard.Kunci,
                            Grid = kelompokWizard.Grid
                        };
                        foreach (var kolomItem in kelompokWizard.Kolom)
                        {
                            var model = kolomItem.KeModel(kunciTerpakai);
                            if (string.IsNullOrWhiteSpace(model.Label)) continue;

                            kunciTerpakai.Add(model.Kunci);
                            model.Kelompok = kelompok.JudulKelompok;
                            kelompok.Kolom.Add(model);
                            kolom.Add(model);
                        }
                        if (kelompok.Kolom.Count > 0)
                        {
                            bagian.Add(kelompok);
                        }
                    }
                }

                // Daftar datar "umum" yang diisi SiapkanBaru TIDAK ikut tercetak pada
                // susunan baru — kolom hanyalah yang ada di dalam bagian.
            }
            else
            {
                // Template lama tanpa Bagian: pertahankan persis perilaku semula.
                foreach (var item in Kolom)
                {
                    if (!_pakaiKolom) break;
                    var model = item.KeModel(kunciTerpakai);
                    kunciTerpakai.Add(model.Kunci);
                    kolom.Add(model);
                }
            }

            return new TemplateSuratKustom
            {
                Id = _idTersunting,
                Nama = (_nama ?? string.Empty).Trim(),
                Deskripsi = (_deskripsi ?? string.Empty).Trim(),
                PakaiKop = _pakaiKop,
                Judul = _pakaiJudul ? (_judul ?? string.Empty).Trim() : string.Empty,
                SubJudul = _pakaiJudul ? (_subJudul ?? string.Empty).Trim() : string.Empty,
                PakaiNomor = _pakaiNomor,
                AwalanNomor = (_awalanNomor ?? string.Empty).Trim(),
                PolaNomor = string.IsNullOrWhiteSpace(_polaNomor) ? TemplateSuratNomor.PolaBawaan : _polaNomor.Trim(),
                PakaiTempatTanggal = _pakaiTempatTanggal,
                Blok = blok,
                Kolom = kolom,
                Bagian = bagian,
                PakaiGrid = _pakaiGrid,
                PakaiTandaTangan = _pakaiTandaTangan,
                JabatanPenandatangan = (_jabatanPenandatangan ?? string.Empty).Trim(),
                PakaiTeksKaki = _pakaiTeksKaki,
                TeksKaki = (_teksKaki ?? string.Empty).Trim(),
                NomorManual = _nomorManual,
                PenandatanganDariDataDiri = _penandatanganDariDataDiri,
                KunciPenandatanganDataDiri = (_kunciPenandatanganDataDiri ?? string.Empty).Trim(),
                NomorTerakhir = _nomorTerakhir,
                TahunNomor = _tahunNomor,
                Dibuat = _dibuat,
                DibuatOleh = _dibuatOleh
            };
        }

        /// <summary>Perbarui ketersediaan tombol setelah perubahan status.</summary>
        private void RaisePerintah()
        {
            LanjutCommand.RaiseCanExecuteChanged();
            KePratinjauCommand.RaiseCanExecuteChanged();
            SimpanCommand.RaiseCanExecuteChanged();
            SimpanDanIsiCommand.RaiseCanExecuteChanged();
            SegarkanPratinjauCommand.RaiseCanExecuteChanged();
            KembaliCommand.RaiseCanExecuteChanged();
            ((RelayCommand)TambahBlokCommand).RaiseCanExecuteChanged();
        }
    }
}
