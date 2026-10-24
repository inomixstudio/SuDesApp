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
    /// Dasar hukum disusun dari regulasi yang berlaku (UU, PP, dan Permendagri
    /// pengangkatan/pemberhentian perangkat desa) mengikuti format naskah dinas
    /// Peraturan Bupati tentang Tata Naskah Dinas Desa. Operator tetap disarankan
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
        /// Judul daftar pada halaman lampiran; kosong memakai "DAFTAR {judul
        /// keputusan}". Contoh SK desa memakai bentuknya sendiri — Posyandu:
        /// "DAFTAR NAMA KADER POSYANDU DAN KADER POKJANAL DESA {DESA}", Linmas:
        /// "DAFTAR PENGANGKATAN ANGGOTA SATUAN PERLINDUNGAN MASYARAKAT (LINMAS)
        /// DESA {DESA}". Dipakai untuk pengangkatan dan penetapan; SK pemberhentian
        /// memakai "DAFTAR {judul pemberhentian}" agar tidak salah sebut.
        /// </summary>
        public string JudulLampiran { get; init; } = string.Empty;

        /// <summary>
        /// Penanda diktum pertama untuk kelompok ini; null memakai "KESATU".
        /// Linmas pada contoh dokumen memakai "PERTAMA".
        /// </summary>
        public string? PenandaDiktum { get; init; }

        /// <summary>Butir "Menimbang" untuk SK pengangkatan (huruf a, b, c).</summary>
        public IReadOnlyList<string> Menimbang { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Butir "Menimbang" untuk SK pemberhentian; kosong memakai
        /// <see cref="Menimbang"/> milik template yang sama, lalu butir baku
        /// pemberhentian katalog. Pemberhentian punya pertimbangan yang berbeda
        /// dari pengangkatan (mis. konsultasi kepada Camat), jadi template yang
        /// merinci keduanya mengisinya di sini.
        /// </summary>
        public IReadOnlyList<string> MenimbangPemberhentian { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Butir "Memperhatikan" bawaan kelompok; kosong memakai bawaan umum
        /// katalog. Contoh: Linmas menyebut hasil kesepakatan sebelumnya, KPM
        /// menyebut hasil musyawarah desa.
        /// </summary>
        public IReadOnlyList<string> Memperhatikan { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Butir "Memperhatikan" untuk SK pemberhentian — contoh SK perangkat
        /// desa menyebut surat permohonan/pengunduran diri dan rekomendasi
        /// Camat, bukan berita acara penjaringan. Kosong memakai
        /// <see cref="Memperhatikan"/>.
        /// </summary>
        public IReadOnlyList<string> MemperhatikanPemberhentian { get; init; } = Array.Empty<string>();

        /// <summary>Butir "Mengingat" — daftar dasar hukum (angka 1, 2, 3).</summary>
        public IReadOnlyList<string> Mengingat { get; init; } = Array.Empty<string>();

        /// <summary>Kalimat keputusan pengangkatan; null memakai kalimat generik.</summary>
        public string? KetentuanPengangkatan { get; init; }

        /// <summary>
        /// Kalimat keputusan pengangkatan pada SK bersama (banyak orang dengan
        /// lampiran daftar nama); null memakai butir baku katalog
        /// (<see cref="SkPerangkatKatalog.KetentuanPengangkatanLampiranUmum"/>).
        /// Contoh SK desa memakai bentuknya sendiri — mis. LINMAS: “Mengangkat
        /// Anggota Satuan Perlindungan Masyarakat (LINMAS) Desa {DESA}.”
        /// </summary>
        public string? KetentuanPengangkatanLampiran { get; init; }

        /// <summary>
        /// Kalimat keputusan penetapan pada SK bersama; null memakai
        /// <see cref="SkPerangkatKatalog.KetentuanPenetapanLampiranUmum"/>.
        /// Contoh SK KPM memakai bentuk “Mengangkat … sebagai Kader
        /// Pembangunan Manusia (KPM) Desa {DESA}”.
        /// </summary>
        public string? KetentuanPenetapanLampiran { get; init; }

        /// <summary>Kalimat keputusan pemberhentian; null memakai kalimat generik.</summary>
        public string? KetentuanPemberhentian { get; init; }

        /// <summary>Kalimat keputusan penetapan; null memakai kalimat generik.</summary>
        public string? KetentuanPenetapan { get; init; }

        /// <summary>
        /// Diktum kewajiban (diktum KETIGA) khusus template ini; kosong memakai
        /// <see cref="SkPerangkatKatalog.KetentuanKetigaUmum"/>. Teksnya memuat
        /// "{PENANDA}" dan boleh memuat placeholder lain, sama seperti butir
        /// pertimbangan.
        /// </summary>
        public string? KetentuanKetiga { get; init; }

        /// <summary>True bila kelompok ini punya SK penetapan (mis. KPM).</summary>
        public bool BisaPenetapan => !string.IsNullOrWhiteSpace(JudulPenetapan);

        /// <summary>Dasar hukum siap tampil untuk panel kelompok di halaman.</summary>
        public string DasarHukumTeks
        {
            get
            {
                var baris = Mengingat.Select((d, i) => $"{i + 1}. {d}");
                // Panel menampilkan daftar ini MENTAH (tanpa IsiPlaceholder
                // generator), jadi placeholder nama wilayah haru terlihat sebagai
                // keterangan yang bisa dibaca — bukan "{KABUPATEN}" yang membuat
                // operator mengira tampilannya rusak.
                return string.Join(Environment.NewLine, baris)
                    .Replace("{KABUPATEN}", "[nama kabupaten]")
                    .Replace("{KECAMATAN}", "[nama kecamatan]")
                    .Replace("{DESA}", "[nama desa]");
            }
        }

        /// <summary>
        /// Daftar dasar hukum yang sudah bersih dari placeholder, untuk ditampilkan
        /// di panel SK (<see cref="DasarHukumTeks"/>); dokumen tetap memakai
        /// <see cref="Mengingat"/> asli karena generator yang mengganti
        /// placeholder dengan nama wilayah dari Pengaturan Desa.
        /// </summary>

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
    /// Katalog template SK per kelompok jabatan. Seluruh kelompok yang tampil
    /// di halaman Data Perangkat Desa punya template — termasuk Staf Desa,
    /// Operator Desa, dan Karang Taruna (LPM menampung pula nama lama LPMD)
    /// — sedangkan kelompok LAINNYA
    /// sengaja tidak sebab tidak punya dasar hukum tetap.
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

        /// <summary>
        /// Alasan pemberhentian yang dipakai bila operator belum mengisi kotak
        /// "Alasan" — WAJIB ada, karena SK pemberhentian yang tidak menyebutkan
        /// dasarnya tidak sah. Sebelumnya placeholder {ALASAN} dibiarkan kosong
        /// sehingga kalimatnya hanya berhenti pada "… Desa XYZ, terhitung sejak
        /// tanggal …" tanpa apa pun yang diberhentikan: dokumen terbit lewat
        /// jalur simpan form (jalur bawaan) tanpa satu pun peringatan.
        /// </summary>
        public const string AlasanPemberhentianKosong = "karena telah berakhir masa jabatannya";

        /// <summary>
        /// Alasan yang masuk ke kalimat pemberhentian, sudah berpola jadi:
        /// "karena telah berakhir masa jabatannya". Alasan operator memakai
        /// catatan baris (KeteranganWarga) bila ada.
        /// </summary>
        public static string KalimatAlasanPemberhentian(string? alasan) =>
            string.IsNullOrWhiteSpace(alasan)
                ? AlasanPemberhentianKosong
                : "karena " + alasan!.Trim().TrimEnd('.');

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
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
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
            // Staf sekretariat desa dan operator desa bukan jabatan struktural
            // menurut Permendagri 84/2015, jadi SK-nya ditetapkan Kepala Desa
            // berdasarkan SOTK dan penugasan; susunannya mengikuti contoh SK
            // perangkat desa, tanpa butir penjaringan calon perangkat desa.
            ["STAF DESA"] = new SkKelompokTemplate
            {
                Kelompok = "STAF DESA",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa untuk kelancaran administrasi pemerintahan dan pelayanan masyarakat di Desa {DESA} perlu ada {JABATAN} yang melaksanakan tugas administratif.",
                    "Bahwa saudara/i {NAMA} merupakan warga Desa {DESA} yang memenuhi syarat serta bersedia mengemban tugas tersebut.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 84 Tahun 2015 tentang Susunan Organisasi dan Tata Kerja Pemerintah Desa;",
                    "Sistem Organisasi dan Tata Kerja Pemerintah Desa {DESA}."
                },
                KetentuanKetiga =
                    "Dalam melaksanakan tugas sebagai {JABATAN}, wajib berpedoman pada Susunan Organisasi dan Tata Kerja "
                    + "Pemerintah Desa {DESA} serta menyampaikan laporan pelaksanaan tugasnya kepada Kepala Desa."
            },
            ["OPERATOR DESA"] = new SkKelompokTemplate
            {
                Kelompok = "OPERATOR DESA",
                // Jabatannya sudah berakhiran "DESA", jadi judul tidak menambah
                // kata DESA lagi — kalau tidak, tercetak "OPERATOR DESA DESA".
                JudulPengangkatan = "PENGANGKATAN {JABATAN} {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} {DESA}",
                Menimbang = new[]
                {
                    "Bahwa penyelenggaraan pemerintahan desa secara elektronik di Desa {DESA} memerlukan {JABATAN} yang mengelola data dan sistem informasi desa.",
                    "Bahwa saudara/i {NAMA} ditunjuk berdasarkan penugasan Kepala Desa, memiliki kemampuan mengoperasikan aplikasi pemerintahan desa, dan bersedia mengemban tugas tersebut.",
                    "Bahwa penunjukan tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 84 Tahun 2015 tentang Susunan Organisasi dan Tata Kerja Pemerintah Desa;",
                    "Peraturan Desa {DESA} tentang Tata Kelola Data dan Sistem Informasi Desa."
                },
                KetentuanKetiga =
                    "Dalam melaksanakan tugas sebagai {JABATAN}, wajib menjaga kerahasiaan dan keamanan data pemerintahan desa, "
                    + "mematuhi ketentuan tata kelola data, serta melaporkan pelaksanaan tugasnya kepada Kepala Desa."
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
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
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
                JudulPengangkatan = "PENGANGKATAN KEANGGOTAAN SATUAN PERLINDUNGAN MASYARAKAT ({JABATAN}) DESA {DESA}",
                // Judul daftar pada Contoh SK Linmas desa.
                JudulLampiran = "DAFTAR PENGANGKATAN ANGGOTA SATUAN PERLINDUNGAN MASYARAKAT (LINMAS) DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN KEANGGOTAAN SATUAN PERLINDUNGAN MASYARAKAT ({JABATAN}) DESA {DESA}",
                // Butir meniru contoh SK Linmas desa: penyegaran keanggotaan
                // Linmas sehubungan dengan susunan organisasi pemerintahan desa.
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pembinaan Satuan Perlindungan Masyarakat (LINMAS) di Desa {DESA} perlu menyesuaikan keanggotaan Linmas dengan susunan organisasi pemerintahan desa yang berlaku.",
                    "Bahwa guna memacu pemberdayaan Anggota Perlindungan Masyarakat (LINMAS) di Desa {DESA} perlu diadakan penyegaran keanggotaan Linmas.",
                    "Bahwa berdasarkan pertimbangan tersebut di atas perlu ditetapkan dengan Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Memperhatikan = new[]
                {
                    "Keputusan Kepala Desa {DESA} tentang Pengangkatan Anggota Satuan Perlindungan Masyarakat (LINMAS) Desa {DESA};"
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 14 Tahun 1950 tentang Pemerintahan Daerah;",
                    "Undang-Undang Nomor 2 Tahun 2002 tentang Kepolisian Negara Republik Indonesia;",
                    "Undang-Undang Nomor 3 Tahun 2002 tentang Pertahanan Negara;",
                    "Undang-Undang Nomor 10 Tahun 2004 tentang Pembentukan Peraturan Perundang-Undangan;",
                    "Undang-Undang Nomor 23 Tahun 2014 tentang Pemerintahan Daerah;",
                    "Undang-Undang Nomor 1 Tahun 2022 tentang Hubungan Keuangan Antara Pemerintah Pusat dan Pemerintahan Daerah;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Keputusan Presiden Nomor 56 Tahun 1972 tentang Penyerahan Pembinaan Organisasi Pertahanan Sipil dari Departemen Pertahanan kepada Departemen Dalam Negeri;",
                    "Kesepakatan Bersama Menteri Dalam Negeri dan Kepolisian Negara Republik Indonesia Nomor 119/1572/SJ Tahun 2002 tentang Kerjasama Pembinaan Penyelenggaraan Ketentraman dan Ketertiban Masyarakat;",
                    "Keputusan Menteri Dalam Negeri Nomor 36 Tahun 1979 tentang Pakaian Seragam dan Atribut Pertahanan Sipil;",
                    "{PERKEPALA_TANPA_NAMA} tentang Pembinaan dan Pelaksanaan Perlindungan Masyarakat;",
                    "Peraturan Desa {DESA} tentang Pembinaan Linmas."
                },
                // Satu SK bersama: diktum mengikuti contoh SK Linmas — anggota
                // LINMAS disebut sebagai satu kesatuan, namanya ada di lampiran.
                KetentuanPengangkatan = "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai Anggota Satuan Perlindungan Masyarakat ({JABATAN}) Desa {DESA} {WILAYAH}, terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.",
                KetentuanPengangkatanLampiran = "Mengangkat Anggota Satuan Perlindungan Masyarakat ({JABATAN}) Desa {DESA} sebagaimana tercantum dalam Lampiran Surat Keputusan ini.",
                KetentuanKetiga = "Anggota Linmas Desa {DESA} berkewajiban untuk mengikuti dan melaksanakan kegiatan pelatihan, piket, pembinaan, pengembangan dan pengawasan pelaksanaan kegiatan Siskamling, serta tugas ketentraman dan ketertiban masyarakat lainnya."
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
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 18 Tahun 2018 tentang Lembaga Kemasyarakatan Desa dan Lembaga Adat Desa;",
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
                JudulPengangkatan = "PENGANGKATAN KADER POSYANDU DAN KADER POKJANAL DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN KADER POSYANDU DAN KADER POKJANAL DESA {DESA}",
                // Judul daftar pada Contoh SK Posyandu desa.
                JudulLampiran = "DAFTAR NAMA KADER POSYANDU DAN KADER POKJANAL DESA {DESA}",
                // Butir meniru contoh SK Posyandu & Pokjanal desa.
                Menimbang = new[]
                {
                    "Bahwa untuk meningkatkan kesehatan masyarakat khususnya kesehatan ibu dan anak melalui program Posyandu di Desa {DESA};",
                    "Bahwa untuk kelancaran pelaksanaan program tersebut, maka perlu segera mengangkat yang cakap dan mampu melaksanakan tugas sebagai kader Posyandu dan Kader Pokjanal Desa {DESA};",
                    "Bahwa untuk melaksanakan ketentuan huruf a dan b di atas, perlu ditetapkan dengan Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 17 Tahun 2023 tentang Kesehatan;",
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 18 Tahun 2018 tentang Lembaga Kemasyarakatan Desa dan Lembaga Adat Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 19 Tahun 2011 tentang Pengintegrasian Layanan Sosial Dasar di Posyandu;",
                    "Peraturan Menteri Dalam Negeri Nomor 13 Tahun 2024 tentang Pos Pelayanan Terpadu;",
                    "Peraturan Menteri Dalam Negeri Nomor 1 Tahun 2013 tentang Pemberdayaan Masyarakat Melalui Gerakan Pemberdayaan dan Kesejahteraan Keluarga;",
                    "{PERDA} tentang Desa;",
                    "Peraturan Desa {DESA} tentang Penyelenggaraan Posyandu."
                },
                // Satu SK bersama: diktum mengikuti contoh SK Posyandu & Pokjanal —
                // anggota Pokjanal dicatat sebagai kader dengan Unit “POKJANAL
                // DESA” supaya jadi blok sendiri pada lampiran.
                KetentuanPengangkatan = "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai Kader Posyandu dan Kader Pokjanal Desa {DESA} {WILAYAH}, terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.",
                KetentuanPengangkatanLampiran = "Mengangkat nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini sebagai Kader Posyandu dan Kader Pokjanal Desa {DESA} Kecamatan {KECAMATAN} {IDENTITAS_WILAYAH}.",
                KetentuanKetiga = "Kader sebagaimana diktum {PENANDA} melaksanakan tugas pokok dan fungsinya sesuai dengan peraturan perundang-undangan yang berlaku."
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
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 110 Tahun 2016 tentang Badan Permusyawaratan Desa."
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
                // bukan pengangkatan — dan seperti contoh SK desa, satu SK
                // untuk seluruh kader dengan lampiran daftar nama.
                Bersama = true,
                JudulPengangkatan = "PENGANGKATAN KADER PEMBANGUNAN MANUSIA (KPM) DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN KADER PEMBANGUNAN MANUSIA (KPM) DESA {DESA}",
                JudulPenetapan = "PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM) KONVERGENSI PENCEGAHAN STUNTING DESA {DESA}",
                Memperhatikan = new[]
                {
                    "Bahwa nama-nama yang akan ditetapkan tersebut merupakan hasil musyawarah desa."
                },
                // Butir meniru contoh SK KPM desa: pelaksanaan Konvergensi
                // Pencegahan Stunting, lalu penetapan SK Kepala Desa (dua butir
                // huruf a dan b, sama seperti contohnya).
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pelaksanaan Konvergensi Pencegahan Stunting di Desa {DESA} Kecamatan {KECAMATAN} {IDENTITAS_WILAYAH} perlu ditetapkan Kader Pembangunan Manusia (KPM);",
                    "Bahwa berdasarkan pertimbangan sebagaimana dimaksud pada huruf a di atas, maka dipandang perlu menetapkan Surat Keputusan Kepala Desa tentang Penetapan Kader Pembangunan Manusia (KPM) agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Menteri Dalam Negeri Nomor 114 Tahun 2014 tentang Pedoman Pembangunan Desa;",
                    "Peraturan Presiden Nomor 83 Tahun 2017 tentang Kebijakan Strategis Pangan dan Gizi;",
                    "Peraturan Presiden Nomor 72 Tahun 2021 tentang Rencana Aksi Nasional Percepatan Penurunan Stunting;",
                    "{PERKEPALA_TANPA_NAMA} tentang Percepatan Penurunan Stunting;",
                    "{PERDA} tentang Desa;",
                    "Hasil Musyawarah Desa Penetapan Kader Pembangunan Manusia (KPM) Desa {DESA};",
                    "Peraturan Desa {DESA} tentang Kader Pembangunan Manusia."
                },
                // Diktum mengikuti contoh SK KPM: kader “diangkat” (bukan
                // “ditetapkan”) pada diktum, tanpa klausul masa jabatan.
                KetentuanPenetapan = "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai Kader Pembangunan Manusia (KPM) Desa {DESA} {WILAYAH}.",
                KetentuanPenetapanLampiran = "Mengangkat nama-nama sebagaimana tercantum dalam Lampiran Surat Keputusan ini sebagai Kader Pembangunan Manusia (KPM) Desa {DESA}."
            },
            ["KARANG TARUNA"] = new SkKelompokTemplate
            {
                Kelompok = "KARANG TARUNA",
                JudulPengangkatan = "PENGANGKATAN {JABATAN} DESA {DESA}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN} DESA {DESA}",
                Menimbang = new[]
                {
                    "Bahwa dalam rangka pembinaan dan pemberdayaan pemuda di Desa {DESA} perlu ditetapkan pengurus Karang Taruna Desa {DESA}.",
                    "Bahwa saudara/i {NAMA} dipilih melalui musyawarah pemuda untuk mengisi jabatan {JABATAN} dan memenuhi syarat.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = new[]
                {
                    "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                    "Peraturan Pemerintah Nomor 87 Tahun 2007 tentang Pemberdayaan Karang Taruna;",
                    "Peraturan Desa {DESA} tentang Organisasi Karang Taruna."
                },
                KetentuanKetiga =
                    "Dalam melaksanakan tugas sebagai {JABATAN}, wajib menyelenggarakan kegiatan kepemudaan sesuai rencana kerja "
                    + "Karang Taruna Desa {DESA} dan melaporkan pelaksanaannya kepada Kepala Desa."
            }
        };

        /// <summary>
        /// Dasar hukum bersama seluruh perangkat desa, disusun dari aturan yang
        /// berlaku: UU Desa, UU Pemerintahan Daerah beserta perubahannya, PP Desa
        /// beserta perubahannya, Permendagri pengangkatan/pemberhentian perangkat
        /// desa, SOTK, lalu Perda/Perdes SOTK setempat.
        /// </summary>
        private static IReadOnlyList<string> MengingatPerangkatDesa() => new[]
        {
            "Undang-Undang Republik Indonesia Nomor 6 Tahun 2014 tentang Desa;",
            "Undang-Undang Nomor 23 Tahun 2014 tentang Pemerintahan Daerah sebagaimana telah beberapa kali diubah, terakhir dengan Undang-Undang Nomor 9 Tahun 2015;",
            // PP 43/2014 (beserta perubahannya PP 47/2015 & PP 11/2019) dicabut
            // dan diganti dengan PP 16 Tahun 2026 (peraturan pelaksana UU Desa
            // terbaru), jadi dasar hukum SK selalu memakai PP yang masih berlaku.
            "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
            "Peraturan Menteri Dalam Negeri Nomor 83 Tahun 2015 tentang Pengangkatan dan Pemberhentian Perangkat Desa sebagaimana diubah dengan Peraturan Menteri Dalam Negeri Nomor 67 Tahun 2017;",
            "Peraturan Menteri Dalam Negeri Nomor 84 Tahun 2015 tentang Susunan Organisasi dan Tata Kerja Pemerintah Desa;",
            "{PERDA} tentang Susunan Organisasi dan Tata Kerja Pemerintah Desa dan Perangkat Desa;",
            "Peraturan Desa {DESA} tentang Susunan Organisasi dan Tata Kerja Pemerintah Desa dan Perangkat Desa."
        };

        /// <summary>
        /// Template SK untuk satu jabatan perangkat desa: tugas pokok jabatan itu
        /// dipakai menyusun butir Menimbang, diktum KETIGA, dan kalimat keputusan,
        /// sehingga SK tiap jabatan tidak lagi seragam.
        /// </summary>
        private static SkKelompokTemplate TemplateJabatan(string tugas, string jabatanTampil)
        {
            return new SkKelompokTemplate
            {
                Kelompok = "PERANGKAT DESA",
                JudulPengangkatan = "PENGANGKATAN {JABATAN}",
                JudulPemberhentian = "PEMBERHENTIAN {JABATAN}",
                Menimbang = new[]
                {
                    "Bahwa untuk kelancaran penyelenggaraan pemerintahan Desa {DESA} perlu mengangkat {JABATAN} yang bertugas " + tugas + ".",
                    "Bahwa saudara/i {NAMA} merupakan warga Desa {DESA} yang memenuhi persyaratan dan telah melalui penjaringan serta penyaringan perangkat desa.",
                    "Bahwa berdasarkan pertimbangan sebagaimana dimaksud, perlu menetapkan pengangkatan tersebut dengan Surat Keputusan Kepala Desa."
                },
                MenimbangPemberhentian = new[]
                {
                    "Bahwa saudara/i {NAMA} saat ini menjabat sebagai {JABATAN} di Desa {DESA} dengan tugas " + tugas + ".",
                    "Bahwa pemberhentian tersebut telah dikonsultasikan kepada Camat sebagaimana diatur dalam Peraturan Menteri Dalam Negeri Nomor 83 Tahun 2015 sebagaimana diubah dengan Peraturan Menteri Dalam Negeri Nomor 67 Tahun 2017.",
                    "Bahwa hal tersebut perlu ditetapkan dengan Surat Keputusan Kepala Desa agar memiliki kepastian hukum."
                },
                Mengingat = MengingatPerangkatDesa(),
                // Contoh SK perangkat desa memperhatikan berita acara penjaringan
                // dan rekomendasi Camat; pemberhentian memakai surat pengunduran
                // diri beserta rekomendasi Camat.
                Memperhatikan = new[]
                {
                    "Berita Acara Hasil Penjaringan dan Penyaringan Calon Perangkat Desa {DESA} Nomor .......... tanggal .......... tentang Penetapan Calon Perangkat Desa;",
                    "Surat Rekomendasi Camat {KECAMATAN} tanggal .......... Nomor .......... perihal Permohonan Rekomendasi Pengangkatan Perangkat Desa."
                },
                MemperhatikanPemberhentian = new[]
                {
                    "Surat permohonan/pengunduran diri saudara/i {NAMA} tanggal ..........;",
                    "Surat Rekomendasi Camat {KECAMATAN} tanggal .......... Nomor .......... perihal Pemberhentian Perangkat Desa."
                },
                KetentuanPengangkatan =
                    "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai {JABATAN} pada Pemerintahan Desa {DESA} {WILAYAH}, "
                    + "terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.",
                KetentuanPemberhentian =
                    "Memberhentikan saudara/i {NAMA}, NIK. {NIK}, dari jabatan {JABATAN} pada Pemerintahan Desa {DESA} {WILAYAH} {ALASAN}, "
                    + "terhitung sejak tanggal {TANGGAL}.",
                KetentuanKetiga =
                    "Dalam melaksanakan tugas sebagai " + jabatanTampil + ", wajib berpedoman pada Susunan Organisasi dan Tata Kerja "
                    + "Pemerintah Desa {DESA} dan melaporkan pelaksanaan tugasnya kepada Kepala Desa."
            };
        }

        /// <summary>
        /// Template SK per jabatan resmi perangkat desa (Kepala Desa sampai
        /// Kadus). Jabatan yang belum ada di sini memakai template kelompoknya,
        /// sehingga hanya jabatan perangkat desa yang mendapat contoh SK
        /// khusus; kelompok lain (RT/RW, Linmas, Posyandu, dst.) tetap memakai
        /// template kelompoknya seperti semula.
        /// </summary>
        private static readonly Dictionary<string, SkKelompokTemplate> _perJabatan =
            new(StringComparer.Ordinal)
            {
                [JabatanPerangkat.KepalaDesa] = new SkKelompokTemplate
                {
                    // SK Kepala Desa diterbitkan Bupati: aplikasi menyimpan
                    // berkasnya, tetapi template ini tetap disediakan supaya
                    // bentuk dokumennya seragam dengan jabatan lain. Butirnya
                    // memakai jalur pemilihan, bukan penjaringan perangkat desa.
                    Kelompok = "PERANGKAT DESA",
                    Sumber = SumberSkPerangkat.Bupati,
                    JudulPengangkatan = "PENGANGKATAN {JABATAN}",
                    JudulPemberhentian = "PEMBERHENTIAN {JABATAN}",
                    Menimbang = new[]
                    {
                        "Bahwa berdasarkan hasil pemilihan Kepala Desa {DESA} beserta berita acara pemilihan yang telah ditetapkan, saudara/i {NAMA} terpilih sebagai Kepala Desa {DESA}.",
                        "Bahwa hasil pemilihan tersebut telah dilaporkan kepada {KEPALA_DAERAH} {KABUPATEN} melalui Camat {KECAMATAN} sesuai ketentuan peraturan perundang-undangan.",
                        "Bahwa berdasarkan pertimbangan sebagaimana dimaksud, perlu mengangkat saudara/i {NAMA} sebagai Kepala Desa {DESA} dengan {SK_KEPALA}."
                    },
                    MenimbangPemberhentian = new[]
                    {
                        "Bahwa masa jabatan saudara/i {NAMA} sebagai Kepala Desa {DESA} telah berakhir sesuai ketentuan peraturan perundang-undangan.",
                        "Bahwa berakhirnya masa jabatan tersebut telah dilaporkan kepada {KEPALA_DAERAH} {KABUPATEN} melalui Camat {KECAMATAN}.",
                        "Bahwa hal tersebut perlu ditetapkan dengan {SK_KEPALA} agar memiliki kepastian hukum."
                    },
                    Mengingat = new[]
                    {
                        "Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                        "Peraturan Pemerintah Nomor 16 Tahun 2026 tentang Peraturan Pelaksanaan Undang-Undang Nomor 6 Tahun 2014 tentang Desa;",
                        "Peraturan Menteri Dalam Negeri Nomor 112 Tahun 2014 tentang Pemilihan Kepala Desa;",
                        "{PERDA} tentang Pemilihan Kepala Desa."
                    },
                    Memperhatikan = new[]
                    {
                        "Berita Acara Hasil Pemilihan Kepala Desa {DESA} Nomor .......... tanggal ..........;",
                        "Surat Rekomendasi/Laporan Camat {KECAMATAN} tanggal .......... Nomor .......... perihal Hasil Pemilihan Kepala Desa."
                    },
                    MemperhatikanPemberhentian = new[]
                    {
                        "Laporan berakhirnya masa jabatan Kepala Desa {DESA} tanggal ..........;",
                        "Surat Rekomendasi Camat {KECAMATAN} tanggal .......... Nomor .......... perihal Pemberhentian Kepala Desa."
                    },
                    KetentuanPengangkatan =
                        "Mengangkat saudara/i {NAMA}, NIK. {NIK}, sebagai Kepala Desa {DESA} {WILAYAH} untuk masa jabatan "
                        + "terhitung sejak tanggal {MULAI} sampai dengan {SELESAI}.",
                    KetentuanPemberhentian =
                        "Memberhentikan saudara/i {NAMA}, NIK. {NIK}, dari jabatan Kepala Desa {DESA} {WILAYAH} {ALASAN}, "
                        + "terhitung sejak tanggal {TANGGAL}.",
                    KetentuanKetiga =
                        "Dalam melaksanakan tugas sebagai Kepala Desa, wajib berpedoman pada ketentuan peraturan perundang-undangan "
                        + "dan menyampaikan laporan penyelenggaraan pemerintahan desa kepada {KEPALA_DAERAH} {KABUPATEN} melalui Camat {KECAMATAN}."
                },
                [JabatanPerangkat.SekretarisDesa] = TemplateJabatan(
                    "membantu Kepala Desa dalam penyusunan kebijakan dan koordinasi administrasi pemerintahan desa serta memberikan pelayanan administratif kepada masyarakat",
                    "Sekretaris Desa"),
                [JabatanPerangkat.KaurKeuangan] = TemplateJabatan(
                    "melaksanakan urusan keuangan desa, menyusun rencana anggaran, serta melakukan penatausahaan dan pelaporan keuangan desa",
                    "Kaur Keuangan"),
                [JabatanPerangkat.KaurPerencanaan] = TemplateJabatan(
                    "melaksanakan urusan perencanaan pembangunan desa, menyusun RPJMDes dan RKPDes, serta mengelola data dan evaluasi pembangunan desa",
                    "Kaur Perencanaan"),
                [JabatanPerangkat.KaurTataUsaha] = TemplateJabatan(
                    "melaksanakan urusan tata usaha dan umum, meliputi surat-menyurat, kearsipan, perlengkapan kantor, serta administrasi umum dan kepegawaian desa",
                    "Kaur Umum dan Tata Usaha"),
                [JabatanPerangkat.KasiPemerintahan] = TemplateJabatan(
                    "melaksanakan urusan pemerintahan desa, meliputi ketenteraman dan ketertiban umum, administrasi kependudukan, serta pembinaan politik dan pemerintahan",
                    "Kasi Pemerintahan"),
                [JabatanPerangkat.KasiPelayanan] = TemplateJabatan(
                    "melaksanakan urusan pelayanan kepada masyarakat serta sosialisasi dan penyuluhan program pemerintah desa",
                    "Kasi Pelayanan"),
                [JabatanPerangkat.KasiKesejahteraan] = TemplateJabatan(
                    "melaksanakan urusan pembangunan kesejahteraan masyarakat di bidang ekonomi, sosial, agama, pendidikan, kesehatan, dan keluarga berencana",
                    "Kasi Kesejahteraan"),
                [JabatanPerangkat.Kadus] = TemplateJabatan(
                    "menyelenggarakan pemerintahan, pembangunan, pembinaan kemasyarakatan, dan pemberdayaan masyarakat di wilayah dusun yang dipimpinnya",
                    "Kepala Dusun"),
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

        /// <summary>
        /// Template khusus satu jabatan; null bila jabatan itu belum punya
        /// template sendiri (mis. jabatan lama di luar daftar resmi).
        /// </summary>
        public static SkKelompokTemplate? CariJabatan(string? jabatan) =>
            string.IsNullOrWhiteSpace(jabatan) ? null
            : _perJabatan.TryGetValue(JabatanPerangkat.Normalisasi(jabatan), out var template) ? template : null;

        /// <summary>
        /// Template yang dipakai satu isian SK: template per jabatan bila ada
        /// (jabatan perangkat desa), selain itu template kelompok (RT/RW,
        /// Linmas, Posyandu, dst.). Null berarti kelompoknya memang tanpa
        /// template bawaan.
        /// </summary>
        public static SkKelompokTemplate? Untuk(string? jabatan, string? kelompok) =>
            CariJabatan(jabatan) ?? Cari(kelompok);

        /// <summary>True bila jabatan/kelompok ini punya template SK yang bisa dipakai.</summary>
        public static bool AdaUntuk(string? jabatan, string? kelompok) => Untuk(jabatan, kelompok) != null;

        /// <summary>Jabatan resmi yang sudah punya template SK sendiri (untuk panel & pemeriksaan).</summary>
        public static IReadOnlyList<string> JabatanBerTemplate =>
            JabatanPerangkat.Resmi.Where(j => _perJabatan.ContainsKey(j)).ToList();
    }
}
