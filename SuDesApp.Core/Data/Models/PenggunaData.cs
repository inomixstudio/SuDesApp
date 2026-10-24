using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Peran pengguna aplikasi. Satu instalasi desa biasanya punya satu
    /// operator, satu sekretaris, dan satu kepala desa — karena itu peran
    /// dibuat tetap (bukan izin bebas per pengguna) supaya tidak ada
    /// kombinasi yang sulit diaudit.
    ///
    /// Administrator adalah peran pengelola: membuat akun, mengganti peran,
    /// dan mereset kata sandi. Hanya akun yang benar-benar dipakai untuk
    /// mengelola aplikasi yang sebaiknya memegang peran ini.
    /// </summary>
    public static class PeranPengguna
    {
        public const string Administrator = "ADMINISTRATOR";
        public const string Operator = "OPERATOR";
        public const string Sekdes = "SEKDES";
        public const string Kades = "KADES";
        public const string Auditor = "AUDITOR";

        /// <summary>Semua peran, urut dari yang paling luas wewenangnya.</summary>
        public static readonly IReadOnlyList<string> Semua = new[]
        {
            Administrator, Sekdes, Operator, Kades, Auditor
        };

        public static bool Valid(string? peran) =>
            !string.IsNullOrWhiteSpace(peran)
            && Semua.Contains(peran.Trim().ToUpperInvariant(), StringComparer.Ordinal);

        public static string Normalisasi(string? peran) =>
            Valid(peran) ? peran!.Trim().ToUpperInvariant() : Operator;

        /// <summary>Nama peran untuk ditampilkan di layar.</summary>
        public static string Tampilan(string? peran) => Normalisasi(peran) switch
        {
            Administrator => "Administrator",
            Sekdes => "Sekretaris Desa",
            Kades => "Kepala Desa",
            Auditor => "Auditor",
            _ => "Operator"
        };

        /// <summary>Penjelasan singkat wewenang peran, untuk halaman kelola pengguna.</summary>
        public static string Keterangan(string? peran) => Normalisasi(peran) switch
        {
            Administrator => "Semua menu, termasuk membuat akun dan mengganti peran pengguna.",
            Sekdes => "Semua menu kecuali kelola pengguna; termasuk pengaturan aplikasi dan cadangan.",
            Kades => "Memeriksa dan menandatangani surat, laporan, verifikasi keaslian, dan riwayat aktivitas.",
            Auditor => "Hanya membaca: laporan, verifikasi keaslian surat, dan riwayat aktivitas.",
            _ => "Membuat surat, mengelola data warga dan perangkat, layanan online, dan laporan."
        };
    }

    /// <summary>
    /// Izin bertingkat yang dipakai menu dan halaman. Izin diturunkan dari
    /// peran lewat <see cref="HakAkses"/> — bukan disimpan per pengguna —
    /// supaya aturan siapa-boleh-apa hanya ada di satu tempat.
    /// </summary>
    public enum IzinAplikasi
    {
        /// <summary>Membuat, mengubah, dan menghapus surat.</summary>
        BuatSurat,

        /// <summary>Memverifikasi dan menandai surat sudah ditandatangani.</summary>
        TandaTanganSurat,

        KelolaWarga,
        KelolaPerangkat,
        Laporan,
        LayananOnline,

        /// <summary>Memeriksa keaslian surat dari kode verifikasi.</summary>
        VerifikasiSurat,

        RiwayatAktivitas,
        PengaturanAplikasi,

        /// <summary>Ekspor/impor database dan pencadangan.</summary>
        Cadangan,

        /// <summary>Membuat akun, mengganti peran, dan mereset kata sandi.</summary>
        KelolaPengguna
    }

    /// <summary>Peta peran → izin. Satu-satunya sumber kebenaran hak akses.</summary>
    public static class HakAkses
    {
        private static readonly IReadOnlySet<IzinAplikasi> SemuaIzin =
            Enum.GetValues<IzinAplikasi>().ToHashSet();

        private static readonly IReadOnlySet<IzinAplikasi> IzinOperator = new HashSet<IzinAplikasi>
        {
            IzinAplikasi.BuatSurat,
            IzinAplikasi.KelolaWarga,
            IzinAplikasi.KelolaPerangkat,
            IzinAplikasi.Laporan,
            IzinAplikasi.LayananOnline,
            IzinAplikasi.VerifikasiSurat,
            IzinAplikasi.RiwayatAktivitas
        };

        private static readonly IReadOnlySet<IzinAplikasi> IzinSekdes = new HashSet<IzinAplikasi>
        {
            IzinAplikasi.BuatSurat,
            IzinAplikasi.TandaTanganSurat,
            IzinAplikasi.KelolaWarga,
            IzinAplikasi.KelolaPerangkat,
            IzinAplikasi.Laporan,
            IzinAplikasi.LayananOnline,
            IzinAplikasi.VerifikasiSurat,
            IzinAplikasi.RiwayatAktivitas,
            IzinAplikasi.PengaturanAplikasi,
            IzinAplikasi.Cadangan
        };

        private static readonly IReadOnlySet<IzinAplikasi> IzinKades = new HashSet<IzinAplikasi>
        {
            IzinAplikasi.TandaTanganSurat,
            IzinAplikasi.Laporan,
            IzinAplikasi.VerifikasiSurat,
            IzinAplikasi.RiwayatAktivitas
        };

        private static readonly IReadOnlySet<IzinAplikasi> IzinAuditor = new HashSet<IzinAplikasi>
        {
            IzinAplikasi.Laporan,
            IzinAplikasi.VerifikasiSurat,
            IzinAplikasi.RiwayatAktivitas
        };

        /// <summary>True bila peran ini boleh melakukan izin tersebut.</summary>
        public static bool Boleh(string? peran, IzinAplikasi izin) =>
            IzinUntuk(peran).Contains(izin);

        /// <summary>Seluruh izin yang dimiliki sebuah peran.</summary>
        public static IReadOnlySet<IzinAplikasi> IzinUntuk(string? peran) => PeranPengguna.Normalisasi(peran) switch
        {
            PeranPengguna.Administrator => SemuaIzin,
            PeranPengguna.Sekdes => IzinSekdes,
            PeranPengguna.Kades => IzinKades,
            PeranPengguna.Auditor => IzinAuditor,
            _ => IzinOperator
        };
    }

    /// <summary>
    /// Satu akun pengguna aplikasi. Kata sandi TIDAK pernah disimpan apa adanya:
    /// yang tersimpan hanya salt acak + hash PBKDF2 (lihat <c>KataSandiPengguna</c>).
    /// </summary>
    public class Pengguna
    {
        public int ID { get; set; }

        /// <summary>Nama untuk masuk; disimpan huruf kecil supaya tidak ada dua akun beda huruf besar.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Nama lengkap yang ditampilkan di riwayat aktivitas dan halaman login.</summary>
        public string NamaTampilan { get; set; } = string.Empty;

        public string Peran { get; set; } = PeranPengguna.Operator;

        public string Salt { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;

        /// <summary>Jumlah putaran PBKDF2 saat hash dibuat (bisa dinaikkan tanpa memutus akun lama).</summary>
        public int Iterasi { get; set; } = 120_000;

        public bool Aktif { get; set; } = true;

        /// <summary>Jumlah percobaan gagal berturut-turut; direset saat berhasil masuk.</summary>
        public int GagalLogin { get; set; }

        /// <summary>Waktu sampai kapan akun terkunci karena percobaan gagal (tersimpan di database).</summary>
        public DateTime? TerkunciSampai { get; set; }

        public DateTime? LoginTerakhir { get; set; }

        public string? DibuatOleh { get; set; }
        public string? DiperbaruiOleh { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // ---------- properti tampilan ----------

        public string PeranTampil => PeranPengguna.Tampilan(Peran);

        public string StatusTampil => Aktif ? "Aktif" : "Nonaktif";

        public bool SedangTerkunci => TerkunciSampai.HasValue && TerkunciSampai.Value > DateTime.Now;

        /// <summary>True bila peran ini boleh melakukan izin tersebut.</summary>
        public bool Boleh(IzinAplikasi izin) => HakAkses.Boleh(Peran, izin);
    }
}
