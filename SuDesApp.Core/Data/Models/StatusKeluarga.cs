using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Kedudukan warga dalam Kartu Keluarganya. Dipakai kolom
    /// <c>Warga.StatusKeluarga</c>, form Data Warga, dan penghitungan Kepala
    /// Keluarga pada rekapitulasi/laporan penduduk: yang dihitung Kepala
    /// Keluarga hanyalah baris yang kedudukannya memang Kepala Keluarga —
    /// bukan sekadar pemilik nomor KK pertama.
    /// </summary>
    public static class StatusKeluargaTipe
    {
        public const string KepalaKeluarga = "Kepala Keluarga";
        public const string Istri = "Istri";
        public const string Suami = "Suami";
        public const string Anak = "Anak";
        public const string Cucu = "Cucu";
        public const string FamilyLain = "Family Lain";

        /// <summary>Pilihan dropdown form, urut sesuai kedudukan baku KK.</summary>
        public static readonly IReadOnlyList<string> Semua = new[]
        {
            KepalaKeluarga, Istri, Suami, Anak, Cucu, FamilyLain
        };

        /// <summary>Benar bila kedudukan ini dihitung sebagai Kepala Keluarga.</summary>
        public static bool AdalahKepalaKeluarga(string? status) =>
            string.Equals(Normalisasi(status), KepalaKeluarga, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Rapikan isian: spasi berlebih dibuang, kapitalisasi kalimat
        /// ("kepala keluarga" → "Kepala Keluarga"), sinonim dipetakan
        /// ("KEPALA" → "Kepala Keluarga", "ISTRI" → "Istri").
        /// </summary>
        public static string Normalisasi(string? nilai)
        {
            string teks = string.Join(" ", (nilai ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (teks.Length == 0) return string.Empty;

            foreach (var pilihan in Semua)
            {
                if (string.Equals(teks, pilihan, StringComparison.OrdinalIgnoreCase))
                {
                    return pilihan;
                }
            }

            // Singkatan/sinonim umum.
            string kecil = teks.ToLowerInvariant();
            if (kecil is "kepala" or "kk" or "kades" or "kepalakeluarga") return KepalaKeluarga;
            if (kecil is "istri" or "wanita" or "bini") return Istri;
            if (kecil is "suami" or "laki-laki" or "pria") return Suami;
            if (kecil is "anak" or "putra" or "putri") return Anak;
            if (kecil is "cucu" or "cicit") return Cucu;
            if (kecil is "family lain" or "famili lain" or "keluarga lain" or "famili" or "fam") return FamilyLain;

            // Nilai asing dibiarkan apa adanya supaya data tidak diam-diam berubah.
            return teks;
        }
    }
}
