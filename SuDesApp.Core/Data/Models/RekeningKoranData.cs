using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Data.Models
{
    public class RekeningKoranData
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        /// <summary>
        /// Payload JSON untuk kolom <c>Surat.AdditionalData</c> — dipakai agar surat
        /// permohonan rekening koran yang tercatat di register bisa dibuka/dicetak
        /// ulang dengan isi yang sama.
        /// </summary>
        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        /// <summary>Baca kembali payload dari <c>AdditionalData</c>; null bila kosong/rusak.</summary>
        public static RekeningKoranData? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<RekeningKoranData>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        [Required]
        public string ?NomorSurat { get; set; }

        [Required]
        public string Perihal { get; set; } = "Permohonan Print Out Rekening Koran";

        [Required]
        public DateTime TanggalSurat { get; set; }

        [Required]
        public string ?NamaPejabat { get; set; }

        [Required]
        public string ?Jabatan { get; set; }

        /// <summary>
        /// Alamat pejabat dalam satu baris bebas. Dipertahankan supaya surat lama yang
        /// sudah tersimpan di register tetap bisa dicetak; surat baru memakai empat
        /// komponen alamat di bawah ini.
        /// </summary>
        public string? AlamatPejabat { get; set; }

        /// <summary>Komponen alamat pejabat — Dusun/Jalan (seperti kolom di menu input surat).</summary>
        public string? AlamatDusun { get; set; }

        /// <summary>Komponen alamat pejabat — Desa.</summary>
        public string? AlamatDesa { get; set; }

        /// <summary>Komponen alamat pejabat — Kecamatan.</summary>
        public string? AlamatKecamatan { get; set; }

        /// <summary>Komponen alamat pejabat — Kabupaten.</summary>
        public string? AlamatKabupaten { get; set; }

        /// <summary>
        /// Alamat pejabat siap cetak. Dibentuk dari empat komponen memakai pemformatan
        /// yang sama dengan surat-surat lain (Dusun/Jalan &amp; Desa pada baris pertama,
        /// Kecamatan &amp; Kabupaten pada baris kedua), sehingga hasil cetaknya identik
        /// dengan hasil input surat. Surat lama yang belum punya komponen memakai
        /// <see cref="AlamatPejabat"/> apa adanya.
        /// </summary>
        [JsonIgnore]
        public string AlamatPejabatLengkap
        {
            get
            {
                string dariKomponen = GeneratorPdf.AlamatFormatter.Format(
                    AlamatDusun, AlamatDesa, AlamatKecamatan, AlamatKabupaten, fallback: string.Empty);

                return dariKomponen.Length > 0
                    ? dariKomponen
                    : (AlamatPejabat ?? string.Empty).Trim();
            }
        }

        /// <summary>Alamat belum terisi sama sekali (memicu peringatan pada validasi).</summary>
        [JsonIgnore]
        public bool AlamatKosong => string.IsNullOrWhiteSpace(AlamatPejabatLengkap);

        [Required]
        public string ?NamaPemegangRekening { get; set; }

        [Required]
        public string ?NomorRekening { get; set; }

        [Required]
        public string ?PeriodeRekening { get; set; }

        public DesaData ?Desa { get; set; }

        [Required]
        public string ?Bank { get; set; }

        [Required]
        public string ?KCP { get; set; }
    }
}
