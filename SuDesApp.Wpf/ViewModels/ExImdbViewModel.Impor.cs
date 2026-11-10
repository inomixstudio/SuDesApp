using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class ExImdbViewModel : ObservableObject
    {

        // =====================================================================
        // Impor (kartu 2)
        // =====================================================================

        /// <summary>Berkas cadangan yang akan dipakai memulihkan database.</summary>
        public string ImportPath
        {
            get => _importPath;
            set
            {
                if (SetProperty(ref _importPath, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(KeteranganSumberImpor));
                    OnPropertyChanged(nameof(SumberImporDikenali));
                    PerbaruiInfoSumberImpor();
                }
            }
        }

        public bool SumberImporDikenali => _importPath.Trim().Length > 0;

        // Ringkasan berkas yang dipilih — dipakai petak KPI di kartu Impor.
        private string _sumberImporUkuran = "-";
        private string _sumberImporDiubah = "-";
        private string _sumberImporStatus = "Belum dipilih";
        private bool _sumberImporSiap;

        public string SumberImporUkuran { get => _sumberImporUkuran; private set => SetProperty(ref _sumberImporUkuran, value); }
        public string SumberImporDiubah { get => _sumberImporDiubah; private set => SetProperty(ref _sumberImporDiubah, value); }
        public string SumberImporStatus { get => _sumberImporStatus; private set => SetProperty(ref _sumberImporStatus, value); }
        public bool SumberImporSiap { get => _sumberImporSiap; private set => SetProperty(ref _sumberImporSiap, value); }

        private void PerbaruiInfoSumberImpor()
        {
            var path = _importPath.Trim();
            if (path.Length == 0)
            {
                SumberImporUkuran = "-";
                SumberImporDiubah = "-";
                SumberImporStatus = "Belum dipilih";
                SumberImporSiap = false;
                return;
            }

            try
            {
                var berkas = new FileInfo(path);
                if (berkas.Exists)
                {
                    SumberImporUkuran = FormulirTemplateUkuran(berkas.Length);
                    SumberImporDiubah = berkas.LastWriteTime.ToString("dd MMM yyyy HH:mm");
                    SumberImporStatus = "Siap dipulihkan";
                    SumberImporSiap = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Info berkas cadangan tidak dapat dibaca");
            }

            SumberImporUkuran = "-";
            SumberImporDiubah = "-";
            SumberImporStatus = "Berkas tidak ditemukan";
            SumberImporSiap = false;
        }

        public string KeteranganSumberImpor
        {
            get
            {
                var path = _importPath.Trim();
                if (path.Length == 0)
                {
                    return "Belum ada berkas cadangan dipilih. Tekan Pilih Berkas untuk menunjuk berkas .db hasil ekspor sebelumnya.";
                }

                try
                {
                    var berkas = new FileInfo(path);
                    return berkas.Exists
                        ? $"Berkas {berkas.Name} — {FormulirTemplateUkuran(berkas.Length)}, terakhir diubah {berkas.LastWriteTime:dd MMM yyyy HH:mm}."
                        : "Berkas yang ditunjuk tidak ditemukan. Periksa kembali lokasinya.";
                }
                catch (Exception ex)
                {
                    return "Berkas tidak dapat diperiksa: " + ex.Message;
                }
            }
        }

        private static string FormulirTemplateUkuran(long byteCount)
        {
            if (byteCount <= 0) return "0 KB";
            if (byteCount < 1024 * 1024) return Math.Max(1, byteCount / 1024) + " KB";
            return (byteCount / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

    }
}
