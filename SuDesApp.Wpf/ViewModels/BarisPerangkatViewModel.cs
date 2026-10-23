using System;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu baris pada tabel halaman Data Perangkat Desa: satu jabatan
    /// perangkat desa beserta orang yang memegangnya. Jabatan yang belum punya
    /// orang tetap mempunyai baris (<see cref="IsBaru"/>) sehingga tabel selalu
    /// lengkap dari Kepala Desa sampai Kadus.
    ///
    /// Tabel ini hanya tampilan: isian data diri dilakukan lewat form lengkap
    /// (tombol Tambah Orang atau tombol baris), bukan lewat sel. Jabatan
    /// yang boleh dipegang lebih dari satu orang — misalnya KADUS untuk tiap
    /// dusun — tampil sebagai satu baris per orang, dan kolom
    /// <see cref="WilayahRingkas"/> menunjukkan dusun masing-masing.
    /// </summary>
    public class BarisPerangkatViewModel : ObservableObject
    {
        private readonly PerangkatDesa? _sumber;

        /// <summary>Baris untuk orang yang sudah tercatat di database.</summary>
        public BarisPerangkatViewModel(PerangkatDesa sumber)
        {
            _sumber = sumber ?? throw new ArgumentNullException(nameof(sumber));
            JabatanTampil = sumber.JabatanTampil;
            Status = sumber.Status;
            StatusTampil = sumber.StatusTampil;
            ToolTipAksiSk = sumber.ToolTipAksiSk;
        }

        /// <summary>Baris kosong penanda jabatan yang belum ada orangnya.</summary>
        public BarisPerangkatViewModel(string jabatan)
        {
            _sumber = null;
            JabatanTampil = jabatan;
            Status = string.Empty;
            StatusTampil = "BELUM DIISI";
            ToolTipAksiSk = $"Belum ada orang pada jabatan {jabatan}. Klik untuk membuka panel " +
                            "Surat Keputusan dengan contoh jabatan ini.";
        }

        /// <summary>Nama jabatan baris ini — selalu salah satu dari <c>JabatanPerangkat.Resmi</c>.</summary>
        public string JabatanTampil { get; }

        /// <summary>Baris tanpa orang: penanda jabatan ini belum terisi.</summary>
        public bool IsBaru => _sumber is null;

        /// <summary>Baris sudah memuat orang, jadi boleh dihapus.</summary>
        public bool AdaData => _sumber is not null;

        /// <summary>Data sumber baris; null untuk baris kosong penanda.</summary>
        public PerangkatDesa? Sumber => _sumber;

        /// <summary>
        /// Tooltip tombol SK pada baris ini — menjelaskan apa yang terjadi
        /// bila tombol SK di kolom AKSI diklik.
        /// </summary>
        public string ToolTipAksiSk { get; }

        /// <summary>Nilai mentah status, dipakai pewarna lencana di tampilan.</summary>
        public string Status { get; }

        /// <summary>Status untuk tampilan; baris kosong berlabel <c>BELUM DIISI</c>.</summary>
        public string StatusTampil { get; }

        public string Nama => _sumber?.Nama ?? string.Empty;

        public string NIK => _sumber?.NIK ?? string.Empty;

        public string NomorHP => _sumber?.NomorHP ?? string.Empty;

        public string MasaJabatan => _sumber?.MasaJabatanTampil ?? "Belum ditetapkan";

        // ---------- kolom mengikuti berkas "DATA PERANGKAT" contoh ----------
        // (nilai kosong dicetak "-" sama seperti pada berkas contoh.)

        /// <summary>Jenis kelamin siap cetak (Laki-laki/Perempuan); "-" bila belum ada.</summary>
        public string JenisKelamin => string.IsNullOrWhiteSpace(_sumber?.JenisKelamin)
            ? "-" : PerangkatDesa.JenisKelaminRingkas(_sumber!.JenisKelamin);

        /// <summary>Tempat lahir; "-" bila belum diisi.</summary>
        public string TempatLahir => AtauStrip(_sumber?.TempatLahir);

        /// <summary>Tanggal lahir, format dd-MM-yyyy; "-" bila belum ada.</summary>
        public string TanggalLahir => _sumber?.TanggalLahir?.ToString("dd-MM-yyyy") ?? "-";

        /// <summary>Agama; "-" bila belum diisi.</summary>
        public string Agama => AtauStrip(_sumber?.Agama);

        /// <summary>Nomor rekomendasi pengangkatan; "-" bila belum diisi.</summary>
        public string NomorRekomendasi => AtauStrip(_sumber?.NomorRekomendasi);

        /// <summary>Tanggal rekomendasi, format dd-MM-yyyy; "-" bila belum ada.</summary>
        public string TanggalRekomendasi => _sumber?.TanggalRekomendasi?.ToString("dd-MM-yyyy") ?? "-";

        /// <summary>Tahun lulus; "-" bila belum diisi.</summary>
        public string TahunLulus => AtauStrip(_sumber?.TahunLulus);

        /// <summary>Nomor ijazah; "-" bila belum diisi.</summary>
        public string NoIjasah => AtauStrip(_sumber?.NoIjasah);

        private static string AtauStrip(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "-" : nilai!.Trim();

        /// <summary>Nomor SK pelantikan/pemberhentian; "-" bila belum ada.</summary>
        public string NomorSk => string.IsNullOrWhiteSpace(_sumber?.NomorSK) ? "-" : _sumber!.NomorSK!.Trim();

        /// <summary>Tanggal pengangkatan (TanggalSK), format dd-MM-yyyy; "-" bila belum ada.</summary>
        public string TanggalPengangkatan => _sumber?.TanggalSK?.ToString("dd-MM-yyyy") ?? "-";

        /// <summary>Pendidikan terakhir; "-" bila belum diisi.</summary>
        public string Pendidikan => string.IsNullOrWhiteSpace(_sumber?.Pendidikan) ? "-" : _sumber!.Pendidikan!.Trim();

        /// <summary>Nomor rekening bank; "-" bila belum diisi.</summary>
        public string NomorRekening => string.IsNullOrWhiteSpace(_sumber?.NomorRekening) ? "-" : _sumber!.NomorRekening!.Trim();

        /// <summary>Nomor kepesertaan BPJS; "-" bila belum diisi.</summary>
        public string NomorBpjs => string.IsNullOrWhiteSpace(_sumber?.NomorBPJS) ? "-" : _sumber!.NomorBPJS!.Trim();

        /// <summary>
        /// Dusun/RT/RW pemegang jabatan, contoh "Kalenjaya, RT 01/RW 02".
        /// Penanda baris kosong berarti "-". Kolom inilah yang membedakan
        /// beberapa orang pada jabatan yang sama (KADUS per dusun, dst.).
        /// </summary>
        public string WilayahRingkas => _sumber?.WilayahRingkas ?? "-";
    }
}
