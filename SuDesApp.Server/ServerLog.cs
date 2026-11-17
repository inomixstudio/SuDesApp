using Microsoft.Extensions.Logging;

namespace SuDesApp.Server;

/// <summary>
/// Logger ber-event ID tetap untuk layanan server. Source generator membuat
/// delegate bertipe sehingga pemanggilan log tidak membuat array object dan
/// format string pada setiap request.
/// </summary>
internal static partial class ServerLog
{
/// <summary>Samarkan kredensial: hanya 4 karakter terakhir yang tampil di log.</summary>
internal static string Samarkan(string? nilai) =>
    string.IsNullOrEmpty(nilai) ? "****" : (nilai.Length <= 4 ? "****" : "****" + nilai[^4..]);
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

    [LoggerMessage(11, LogLevel.Warning, "Trial ditolak untuk {Id}: {Pesan}")]
    internal static partial void TrialDitolak(ILogger logger, string id, string? pesan);

    [LoggerMessage(12, LogLevel.Information, "Trial 30 hari aktif untuk {Id} ({Desa}), berlaku sampai {Sampai:O}.")]
    internal static partial void TrialAktif(ILogger logger, string id, string desa, DateTime sampai);

    [LoggerMessage(13, LogLevel.Error, "Penandaan lunas manual desa '{Kode}' gagal: tagihan {Order} tidak terbaca oleh perpanjangan.")]
    internal static partial void PembayaranManualGagal(ILogger logger, string kode, string order);

    [LoggerMessage(14, LogLevel.Warning, "Penandaan lunas desa '{Kode}' memakai nominal {Uang} — berbeda dari harga katalog paket '{Paket}' sebesar {Harga}. Selisih ini disengaja?")]
    internal static partial void PembayaranManualSelisih(ILogger logger, string kode, long uang, string paket, long harga);

    [LoggerMessage(15, LogLevel.Warning, "Admin menandai LUNAS desa '{Kode}' ({Nama}): paket {Paket} Rp{Uang}, {Hari} hari, order {Order}{Catatan}{Lampiran}.")]
    internal static partial void PembayaranManualLunas(ILogger logger, string kode, string nama, string paket, long uang, int hari, string order, string catatan, string lampiran);
    [LoggerMessage(42, LogLevel.Warning, "Berlangganan awal: desa '{Kode}' ({Nama}) didaftarkan dari jalur buat-tagihan — kode aktif setelah pembayaran lunas.")]
    internal static partial void TagihanBerlanggananAwal(ILogger logger, string kode, string nama);

    [LoggerMessage(43, LogLevel.Error, "Berlangganan awal gagal mendaftarkan desa '{Nama}'.")]
    internal static partial void TagihanBerlanggananAwalGagal(ILogger logger, Exception exception, string nama);

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

    [LoggerMessage(23, LogLevel.Information, "Pembayaran duplikat untuk order {Order} diabaikan (sudah pernah diproses).")]
    internal static partial void PembayaranDuplikat(ILogger logger, string order);

    [LoggerMessage(24, LogLevel.Information, "Langganan {Id} diperpanjang {Hari} hari lewat order {Order}.")]
    internal static partial void LanggananDiperpanjang(ILogger logger, string id, int hari, string order);

    [LoggerMessage(25, LogLevel.Information, "Tabel pengumuman belum ada di Supabase (schema.sql belum dijalankan ulang); klien akan menerima daftar kosong.")]
    internal static partial void TabelPengumumanBelumAda(ILogger logger);
    [LoggerMessage(34, LogLevel.Information, "Kunci penandatangan lisensi dimuat dari {Path}")]
    internal static partial void KunciLisensiDimuat(ILogger logger, string path);

    [LoggerMessage(35, LogLevel.Warning, "Kunci penandatangan lisensi BARU dibuat di {Path}. Jaga berkas ini (privat, jangan di-commit) — menghapusnya membuat semua token lama tidak valid.")]
    internal static partial void KunciLisensiDibuat(ILogger logger, string path);

    [LoggerMessage(36, LogLevel.Warning, "Verifikasi token lisensi gagal (token rusak atau format salah)")]
    internal static partial void VerifikasiTokenGagal(ILogger logger, Exception exception);
    [LoggerMessage(46, LogLevel.Error, "Gagal menyiapkan skema/isolasi RLS.")]
    internal static partial void PersiapanSkemaGagal(ILogger logger, Exception exception);
    [LoggerMessage(51, LogLevel.Information, "Penyimpanan lokal belum ada — berkas baru akan dibuat di {Jalur}.")]
    internal static partial void PenyimpananLokalBaru(ILogger logger, string jalur);

    [LoggerMessage(52, LogLevel.Information, "Penyimpanan lokal dibaca dari {Jalur}: {Desa} desa, {Pengumuman} pengumuman, {Audit} entri audit.")]
    internal static partial void PenyimpananLokalDibaca(ILogger logger, string jalur, int desa, int pengumuman, int audit);

    [LoggerMessage(53, LogLevel.Warning, "Berkas penyimpanan lokal tidak bisa dipakai ({Alasan}); berkas lama dipindahkan ke {Rusak} dan server mulai dari kosong.")]
    internal static partial void PenyimpananLokalRusak(ILogger logger, string alasan, string rusak);

    [LoggerMessage(54, LogLevel.Warning, "Berkas penyimpanan lokal tidak bisa dipindahkan dari {Jalur}.")]
    internal static partial void PenyimpananLokalGagalPindah(ILogger logger, Exception exception, string jalur);

    [LoggerMessage(55, LogLevel.Error, "Gagal menyimpan penyimpanan lokal ke {Jalur} — keadaan di memori tetap sah untuk sesi ini.")]
    internal static partial void PenyimpananLokalGagalSimpan(ILogger logger, Exception exception, string jalur);
}
