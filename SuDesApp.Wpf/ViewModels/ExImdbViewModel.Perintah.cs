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
        // Perintah
        // =====================================================================

        public ICommand EksporCommand { get; }
        public ICommand ImporCommand { get; }
        public ICommand PilihTujuanEksporCommand { get; }
        public ICommand PilihSumberImporCommand { get; }
        public ICommand KonfirmasiImporCommand { get; }
        public ICommand BatalImporCommand { get; }

        /// <summary>Buka folder database di Windows Explorer — berguna untuk mencari berkas hasil ekspor/backup.</summary>
        public ICommand BukaFolderDatabaseCommand { get; }

        private void SalinLokasiDatabase()
        {
            try
            {
                if (LokasiDatabase == "-" || LokasiDatabase.Length == 0)
                {
                    StatusText = "Lokasi database belum terbaca.";
                    return;
                }

                System.Windows.Clipboard.SetText(LokasiDatabase);
                StatusText = "Lokasi database disalin ke papan klip.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lokasi database tidak dapat disalin");
                StatusText = "Lokasi database tidak dapat disalin.";
            }
        }

        /// <summary>
        /// Cadangan sekali klik: berkas dibuat di folder database dengan nama
        /// bertanggal. Nama yang sudah terpakai tidak ditimpa — diberi akhiran
        /// angka supaya cadangan sebelumnya tetap utuh.
        /// </summary>
        private async Task BuatCadanganCepatAsync()
        {
            if (IsExporting || IsImporting) return;

            string folder;
            try
            {
                folder = Path.GetDirectoryName(_service.GetDatabasePath()) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Folder database tidak terbaca untuk cadangan cepat");
                TampilkanPesan("Cadangan gagal", "Folder database tidak dapat dibaca.", berhasil: false,
                    "Gunakan bagian Ekspor lalu pilih lokasi cadangan secara manual.");
                return;
            }

            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                StatusText = "Folder database tidak ditemukan.";
                TampilkanPesan("Cadangan gagal", "Folder database tidak ditemukan.", berhasil: false,
                    "Gunakan bagian Ekspor untuk memilih folder cadangan lain.");
                return;
            }

            var dasar = CadanganDatabase.NamaEksporBawaan(DateTime.Now);
            var path = Path.Combine(folder, dasar);
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(folder, Path.GetFileNameWithoutExtension(dasar) + "_" + i + ".db");
            }

            ExportPath = path;
            await JalankanEksporAsync(path);
        }

        private void BukaFolderDatabase()
        {
            try
            {
                var folder = Path.GetDirectoryName(_service.GetDatabasePath());
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    StatusText = "Folder database tidak ditemukan.";
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                StatusText = "Folder database dibuka: " + folder;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder database");
                StatusText = "Folder database tidak dapat dibuka dari sini.";
            }
        }

        private void RaiseCommandStates()
        {
            OnPropertyChanged(nameof(AdaKesibukan));
            OnPropertyChanged(nameof(TampilTombolImpor));
            (EksporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PilihTujuanEksporCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PilihSumberImporCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (KonfirmasiImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PulihkanCadanganCommand as RelayCommand<CadanganTampil>)?.RaiseCanExecuteChanged();
            (BuatCadanganCepatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Pilih lokasi berkas cadangan baru (SaveFileDialog — pemilih berkas, bukan popup kabar).</summary>
        private void PilihTujuanEkspor()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Simpan salinan cadangan database",
                Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                FileName = Path.GetFileName(_exportPath.Trim().Length > 0
                    ? _exportPath
                    : CadanganDatabase.NamaEksporBawaan(DateTime.Now)),
                OverwritePrompt = true,
                InitialDirectory = FolderAwal(_exportPath),
            };

            if (dialog.ShowDialog() == true)
            {
                ExportPath = dialog.FileName;
                StatusText = "Tujuan cadangan siap: " + Path.GetFileName(dialog.FileName);
            }
        }

        /// <summary>Pilih berkas cadangan yang akan dipulihkan (OpenFileDialog).</summary>
        private void PilihSumberImpor()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pilih berkas cadangan database",
                Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = FolderAwal(_importPath),
            };

            if (dialog.ShowDialog() == true)
            {
                ImportPath = dialog.FileName;
                AdaKonfirmasiImpor = false;
                StatusText = "Berkas cadangan dipilih: " + Path.GetFileName(dialog.FileName);
            }
        }

        private string FolderAwal(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path.Trim());
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;

                dir = Path.GetDirectoryName(_service.GetDatabasePath());
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch (Exception ex)
            {
                // Jatuh ke folder Dokumen; catat sebabnya agar tidak hilang.
                _logger.LogDebug(ex, "Folder awal ekspor/impor tidak terbaca");
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Langkah 1 impor: periksa berkas lalu minta persetujuan di dalam halaman.</summary>
        private async Task ImporAsync()
        {
            if (IsImporting || IsExporting) return;

            if (!ValidasiSumberImpor(out var pesanSalah))
            {
                StatusText = "Impor belum bisa dijalankan.";
                TampilkanPesan("Berkas cadangan belum siap", pesanSalah!, berhasil: false);
                return;
            }

            AdaPesan = false;

            // Konfirmasi ditampilkan sebagai kartu di halaman (bukan dialog popup),
            // karena impor menimpa database yang sedang dipakai.
            AdaKonfirmasiImpor = true;
            StatusText = "Menunggu konfirmasi impor.";
            await Task.CompletedTask;
        }

        /// <summary>Langkah 2 impor: jalankan setelah pengguna menyetujui di kartu konfirmasi.</summary>
        private async Task JalankanImporAsync()
        {
            if (IsImporting || IsExporting) return;

            AdaKonfirmasiImpor = false;
            var path = _importPath.Trim();

            IsImporting = true;
            StatusText = "Memulihkan database dari cadangan...";
            try
            {
                _logger.LogInformation("Memulai impor database dari: {Path}", path);
                await _service.ImportDatabaseAsync(path);
                MuatInfoDatabase();
                MuatCadanganTerbaru();

                StatusText = "Database berhasil dipulihkan dari cadangan.";
                TampilkanPesan(
                    "Database berhasil dipulihkan",
                    "Seluruh data sekarang memakai isi berkas cadangan yang dipilih.",
                    berhasil: true,
                    "Salinan database lama sebelum impor disimpan di folder database dengan nama " +
                    "desa_backup_before_import_<tanggal>.db — bisa dipakai bila ingin kembali ke keadaan sebelumnya.",
                    $"Lokasi database: {LokasiDatabase}",
                    "Semua data di aplikasi sudah memakai hasil impor — buka ulang halaman lain bila perlu.");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Berkas cadangan tidak ditemukan: {Path}", path);
                StatusText = "Impor gagal: berkas cadangan tidak ditemukan.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Periksa kembali lokasi berkas cadangan, lalu pilih berkasnya lagi.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Berkas cadangan tidak valid: {Path}", path);
                StatusText = "Impor gagal: berkas cadangan tidak valid.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Pastikan berkas berasal dari ekspor aplikasi ini dan belum rusak.",
                    "Database yang sekarang dipakai tidak berubah.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impor database gagal: {Path}", path);
                StatusText = "Impor gagal.";
                TampilkanPesan("Impor gagal", ex.Message, berhasil: false,
                    "Database yang sekarang dipakai tidak berubah — coba lagi atau pilih berkas cadangan lain.");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private bool ValidasiSumberImpor(out string? pesanSalah)
        {
            var path = _importPath.Trim();

            if (path.Length == 0)
            {
                pesanSalah = "Pilih dulu berkas cadangan (.db) yang ingin dipulihkan.";
                return false;
            }

            if (!File.Exists(path))
            {
                pesanSalah = "Berkas cadangan tidak ditemukan pada lokasi tersebut.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                pesanSalah = "Berkas cadangan harus berekstensi .db (hasil ekspor aplikasi ini).";
                return false;
            }

            pesanSalah = null;
            return true;
        }

        /// <summary>Buat salinan cadangan database ke berkas tujuan.</summary>
        private async Task EksporAsync()
        {
            if (IsExporting || IsImporting) return;

            var path = _exportPath.Trim();
            if (path.Length == 0)
            {
                StatusText = "Tujuan cadangan belum diisi.";
                TampilkanPesan("Tujuan cadangan belum diisi", "Tentukan dulu lokasi berkas cadangan.", berhasil: false,
                    "Tekan tombol Pilih Lokasi untuk memilih folder dan nama berkas cadangan.");
                return;
            }

            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Nama berkas cadangan harus berakhiran .db.";
                TampilkanPesan("Nama berkas belum sesuai", "Berkas cadangan harus berekstensi .db.", berhasil: false,
                    "Contoh: desa_cadangan_2026-09-21_1430.db — ubah nama berkasnya lewat tombol Pilih Lokasi.");
                return;
            }

            await JalankanEksporAsync(path);
        }

        /// <summary>Inti ekspor: satu jalur untuk tombol Ekspor dan cadangan sekali klik.</summary>
        private async Task JalankanEksporAsync(string path)
        {
            if (IsExporting || IsImporting) return;

            AdaKonfirmasiImpor = false;
            IsExporting = true;
            StatusText = "Membuat salinan cadangan database...";
            try
            {
                _logger.LogInformation("Memulai ekspor database ke: {Path}", path);
                await _service.ExportDatabaseAsync(path);

                var ukuran = "-";
                try
                {
                    var berkas = new FileInfo(path);
                    if (berkas.Exists) ukuran = FormulirTemplateUkuran(berkas.Length);
                }
                catch (Exception ex)
                {
                    // Ukuran hanya keterangan tambahan; jangan gagalkan ekspor.
                    _logger.LogDebug(ex, "Ukuran berkas cadangan tidak terbaca");
                }

                MuatCadanganTerbaru();
                StatusText = "Cadangan database berhasil dibuat.";
                TampilkanPesan(
                    "Cadangan berhasil dibuat",
                    "Database tersimpan sebagai berkas cadangan yang bisa dipulihkan kapan saja.",
                    berhasil: true,
                    $"Berkas: {path}",
                    $"Ukuran: {ukuran}",
                    "Simpan berkas ini di tempat lain (flashdisk/Drive) supaya aman bila komputer bermasalah.");
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Database aktif tidak ditemukan saat ekspor");
                StatusText = "Cadangan gagal: database aktif tidak ditemukan.";
                TampilkanPesan("Cadangan gagal", ex.Message, berhasil: false,
                    "Pastikan aplikasi masih memakai database seperti biasa, lalu coba lagi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ekspor database gagal: {Path}", path);
                StatusText = "Cadangan gagal dibuat.";
                TampilkanPesan("Cadangan gagal", ex.Message, berhasil: false,
                    "Pastikan folder tujuan bisa ditulis (bukan folder sistem yang dilindungi).",
                    "Coba pilih lokasi lain, misalnya folder Dokumen.");
            }
            finally
            {
                IsExporting = false;
            }
        }

    }
}
