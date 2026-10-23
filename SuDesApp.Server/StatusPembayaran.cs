namespace SuDesApp.Server;

/// <summary>
/// Hasil pembacaan status satu transaksi dari gateway.
/// </summary>
/// <param name="Ditemukan">
/// True bila gateway mengenal order ini DAN menjawab dengan data status.
/// False = tidak bisa dipastikan (404, HTTP gagal, jaringan mati) — pemanggil
/// tidak boleh menyimpulkan apa pun tentang pembayarannya.
/// </param>
/// <param name="TransactionStatus">Nilai <c>transaction_status</c> apa adanya dari gateway.</param>
/// <param name="GrossAmount">Nominal yang dicatat gateway, sebagai TEKS asli.</param>
/// <param name="FraudStatus"><c>fraud_status</c> (kartu): accept / challenge / deny.</param>
/// <param name="Pesan">Keterangan untuk log bila tidak ditemukan/gagal.</param>
public sealed record HasilStatusMidtrans(
    bool Ditemukan,
    string? TransactionStatus,
    string? GrossAmount,
    string? FraudStatus,
    string? Pesan)
{
    /// <summary>Tidak bisa dipastikan — bukan berarti belum dibayar.</summary>
    public static HasilStatusMidtrans TidakDiketahui(string pesan) =>
        new(false, null, null, null, pesan);
}

/// <summary>
/// Sumber status pembayaran bagi rekonsiliasi.
///
/// Dipisah sebagai antarmuka supaya aturan rekonsiliasi (kapan hak akses
/// diberikan, apa yang dihitung gagal) bisa diuji tanpa HTTP dan tanpa
/// Midtrans — sama alasannya dengan <see cref="IRepositoryLangganan"/>.
/// </summary>
public interface IPenyediaStatusPembayaran
{
    /// <summary>Apakah gateway siap ditanyai (kredensial terisi).</summary>
    bool Aktif { get; }

    /// <summary>
    /// Tanyakan status <paramref name="orderId"/> langsung ke gateway.
    /// Tidak pernah melempar: kegagalan apa pun dikembalikan sebagai
    /// <see cref="HasilStatusMidtrans.TidakDiketahui"/> supaya satu order yang
    /// bermasalah tidak menggagalkan seluruh putaran rekonsiliasi.
    /// </summary>
    Task<HasilStatusMidtrans> AmbilStatusAsync(string orderId, CancellationToken ct = default);
}
