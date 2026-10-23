namespace SuDesApp.Server;

/// <summary>Pelanggan (desa) yang membeli langganan SuDesApp.</summary>
public class Pelanggan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Kode unik yang diketik di aplikasi desktop saat aktivasi.</summary>
    public string KodeAktivasi { get; set; } = "";

    public string NamaDesa { get; set; } = "";
    public string Kontak { get; set; } = "";
    public DateTime Dibuat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Batas komputer yang boleh memakai kode aktivasi ini. Mencegah satu
    /// langganan dipakai di banyak PC sekaligus.
    /// </summary>
    public int MaxPerangkat { get; set; } = 2;

    /// <summary>Langganan terkait (diisi SupabaseStore saat baca dari DB).</summary>
    public Langganan? Langganan { get; set; }

    /// <summary>
    /// True setelah desa ini pernah memakai trial 14 hari. Trial hanya boleh
    /// SEKALI per desa — kolom ini yang menahannya, bukan kejujuran klien.
    /// </summary>
    public bool TrialSudah { get; set; }
}

/// <summary>
/// Status langganan aktif. Diperbarui HANYA dari webhook pembayaran yang
/// tanda tangannya sudah diverifikasi — bukan dari permintaan klien mana pun.
/// </summary>
public class Langganan
{
    public string PelangganId { get; set; } = "";
    public string Paket { get; set; } = "bulanan";

    /// <summary>Waktu jatuh tempo akses (UTC). Null = belum pernah bayar.</summary>
    public DateTime? BerlakuSampai { get; set; }

    /// <summary>Tagihan terakhir yang dibuat untuk siklus ini.</summary>
    public string? OrderIdTagihanTerakhir { get; set; }

    public int TotalTerbayar { get; set; }
}

/// <summary>
/// Permintaan pengecekan aktivasi dari aplikasi desktop.
/// <c>IdPerangkat</c> dipakai server untuk membatasi jumlah PC per langganan.
/// </summary>
public sealed record AktivasiRequest(string KodeAktivasi, string? IdPerangkat);

/// <summary>
/// Ringkasan satu desa untuk "pusat administer" — cukup untuk melihat desa mana
/// yang mentok kuota perangkat tanpa membuka data warga. <c>JumlahPerangkat</c>
/// dibandingkan dengan <c>MaxPerangkat</c> oleh pemanggil untuk menandai "penuh".
/// </summary>
public sealed record RingkasPelanggan(
    string Id, string KodeAktivasi, string NamaDesa, int MaxPerangkat,
    int JumlahPerangkat, string Paket, DateTime? BerlakuSampai);

/// <summary>Satu perangkat terdaftar untuk sebuah desa, beserta kapan terakhir terlihat.</summary>
public sealed record InfoPerangkat(string IdPerangkat, DateTime TerakhirLihat);

/// <summary>
/// Satu pembayaran (dana masuk) sebuah desa — bahan "riwayat pembayaran" di
/// pusat administer. <c>Paket</c> diambil dari tagihan terkait; <c>null</c> bila
/// tagihannya sudah tidak ada (mis. dibersihkan) sehingga paketnya tak diketahui.
/// <c>Catatan</c> adalah referensi bukti bayar yang diketik operator pada
/// pembukuan manual ("QRIS 17/10 21:04"); <c>null</c> untuk pembayaran gateway,
/// yang buktinya adalah notifikasi/status Midtrans itu sendiri.
///
/// <c>AdaBukti</c>/<c>TipeBukti</c>/<c>UkuranBukti</c> adalah LAMPIRAN gambar
/// bukti transfer yang diunggah operator (lihat <see cref="BuktiTransfer"/>).
/// Isi gambarnya sengaja TIDAK ikut di sini: daftar riwayat dipakai untuk
/// menampilkan tabel, bukan mengunduh berkas — gambarnya diambil terpisah
/// lewat endpoint bukti supaya satu desa dengan sepuluh pembayaran tidak
/// mengirim puluhan megabyte hanya untuk membuka halaman.
/// </summary>
public sealed record InfoPembayaran(
    string OrderId, long Nominal, string Status, DateTime Dicatat, string? Paket,
    string? Catatan = null,
    bool AdaBukti = false,
    string? TipeBukti = null,
    int UkuranBukti = 0);

/// <summary>
/// Satu lampiran bukti transfer: gambar yang diunggah operator saat menandai
/// lunas manual. <c>Tipe</c> SELALU hasil pembacaan isi berkas (bukan kata
/// pengirim), dan <c>Nama</c> hanya untuk tampilan.
/// </summary>
public sealed record BuktiBayar(byte[] Isi, string Tipe, string? Nama = null);

/// <summary>
/// Permintaan pembuatan tagihan.
///
/// <c>Paket</c> memilih paket ("bulanan" / "tahunan"); dikosongkan berarti
/// paket bawaan. SENGAJA TIDAK ada medan nominal: harga ditentukan server dari
/// katalog. Bila nominal boleh dikirim klien, pengguna bisa meminta tagihan
/// Rp1 lalu membayar Rp1 untuk mendapat akses penuh satu bulan (atau setahun).
/// </summary>
public sealed record BuatTagihanRequest(string KodeAktivasi, string? Paket = null);
