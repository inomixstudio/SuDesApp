namespace SuDesApp.Data.Models
{
    public class SuratKeluarMasukData
    {
        public int IdBarisExcel { get; set; }
        public string JenisSurat { get; set; } = string.Empty;
        public string NomorSurat { get; set; } = string.Empty;
        public DateTime TanggalSurat { get; set; }
        public DateTime? TanggalDiterimaDikirim { get; set; }
        public string AsalTujuan { get; set; } = string.Empty;
        public string Perihal { get; set; } = string.Empty;
        public string IsiRingkas { get; set; } = string.Empty;
        public string Keterangan { get; set; } = string.Empty;

        /// <summary>Nama berkas lampiran (PDF/gambar) yang disalin ke folder ArsipSuratFiles.</summary>
        public string? FileLampiran { get; set; }
    }
}
