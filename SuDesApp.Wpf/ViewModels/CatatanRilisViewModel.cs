using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Jenis sub-bagian catatan rilis; dipakai untuk penanda warna pada kartu
    /// (mis. "Fitur Baru" hijau, "Perbaikan Bug" oranye, sisanya aksen tema).
    /// </summary>
    public enum GayaRilisCatatan
    {
        FiturBaru,
        Peningkatan,
        Perbaikan,
        Pembaruan,
        Umum
    }

    /// <summary>
    /// Entri sub-bagian dalam satu versi catatan rilis.
    /// </summary>
    public class CatatanRilisSubSection : ObservableObject
    {
        public string Heading { get; }
        public List<string> Points { get; }
        public GayaRilisCatatan Jenis { get; }
        public string LabelJenis { get; }

        public CatatanRilisSubSection(string heading, List<string> points, GayaRilisCatatan? jenis = null)
        {
            Heading = heading;
            Points = points;
            Jenis = jenis ?? TentukanJenis(heading);
            LabelJenis = LabelUntuk(Jenis);
        }

        /// <summary>Tentukan jenis sub-bagian dari awalan judulnya.</summary>
        private static GayaRilisCatatan TentukanJenis(string heading)
        {
            var teks = heading ?? string.Empty;
            if (teks.StartsWith("Fitur Baru", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.FiturBaru;
            if (teks.StartsWith("Peningkatan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Peningkatan;
            if (teks.StartsWith("Perbaikan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Perbaikan;
            if (teks.StartsWith("Pembaruan", StringComparison.OrdinalIgnoreCase)) return GayaRilisCatatan.Pembaruan;
            return GayaRilisCatatan.Umum;
        }

        private static string LabelUntuk(GayaRilisCatatan jenis) => jenis switch
        {
            GayaRilisCatatan.FiturBaru => "FITUR BARU",
            GayaRilisCatatan.Peningkatan => "PENINGKATAN",
            GayaRilisCatatan.Perbaikan => "PERBAIKAN",
            GayaRilisCatatan.Pembaruan => "PEMBARUAN",
            _ => "LAINNYA"
        };
    }

    /// <summary>
    /// Satu blok versi pada halaman catatan rilis.
    /// </summary>
    public class CatatanRilisEntry : ObservableObject
    {
        public string VersionText { get; }
        public List<CatatanRilisSubSection> Sections { get; }

        /// <summary>
        /// True HANYA untuk entri paling atas — diatur otomatis oleh
        /// <see cref="CatatanRilisViewModel.SusunRiwayatVersi"/> dari posisi dalam
        /// daftar, bukan bendera manual per entri. Dulu bendera itu ditulis tangan
        /// dan 2.5.9 ikut menandai dirinya "TERBARU" sehingga dua badge tampil
        /// bersamaan.
        /// </summary>
        public bool IsLatest { get; internal set; }

        public CatatanRilisEntry(string versionText, List<CatatanRilisSubSection> sections)
        {
            VersionText = versionText;
            Sections = sections;
        }
    }

    /// <summary>
    /// ViewModel halaman Catatan Rilis — padanan <c>CatatanRilis</c> (WinForms).
    /// Data disalin 1:1 dari <c>Views/CatatanRilis.cs (TampilkanCatatanRilis)</c>.
    /// </summary>
    public partial class CatatanRilisViewModel : ObservableObject
    {
        private readonly ILogger<CatatanRilisViewModel> _logger;

        public string Title => "CATATAN RILIS";
        public string Subtitle => "Pembaruan, perbaikan, dan fitur baru dari waktu ke waktu";
        /// <summary>
        /// Identitas aplikasi pada statusbar halaman ("Surat Desa V.x.y.z  •  ...") —
        /// disusun otomatis dari atribut assembly (csproj) oleh <see cref="IdentitasAplikasi"/>,
        /// sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string VersionFooter => IdentitasAplikasi.FooterVersi;

        /// <summary>
        /// Jumlah kartu versi per batch. Halaman dibuka dengan satu batch (ringkas),
        /// lalu tombol "Muat versi lebih lama" menambah satu batch lagi — seluruh
        /// riwayat tetap bisa dibaca tanpa membanjiri halaman sejak awal.
        /// </summary>
        public const int MaksKartu = 3;

        /// <summary>Kartu versi yang sedang tampil (terbaru di atas). Bisa bertambah
        /// lewat <see cref="MuatVersiLamaCommand"/>.</summary>
        public ObservableCollection<CatatanRilisEntry> Entries { get; } = new();

        /// <summary>Seluruh riwayat versi aplikasi — sumber tunggal kartu-kartu di halaman.</summary>
        public IReadOnlyList<CatatanRilisEntry> SemuaVersi { get; }

        /// <summary>Tambah satu batch kartu versi lama (nonaktif saat semua sudah tampil).</summary>
        public ICommand MuatVersiLamaCommand { get; }

        /// <summary>Jumlah versi lama yang belum ditampilkan.</summary>
        public int SisaVersi => SemuaVersi.Count - Entries.Count;

        /// <summary>Masih ada versi lama yang bisa dibuka?</summary>
        public bool AdaSisaVersi => SisaVersi > 0;

        /// <summary>
        /// Ringkasan riwayat ("Menampilkan 3 dari 19 versi…"). Menggantikan baris
        /// yang dulu mencetak footer versi — teks yang sama sudah tampil di status
        /// bar di bawah halaman, jadi tidak perlu diulang dua kali.
        /// </summary>
        public string RingkasanRiwayat =>
            $"Menampilkan {Entries.Count} dari {SemuaVersi.Count} versi — yang terbaru di atas.";

        public CatatanRilisViewModel(ILogger<CatatanRilisViewModel> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SemuaVersi = RiwayatVersi();

            // Halaman dibuka dengan batch pertama saja; sisanya menunggu klik
            // "Muat versi lebih lama" agar daftar panjang tidak langsung membanjiri layar.
            foreach (var entri in SemuaVersi.Take(MaksKartu))
            {
                Entries.Add(entri);
            }

            MuatVersiLamaCommand = new RelayCommand(MuatVersiLama, () => AdaSisaVersi);

            _logger.LogInformation(
                "Catatan rilis dimuat: {Total} versi, {Tampil} kartu ditampilkan (batch {Maks})",
                SemuaVersi.Count, Entries.Count, MaksKartu);
        }

        /// <summary>Tambahkan batch versi berikutnya ke <see cref="Entries"/>.</summary>
        private void MuatVersiLama()
        {
            foreach (var entri in SemuaVersi.Skip(Entries.Count).Take(MaksKartu).ToList())
            {
                Entries.Add(entri);
            }

            OnPropertyChanged(nameof(SisaVersi));
            OnPropertyChanged(nameof(AdaSisaVersi));
            OnPropertyChanged(nameof(RingkasanRiwayat));
            ((RelayCommand)MuatVersiLamaCommand).RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Seluruh riwayat versi aplikasi — sumber tunggal untuk kartu-kartu di halaman
        /// Catatan Rilis (tiga versi terbaru yang diambil dari daftar ini).
        /// </summary>
        public static IReadOnlyList<CatatanRilisEntry> RiwayatVersi()
            => _riwayatVersi ??= SusunRiwayatVersi();

        private static IReadOnlyList<CatatanRilisEntry>? _riwayatVersi;


    }
}
