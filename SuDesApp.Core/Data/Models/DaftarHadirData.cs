using System;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu baris peserta pada dokumen Daftar Hadir. Tidak disimpan ke database —
    /// hanya dipakai sebagai isian form lalu dirender ke PDF.
    /// </summary>
    public class DaftarHadirPeserta
    {
        public string Nama { get; set; } = string.Empty;

        /// <summary>Jenis kelamin singkat: "L" atau "P".</summary>
        public string JenisKelamin { get; set; } = string.Empty;

        public string Jabatan { get; set; } = string.Empty;
        public string Nip { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string NoHp { get; set; } = string.Empty;
        public string Alamat { get; set; } = string.Empty;
        public string Keterangan { get; set; } = string.Empty;

        /// <summary>True bila seluruh kolom isian kosong (baris dilewati saat cetak).</summary>
        public bool IsKosong =>
            string.IsNullOrWhiteSpace(Nama) &&
            string.IsNullOrWhiteSpace(JenisKelamin) &&
            string.IsNullOrWhiteSpace(Jabatan) &&
            string.IsNullOrWhiteSpace(Nip) &&
            string.IsNullOrWhiteSpace(Nik) &&
            string.IsNullOrWhiteSpace(NoHp) &&
            string.IsNullOrWhiteSpace(Alamat) &&
            string.IsNullOrWhiteSpace(Keterangan);
    }

    /// <summary>
    /// Data dokumen Daftar Hadir: judul yang bisa diganti lewat textbox, pilihan
    /// kolom yang dicetak (No., Nama, Jabatan, dan Tanda Tangan aktif secara
    /// default), serta opsi blok tanda tangan Kepala Desa di bagian bawah.
    /// Hari dan tanggal diisi lebih dulu dari tanggal cetak, tetapi tetap bisa
    /// diedit pada textbox sebelum dokumen dibuat.
    /// </summary>
    public class DaftarHadirData
    {
        /// <summary>Judul dokumen. Boleh berisi baris baru (Enter) untuk judul dua baris.</summary>
        public string Judul { get; set; } = "DAFTAR HADIR";

        /// <summary>Budaya Indonesia untuk penulisan nama hari/bulan.</summary>
        private static readonly CultureInfo Budaya = new("id-ID");

        /// <summary>
        /// Hari &amp; tanggal yang dicetak pada baris <c>Hari/Tanggal</c>, mis.
        /// "Jumat, 18 September 2026". Bisa diedit bebas; dibiarkan kosong berarti
        /// barisnya tidak dicetak.
        /// </summary>
        public string HariTanggal { get; set; } = string.Empty;

        /// <summary>
        /// Momen cetak — dipakai untuk mengisi awal <see cref="HariTanggal"/> dan
        /// sebagai tanggal pada baris kota di atas tanda tangan Kepala Desa.
        /// </summary>
        public DateTime TanggalCetak { get; set; } = DateTime.Now;

        /// <summary>Waktu kegiatan, mis. "09.00 WIB". Dikosongkan berarti barisnya tidak dicetak.</summary>
        public string Pukul { get; set; } = string.Empty;

        /// <summary>Tempat kegiatan, mis. "Aula Kantor Desa Sumberjaya". Dikosongkan berarti tidak dicetak.</summary>
        public string Tempat { get; set; } = string.Empty;

        /// <summary>Hari &amp; tanggal bawaan untuk sebuah tanggal, mis. "Jumat, 18 September 2026".</summary>
        public static string HariTanggalIndo(DateTime tanggal) => tanggal.ToString("dddd, d MMMM yyyy", Budaya);

        /// <summary>Tanggal saja tanpa nama hari, mis. "18 September 2026".</summary>
        public static string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", Budaya);

        /// <summary>
        /// Tanggal yang terkandung pada teks <see cref="HariTanggal"/> — dipakai supaya
        /// baris kota di atas tanda tangan ikut menyesuaikan bila harinya diubah.
        /// Mengembalikan null bila teksnya tidak bisa dibaca sebagai tanggal
        /// (mis. "Hari pertama kerja bakti").
        /// </summary>
        public DateTime? TanggalDariHariTanggal()
        {
            if (string.IsNullOrWhiteSpace(HariTanggal))
            {
                return null;
            }

            string teks = HariTanggal.Trim();

            // Buang nama hari di depan ("Jumat, 18 September 2026") karena beberapa
            // bentuk penulisan hari tidak dikenali parser.
            int koma = teks.IndexOf(',');
            string tanpaHari = koma >= 0 ? teks[(koma + 1)..].Trim() : teks;

            return DateTime.TryParse(teks, Budaya, DateTimeStyles.None, out var utuh)
                ? utuh
                : DateTime.TryParse(tanpaHari, Budaya, DateTimeStyles.None, out var sisa)
                    ? sisa
                    : null;
        }

        public List<DaftarHadirPeserta> Peserta { get; set; } = new();

        // ==== Pilihan kolom yang dicetak ====
        public bool TampilkanNo { get; set; } = true;
        public bool TampilkanNama { get; set; } = true;
        public bool TampilkanJenisKelamin { get; set; }
        public bool TampilkanJabatan { get; set; } = true;
        public bool TampilkanTandaTangan { get; set; } = true;
        public bool TampilkanNip { get; set; }
        public bool TampilkanNik { get; set; }
        public bool TampilkanNoHp { get; set; }
        public bool TampilkanAlamat { get; set; }
        public bool TampilkanKeterangan { get; set; }

        /// <summary>Tampilkan blok tanda tangan Kepala Desa di bagian bawah dokumen.</summary>
        public bool TampilkanFooterKepalaDesa { get; set; } = true;

        /// <summary>Cetak kop surat desa di atas daftar hadir (false = tanpa kop).</summary>
        public bool TampilkanKopSurat { get; set; } = true;

        /// <summary>Info desa untuk kop surat dan footer.</summary>
        public DesaData Desa { get; set; } = new();
    }
}
