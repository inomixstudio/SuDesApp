using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Formulir input/edit satu data buku agenda Surat Masuk/Keluar.
    /// Padanan InputKeluarMasuk (WinForms): validasi + simpan/ubah ke Excel.
    /// </summary>
    public class InputAgendaViewModel : ObservableObject
    {
        private readonly IArsipSuratRepository _repository;
        private readonly IMessageService _messageService;
        private readonly ILogger<InputAgendaViewModel> _logger;

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

        public event Action? RequestClose;

        public InputAgendaViewModel(
            IArsipSuratRepository repository,
            IMessageService messageService,
            ILogger<InputAgendaViewModel> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string NomorSurat { get => _nomorSurat; set => SetProperty(ref _nomorSurat, value); }
        public string TanggalSurat { get => _tanggalSurat; set => SetProperty(ref _tanggalSurat, value); }
        public string TanggalTerimaKirim { get => _tanggalTerimaKirim; set => SetProperty(ref _tanggalTerimaKirim, value); }
        public string AsalTujuan { get => _asalTujuan; set => SetProperty(ref _asalTujuan, value); }
        public string Perihal { get => _perihal; set => SetProperty(ref _perihal, value); }
        public string IsiRingkas { get => _isiRingkas; set => SetProperty(ref _isiRingkas, value); }
        public string Keterangan { get => _keterangan; set => SetProperty(ref _keterangan, value); }

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        public void Initialize(string jenisSurat, SuratKeluarMasukData? editData)
        {
            _jenisSurat = (jenisSurat ?? "MASUK").ToUpperInvariant();
            _editData = editData;
            bool isEdit = _editData != null;
            Title = _jenisSurat == "MASUK"
                ? (isEdit ? "Edit Surat Masuk" : "Input Surat Masuk")
                : (isEdit ? "Edit Surat Keluar" : "Input Surat Keluar");

            if (isEdit)
            {
                NomorSurat = _editData!.NomorSurat;
                TanggalSurat = _editData.TanggalSurat.ToString("dd-MM-yyyy");
                TanggalTerimaKirim = _editData.TanggalDiterimaDikirim?.ToString("dd-MM-yyyy") ?? string.Empty;
                AsalTujuan = _editData.AsalTujuan;
                Perihal = _editData.Perihal;
                IsiRingkas = _editData.IsiRingkas;
                Keterangan = _editData.Keterangan;
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
            }
        }

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
                    Keterangan = Keterangan.Trim()
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