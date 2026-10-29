using System.Collections.Generic;
using System.Globalization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu terbitan resmi pemerintah pusat tentang status/indeks desa.
    /// </summary>
    /// <param name="Tahun">Tahun data yang ditetapkan dokumen ini.</param>
    /// <param name="Judul">Judul dokumen persis seperti di situs resmi.</param>
    /// <param name="Alamat">Halaman resmi dokumen (JDIH Kemendesa PDT).</param>
    /// <param name="Keterangan">Penjelasan singkat: apa yang ditetapkan dan apa yang belum terbit.</param>
    public sealed record TerbitanIdmResmi(
        int Tahun,
        string Judul,
        string Alamat,
        string Keterangan);

    /// <summary>
    /// Terbitan resmi pusat yang berkaitan dengan Indeks Desa Membangun, dipakai
    /// halaman IDM ketika portal IDM belum memuat tahun terbaru.
    ///
    /// Aturan isi daftar ini: hanya dokumen yang benar-benar terbit di situs resmi
    /// pemerintah pusat (JDIH Kementerian Desa dan PDT) dan sudah diperiksa.
    /// Dataset nasional Indeks Desa 2025 ditampilkan terpisah dari seri IDM lama;
    /// enam dimensi baru tidak dipetakan ke IKS/IKE/IKL.
    /// </summary>
    public static class IdmTerbitanResmi
    {
        /// <summary>Daftar terbitan resmi yang sudah diverifikasi (urut tahun menaik).</summary>
        public static IReadOnlyList<TerbitanIdmResmi> Semua { get; } = new[]
        {
            new TerbitanIdmResmi(
                2025,
                "Kepmendesa PDT No. 343 Tahun 2025 — Status Kemajuan dan Kemandirian Desa Tahun 2025",
                "https://jdih.kemendesa.go.id/web/regulations/read/"
                + "status-kemajuan-dan-kemandirian-desa-tahun-2025-343-2025",
                "Ditetapkan 27 Agustus 2025 di JDIH Kementerian Desa dan PDT. Penilaian tahun 2025 "
                + "kini ditampilkan terpisah sebagai Indeks Desa enam dimensi."),
        };

        /// <summary>Terbitan resmi terbaru yang tahunnya tidak melewati tahun berjalan.</summary>
        public static TerbitanIdmResmi? Terbaru(int tahunBerjalan)
        {
            TerbitanIdmResmi? terbaru = null;
            foreach (var terbitan in Semua)
            {
                if (terbitan.Tahun <= tahunBerjalan &&
                    (terbaru == null || terbitan.Tahun > terbaru.Tahun))
                {
                    terbaru = terbitan;
                }
            }

            return terbaru;
        }

        /// <summary>
        /// Terbitan resmi yang LEBIH BARU dari tahun data yang sedang tampil —
        /// null bila tidak ada (mis. data yang tampil sudah tahun terbaru).
        /// </summary>
        public static TerbitanIdmResmi? LebihBaruDari(int tahunTampil, int tahunBerjalan)
        {
            var terbaru = Terbaru(tahunBerjalan);
            return terbaru != null && terbaru.Tahun > tahunTampil ? terbaru : null;
        }

        /// <summary>
        /// Kalimat penjelas untuk halaman IDM: menyebut tahun yang sudah ditetapkan
        /// pusat dan menjelaskan bahwa seri Indeks Desa 2025 berbeda dari IDM lama.
        /// Kosong bila tidak ada terbitan yang lebih baru.
        /// </summary>
        public static string Keterangan(int tahunTampil, int tahunBerjalan)
        {
            var terbitan = LebihBaruDari(tahunTampil, tahunBerjalan);
            if (terbitan == null)
            {
                return string.Empty;
            }

            string tahun = terbitan.Tahun.ToString(CultureInfo.InvariantCulture);
            return "Kepmendesa PDT menetapkan status desa tahun " + tahun
                + ". Penilaian tahun " + tahun
                + " ditampilkan terpisah sebagai Indeks Desa enam dimensi dari dataset resmi Kementerian Desa PDT.";
        }
    }
}
