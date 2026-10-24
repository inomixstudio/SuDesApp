using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Jenis kartu pemberitahuan inline — menentukan warna dan ikonnya.</summary>
    public enum JenisPesanInline
    {
        Info,
        Sukses,
        Peringatan,
        Galat,
        Pertanyaan
    }

    /// <summary>
    /// Satu kartu pemberitahuan yang tampil <b>di dalam halaman</b>, bukan sebagai
    /// dialog popup yang menutupi pekerjaan pengguna.
    ///
    /// Kartu bisa ditutup sendiri (tombol ✕), dan kartu berkunci sama saling menimpa
    /// supaya kabar sejenis tidak menumpuk. Kartu jenis <see cref="JenisPesanInline.Pertanyaan"/>
    /// menyediakan dua tombol jawaban di dalam kartunya sehingga keputusan pun tidak
    /// perlu jendela terpisah.
    /// </summary>
    public class PesanInline : ObservableObject
    {
        private string _pertanyaan = string.Empty;
        private string _teksYa = "Lanjutkan";
        private string _teksTidak = "Batal";
        private Action<bool>? _jawab;

        public PesanInline(JenisPesanInline jenis, string judul, string isi)
        {
            Jenis = jenis;
            Judul = judul ?? string.Empty;
            Isi = isi ?? string.Empty;
            TutupCommand = new RelayCommand(() => SaatDitutup?.Invoke(this));
            JawabYaCommand = new RelayCommand(() => Jawab(true));
            JawabTidakCommand = new RelayCommand(() => Jawab(false));
        }

        /// <summary>Dipanggil saat pengguna menutup kartu ini (tombol ✕). Diisi oleh kumpulannya.</summary>
        internal Action<PesanInline>? SaatDitutup { get; set; }

        public JenisPesanInline Jenis { get; }

        /// <summary>Nama jenis sebagai teks — dipakai pemicu (trigger) warna pada XAML.</summary>
        public string NamaJenis => Jenis.ToString();

        public string Judul { get; }

        public string Isi { get; }

        /// <summary>Kunci pengelompokan: pesan berkunci sama saling menimpa (tidak menumpuk).</summary>
        public string Kunci { get; set; } = string.Empty;

        /// <summary>Ikon kecil yang mengikuti jenis pesan.</summary>
        public string Ikon => Jenis switch
        {
            JenisPesanInline.Sukses => "\u2714",      // ✔
            JenisPesanInline.Peringatan => "\u26A0",  // ⚠
            JenisPesanInline.Galat => "\u2715",       // ✕
            JenisPesanInline.Pertanyaan => "\u2753",  // ❓
            _ => "\u2139"                             // ℹ
        };

        /// <summary>Pertanyaan yang menunggu jawaban (kosong untuk kabar biasa).</summary>
        public string Pertanyaan
        {
            get => _pertanyaan;
            private set
            {
                if (SetProperty(ref _pertanyaan, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaPertanyaan));
                }
            }
        }

        /// <summary>Benar bila kartu ini menunggu keputusan pengguna (tombol ya/tidak).</summary>
        public bool AdaPertanyaan => _pertanyaan.Length > 0;

        /// <summary>Kartu keputusan tidak boleh ditutup begitu saja — jawabannya wajib.</summary>
        public bool BisaDitutup => Jenis != JenisPesanInline.Pertanyaan;

        public string TeksYa
        {
            get => _teksYa;
            private set => SetProperty(ref _teksYa, value);
        }

        public string TeksTidak
        {
            get => _teksTidak;
            private set => SetProperty(ref _teksTidak, value);
        }

        public ICommand TutupCommand { get; }
        public ICommand JawabYaCommand { get; }
        public ICommand JawabTidakCommand { get; }

        /// <summary>
        /// Ubah kartu ini menjadi kartu keputusan: pertanyaan, label tombol, dan
        /// tindakan yang menerima jawaban pengguna.
        /// </summary>
        public void SiapkanPertanyaan(string pertanyaan, string teksYa, string teksTidak, Action<bool> jawab)
        {
            Pertanyaan = pertanyaan;
            TeksYa = string.IsNullOrWhiteSpace(teksYa) ? "Lanjutkan" : teksYa;
            TeksTidak = string.IsNullOrWhiteSpace(teksTidak) ? "Batal" : teksTidak;
            _jawab = jawab;
        }

        /// <summary>Jawab sekali saja — klik kedua pada tombol yang sama diabaikan.</summary>
        private void Jawab(bool ya)
        {
            var jawab = _jawab;
            _jawab = null;
            jawab?.Invoke(ya);
        }
    }

    /// <summary>
    /// Kumpulan kartu pemberitahuan inline milik satu halaman. Dipakai bersama
    /// halaman Pembaruan, Register Surat, dan formulir input surat supaya seluruh
    /// aplikasi memakai pola kabar yang sama: tampil di halaman, bisa ditutup,
    /// dan tidak menutupi pekerjaan pengguna.
    /// </summary>
    public sealed class KumpulanPesanInline : ObservableCollection<PesanInline>
    {
        public KumpulanPesanInline(int maksKartu = 4)
        {
            MaksKartu = maksKartu < 1 ? 1 : maksKartu;

            // Properti turunan (Ada, MenungguJawaban) diikat XAML (mis.
            // Visibility="{Binding Pesan.Ada}"). Tanpa pemberitahuan eksplisit ini,
            // binding hanya dievaluasi sekali saat koleksi masih kosong dan tidak
            // pernah diperbarui — kartu pemberitahuan (termasuk galat cetak) tidak
            // pernah tampil walaupun sudah ditambahkan.
            CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Ada)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(MenungguJawaban)));
            };
        }

        /// <summary>Batas jumlah kartu yang ditampilkan supaya halaman tetap ringkas.</summary>
        public int MaksKartu { get; }

        /// <summary>Benar bila ada kartu untuk ditampilkan (dipakai binding Visibility).</summary>
        public bool Ada => Count > 0;

        /// <summary>Benar bila ada kartu keputusan yang menunggu jawaban.</summary>
        public bool MenungguJawaban => this.Any(p => p.AdaPertanyaan);

        /// <summary>Tambah kartu baru; kartu berkunci sama yang lama digantikan.</summary>
        public PesanInline Tampilkan(JenisPesanInline jenis, string judul, string isi, string kunci = "")
        {
            if (kunci.Length > 0)
            {
                for (int i = Count - 1; i >= 0; i--)
                {
                    if (string.Equals(this[i].Kunci, kunci, StringComparison.Ordinal))
                    {
                        RemoveAt(i);
                    }
                }
            }

            var pesan = new PesanInline(jenis, judul, isi)
            {
                Kunci = kunci,
                SaatDitutup = Tutup
            };

            Insert(0, pesan);

            while (Count > MaksKartu)
            {
                RemoveAt(Count - 1);
            }

            return pesan;
        }

        /// <summary>Kabar biasa (biru).</summary>
        public PesanInline Info(string judul, string isi, string kunci = "")
            => Tampilkan(JenisPesanInline.Info, judul, isi, kunci);

        /// <summary>Kabar berhasil (hijau).</summary>
        public PesanInline Sukses(string judul, string isi, string kunci = "")
            => Tampilkan(JenisPesanInline.Sukses, judul, isi, kunci);

        /// <summary>Peringatan (kuning).</summary>
        public PesanInline Peringatan(string judul, string isi, string kunci = "")
            => Tampilkan(JenisPesanInline.Peringatan, judul, isi, kunci);

        /// <summary>Kegagalan (merah).</summary>
        public PesanInline Galat(string judul, string isi, string kunci = "")
            => Tampilkan(JenisPesanInline.Galat, judul, isi, kunci);

        /// <summary>Tutup satu kartu (tombol ✕ pada kartunya).</summary>
        public void Tutup(PesanInline? pesan)
        {
            if (pesan != null)
            {
                Remove(pesan);
            }
        }

        /// <summary>
        /// Buang kabar lama — dipakai saat memulai pekerjaan baru. Kartu keputusan
        /// yang masih menunggu jawaban tidak ikut dibuang.
        /// </summary>
        public void Bersihkan()
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (this[i].BisaDitutup)
                {
                    RemoveAt(i);
                }
            }
        }

        /// <summary>Buang semua kartu, termasuk kartu keputusan.</summary>
        public void Kosongkan() => Clear();

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnCollectionChanged(e);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Ada)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(MenungguJawaban)));
        }
    }
}
