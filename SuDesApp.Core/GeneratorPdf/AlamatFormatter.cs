using System.Collections.Generic;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    // Membentuk alamat lengkap penduduk dari komponen alamat (Dusun/Jalan, Desa, Kecamatan, Kabupaten).
    // Komponen ini yang selalu terisi pada input surat; kolom Warga.Alamat menyimpan alamat lama yang kosong.
    public static class AlamatFormatter
    {
        public static string Format(WargaData? warga, string fallback = "[Alamat Warga]")
        {
            if (warga == null) return fallback;
            return Format(warga.Dusun, warga.Desa, warga.Kecamatan, warga.Kabupaten, fallback);
        }

        public static string Format(string? alamatDusunJalan, string? desa, string? kecamatan, string? kabupaten, string fallback = "[Alamat Warga]")
        {
            // Baris 1: Dusun/Jalan + Desa — Baris 2: Kecamatan + Kabupaten.
            // Pemisah antar komponen SPASI (tanpa koma/tanda baca lain).
            var line1 = new List<string>();
            AddPart(line1, NormalizeDusunJalan(alamatDusunJalan));
            AddPart(line1, desa, "Desa");

            var line2 = new List<string>();
            AddPart(line2, kecamatan, "Kecamatan");
            AddPart(line2, kabupaten, "Kabupaten");

            if (line1.Count == 0 && line2.Count == 0) return fallback;
            if (line2.Count == 0) return string.Join(" ", line1);
            if (line1.Count == 0) return string.Join(" ", line2);

            return string.Join(" ", line1) + "\n" + string.Join(" ", line2);
        }

        private static void AddPart(List<string> parts, string? value, string? prefix = null)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length == 0) return;
            parts.Add(prefix != null ? $"{prefix} {v}" : v);
        }

        /// <summary>
        /// Nilai Dusun/Jalan dibiarkan apa adanya bila sudah memuat label sendiri
        /// ("Dusun Krajan", "Jl. Merdeka"); bila belum, diawali "Dusun " agar
        /// tampil konsisten: Dusun X Desa Y …
        /// </summary>
        private static string NormalizeDusunJalan(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length == 0) return string.Empty;

            var lower = v.ToLowerInvariant();
            bool sudahBerlabel = lower.StartsWith("dusun ") ||
                                 lower.StartsWith("jl.") || lower.StartsWith("jl ") ||
                                 lower.StartsWith("jalan ");
            return sudahBerlabel ? v : "Dusun " + v;
        }
    }
}