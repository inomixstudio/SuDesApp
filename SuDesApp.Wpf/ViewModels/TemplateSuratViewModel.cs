using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// Contoh siap pakai dipasang otomatis sekali bila daftar masih kosong, dan
    /// tombol "Perbarui Contoh" menyegarkan template yang berasal dari contoh bawaan
    /// ke susunan terbaru.
    /// </summary>
    public class TemplateSuratViewModel : ObservableObject
    {
        /// <summary>Mode pembukaan wizard.</summary>
        public const int ModeBaru = 0;
        public const int ModeEdit = 1;
        public const int ModeDuplikat = 2;

        private readonly ITemplateSuratRepository _repository;
        private readonly TemplateSuratGenerator _generator;
        private readonly TemplateSuratBawaanService _bawaanService;
        private readonly Func<int, TemplateSuratKustom?, TemplateSuratWizardViewModel> _wizardFactory;
        private readonly Func<IsiTemplateSuratViewModel> _isiFactory;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly ILogger<TemplateSuratViewModel> _logger;

        private readonly List<TemplateSuratKustom> _semua = new();

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
            PerbaruiContohCommand = new AsyncRelayCommand(PerbaruiContohAsync, () => !IsBusy && JumlahContohBisaDisegarkan > 0);
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

        /// <summary>Segarkan template yang berasal dari contoh bawaan ke susunan terbaru.</summary>
        public AsyncRelayCommand PerbaruiContohCommand { get; }

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

        /// <summary>
        /// Jumlah template pengguna yang berasal dari contoh bawaan — inilah yang bisa
        /// diperbarui ke susunan contoh terbaru (mis. Surat Izin Keramaian yang baru).
        /// </summary>
        public int JumlahContohBisaDisegarkan => TemplateSuratBawaanService.JumlahBisaDisegarkan(_semua);

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
                await _messageService.ShowErrorAsync("Gagal memuat daftar Template Surat: " + ex.Message);
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
                _ = _messageService.ShowErrorAsync("Gagal membuka wizard Template Surat: " + ex.Message);
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
                await _messageService.ShowErrorAsync("Gagal menghapus template: " + ex.Message);
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
                await _messageService.ShowErrorAsync("Gagal membuka pengisian surat: " + ex.Message);
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
                    await _messageService.ShowWarningAsync("Template belum memiliki elemen yang bisa dicetak.");
                    return;
                }

                var nilaiContoh = TemplateSuratNilai.NilaiContoh(template);
                int urut;
                string nomor = template.NomorBerikutnya(DateTime.Now, out urut);
                var pdf = await _generator.BuatPdfAsync(template, nilaiContoh, nomor, DateTime.Now);

                if (pdf.Length == 0)
                {
                    await _messageService.ShowWarningAsync("Pratinjau gagal dibuat: berkas PDF kosong.");
                    return;
                }

                string berkas = Path.Combine(Path.GetTempPath(), "SuDesApp",
                    $"pratinjau-template-{Guid.NewGuid():N}.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(berkas));
                await File.WriteAllBytesAsync(berkas, pdf);

                _navigation.Navigate(_previewFactory($"Pratinjau template — {template.NamaTampil}", berkas));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau template surat");
                await _messageService.ShowErrorAsync("Gagal membuat pratinjau: " + ex.Message);
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
                    InfoBawaan = $"{terpasang.Count} contoh template siap pakai sudah disiapkan (misalnya " +
                        "Surat Pengantar RT/RW, Surat Izin Keramaian, dan Surat Keterangan Penghasilan). " +
                        "Contoh-contoh ini bisa langsung diisi dan dicetak, disunting, atau dihapus seperti template biasa.";
                }

                PerbaruiInfoContoh();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyiapkan contoh template bawaan");
            }
        }

        private void PerbaruiInfoContoh()
        {
            OnPropertyChanged(nameof(JumlahContohBisaDisegarkan));
            PerbaruiContohCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Ganti definisi template yang berasal dari contoh bawaan dengan definisi contoh
        /// terbaru. Berguna ketika susunan contoh diperbaiki (mis. Surat Izin Keramaian
        /// mengikuti format surat pengantar izin rame-rame).
        /// </summary>
        private async Task PerbaruiContohAsync()
        {
            var kandidat = _semua
                .Where(t => TemplateSuratBawaan.KodeDariNama(t.NamaTampil) != null)
                .ToList();
            if (kandidat.Count == 0)
            {
                InfoBawaan = "Tidak ada template dari contoh bawaan pada daftar.";
                return;
            }

            string daftarNama = string.Join(", ", kandidat.Take(4).Select(t => t.NamaTampil));
            if (kandidat.Count > 4)
            {
                daftarNama += $", dan {kandidat.Count - 4} lainnya";
            }

            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Perbarui Contoh Bawaan",
                $"Perbarui {kandidat.Count} template berikut ke susunan terbaru: {daftarNama}? " +
                "Susunan surat dan kolom isiannya diganti, sehingga perubahan yang pernah Anda buat " +
                "pada template tersebut ikut tertimpa. Nomor surat terakhir tetap dipertahankan.");

            if (!lanjut) return;

            try
            {
                var diubah = await _bawaanService.SegarkanAsync();
                InfoBawaan = diubah.Count > 0
                    ? $"{diubah.Count} template diperbarui ke susunan contoh terbaru."
                    : "Tidak ada template yang perlu diperbarui.";

                await MuatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memperbarui contoh template bawaan");
                await _messageService.ShowErrorAsync("Gagal memperbarui contoh template: " + ex.Message);
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
            PerbaruiContohCommand.RaiseCanExecuteChanged();
        }
    }
}
