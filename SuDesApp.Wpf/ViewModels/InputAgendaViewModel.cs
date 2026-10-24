using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Formulir input/edit satu data buku agenda Surat Masuk/Keluar.
    /// Padanan InputKeluarMasuk (WinForms): validasi + simpan/ubah ke database.
    ///
    /// Fitur lampiran: satu berkas <b>PDF atau gambar</b> (hasil pindai/tangkapan
    /// surat) dapat dilampirkan pada tiap baris. Isi berkas tidak pernah dibaca
    /// aplikasi — berkas hanya disalin ke folder ArsipSuratFiles saat data
    /// disimpan, sama seperti lampiran pada buku Keputusan.
    /// </summary>
    public class InputAgendaViewModel : ObservableObject, IJudulHalaman
    {
        private readonly IArsipSuratRepository _repository;
        private readonly FileService _fileService;
        private readonly IMessageService _messageService;
        private readonly ILogger<InputAgendaViewModel> _logger;
        /// <summary>Pemilih berkas (dialog Windows). Bisa diganti saat pengujian.</summary>
        private readonly Func<string?> _pilihBerkas;

        private string _jenisSurat = "MASUK";
        private SuratKeluarMasukData? _editData;
        private string _title = "Input Surat";
        private bool _isBusy;

        private string _nomorSurat = string.Empty;
        private string _tanggalSurat = string.Empty;
        private string _tanggalTerimaKirim = string.Empty;
        private string _asalTujuan = string.Empty;
        private string _perihal = string.Empty;
        private string _isiRingkas = string.Empty;
        private string _keterangan = string.Empty;

        /// <summary>Path berkas lampiran yang baru dipilih (sumber). Disalin saat Simpan.</summary>
        private string _lampiranFilePath = string.Empty;
        /// <summary>Nama berkas lampiran yang sudah tersimpan di arsip (kolom FileLampiran).</summary>
        private string? _storedFileName;
        /// <summary>Pengguna menekan "Hapus Lampiran": berkas lama dibuang saat Simpan.</summary>
        private bool _lampiranDihapus;

        public event Action? RequestClose;

        /// <summary>Data berhasil disimpan — halaman induk perlu memuat ulang daftarnya.</summary>
        public event Action? Tersimpan;

        public InputAgendaViewModel(
            IArsipSuratRepository repository,
            FileService fileService,
            IMessageService messageService,
            ILogger<InputAgendaViewModel> logger,
            Func<string?>? pilihBerkasDialog = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pilihBerkas = pilihBerkasDialog ?? PilihBerkasLewatDialog;

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            PilihLampiranCommand = new AsyncRelayCommand(PilihLampiranAsync);
            HapusLampiranCommand = new RelayCommand(HapusLampiran, () => HasLampiran);
            BukaLampiranCommand = new AsyncRelayCommand(BukaLampiranAsync, () => HasLampiran);
        }

        public string Title
        {
            get => _title;
            private set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(JudulHalaman));
                }
            }
        }

        /// <summary>Judul halaman ini di title bar jendela (lihat <see cref="IJudulHalaman"/>).</summary>
        public string JudulHalaman => Title;

        public string NomorSurat { get => _nomorSurat; set => SetProperty(ref _nomorSurat, value); }
        public string TanggalSurat { get => _tanggalSurat; set => SetProperty(ref _tanggalSurat, value); }
        public string TanggalTerimaKirim { get => _tanggalTerimaKirim; set => SetProperty(ref _tanggalTerimaKirim, value); }
        public string AsalTujuan { get => _asalTujuan; set => SetProperty(ref _asalTujuan, value); }
        public string Perihal { get => _perihal; set => SetProperty(ref _perihal, value); }
        public string IsiRingkas { get => _isiRingkas; set => SetProperty(ref _isiRingkas, value); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value); }

        /// <summary>Nama berkas lampiran yang ditampilkan di formulir.</summary>
        public string LampiranDisplay =>
            !string.IsNullOrWhiteSpace(_lampiranFilePath) ? Path.GetFileName(_lampiranFilePath)
            : (!_lampiranDihapus && !string.IsNullOrWhiteSpace(_storedFileName)) ? _storedFileName!
            : "Tidak ada berkas";

        /// <summary>Ada berkas lampiran (baru dipilih atau sudah tersimpan).</summary>
        public bool HasLampiran =>
            !string.IsNullOrWhiteSpace(_lampiranFilePath) ||
            (!_lampiranDihapus && !string.IsNullOrWhiteSpace(_storedFileName));

        /// <summary>Ada berkas lampiran baru yang belum tersalin ke arsip.</summary>
        public bool HasNewLampiran => !string.IsNullOrWhiteSpace(_lampiranFilePath);

        /// <summary>Label pendek jenis berkas untuk chip (PDF/JPG/...), atau kosong.</summary>
        public string EkstensiLampiran => HasLampiran
            ? LampiranArsipSurat.Label(!string.IsNullOrWhiteSpace(_lampiranFilePath) ? _lampiranFilePath : _storedFileName)
            : string.Empty;

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public AsyncRelayCommand PilihLampiranCommand { get; }
        public RelayCommand HapusLampiranCommand { get; }
        public AsyncRelayCommand BukaLampiranCommand { get; }

        public void Initialize(string jenisSurat, SuratKeluarMasukData? editData, SuratKeluarMasukData? prefill = null)
        {
            _jenisSurat = (jenisSurat ?? "MASUK").ToUpperInvariant();
            _editData = editData;
            bool isEdit = _editData != null;
            bool isPrefill = !isEdit && prefill != null;
            Title = _jenisSurat == "MASUK"
                ? (isEdit ? "Edit Surat Masuk" : isPrefill ? "Salin Surat Masuk" : "Input Surat Masuk")
                : (isEdit ? "Edit Surat Keluar" : isPrefill ? "Salin Surat Keluar" : "Input Surat Keluar");

            if (isEdit)
            {
                NomorSurat = _editData!.NomorSurat;
                TanggalSurat = _editData.TanggalSurat.ToString("dd-MM-yyyy");
                TanggalTerimaKirim = _editData.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy") ?? string.Empty;
                AsalTujuan = _editData.AsalTujuan;
                Perihal = _editData.Perihal;
                IsiRingkas = _editData.IsiRingkas;
                Keterangan = _editData.Keterangan;
                _storedFileName = _editData.FileLampiran;
            }
            else if (isPrefill)
            {
                NomorSurat = prefill!.NomorSurat;
                TanggalSurat = prefill.TanggalSurat.ToString("dd-MM-yyyy");
                TanggalTerimaKirim = prefill.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy") ?? string.Empty;
                AsalTujuan = prefill.AsalTujuan;
                Perihal = prefill.Perihal;
                IsiRingkas = prefill.IsiRingkas;
                Keterangan = prefill.Keterangan;
                _storedFileName = null;
            }
            else
            {
                NomorSurat = string.Empty;
                TanggalSurat = DateTime.Today.ToString("dd-MM-yyyy");
                TanggalTerimaKirim = string.Empty;
                AsalTujuan = string.Empty;
                Perihal = string.Empty;
                IsiRingkas = string.Empty;
                Keterangan = string.Empty;
                _storedFileName = null;
            }

            _lampiranFilePath = string.Empty;
            _lampiranDihapus = false;
            UpdateLampiranState();
        }

        // =====================================================================
        // Lampiran PDF / gambar
        // =====================================================================

        private static string? PilihBerkasLewatDialog()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Pilih berkas lampiran (PDF atau gambar)",
                Filter = LampiranArsipSurat.FilterDialog
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>Memilih berkas lampiran; hanya PDF/gambar yang diterima.</summary>
        private async Task PilihLampiranAsync()
        {
            try
            {
                string? path = _pilihBerkas();
                if (string.IsNullOrWhiteSpace(path)) return;

                if (!File.Exists(path))
                {
                    await _messageService.ShowWarningAsync("Berkas yang dipilih tidak ditemukan.");
                    return;
                }

                if (!LampiranArsipSurat.Didukung(path))
                {
                    await _messageService.ShowWarningAsync(LampiranArsipSurat.PesanTidakDidukung(path));
                    return;
                }

                _lampiranFilePath = path;
                _lampiranDihapus = false;
                UpdateLampiranState();
                _logger.LogInformation("Lampiran agenda surat dipilih: {File}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memilih berkas lampiran");
                await _messageService.ShowErrorAsync("Gagal memilih berkas: " + ex.Message);
            }
        }

        /// <summary>Menandai lampiran agar dibuang (berkas lama dihapus saat Simpan).</summary>
        private void HapusLampiran()
        {
            _lampiranFilePath = string.Empty;
            _lampiranDihapus = !string.IsNullOrWhiteSpace(_storedFileName);
            UpdateLampiranState();
        }

        /// <summary>Membuka lampiran: yang baru dipilih atau yang tersimpan di arsip.</summary>
        private async Task BukaLampiranAsync()
        {
            string? target = !string.IsNullOrWhiteSpace(_lampiranFilePath)
                ? _lampiranFilePath
                : _repository.ResolveLampiranFullPath(_storedFileName);

            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            {
                await _messageService.ShowWarningAsync("Berkas lampiran tidak ditemukan di penyimpanan.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka berkas lampiran: {Path}", target);
                await _messageService.ShowErrorAsync("Gagal membuka berkas: " + ex.Message);
            }
        }

        private void UpdateLampiranState()
        {
            OnPropertyChanged(nameof(LampiranDisplay));
            OnPropertyChanged(nameof(HasLampiran));
            OnPropertyChanged(nameof(HasNewLampiran));
            OnPropertyChanged(nameof(EkstensiLampiran));
            HapusLampiranCommand.RaiseCanExecuteChanged();
            ((AsyncRelayCommand)BukaLampiranCommand).RaiseCanExecuteChanged();
        }

        // =====================================================================
        // Simpan
        // =====================================================================

        private async Task SaveAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            try
            {
                var errors = Validate();
                if (errors.Count > 0)
                {
                    await _messageService.ShowWarningAsync(
                        "Data belum lengkap atau format salah:\n\n" + string.Join("\n", errors));
                    return;
                }

                var item = new SuratKeluarMasukData
                {
                    JenisSurat = _jenisSurat,
                    NomorSurat = NomorSurat.Trim(),
                    TanggalSurat = ParseDate(TanggalSurat)!.Value,
                    TanggalDiterimaDikirim = ParseNullableDate(TanggalTerimaKirim),
                    AsalTujuan = AsalTujuan.Trim(),
                    Perihal = Perihal.Trim(),
                    IsiRingkas = IsiRingkas.Trim(),
                    Keterangan = Keterangan.Trim(),
                    FileLampiran = _lampiranDihapus ? null : _editData?.FileLampiran
                };

                if (_editData != null)
                {
                    item.IdBarisExcel = _editData.IdBarisExcel;
                    await _repository.UpdateAsync(item);
                    _logger.LogInformation("Arsip surat diedit: Id={Id}", item.IdBarisExcel);
                }
                else
                {
                    await _repository.AddAsync(item);
                }

                // Berkas lampiran disalin setelah data tersimpan (id sudah ada untuk
                // penamaan berkas). Kegagalan menyalin berkas tidak membatalkan data.
                if (HasNewLampiran)
                {
                    try
                    {
                        await SimpanLampiranAsync(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal menyalin berkas lampiran agenda surat");
                        await _messageService.ShowErrorAsync(LampiranArsipSurat.Didukung(_lampiranFilePath)
                            ? "Data tersimpan, tetapi berkas lampiran gagal disalin:\n\n" + ex.Message
                            : "Data tersimpan tanpa lampiran.\n\n" + ex.Message);
                    }
                }
                else if (_lampiranDihapus)
                {
                    await HapusBerkasLamaAsync(_editData?.FileLampiran);
                }

                Tersimpan?.Invoke();
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan arsip surat masuk/keluar");
                await _messageService.ShowErrorAsync($"Gagal menyimpan data: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        }

        /// <summary>Menyalin berkas lampiran ke folder arsip dan menyimpan namanya di database.</summary>
        private async Task SimpanLampiranAsync(SuratKeluarMasukData item)
        {
            var (fileName, destPath) = _repository.ResolveLampiranStorage(
                item.IdBarisExcel, item.JenisSurat, item.NomorSurat,
                Path.GetExtension(_lampiranFilePath));

            // Berkas lama (bila ada dan berbeda) dibuang agar tidak menumpuk.
            var oldPath = _repository.ResolveLampiranFullPath(_editData?.FileLampiran);
            if (!string.IsNullOrEmpty(oldPath) &&
                !string.Equals(oldPath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(oldPath); } catch { /* biarkan berkas lama bila gagal */ }
            }

            _fileService.EnsureDirectoryExists(Path.GetDirectoryName(destPath) ?? destPath);
            await _fileService.CopyFileAsync(_lampiranFilePath, destPath, overwrite: true);

            item.FileLampiran = fileName;
            await _repository.UpdateAsync(item); // simpan kolom FileLampiran

            _storedFileName = fileName;
            _lampiranFilePath = string.Empty;
            _lampiranDihapus = false;
            UpdateLampiranState();
            _logger.LogInformation("Lampiran agenda surat disalin ke arsip: {File}", fileName);
        }

        /// <summary>Menghapus berkas lampiran lama dari folder arsip (bila pengguna membuangnya).</summary>
        private Task HapusBerkasLamaAsync(string? fileName)
        {
            try
            {
                var path = _repository.ResolveLampiranFullPath(fileName);
                if (path != null)
                {
                    File.Delete(path);
                    _logger.LogInformation("Lampiran agenda surat dihapus: {File}", fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghapus berkas lampiran agenda surat: {File}", fileName);
            }
            _storedFileName = null;
            _lampiranDihapus = false;
            UpdateLampiranState();
            return Task.CompletedTask;
        }

        private List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(NomorSurat)) errors.Add("- Nomor Surat wajib diisi.");
            if (string.IsNullOrWhiteSpace(AsalTujuan)) errors.Add("- Asal/Tujuan Surat wajib diisi.");
            if (string.IsNullOrWhiteSpace(Perihal)) errors.Add("- Perihal wajib diisi.");
            if (string.IsNullOrWhiteSpace(IsiRingkas)) errors.Add("- Isi Ringkas wajib diisi.");

            if (string.IsNullOrWhiteSpace(TanggalSurat)) errors.Add("- Tanggal Surat wajib diisi.");
            else if (ParseDate(TanggalSurat) == null) errors.Add("- Format Tanggal Surat tidak valid (DD-MM-YYYY).");

            if (!string.IsNullOrWhiteSpace(TanggalTerimaKirim) && ParseDate(TanggalTerimaKirim) == null)
            {
                errors.Add("- Format Tanggal Diterima/Dikirim tidak valid (DD-MM-YYYY).");
            }

            return errors;
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParseExact(value, "dd-MM-yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : null;
        }

        private static DateTime? ParseNullableDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return ParseDate(value);
        }
    }
}
