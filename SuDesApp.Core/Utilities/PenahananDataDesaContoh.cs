using SuDesApp.Data.Models;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Aturan penahanan surat warga karena data desa masih contoh: teks alasannya,
    /// cara mengenali kembali permintaan yang tertahan karena sebab itu, dan kalimat
    /// pelaporan saat penahanannya dibuka.
    ///
    /// Teks alasan dan pengenalannya sengaja duduk di satu tempat: kalimat yang
    /// ditulis saat menahan harus tetap bisa dikenali saat data desa disimpan —
    /// termasuk pada permintaan yang ditahan versi aplikasi sebelumnya.
    /// </summary>
    public static class PenahananDataDesaContoh
    {
        /// <summary>
        /// Awal catatan permintaan yang menandai penahanan karena data desa contoh.
        /// Dikenali dari awalnya saja supaya daftar kolom di belakangnya boleh berubah.
        /// </summary>
        public const string AwalanCatatan = "Ditahan sebelum dibuat/dikirim: data desa masih contoh";

        /// <summary>
        /// Catatan yang menyertai penahanan — menyebut kolom mana yang masih contoh
        /// beserta jalan keluarnya. Kosong bila data desa tidak menahan apa pun.
        /// </summary>
        public static string Alasan(KeadaanDataDesaContoh? keadaan)
            => keadaan is { MasihContoh: true }
                ? $"{AwalanCatatan} ({keadaan.RingkasField}). " +
                  "Lengkapi Pengaturan Surat → Data Desa, lalu proses ulang permintaan ini."
                : string.Empty;

        /// <summary>
        /// Catatan pembuka penahanan: ditulis saat permintaan dikembalikan ke BARU
        /// supaya jalur otomatis biasa yang mengambilnya.
        /// </summary>
        public const string CatatanDibukaKembali =
            "Penahanan data desa contoh dibuka — data desa sudah diisi, permintaan diantrekan kembali untuk diproses otomatis.";

        /// <summary>
        /// True bila permintaan ini tertahan HANYA karena data desa masih contoh.
        /// Permintaan yang gagal karena sebab lain (generator, pengiriman) tidak
        /// termasuk, jadi tidak ikut diproses ulang begitu data desa diisi.
        /// </summary>
        public static bool Ditahan(string? status, string? catatan)
            => status == WaRequestStatus.PERLU_PERBAIKAN
               && catatan != null
               && catatan.TrimStart().StartsWith(AwalanCatatan, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True bila permintaan berada dalam keadaan tertahan oleh data desa contoh.</summary>
        public static bool Ditahan(PermintaanWa? permintaan)
            => permintaan != null && Ditahan(permintaan.Status, permintaan.Catatan);
    }
}
