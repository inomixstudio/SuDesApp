using Microsoft.Extensions.Logging;

namespace SuDesApp.Server;

/// <summary>
/// Logger ber-event ID tetap untuk layanan server. Source generator membuat
/// delegate bertipe sehingga pemanggilan log tidak membuat array object dan
/// format string pada setiap request.
/// </summary>
internal static partial class ServerLog
{
    [LoggerMessage(1, LogLevel.Warning, "Admin menghapus {Jumlah} perangkat desa '{Kode}'{Sasaran}.")]
    internal static partial void AdminHapusPerangkat(ILogger logger, int jumlah, string kode, string sasaran);

    [LoggerMessage(2, LogLevel.Warning, "Admin mengubah batas perangkat desa '{Kode}' menjadi {Batas}.")]
    internal static partial void AdminUbahBatas(ILogger logger, string kode, int batas);

    [LoggerMessage(3, LogLevel.Warning, "Admin mengubah data desa '{Kode}' menjadi '{Nama}'.")]
    internal static partial void AdminUbahDesa(ILogger logger, string kode, string nama);

    [LoggerMessage(4, LogLevel.Warning, "Admin MENGHAPUS PAKSA desa '{Kode}' ({Nama}) — ikut hilang {Tagihan} tagihan dan {Pembayaran} pembayaran. Konfirmasi eksplisit diberikan.")]
    internal static partial void AdminHapusPaksa(ILogger logger, string kode, string nama, int tagihan, int pembayaran);

    [LoggerMessage(5, LogLevel.Warning, "Admin MENGHAPUS desa '{Kode}' ({Nama}).")]
    internal static partial void AdminHapusDesa(ILogger logger, string kode, string nama);

    [LoggerMessage(6, LogLevel.Information, "Pendaftaran desa '{Kode}' dilewati: kode sudah ada.")]
    internal static partial void AdminPendaftaranDilewati(ILogger logger, string kode);

    [LoggerMessage(7, LogLevel.Warning, "Admin mendaftarkan desa '{Kode}' ({Nama}), batas {Batas} perangkat.")]
    internal static partial void AdminMendaftarkanDesa(ILogger logger, string kode, string nama, int batas);

    [LoggerMessage(8, LogLevel.Warning, "Aktivasi ditolak untuk {Id}: {Pesan}")]
    internal static partial void AktivasiDitolak(ILogger logger, string id, string? pesan);

    [LoggerMessage(9, LogLevel.Information, "Aktivasi diterima untuk {Id} ({Desa}), paket {Paket}, berlaku sampai {Sampai:O}.")]
    internal static partial void AktivasiDiterima(ILogger logger, string id, string desa, string paket, DateTime sampai);

    [LoggerMessage(10, LogLevel.Warning, "Trial ditolak untuk {Id}: {Pesan}")]
    internal static partial void TrialDitolakPerangkat(ILogger logger, string id, string? pesan);

    [LoggerMessage(11, LogLevel.Information, "Trial ditolak untuk {Id}: {Pesan}")]
    internal static partial void TrialDitolak(ILogger logger, string id, string? pesan);

    [LoggerMessage(12, LogLevel.Information, "Trial 14 hari aktif untuk {Id} ({Desa}), berlaku sampai {Sampai:O}.")]
    internal static partial void TrialAktif(ILogger logger, string id, string desa, DateTime sampai);

    [LoggerMessage(13, LogLevel.Error, "Penandaan lunas manual desa '{Kode}' gagal: tagihan {Order} tidak terbaca oleh perpanjangan.")]
    internal static partial void PembayaranManualGagal(ILogger logger, string kode, string order);

    [LoggerMessage(14, LogLevel.Warning, "Penandaan lunas desa '{Kode}' memakai nominal {Uang} — berbeda dari harga katalog paket '{Paket}' sebesar {Harga}. Selisih ini disengaja?")]
    internal static partial void PembayaranManualSelisih(ILogger logger, string kode, long uang, string paket, long harga);

    [LoggerMessage(15, LogLevel.Warning, "Admin menandai LUNAS desa '{Kode}' ({Nama}): paket {Paket} Rp{Uang}, {Hari} hari, order {Order}{Catatan}{Lampiran}.")]
    internal static partial void PembayaranManualLunas(ILogger logger, string kode, string nama, string paket, long uang, int hari, string order, string catatan, string lampiran);

    [LoggerMessage(16, LogLevel.Error, "Gateway gagal membuat tagihan {Order} ({Paket} Rp{Harga}) — tagihan tetap tercatat dan rekonsiliasi akan menyusul bila pembayarannya ternyata masuk.")]
    internal static partial void TagihanGatewayGagal(ILogger logger, string order, string paket, long harga);

    [LoggerMessage(17, LogLevel.Critical, "Gagal menyimpan audit admin durable untuk trace {TraceId}")]
    internal static partial void AuditAdminGagal(ILogger logger, Exception exception, string traceId);

    [LoggerMessage(18, LogLevel.Information, "Admin action {Outcome}: operator {OperatorId} ({OperatorName}), permission {Permission}, {Method} {Route}, HTTP {StatusCode}, trace {TraceId}")]
    internal static partial void AuditAdminTindakan(ILogger logger, string outcome, string operatorId, string operatorName, string permission, string method, string route, int statusCode, string traceId);

    [LoggerMessage(19, LogLevel.Information, "Skema, RLS, role, dan fungsi pencarian diterapkan lewat koneksi migrasi.")]
    internal static partial void SkemaDiterapkan(ILogger logger);

    [LoggerMessage(20, LogLevel.Information, "Tanpa SUPABASE_MIGRATE_PASSWORD: skema tidak diubah. Jalankan SuDesApp.Server/schema.sql sekali di SQL Editor bila tabel/RLS belum ada.")]
    internal static partial void SkemaTidakDiubah(ILogger logger);

    [LoggerMessage(21, LogLevel.Information, "Isolasi RLS terverifikasi: role '{Pengguna}' tidak istimewa, {Jumlah}/7 tabel ber-RLS, fungsi pencarian siap.")]
    internal static partial void RlsTerverifikasi(ILogger logger, string pengguna, long jumlah);

    [LoggerMessage(22, LogLevel.Warning, "Pelanggan {Id} menolak perangkat baru ({Jumlah} tercatat, batas {Batas}) — tidak dicatat.")]
    internal static partial void PerangkatBaruDitolak(ILogger logger, string id, int jumlah, int batas);

    [LoggerMessage(23, LogLevel.Information, "Webhook duplikat untuk order {Order} diabaikan (sudah pernah diproses).")]
    internal static partial void WebhookDuplikat(ILogger logger, string order);

    [LoggerMessage(24, LogLevel.Information, "Langganan {Id} diperpanjang {Hari} hari lewat order {Order}.")]
    internal static partial void LanggananDiperpanjang(ILogger logger, string id, int hari, string order);

    [LoggerMessage(25, LogLevel.Information, "Tabel pengumuman belum ada di Supabase (schema.sql belum dijalankan ulang); klien akan menerima daftar kosong.")]
    internal static partial void TabelPengumumanBelumAda(ILogger logger);

    [LoggerMessage(26, LogLevel.Warning, "Midtrans:EnabledPayments bukan array JSON valid: {V}")]
    internal static partial void MidtransEnabledPaymentsInvalid(ILogger logger, Exception exception, string v);

    [LoggerMessage(27, LogLevel.Error, "Midtrans Snap gagal (HTTP {S}): {B}")]
    internal static partial void MidtransSnapGagal(ILogger logger, int s, string b);

    [LoggerMessage(28, LogLevel.Warning, "Status Midtrans untuk {Order} gagal (HTTP {S}): {B}")]
    internal static partial void MidtransStatusGagal(ILogger logger, string order, int s, string b);

    [LoggerMessage(29, LogLevel.Warning, "Gagal menanyakan status Midtrans untuk {Order}.")]
    internal static partial void MidtransQueryGagal(ILogger logger, Exception exception, string order);

    [LoggerMessage(30, LogLevel.Warning, "Webhook ditolak: signature tidak valid untuk order {Order}.")]
    internal static partial void WebhookSignatureDitolak(ILogger logger, string order);

    [LoggerMessage(31, LogLevel.Warning, "[{Asal}] Order tak dikenal: {Order}")]
    internal static partial void WebhookOrderTakDikenal(ILogger logger, string asal, string order);

    [LoggerMessage(32, LogLevel.Warning, "[{Asal}] Nominal {Diterima} tidak cocok dengan tagihan {Diharapkan} (order {Order}).")]
    internal static partial void WebhookNominalTidakCocok(ILogger logger, string asal, string diterima, long diharapkan, string order);

    [LoggerMessage(33, LogLevel.Warning, "[{Asal}] Order {Order} status '{Status}' tetapi fraud_status '{Fraud}' — tidak memperpanjang.")]
    internal static partial void WebhookFraudBelumAman(ILogger logger, string asal, string order, string status, string fraud);

    [LoggerMessage(34, LogLevel.Information, "Kunci penandatangan lisensi dimuat dari {Path}")]
    internal static partial void KunciLisensiDimuat(ILogger logger, string path);

    [LoggerMessage(35, LogLevel.Warning, "Kunci penandatangan lisensi BARU dibuat di {Path}. Jaga berkas ini (privat, jangan di-commit) — menghapusnya membuat semua token lama tidak valid.")]
    internal static partial void KunciLisensiDibuat(ILogger logger, string path);

    [LoggerMessage(36, LogLevel.Warning, "Verifikasi token lisensi gagal (token rusak atau format salah)")]
    internal static partial void VerifikasiTokenGagal(ILogger logger, Exception exception);

    [LoggerMessage(37, LogLevel.Information, "Rekonsiliasi latar dimatikan (interval 0 menit).")]
    internal static partial void RekonsiliasiLatarMati(ILogger logger);

    [LoggerMessage(38, LogLevel.Information, "Rekonsiliasi latar aktif: tiap {Interval:N0} menit, umur tagihan {UmurMin:N0} menit sampai {UmurMaks:N0} hari, maksimum {Batas} per putaran.")]
    internal static partial void RekonsiliasiLatarAktif(ILogger logger, double interval, double umurMin, double umurMaks, int batas);

    [LoggerMessage(39, LogLevel.Error, "Putaran rekonsiliasi gagal; putaran berikutnya tetap dijadwalkan.")]
    internal static partial void RekonsiliasiPutaranGagal(ILogger logger, Exception exception);

    [LoggerMessage(40, LogLevel.Information, "Rekonsiliasi dilewati: ServerKey Midtrans belum diisi, status pembayaran tidak bisa ditanyakan.")]
    internal static partial void RekonsiliasiTanpaServerKey(ILogger logger);

    [LoggerMessage(41, LogLevel.Error, "Rekonsiliasi tidak bisa mengambil daftar tagihan.")]
    internal static partial void RekonsiliasiDaftarGagal(ILogger logger, Exception exception);

    [LoggerMessage(42, LogLevel.Information, "Rekonsiliasi: tidak ada tagihan menunggu pembayaran.")]
    internal static partial void RekonsiliasiTanpaTagihan(ILogger logger);

    [LoggerMessage(43, LogLevel.Information, "Rekonsiliasi: status order {Order} belum bisa dipastikan ({Pesan}).")]
    internal static partial void RekonsiliasiStatusBelumPasti(ILogger logger, string order, string? pesan);

    [LoggerMessage(44, LogLevel.Error, "Rekonsiliasi order {Order} gagal diproses.")]
    internal static partial void RekonsiliasiOrderGagal(ILogger logger, Exception exception, string order);

    [LoggerMessage(45, LogLevel.Information, "Rekonsiliasi selesai: {Diperiksa} diperiksa, {Diselesaikan} diselesaikan, {Sudah} sudah pernah diproses, {Belum} belum bayar, {Perlu} perlu diperiksa, {TidakPasti} tidak pasti.")]
    internal static partial void RekonsiliasiSelesai(ILogger logger, int diperiksa, int diselesaikan, int sudah, int belum, int perlu, int tidakPasti);

    [LoggerMessage(46, LogLevel.Error, "Gagal menyiapkan skema/isolasi RLS.")]
    internal static partial void PersiapanSkemaGagal(ILogger logger, Exception exception);

    [LoggerMessage(47, LogLevel.Warning, "Uji pembayaran: MIDTRANS_NOTIFICATION_URL belum diisi — notifikasi transaksi uji {Order} mengikuti 'Payment Notification URL' di dashboard akun Midtrans, bukan server ini.")]
    internal static partial void UjiTanpaUrlNotifikasi(ILogger logger, string order);

    [LoggerMessage(48, LogLevel.Information, "Uji pembayaran: transaksi {Order} dibuat untuk desa '{Kode}' (Rp{Nominal}, {Lingkungan}) — notifikasi ke {Url}.")]
    internal static partial void UjiTagihanDibuat(ILogger logger, string order, string kode, long nominal, string lingkungan, string? url);

    [LoggerMessage(49, LogLevel.Warning, "Uji pembayaran: URL notifikasi '{Url}' tidak bisa dipakai untuk order {Order} — header X-Override-Notification tidak dikirim, notifikasi mengikuti dashboard akun.")]
    internal static partial void MidtransNotifikasiUjiTidakValid(ILogger logger, string order, string url);

    [LoggerMessage(50, LogLevel.Information, "Notifikasi pembayaran masuk: order {Order}, status {StatusTransaksi}, signature {Signature}, HTTP {StatusBalasan} — {Pesan}")]
    internal static partial void WebhookMasuk(ILogger logger, string order, string? statusTransaksi, string signature, int statusBalasan, string? pesan);
}
