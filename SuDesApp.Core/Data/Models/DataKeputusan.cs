namespace SuDesApp.Data.Models
{
    public class DataKeputusan
    {

        public int IdBarisExcel { get; set; }

        public string JenisKeputusan { get; set; } = string.Empty;

        public string Nomor { get; set; } = string.Empty;

        public DateTime Tanggal { get; set; }

        public string Tentang { get; set; } = string.Empty;

        public string Keterangan { get; set; } = string.Empty;

        /// <summary>Nama file Word yang disalin ke folder ArsipKeputusanFiles pada saat import.</summary>
        public string? FileWord { get; set; }
    }
}
