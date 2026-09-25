using System;

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
        /// Jenis surat untuk permohonan print out rekening koran ke bank. Ikut
        /// penomoran bersama (urut yang sama dengan SKD dan surat keterangan lain),
        /// karena surat ini juga surat keluar desa.
        /// </summary>
        public const string REKENING_KORAN = "REKENING_KORAN";

        /// <summary>
        /// Jenis surat untuk surat yang dibuat dari menu Template Surat (jenis surat
        /// buatan pengguna sendiri). Nomornya berasal dari template — tiap template
        /// punya penghitung sendiri — sehingga tidak ikut penomoran bersama surat desa.
        /// Seluruh isi surat disimpan pada payload <c>AdditionalData</c> baris register.
        /// </summary>
        public const string TEMPLATE_SURAT = "TEMPLATE_SURAT";

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
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N5.
        /// </summary>
        public const string NTCR_N5 = "NTCR_N5";

        /// <summary>
        /// Jenis surat untuk persyaratan pendaftaran pernikahan (NTCR) — N6.
        /// </summary>
        public const string NTCR_N6 = "NTCR_N6";

        /// <summary>
        /// Jenis surat keterangan numpang nikah (numpang kawin) — N8. Bukan blanko
        /// Kepdirjen, melainkan surat keterangan desa bagi warga yang akan
        /// melangsungkan akad nikah di wilayah lain.
        /// </summary>
        public const string NTCR_N8 = "NTCR_N8";

        /// <summary>
        /// Seluruh jenis surat NTCR yang dikenal aplikasi: blanko Kepdirjen N1–N6
        /// (Keputusan Dirjen Bimas Islam No. 473 Tahun 2020) ditambah N8 (surat
        /// keterangan numpang nikah buatan desa). Inilah daftar yang dipakai menu,
        /// register, generator, validator, dan handler data.
        ///
        /// Model N7 (penolakan kehendak nikah/rujuk) sudah dihapus dari aplikasi
        /// karena diterbitkan KUA, bukan kantor desa.
        /// </summary>
        public static readonly string[] NtcrSemua =
        {
            NTCR_N1, NTCR_N2, NTCR_N3, NTCR_N4, NTCR_N5, NTCR_N6, NTCR_N8
        };

        /// <summary>
        /// Blanko NTCR Kepdirjen N1–N6 yang memakai satu deret penomoran bersama
        /// (awalan 474.3). N8 sengaja tidak disertakan karena memakai deret sendiri
        /// (awalan 474.2).
        /// </summary>
        public static readonly string[] NtcrBlankoKepdirjen =
        {
            NTCR_N1, NTCR_N2, NTCR_N3, NTCR_N4, NTCR_N5, NTCR_N6
        };

        /// <summary>Apakah <paramref name="namaJenis"/> salah satu blanko NTCR N1–N6.</summary>
        public static bool IsNtcrBlankoKepdirjen(string namaJenis) =>
            !string.IsNullOrWhiteSpace(namaJenis) &&
            Array.Exists(NtcrBlankoKepdirjen, n => string.Equals(n, namaJenis, StringComparison.OrdinalIgnoreCase));

        /// <summary>Apakah <paramref name="namaJenis"/> termasuk kelompok formulir NTCR.</summary>
        public static bool IsNtcr(string namaJenis) =>
            !string.IsNullOrWhiteSpace(namaJenis) &&
            Array.Exists(NtcrSemua, n => string.Equals(n, namaJenis, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Daftar semua jenis surat yang valid.
        /// </summary>
        public static readonly string[] ValidJenisSurat =
        {
            KEMATIAN, SKU, IZIN_ORTU, GARAPAN_SAWAH, SKTM, DOMISILI_WARGA,
            INSTANSI, BEDANAMA, KENAL_LAHIR, AHLI_WARIS, PENGANTAR_SKCK,
            SKD_UMUM, IJIN_TINGGAL, REKENING_KORAN, TEMPLATE_SURAT,
            NTCR_N1, NTCR_N2, NTCR_N3,
            NTCR_N4, NTCR_N5, NTCR_N6, NTCR_N8
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
