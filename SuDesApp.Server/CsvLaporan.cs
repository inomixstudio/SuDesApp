using System.Text;
using System.Globalization;

namespace SuDesApp.Server;

/// <summary>
/// Pembuat berkas CSV laporan untuk pusat administer (daftar desa & riwayat
/// pembayaran).
///
/// DIPISAH dari lambda endpoint karena dua alasan:
///  1. Bisa diuji tanpa HTTP server maupun database.
///  2. Bentuk berkasnya harus sama dengan yang dihasilkan halaman <c>/admin</c>
///     (lihat <c>csvSemuaDesa</c>/<c>csvPembayaran</c> di <c>admin.html</c>).
///     Kalau keduanya punya aturan sendiri, laporan dari CLI dan dari peramban
///     bisa berbeda kolom tanpa ada yang sadar.
///
/// UTF-8 dengan BOM sengaja dipakai: tanpa BOM, Excel (bahasa Indonesia) salah
/// membaca huruf beraksen dalam nama desa.
/// </summary>
public static class CsvLaporan
{
    /// <summary>
    /// Batas hari untuk label "segera habis". Sama dengan bilangan 30 di
    /// <c>admin.html</c> — kalau salah satu berubah, ubah keduanya.
    /// </summary>
    public const int BatasHariSegeraHabis = 30;

    /// <summary>
    /// Label status langganan menurut sisa hari: sama seperti halaman
    /// <c>/admin</c>. Bila belum pernah bayar, <c>"belum bayar"</c>.
    /// </summary>
    public static string StatusLangganan(DateTime? berlakuSampai, DateTime sekarang)
    {
        if (berlakuSampai is not DateTime sampai) return "belum bayar";

        var hari = (int)Math.Ceiling((sampai - sekarang).TotalDays);
        if (hari < 0) return "sudah habis";
        if (hari <= BatasHariSegeraHabis) return $"segera habis · {hari}h";
        return $"aktif · {hari}h";
    }

    /// <summary>Daftar desa sebagai teks CSV (dengan baris header).</summary>
    public static string DaftarDesa(IEnumerable<RingkasPelanggan> daftar, DateTime sekarang)
    {
        var baris = new List<string>
        {
            BarisCsv("kode_aktivasi", "nama_desa", "paket", "max_perangkat",
                     "jumlah_perangkat", "berlaku_sampai", "status")
        };

        foreach (var d in daftar)
        {
            baris.Add(BarisCsv(
                d.KodeAktivasi,
                d.NamaDesa,
                d.Paket,
                d.MaxPerangkat.ToString(CultureInfo.InvariantCulture),
                d.JumlahPerangkat.ToString(CultureInfo.InvariantCulture),
                Waktu(d.BerlakuSampai),
                StatusLangganan(d.BerlakuSampai, sekarang)));
        }

        return string.Join("\r\n", baris);
    }

    /// <summary>
    /// Riwayat pembayaran sebagai teks CSV (dengan baris header). Kolom
    /// <c>bukti</c> ada di PALING AKHIR dan harus sama urutannya dengan
    /// <c>csvPembayaran</c> di <c>admin.html</c>: laporan dari CLI dan dari
    /// peramban tidak boleh berbeda bentuk. <c>catatan</c> kosong pada pembayaran
    /// gateway (yang buktinya notifikasi Midtrans), terisi pada pembukuan manual;
    /// <c>bukti</c> hanya terisi bila operator melampirkan gambar bukti transfer —
    /// gambarnya sendiri tidak ikut ke CSV, hanya keterangan jenis &amp; ukurannya
    /// (mis. "JPEG · 240 KB") supaya terlihat baris mana yang punya lampiran.
    /// </summary>
    public static string Pembayaran(IEnumerable<InfoPembayaran> pembayaran)
    {
        var baris = new List<string>
        {
            BarisCsv("order_id", "paket", "nominal", "status", "dicatat", "catatan", "bukti")
        };

        foreach (var p in pembayaran)
        {
            baris.Add(BarisCsv(
                p.OrderId,
                p.Paket,
                p.Nominal.ToString(CultureInfo.InvariantCulture),
                p.Status,
                p.Dicatat.ToString("O", CultureInfo.InvariantCulture),
                p.Catatan,
                BuktiTransfer.Ringkas(p.AdaBukti, p.TipeBukti, p.UkuranBukti)));
        }

        return string.Join("\r\n", baris);
    }

    /// <summary>
    /// UTF-8 berserta BOM, bentuk yang siap ditulis sebagai berkas unduhan.
    /// </summary>
    public static byte[] JadiBerkasUtf8(string isi) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(isi)];

    /// <summary>Satu baris CSV: tiap nilai dikutip bila perlu, dipisah koma.</summary>
    /// <remarks>
    /// Nilai yang memuat koma, kutip, CR, atau LF WAJIB dikutip dan kutip di
    /// dalamnya digandakan — kalau tidak, nama desa yang memuat koma akan
    /// memecah kolom dan merusak seluruh laporan.
    /// </remarks>
    private static string BarisCsv(params string?[] nilai) =>
        string.Join(",", nilai.Select(KutipBilaPerlu));

    private static string KutipBilaPerlu(string? nilai)
    {
        if (nilai is null) return "";

        // Excel and similar spreadsheet apps evaluate formula-leading cells
        // even when they are correctly CSV-quoted. Prefix an apostrophe before
        // untrusted text so an exported village name or payment note cannot run
        // a formula when an operator opens the report.
        var pertama = nilai.AsSpan().TrimStart();
        if (!pertama.IsEmpty && pertama[0] is '=' or '+' or '-' or '@')
            nilai = "'" + nilai;
        else if (nilai.Length > 0 && nilai[0] is '\t' or '\r')
            nilai = "'" + nilai;

        return nilai.Any(c => c is ',' or '"' or '\r' or '\n')
            ? "\"" + nilai.Replace("\"", "\"\"") + "\""
            : nilai;
    }

    /// <summary>Waktu dalam bentuk ISO-8601 (sama dengan JSON), atau kosong.</summary>
    private static string Waktu(DateTime? nilai) => nilai?.ToString("O", CultureInfo.InvariantCulture) ?? "";
}
