namespace SuDesApp.Data.Models
{
    public static class SuratConstants
    {
        /// <summary>
        /// Jenis surat untuk surat kematian.
        /// </summary>
        public const string KEMATIAN = "KEMATIAN";

        /// <summary>
        /// Jenis surat untuk surat keterangan usaha.
        /// </summary>
        public const string SKU = "SKU";

        /// <summary>
        /// Jenis surat untuk izin suami/orang tua.
        /// </summary>
        public const string IZIN_ORTU = "IZIN_ORTU";

        /// <summary>
        /// Jenis surat untuk keterangan garapan sawah.
        /// </summary>
        public const string GARAPAN_SAWAH = "GARAPAN_SAWAH";

        /// <summary>
        /// Jenis surat untuk surat keterangan tidak mampu.
        /// </summary>
        public const string SKTM = "SKTM";

        /// <summary>
        /// Jenis surat untuk domisili warga.
        /// </summary>
        public const string DOMISILI_WARGA = "DOMISILI_WARGA";

        /// <summary>
        /// Jenis surat untuk domisili instansi/lembaga.
        /// </summary>
        public const string INSTANSI = "INSTANSI";

        /// <summary>
        /// Jenis surat untuk surat beda data.
        /// </summary>
        public const string BEDANAMA = "BEDANAMA";

        /// <summary>
        /// Jenis surat untuk surat kenal lahir.
        /// </summary>
        public const string KENAL_LAHIR = "KENAL_LAHIR";

        /// <summary>
        /// Jenis surat untuk keterangan ahli waris.
        /// </summary>
        public const string AHLI_WARIS = "AHLI_WARIS";

        /// <summary>
        /// Jenis surat untuk pengantar SKCK.
        /// </summary>
        public const string PENGANTAR_SKCK = "PENGANTAR_SKCK";

        /// <summary>
        /// Jenis surat untuk SKD umum.
        /// </summary>
        public const string SKD_UMUM = "SKD_UMUM";

        /// <summary>
        /// Jenis surat untuk surat izin tinggal.
        /// </summary>
        public const string IJIN_TINGGAL = "IJIN_TINGGAL";

        /// <summary>
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N1.
        /// </summary>
        public const string NTCR_N1 = "NTCR_N1";

        /// <summary>
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N2.
        /// </summary>
        public const string NTCR_N2 = "NTCR_N2";

        /// <summary>
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N3.
        /// </summary>
        public const string NTCR_N3 = "NTCR_N3";

        /// <summary>
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N4.
        /// </summary>
        public const string NTCR_N4 = "NTCR_N4";

        /// <summary>
        /// Daftar semua jenis surat yang valid.
        /// </summary>
        public static readonly string[] ValidJenisSurat =
        {
            KEMATIAN, SKU, IZIN_ORTU, GARAPAN_SAWAH, SKTM, DOMISILI_WARGA,
            INSTANSI, BEDANAMA, KENAL_LAHIR, AHLI_WARIS, PENGANTAR_SKCK,
            SKD_UMUM, IJIN_TINGGAL, NTCR_N1, NTCR_N2, NTCR_N3, NTCR_N4
        };

        /// <summary>
        /// Status surat yang valid.
        /// </summary>
        public static readonly string[] ValidStatus =
        {
            "Draft", "Active", "Cancelled"
        };

        /// <summary>
        /// Hubungan keluarga yang valid untuk ahli waris dan kematian.
        /// </summary>
        public static readonly string[] ValidHubunganKeluarga =
        {
            "Suami", "Istri", "Anak", "Ayah", "Ibu", "Saudara", "Lainnya"
        };

        /// <summary>
        /// Jenis kelamin yang valid.
        /// </summary>
        public static readonly string[] ValidJenisKelamin =
        {
            "L", "P", "Laki-laki", "Perempuan"
        };

        /// <summary>
        /// Agama yang valid.
        /// </summary>
        public static readonly string[] ValidAgama =
        {
            "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu"
        };

        /// <summary>
        /// Status perkawinan yang valid.
        /// </summary>
        public static readonly string[] ValidStatusPerkawinan =
        {
            "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati"
        };

        /// <summary>
        /// Tingkat pendidikan yang valid.
        /// </summary>
        public static readonly string[] ValidPendidikan =
        {
            "Tidak/Belum Sekolah",
            "Belum Tamat SD/Sederajat",
            "Tamat SD/Sederajat",
            "SLTP/Sederajat",
            "SLTA/Sederajat",
            "Diploma I/II",
            "Akademi/Diploma III/S.Muda",
            "Diploma IV/Strata I",
            "Strata II",
            "Strata III"
        };

        /// <summary>
        /// Kewarganegaraan yang valid.
        /// </summary>
        public static readonly string[] ValidKewarganegaraan =
        {
            "WNI", "WNA"
        };

        /// <summary>
        /// NIK khusus untuk instansi.
        /// </summary>
        public const string NIK_INSTANSI = "9999999999999999";

        /// <summary>
        /// NIK khusus untuk kematian (dummy).
        /// </summary>
        public const string NIK_KEMATIAN = "0000000000000000";

        /// <summary>
        /// Maksimal ukuran file upload (dalam bytes) - 5MB.
        /// </summary>
        public const long MAX_FILE_SIZE = 5 * 1024 * 1024;

        /// <summary>
        /// Format file yang diperbolehkan untuk upload.
        /// </summary>
        public static readonly string[] AllowedFileExtensions =
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".docx", ".doc"
        };
    }
}
