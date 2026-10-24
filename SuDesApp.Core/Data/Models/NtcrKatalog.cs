using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu blanko NTCR dalam katalog.
    /// </summary>
    public sealed class NtcrBlanko
    {
        /// <summary>Nama jenis surat di database (SuratConstants.NTCR_Nx).</summary>
        public string NamaJenis { get; init; } = string.Empty;

        /// <summary>Kode blanko resmi: "N1" … "N6".</summary>
        public string Kode { get; init; } = string.Empty;

        /// <summary>Judul blanko yang tercetak di PDF.</summary>
        public string NamaFormulir { get; init; } = string.Empty;

        /// <summary>Nama singkat blanko untuk menu dan daftar pilihan.</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>Ringkasan kegunaan blanko (satu baris).</summary>
        public string Keterangan { get; init; } = string.Empty;

        /// <summary>Isi kolom "keperluan" surat di register.</summary>
        public string Keperluan { get; init; } = string.Empty;

        /// <summary>
        /// Nomor lampiran Kepdirjen Bimas Islam No. 473 Tahun 2020 yang tercetak di
        /// kepala blanko (mis. "IV" → "LAMPIRAN IV"). Kosong untuk surat desa biasa
        /// seperti N8 (numpang nikah) yang tidak memakai blok Kepdirjen.
        /// </summary>
        public string Lampiran { get; init; } = string.Empty;

        /// <summary>
        /// Apakah blanko mencetak blok kepala "LAMPIRAN … / KEPUTUSAN DIREKTUR JENDERAL …"
        /// dan ikut dalam paket pernikahan. Benar untuk blanko Kepdirjen N1–N6; salah
        /// untuk N8 yang berbentuk surat keterangan desa biasa.
        /// </summary>
        public bool BlokKepdirjen { get; init; } = true;

        /// <summary>Judul lengkap untuk daftar pilihan, mis. "N1 — Surat Pengantar Nikah".</summary>
        public string Judul => $"{Kode} — {Label}";
    }

    /// <summary>
    /// Katalog blanko persyaratan pernikahan (NTCR): blanko Kepdirjen N1–N6 sesuai
    /// Keputusan Direktur Jenderal Bimbingan Masyarakat Islam Nomor 473 Tahun 2020,
    /// ditambah N8 (surat keterangan numpang nikah) yang diterbitkan kantor desa
    /// sendiri. Menjadi satu sumber kebenaran untuk generator PDF, form input, dan
    /// alur paket pernikahan (sekali isi data satu pasangan → cetak N1 sampai N6
    /// dalam satu berkas).
    ///
    /// Model N7 (penolakan kehendak nikah/rujuk) tidak ada di sini karena diterbitkan
    /// KUA, bukan kantor desa.
    /// </summary>
    public static class NtcrKatalog
    {
        private static readonly NtcrBlanko[] Daftar =
        {
            new()
            {
                NamaJenis = SuratConstants.NTCR_N1,
                Kode = "N1",
                Lampiran = "IV",
                NamaFormulir = "FORMULIR PENGANTAR NIKAH",
                Label = "Surat Pengantar Nikah",
                Keterangan = "Diterbitkan & ditandatangani Kepala Desa/Lurah",
                Keperluan = "Surat Pengantar Nikah (N1)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N2,
                Kode = "N2",
                Lampiran = "VI",
                NamaFormulir = "FORMULIR PERMOHONAN KEHENDAK NIKAH",
                Label = "Permohonan Kehendak Nikah",
                Keterangan = "Diajukan calon pengantin kepada KUA/PPN LN",
                Keperluan = "Permohonan Kehendak Nikah (N2)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N3,
                Kode = "N3",
                Lampiran = "VII",
                NamaFormulir = "FORMULIR PERMOHONAN PENCATATAN ISBAT",
                Label = "Permohonan Pencatatan Isbat",
                Keterangan = "Diajukan kepada KUA untuk pencatatan isbat",
                Keperluan = "Permohonan Pencatatan Isbat (N3)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N4,
                Kode = "N4",
                Lampiran = "VIII",
                NamaFormulir = "SURAT PERSETUJUAN PENGANTIN",
                Label = "Persetujuan Calon Pengantin",
                Keterangan = "Ditandatangani kedua calon pengantin",
                Keperluan = "Persetujuan Calon Pengantin (N4)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N5,
                Kode = "N5",
                Lampiran = "IX",
                NamaFormulir = "SURAT IZIN ORANG TUA",
                Label = "Surat Izin Orang Tua",
                Keterangan = "Ditandatangani ayah & ibu/wali calon pengantin",
                Keperluan = "Surat Izin Orang Tua (N5)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N6,
                Kode = "N6",
                Lampiran = "X",
                NamaFormulir = "SURAT KETERANGAN KEMATIAN SUAMI/ISTRI",
                Label = "Keterangan Kematian Suami/Istri",
                Keterangan = "Diterbitkan Kepala Desa/Lurah untuk pernikahan/rujuk",
                Keperluan = "Surat Keterangan Kematian Suami/Istri (N6)"
            },
            new()
            {
                NamaJenis = SuratConstants.NTCR_N8,
                Kode = "N8",
                NamaFormulir = "SURAT KETERANGAN NUMPANG NIKAH",
                Label = "Surat Numpang Nikah (Numpang Kawin)",
                Keterangan = "Keterangan desa bahwa warga akan numpang nikah di wilayah lain",
                Keperluan = "Surat Keterangan Numpang Nikah (N8)",
                // Bukan blanko Kepdirjen: surat keterangan desa biasa, sudah memakai
                // kop surat desa dan tidak ikut paket blanko N1–N6.
                BlokKepdirjen = false
            }
        };

        /// <summary>Seluruh blanko NTCR yang dikenal aplikasi, urut N1 → N8.</summary>
        public static IReadOnlyList<NtcrBlanko> Semua => Daftar;

        /// <summary>Blanko yang ikut satu paket pernikahan (blanko Kepdirjen N1–N6).</summary>
        public static IReadOnlyList<NtcrBlanko> UntukPaket { get; } =
            Daftar.Where(b => b.BlokKepdirjen).ToList();

        /// <summary>Blanko NTCR yang biasa dipakai satu paket pernikahan (N1–N5).</summary>
        public static IReadOnlyList<string> PaketStandar { get; } = new[]
        {
            SuratConstants.NTCR_N1,
            SuratConstants.NTCR_N2,
            SuratConstants.NTCR_N3,
            SuratConstants.NTCR_N4,
            SuratConstants.NTCR_N5
        };

        /// <summary>Cari blanko berdasarkan nama jenis surat (tidak peka huruf besar/kecil).</summary>
        public static NtcrBlanko? Cari(string? namaJenis) =>
            string.IsNullOrWhiteSpace(namaJenis)
                ? null
                : Daftar.FirstOrDefault(b => string.Equals(b.NamaJenis, namaJenis.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Apakah blanko memakai blok kepala Kepdirjen ("LAMPIRAN …") — blanko N1–N6
        /// ya, N8 tidak.
        /// </summary>
        public static bool PakaiBlokKepdirjen(string? namaJenis) => Cari(namaJenis)?.BlokKepdirjen ?? false;

        /// <summary>Kode blanko ("N1"…"N8"); kosong bila tidak dikenali.</summary>
        public static string Kode(string? namaJenis) => Cari(namaJenis)?.Kode ?? string.Empty;

        /// <summary>Judul blanko yang tercetak; kosong bila tidak dikenali.</summary>
        public static string NamaFormulir(string? namaJenis) => Cari(namaJenis)?.NamaFormulir ?? string.Empty;

        /// <summary>Nomor lampiran Kepdirjen (mis. "IV") untuk blanko tertentu.</summary>
        public static string Lampiran(string? namaJenis) => Cari(namaJenis)?.Lampiran ?? string.Empty;

        /// <summary>Nama singkat blanko; memakai nama jenis apa adanya bila tidak dikenali.</summary>
        public static string Label(string? namaJenis) => Cari(namaJenis)?.Label ?? namaJenis ?? string.Empty;

        /// <summary>Judul lengkap "N1 — Surat Pengantar Nikah".</summary>
        public static string Judul(string? namaJenis) =>
            Cari(namaJenis)?.Judul ?? namaJenis ?? string.Empty;

        /// <summary>Isi kolom keperluan surat; memakai nama jenis apa adanya bila tidak dikenali.</summary>
        public static string Keperluan(string? namaJenis) => Cari(namaJenis)?.Keperluan ?? namaJenis ?? string.Empty;

        /// <summary>Urutan blanko N1..N8 (angka besar bila tidak dikenali).</summary>
        public static int Urutan(string? namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis)) return int.MaxValue;

            for (int i = 0; i < Daftar.Length; i++)
            {
                if (string.Equals(Daftar[i].NamaJenis, namaJenis.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return int.MaxValue;
        }

        /// <summary>Urutkan nama jenis surat NTCR sesuai urutan blanko N1 → N8.</summary>
        public static IReadOnlyList<string> Urutkan(IEnumerable<string>? namaJenis)
        {
            if (namaJenis == null) return Array.Empty<string>();

            return namaJenis
                .Where(j => !string.IsNullOrWhiteSpace(j))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Urutan)
                .ThenBy(j => j, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
