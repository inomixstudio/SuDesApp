using System;
using System.Collections.Generic;
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
    /// Halaman SOTK Desa: mencetak Susunan Organisasi dan Tata Kerja Pemerintah
    /// Desa (kop desa, uraian tugas pokok per unsur, dan tabel pejabat) dari
    /// data perangkat yang tersimpan, lalu membukanya di pratinjau PDF.
    /// Halaman yang sama juga memuat isian untuk membuat naskah Perdes SOTK.
    /// </summary>
    public class SotkDesaViewModel : ObservableObject
    {
        private const string TentangPerdesSotk =
            "Susunan Organisasi dan Tata Kerja Pemerintah Desa dan Perangkat Desa";

        private const string KeteranganPerdesSotk =
            "Perdes SOTK — dibuat dari menu SOTK Desa dan disimpan ke Buku Perdes.";

        private readonly IPerangkatDesaRepository _perangkatRepo;
        private readonly IDesaRepository _desaRepo;
        private readonly IArsipKeputusanRepository _arsipRepo;
        private readonly AppConfig _appConfig;
        private readonly SotkDesaGenerator _generator;
        private readonly PerdesSotkGenerator _perdesGenerator;
        private readonly PerdesSotkStore _perdesStore;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<SotkDesaViewModel> _logger;

        private IReadOnlyList<PerangkatDesa> _semua = Array.Empty<PerangkatDesa>();
        private string _statusText = string.Empty;
        private string _ringkasan = string.Empty;
        private bool _isBusy;
        private readonly PenjagaLisensi? _penjaga;
        private string _nomorPerdes = string.Empty;
        private string _tanggalPerdes = TanggalIsian.KeTeks(DateTime.Today);
        private string _rujukanDaerah = string.Empty;
        private string _namaDesa = string.Empty;
        private string _statusPerdes = string.Empty;
        private string _statusArsipPerdes = string.Empty;
        private int? _arsipId;

        public SotkDesaViewModel(
            IPerangkatDesaRepository perangkatRepo,
            IDesaRepository desaRepo,
            IArsipKeputusanRepository arsipRepo,
            AppConfig appConfig,
            SotkDesaGenerator generator,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<SotkDesaViewModel> logger,
            PerdesSotkGenerator perdesGenerator,
            PerdesSotkStore perdesStore,
            PenjagaLisensi? penjaga = null)
        {
            _perangkatRepo = perangkatRepo ?? throw new ArgumentNullException(nameof(perangkatRepo));
            _desaRepo = desaRepo ?? throw new ArgumentNullException(nameof(desaRepo));
            _arsipRepo = arsipRepo ?? throw new ArgumentNullException(nameof(arsipRepo));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _perdesGenerator = perdesGenerator ?? throw new ArgumentNullException(nameof(perdesGenerator));
            _perdesStore = perdesStore ?? throw new ArgumentNullException(nameof(perdesStore));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _penjaga = penjaga;

            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy);
            PratinjauPerdesCommand = new AsyncRelayCommand(PratinjauPerdesAsync, () => !IsBusy);
            SimpanPerdesCommand = new AsyncRelayCommand(SimpanPerdesAsync, () => !IsBusy);
            SimpanKeBukuPerdesCommand = new AsyncRelayCommand(SimpanKeBukuPerdesAsync, () => !IsBusy);
        }

        public string InfoSotk =>
            "Dokumen merangkum susunan jabatan dan pejabat aktif dari menu Perangkat Desa. "
            + "Uraian tugas mengikuti peraturan daerah yang berlaku; pastikan rujukan daerah pada isian Perdes benar.";

        public string NomorPerdes
        {
            get => _nomorPerdes;
            set => SetProperty(ref _nomorPerdes, value);
        }

        public string TanggalPerdes
        {
            get => _tanggalPerdes;
            set => SetProperty(ref _tanggalPerdes, value);
        }

        public string RujukanDaerah
        {
            get => _rujukanDaerah;
            set => SetProperty(ref _rujukanDaerah, value);
        }

        public string NamaDesa
        {
            get => _namaDesa;
            private set => SetProperty(ref _namaDesa, value);
        }

        public string StatusPerdes
        {
            get => _statusPerdes;
            set => SetProperty(ref _statusPerdes, value);
        }

        /// <summary>Keadaan hubungan naskah ini dengan baris Buku Perdes.</summary>
        public string StatusArsipPerdes
        {
            get => _statusArsipPerdes;
            private set => SetProperty(ref _statusArsipPerdes, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string Ringkasan
        {
            get => _ringkasan;
            set => SetProperty(ref _ringkasan, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    PratinjauCommand.RaiseCanExecuteChanged();
                    PratinjauPerdesCommand.RaiseCanExecuteChanged();
                    SimpanPerdesCommand.RaiseCanExecuteChanged();
                    SimpanKeBukuPerdesCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncRelayCommand PratinjauCommand { get; }

        public AsyncRelayCommand PratinjauPerdesCommand { get; }

        public AsyncRelayCommand SimpanPerdesCommand { get; }

        /// <summary>Simpan naskah SOTK menjadi baris Buku Perdes beserta PDF-nya.</summary>
        public AsyncRelayCommand SimpanKeBukuPerdesCommand { get; }

        /// <summary>Muat data perangkat; dipanggil MainWindowViewModel sebelum navigasi.</summary>
        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;
                _semua = await _perangkatRepo.GetAllAsync().ConfigureAwait(true);
                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                NamaDesa = desa?.NamaDesa?.Trim() ?? string.Empty;

                try
                {
                    var tersimpan = await _perdesStore.AmbilAsync().ConfigureAwait(true);
                    NomorPerdes = tersimpan.Nomor;
                    if (!string.IsNullOrWhiteSpace(tersimpan.Tanggal)) TanggalPerdes = tersimpan.Tanggal;
                    RujukanDaerah = tersimpan.RujukanDaerah;
                    _arsipId = tersimpan.ArsipId is > 0 ? tersimpan.ArsipId : null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal memuat isian Perdes SOTK tersimpan");
                    StatusPerdes = "Isian Perdes tersimpan belum bisa dimuat: " + ex.Message;
                }

                await MuatStatusArsipAsync().ConfigureAwait(true);

                int terisi = JabatanPerangkat.Resmi.Count(j => _semua.Any(p =>
                    string.Equals(JabatanPerangkat.Normalisasi(p.Jabatan), j, StringComparison.Ordinal)
                    && p.MasihMemegangJabatan));
                Ringkasan = $"{terisi} dari {JabatanPerangkat.Resmi.Count} jabatan perangkat desa sudah terisi.";
                StatusText = "Data perangkat dimuat. Tekan Pratinjau SOTK untuk melihat dokumennya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data untuk SOTK");
                StatusText = "Data perangkat belum bisa dimuat: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PratinjauAsync()
        {
            try
            {
                IsBusy = true;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                if (desa == null)
                {
                    await _messageService.ShowWarningAsync(
                        "Informasi desa belum lengkap. Lengkapi Pengaturan Surat dulu supaya kop dan "
                        + "tanda tangan SOTK tercetak benar.");
                    return;
                }

                var data = new SotkDesaData
                {
                    Desa = desa,
                    Tanggal = DateTime.Today,
                    Pejabat = SusunPejabat()
                };

                using var stream = new MemoryStream();
                _generator.GeneratePdf(stream, data);
                byte[] isi = stream.ToArray();
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat PDF SOTK atau PDF kosong.");
                    return;
                }

                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, isi, "SOTK")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"SOTK {Ringkasan}", "Pratinjau SOTK", jalur);
                StatusText = "SOTK Pemerintah Desa dibuka di pratinjau PDF.";
                _navigation.Navigate(_previewFactory("SOTK Pemerintah Desa", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF SOTK");
                await _messageService.ShowErrorAsync("Gagal membuat PDF SOTK.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PratinjauPerdesAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            try
            {
                IsBusy = true;
                var hasil = await SiapkanPerdesAsync().ConfigureAwait(true);
                if (hasil is null) return;

                var (data, isi) = hasil.Value;
                await _perdesStore.SimpanAsync(NomorPerdes, TanggalPerdes, RujukanDaerah, _arsipId)
                    .ConfigureAwait(true);

                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, isi, "PerdesSOTK")
                    .ConfigureAwait(true);
                string judul = $"Perdes SOTK Nomor {NomorPerdes.Trim()} Tahun {data.Tanggal.Year}";
                _activityLog.Log("PERANGKAT", judul, "Pratinjau Perdes", jalur);
                StatusPerdes = $"{judul} dibuka di pratinjau PDF.";
                _navigation.Navigate(_previewFactory(judul, jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF Perdes SOTK");
                await _messageService.ShowErrorAsync("Gagal membuat PDF Perdes.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Validasi isian Perdes SOTK lalu hasilkan naskahnya. Dipakai bersama
        /// oleh pratinjau dan penyimpanan ke Buku Perdes supaya keduanya tidak
        /// pernah menghasilkan dokumen dengan isian yang berbeda.
        /// </summary>
        private async Task<(PerdesSotkData Data, byte[] Pdf)?> SiapkanPerdesAsync()
        {
            if (string.IsNullOrWhiteSpace(NomorPerdes))
            {
                StatusPerdes = "Nomor Perdes wajib diisi, mis. 2 atau 4/DESA/2026.";
                await _messageService.ShowWarningAsync("Nomor Perdes wajib diisi.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(RujukanDaerah))
            {
                StatusPerdes = "Isi dasar hukum daerah yang berlaku (Perda/Perbup atau Perwali) sebelum membuat PDF.";
                await _messageService.ShowWarningAsync(StatusPerdes);
                return null;
            }

            if (!TanggalIsian.BacaWajib(TanggalPerdes, "Tanggal Perdes", out DateTime tanggal, out string pesan))
            {
                StatusPerdes = pesan;
                await _messageService.ShowWarningAsync(pesan);
                return null;
            }

            var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
            if (desa == null)
            {
                StatusPerdes = "Informasi desa belum lengkap. Lengkapi Pengaturan Surat dulu.";
                await _messageService.ShowWarningAsync(StatusPerdes);
                return null;
            }

            var data = new PerdesSotkData
            {
                Desa = desa,
                Nomor = NomorPerdes.Trim(),
                Tanggal = tanggal,
                Tempat = desa.NamaDesa?.Trim() ?? string.Empty,
                RujukanDaerah = RujukanDaerah.Trim(),
                SusunanPerangkat = SusunPejabat(),
                NamaBpd = _semua
                    .Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "BPD" && p.MasihMemegangJabatan)
                    .OrderBy(p => JabatanPerangkat.Normalisasi(p.Jabatan), StringComparer.Ordinal)
                    .ThenBy(p => p.Nama, StringComparer.OrdinalIgnoreCase)
                    .Select(p => $"{JabatanPerangkat.Normalisasi(p.Jabatan)} — {p.Nama.Trim()}")
                    .ToArray()
            };

            using var stream = new MemoryStream();
            _perdesGenerator.GeneratePdf(stream, data);
            byte[] isi = stream.ToArray();
            if (isi.Length == 0)
            {
                StatusPerdes = "Gagal membuat PDF Perdes atau PDF kosong.";
                await _messageService.ShowErrorAsync(StatusPerdes);
                return null;
            }

            return (data, isi);
        }

        private async Task SimpanPerdesAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            if (string.IsNullOrWhiteSpace(NomorPerdes))
            {
                StatusPerdes = "Nomor Perdes wajib diisi sebelum disimpan.";
                await _messageService.ShowWarningAsync(StatusPerdes);
                return;
            }

            try
            {
                IsBusy = true;
                await _perdesStore.SimpanAsync(NomorPerdes, TanggalPerdes, RujukanDaerah, _arsipId)
                    .ConfigureAwait(true);
                StatusPerdes = "Draf isian Perdes SOTK tersimpan. Gunakan Simpan ke Buku Perdes untuk menerbitkan arsipnya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan isian Perdes SOTK");
                StatusPerdes = "Isian Perdes SOTK belum tersimpan: " + ex.Message;
                await _messageService.ShowErrorAsync(StatusPerdes);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Terbitkan naskah SOTK sebagai baris Buku Perdes: PDF disalin ke arsip
        /// Perdes, sehingga naskah bisa dibuka, disunting metadatanya, dan
        /// dihapus dari halaman PERDES seperti peraturan desa lain — bukan
        /// tersimpan di tempat terpisah.
        /// </summary>
        private async Task SimpanKeBukuPerdesAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            try
            {
                IsBusy = true;
                var hasil = await SiapkanPerdesAsync().ConfigureAwait(true);
                if (hasil is null) return;
                var (data, pdf) = hasil.Value;

                var semua = await _arsipRepo.GetAllAsync().ConfigureAwait(true);
                var arsip = _arsipId is int id
                    ? semua.FirstOrDefault(k => k.IdBarisExcel == id)
                    : null;

                bool duplikat = semua.Any(k =>
                    k.JenisKeputusan.Equals("PERDES", StringComparison.OrdinalIgnoreCase) &&
                    k.Nomor.Equals(data.Nomor, StringComparison.OrdinalIgnoreCase) &&
                    k.IdBarisExcel != (arsip?.IdBarisExcel ?? 0));
                if (duplikat)
                {
                    StatusPerdes = $"Nomor Perdes '{data.Nomor}' sudah terdaftar di Buku Perdes. "
                        + "Gunakan nomor lain atau sunting data lama melalui menu PERDES.";
                    await _messageService.ShowWarningAsync(StatusPerdes);
                    return;
                }

                // Judul dan keterangan hanya diisi untuk baris baru. Bila pengguna
                // sudah menyuntingnya di Buku Perdes, suntingan itu dipertahankan.
                var item = arsip ?? new DataKeputusan { JenisKeputusan = "PERDES" };
                item.Nomor = data.Nomor;
                item.Tanggal = data.Tanggal;
                if (string.IsNullOrWhiteSpace(item.Tentang)) item.Tentang = TentangPerdesSotk;
                if (string.IsNullOrWhiteSpace(item.Keterangan)) item.Keterangan = KeteranganPerdesSotk;

                if (arsip == null)
                {
                    await _arsipRepo.AddAsync(item).ConfigureAwait(true);
                }
                else
                {
                    await _arsipRepo.UpdateAsync(item).ConfigureAwait(true);
                }

                var (fileName, tujuan) = _arsipRepo.ResolveWordStorage(
                    item.IdBarisExcel, item.JenisKeputusan, item.Nomor, ".pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(tujuan) ?? _appConfig.TempPdfFolder);

                var lama = _arsipRepo.ResolveWordFullPath(item.FileWord);
                if (lama != null && !string.Equals(lama, tujuan, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(lama); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Gagal menghapus lampiran Perdes SOTK lama"); }
                }

                await File.WriteAllBytesAsync(tujuan, pdf).ConfigureAwait(true);
                item.FileWord = fileName;
                await _arsipRepo.UpdateAsync(item).ConfigureAwait(true);

                _arsipId = item.IdBarisExcel;
                await _perdesStore.SimpanAsync(NomorPerdes, TanggalPerdes, RujukanDaerah, _arsipId)
                    .ConfigureAwait(true);
                await MuatStatusArsipAsync().ConfigureAwait(true);

                string judul = $"Perdes SOTK Nomor {item.Nomor} Tahun {item.Tanggal.Year}";
                _activityLog.Log("PERANGKAT", judul, "Simpan ke Buku Perdes", tujuan);
                StatusPerdes = $"{judul} tersimpan di Buku Perdes. Buka menu PERDES untuk menyunting atau mencetaknya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan Perdes SOTK ke Buku Perdes");
                StatusPerdes = "Naskah belum tersimpan ke Buku Perdes: " + ex.Message;
                await _messageService.ShowErrorAsync(StatusPerdes);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Periksa apakah baris Buku Perdes yang tertaut masih ada.</summary>
        private async Task MuatStatusArsipAsync()
        {
            if (_arsipId is not int id)
            {
                StatusArsipPerdes = "Belum diterbitkan ke Buku Perdes. Gunakan Simpan ke Buku Perdes.";
                return;
            }

            try
            {
                var semua = await _arsipRepo.GetAllAsync().ConfigureAwait(true);
                var arsip = semua.FirstOrDefault(k => k.IdBarisExcel == id);
                if (arsip == null)
                {
                    _arsipId = null;
                    StatusArsipPerdes = "Baris Buku Perdes yang tertaut sudah dihapus. "
                        + "Simpan ulang untuk menerbitkan naskah baru.";
                    return;
                }

                StatusArsipPerdes = $"Tersimpan di Buku Perdes: nomor {arsip.Nomor}, "
                    + $"{arsip.Tanggal:dd-MM-yyyy}.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca status Buku Perdes untuk Perdes SOTK");
                StatusArsipPerdes = "Status Buku Perdes belum bisa dibaca.";
            }
        }

        /// <summary>Satu baris per pejabat; jabatan belum terisi tetap ada baris "-" supaya susunannya utuh.</summary>
        private List<SotkPerangkatBaris> SusunPejabat()
        {
            var baris = new List<SotkPerangkatBaris>();
            foreach (string jabatan in JabatanPerangkat.Resmi)
            {
                var pemegang = _semua
                    .Where(p => string.Equals(JabatanPerangkat.Normalisasi(p.Jabatan), jabatan, StringComparison.Ordinal)
                        && p.MasihMemegangJabatan)
                    .OrderBy(p => p.Nama, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pemegang.Count == 0)
                {
                    baris.Add(new SotkPerangkatBaris { Jabatan = jabatan, Nama = "-", Wilayah = "-" });
                    continue;
                }

                foreach (var orang in pemegang)
                {
                    baris.Add(new SotkPerangkatBaris
                    {
                        Jabatan = jabatan,
                        Nama = orang.Nama,
                        Wilayah = orang.WilayahRingkas
                    });
                }
            }

            return baris;
        }
    }
}

