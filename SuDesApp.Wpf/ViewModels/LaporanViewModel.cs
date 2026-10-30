using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu tabel laporan siap tampil di layar pratinjau.</summary>
    public class LaporanTabelBaris
    {
        public string Label { get; init; } = string.Empty;
        public string Nilai { get; init; } = "-";
        public string Persentase { get; init; } = string.Empty;
    }

    /// <summary>Pratinjau satu tabel laporan beserta judulnya.</summary>
    public class LaporanPratinjau
    {
        public string Judul { get; init; } = string.Empty;
        public string? Catatan { get; init; }
        public ObservableCollection<LaporanTabelBaris> Baris { get; init; } = new();
    }

    /// <summary>Checkbox satu tabel laporan pada dialog "pilih isi laporan".</summary>
    public class LaporanTabelPilihan : ObservableObject
    {
        private bool _dipakai = true;

        public JenisTabelLaporan Jenis { get; init; }
        public string Judul { get; init; } = string.Empty;

        /// <summary>
        /// Status centang tabel ini. Wajib memberi tahu UI (INotifyPropertyChanged):
        /// tanpa notifikasi, tombol "Pilih semua"/"Hapus semua" mengubah nilai di
        /// ViewModel tetapi checkbox di layar diam di tempat sehingga tombol
        /// terlihat tidak berfungsi.
        /// </summary>
        public bool Dipakai
        {
            get => _dipakai;
            set => SetProperty(ref _dipakai, value);
        }

        public string Keterangan { get; init; } = string.Empty;
    }

    /// <summary>
    /// Halaman Laporan Penduduk: rekapitulasi kependudukan yang bisa langsung
    /// dicetak sebagai PDF atau disimpan sebagai Excel.
    ///
    /// Angka yang tampil di layar, angka di PDF, dan angka di Excel semuanya berasal
    /// dari satu objek <see cref="LaporanPendudukData"/> yang disusun sekali lewat
    /// <c>ILaporanPendudukService</c> — bukan dihitung ulang di masing-masing
    /// keluaran. Kalau tidak begitu, laporan yang dibagikan ke dinas bisa
    /// berbeda dengan yang terlihat di layar.
    /// </summary>
    public class LaporanViewModel : ObservableObject
    {
        private readonly ILaporanPendudukService _laporan;
        private readonly AppConfig _appConfig;
        private readonly NavigationService _navigasi;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly ActivityLogService _activityLog;
        private readonly IMessageService _messageService;
        private readonly ILogger<LaporanViewModel> _logger;

        private bool _isBusy;
        private string _statusText = string.Empty;
        private string _periodeTampil = string.Empty;
        private string _galatMuat = string.Empty;
        private LaporanPendudukData? _data;

        public LaporanViewModel(
            ILaporanPendudukService laporan,
            AppConfig appConfig,
            NavigationService navigasi,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            ActivityLogService activityLog,
            IMessageService messageService,
            ILogger<LaporanViewModel> logger)
        {
            _laporan = laporan ?? throw new ArgumentNullException(nameof(laporan));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _navigasi = navigasi ?? throw new ArgumentNullException(nameof(navigasi));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            MuatUlangCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusy);
            // Cetak PDF kini menjalankan alur pratinjau (tombol "Pratinjau"
            // tersendiri sudah dihapus): susun PDF → halaman pratinjau SudesPdf,
            // tempat tombol Cetak dan Ekspor berada. Simpan-ke-berkas tidak lagi
            // menjadi alur terpisah.
            CetakPdfCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy);
            SimpanExcelCommand = new AsyncRelayCommand(() => SimpanAsync("xlsx"), () => !IsBusy);
            AlihkanSemuaCommand = new RelayCommand(() => SetSemuaTabel(true));
            MatikanSemuaCommand = new RelayCommand(() => SetSemuaTabel(false));
        }

        // ----- Identitas halaman -----
        public string HeaderTitle => "LAPORAN PENDUDUK";
        public string HeaderSubtitle =>
            "Rekapitulasi kependudukan desa per tanggal laporan, siap dicetak sebagai PDF atau Excel.";

        // ----- Status & Busy -----
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    PerbaruiStatusCommand();
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>
        /// Cakupan data yang dicetak di kop laporan. Laporan Penduduk saat ini
        /// mencakup seluruh warga aktif dan baru, jadi kalimatnya tetap —
        /// hanya read-only supaya tidak terlihat bisa diubah padahal tidak ada
        /// filter di layar ini.
        /// </summary>
        public string KeteranganCakupan => "Seluruh warga";

        /// <summary>Periode laporan terakhir yang dimuat, mis. "Per 27 September 2026".</summary>
        public string PeriodeTampil
        {
            get => _periodeTampil;
            private set => SetProperty(ref _periodeTampil, value);
        }

        // ===================== Keadaan pemuatan =====================

        /// <summary>
        /// Sebab laporan terakhir gagal disusun; kosong bila berhasil. Disimpan
        /// terpisah dari <see cref="StatusText"/> karena baris status ada di dalam
        /// kartu pratinjau yang justru tidak tampil saat pemuatan gagal.
        /// </summary>
        public string GalatMuat
        {
            get => _galatMuat;
            private set
            {
                if (SetProperty(ref _galatMuat, value))
                {
                    OnPropertyChanged(nameof(AdaGalatMuat));
                    OnPropertyChanged(nameof(TampilkanKartuKeadaan));
                    OnPropertyChanged(nameof(JudulKeadaan));
                    OnPropertyChanged(nameof(SebabKeadaan));
                }
            }
        }

        public bool AdaGalatMuat => _galatMuat.Length > 0;

        /// <summary>
        /// Tidak ada angka yang bisa ditampilkan dan tidak sedang menghitung:
        /// tempat kartu rekapitulasi dan pratinjau digantikan satu kartu keadaan.
        /// Tanpa ini halaman terbuka kosong setelah dialog galat ditutup.
        /// </summary>
        public bool TampilkanKartuKeadaan => _data is null && !IsBusy;

        /// <summary>Judul kartu keadaan — membedakan "belum tersusun" dan "gagal".</summary>
        public string JudulKeadaan => AdaGalatMuat
            ? "Laporan belum bisa disusun"
            : "Laporan belum dimuat";

        /// <summary>Sebab kegagalan dari layanan laporan, atau kalimat menunggu.</summary>
        public string SebabKeadaan => AdaGalatMuat
            ? _galatMuat
            : "Angka laporan belum tersusun dari data warga yang tersimpan.";

        /// <summary>
        /// Petunjuk lanjutan: menegaskan bahwa penyusunan laporan hanya membaca data
        /// (tidak mengubah catatan warga) dan menunjukkan tempat yang perlu diperiksa.
        /// </summary>
        public string PetunjukKeadaan =>
            "Penyusunan laporan hanya membaca data — catatan warga tidak berubah. "
            + "Bila gagal berulang, periksa berkas database di Pengaturan Aplikasi, lalu tekan Segarkan.";

        // ----- Kartu & tabel -----
        public ObservableCollection<WargaStatistikKartu> KartuStatistik { get; } = new();
        public ObservableCollection<LaporanPratinjau> TabelPratinjau { get; } = new();

        /// <summary>Semua tabel yang bisa dipilih; dipakai dialog & tampilan layar.</summary>
        public IReadOnlyList<LaporanTabelPilihan> PilihanTabel { get; } = new[]
        {
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.RingkasanPenduduk,
                Judul = "Ringkasan Penduduk",
                Keterangan = "Jumlah penduduk, laki-laki, perempuan, pindah, meninggal"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.KepalaKeluarga,
                Judul = "Kepala Keluarga",
                Keterangan = "Total KK, KK laki-laki, KK wanita"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.KelompokUsia,
                Judul = "Kelompok Usia",
                // Kantong usia laporan adalah 0-4, 5-9, …, 60-64, lalu "65+"
                // (lihat KantongUsia di WargaRepository). Keterangan ini satu-satunya
                // tempat operator bisa melihat batasnya, jadi harus menyebut 65
                // tahun — sebelumnya tertulis 75 sehingga laporan resmi tampak
                // berselisih dengan pilihan yang dipilih operator.
                Keterangan = "0-4 sampai 65 tahun ke atas"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.Pendidikan,
                Judul = "Pendidikan Terakhir",
                Keterangan = "Dikelompokkan ke jenjang baku BPS"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.Agama,
                Judul = "Agama",
                Keterangan = "Agama warga"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.StatusPerkawinan,
                Judul = "Status Perkawinan",
                Keterangan = "Belum menikah, menikah, cerai, dll."
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.Rt,
                Judul = "RT",
                Keterangan = "Jumlah penduduk per RT"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.StatusWarga,
                Judul = "Status Tinggal",
                Keterangan = "Aktif, baru, pindah, meninggal"
            },
            new LaporanTabelPilihan
            {
                Jenis = JenisTabelLaporan.KartuKeluarga,
                Judul = "Kartu Keluarga",
                Keterangan = "Jumlah Kartu Keluarga dan warga tanpa KK"
            }
        };

        public AsyncRelayCommand MuatUlangCommand { get; }
        public AsyncRelayCommand CetakPdfCommand { get; }
        public AsyncRelayCommand SimpanExcelCommand { get; }
        public RelayCommand AlihkanSemuaCommand { get; }
        public RelayCommand MatikanSemuaCommand { get; }

        private void PerbaruiStatusCommand()
        {
            MuatUlangCommand.RaiseCanExecuteChanged();
            CetakPdfCommand.RaiseCanExecuteChanged();
            SimpanExcelCommand.RaiseCanExecuteChanged();
        }

        private void SetSemuaTabel(bool dipakai)
        {
            foreach (var pilihan in PilihanTabel) pilihan.Dipakai = dipakai;
            OnPropertyChanged(nameof(PilihanTabel));
        }

        /// <summary>
        /// Tabel yang dicentang, diteruskan apa adanya ke <c>SusunAsync</c>. Daftar
        /// kosong dan null harus dibedakan: null berarti "semua tabel" (perilaku bawaan
        /// layanan), sedangkan daftar kosong berarti operator sengaja mematikan seluruh
        /// tabel. Dulu keduanya diperlakukan sama — menekan "Hapus semua" justru
        /// mencetak laporan lengkap.
        /// </summary>
        private IReadOnlyList<JenisTabelLaporan> TabelDipilih =>
            PilihanTabel.Where(p => p.Dipakai).Select(p => p.Jenis).ToList();



        // =====================================================================
        // Pemuatan
        // =====================================================================

        /// <summary>
        /// Hitung ulang seluruh angka laporan. Dipanggil saat halaman dibuka dan
        /// setiap kali operator menekan Segarkan.
        /// </summary>
        public async Task LoadAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                // Daftar kosong diteruskan apa adanya (bukan null): "Hapus semua"
                // harus berarti laporan tanpa tabel, bukan diam-diam mencetak semua.
                _data = await _laporan.SusunAsync(KeteranganCakupan, TabelDipilih);

                GalatMuat = string.Empty;
                SusunTampilan(_data);
                StatusText = BuildStatus(_data);
                OnPropertyChanged(nameof(TampilkanKartuKeadaan));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun laporan penduduk");
                _data = null;
                KartuStatistik.Clear();
                TabelPratinjau.Clear();
                PeriodeTampil = string.Empty;
                GalatMuat = ex.Message;
                StatusText = "Laporan gagal disusun: " + ex.Message;

                // Tidak ada dialog galat di sini: kartu keadaan di halaman sudah
                // menyebut sebab dan menyediakan tombol Segarkan. Dialog modal hanya
                // menutupi layar kosong yang sama, lalu menghilang tanpa jejak.
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SusunTampilan(LaporanPendudukData data)
        {
            PeriodeTampil = data.Periode;

            KartuStatistik.Clear();
            var r = data.Ringkasan;
            int penduduk = r.TotalAktif + r.TotalBaru;

            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "PENDUDUK",
                Nilai = Format(penduduk),
                Keterangan = "Aktif + baru (tinggal di desa)",
                Tekankan = true
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "LAKI-LAKI",
                Nilai = Format(r.LakiLakiPenduduk),
                Keterangan = $"Dari {Format(penduduk)} penduduk"
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "PEREMPUAN",
                Nilai = Format(r.PerempuanPenduduk),
                Keterangan = $"Dari {Format(penduduk)} penduduk"
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "KEPALA KELUARGA",
                Nilai = Format(r.JumlahKepalaKeluarga),
                Keterangan = $"{Format(r.KepalaKeluargaLakiLaki)} laki-laki, {Format(r.KepalaKeluargaPerempuan)} wanita",
                Tekankan = true
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "KK LAKI-LAKI",
                Nilai = Format(r.KepalaKeluargaLakiLaki),
                Keterangan = "Berkepala laki-laki"
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "KK WANITA",
                Nilai = Format(r.KepalaKeluargaPerempuan),
                Keterangan = "Berkepala wanita"
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "KARTU KELUARGA",
                Nilai = Format(r.JumlahKartuKeluarga),
                Keterangan = $"{Format(r.TanpaKartuKeluarga)} warga tanpa nomor KK"
            });
            KartuStatistik.Add(new WargaStatistikKartu
            {
                Label = "PINDAH / MENINGGAL",
                Nilai = $"{Format(r.TotalPindah)} / {Format(r.TotalMeninggal)}",
                Keterangan = "Riwayat, tidak dihitung penduduk"
            });

            TabelPratinjau.Clear();
            foreach (var tabel in data.TabelTerisi)
            {
                var baris = new LaporanPratinjau
                {
                    Judul = tabel.Judul,
                    Catatan = tabel.Catatan
                };

                foreach (var b in tabel.Baris)
                {
                    baris.Baris.Add(new LaporanTabelBaris
                    {
                        Label = b.Length > 0 ? b[0] : string.Empty,
                        // Tabel laporan kini tiga kolom: label, angka, persen —
                        // persen diambil dari kolom ketiga supaya tidak tampil
                        // ganda di kolom Jumlah. Tabel dua kolom (tanpa persen)
                        // tetap didukung.
                        Nilai = b.Length > 1 ? b[1] : "-",
                        Persentase = b.Length > 2 ? b[2] : string.Empty
                    });
                }

                TabelPratinjau.Add(baris);
            }
        }

        private static string BuildStatus(LaporanPendudukData data)
        {
            if (!data.AdaIsi)
            {
                return "Belum ada data warga. Laporan tetap bisa dicetak sebagai dokumen kosong.";
            }

            int penduduk = data.Ringkasan.TotalAktif + data.Ringkasan.TotalBaru;
            return $"{Format(penduduk)} penduduk, {data.TabelTerisi.Count} tabel, {data.Periode}.";
        }

        private static string Format(int angka) =>
            angka.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));

        // =====================================================================
        // Keluaran
        // =====================================================================

        /// <summary>
        /// Pratinjau PDF — alur tombol Cetak PDF (tombol Pratinjau tersendiri
        /// sudah dihapus; simpan-ke-berkas tersedia lewat Ekspor di halaman
        /// pratinjau): susun laporan, tulis ke folder sementara, lalu buka
        /// halaman pratinjau yang sudah dipakai seluruh aplikasi. PdfPreviewView
        /// juga menyediakan tombol cetak, jadi satu tombol cukup untuk melihat
        /// dan mencetak.
        /// </summary>
        private async Task PratinjauAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var data = await _laporan.SusunAsync(KeteranganCakupan, TabelDipilih);
                byte[] pdf = await _laporan.RenderPdfAsync(data);

                string folder = TempFolder();
                Directory.CreateDirectory(folder);

                string nama = $"{data.NamaBerkas}_Pratinjau.pdf";
                string jalur = Path.Combine(folder, nama);

                await File.WriteAllBytesAsync(jalur, pdf);

                _data = data;
                SusunTampilan(data);
                StatusText = "Pratinjau PDF siap. Gunakan tombol Cetak pada halaman pratinjau.";

                // Kirim jalur PENUH, bukan nama berkas: SudesPdf memuat dokumen
                // persis dari path yang diberikan, dan berkas tadi ditulis ke
                // folder TempPDF — bukan direktori kerja proses.
                NavigasiKePratinjau(jalur, $"Laporan Penduduk — {data.Periode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau PDF laporan penduduk");
                await _messageService.ShowErrorAsync("Gagal membuat pratinjau laporan.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Simpan laporan sebagai PDF atau Excel sesuai ekstensi.</summary>
        private async Task SimpanAsync(string ekstensi)
        {
            if (IsBusy) return;

            var sfd = new SaveFileDialog
            {
                Title = ekstensi == "pdf" ? "Simpan Laporan Penduduk (PDF)" : "Simpan Laporan Penduduk (Excel)",
                Filter = ekstensi == "pdf"
                    ? "PDF (*.pdf)|*.pdf"
                    : "Microsoft Excel (*.xlsx)|*.xlsx",
                FileName = $"{_data?.NamaBerkas ?? "Laporan_Penduduk"}.{ekstensi}",
                DefaultExt = "." + ekstensi,
                OverwritePrompt = true
            };

            if (sfd.ShowDialog() != true) return;

            // Nama berkas yang benar-benar diketik operator menentukan isinya.
            // Dialog hanya "menyarankan" ekstensi lewat Filter — tidak membatasinya,
            // sehingga "Laporan_2026.pdf" (dari tombol Excel) bisa menghasilkan
            // berkas bernama .pdf yang isinya xlsx (salah kapak, tidak bisa dibuka)
            // dan menimpa PDF asli lewat OverwritePrompt.
            var ekstensiKetikan = System.IO.Path.GetExtension(sfd.FileName).TrimStart('.').ToLowerInvariant();
            if (ekstensiKetikan == "pdf" || ekstensiKetikan == "xlsx") ekstensi = ekstensiKetikan;
            else if (ekstensiKetikan.Length > 0) ekstensi = "xlsx"; // ekstensi tak dikenal → Excel

            IsBusy = true;
            try
            {
                var data = await _laporan.SusunAsync(KeteranganCakupan, TabelDipilih);

                byte[] isi = ekstensi == "pdf"
                    ? await _laporan.RenderPdfAsync(data)
                    : await _laporan.RenderExcelAsync(data);

                await File.WriteAllBytesAsync(sfd.FileName, isi);

                _data = data;
                SusunTampilan(data);
                _activityLog.Log("LAPORAN", data.NamaBerkas, "Simpan " + ekstensi.ToUpperInvariant(), sfd.FileName);
                StatusText = $"Laporan disimpan ke {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync(
                    $"Laporan berhasil disimpan.\n\n{sfd.FileName}\n\n" +
                    "Ukuran: " + UkuranTerbaca(isi.Length));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan laporan penduduk");
                await _messageService.ShowErrorAsync("Gagal menyimpan laporan.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string UkuranTerbaca(int jumlahByte)
        {
            if (jumlahByte < 1024) return jumlahByte + " B";
            if (jumlahByte < 1024 * 1024) return (jumlahByte / 1024.0).ToString("0.0") + " KB";
            return (jumlahByte / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        /// <summary>
        /// Folder tempat berkas pratinjau diletakkan. Harus folder TempPDF yang
        /// dikonfigurasi aplikasi (<see cref="AppConfig.TempPdfFolder"/>) supaya
        /// ikut dibersihkan <c>TempPdfCleanup</c> seperti berkas surat — kalau
        /// pratinjau ditulis ke folder lain, berkasnya menumpuk selamanya.
        /// </summary>
        private string TempFolder() => _appConfig.TempPdfFolder;

        /// <summary>
        /// Buka halaman pratinjau PDF yang sama dengan yang dipakai surat dan
        /// daftar hadir, sehingga satu gaya pratinjau berlaku untuk semua
        /// keluaran di aplikasi ini. Parameter harus jalur penuh ke berkas —
        /// pemanggilan dengan nama berkas saja membuat SudesPdf gagal memuat
        /// dokumen (pratinjau kosong / "dokumen tidak ditemukan").
        /// </summary>
        private void NavigasiKePratinjau(string jalurPdf, string judul)
        {
            try
            {
                _navigasi.Navigate(_previewFactory(judul, jalurPdf));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka pratinjau PDF; berkas tetap tersimpan.");
                _messageService.ShowErrorAsync(
                    "Laporan sudah dibuat, tetapi halaman pratinjau tidak bisa dibuka.\n\n" + ex.Message);
            }
        }
    }
}
