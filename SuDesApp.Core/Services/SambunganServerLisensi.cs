using System;

namespace SuDesApp.Services
{
    /// <summary>Keadaan sambungan ke server lisensi menurut panggilan TERAKHIR yang benar-benar terjadi.</summary>
    public enum KeadaanSambunganServer
    {
        /// <summary>Belum ada panggilan yang selesai sejak aplikasi dibuka — bukan gagal, hanya belum diketahui.</summary>
        BelumDiuji = 0,

        /// <summary>
        /// Server menjawab, apa pun status HTTP-nya: alamatnya benar dan
        /// layanannya hidup (bahwa jawabannya 404/500 adalah urusan lain).
        /// </summary>
        Terhubung = 1,

        /// <summary>Permintaan tidak sampai: alamat salah, server mati, atau jaringan putus.</summary>
        TidakTerjangkau = 2
    }

    /// <summary>
    /// Pemilik bersama keadaan sambungan server lisensi: dicatat oleh setiap
    /// panggilan sungguhan ke server (cek lisensi berkala, muat katalog harga,
    /// buat tagihan), dan dibaca tampilan yang menanyakan "server mana yang
    /// sedang dipakai" — mis. baris menu Pusat Administrasi di sidebar.
    ///
    /// Jujur apa adanya: keadaan hanya berubah bila benar-benar ada panggilan
    /// yang selesai, jadi "Belum diuji" berarti memang belum ada percobaan —
    /// bukan "gagal" yang menakut-nakuti tanpa alasan, dan bukan "Terhubung"
    /// yang dibanggakan tanpa bukti. Pelanggannya (lapisan jaringan) wajib
    /// memanggil <see cref="CatatTerhubung"/> hanya setelah ADA balasan dari
    /// server, dan <see cref="CatatTidakTerjangkau"/> hanya untuk kegagalan
    /// jaringan sungguhan — bukan untuk kegagalan lokal seperti berkas tidak
    /// bisa ditulis atau jawaban yang gagal dibaca.
    /// </summary>
    public sealed class SambunganServerLisensi
    {
        /// <summary>Keadaan yang sedang berlaku.</summary>
        public KeadaanSambunganServer Sekarang { get; private set; } = KeadaanSambunganServer.BelumDiuji;

        /// <summary>Terjadi setelah keadaan benar-benar berubah.</summary>
        public event Action? Berubah;

        /// <summary>Server menjawab — dicatat setelah balasan apa pun diterima.</summary>
        public void CatatTerhubung() => Catat(KeadaanSambunganServer.Terhubung);

        /// <summary>Permintaan tidak sampai ke server.</summary>
        public void CatatTidakTerjangkau() => Catat(KeadaanSambunganServer.TidakTerjangkau);

        private void Catat(KeadaanSambunganServer keadaan)
        {
            if (Sekarang == keadaan) return;
            Sekarang = keadaan;
            Berubah?.Invoke();
        }

        /// <summary>Kalimat keadaan untuk tampilan: "Terhubung" / "Belum terjangkau" / "Belum diuji".</summary>
        public static string Teks(KeadaanSambunganServer keadaan) => keadaan switch
        {
            KeadaanSambunganServer.Terhubung => "Terhubung",
            KeadaanSambunganServer.TidakTerjangkau => "Belum terjangkau",
            _ => "Belum diuji"
        };
    }
}
