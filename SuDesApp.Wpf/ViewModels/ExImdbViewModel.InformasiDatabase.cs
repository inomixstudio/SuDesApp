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
        // Informasi database yang sedang dipakai
        // =====================================================================

        private string _lokasiDatabase = "-";
        private string _namaDatabase = "-";
        private string _ukuranDatabase = "-";
        private string _diubahDatabase = "-";
        private string _statusDatabase = "Tidak ditemukan";
        private bool _databaseAda;

        public string LokasiDatabase { get => _lokasiDatabase; private set => SetProperty(ref _lokasiDatabase, value); }
        public string NamaDatabase { get => _namaDatabase; private set => SetProperty(ref _namaDatabase, value); }
        public string UkuranDatabase { get => _ukuranDatabase; private set => SetProperty(ref _ukuranDatabase, value); }
        public string DiubahDatabase { get => _diubahDatabase; private set => SetProperty(ref _diubahDatabase, value); }
        public string StatusDatabase { get => _statusDatabase; private set => SetProperty(ref _statusDatabase, value); }
        public bool DatabaseAda { get => _databaseAda; private set => SetProperty(ref _databaseAda, value); }

        /// <summary>Baca lokasi, ukuran, dan waktu ubah database yang sedang aktif.</summary>
        public void MuatInfoDatabase()
        {
            try
            {
                var path = _service.GetDatabasePath();
                LokasiDatabase = path;
                NamaDatabase = Path.GetFileName(path);

                var berkas = new FileInfo(path);
                DatabaseAda = berkas.Exists;
                StatusDatabase = berkas.Exists ? "Terbaca" : "Tidak ditemukan";
                UkuranDatabase = berkas.Exists ? FormulirTemplateUkuran(berkas.Length) : "belum ada berkas";
                DiubahDatabase = berkas.Exists ? berkas.LastWriteTime.ToString("dd MMM yyyy HH:mm") : "-";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca info database");
                LokasiDatabase = "-";
                NamaDatabase = "-";
                UkuranDatabase = "-";
                DiubahDatabase = "-";
                StatusDatabase = "Tidak ditemukan";
                DatabaseAda = false;
            }
        }

        private void IsiNamaBerkasEksporDefault()
        {
            if (_exportPath.Trim().Length > 0) return;

            try
            {
                var dir = Path.GetDirectoryName(_service.GetDatabasePath());
                var folder = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)
                    ? dir!
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                ExportPath = Path.Combine(folder, CadanganDatabase.NamaEksporBawaan(DateTime.Now));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nama berkas ekspor bawaan tidak dapat disusun");
            }
        }

    }
}
