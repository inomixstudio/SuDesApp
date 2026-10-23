namespace SuDesApp.Server;

/// <summary>
/// Satu notifikasi pembayaran yang benar-benar diterima server.
///
/// <para>Yang dicatat sengaja INI SAJA dan bukan isi mentah body: operator
/// memakai halaman uji untuk melihat "notifikasi sungguhan datang atau tidak",
/// jadi yang penting adalah buktinya — dari mana, kapan, signature-nya
/// sah atau tidak, dan server menjawab apa. Isi body (nama, nomor rekening
/// pengirim) tidak ada di sini supaya jejaknya tidak jadi tempat держать
/// data pembayaran yang tidak perlu disimpan.</para>
/// </summary>
/// <param name="Waktu">Kapan notifikasi diterima (UTC).</param>
/// <param name="OrderId">Order id dari notifikasi; "(tidak terbaca)" bila body tak terurai.</param>
/// <param name="TransactionStatus">Status transaksi seperti dikirim Midtrans.</param>
/// <param name="GrossAmount">Nominal seperti dikirim (teks apa adanya).</param>
/// <param name="FraudStatus">Status FDS untuk kartu, bila ada.</param>
/// <param name="PaymentType">Kanal pembayaran (qris, bank_transfer, credit_card…).</param>
/// <param name="SignatureValid">True sah, false ditolak, null = belum dinilai (bad request).</param>
/// <param name="StatusBalasan">Kode HTTP yang server balas ke Midtrans.</param>
/// <param name="Diterima">Apakah notifikasi itu dianggap sah oleh aturan pembayaran.</param>
/// <param name="Duplikat">True bila order-nya sudah pernah diproses.</param>
/// <param name="HariDitambah">Berapa hari akses yang ditambahkan (null bila tidak ada).</param>
/// <param name="Pesan">Keterangan hasil pemrosesan.</param>
/// <param name="AlamatIp">IP pengirim (sudah memperhitungkan proxy tepercaya).</param>
/// <param name="UserAgent">User-Agent pengirim — Midtrans mengiri "Veritrans/…".</param>
public sealed record CatatanNotifikasi(
    DateTime Waktu,
    string OrderId,
    string? TransactionStatus,
    string? GrossAmount,
    string? FraudStatus,
    string? PaymentType,
    bool? SignatureValid,
    int StatusBalasan,
    bool Diterima,
    bool Duplikat,
    int? HariDitambah,
    string? Pesan,
    string? AlamatIp,
    string? UserAgent);

/// <summary>
/// Penyimpan jejak notifikasi webhook yang masuk, supaya operator bisa
/// melihat bukti notifikasi sungguhan tanpa menggali log server.
///
/// <para>Disimpan di MEMORI saja: jejaknya untuk dipakai DURASIKUJI, bukan
/// arsip. Kalau notifikasi ini ikut disimpan permanen, server jadi gudang
/// data pembayaran yang tak pernah dibersihkan — dan tabel `pembayaran`
/// yang sudah ada tetap satu-satunya jejak uang yang permanen.</para>
///
/// <para>Jumlahnya dibatasi (antrean berbatas) supaya halaman uji yang
/// dibiarkan terbuka semalaman tidak membuat memori server membengkak.</para>
/// </summary>
public sealed class PelacakWebhook
{
    /// <summary>Berapa notifikasi terakhir yang disimpan.</summary>
    public const int MaksCatatan = 100;

    private readonly object _kunci = new();
    private readonly Queue<CatatanNotifikasi> _catatan = new();

    /// <summary>Catat satu notifikasi yang baru saja diterima.</summary>
    public void Catat(CatatanNotifikasi catatan)
    {
        lock (_kunci)
        {
            _catatan.Enqueue(catatan);
            while (_catatan.Count > MaksCatatan) _catatan.Dequeue();
        }
    }

    /// <summary>Semua catatan, terbaru lebih dulu.</summary>
    public IReadOnlyList<CatatanNotifikasi> Semua()
    {
        lock (_kunci)
        {
            return _catatan.Reverse().ToArray();
        }
    }

    /// <summary>Catatan untuk satu order id saja.</summary>
    public IReadOnlyList<CatatanNotifikasi> Untuk(string? orderId)
    {
        var kunci = (orderId ?? "").Trim();
        if (kunci.Length == 0) return Array.Empty<CatatanNotifikasi>();

        lock (_kunci)
        {
            return _catatan
                .Where(c => string.Equals(c.OrderId, kunci, StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .ToArray();
        }
    }
}
