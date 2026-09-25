using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman Panduan Awal: panduan singkat tiga langkah yang menuntun pengguna
    /// mengisi data desa, pejabat, dan nomor surat sesaat setelah aplikasi pertama
    /// kali dibuka. Halaman ini juga bisa dibuka kapan saja dari menu Panduan Awal.
    ///
    /// Keadaan setiap langkah dihitung dari data aplikasi yang sebenarnya:
    /// data desa/pejabat dari <see cref="SettingsManager"/> (lewat pemeriksaan
    /// <see cref="DesaContoh"/> gaya yang sama dengan halaman Pengaturan Surat),
    /// dan penomoran surat dari <see cref="PenomoranSuratService"/>. Jadi lencana
    /// "Selesai" tidak bisa menipu: begitu data desa benar-benar diisi, langkahnya
    /// ikut selesai tanpa perlu diklik.
    ///
    /// Panduan dibuka otomatis hanya bila data desa/pejabat belum beres dan pengguna
    /// belum pernah menutupnya; setelah itu langkah penomoran tetap bisa diperiksa
    /// dari menu. Penanda keadaannya disimpan lewat <see cref="IPanduanAwalStore"/>.
    /// </summary>
    public class PanduanAwalViewModel : ObservableObject
    {
        /// <summary>Kolom langkah 1 pada daftar pemeriksaan data desa.</summary>
        private static readonly string[] KolomDesa = { "Nama Desa", "Kecamatan", "Kabupaten", "Alamat Desa" };

        /// <summary>Kolom langkah 2 pada daftar pemeriksaan data desa.</summary>
        private static readonly string[] KolomPejabat = { "Kepala Desa", "Sekretaris Desa" };

        private readonly SettingsManager _settings;
        private readonly PenomoranSuratService _penomoran;
        private readonly IPanduanAwalStore _store;
        private readonly IServiceProvider _provider;
        private readonly NavigationService _navigation;
        private readonly ILogger<PanduanAwalViewModel> _logger;

        private string _statusPesan = "Memeriksa kelengkapan data…";
        private string _pesanKesalahan = string.Empty;

        public PanduanAwalViewModel(
            SettingsManager settings,
            PenomoranSuratService penomoran,
            IPanduanAwalStore store,
            IServiceProvider provider,
            NavigationService navigation,
            ILogger<PanduanAwalViewModel> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _penomoran = penomoran ?? throw new ArgumentNullException(nameof(penomoran));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Langkah = new ObservableCollection<PanduanLangkahViewModel>
            {
                new(1, "Data Desa",
                    "Nama desa, kecamatan, kabupaten, dan alamat kantor desa — dipakai pada kop seluruh surat.",
                    "Isi Data Desa",
                    judulTombolSelesai: "Ubah Data Desa"),
                new(2, "Pejabat Desa",
                    "Kepala Desa dan Sekretaris Desa untuk blok tanda tangan surat. Pejabat Kecamatan (camat) boleh dikosongkan.",
                    "Isi Pejabat Desa",
                    judulTombolSelesai: "Ubah Pejabat Desa"),
                new(3, "Nomor Surat",
                    "Periksa awalan (kode klasifikasi) nomor surat tiap jenis surat. Bawaan aplikasi bisa langsung dipakai bila sudah cocok dengan kantor desa.",
                    "Periksa Penomoran Surat",
                    adaTombolKedua: true,
                    judulTombolKedua: "Nomor surat sudah sesuai",
                    judulTombolSelesai: "Buka Penomoran Surat"),
                new(4, "Logo & Ukuran Kertas",
                    "Pasang logo kop desa dan pilih ukuran kertas PDF (A4 atau F4). Keduanya tersimpan langsung tanpa menekan Simpan.",
                    "Atur Logo & Kertas",
                    opsional: true),
            };

            BukaCommand = new RelayCommand<PanduanLangkahViewModel>(BukaLangkah, l => l != null);
            PeriksaCommand = new AsyncRelayCommand(PeriksaAsync);
            TandaiPenomoranCommand = new AsyncRelayCommand(TandaiPenomoranAsync);
            SelesaiCommand = new AsyncRelayCommand(SelesaiDanTutupAsync);

            _ = PeriksaAsync();
        }

        /// <summary>Dipicu saat pengguna menutup panduan — host memindahkannya ke Beranda.</summary>
        public event Action? PanduanDitutup;

        public ObservableCollection<PanduanLangkahViewModel> Langkah { get; }

        public RelayCommand<PanduanLangkahViewModel> BukaCommand { get; }
        public AsyncRelayCommand PeriksaCommand { get; }
        public AsyncRelayCommand TandaiPenomoranCommand { get; }
        public AsyncRelayCommand SelesaiCommand { get; }

        /// <summary>Langkah 1 — data desa (dipakai juga untuk menentukan panduan perlu tampil).</summary>
        public PanduanLangkahViewModel LangkahDataDesa => Langkah[0];

        /// <summary>Langkah 2 — pejabat desa.</summary>
        public PanduanLangkahViewModel LangkahPejabatDesa => Langkah[1];

        /// <summary>Langkah 3 — nomor surat.</summary>
        public PanduanLangkahViewModel LangkahNomorSurat => Langkah[2];

        /// <summary>Langkah opsional — logo &amp; ukuran kertas.</summary>
        public PanduanLangkahViewModel LangkahLogo => Langkah[3];

        /// <summary>Judul halaman.</summary>
        public string Judul => "Panduan Awal";

        /// <summary>Kalimat pembuka halaman.</summary>
        public string Pengantar =>
            "Tiga langkah singkat sebelum surat pertama dicetak. Semuanya bisa diubah kapan saja lewat " +
            "Pengaturan Surat dan Pengaturan Aplikasi — panduan ini hanya menuntun urutannya.";

        /// <summary>Keterangan bawah halaman: kapan panduan dibuka otomatis.</summary>
        public string Catatan =>
            "Panduan ini terbuka otomatis saat aplikasi pertama kali dibuka, lalu berhenti muncul setelah " +
            "data desa dan pejabat terisi (atau setelah Anda menutupnya). Menu Panduan Awal di sidebar " +
            "membukanya kembali kapan saja.";

        /// <summary>Kiat singkat agar aplikasi siap dipakai sehari-hari.</summary>
        public IReadOnlyList<string> Tips { get; } = new[]
        {
            "Data desa dan pejabat dipakai bersama: kop, badan surat, dan tanda tangan memakai data yang sama.",
            "Nomor surat terisi otomatis saat surat dibuat — cukup pastikan awalannya sesuai tata naskah kantor desa.",
            "Aktifkan pencadangan Google Drive di Pengaturan Aplikasi supaya data dan template aman.",
            "Template surat sendiri bisa dibuat dari berkas Word lewat menu Template Surat.",
        };

        /// <summary>Jumlah langkah yang dihitung pada kemajuan (langkah opsional tidak dihitung).</summary>
        public int JumlahWajib => Langkah.Count(l => !l.Opsional);

        /// <summary>Jumlah langkah yang sudah selesai dari langkah wajib.</summary>
        public int JumlahSelesai => Langkah.Count(l => !l.Opsional && l.Selesai);

        /// <summary>Persentase kemajuan untuk bilah kemajuan halaman.</summary>
        public double ProgresPersen => JumlahWajib == 0 ? 100 : JumlahSelesai * 100.0 / JumlahWajib;

        public string ProgresTeks => JumlahSelesai == JumlahWajib
            ? $"Semua {JumlahWajib} langkah selesai"
            : $"{JumlahSelesai} dari {JumlahWajib} langkah selesai";

        /// <summary>Benar bila seluruh langkah wajib sudah beres.</summary>
        public bool SemuaSelesai => JumlahSelesai == JumlahWajib;

        /// <summary>Kalimat keadaan di kartu kemajuan.</summary>
        public string StatusPanduan => SemuaSelesai
            ? "Aplikasi siap dipakai — surat pertama bisa dibuat sekarang."
            : "Lengkapi langkah di bawah supaya surat yang dicetak tidak memakai data contoh.";

        /// <summary>Pesan statusbar halaman (bukan dialog).</summary>
        public string StatusPesan
        {
            get => _statusPesan;
            private set => SetProperty(ref _statusPesan, value ?? string.Empty);
        }

        /// <summary>Kesalahan terakhir saat membuka langkah — ditampilkan di halaman, bukan dialog.</summary>
        public string PesanKesalahan
        {
            get => _pesanKesalahan;
            private set
            {
                if (SetProperty(ref _pesanKesalahan, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public bool AdaKesalahan => _pesanKesalahan.Length > 0;

        /// <summary>
        /// Panduan perlu dibuka otomatis saat aplikasi dibuka: data desa atau pejabat
        /// belum beres, dan pengguna belum pernah menutup panduan. Penomoran surat
        /// tidak ikut memicu karena bawaan aplikasi sering sudah dipakai apa adanya.
        /// </summary>
        public bool PerluDitampilkanSaatMulai =>
            !_store.SudahSelesai && (!LangkahDataDesa.Selesai || !LangkahPejabatDesa.Selesai);

        /// <summary>
        /// Periksa ulang keadaan seluruh langkah dari data aplikasi, lalu perbarui
        /// kemajuan, teks statusbar, dan penanda "panduan tidak perlu dibuka lagi".
        /// </summary>
        public async Task PeriksaAsync()
        {
            try
            {
                PesanKesalahan = string.Empty;
                await PeriksaDataDesaAsync();
                PeriksaPenomoran();
                PeriksaLogoDanKertas();
                PerbaruiProgres();
                StatusPesan = ProgresTeks + ".";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa kelengkapan data panduan awal");
                PesanKesalahan = "Kelengkapan data tidak terbaca: " + ex.Message;
                StatusPesan = "Gagal memeriksa kelengkapan data.";
            }
        }

        /// <summary>Buka halaman yang tepat untuk sebuah langkah.</summary>
        private void BukaLangkah(PanduanLangkahViewModel? langkah)
        {
            if (langkah == null) return;

            try
            {
                PesanKesalahan = string.Empty;

                if (langkah.Nomor == 3)
                {
                    // Penomoran surat ada di Pengaturan Aplikasi, seksi "Penomoran Surat".
                    var pengaturan = _provider.GetRequiredService<PengaturanAplikasiViewModel>();
                    pengaturan.SetFocusSection("penomoran");
                    _navigation.Navigate(pengaturan);
                    StatusPesan = "Membuka Penomoran Surat di Pengaturan Aplikasi…";
                    return;
                }

                // Langkah 1, 2, dan 4 semuanya ada di halaman Pengaturan Surat; nomor
                // langkah (1..4) sejajar dengan urutan bagian di halaman itu.
                var setelan = _provider.GetRequiredService<SetelanViewModel>();
                setelan.TampilkanBagian(langkah.Nomor - 1);
                _navigation.Navigate(setelan);
                StatusPesan = "Membuka Pengaturan Surat…";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka langkah panduan {Nomor}", langkah.Nomor);
                PesanKesalahan = $"Langkah \"{langkah.Judul}\" gagal dibuka: {ex.Message}";
            }
        }

        /// <summary>
        /// Pengguna menyatakan penomoran surat sudah sesuai — langkah nomor surat
        /// dianggap selesai tanpa perlu mengubah apa pun (banyak kantor desa memang
        /// memakai kode klasifikasi bawaan).
        /// </summary>
        private async Task TandaiPenomoranAsync()
        {
            _store.SetPenomoranDiperiksa(true);
            await PeriksaAsync();
            StatusPesan = "Penomoran surat ditandai sudah sesuai — langkah ini tidak akan ditagih lagi.";
        }

        /// <summary>Tutup panduan dan tandai selesai supaya tidak dibuka otomatis lagi.</summary>
        private async Task SelesaiDanTutupAsync()
        {
            _store.SetSudahSelesai(true);
            await PeriksaAsync();
            StatusPesan = "Panduan awal ditutup — bisa dibuka lagi dari menu Panduan Awal.";
            PanduanDitutup?.Invoke();
        }

        private async Task PeriksaDataDesaAsync()
        {
            var desa = await _settings.GetSettingsAsync();
            var contoh = DesaContoh.FieldContoh(desa);

            var kolomDesa = contoh.Where(f => KolomDesa.Contains(f)).ToList();
            LangkahDataDesa.Selesai = kolomDesa.Count == 0;
            LangkahDataDesa.Rincian = kolomDesa.Count == 0
                ? $"Terisi: Desa {Isi(desa?.NamaDesa)} Kec. {Isi(desa?.Kecamatan)} Kab. {Isi(desa?.Kabupaten)}."
                : "Masih contoh atau belum diisi: " + string.Join(", ", kolomDesa) + ".";

            var kolomPejabat = contoh.Where(f => KolomPejabat.Contains(f)).ToList();
            LangkahPejabatDesa.Selesai = kolomPejabat.Count == 0;
            LangkahPejabatDesa.Rincian = kolomPejabat.Count == 0
                ? $"Terisi: {Isi(desa?.KepalaDesa)} (Kepala Desa) dan {Isi(desa?.SekretarisDesa)} (Sekretaris Desa)."
                : "Masih contoh atau belum diisi: " + string.Join(", ", kolomPejabat) + ".";
        }

        private void PeriksaPenomoran()
        {
            try
            {
                var entri = _penomoran.MuatSemua();
                int diubah = entri.Count(e => e.Disesuaikan);
                string contohNomor = ContohNomorSurat(entri);

                LangkahNomorSurat.Selesai = diubah > 0 || _store.PenomoranDiperiksa;
                LangkahNomorSurat.Rincian = $"Contoh nomor surat sekarang: {contohNomor} " +
                    $"({entri.Count} jenis surat, {diubah} sudah disesuaikan" +
                    (_store.PenomoranDiperiksa ? ", sudah Anda nyatakan sesuai" : string.Empty) + ").";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pengaturan penomoran surat tidak terbaca untuk panduan awal");
                LangkahNomorSurat.Selesai = _store.PenomoranDiperiksa;
                LangkahNomorSurat.Rincian = "Pengaturan penomoran surat belum bisa dibaca dari berkas konfigurasi.";
            }
        }

        private void PeriksaLogoDanKertas()
        {
            bool logoPengguna = !string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo());
            var ukuran = PengaturanCetak.GetUkuranKertas();
            LangkahLogo.Selesai = logoPengguna || ukuran == UkuranKertasSurat.F4;
            LangkahLogo.Rincian = LangkahLogo.Selesai
                ? $"Logo {(logoPengguna ? "pilihan Anda" : "bawaan")} · kertas {PengaturanCetak.LabelUkuranKertas(ukuran)}."
                : $"Masih memakai logo bawaan aplikasi · kertas {PengaturanCetak.LabelUkuranKertas(ukuran)}.";
        }

        private void PerbaruiProgres()
        {
            OnPropertyChanged(nameof(JumlahWajib));
            OnPropertyChanged(nameof(JumlahSelesai));
            OnPropertyChanged(nameof(ProgresPersen));
            OnPropertyChanged(nameof(ProgresTeks));
            OnPropertyChanged(nameof(SemuaSelesai));
            OnPropertyChanged(nameof(StatusPanduan));
            OnPropertyChanged(nameof(PerluDitampilkanSaatMulai));

            // Semua langkah wajib beres = panduan tidak perlu dibuka otomatis lagi.
            if (SemuaSelesai)
            {
                _store.SetSudahSelesai(true);
            }
        }

        /// <summary>Contoh nomor surat dari jenis SKD bila ada, jika tidak dari jenis pertama.</summary>
        private static string ContohNomorSurat(IReadOnlyList<PenomoranSuratEntri> entri)
        {
            if (entri.Count == 0) return "(belum ada jenis surat)";

            var contoh = entri.FirstOrDefault(e =>
                string.Equals(e.NamaJenis, "SKD_UMUM", StringComparison.OrdinalIgnoreCase)) ?? entri[0];

            return PenomoranSuratService.Contoh(contoh.Format);
        }

        private static string Isi(string? nilai)
        {
            string bersih = (nilai ?? string.Empty).Trim();
            return bersih.Length == 0 ? "(belum diisi)" : bersih;
        }
    }
}
