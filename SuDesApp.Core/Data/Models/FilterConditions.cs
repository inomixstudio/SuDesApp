namespace SuDesApp.Data.Models
{
    public class FilterConditions
    {
        public string ?JenisSurat { get; set; }
        public List<string> ?ExcludeJenisNames { get; set; }
        public string ?Tahun { get; set; }
        public string ?SearchText { get; set; }
        public string? Status { get; set; }
        public DateTime? TanggalMulai { get; set; }
        public DateTime? TanggalSelesai { get; set; }

        /// <summary>
        /// Salinan filter tanpa syarat status — dipakai saat menghitung jumlah per
        /// status (satu query GROUP BY) agar syarat status tidak ikut menyaring.
        /// </summary>
        public FilterConditions CloneTanpaStatus() => new()
        {
            JenisSurat = JenisSurat,
            ExcludeJenisNames = ExcludeJenisNames,
            Tahun = Tahun,
            SearchText = SearchText,
            TanggalMulai = TanggalMulai,
            TanggalSelesai = TanggalSelesai
        };
    }
}
