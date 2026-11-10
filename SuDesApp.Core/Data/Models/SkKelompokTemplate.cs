using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{

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

}
