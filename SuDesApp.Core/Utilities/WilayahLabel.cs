namespace SuDesApp.Utilities
{
    /// <summary>
    /// Label teks yang menyesuaikan jenis wilayah kabupaten/kota. Dipakai pada
    /// naskah SK dan Perdes supaya konsisten: "Bupati" vs "Wali Kota",
    /// "Peraturan Daerah Kabupaten" vs "Peraturan Daerah Kota", dll.
    /// </summary>
    public static class WilayahLabel
    {
        /// <summary>
        /// Jenis wilayah: "Kabupaten" atau "Kota". Bawaan "Kabupaten" untuk data lama.
        /// </summary>
        public static string Jenis(string? jenisKabupaten) =>
            string.Equals(jenisKabupaten?.Trim(), "Kota", StringComparison.OrdinalIgnoreCase)
                ? "Kota"
                : "Kabupaten";

        /// <summary>
        /// Gelar kepala daerah: "Bupati" untuk kabupaten, "Wali Kota" untuk kota.
        /// </summary>
        public static string Kepala(string? jenisKabupaten) =>
            Jenis(jenisKabupaten) == "Kota" ? "Wali Kota" : "Bupati";

        /// <summary>
        /// "Peraturan Daerah Kabupaten {nama}" atau "Peraturan Daerah Kota {nama}".
        /// </summary>
        public static string PeraturanDaerah(string? jenisKabupaten, string namaKabupaten) =>
            $"Peraturan Daerah {Jenis(jenisKabupaten)} {namaKabupaten}";

        /// <summary>
        /// "Peraturan Bupati {nama}" atau "Peraturan Wali Kota {nama}".
        /// </summary>
        public static string PeraturanKepala(string? jenisKabupaten, string namaKabupaten) =>
            $"Peraturan {Kepala(jenisKabupaten)} {namaKabupaten}";

        /// <summary>
        /// "Peraturan Bupati" atau "Peraturan Wali Kota" (tanpa nama).
        /// </summary>
        public static string PeraturanKepalaTanpaNama(string? jenisKabupaten) =>
            $"Peraturan {Kepala(jenisKabupaten)}";

        /// <summary>
        /// "Surat Keputusan Bupati" atau "Surat Keputusan Wali Kota".
        /// </summary>
        public static string SkKepala(string? jenisKabupaten) =>
            $"Surat Keputusan {Kepala(jenisKabupaten)}";

        /// <summary>
        /// "Kabupaten {nama}" atau "Kota {nama}" untuk identitas wilayah.
        /// </summary>
        public static string IdentitasWilayah(string? jenisKabupaten, string namaKabupaten) =>
            $"{Jenis(jenisKabupaten)} {namaKabupaten}";

        /// <summary>
        /// "dilaporkan kepada Bupati" atau "dilaporkan kepada Wali Kota".
        /// </summary>
        public static string LaporanKepala(string? jenisKabupaten) =>
            $"kepada {Kepala(jenisKabupaten)}";
    }
}
