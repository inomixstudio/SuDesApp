namespace SuDesApp.Server;

/// <summary>
/// Tagihan yang berhasil dibuat di gateway pembayaran.
/// </summary>
/// <param name="OrderId">Order id yang sama dengan yang tersimpan di tabel tagihan.</param>
/// <param name="Nominal">Nominal yang diminta gateway (selalu harga dari katalog server).</param>
/// <param name="RedirectUrl">Tautan yang dibuka pembeli.</param>
/// <param name="Token">Token Snap, bila gateway memakainya.</param>
public sealed record TagihanGateway(string OrderId, long Nominal, string? RedirectUrl, string? Token);

/// <summary>
/// Pembuat tagihan di gateway pembayaran.
///
/// Dipisah sebagai antarmuka supaya aturan pembuatan tagihan (paket &amp; harga
/// ditentukan server, order id, apa yang disimpan ke database) bisa diuji tanpa
/// memanggil Midtrans — sama alasannya dengan
/// <see cref="IPenyediaStatusPembayaran"/>.
/// </summary>
public interface IPembuatTagihan
{
    /// <summary>Apakah gateway siap dipakai (kredensial terisi).</summary>
    bool Aktif { get; }

    /// <summary>
    /// Buat tagihan dan kembalikan tautan pembayarannya. Null = gagal (galat
    /// gateway, HTTP gagal, atau kredensial ditolak); pemanggil yang memutuskan
    /// apa yang ditampilkan ke pengguna.
    /// </summary>
    /// <param name="urlNotifikasi">
    /// URL notifikasi KHUSUS untuk transaksi ini (header
    /// <c>X-Override-Notification</c>). Null = notifikasi mengikuti "Payment
    /// Notification URL" tingkat akun di dashboard gateway.
    ///
    /// <para>Parameter ini hanya dipakai jalur uji; tagihan pelanggan sungguhan
    /// selalu meneruskan null supaya notifikasinya tetap ke URL akun.</para>
    /// </param>
    Task<TagihanGateway?> BuatTagihanAsync(
        string orderId, long nominal, string namaBarang, string? urlNotifikasi = null);
}
