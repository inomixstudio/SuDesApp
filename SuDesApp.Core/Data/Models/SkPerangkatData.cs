using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>Jenis Surat Keputusan yang disediakan template-nya.</summary>
    public enum SkJenisPerangkat
    {
        Pengangkatan,
        Pemberhentian,

        /// <summary>
        /// Penetapan (bukan pengangkatan) — SK KPM pada contoh dokumen memakai
        /// bentuk ini, misalnya "PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM)".
        /// </summary>
        Penetapan
    }

    /// <summary>
    /// Isian satu dokumen SK yang dikirim ke generator PDF. Bila sebuah nilai
    /// kosong, generator mencetak garis titik-titik (untuk contoh yang belum
    /// terisi orang).
    /// </summary>
    public sealed class SkPerangkatIsi
    {
        public SkJenisPerangkat Jenis { get; init; } = SkJenisPerangkat.Pengangkatan;

        /// <summary>Kelompok jabatan (lihat <see cref="JabatanPerangkat.UrutanKelompok"/>).</summary>
        public string Kelompok { get; init; } = string.Empty;

        /// <summary>Nomor SK; kosong → digariskan.</summary>
        public string Nomor { get; init; } = string.Empty;

        /// <summary>Tanggal penandatanganan SK; null → hari ini.</summary>
        public DateTime? Tanggal { get; init; }

        public string Nama { get; init; } = string.Empty;
        public string NIK { get; init; } = string.Empty;
        public string Jabatan { get; init; } = string.Empty;

        /// <summary>Keterangan wilayah, mis. "RT 01/RW 02" atau dusun; boleh kosong.</summary>
        public string Wilayah { get; init; } = string.Empty;

        public DateTime? Mulai { get; init; }
        public DateTime? Selesai { get; init; }

        /// <summary>Alasan pemberhentian, mis. "karena masa jabatan telah berakhir".</summary>
        public string Alasan { get; init; } = string.Empty;

        /// <summary>
        /// Penanda diktum pertama. Contoh SK tidak seragam: Posyandu memakai
        /// "KESATU", sedangkan Linmas memakai "PERTAMA". Kosong memakai bawaan
        /// template kelompok, lalu "KESATU".
        /// </summary>
        public string PenandaDiktum { get; init; } = string.Empty;

        /// <summary>
        /// Butir blok "Memperhatikan". Kosong memakai butir bawaan kelompok
        /// (hasil kesepakatan/musyawarah), lalu bawaan umum katalog.
        /// </summary>
        public IReadOnlyList<string> Memperhatikan { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Isi diktum ketiga. Kosong memakai kalimat bawaan katalog. Diktum ini
        /// menampilkan "{PENANDA}", diganti penanda diktum pertama.
        /// </summary>
        public string KetentuanKetiga { get; init; } = string.Empty;

        /// <summary>
        /// Penerima tembusan, satu butir per baris. Kosong memakai daftar bawaan
        /// katalog; butir yang dikosongkan operator berarti SK itu memang tidak
        /// punya tembusan sama sekali.
        /// </summary>
        public IReadOnlyList<string>? Tembusan { get; init; }

        /// <summary>
        /// Daftar nama pada lampiran SK. Kosong berarti SK satu orang (diktum
        /// KESATU menyebut namanya langsung); berisi berarti SK banyak orang
        /// sekaligus — diktum KESATU merujuk lampiran dan tabelnya dicetak di
        /// halaman berikutnya, dikelompokkan per unit (mis. Posyandu).
        /// </summary>
        public IReadOnlyList<BarisLampiranSk> Lampiran { get; init; } = Array.Empty<BarisLampiranSk>();

        /// <summary>Benar bila SK memakai diktum lampiran (banyak orang).</summary>
        public bool AdaLampiran => Lampiran.Count > 0;

        /// <summary>True bila ini contoh kosong (tanpa orang tertentu).</summary>
        public bool Contoh { get; init; }
    }

    /// <summary>
    /// Template SK untuk satu kelompok jabatan: judul, kalimat keputusan,
    /// serta daftar pertimbangan dan dasar hukum. Teks memuat placeholder
    /// {JABATAN}, {NAMA}, {NIK}, {DESA}, {WILAYAH}, {MULAI}, {SELESAI},
    /// {TANGGAL} yang diganti generator.
    ///
    /// Dasar hukum disusun dari regulasi nasional yang umum dirujuk contoh SK
    /// desa (ciptadesa.com, JDIH kabupaten). Operator tetap disarankan
    /// menyesuaikan dengan Perbup/Perdes setempat sebelum menetapkan.
    /// </summary>
    /// <summary>
    /// Kategori sumber SK per kelompok jabatan, mengikuti praktik nyata penerbitan SK:
    /// 
    /// - <b>KepalaDesa</b>: SK per orang yang ditandatangani Kepala Desa (perangkat desa,
    ///   kadus, RT/RW, kader, dll. — perilaku lama aplikasi).
    /// - <b>KepalaDesaBersama</b>: satu SK Kepala Desa untuk banyak orang sekaligus
    ///   dengan lampiran daftar nama (Linmas, kader Posyandu/Pokjanal) — bentuk SK-nya
    ///   tetap milik Kepala Desa.
    /// - <b>Bupati</b>: SK diterbitkan Bupati (SK Kepala Desa, SK anggota BPD).
    ///   Aplikasi tidak menerbitkan SK ini; yang dibutuhkan adalah mengarsipkan berkas
    ///   PDF-nya dan menautkannya dari data perangkat.
    /// </summary>
    public enum SumberSkPerangkat
    {
        KepalaDesa = 0,
        KepalaDesaBersama = 1,
        Bupati = 2
    }

    public sealed class SkKelompokTemplate
    {
        public string Kelompok { get; init; } = string.Empty;

        public string JudulPengangkatan { get; init; } = string.Empty;
        public string JudulPemberhentian { get; init; } = string.Empty;

        /// <summary>
        /// Judul penetapan. Bila terisi, kelompok ini menawarkan jenis SK
        /// <see cref="SkJenisPerangkat.Penetapan"/> (mis. KPM).
        /// </summary>
        public string JudulPenetapan { get; init; } = string.Empty;

        /// <summary>
        /// Penanda diktum pertama untuk kelompok ini; null memakai "KESATU".
        /// Linmas pada contoh dokumen memakai "PERTAMA".
        /// </summary>
        public string? PenandaDiktum { get; init; }

        /// <summary>Butir "Menimbang" untuk SK pengangkatan (huruf a, b, c).</summary>
        public IReadOnlyList<string> Menimbang { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Butir "Memperhatikan" bawaan kelompok; kosong memakai bawaan umum
        /// katalog. Contoh: Linmas menyebut hasil kesepakatan sebelumnya, KPM
        /// menyebut hasil musyawarah desa.
        /// </summary>
        public IReadOnlyList<string> Memperhatikan { get; init; } = Array.Empty<string>();

        /// <summary>Butir "Mengingat" — daftar dasar hukum (angka 1, 2, 3).</summary>
        public IReadOnlyList<string> Mengingat { get; init; } = Array.Empty<string>();

        /// <summary>Kalimat keputusan pengangkatan; null memakai kalimat generik.</summary>
        public string? KetentuanPengangkatan { get; init; }

        /// <summary>Kalimat keputusan pemberhentian; null memakai kalimat generik.</summary>
        public string? KetentuanPemberhentian { get; init; }

        /// <summary>Kalimat keputusan penetapan; null memakai kalimat generik.</summary>
        public string? KetentuanPenetapan { get; init; }

        /// <summary>True bila kelompok ini punya SK penetapan (mis. KPM).</summary>
        public bool BisaPenetapan => !string.IsNullOrWhiteSpace(JudulPenetapan);

        /// <summary>Dasar hukum siap tampil untuk panel kelompok di halaman.</summary>
        public string DasarHukumTeks =>
            string.Join(Environment.NewLine, Mengingat.Select((d, i) => $"{i + 1}. {d}"));

        /// <summary>
        /// Siapa yang menerbitkan SK kelompok ini (lihat <see cref="SumberSkPerangkat"/>).
        /// Bila Bupati, panel SK kelompok ini tidak lagi menawarkan penerbitan dokumen
        /// melainkan pengarsipan berkas PDF SK dari Bupati.
        /// </summary>
        public SumberSkPerangkat Sumber { get; init; } = SumberSkPerangkat.KepalaDesa;

        /// <summary>SK kelompok ini lazim diterbitkan untuk banyak orang sekaligus (SK bersama berlampiran).</summary>
        public bool Bersama { get; init; }
    }

    /// <summary>
    /// Katalog template SK per kelompok jabatan. Hanya kelompok yang punya
    /// dasar hukum jelas yang diberi template; kelompok LAINNYA sengaja tidak.
    /// </summary>
    public static class SkPerangkatKatalog
    {
        /// <summary>
        /// Judul keputusan umum (blok "TENTANG"): dipakai bila sebuah template
        /// kelompok tidak menentukan judulnya sendiri. Judul ini tercetak di
        /// dokumen, jadi tidak boleh kosong.
        /// </summary>
        public const string JudulPengangkatanUmum = "PENGANGKATAN {JABATAN} DESA {DESA}";

        public const string JudulPemberhentianUmum = "PEMBERHENTIAN {JABATAN} DESA {DESA}";

        public const string JudulPenetapanUmum = "PENETAPAN {JABATAN} DESA {DESA}";

        /// <summary>Kalimat keputusan umum, dipakai bila kelompok tidak mengganti.</summary>
        public const string KetentuanPengangkatanUmum =
            "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai {JABATAN} di Desa {DESA} {WILAYAH}, " +
            "terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.";

        public const string KetentuanPemberhentianUmum =
            "Memberhentikan saudara/i {NAMA}, NIK. {NIK}, dari jabatan {JABATAN} di Desa {DESA} {WILAYAH} {ALASAN}, " +
            "terhitung sejak tanggal {TANGGAL}.";

        public const string KetentuanPenetapanUmum =
            "Menetapkan saudara/i {NAMA}, NIK. {NIK}, sebagai {JABATAN} di Desa {DESA} {WILAYAH}, " +
            "terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.";

        /// <summary>
        /// Kalimat keputusan untuk SK yang memuat banyak orang sekaligus: diktum
        /// KESATU merujuk lampiran, bukan menyebut satu nama. Dipakai generator
        /// setiap kali isian SK punya lampiran.
        /// </summary>
        public const string KetentuanPengangkatanLampiranUmum =
            "Mengangkat nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini " +
            "sebagai {JABATAN} di Desa {DESA} {WILAYAH}, terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.";

        public const string KetentuanPemberhentianLampiranUmum =
            "Memberhentikan nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini " +
            "dari jabatan {JABATAN} di Desa {DESA} {WILAYAH} {ALASAN}, terhitung sejak tanggal {TANGGAL}.";

        public const string KetentuanPenetapanLampiranUmum =
            "Menetapkan nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini " +
            "sebagai {JABATAN} di Desa {DESA} {WILAYAH}, terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.";

        /// <summary>
        /// Diktum ketiga (kewajiban). Teksnya memakai "{PENANDA}" supaya kalimatnya
        /// ikut menyesuaikan diktum pertama kelompok (KESATU atau PERTAMA).
        /// </summary>
        public const string KetentuanKetigaUmum =
            "Perintah dalam ketentuan {PENANDA} wajib dilaksanakan oleh {JABATAN} dan pihak yang bersangkutan.";

        /// <summary>Penanda diktum pertama bila kelompok tidak menetapkannya sendiri.</summary>
        public const string PenandaDiktumBawaan = "KESATU";

        /// <summary>
        /// Blok "Memperhatikan" bawaan umum: nama yang ditetapkan sudah merupakan
        /// hasil kesepakatan/musyawarah. Kelompok yang punya butir sendiri
        /// (Linmas, KPM) memakai butirnya masing-masing.
        /// </summary>
        public static readonly IReadOnlyList<string> MemperhatikanUmum = new[]
        {
            "Bahwa nama-nama yang akan ditetapkan tersebut telah disepakati melalui musyawarah sesuai ketentuan yang berlaku."
        };

        /// <summary>
        /// Daftar tembusan bawaan, mengikuti bentuk SK desa pada umumnya: blok
        /// "Tembusan" bernomor di kiri bawah tanda tangan. Daftar ini hanya titik
        /// awal; penerima lain ditambahkan operator pada isian panel SK.
        /// </summary>
        public static readonly IReadOnlyList<string> TembusanBawaan = new[]
        {
            "Yth. Bagian Kesbang dan Linmas Kabupaten {KABUPATEN}",
            "Yth. Kecamatan {KECAMATAN}",
            "Yth. Danramil",
            "Yth. BPD",
            "Yth. LPMD",
            "Yth. yang bersangkutan"
        };

        /// <summary>
        /// Kalimat keputusan lampiran sesuai jenis SK. Versi tiga jenis:
        /// pengangkatan, pemberhentian, dan penetapan.
        /// </summary>
        public static string KetentuanLampiran(SkJenisPerangkat jenis) => jenis switch
        {
            SkJenisPerangkat.Pemberhentian => KetentuanPemberhentianLampiranUmum,
            SkJenisPerangkat.Penetapan => KetentuanPenetapanLampiranUmum,
            _ => KetentuanPengangkatanLampiranUmum
        };

        /// <summary>
        /// Kalimat keputusan lampiran untuk pengangkatan/pemberhentian
        /// (tanpa bentuk penetapan) — bentuk nyaman untuk halaman isian SK.
        /// </summary>
        public static string KetentuanLampiranUmum(bool pengangkatan) => pengangkatan
            ? KetentuanPengangkatanLampiranUmum
            : KetentuanPemberhentianLampiranUmum;

        /// <summary>Menimbang baku untuk SK pemberhentian (berlaku semua kelompok).</summary>
        public static readonly IReadOnlyList<string> MenimbangPemberhentian = new[]
        {
            "Bahwa saudara/i {NAMA} saat ini menjabat sebagai {JABATAN} di Desa {DESA}.",
            "Bahwa jabatan tersebut perlu diberhentikan sebagaimana dimaksud dalam butir pertimbangan.",
            "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
        };

        /// <summary>
        /// Menimbang baku untuk SK pemberhentian yang memuat banyak orang:
        /// menyebut lampiran, sebab "saudara/i {NAMA}" tidak masuk akal bila
        /// yang diberhentikan sepuluh orang sekaligus.
        /// </summary>
        public static readonly IReadOnlyList<string> MenimbangPemberhentianLampiran = new[]
        {
            "Bahwa nama-nama sebagaimana tercantum dalam Lampiran saat ini menjabat sebagai {JABATAN} di Desa {DESA}.",
            "Bahwa jabatan tersebut perlu diberhentikan sebagaimana dimaksud dalam butir pertimbangan.",
            "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
        };

        private static readonly Dictionary<string, SkKelompokTemplate> _perKelompok = new(StringComparer.Ordinal)
        {
            ["PERANGKAT DESA"] = new SkKelompokTemplate
            {
                Kelompok = "PERANGKAT DESA",
                JudulPengangkatan = "PENGANGKATAN {JABATAN}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka kelancaran penyelenggaraan pemerintahan, pembangunan, dan pelayanan masyarakat di Desa {DESA} perlu mengisi jabatan {JABATAN}.",
                    "Bahwa saudara/i {NAMA} merupakan warga Desa {DESA} yang memenuhi syarat untuk mengisi jabatan tersebut.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 43 Tahun 2014 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 83 Tahun 2015 tentang Pengangkatan dan Pemberhentian Perangkat Desa sebagaimana diubah dengan Peraturan Menteri Dalam Negeri Nomor 67 Tahun 2017;",
                    "Sistem Organisasi dan Tata Kerja Pemerintah Desa {DESA}."
                },
                KetentuanPengangkatan =
                    "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai {JABATAN} pada Pemerintahan Desa {DESA} {WILAYAH}, " +
                    "terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.",
                KetentuanPemberhentian =
                    "Memberhentikan saudara/i {NAMA}, NIK. {NIK}, dari jabatan {JABATAN} pada Pemerintahan Desa {DESA} {WILAYAH} {ALASAN}, " +
                    "terhitung sejak tanggal {TANGGAL}."
            },
            ["RT/RW"] = new SkKelompokTemplate
            {
                Kelompok = "RT/RW",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa penyelenggaraan ketertiban dan pelayanan warga di Desa {DESA} memerlukan kepengurusan {JABATAN} yang ditetapkan.",
                    "Bahwa saudara/i {NAMA} diusulkan oleh musyawarah warga untuk mengisi jabatan {JABATAN} {WILAYAH} dan memenuhi syarat.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 43 Tahun 2014 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Desa {DESA} tentang Pembentukan dan Tata Kerja Rukun Tetangga dan Rukun Warga."
                }
            },
            ["LINMAS"] = new SkKelompokTemplate
            {
                Kelompok = "LINMAS",
                // SK Linmas lazim satu SK untuk ±10 orang dengan lampiran daftar nama
                // (sesuai contoh dokumen pemelihara).
                Bersama = true,
                // Contoh Linmas memakai PERTAMA/KEDUA/KETIGA, bukan KESATU.
                PenandaDiktum = "PERTAMA",
                JudulPengangkatan = "PENGANGKATAN ANGGOTA {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN ANGGOTA {JABATAN} DESA {DESA}",
                Memperhatikan = new[]
                {
                    "Bahwa nama-nama yang akan ditetapkan tersebut telah disepakati dalam kesepakatan warga atau keputusan sebelumnya."
                },
                Menimbang = new[]
                {
                    "Bahwa dalam rangka penciptaan ketertiban umum dan keamanan di Desa {DESA} perlu ditetapkan anggota {JABATAN}.",
                    "Bahwa saudara/i {NAMA} berdomisili di Desa {DESA} dan bersedia diangkat sebagai {JABATAN}.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 23 Tahun 2014 tentang Pemerintahan Daerah;",
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Bupati tentang Pembinaan dan Pelaksanaan Perlindungan Masyarakat;",
                    "Peraturan Desa {DESA} tentang Pembinaan Linmas."
                }
            },
            ["LPM"] = new SkKelompokTemplate
            {
                Kelompok = "LPM",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pembinaan kemasyarakatan dan pemberdayaan warga di Desa {DESA} perlu ditetapkan {JABATAN}.",
                    "Bahwa saudara/i {NAMA} memenuhi syarat dan bersedia mengemban amanah tersebut.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 43 Tahun 2014 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 18 Tahun 2018 tentang Pedoman Pembinaan Kelembagaan Desa;",
                    "Peraturan Desa {DESA} tentang Lembaga Pemberdayaan Masyarakat Desa."
                }
            },
            ["PKK"] = new SkKelompokTemplate
            {
                Kelompok = "PKK",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka mendukung program pemberdayaan keluarga di Desa {DESA} perlu ditetapkan pengurus Tim Penggerak PKK Desa {DESA}.",
                    "Bahwa saudara/i {NAMA} ditetapkan berdasarkan musyawarah pengurus untuk mengisi jabatan {JABATAN}.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Presiden Nomor 99 Tahun 2017 tentang Gerakan Pemberdayaan dan Kesejahteraan Keluarga;",
                    "Peraturan Menteri Dalam Negeri Nomor 36 Tahun 2020 tentang Peraturan Pelaksanaan Peraturan Presiden Nomor 99 Tahun 2017;",
                    "Peraturan Desa {DESA} tentang Tim Penggerak PKK Desa."
                }
            },
            ["POSYANDU"] = new SkKelompokTemplate
            {
                Kelompok = "POSYANDU",
                // SK kader Posyandu/Pokjanal diterbitkan satu SK untuk seluruh kader
                // dengan lampiran dikelompokkan per unit Posyandu (contoh dokumen
                // pemelihara: Pokjanal + Sakura I–V).
                Bersama = true,
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pelayanan kesehatan dasar kepada ibu, bayi, dan balita di Desa {DESA} perlu ditetapkan {JABATAN}.",
                    "Bahwa saudara/i {NAMA} bersedia dan memenuhi syarat untuk mengemban tugas tersebut.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 36 Tahun 2009 tentang Kesehatan;",
                    "Undang-Undang Nomor 52 Tahun 2009 tentang Perkembangan Kependudukan dan Pembangunan Keluarga;",
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Bupati tentang Penyelenggaraan Posyandu; dan",
                    "Peraturan Desa {DESA} tentang Penyelenggaraan Posyandu."
                }
            },
            ["BPD"] = new SkKelompokTemplate
            {
                Kelompok = "BPD",
                // SK anggota BPD diterbitkan Bupati (SK bersama seluruh anggota),
                // jadi aplikasi tidak menerbitkan dokumennya — yang dibutuhkan
                // operator adalah mengarsipkan berkas PDF SK Bupati dan menautkannya.
                Sumber = SumberSkPerangkat.Bupati,
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa keanggotaan Badan Permusyawaratan Desa {DESA} perlu diisi guna menjamin kelangsungan penyelenggaraan pemerintahan desa.",
                    "Bahwa saudara/i {NAMA} diusulkan melalui musyawarah desa dan memenuhi syarat sebagai {JABATAN}.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 43 Tahun 2014 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 112 Tahun 2014 tentang Pedoman Pembentukan dan Kedudukan Badan Permusyawaratan Desa."
                }
            },
            ["KADER STUNTING"] = new SkKelompokTemplate
            {
                Kelompok = "KADER STUNTING",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka percepatan penurunan stunting di Desa {DESA} perlu ditetapkan {JABATAN}.",
                    "Bahwa saudara/i {NAMA} merupakan warga Desa {DESA} yang bersedia dan memenuhi syarat.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Presiden Nomor 72 Tahun 2021 tentang Rencana Aksi Nasional Percepatan Penurunan Stunting;",
                    "Peraturan Desa {DESA} tentang Kader Desa Pencegahan Stunting."
                }
            },
            ["KADER KPM"] = new SkKelompokTemplate
            {
                Kelompok = "KADER KPM",
                // SK KPM pada contoh dokumen memakai bentuk penetapan,
                // bukan pengangkatan.
                JudulPengangkatan = "PENGANGKATAN KADER PEMBANGUNAN MANUSIA (KPM) DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN KADER PEMBANGUNAN MANUSIA (KPM) DESA {DESA}",
                JudulPenetapan = "PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM) DESA {DESA}",
                Memperhatikan = new[]
                {
                    "Bahwa nama-nama yang akan ditetapkan tersebut merupakan hasil musyawarah desa."
                },
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pembangunan sumber daya manusia dan pencegahan stunting di Desa {DESA} perlu ditetapkan Kader Pembangunan Manusia (KPM).",
                    "Bahwa saudara/i {NAMA} merupakan warga Desa {DESA} yang dipilih melalui musyawarah desa untuk mengemban tugas tersebut.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Presiden Nomor 72 Tahun 2021 tentang Rencana Aksi Nasional Percepatan Penurunan Stunting;",
                    "Peraturan Presiden Nomor 83 Tahun 2017 tentang Kebijakan Strategis Pangan dan Gizi;",
                    "Peraturan Desa {DESA} tentang Kader Pembangunan Manusia."
                }
            }
        };

        /// <summary>Daftar template dalam urutan kelompok (untuk panel & pengujian).</summary>
        public static IReadOnlyList<SkKelompokTemplate> Semua =>
            JabatanPerangkat.UrutanKelompok
                .Where(_perKelompok.ContainsKey)
                .Select(k => _perKelompok[k])
                .ToList();

        /// <summary>Ambil template satu kelompok; null bila kelompok tidak punya template.</summary>
        public static SkKelompokTemplate? Cari(string? kelompok) =>
            string.IsNullOrWhiteSpace(kelompok) ? null
            : _perKelompok.TryGetValue(kelompok.Trim(), out var t) ? t : null;

        /// <summary>True bila kelompok punya template SK.</summary>
        public static bool Ada(string? kelompok) => Cari(kelompok) != null;

        /// <summary>
        /// Sumber SK satu kelompok. Kelompok tanpa template (LAINNYA) dianggap SK
        /// Kepala Desa per orang, sama seperti sebelumnya.
        /// </summary>
        public static SumberSkPerangkat Sumber(string? kelompok) =>
            Cari(kelompok)?.Sumber ?? SumberSkPerangkat.KepalaDesa;

        /// <summary>True bila SK kelompok ini diterbitkan Bupati (arsip berkas, bukan penerbitan).</summary>
        public static bool DariBupati(string? kelompok) => Sumber(kelompok) == SumberSkPerangkat.Bupati;

        /// <summary>True bila SK kelompok ini lazim diterbitkan bersama berlampiran.</summary>
        public static bool LazimBersama(string? kelompok) => Cari(kelompok)?.Bersama == true;

        /// <summary>True bila kelompok punya SK penetapan (mis. KPM).</summary>
        public static bool BisaPenetapan(string? kelompok) => Cari(kelompok)?.BisaPenetapan == true;

        /// <summary>
        /// Penanda diktum pertama kelompok; null/empty memakai
        /// <see cref="PenandaDiktumBawaan"/>. Contoh Linmas memakai "PERTAMA".
        /// </summary>
        public static string PenandaDiktum(string? kelompok)
        {
            string? nilai = Cari(kelompok)?.PenandaDiktum;
            return string.IsNullOrWhiteSpace(nilai) ? PenandaDiktumBawaan : nilai.Trim();
        }

        /// <summary>
        /// Blok "Memperhatikan" bawaan kelompok; kelompok tanpa butir sendiri
        /// memakai <see cref="MemperhatikanUmum"/>.
        /// </summary>
        public static IReadOnlyList<string> Memperhatikan(string? kelompok)
        {
            var butir = Cari(kelompok)?.Memperhatikan;
            return butir is { Count: > 0 } ? butir : MemperhatikanUmum;
        }
    }
}
