using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class RegisterSuratViewModel : ObservableObject
    {
        private async Task HandlePageNavigationAsync(int pageChange)
        {
            var newPage = _currentPage + pageChange;
            if (newPage < 1) return;
            _currentPage = newPage;
            await LoadDataSafeAsync();
        }

        /// <summary>
        /// Perubahan filter (jenis/status/tahun/tanggal) masuk lewat sini.
        /// Saat inisialisasi berjalan (binding awal menyalakan SelectionChanged),
        /// permintaan ditunda dan di-flush SEKALI setelah inisialisasi — sehingga
        /// data hanya dimuat sekali per gugus perubahan filter, bukan berulang kali.
        /// </summary>
        private async Task HandleFilterChangeAsync()
        {
            if (_suppressFilterEvents) return; // perubahan programatik (mis. toggle Draft) sudah memuat sendiri
            if (_isInitializing)
            {
                _pendingFilterRequests++;
                return;
            }
            if (!_isComboBoxInitialized) return;

            if (_pendingFilterRequests > 0)
            {
                // Ada permintaan yang tertunda dari masa inisialisasi: buang sekarang,
                // state filter yang aktif di properti sudah final.
                _pendingFilterRequests = 0;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        /// <summary>Dipanggil di akhir inisialisasi: flush TUNDA-an jadi SATU reload.
        /// Bila tidak ada permintaan tertunda, tidak ada reload tambahan sama sekali.</summary>
        private async Task FlushPendingFilterChangeAsync()
        {
            if (_pendingFilterRequests == 0) return;
            _pendingFilterRequests = 0;
            _logger.LogDebug("Flushing pending filter change(s) as one reload");
            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        /// <summary>
        /// Tombol cepat Draft di toolbar: aktifkan filter status Draft bila belum aktif,
        /// atau kembalikan ke Semua Status bila sedang aktif (toggle).
        /// </summary>
        private async Task ToggleDraftFilterAsync()
        {
            if (_isInitializing)
            {
                _pendingFilterRequests++;
                return;
            }
            if (!_isComboBoxInitialized) return;            // Programatik: tahan event SelectionChanged dari ComboBox agar tidak
            // terjadi dobel load (toggle sudah memuat sendiri di bawah).
            _suppressFilterEvents = true;
            try
            {
                SelectedStatus = IsDraftFilterActive!
                    ? StatusItems.FirstOrDefault(i => i.FilterValue == AllStatusFilterValue)
                    : StatusItems.FirstOrDefault(i => string.Equals(i.FilterValue, "Draft", StringComparison.OrdinalIgnoreCase))
                      ?? StatusItems.FirstOrDefault();
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
            await LoadStatusSummaryAsync();
        }

        private async Task HandleSearchTextChangeAsync()
        {
            if (_isInitializing) return;
            CancelCurrentLoad();
            _loadCancellation = new CancellationTokenSource();

            try
            {
                await Task.Delay(500, _loadCancellation.Token);
                _currentPage = 1;
                await LoadDataAsync(_loadCancellation.Token, ++_loadSequence);
                await LoadStatusSummaryAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Search operation cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during search");
            }
        }

        private async Task HandleDateFilterToggleAsync()
        {
            if (!_isComboBoxInitialized || _isInitializing) return;

            // Menyalakan filter sekaligus memeriksa isian tanggalnya, sama seperti
            // saat operator meninggalkan kotak tanggal; mematikannya cukup memuat
            // ulang daftar tanpa batas tanggal.
            if (IsDateFilterEnabled)
            {
                await HandleDateChangeAsync();
                return;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
        }

        private async Task HandleDateChangeAsync()
        {
            if (!_isComboBoxInitialized || _isInitializing || !IsDateFilterEnabled) return;

            if (!BacaRentangTanggal(out string pesanTanggal))
            {
                Pesan.Peringatan("Filter tanggal belum sah", pesanTanggal, "filter-tanggal");
                return;
            }

            if (_tanggalMulaiSah is { } mulai && _tanggalSelesaiSah is { } selesai && mulai > selesai)
            {
                Pesan.Peringatan("Filter tanggal belum tepat",
                    "Tanggal mulai tidak boleh lebih baru dari tanggal selesai. Urutkan dulu tanggalnya, ya.",
                    "filter-tanggal");
                return;
            }

            _currentPage = 1;
            await LoadDataSafeAsync();
        }

        private async Task LoadStatusSummaryAsync()
        {
            try
            {
                var filters = BuildFilterConditions();
                Dictionary<string, int> counts;

                // SATU query GROUP BY untuk seluruh status (dulu satu query COUNT per
                // status) — halaman terbuka/berganti filter jauh lebih ringan.
                // Serialisasi via _dbGate: hindari query konkuren pada koneksi bersama.
                await _dbGate.WaitAsync();
                try
                {
                    counts = await _unitOfWork.SuratRepository.CountByStatusAsync(filters);
                }
                finally
                {
                    _dbGate.Release();
                }

                // Status yang belum punya baris tetap muncul dengan angka 0.
                foreach (var status in SuratConstants.ValidStatus)
                {
                    counts.TryAdd(status, 0);
                }

                ActiveSuratCount = counts.GetValueOrDefault("Active", 0);
                DraftSuratCount = counts.GetValueOrDefault("Draft", 0);
                CancelledSuratCount = counts.GetValueOrDefault("Cancelled", 0);
                TotalSuratCount = ActiveSuratCount + DraftSuratCount + CancelledSuratCount;
                OnPropertyChanged(nameof(DraftBadgeText));
                OnPropertyChanged(nameof(HasDraftBadge));

                if (counts.Any())
                {
                    var summaryText = string.Join(" | ", counts.Select(kvp => $"{GetStatusDisplayName(kvp.Key)}: {kvp.Value}"));
                    StatusSummary = $"Status: {summaryText} | Total: {TotalSuratCount}";
                }
                else
                {
                    StatusSummary = "Tidak ada data statistik status.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load status summary");
                StatusSummary = "Gagal memuat statistik status.";
            }
        }


    }
}
