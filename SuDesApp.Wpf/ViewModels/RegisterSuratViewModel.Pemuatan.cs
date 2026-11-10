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
        private async Task LoadDataSafeAsync()
        {
            CancelCurrentLoad();
            _loadCancellation = new CancellationTokenSource();
            int seq = ++_loadSequence;

            try
            {
                await LoadDataAsync(_loadCancellation.Token, seq);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Data load cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load data");
                // Hanya muat kosong bila masih load terbaru (latest-wins).
                if (seq == _loadSequence)
                {
                    UpdateUIWithEmptyData();
                }
            }
        }

        private async Task LoadDataAsync(CancellationToken cancellationToken, int seq)
        {
            _logger.LogDebug("Loading data page {Page}...", _currentPage);
            IsLoading = true;
            UpdateCommandStates();

            try
            {
                if (_unitOfWork.SuratRepository == null)
                {
                    _logger.LogError("SuratRepository is null");
                    return;
                }

                var filters = BuildFilterConditions();
                _logger.LogDebug("Filters: {@Filters}", filters);

                // PENTING: query dijalankan BERURUTAN + di bawah _dbGate.
                // Microsoft.Data.Sqlite tidak thread-safe untuk query konkuren
                // pada satu koneksi bersama — konkurensi memicu korupsi native
                // (crash 0x800703E9 stack overflow di luar kode user).
                List<SuratData> data;
                int total;
                await _dbGate.WaitAsync(cancellationToken);
                try
                {
                    data = (await _unitOfWork.SuratRepository.GetFilteredAsync(
                        filters,
                        sortBy: _sortBy,
                        ascending: _sortAscending,
                        skip: (_currentPage - 1) * PageSize,
                        take: PageSize,
                        cancellationToken: cancellationToken)).ToList();

                    total = await _unitOfWork.SuratRepository.CountAsync(filters, cancellationToken);
                }
                finally
                {
                    _dbGate.Release();
                }

                // Buang hasil basi bila sudah ada load yang lebih baru (latest-wins).
                if (seq != _loadSequence)
                {
                    _logger.LogDebug("Discarding stale load #{Seq} (current #{Current})", seq, _loadSequence);
                    return;
                }

                _currentDisplayData = data;
                _totalRecords = total;

                _logger.LogDebug("Loaded {Count} records, total {Total}", _currentDisplayData.Count, _totalRecords);

                UpdateDataGridViewAndPaging();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data");
                if (seq == _loadSequence)
                {
                    UpdateUIWithEmptyData();
                }
            }
            finally
            {
                if (seq == _loadSequence)
                {
                    IsLoading = false;
                    UpdateCommandStates();
                }
            }
        }

        private FilterConditions BuildFilterConditions()
        {
            var filters = new FilterConditions();

            try
            {
                var selectedJenis = SelectedJenisSurat;
                if (selectedJenis?.FilterValue != null && selectedJenis.FilterValue != AllTypesFilterValue)
                {
                    filters.JenisSurat = selectedJenis.FilterValue;
                }

                // Register Surat umum memisahkan NTCR: jenis NTCR tidak tampil di
                // daftar umum (di Register NTCR justru hanya NTCR yang tampil).
                var excluded = ExcludedJenisNames;
                if (excluded != null && excluded.Count > 0)
                {
                    filters.ExcludeJenisNames = new List<string>(excluded);
                }

                var selectedStatus = SelectedStatus;
                if (selectedStatus?.FilterValue != null && selectedStatus.FilterValue != AllStatusFilterValue)
                {
                    filters.Status = selectedStatus.FilterValue;
                }

                if (!string.IsNullOrWhiteSpace(SelectedTahun) && SelectedTahun != "Semua Tahun")
                {
                    filters.Tahun = SelectedTahun;
                }

                if (IsDateFilterEnabled)
                {
                    // Isian yang belum sah/belum diisi tidak ikut menyaring; pesan
                    // galatnya muncul saat operator meninggalkan kotak tanggalnya.
                    if (_tanggalMulaiSah.HasValue) filters.TanggalMulai = _tanggalMulaiSah;
                    if (_tanggalSelesaiSah.HasValue) filters.TanggalSelesai = _tanggalSelesaiSah;
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    filters.SearchText = SearchText.Trim();
                }

                return filters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build filters");
                return new FilterConditions();
            }
        }

        private void UpdateDataGridViewAndPaging()
        {
            SuratItems.Clear();
            foreach (var surat in _currentDisplayData)
            {
                var statusValue = string.IsNullOrWhiteSpace(surat.Status) ? "Draft" : surat.Status;
                var model = NewDisplayModel();
                model.ID_Surat = surat.ID_Surat;
                model.TanggalSurat = surat.TanggalSurat;
                model.NomorSurat = surat.NomorSurat ?? "-";
                model.NamaDisplay = GetNamaDisplay(surat);
                model.TTLDisplay = GetTTLDisplay(surat);
                model.JenisKelaminDisplay = GetJenisKelaminDisplay(surat);
                model.AlamatDisplay = GetAlamatDisplay(surat);
                model.Keperluan = surat.Keperluan ?? "-";
                model.NamaJenis = surat.NamaJenis ?? "-";
                model.JenisSuratLabel = GetJenisSuratLabel(surat);
                model.Status = statusValue;
                model.StatusDisplay = GetStatusDisplayName(statusValue);
                model.CreatedAt = surat.CreatedAt == default ? DateTime.Now : surat.CreatedAt;
                model.UpdatedAt = surat.UpdatedAt;
                model.OriginalSuratData = surat;

                // Register turunan (mis. Register NTCR) menambah kolomnya sendiri.
                FillDisplayModelExtras(model, surat);

                SuratItems.Add(model);
            }

            UpdatePagingControls();
        }

        /// <summary>Buat model baris register. Register NTCR memakai modelnya sendiri.</summary>
        protected virtual SuratDisplayModel NewDisplayModel() => new SuratDisplayModel();

        /// <summary>
        /// Isi kolom tambahan yang hanya ada di register turunan. Register Surat umum
        /// tidak menambahkan apa pun.
        /// </summary>
        protected virtual void FillDisplayModelExtras(SuratDisplayModel model, SuratData surat)
        {
        }

        /// <summary>
        /// Register yang dibuka kembali setelah form edit ditutup — Register Surat
        /// kembali ke Register Surat, Register NTCR kembali ke Register NTCR.
        ///
        /// Scope-nya dibuka lewat <see cref="NavigationService.BukaHalaman"/> dan
        /// baru ditutup saat Register baru benar-benar ditampilkan (Navigate).
        /// Jangan buat <c>_serviceProvider.CreateScope()</c> lokal: tanpa pemilik,
        /// <c>SqliteConnection</c> scoped-nya tak pernah dilepas dan handle SQLite
        /// native tetap terpegang sampai proses mati.
        /// </summary>
        protected virtual object CreateRegisterForReturn() =>
            _navigation.BukaHalaman().GetRequiredService<RegisterSuratViewModel>();

        private void UpdatePagingControls()
        {
            PageInfo = $"Halaman {_currentPage} dari {TotalPages} ({_totalRecords} total)";
            OnPropertyChanged(nameof(CanPreviousPage));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(HasData));
        }

        private void UpdateUIWithEmptyData()
        {
            _currentDisplayData = new List<SuratData>();
            _totalRecords = 0;
            UpdateDataGridViewAndPaging();
        }

        private void CancelCurrentLoad()
        {
            try
            {
                if (_loadCancellation != null && !_loadCancellation.IsCancellationRequested)
                    _loadCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("CancellationTokenSource already disposed");
            }
            finally
            {
                _loadCancellation?.Dispose();
                _loadCancellation = null;
            }
        }


    }
}
