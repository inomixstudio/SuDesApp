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
        private async Task InitializeAsync()
        {
            _isInitializing = true;
            try
            {
                _logger.LogDebug("Loading RegisterSurat data...");
                // Berurutan, bukan Task.WhenAll: seluruh repositori dalam satu
                // scope memakai instance SqliteConnection yang sama, dan
                // Microsoft.Data.Sqlite tidak mendukung pemakaian satu koneksi
                // secara bersamaan.
                await LoadJenisSuratKeComboBoxAsync();
                await LoadStatusKeComboBoxAsync();
                await LoadTahunKeComboBoxAsync();
                _isComboBoxInitialized = true;
                await LoadDataSafeAsync();
                await LoadStatusSummaryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize RegisterSurat");
            }
            finally
            {
                // Event filter dari view (SelectionChanged/TextChanged yang terpicu
                // binding awal) ditunda selama inisialisasi, lalu di-flush SEKALI di sini —
                // data dimuat sekali per gugus perubahan, bukan tumpukan konkuren.
                _isInitializing = false;
                await FlushPendingFilterChangeAsync();
            }
        }

        private async Task LoadJenisSuratKeComboBoxAsync()
        {
            try
            {
                _logger.LogDebug("Loading jenis surat to ComboBox...");
                JenisSuratItems.Clear();

                foreach (var item in await BuildJenisFilterItemsAsync())
                {
                    JenisSuratItems.Add(item);
                }

                SelectedJenisSurat = JenisSuratItems.FirstOrDefault();
                _logger.LogDebug("Loaded {Count} jenis surat", JenisSuratItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load jenis surat");
            }
        }

        /// <summary>
        /// Susun pilihan filter jenis surat untuk register ini. Register Surat umum
        /// memuat semua jenis surat desa kecuali blanko NTCR; Register NTCR
        /// mengoverride-nya dengan daftar blanko N1–N8 saja.
        /// </summary>
        protected virtual async Task<List<FilterItem>> BuildJenisFilterItemsAsync()
        {
            var items = new List<FilterItem>
            {
                new FilterItem { DisplayName = DisplayNameSemuaJenis, FilterValue = AllTypesFilterValue },
                new FilterItem { DisplayName = DisplayNameSuratKeteranganDesa, FilterValue = GroupKeteranganDesaFilterValue }
            };

            var allJenis = await _unitOfWork.JenisSuratRepository.GetAllJenisSuratAsync();
            var allDisplayNames = await _unitOfWork.JenisSuratRepository.GetJenisSuratDisplayNamesAsync();

            var ntcrSet = new HashSet<string>(SuratConstants.NtcrSemua, StringComparer.OrdinalIgnoreCase);

            foreach (var jenis in allJenis.Where(j => j?.NamaJenis != null && !ntcrSet.Contains(j.NamaJenis)))
            {
                var namaJenis = jenis!.NamaJenis!;
                var displayName = allDisplayNames.TryGetValue(namaJenis, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(namaJenis.Replace("_", " ").ToLower());
                items.Add(new FilterItem { DisplayName = displayName, FilterValue = namaJenis.ToUpperInvariant() });
            }

            return items;
        }

        private async Task LoadStatusKeComboBoxAsync()
        {
            try
            {
                _logger.LogDebug("Loading status to ComboBox...");
                StatusItems.Clear();

                var items = new List<FilterItem>
                {
                    new FilterItem { DisplayName = DisplayNameSemuaStatus, FilterValue = AllStatusFilterValue }
                };

                foreach (var status in SuratConstants.ValidStatus)
                {
                    items.Add(new FilterItem
                    {
                        DisplayName = GetStatusDisplayName(status),
                        FilterValue = status
                    });
                }

                foreach (var item in items)
                    StatusItems.Add(item);

                SelectedStatus = StatusItems.FirstOrDefault();
                _logger.LogDebug("Loaded {Count} status items", StatusItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load status");
            }
        }

        private static string GetStatusDisplayName(string status) => status switch
        {
            "Draft" => "Draft",
            "Active" => "Aktif",
            "Cancelled" => "Dibatalkan",
            _ => status
        };

        private async Task LoadTahunKeComboBoxAsync()
        {
            try
            {
                var years = await _unitOfWork.JenisSuratRepository.GetAvailableYearsAsync();
                TahunItems.Clear();
                TahunItems.Add("Semua Tahun");
                foreach (var year in years)
                    TahunItems.Add(year);

                var currentYear = DateTime.Now.Year.ToString();
                SelectedTahun = TahunItems.Contains(currentYear) ? currentYear : "Semua Tahun";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tahun");
                TahunItems.Clear();
                TahunItems.Add("Semua Tahun");
                TahunItems.Add(DateTime.Now.Year.ToString());
                SelectedTahun = "Semua Tahun";
            }
        }


    }
}
