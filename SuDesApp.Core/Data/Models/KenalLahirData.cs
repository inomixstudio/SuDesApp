namespace SuDesApp.Data.Models
{
    public class KenalLahirData
    {
        public WargaData Ayah { get; set; }
        public WargaData Ibu { get; set; }
        public AnakData Anak { get; set; }
        public string? NamaAnak { get; set; }
        public DateTime? TanggalLahirAnak { get; set; }
        public string? TempatLahirAnak { get; set; }
        public int AnakKe { get; set; }
        public string? LahirDi { get; set; }
        public string? AlamatLengkapAnak { get; set; }

        // Metode validasi untuk KenalLahirData
        public bool IsValid()
        {
            // UI mengisi Ayah/Ibu sebagai WargaData dan data anak via field denormalized
            // (NamaAnak/TanggalLahirAnak/TempatLahirAnak), bukan lewat objek graph Anak.
            var hasAyah = Ayah != null && !string.IsNullOrWhiteSpace(Ayah.NIK);
            var hasIbu = Ibu != null && !string.IsNullOrWhiteSpace(Ibu.NIK);

            var namaAnak = !string.IsNullOrWhiteSpace(NamaAnak)
                ? NamaAnak
                : Anak?.NamaAnak;

            return (hasAyah || hasIbu) && !string.IsNullOrWhiteSpace(namaAnak);
        }
    }

    public class AnakData : WargaData
    {
        public string? NamaAnak { get => Nama; set => Nama = value; }
        public int AnakKe { get; set; }
        public string LahirDi { get; set; } // Misal: "Rumah" atau "Rumah Sakit"
    }
}
