using System;

namespace SuDesApp.Services
{
    /// <summary>
    /// Penerjemah SATU-SATUNYA dari <see cref="StatusLisensi"/> menjadi kata-kata
    /// yang dibaca pengguna di halaman Langganan: judul+nada banner, keterangan
    /// di bawahnya, nama paket, dan tiga petak ringkasan (sah sampai, sisa hari,
    /// terakhir diperiksa).
    ///
    /// Kenapa di Core, bukan di ViewModel: aturannya bukan urusan tampilan.
    /// Tanggal mana yang dipakai ("sah sampai", bukan "berakhir"), kapan banner
    /// berwarna kuning walau langganan masih aktif (tanggal bayar sudah lewat,
    /// aplikasi hidup dari masa tenggang), dan kapan tombol trial disembunyikan
    /// adalah kebijakan lisensi yang harus sama dengan yang dipakai gerbang
    /// fitur. Sebelumnya aturan tanggal itu ditulis dua kali — sekali di banner,
    /// sekali di petak KPI — sehingga keduanya bisa menyimpang; sekarang
    /// keduanya membaca hasil objek ini.
    ///
    /// Kelas ini tidak memutuskan APA PUN tentang uang atau kelayakan: inputnya
    /// status yang klaim tokennya sudah diverifikasi <see cref="LisensiManager"/>,
    /// dan <see cref="StatusLisensi.SahSampai"/> sudah dihitung dari rumus yang
    /// sama dengan gerbang fitur.
    /// </summary>
    public sealed record RingkasanLangganan(
        string Judul,
        string Keterangan,
        int Nada,
        bool TampilTombolTrial,
        string PaketNama,
        string SahSampaiTampil,
        string SisaHariTampil,
        string TerakhirDiperiksaTampil)
    {
        /// <summary>Nada netral — dipakai sebelum ada status sama sekali.</summary>
        private const int NadaNetral = 0;

        /// <summary>Nada hijau: aktif dan tidak ada yang perlu dikerjakan.</summary>
        private const int NadaAktif = 1;

        /// <summary>Nada kuning: bisa dipakai, tetapi perlu tindakan (bayar/perpanjang).</summary>
        private const int NadaPerluTindakan = 2;

        /// <summary>Ringkasan untuk keadaan "belum ada status terbaca".</summary>
        public static RingkasanLangganan BelumDiperiksa { get; } = new(
            Judul: "Belum diperiksa",
            Keterangan: string.Empty,
            Nada: NadaNetral,
            TampilTombolTrial: true,
            PaketNama: "—",
            SahSampaiTampil: "—",
            SisaHariTampil: "—",
            TerakhirDiperiksaTampil: "—");

        /// <summary>
        /// Susun ringkasan dari status yang sudah diverifikasi. <paramref name="kodeAktivasi"/>
        /// hanya dipakai untuk memilih kalimat saat belum ada langganan sama sekali —
        /// "isi kode aktivasi" berbeda maksudnya dari "kode belum dikenal server".
        /// </summary>
        public static RingkasanLangganan Dari(StatusLisensi status, string? kodeAktivasi)
        {
            ArgumentNullException.ThrowIfNull(status);

            // Batas SAH efektif (tanggal bayar ATAU tenggang), bukan tanggal bayar
            // saja: saat tanggal bayar sudah lewat tetapi aplikasi masih di masa
            // tenggang, tanggal bayar sudah di masa lalu dan terbaca seolah
            // statusnya tidak aktif.
            var sahSampai = status.SahSampai ?? status.BerlakuSampai;

            // Tanggal bayar lewat tetapi tenggang masih berjalan: aplikasi masih
            // bisa dipakai, tetapi ini keadaan yang butuh tindakan — hijau
            // "semua beres" justru menyembunyikannya.
            bool lewatTanggalBayar = status.Aktif
                && status.BerlakuSampai is DateTime bayar
                && bayar < DateTime.UtcNow;

            string judul;
            string keterangan;
            int nada;

            if (status.Aktif && sahSampai is DateTime sah)
            {
                judul = $"Aktif s.d. {Tanggal(sah)}";
                nada = lewatTanggalBayar ? NadaPerluTindakan : NadaAktif;
                keterangan = lewatTanggalBayar
                    ? $"Masa bayar sudah lewat {Tanggal(status.BerlakuSampai)}, tetapi aplikasi masih sah s.d. " +
                      $"{Tanggal(sah)} (masa tenggang {LisensiManager.MASA_TENGGANG_HARI} hari). " +
                      "Sambungkan internet lalu tekan Cek Status untuk memperpanjang."
                    : $"Langganan sah{(string.IsNullOrEmpty(status.NamaDesa) ? "" : " — " + status.NamaDesa)}. " +
                      $"Sisa {status.SisaHariTenggang} hari sebelum perlu koneksi internet lagi.";
            }
            else if (status.BerlakuSampai.HasValue)
            {
                judul = "Tidak aktif — langganan berakhir";
                nada = NadaPerluTindakan;
                keterangan = $"Berakhir {Tanggal(status.BerlakuSampai)}. Perpanjang lewat tombol Bayar.";
            }
            else
            {
                judul = "Tidak aktif";
                nada = NadaPerluTindakan;
                keterangan = string.IsNullOrWhiteSpace(kodeAktivasi)
                    ? "Masukkan kode aktivasi lalu tekan Cek Status."
                    : "Kode aktivasi belum dikenal server atau belum pernah bayar.";
            }

            return new RingkasanLangganan(
                Judul: judul,
                Keterangan: keterangan,
                Nada: nada,
                TampilTombolTrial: !status.Aktif,
                PaketNama: KatalogLangganan.Bawaan.LabelAktif(status.Paket),
                SahSampaiTampil: Tanggal(sahSampai),
                SisaHariTampil: status.Aktif ? status.SisaHariTenggang + " hari" : "—",
                TerakhirDiperiksaTampil: TanggalDanJam(status.CekTerakhir));
        }

        /// <summary>Tanggal UTC → tanggal lokal ("22 Okt 2026"), atau "—" bila belum ada.</summary>
        private static string Tanggal(DateTime? utc) =>
            utc is DateTime t ? t.ToLocalTime().ToString("dd MMM yyyy") : "—";

        /// <summary>Waktu UTC → waktu lokal ("22 Okt 2026 14:05"), atau "—" bila belum pernah.</summary>
        private static string TanggalDanJam(DateTime utc) =>
            utc > DateTime.MinValue ? utc.ToLocalTime().ToString("dd MMM yyyy HH:mm") : "—";
    }
}
