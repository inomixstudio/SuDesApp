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
    }
}
