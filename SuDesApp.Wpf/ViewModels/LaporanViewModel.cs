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
    public class LaporanTabelPilihan
    {
        public JenisTabelLaporan Jenis { get; init; }
        public string Judul { get; init; } = string.Empty;
        public bool Dipakai { get; set; } = true;

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
            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy);
            CetakPdfCommand = new AsyncRelayCommand(() => SimpanAsync("pdf"), () => !IsBusy);
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
                Keterangan = "0-4 sampai 75 tahun ke atas"
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
        public AsyncRelayCommand PratinjauCommand { get; }
        public AsyncRelayCommand CetakPdfCommand { get; }
        public AsyncRelayCommand SimpanExcelCommand { get; }
        public RelayCommand AlihkanSemuaCommand { get; }
        public RelayCommand MatikanSemuaCommand { get; }

        private void PerbaruiStatusCommand()
        {
            MuatUlangCommand.RaiseCanExecuteChanged();
            PratinjauCommand.RaiseCanExecuteChanged();
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

                SusunTampilan(_data);
                StatusText = BuildStatus(_data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun laporan penduduk");
                _data = null;
                KartuStatistik.Clear();
                TabelPratinjau.Clear();
                PeriodeTampil = string.Empty;
                StatusText = "Laporan gagal disusun: " + ex.Message;
                await _messageService.ShowErrorAsync("Gagal menyusun laporan penduduk.\n\n" + ex.Message);
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
        /// Pratinjau PDF: susun laporan, tulis ke folder sementara, lalu buka
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

                NavigasiKePratinjau(nama, $"Laporan Penduduk — {data.Periode}");
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
        /// keluaran di aplikasi ini.
        /// </summary>
        private void NavigasiKePratinjau(string namaBerkas, string judul)
        {
            try
            {
                _navigasi.Navigate(_previewFactory(judul, namaBerkas));
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
