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
    public class TemplateSuratViewModel : ObservableObject
    {
        /// <summary>Mode pembukaan wizard.</summary>
        public const int ModeBaru = 0;
        public const int ModeEdit = 1;
        public const int ModeDuplikat = 2;

        /// <summary>Buat template baru dari hasil pembacaan berkas Word (bukan lewat wizard dari nol).</summary>
        public const int ModeImpor = 3;

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

        /// <summary>
        /// Kabar halaman yang tampil di dalam daftar template (bukan dialog popup): hasil
        /// pembacaan berkas Word, sebab sebuah tombol tidak bisa dilanjutkan, dan kegagalan
        /// daftar/pratinjau. Kartu berkunci sama saling menimpa supaya tidak menumpuk.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        /// <summary>Contoh bawaan hanya disiapkan sekali per pemakaian halaman.</summary>
        private bool _sudahSiapkanBawaan;

        private string _pencarian = string.Empty;
        private TemplateSuratKustom? _terpilih;
        private bool _isBusy;
        private string _pesanKosong = string.Empty;
        private string _infoBawaan = string.Empty;

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
            ILogger<TemplateSuratViewModel> logger)
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

            Items = new ObservableCollection<TemplateSuratKustom>();

            BuatBaruCommand = new RelayCommand(BukaWizardBaru, () => !IsBusy);
            EditCommand = new RelayCommand(BukaWizardEdit, () => !IsBusy && Terpilih != null);
            DuplikatCommand = new RelayCommand(BukaWizardDuplikat, () => !IsBusy && Terpilih != null);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => !IsBusy && Terpilih != null);
            IsiCommand = new AsyncRelayCommand(IsiAsync, () => !IsBusy && Terpilih != null);
            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy && Terpilih != null);
            SegarkanCommand = new AsyncRelayCommand(MuatAsync, () => !IsBusy);
            BuatDariWordCommand = new AsyncRelayCommand(BuatDariWordAsync, () => !IsBusy);
            BersihkanPencarianCommand = new RelayCommand(() => Pencarian = string.Empty);

            _ = MuatAsync();
        }

        // ===== Data =====

        public ObservableCollection<TemplateSuratKustom> Items { get; }

        public string Pencarian
        {
            get => _pencarian;
            set
            {
                if (SetProperty(ref _pencarian, value))
                {
                    TerapkanFilter();
                }
            }
        }

        public TemplateSuratKustom? Terpilih
        {
            get => _terpilih;
            set
            {
                if (SetProperty(ref _terpilih, value))
                {
                    OnPropertyChanged(nameof(AdaPilihan));
                    RaisePerintah();
                }
            }
        }

        public bool AdaPilihan => _terpilih != null;

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

        // ===== Ringkasan =====

        public int JumlahTotal => _semua.Count;
        public int JumlahTampil => Items.Count;
        public int JumlahBerkolom => _semua.Count(t => t.AdaKolom);
        public int JumlahBerkop => _semua.Count(t => t.PakaiKop);
        public string RingkasanTampil => $"Menampilkan {Items.Count} dari {_semua.Count} template";
        public string RingkasanBerkolom => $"{JumlahBerkolom} dari {_semua.Count} siap diisi";
        public string RingkasanBerkop => $"{JumlahBerkop} dari {_semua.Count} memakai kop";

        /// <summary>Pesan pada daftar ketika belum ada data yang cocok.</summary>
        public string PesanKosong
        {
            get => _pesanKosong;
            private set
            {
                if (SetProperty(ref _pesanKosong, value))
                {
                    OnPropertyChanged(nameof(AdaPesanKosong));
                }
            }
        }

        /// <summary>True bila daftar kosong/tidak ada hasil pencarian (pesan ditampilkan).</summary>
        public bool AdaPesanKosong => !string.IsNullOrWhiteSpace(_pesanKosong);

        // ===== Perintah =====

        public RelayCommand BuatBaruCommand { get; }
        public RelayCommand EditCommand { get; }
        public RelayCommand DuplikatCommand { get; }
        public AsyncRelayCommand HapusCommand { get; }
        public AsyncRelayCommand IsiCommand { get; }
        public AsyncRelayCommand PratinjauCommand { get; }
        public AsyncRelayCommand SegarkanCommand { get; }

        /// <summary>Susun template baru dari berkas Word (.docx) yang sudah dipakai.</summary>
        public AsyncRelayCommand BuatDariWordCommand { get; }

        public RelayCommand BersihkanPencarianCommand { get; }

        /// <summary>Keterangan singkat setelah contoh bawaan disiapkan otomatis.</summary>
        public string InfoBawaan
        {
            get => _infoBawaan;
            private set
            {
                if (SetProperty(ref _infoBawaan, value))
                {
                    OnPropertyChanged(nameof(AdaInfoBawaan));
                }
            }
        }

        public bool AdaInfoBawaan => !string.IsNullOrWhiteSpace(_infoBawaan);

        // ===== Muat data =====

        /// <summary>Muat ulang seluruh template dari database.</summary>
        public async Task MuatAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // Sekali saja: bila daftar masih benar-benar kosong, contoh siap pakai
                // (pengantar RT/RW, izin keramaian, keterangan penghasilan, dan lainnya)
                // dipasang supaya pengguna bisa langsung mencetak tanpa menyusun dulu.
                await SiapkanContohBawaanAsync();

                var daftar = await _repository.GetAllAsync();

                _semua.Clear();
                _semua.AddRange(daftar);

                TerapkanFilter();

                // Pilihan sebelumnya dipertahankan bila template-nya masih ada.
                if (_terpilih != null)
                {
                    Terpilih = Items.FirstOrDefault(t => t.Id == _terpilih.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar template surat");
                PesanKosong = "Daftar template tidak dapat dimuat: " + ex.Message;
                Pesan.Galat("Daftar template tidak dapat dimuat",
                    "Coba buka halaman ini lagi sebentar lagi. Penyebabnya: " + ex.Message,
                    "muat-template");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void TerapkanFilter()
        {
            string cari = (_pencarian ?? string.Empty).Trim();

            var hasil = string.IsNullOrEmpty(cari)
                ? _semua
                : _semua.Where(t => Cocok(t, cari)).ToList();

            Items.Clear();
            foreach (var template in hasil)
            {
                Items.Add(template);
            }

            PesanKosong = _semua.Count == 0
                ? "Belum ada template surat. Tekan \"Buat Template Baru\" untuk membuat jenis surat sendiri."
                : (Items.Count == 0 ? $"Tidak ada template yang cocok dengan \"{cari}\"." : string.Empty);

            OnPropertyChanged(nameof(JumlahTotal));
            OnPropertyChanged(nameof(JumlahTampil));
            OnPropertyChanged(nameof(JumlahBerkolom));
            OnPropertyChanged(nameof(JumlahBerkop));
            OnPropertyChanged(nameof(RingkasanTampil));
            OnPropertyChanged(nameof(RingkasanBerkolom));
            OnPropertyChanged(nameof(RingkasanBerkop));
        }

        private static bool Cocok(TemplateSuratKustom template, string cari)
        {
            bool CocokDengan(string? teks) => !string.IsNullOrEmpty(teks) &&
                teks.Contains(cari, StringComparison.OrdinalIgnoreCase);

            return CocokDengan(template.Nama)
                || CocokDengan(template.Judul)
                || CocokDengan(template.Deskripsi)
                || CocokDengan(template.AwalanNomor)
                || CocokDengan(template.RingkasanSusunan);
        }

        // ===== Aksi =====

        /// <summary>Buka wizard: baru, menyunting yang terpilih, atau menyalinnya.</summary>
        private void BukaWizard(int mode) =>
            BukaWizardDenganTemplate(mode, mode == ModeBaru ? null : Terpilih);

        /// <summary>
        /// Tampilkan wizard penyusunan template di area konten menu utama (bukan jendela
        /// terpisah). Selesai menyimpan atau batal akan mengembalikan pengguna ke halaman
        /// daftar Template Surat.
        /// </summary>
        private void BukaWizardDenganTemplate(int mode, TemplateSuratKustom? template)
        {
            try
            {
                var wizard = _wizardFactory(mode, template);

                // Bila pengguna memilih "Simpan & Isi Surat", navigasi diambil alih oleh
                // formulir pengisian — halaman wizard tidak dikembalikan ke daftar agar
                // tidak menimpa navigasi itu.
                var lanjutKePengisian = false;

                wizard.Selesai += (hasil, lanjutIsi) =>
                {
                    _ = MuatAsync();

                    if (lanjutIsi && hasil != null)
                    {
                        lanjutKePengisian = true;
                        _ = BukaPengisianAsync(hasil);
                    }
                };
                wizard.RequestClose += () =>
                {
                    if (!lanjutKePengisian)
                    {
                        _navigation.Navigate(this);
                    }
                };

                _navigation.Navigate(wizard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka wizard template surat (mode {Mode})", mode);
                Pesan.Galat("Gagal membuka wizard Template Surat",
                    "Penyebabnya: " + ex.Message, "wizard-template");
            }
        }

        private void BukaWizardBaru() => BukaWizard(ModeBaru);
        private void BukaWizardEdit() => BukaWizard(ModeEdit);
        private void BukaWizardDuplikat() => BukaWizard(ModeDuplikat);

        private async Task HapusAsync()
        {
            if (Terpilih == null) return;

            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Hapus Template",
                $"Hapus template \"{Terpilih.NamaTampil}\"? Surat yang sudah pernah dicetak tidak terpengaruh, " +
                "tetapi template ini tidak bisa dikembalikan.");

            if (!lanjut) return;

            try
            {
                int id = Terpilih.Id;
                await _repository.DeleteAsync(id);
                Terpilih = null;
                await MuatAsync();
                _logger.LogInformation("Template surat #{Id} dihapus", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus template surat");
                Pesan.Galat("Gagal menghapus template",
                    "Template belum terhapus. Penyebabnya: " + ex.Message, "hapus-template");
            }
        }

        /// <summary>Buka formulir pengisian surat memakai template terpilih.</summary>
        private Task IsiAsync() => Terpilih == null ? Task.CompletedTask : BukaPengisianAsync(Terpilih);

        /// <summary>
        /// Tampilkan formulir pengisian untuk satu template. Tombol Batal pada
        /// formulir mengembalikan pengguna ke daftar template ini (bukan ke halaman kosong).
        /// </summary>
        private async Task BukaPengisianAsync(TemplateSuratKustom template)
        {
            try
            {
                var vm = _isiFactory();
                vm.SebelumBatal = () =>
                {
                    _ = MuatAsync();
                    _navigation.Navigate(this);
                };
                await vm.ConfigureAsync(template);
                _navigation.Navigate(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pengisian surat dari template");
                Pesan.Galat("Gagal membuka pengisian surat",
                    "Formulir isian template tidak dapat dibuka. Penyebabnya: " + ex.Message,
                    "isi-template");
            }
        }

        /// <summary>
        /// Buat contoh surat (memakai isi contoh) dari template terpilih supaya susunan
        /// surat dapat diperiksa sebelum benar-benar dipakai.
        /// </summary>
        private async Task PratinjauAsync()
        {
            if (Terpilih == null) return;
            try
            {
                var template = await _repository.GetByIdAsync(Terpilih.Id) ?? Terpilih;
                if (template.JumlahElemen == 0)
                {
                    Pesan.Peringatan("Template belum bisa dicetak",
                        "Template ini belum memiliki elemen yang bisa dicetak. Tambahkan elemennya lewat Edit dulu, ya.",
                        "pratinjau-template");
                    return;
                }

                var nilaiContoh = TemplateSuratNilai.NilaiContoh(template);
                int urut;
                string nomor = template.NomorBerikutnya(DateTime.Now, out urut);
                var pdf = await _generator.BuatPdfAsync(template, nilaiContoh, nomor, DateTime.Now);

                if (pdf.Length == 0)
                {
                    Pesan.Peringatan("Pratinjau gagal dibuat",
                        "Berkas PDF-nya kosong, jadi pratinjau tidak dibuka.", "pratinjau-template");
                    return;
                }

                string berkas = Path.Combine(Path.GetTempPath(), "SuDesApp",
                    $"pratinjau-template-{Guid.NewGuid():N}.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(berkas)!);
                await File.WriteAllBytesAsync(berkas, pdf);

                _navigation.Navigate(_previewFactory($"Pratinjau template — {template.NamaTampil}", berkas));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau template surat");
                Pesan.Galat("Gagal membuat pratinjau template",
                    "Penyebabnya: " + ex.Message, "pratinjau-template");
            }
        }

        // =====================================================================
        // Buat template dari berkas Word
        // =====================================================================

        /// <summary>
        /// Buat template surat dari berkas Word yang sudah dipakai sehari-hari: .docx
        /// langsung dibaca, berkas .doc lama dikonversi otomatis lebih dulu (Microsoft Word).
        ///
        /// Berkas yang memuat satu bentuk surat langsung dibuka di wizard (mode impor)
        /// supaya susunannya bisa diperiksa sebelum disimpan. Berkas yang memuat beberapa
        /// bentuk surat (beberapa halaman/bagian) menampilkan dialog pemilihan, sehingga
        /// bagian yang dipilih dapat disimpan sekaligus sebagai beberapa template.
        /// </summary>
        private async Task BuatDariWordAsync()
        {
            if (IsBusy) return;

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pilih berkas Word untuk dijadikan template surat",
                    Filter = "Dokumen Word|*.docx;*.doc|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|Semua Berkas (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog() != true) return;

                IsBusy = true;
                var hasil = await _wordImpor.BacaAsync(dialog.FileName);

                // Pembacaan selesai: halaman bebas lagi supaya daftar bisa dimuat ulang
                // setelah template hasil pembacaan disimpan.
                IsBusy = false;

                if (hasil.Kandidat.Count == 0)
                {
                    string catatan = hasil.Peringatan.Count == 0
                        ? string.Empty
                        : "\n\n" + string.Join("\n", hasil.Peringatan);
                    Pesan.Peringatan("Tidak ada surat yang bisa dibaca",
                        "Pastikan isi berkas berupa teks, bukan gambar atau hasil pindai." + catatan,
                        "impor-word");
                    return;
                }

                var namaTerpakai = _semua.Select(t => t.NamaTampil).ToList();

                // Satu surat saja: buka wizard supaya pengguna bisa memeriksa dan menyimpan.
                if (hasil.Kandidat.Count == 1)
                {
                    var kandidat = hasil.Kandidat[0];
                    kandidat.Definisi.Nama = TemplateSuratWordImporService.NamaUnik(
                        kandidat.Definisi.Nama, namaTerpakai);
                    BukaWizardDenganTemplate(ModeImpor, kandidat.Definisi);
                    return;
                }

                // Beberapa surat dalam satu berkas: pengguna memilih bagian yang disimpan.
                // Setiap bagian bisa dipratinjau (halaman pertama) sebelum diputuskan.
                var terpilih = WordImporWindow.Show(
                    hasil, namaTerpakai, template => _pratinjauService.BuatAsync(template));
                if (terpilih == null || terpilih.Count == 0) return;

                int berhasil = 0;
                var gagal = new List<string>();
                foreach (var (nama, template) in terpilih)
                {
                    try
                    {
                        template.Dibuat = DateTime.Now;
                        await _repository.AddAsync(template);
                        berhasil++;
                        _logger.LogInformation("Template surat dari berkas Word dibuat: {Nama}", nama);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyimpan template hasil pembacaan berkas Word: {Nama}", nama);
                        gagal.Add($"{nama}: {ex.Message}");
                    }
                }

                await MuatAsync();

                if (gagal.Count > 0)
                {
                    Pesan.Peringatan("Sebagian template gagal disimpan",
                        $"{berhasil} template dibuat, {gagal.Count} gagal disimpan:\n\n" + string.Join("\n", gagal),
                        "impor-word");
                    return;
                }

                Pesan.Sukses("Template dari berkas Word dibuat",
                    $"{berhasil} template surat dibuat dari berkas \"{hasil.NamaBerkas}\".\n\n" +
                    "Pilih salah satu pada daftar: tekan Edit untuk menyesuaikan susunannya, atau " +
                    "Isi Surat untuk langsung mencetak.",
                    "impor-word");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat template surat dari berkas Word");
                Pesan.Galat("Gagal membaca berkas Word",
                    "Pastikan berkasnya berformat .doc/.docx, tidak rusak, dan tidak sedang dibuka di " +
                    "aplikasi lain.\n\n" + ex.Message,
                    "impor-word");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // =====================================================================
        // Contoh template siap pakai
        // =====================================================================

        /// <summary>
        /// Sekali per pemakaian halaman: pasang contoh bawaan bila daftar masih kosong.
        /// Penanda di profil pengguna membuat contoh yang sudah dihapus tidak kembali.
        /// </summary>
        private async Task SiapkanContohBawaanAsync()
        {
            if (_sudahSiapkanBawaan) return;
            _sudahSiapkanBawaan = true;

            try
            {
                var terpasang = await _bawaanService.PasangOtomatisAsync();
                if (terpasang.Count > 0)
                {
                    InfoBawaan = $"{terpasang.Count} contoh template siap pakai sudah disiapkan: " +
                        string.Join(", ", terpasang.Select(t => t.NamaTampil)) + ". " +
                        "Contoh-contoh ini bisa langsung diisi dan dicetak, disunting, atau dihapus seperti template biasa.";
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyiapkan contoh template bawaan");
            }
        }

        private void RaisePerintah()
        {
            BuatBaruCommand.RaiseCanExecuteChanged();
            EditCommand.RaiseCanExecuteChanged();
            DuplikatCommand.RaiseCanExecuteChanged();
            HapusCommand.RaiseCanExecuteChanged();
            IsiCommand.RaiseCanExecuteChanged();
            PratinjauCommand.RaiseCanExecuteChanged();
            SegarkanCommand.RaiseCanExecuteChanged();
            BuatDariWordCommand.RaiseCanExecuteChanged();
        }
    }
}
