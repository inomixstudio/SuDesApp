using System.Text.Json.Serialization;

namespace SuDesApp.Data.Models
{
    public class AhliWarisData
    {
        [JsonPropertyName("namaPasangan")]
        public string NamaPasangan { get; set; } = string.Empty;

        [JsonPropertyName("tempatLahirPasangan")]
        public string TempatLahirPasangan { get; set; } = string.Empty;

        [JsonPropertyName("tanggalLahirPasangan")]
        public DateTime? TanggalLahirPasangan { get; set; }

        [JsonPropertyName("jenisKelaminPasangan")]
        public string JenisKelaminPasangan { get; set; } = string.Empty; // "Laki-laki" atau "Perempuan"

        [JsonPropertyName("alamatPasangan")]
        public string AlamatPasangan { get; set; } = string.Empty;

        [JsonPropertyName("jumlahAnak")]
        public int JumlahAnak => Anak.Count;

        [JsonPropertyName("anak")]
        public List<AnakAhliWarisData> Anak { get; set; } = new List<AnakAhliWarisData>();

        [JsonPropertyName("namaPenerimaKuasa")]
        public string NamaPenerimaKuasa { get; set; } = string.Empty;

        [JsonPropertyName("tempatLahirPenerimaKuasa")]
        public string TempatLahirPenerimaKuasa { get; set; } = string.Empty;

        [JsonPropertyName("tanggalLahirPenerimaKuasa")]
        public DateTime? TanggalLahirPenerimaKuasa { get; set; }

        [JsonPropertyName("alamatPenerimaKuasa")]
        public string AlamatPenerimaKuasa { get; set; } = string.Empty;

        [JsonPropertyName("jenisKelaminPenerimaKuasa")]
        public string JenisKelaminPenerimaKuasa { get; set; } = string.Empty;
    }

    public class AnakAhliWarisData
    {
        public string NIK { get; set; } = "0000000000000000";

        [JsonPropertyName("nama")]
        public string Nama { get; set; } = string.Empty;

        [JsonPropertyName("tempatLahir")]
        public string TempatLahir { get; set; } = string.Empty;

        [JsonPropertyName("tanggalLahir")]
        public DateTime? TanggalLahir { get; set; }

        [JsonPropertyName("jenisKelamin")]
        public string JenisKelamin { get; set; } = string.Empty; // "Laki-laki" atau "Perempuan"

        [JsonPropertyName("alamat")]
        public string Alamat { get; set; } = string.Empty;
    }

    public class Waris
    {
        [JsonPropertyName("idAhliWaris")]
        public int ID_AhliWaris { get; set; }

        [JsonPropertyName("namaWaris")]
        public string NamaWaris { get; set; } = string.Empty;

        [JsonPropertyName("nikWaris")]
        public string NIKWaris { get; set; } = string.Empty;

        [JsonPropertyName("hubunganWaris")]
        public string HubunganWaris { get; set; } = string.Empty; // Misalnya: "Istri", "Anak", dll.
    }

    public class AhliWaris
    {
        [JsonPropertyName("waris")]
        public List<Waris> Waris { get; set; } = new List<Waris>();
    }
}
