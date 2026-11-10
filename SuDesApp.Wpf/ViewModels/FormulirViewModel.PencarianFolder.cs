using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class FormulirViewModel : ObservableObject, IDisposable
    {
        // =====================================================================
        // Pencarian, pemilihan, dan folder
        // =====================================================================

        /// <summary>Susun ulang daftar tampil menurut kotak pencarian dan hitung ulang penanda.</summary>
        private void RefreshTampilan()
        {
            var kata = _searchText.Trim();

            var tampil = string.IsNullOrEmpty(kata)
                ? Templates.ToList()
                : Templates
                    .Where(t => t.Name.Contains(kata, StringComparison.OrdinalIgnoreCase)
                             || t.KodeFormulir.Contains(kata, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            TemplatesTampil.Clear();
            foreach (var item in tampil)
            {
                TemplatesTampil.Add(item);
            }

            OnPropertyChanged(nameof(JumlahTampil));
            OnPropertyChanged(nameof(AdaPencarian));
            OnPropertyChanged(nameof(KosongKarenaPencarian));
            OnPropertyChanged(nameof(KosongTanpaFormulir));
            OnPropertyChanged(nameof(PesanKosong));
            OnPropertyChanged(nameof(PesanCari));
            OnPropertyChanged(nameof(JudulKosong));
            PerbaruiPenandaPilihan();
        }

        /// <summary>Hitung ulang jumlah terpilih, label tombol, dan keaktifan tombol Hapus.</summary>
        private void PerbaruiPenandaPilihan()
        {
            OnPropertyChanged(nameof(JumlahTerpilih));
            OnPropertyChanged(nameof(AdaTerpilih));
            OnPropertyChanged(nameof(TeksTerpilih));
            OnPropertyChanged(nameof(SemuaTampilTerpilih));
            OnPropertyChanged(nameof(TeksPilihSemua));
            (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Centang/hapus centang seluruh baris yang tampil.</summary>
        private void TogglePilihSemua()
        {
            bool jadikanTerpilih = !SemuaTampilTerpilih;
            foreach (var item in TemplatesTampil)
            {
                item.IsChecked = jadikanTerpilih;
            }

            PerbaruiPenandaPilihan();
        }

        /// <summary>
        /// Buka pratinjau PDF satu formulir di dalam aplikasi (bukan pembaca PDF
        /// bawaan Windows). Bila berkasnya sudah dipindahkan/dihapus, keadaannya
        /// cukup dilaporkan di statusbar halaman — tanpa dialog.
        /// </summary>
        public void BukaFormulir(FormulirTemplate? formulir)
        {
            if (formulir == null) return;

            try
            {
                var jalur = FindTemplateFile(_appConfig.TemplateFolder, formulir.Name);
                if (jalur == null || !File.Exists(jalur))
                {
                    StatusText = $"Berkas {formulir.NamaBerkas} tidak ditemukan di folder Templates.";
                    return;
                }

                if (_navigation is null || _pdfPreviewFactory is null)
                {
                    StatusText = $"Pratinjau {formulir.NamaBerkas} tidak tersedia di halaman ini.";
                    return;
                }

                var pratinjau = _pdfPreviewFactory(formulir.Name);
                _navigation.Navigate(new Views.PdfPreviewView { DataContext = pratinjau });
                StatusText = $"Membuka pratinjau {formulir.NamaBerkas}...";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka pratinjau formulir {Nama}", formulir.NamaBerkas);
                StatusText = $"Berkas {formulir.NamaBerkas} tidak bisa dibuka dari sini.";
            }
        }

        /// <summary>Buka folder Templates di Windows Explorer.</summary>
        private void BukaFolderTemplate()
        {
            try
            {
                var folder = _appConfig.TemplateFolder;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });

                StatusText = $"Folder Templates dibuka: {folder}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka folder Templates");
                StatusText = "Folder Templates tidak dapat dibuka dari sini.";
            }
        }

        public async Task LoadTemplatesAsync()
        {
            try
            {
                IsLoading = true;
                StatusText = "Membaca folder Templates...";
                Templates.Clear();

                // Sumber formulir mengikuti kabupaten pada Pengaturan Surat.
                await MuatSumberFormulirAsync();

                var templateFolder = _appConfig.TemplateFolder;
                if (!Directory.Exists(templateFolder))
                {
                    Directory.CreateDirectory(templateFolder);
                }

                // Nama, ukuran, dan tanggal ubah berkas dibaca sekaligus supaya
                // baris daftar bisa menampilkan keterangan yang berguna.
                var berkas = await Task.Run(() => Directory.GetFiles(templateFolder, "*.pdf")
                    .Select(f => new FileInfo(f))
                    .Where(fi => !string.IsNullOrWhiteSpace(fi.Name))
                    .GroupBy(fi => Path.GetFileNameWithoutExtension(fi.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(fi => fi.LastWriteTimeUtc).First())
                    .OrderBy(fi => Path.GetFileNameWithoutExtension(fi.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(fi => (
                        Nama: Path.GetFileNameWithoutExtension(fi.Name),
                        Ukuran: fi.Length,
                        Diubah: fi.LastWriteTime))
                    .ToList());

                foreach (var file in berkas)
                {
                    var item = new FormulirTemplate(
                        file.Nama,
                        GetFormCode(file.Nama),
                        file.Ukuran,
                        file.Diubah);

                    // Tombol Buka pada baris membuka pratinjau PDF-nya di dalam aplikasi.
                    item.SaatDibuka = BukaFormulir;

                    // Perubahan centang langsung memperbarui ringkasan pilihan & tombol Hapus.
                    item.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(FormulirTemplate.IsChecked))
                        {
                            PerbaruiPenandaPilihan();
                        }
                    };

                    Templates.Add(item);
                }

                OnPropertyChanged(nameof(HasTemplates));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(TemplateCount));
                OnPropertyChanged(nameof(RingkasanJumlah));
                OnPropertyChanged(nameof(KosongTanpaFormulir));
                _logger.LogInformation("Memuat {Count} template PDF", Templates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar template");
            }
            finally
            {
                IsLoading = false;
                RefreshTampilan();

                StatusText = Templates.Count == 0
                    ? "Folder Templates masih kosong."
                    : $"{Templates.Count} formulir siap dipakai.";

                // Tawarkan unduhan bila daftar template belum lengkap
                // (sekali per sesi aplikasi) — lewat kartu di halaman, bukan popup.
                TawarkanUnduhBilaBelumLengkap();
            }
        }

        /// <summary>
        /// Tawaran unduh formulir bila jumlah template masih di bawah
        /// <see cref="DownloadPromptThreshold"/>. Ditampilkan sebagai kartu di
        /// dalam halaman (bukan popup) dan hanya sekali per sesi aplikasi supaya
        /// tidak mengganggu setiap kali halaman dibuka.
        /// </summary>
        private void TawarkanUnduhBilaBelumLengkap()
        {
            if (_downloadPromptShown || Templates.Count >= DownloadPromptThreshold)
            {
                return;
            }

            // Tawaran hanya bermakna bila aplikasi memang bisa mengunduh sendiri.
            if (!UnduhanOtomatis)
            {
                return;
            }

            _downloadPromptShown = true;
            TawaranUnduhTeks =
                $"Baru ada {Templates.Count} dari {DownloadPromptThreshold} formulir yang biasa dipakai. " +
                $"Formulir resmi {KabupatenAktif} dapat diunduh langsung dari {SumberFormulirTeks} — " +
                "formulir yang sudah ada di folder Templates tidak akan diunduh ulang.";
            AdaTawaranUnduh = true;
        }

        /// <summary>Tambah template PDF baru (padanan formOpenFileDialog pada dialog Formulir).</summary>
        private async Task AddTemplateAsync()
        {
            // Mode baca-saja: menambah template formulir baru diblokir.
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Pilih file template formulir (PDF)",
                Filter = "PDF Files (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string sourcePath = dialog.FileName;
            if (!_fileService.IsValidPdf(sourcePath))
            {
                StatusText = "Berkas yang dipilih bukan PDF yang valid.";
                TampilkanHasilUnduhan(
                    "Berkas bukan PDF",
                    $"\u201c{Path.GetFileName(sourcePath)}\u201d tidak bisa dipakai sebagai template formulir.",
                    new[] { "Pilih berkas PDF yang bisa dibuka di pembaca PDF, lalu coba lagi." },
                    berhasil: false);
                return;
            }

            var templateFolder = _appConfig.TemplateFolder;
            if (!Directory.Exists(templateFolder))
            {
                Directory.CreateDirectory(templateFolder);
            }

            // Normalisasi nama seperti WinForms: spasi -> underscore, huruf besar.
            string baseName = Path.GetFileNameWithoutExtension(sourcePath)
                .Replace(" ", "_")
                .ToUpperInvariant();
            string destinationPath = Path.Combine(templateFolder, $"{baseName}.pdf");

            if (_fileService.FileExists(destinationPath))
            {
                bool overwrite = await _messageService.ShowConfirmationAsync(
                    "Template sudah ada",
                    $"{baseName}.pdf sudah ada di folder Templates. Timpa file lama?");
                if (!overwrite)
                {
                    return;
                }
            }

            try
            {
                await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite: true));
                _logger.LogInformation("Template ditambahkan: {Path}", destinationPath);
                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();
                StatusText = $"Template {baseName}.pdf ditambahkan.";

                // Backup otomatis ke Drive (bila diaktifkan di Pengaturan Aplikasi).
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyalin template: {Path}", destinationPath);

                // Kesalahan ditampilkan sebagai kartu di halaman, bukan dialog.
                TampilkanHasilUnduhan(
                    "Gagal menyalin template",
                    $"Template {baseName}.pdf tidak bisa disalin ke folder Templates.",
                    new[] { ex.Message }, berhasil: false);
                StatusText = "Gagal menyalin template PDF.";
            }
        }

        /// <summary>Hapus semua template yang dicentang (padanan TombolHapus dialog Formulir).</summary>
        private async Task DeleteSelectedAsync()
        {
            // Mode baca-saja: menghapus template formulir diblokir.
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            var checkedItems = Templates.Where(t => t.IsChecked).ToList();
            if (checkedItems.Count == 0)
            {
                return;
            }

            // Penghapusan berkas tetap meminta konfirmasi — berisiko menghilangkan
            // berkas pengguna, jadi layak dikonfirmasi lebih dulu.
            bool confirmed = await _messageService.ShowConfirmationAsync(
                "Hapus template",
                checkedItems.Count == 1
                    ? $"Hapus template \"{checkedItems[0].Name}\" dari folder Templates?"
                    : $"Hapus {checkedItems.Count} template terpilih dari folder Templates?");
            if (!confirmed)
            {
                return;
            }

            var templateFolder = _appConfig.TemplateFolder;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in checkedItems)
                    {
                        string? filePath = FindTemplateFile(templateFolder, item.Name);
                        if (filePath != null && File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            _logger.LogInformation("Template dihapus: {Path}", filePath);
                        }
                    }
                });

                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();
                StatusText = checkedItems.Count == 1
                    ? $"Template {checkedItems[0].Name}.pdf dihapus."
                    : $"{checkedItems.Count} template dihapus dari folder Templates.";

                // Pastikan sisa template tetap tercadangkan di Drive.
                await TryUploadTemplatesToDriveAsync(templateFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus template");
                StatusText = "Gagal menghapus template.";
                TampilkanHasilUnduhan(
                    "Gagal menghapus template",
                    $"{checkedItems.Count} berkas tidak bisa dihapus dari folder Templates.",
                    new[] { ex.Message }, berhasil: false);
            }
        }

        /// <summary>
        /// Temukan file template PDF untuk nama base tertentu, toleran terhadap
        /// perbedaan huruf besar/kecil pada nama file di folder Templates.
        /// </summary>
        private static string? FindTemplateFile(string templateFolder, string baseName)
        {
            string exact = Path.Combine(templateFolder, $"{baseName}.pdf");
            if (File.Exists(exact))
            {
                return exact;
            }

            return Directory.GetFiles(templateFolder, "*.pdf")
                .FirstOrDefault(f => string.Equals(
                    Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Normalisasi nama file dari judul formulir asing:
        /// huruf besar, spasi -> underscore, karakter tidak diizinkan diganti _.
        /// </summary>
        public static string NormalizeTemplateName(string title)
        {
            var s = title.Trim();
            s = new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            s = Regex.Replace(s, @"\s+", "_");
            if (string.IsNullOrWhiteSpace(s)) s = "FORMULIR";
            return s.ToUpperInvariant();
        }

        /// <summary>
        /// Ambil kode formulir dari judul (mis. "Formulir F-1.02, digunakan
        /// untuk ..." → "F-1.02"). Null bila judul tidak memuat kode.
        /// </summary>
        public static string? GetFormCode(string title)
        {
            var m = Regex.Match(title.Trim(), @"^(?:formulir\s+)?(?<kode>F-\d+(?:\.\d+)?)\b",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["kode"].Value.ToUpperInvariant() : null;
        }

        /// <summary>
        /// Nama dasar file template dari judul formulir halaman sumber.
        /// - Judul dengan kode yang hanya dipakai satu formulir memakai kode
        ///   polos: "F-1.01, Formulir Biodata Keluarga" → "F-1.01".
        /// - Judul dengan kode yang dipakai beberapa formulir (mis. lima varian
        ///   "F-2.01 ...") diberi akhiran deskriptor: "F-2.01_AKTA_KELAHIRAN".
        /// - Judul tanpa kode dipotong di koma pertama lalu dinormalisasi.
        /// </summary>
        public static string BuildTemplateBaseName(string title, bool codeIsShared)
        {
            var m = Regex.Match(title.Trim(), @"^(?:formulir\s+)?(?<kode>F-\d+(?:\.\d+)?)\b[\s,:-]*(?<sisa>.*)$",
                RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                // Tanpa kode: potong di koma pertama agar tidak memuat keterangan.
                var shortTitle = title.Split(',')[0].Trim();
                return NormalizeTemplateName(shortTitle);
            }

            var kode = m.Groups["kode"].Value.ToUpperInvariant();
            if (!codeIsShared)
            {
                return kode;
            }

            // Deskriptor: teks sebelum koma pertama (tanpa tanda kurung),
            // maksimal 4 kata.
            var sisa = m.Groups["sisa"].Value.Split(',')[0]
                .Replace("(", " ").Replace(")", " ").Trim();
            var kata = sisa.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();
            if (kata.Length == 0)
            {
                return kode;
            }

            return $"{kode}_{NormalizeTemplateName(string.Join(' ', kata))}";
        }

        /// <summary>
        /// Unduhan formulir dari sumber resmi Kabupaten Karawang
        /// (infosadaradmindukkarawang.id). Alamatnya sengaja tetap sama; yang
        /// ditegaskan hanyalah cakupan daerahnya.
        /// Alur:
        /// 1. Ambil halaman jenis-formulir.
        /// 2. Parse daftar nama + link.
        /// 3. Untuk tiap formulir yang belum ada di Templates, unduh filenya —
        ///    prioritas dari folder SuDesApp-Formulir di Google Drive (bila user
        ///    login), fallback ke sumber resmi.
        /// 4. Template baru diunggah ke folder SuDesApp-Formulir di Drive agar
        ///    instalasi berikutnya bisa mengambil dari Drive, bukan web lagi.
        /// </summary>
        private async Task DownloadFromSourceAsync()
        {
            if (IsLoading) return;

            // Pengaman terakhir: tombolnya memang disembunyikan untuk daerah
            // tanpa unduhan otomatis, tetapi perintah bisa dipanggil dari tempat
            // lain — jangan sampai halaman INSAN Karawang dipakai untuk desa
            // di kabupaten lain.
            if (!UnduhanOtomatis)
            {
                StatusText = UnduhManual
                    ? $"Unduhan otomatis belum tersedia untuk {KabupatenAktif} — buka situs resminya."
                    : $"Belum ada sumber unduhan resmi untuk {KabupatenAktif} — tambahkan formulir manual.";
                return;
            }

            // Unduhan manual: sembunyikan popup otomatis untuk sesi ini agar
            // tidak muncul lagi tepat setelah proses selesai.
            _downloadPromptShown = true;

            IsLoading = true;
            StatusText = "Mengambil daftar formulir dari sumber resmi...";
            try
            {
                var result = await FetchFormulirSourceListAsync(CancellationToken.None);
                if (!result.Ok)
                {
                    StatusText = "Gagal mengambil daftar formulir.";
                    TampilkanHasilUnduhan(
                        "Gagal mengambil daftar formulir",
                        $"Daftar formulir dari {SumberFormulirTeks} belum bisa dibaca.",
                        new[]
                        {
                            result.ErrorMessage ?? "Penyebabnya tidak diketahui.",
                            "Formulir yang sudah ada di folder Templates tetap bisa dipakai seperti biasa."
                        },
                        berhasil: false);
                    return;
                }

                if (result.Entries.Count == 0)
                {
                    StatusText = "Tidak ada formulir yang ditemukan di halaman sumber.";
                    TampilkanHasilUnduhan(
                        "Daftar formulir kosong",
                        $"Halaman sumber {SumberFormulirTeks} tidak memuat tautan formulir saat diperiksa.",
                        new[] { "Formulir yang ada di folder Templates tetap bisa dipakai seperti biasa." },
                        berhasil: false);
                    return;
                }

                // Kemajuan proses terlihat di statusbar halaman; rinciannya
                // ditampilkan sebagai kartu di halaman, bukan dialog popup.
                StatusText = $"Ditemukan {result.Entries.Count} formulir. Memulai unduh...";
                TampilkanHasilUnduhan(
                    "Mengunduh formulir resmi",
                    $"{result.Entries.Count} formulir ditemukan di {SumberFormulirTeks}. " +
                    "Formulir yang sudah ada di folder Templates akan dilewati.",
                    new[]
                    {
                        CadanganDriveAktif
                            ? $"Formulir diambil dari Google Drive lebih dulu, lalu dari sumber resmi bila belum ada."
                            : "Google Drive belum aktif — formulir diunduh langsung dari sumber resmi."
                    },
                    berhasil: true);

                var templateFolder = _appConfig.TemplateFolder;
                if (!Directory.Exists(templateFolder))
                {
                    Directory.CreateDirectory(templateFolder);
                }

                var driveFiles = await TryGetDriveTemplateFilesAsync(CancellationToken.None);

                int downloaded = 0;
                int skipped = 0;
                int fromDrive = 0;
                var fileResults = new List<string>();
                foreach (var entry in result.Entries)
                {
                    StatusText = $"Memproses: {entry.NamaFormulir}...";
                    var (downloadedPath, fromDriveItem, note) = await DownloadOneTemplateAsync(entry, templateFolder, driveFiles, CancellationToken.None);
                    if (downloadedPath != null)
                    {
                        downloaded++;
                        if (fromDriveItem) fromDrive++;
                        fileResults.Add($"✓ {entry.BaseFileName} ({(fromDriveItem ? "Drive" : "sumber")})");
                    }
                    else
                    {
                        skipped++;
                        fileResults.Add($"– {entry.BaseFileName}: {note}");
                    }
                }

                StatusText = $"Selesai. Berhasil {downloaded}, dilewati {skipped}.";

                // Notifikasi lonceng status bar.
                if (downloaded > 0)
                {
                    _notifications?.Success(
                        "Unduh Formulir",
                        $"{downloaded} formulir baru tersimpan di folder Templates" +
                        (fromDrive > 0 ? $" ({fromDrive} dari Google Drive)." : "."));
                }
                else
                {
                    _notifications?.Info(
                        "Unduh Formulir",
                        $"Tidak ada formulir baru — {skipped} sudah ada di Templates.");
                }
                await LoadTemplatesAsync();
                _formulirMenuService.NotifyTemplatesChanged();

                if (downloaded > 0)
                {
                    await TryUploadTemplatesToDriveAsync(templateFolder);
                }

                StatusText = downloaded > 0
                    ? $"Unduhan selesai — {downloaded} formulir baru siap dipakai."
                    : $"Unduhan selesai — tidak ada formulir baru ({skipped} sudah tersedia).";

                TampilkanHasilUnduhan(
                    downloaded > 0 ? "Unduhan formulir selesai" : "Semua formulir sudah tersedia",
                    $"Berhasil {downloaded}  •  dari Drive {fromDrive}  •  dari sumber {downloaded - fromDrive}  •  " +
                    $"dilewati/gagal {skipped}",
                    fileResults
                        .Append("Folder tujuan: " + templateFolder)
                        .ToList(),
                    berhasil: downloaded > 0 && skipped == 0);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Unduhan dibatalkan.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh formulir dari sumber resmi");
                StatusText = "Unduhan gagal.";
                TampilkanHasilUnduhan(
                    "Unduhan gagal",
                    $"Proses unduh formulir dari {SumberFormulirTeks} terhenti.",
                    new[]
                    {
                        ex.Message,
                        "Periksa koneksi internet lalu tekan Unduh dari Sumber lagi — formulir yang sudah terunduh tidak diulang."
                    },
                    berhasil: false);
            }
            finally
            {
                IsLoading = false;
            }
        }


        /// <summary>
        /// Ambil daftar formulir dari halaman jenis-formulir resmi.
        /// </summary>
        private async Task<FormulirSourceResult> FetchFormulirSourceListAsync(CancellationToken ct)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(SourceUrl, ct);
                var entries = ParseFormulirEntries(html);
                return new FormulirSourceResult
                {
                    Ok = true,
                    Entries = entries,
                };
            }
            catch (OperationCanceledException)
            {
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = "Unduhan daftar dibatalkan."
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gagal menghubungi sumber formulir");
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = $"Tidak dapat menghubungi server sumber formulir.\n\nKode kesalahan: {ex.StatusCode}\n\n" +
                        "Pastikan koneksi internet aktif dan server sumber formulir dapat diakses.\n\n" +
                        "Formulir yang ada di folder Templates tetap tersedia."
                };
            }
            catch (Exception ex) when (ex is TaskCanceledException)
            {
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = "Waktu unduh daftar habis. Server sumber formulir mungkin lambat.\n\n" +
                        "Coba lagi atau gunakan formulir yang sudah ada di Templates."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil daftar formulir");
                return new FormulirSourceResult
                {
                    Ok = false,
                    ErrorMessage = $"Gagal mengambil daftar formulir.\n\nDetail: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parse nama dan link formulir dari halaman HTML sumber.
        /// Mendukung dua pola link:
        /// 1. Link Google Drive share (drive.google.com/file/d/ID/...) — pola yang
        ///    dipakai halaman jenis-formulir; nama formulir diambil dari teks
        ///    paragraf di dalam anchor.
        /// 2. Link file dokumen langsung (.pdf/.doc/.docx).
        /// </summary>
        private static IReadOnlyList<FormulirSourceEntry> ParseFormulirEntries(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return Array.Empty<FormulirSourceEntry>();
            }

            var results = new List<FormulirSourceEntry>();

            var anchorPattern = new Regex(
                @"<a\s[^>]*href\s*=\s*""(?<url>[^""]+)""[^>]*>(?<inner>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

            foreach (Match m in anchorPattern.Matches(html))
            {
                var url = m.Groups["url"].Value.Trim();
                if (string.IsNullOrEmpty(url)) continue;

                // Teks nama formulir: gabungkan teks polos di dalam anchor.
                var innerText = Regex.Replace(m.Groups["inner"].Value, @"<script.*?</script>|<style.*?</style>", " ",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                innerText = Regex.Replace(innerText, @"<[^>]+>", " ");
                innerText = System.Net.WebUtility.HtmlDecode(innerText);
                innerText = Regex.Replace(innerText, @"\s+", " ").Trim();

                // Pola 1: Google Drive share link.
                var driveMatch = Regex.Match(url, @"drive\.google\.com/(?:file/d/|open\?id=)(?<id>[-\w]{10,})",
                    RegexOptions.IgnoreCase);
                if (driveMatch.Success)
                {
                    var driveId = driveMatch.Groups["id"].Value;
                    var name = FirstMeaningfulLine(innerText);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "FORMULIR_" + driveId;
                    }

                    results.Add(new FormulirSourceEntry
                    {
                        NamaFormulir = name,
                        // URL unduh langsung untuk file publik; endpoint
                        // usercontent + confirm=t menghindari halaman perantara
                        // (virus-scan notice) sehingga selalu mengembalikan PDF.
                        LinkDownload = $"https://drive.usercontent.google.com/download?id={driveId}&export=download&confirm=t",
                    });
                    continue;
                }

                // Pola 2: link dokumen langsung (.pdf/.doc/.docx).
                var lowerUrl = url.ToLowerInvariant();
                if (lowerUrl.EndsWith(".pdf") || lowerUrl.EndsWith(".doc") || lowerUrl.EndsWith(".docx"))
                {
                    var name = FirstMeaningfulLine(innerText);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = Path.GetFileNameWithoutExtension(url);
                    }
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    results.Add(new FormulirSourceEntry
                    {
                        NamaFormulir = name,
                        LinkDownload = url,
                    });
                }
            }

            // Tetapkan nama dasar file: kode yang dipakai satu formulir saja
            // memakai kode polos; kode bersama diberi akhiran deskriptor.
            var codeCounts = results
                .Select(e => GetFormCode(e.NamaFormulir))
                .Where(c => c != null)
                .GroupBy(c => c!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var e in results)
            {
                var code = GetFormCode(e.NamaFormulir);
                var shared = code != null && codeCounts[code] > 1;
                e.BaseFileName = BuildTemplateBaseName(e.NamaFormulir, shared);
            }

            return results
                .GroupBy(e => e.BaseFileName + "|" + e.LinkDownload, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(e => e.BaseFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Ambil baris teks bermakna pertama dari teks anchor (buang teks
        /// navigasi generik seperti "Home", "Download", dsb.).
        /// </summary>
        private static string? FirstMeaningfulLine(string text)
        {
            var generik = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "home", "download", "unduh", "beranda", "menu", "login", "daftar"
            };

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Length < 3 || generik.Contains(t)) continue;
                if (t.Length > 200) t = t.Substring(0, 200).Trim();
                return t;
            }
            return null;
        }

        /// <summary>
        /// Halaman sumber formulir resmi yang dipakai tombol "Unduh dari Sumber".
        /// Alamatnya tetap halaman jenis-formulir INSAN Karawang — formulir
        /// adminduk untuk kabupaten lain mengikuti Permendagri 109/2019 yang sama,
        /// sehingga contoh tautannya cukup ditampilkan di halaman Tentang.
        /// </summary>
        private static readonly Uri SourceUrl = new("https://infosadaradmindukkarawang.id/insankarawang/page/jenis-formulir");

        /// <summary>
        /// Unduh satu template dari entri sumber.
        /// Prioritas: Google Drive (cadangan backup sebelumnya) → sumber resmi.
        /// Penulisan file atomik: tulis ke .tmp, validasi, baru di-rename.
        /// Kembalikan (path, dariDrive, catatan); path null berarti gagal/dilewati.
        /// </summary>
        private async Task<(string? Path, bool FromDrive, string Note)> DownloadOneTemplateAsync(
            FormulirSourceEntry entry, string templateFolder,
            IReadOnlyDictionary<string, DriveItem>? driveFiles, CancellationToken ct)
        {
            var fileName = entry.BaseFileName + ".pdf";
            var destPath = Path.Combine(templateFolder, fileName);

            if (_fileService.FileExists(destPath))
            {
                _logger.LogInformation("Template sudah ada, dilewati: {Name}", fileName);
                return (null, false, "sudah ada");
            }

            // 1) Coba dari Google Drive dulu (folder SuDesApp-Formulir hasil
            //    backup otomatis). Bila tersedia, web sumber tidak perlu diakses.
            if (driveFiles != null && driveFiles.TryGetValue(entry.BaseFileName, out var driveItem) && !driveItem.IsFolder)
            {
                StatusText = $"Mengunduh dari Google Drive: {entry.NamaFormulir}...";
                try
                {
                    var tmpPath = destPath + ".tmp";
                    await _driveService!.DownloadToFileAsync(driveItem.Id, tmpPath, null, ct);
                    if (_fileService.IsValidPdf(tmpPath))
                    {
                        File.Move(tmpPath, destPath, overwrite: true);
                        _logger.LogInformation("Template diunduh dari Google Drive: {Name} (ID: {Id})", fileName, driveItem.Id);
                        return (destPath, true, "");
                    }

                    File.Delete(tmpPath);
                    _logger.LogWarning("File dari Google Drive bukan PDF valid: {Name}", driveItem.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal mengunduh dari Google Drive: {Name}; fallback ke sumber resmi.", fileName);
                    if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp");
                }
            }

            // 2) Fallback: unduh dari sumber resmi.

            // Jika link relatif, gabungkan dengan base URL
            var downloadUrl = entry.LinkDownload;
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absoluteUrl))
            {
                absoluteUrl = new Uri(SourceUrl, downloadUrl);
            }

            StatusText = $"Mengunduh: {entry.NamaFormulir}...";
            var tmp = destPath + ".tmp";
            try
            {
                using var response = await _httpClient.GetAsync(absoluteUrl, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await stream.CopyToAsync(fileStream, ct);
                await fileStream.FlushAsync(ct);
                fileStream.Close();

                // Validasi hasil unduhan berupa PDF SEBELUM dipakai.
                if (!_fileService.IsValidPdf(tmp))
                {
                    var head = ReadFileHead(tmp);
                    File.Delete(tmp);
                    _logger.LogWarning("File yang diunduh bukan PDF valid ({Head}): {Url}", head, absoluteUrl);
                    return (null, false, $"bukan PDF valid (awalan: {head})");
                }

                File.Move(tmp, destPath, overwrite: true);
                _logger.LogInformation("Template diunduh dari sumber: {Name} <- {Url}", fileName, absoluteUrl);
                return (destPath, false, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengunduh template: {Name} <- {Url}", entry.NamaFormulir, absoluteUrl);
                if (File.Exists(tmp)) File.Delete(tmp);
                return (null, false, ex.Message);
            }
        }

        /// <summary>Baca 16 byte pertama file untuk diagnosa konten unduhan.</summary>
        private static string ReadFileHead(string path)
        {
            try
            {
                var bytes = new byte[16];
                using var fs = File.OpenRead(path);
                var n = fs.Read(bytes, 0, bytes.Length);
                return System.Text.Encoding.ASCII.GetString(bytes, 0, n)
                    .Replace("\r", " ").Replace("\n", " ");
            }
            catch
            {
                return "(tidak terbaca)";
            }
        }

        /// <summary>
        /// Ambil indeks file template dari folder SuDesApp-Formulir di Google
        /// Drive (kunci: nama file tanpa ekstensi, huruf besar). Kembalikan null
        /// bila user belum login Drive atau terjadi kesalahan — pemanggil lalu
        /// fallback ke sumber resmi.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, DriveItem>?> TryGetDriveTemplateFilesAsync(CancellationToken ct)
        {
            if (!IsDriveReady)
            {
                return null;
            }

            try
            {
                var folderId = await _driveService!.FindOrCreateFolderAsync(DriveFormulirFolderName, null, ct);
                var items = await _driveService.ListItemsAsync(folderId, ct);

                var map = new Dictionary<string, DriveItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    if (item.IsFolder) continue;

                    var key = Path.GetFileNameWithoutExtension(item.Name);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        map[key.ToUpperInvariant()] = item;
                    }
                }

                _logger.LogInformation("Indeks template Drive: {Count} file di folder {Folder}", map.Count, DriveFormulirFolderName);
                return map;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil daftar template dari Google Drive; fallback ke sumber resmi.");
                return null;
            }
        }

        /// <summary>
        /// Jika user login Google Drive, unggah template yang belum ada ke folder
        /// SuDesApp-Formulir di Drive sebagai cadangan — sehingga instalasi lain
        /// (atau komputer ini di kemudian hari) bisa mengambil dari Drive tanpa
        /// menyentuh web sumber lagi.
        /// </summary>
        private async Task TryUploadTemplatesToDriveAsync(string templateFolder)
        {
            if (!IsDriveReady || !AppPreferenceStore.IsAutoBackupFormulirDriveEnabled())
            {
                return;
            }

            try
            {
                StatusText = "Menyinkronkan template ke Google Drive...";
                var targetFolderId = await _driveService!.FindOrCreateFolderAsync(DriveFormulirFolderName, null, CancellationToken.None);

                // Hindari duplikat: lewati file yang sudah ada di folder Drive.
                var existing = await _driveService.ListItemsAsync(targetFolderId, CancellationToken.None);
                var existingNames = existing.Where(i => !i.IsFolder)
                    .Select(i => i.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(templateFolder, "*.pdf"))
                {
                    var name = Path.GetFileName(file);
                    if (existingNames.Contains(name)) continue;

                    try
                    {
                        await _driveService.UploadFileAsync(file, targetFolderId, fileName: name, ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Gagal mengunggah template ke Drive: {File}", name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengunggah template ke Google Drive");
            }
        }

    }
}
