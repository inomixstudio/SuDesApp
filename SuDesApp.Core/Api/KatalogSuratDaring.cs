using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Api
{
    /// <summary>
    /// Katalog jenis surat yang bisa diminta lewat kanal daring (API Desa /
    /// backend web) - bukan lewat WhatsApp, yang kanalnya sudah dihapus.
    ///
    /// <para>Dipakai <see cref="IApiRingkasanService.AmbilJenisSuratOnline"/> supaya
    /// sistem luar tahu jenis surat mana yang boleh dikirim.</para>
    ///
    /// <para>Dulu katalog ini dua daftar: KatalogSurat (daring) dan HanyaKantor
    /// (kebalikannya), lalu daring = KatalogSurat dikurangi HanyaKantor. Daftar
    /// HanyaKantor berisi enam jenis NTCR dan KEMATIAN yang TIDAK pernah ada di
    /// KatalogSurat, jadi filter itu tidak menjaga apa pun: menghapus entri
    /// HanyaKantor tidak mengubah hasil. Katalog di sini satu daftar eksplisit;
    /// yang dikecualikan dinyatakan sebagai aturan (Dikecualikan) yang
    /// diperiksa uji, bukan sebagai filter yang bisa mati diam-diam.</para>
    /// </summary>
    public static class KatalogSuratDaring
    {
        /// <summary>
        /// Jenis surat yang bisa diproses daring. Setiap entri memuat kata kunci
        /// yang dikenali dari teks permintaan warga.
        /// </summary>
        public static readonly IReadOnlyList<(string[] KataKunci, string NamaJenis)> Semua =
            new (string[], string)[]
            {
                (new[] { "sktm", "tidak mampu", "kurang mampu" }, SuratConstants.SKTM),
                (new[] { "skd umum", "surat keterangan umum", "skd", "skd belum menikah", "keterangan belum menikah" }, SuratConstants.SKD_UMUM),
                (new[] { "domisili warga", "domisili" }, SuratConstants.DOMISILI_WARGA),
                (new[] { "pengantar skck", "skck" }, SuratConstants.PENGANTAR_SKCK),
                (new[] { "keterangan usaha", "surat usaha", "sku" }, SuratConstants.SKU),
                (new[] { "izin orang tua", "izin suami", "izin ortu", "izin" }, SuratConstants.IZIN_ORTU),
                (new[] { "instansi", "lembaga" }, SuratConstants.INSTANSI)
            };

        /// <summary>
        /// Aturan pengecualian: jenis yang TIDAK PERNAH ditawarkan daring.
        /// Diisi oleh uji, bukan dipakai sebagai filter terhadap Semua (yang
        /// hanya memuat yang memang bisa daring) - supaya penambahan entri
        /// keliru gagal uji alih-alih lolos diam-diam.
        /// </summary>
        public static readonly string[] Dikecualikan =
        {
            // Butuh tanda tangan basah / dokumen asli.
            SuratConstants.KEMATIAN,
            SuratConstants.BEDANAMA,
            SuratConstants.KENAL_LAHIR,
            SuratConstants.AHLI_WARIS,
            SuratConstants.IJIN_TINGGAL,
            // NTCR: formulir N1-N6 khusus, ditandatangani di KUA.
            SuratConstants.NTCR_N1,
            SuratConstants.NTCR_N2,
            SuratConstants.NTCR_N3,
            SuratConstants.NTCR_N4,
            SuratConstants.NTCR_N5,
            SuratConstants.NTCR_N6
        };

        /// <summary>Nama jenis surat yang boleh diminta daring, tanpa duplikat.</summary>
        public static IReadOnlyList<string> NamaJenisOnline()
            => Semua
                .Select(k => k.NamaJenis)
                .Where(n => !Dikecualikan.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
