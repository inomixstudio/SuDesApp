using System.ComponentModel.DataAnnotations;

namespace SuDesApp.Data.Models
{
    public class RekeningKoranData
    {
        [Required]
        public string NomorSurat { get; set; }

        [Required]
        public string Perihal { get; set; } = "Permohonan Print Out Rekening Koran";

        [Required]
        public DateTime TanggalSurat { get; set; }

        [Required]
        public string NamaPejabat { get; set; }

        [Required]
        public string Jabatan { get; set; }

        [Required]
        public string AlamatPejabat { get; set; }

        [Required]
        public string NamaPemegangRekening { get; set; }

        [Required]
        public string NomorRekening { get; set; }

        [Required]
        public string PeriodeRekening { get; set; }

        public DesaData Desa { get; set; }

        [Required]
        public string Bank { get; set; }

        [Required]
        public string KCP { get; set; }
    }
}
