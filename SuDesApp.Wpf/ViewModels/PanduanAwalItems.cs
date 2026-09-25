using System.Globalization;
using System.Windows;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu langkah pada halaman Panduan Awal. Keadaannya (<see cref="Selesai"/>)
    /// dihitung dari data aplikasi yang sebenarnya — bukan dari catatan pengguna —
    /// sehingga lencana "Selesai" selalu mencerminkan isi data desa/penomoran yang
    /// berlaku saat itu.
    /// </summary>
    public class PanduanLangkahViewModel : ObservableObject
    {
        private bool _selesai;
        private string _rincian = string.Empty;

        public PanduanLangkahViewModel(
            int nomor,
            string judul,
            string keterangan,
            string judulTombol,
            bool opsional = false,
            bool adaTombolKedua = false,
            string judulTombolKedua = "",
            string judulTombolSelesai = "")
        {
            Nomor = nomor;
            Judul = judul ?? string.Empty;
            Keterangan = keterangan ?? string.Empty;
            JudulTombol = judulTombol ?? string.Empty;
            Opsional = opsional;
            AdaTombolKedua = adaTombolKedua;
            JudulTombolKedua = judulTombolKedua ?? string.Empty;
            JudulTombolSelesai = string.IsNullOrWhiteSpace(judulTombolSelesai) ? JudulTombol : judulTombolSelesai;
        }

        /// <summary>Nomor urut langkah (1..n) yang juga dipakai tombolnya sebagai sasaran.</summary>
        public int Nomor { get; }

        public string NomorTeks => Nomor.ToString(CultureInfo.InvariantCulture);

        public string Judul { get; }

        /// <summary>Penjelasan singkat apa yang diisi pada langkah ini.</summary>
        public string Keterangan { get; }

        /// <summary>Label tombol utama, mis. "Isi Data Desa".</summary>
        public string JudulTombol { get; }

        /// <summary>Label tombol utama setelah langkahnya selesai, mis. "Ubah Data Desa".</summary>
        public string JudulTombolSelesai { get; }

        /// <summary>Label tombol yang sedang berlaku — tidak meminta hal yang sudah beres.</summary>
        public string JudulTombolTampil => _selesai ? JudulTombolSelesai : JudulTombol;

        /// <summary>Langkah opsional tidak dihitung pada kemajuan panduan.</summary>
        public bool Opsional { get; }

        /// <summary>Langkah ini punya tombol kedua (mis. "Nomor surat sudah sesuai").</summary>
        public bool AdaTombolKedua { get; }

        public string JudulTombolKedua { get; }

        /// <summary>
        /// Tombol kedua hanya tampil selama langkah ini belum beres — setelah selesai
        /// tidak ada lagi yang perlu dinyatakan pengguna.
        /// </summary>
        public bool TampilkanTombolKedua => AdaTombolKedua && !_selesai;

        /// <summary>Benar bila langkah ini sudah beres menurut data aplikasi.</summary>
        public bool Selesai
        {
            get => _selesai;
            set
            {
                if (SetProperty(ref _selesai, value))
                {
                    OnPropertyChanged(nameof(StatusTeks));
                    OnPropertyChanged(nameof(JudulTombolTampil));
                    OnPropertyChanged(nameof(IkonNomor));
                    OnPropertyChanged(nameof(MasihPerlu));
                    OnPropertyChanged(nameof(TampilkanTombolKedua));
                    OnPropertyChanged(nameof(KetebalanJudul));
                }
            }
        }

        /// <summary>Masih perlu dikerjakan (dipakai untuk warna lencana).</summary>
        public bool MasihPerlu => !_selesai;

        /// <summary>Rincian keadaan langkah, mis. daftar kolom yang masih contoh.</summary>
        public string Rincian
        {
            get => _rincian;
            set
            {
                if (SetProperty(ref _rincian, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaRincian));
                }
            }
        }

        public bool AdaRincian => _rincian.Length > 0;

        public string StatusTeks => _selesai
            ? "Selesai"
            : Opsional ? "Opsional" : "Belum dikerjakan";

        /// <summary>Isi lencana nomor: centang bila selesai, nomor urut bila belum.</summary>
        public string IkonNomor => _selesai ? "\u2713" : NomorTeks;

        public FontWeight KetebalanJudul => _selesai ? FontWeights.Normal : FontWeights.SemiBold;

        public override string ToString() => $"{Nomor}. {Judul} ({StatusTeks})";
    }
}
