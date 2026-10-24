using System.Globalization;
using System.Net;
using System.Text;

namespace SuDesApp.Server;

/// <summary>
/// Data satu kuitansi pembayaran lisensi. <c>BerlakuSampai</c> adalah masa aktif
/// desa SAAT kuitansi dibuat — bukan periode yang dibeli oleh pembayaran ini,
/// karena tabel pembayaran tidak menyimpan rentang per pembayaran. Karena itu
/// nilainya diberi label apa adanya di kuitansi, bukan diklaim sebagai periode.
/// </summary>
public sealed record DataKuitansi(
    string OrderId,
    string NamaDesa,
    string KodeAktivasi,
    string? Paket,
    long Nominal,
    DateTime Dicatat,
    DateTime? BerlakuSampai,
    string? Catatan,
    string? BuktiLampiran = null);

/// <summary>
/// Kuitansi pembayaran lisensi sebagai HTML siap cetak (peramban → PDF).
///
/// Kenapa ada: desa membelanjakan uang APBDes untuk langganan ini dan bendahara
/// desa butuh bukti pengeluaran yang menyebut penerima, jumlah, dan tanggal.
/// Tanpa dokumen resmi, pembelian software sering tertahan di pertanggungjawaban
/// anggaran — bukan karena aplikasinya kurang, tetapi karena tidak ada buktinya.
///
/// Dibuat di server, bukan di aplikasi desa, supaya yang tercetak adalah data
/// yang tercatat di database pembayaran — bukan angka yang bisa diketik ulang.
/// Semua teks dinamis di-escape: nama desa dan catatan berasal dari input
/// operator, dan kuitansi ini dibuka di peramban.
/// </summary>
public static class KuitansiPembayaran
{
    /// <summary>Waktu Indonesia Barat (UTC+7). Kuitansi memakai WIB agar tanggalnya sama dengan hari pembayaran di desa.</summary>
    private static readonly TimeZoneInfo Wib =
        TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");

    private static readonly CultureInfo Indonesia = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Nomor kuitansi deterministik: KW/{tahun-bulan WIB}/{order id}.</summary>
    public static string Nomor(DataKuitansi data) =>
        $"KW/{WaktuLokal(data.Dicatat):yyyyMM}/{data.OrderId}";

    /// <summary>"18 Oktober 2026" menurut WIB.</summary>
    public static string Tanggal(DataKuitansi data) =>
        WaktuLokal(data.Dicatat).ToString("dd MMMM yyyy", Indonesia);

    /// <summary>Nominal rupiah dengan pemisah ribuan Indonesia, mis. "Rp 120.000".</summary>
    public static string Rupiah(long nominal) => "Rp " + nominal.ToString("N0", Indonesia);

    /// <summary>
    /// Jumlah dalam huruf — lazim di kuitansi Indonesia untuk mencegah angka
    /// diubah. Null/negatif tidak pernah muncul dari pembayaran, tetapi tetap
    /// ditangani supaya tidak melempar.
    /// </summary>
    public static string Terbilang(long nominal)
    {
        if (nominal < 0) return "minus " + Terbilang(-nominal);
        if (nominal == 0) return "nol rupiah";
        return Rapikan(Kata(nominal)) + " rupiah";
    }

    /// <summary>HTML kuitansi lengkap, siap dibuka di tab baru lalu dicetak/disimpan PDF.</summary>
    public static string Html(DataKuitansi data)
    {
        string nomor = Nomor(data);
        string paket = string.IsNullOrWhiteSpace(data.Paket) ? "langganan" : data.Paket.Trim();
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n<html lang=\"id\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>Kuitansi ").Append(E(nomor)).Append("</title>");
        html.Append(Gaya);
        html.Append("</head><body><main class=\"lembar\">");

        // ── Kepala ──────────────────────────────────────────────────
        html.Append("<header class=\"kepala\">");
        html.Append("<div><h1>Kuitansi Pembayaran</h1>");
        html.Append("<p class=\"nomor\">No. ").Append(E(nomor)).Append("</p></div>");
        html.Append("<div class=\"penerbit\"><strong>Sumberjaya Dev</strong><br>");
        html.Append("<span>Unit pengembangan perangkat lunak PT Duta Publik</span><br>");
        html.Append("<span>SuDesApp — Sistem Surat Desa</span></div>");
        html.Append("</header>");

        // ── Pihak ───────────────────────────────────────────────────
        html.Append("<p class=\"awalan\">Sudah diterima dari</p>");
        html.Append("<table class=\"pihak\">");
        Baris(html, "Nama desa", E(string.IsNullOrWhiteSpace(data.NamaDesa) ? "(tanpa nama)" : data.NamaDesa.Trim()));
        Baris(html, "Kode aktivasi", "<span class=\"mono\">" + E(data.KodeAktivasi) + "</span>");
        Baris(html, "Tanggal", E(Tanggal(data)) + " <span class=\"kecil\">(WIB)</span>");
        html.Append("</table>");

        // ── Jumlah ──────────────────────────────────────────────────
        html.Append("<p class=\"awalan\">Sejumlah</p>");
        html.Append("<p class=\"nominal\">").Append(E(Rupiah(data.Nominal))).Append("<span class=\"lunas\">LUNAS</span></p>");
        html.Append("<p class=\"terbilang\">Terbilang: <em>").Append(E(Terbilang(data.Nominal))).Append("</em></p>");

        // ── Rincian ─────────────────────────────────────────────────
        html.Append("<table class=\"rincian\"><thead><tr><th>Uraian</th><th class=\"kanan\">Jumlah</th></tr></thead><tbody>");
        html.Append("<tr><td>Langganan SuDesApp — paket ").Append(E(paket)).Append("</td>");
        html.Append("<td class=\"kanan\">").Append(E(Rupiah(data.Nominal))).Append("</td></tr>");
        html.Append("</tbody><tfoot><tr><th>Total dibayar</th><th class=\"kanan\">")
            .Append(E(Rupiah(data.Nominal))).Append("</th></tr></tfoot></table>");

        // ── Keterangan ──────────────────────────────────────────────
        html.Append("<table class=\"keterangan\">");
        Baris(html, "Order ID", "<span class=\"mono\">" + E(data.OrderId) + "</span>");
        Baris(html, "Status", "Lunas — tercatat di riwayat pembayaran lisensi");

        if (!string.IsNullOrWhiteSpace(data.Catatan))
        {
            Baris(html, "Referensi bayar", E(data.Catatan.Trim()));
        }

        // Lampiran bukti transfer (gambar) hanya disebutkan — gambarnya tidak
        // ditanam di dokumen ini supaya kuitansi tetap ringan dibuka & dicetak;
        // yang menyimpannya adalah pusat administer (token admin).
        if (!string.IsNullOrWhiteSpace(data.BuktiLampiran))
        {
            Baris(html, "Bukti transfer", E(data.BuktiLampiran.Trim()) +
                " <span class=\"kecil\">(lampiran gambar tersimpan di pusat administer)</span>");
        }

        if (data.BerlakuSampai is { } sampai)
        {
            // Sengaja "saat kuitansi dibuat", bukan "periode yang dibeli": tabel
            // pembayaran tidak menyimpan rentang per pembayaran, dan mengarang
            // rentang di dokumen resmi justru menyesatkan bendahara desa.
            Baris(html, "Masa aktif saat ini", E(Date(sampai)) + " <span class=\"kecil\">(WIB)</span>");
        }

        html.Append("</table>");

        // ── Kaki ────────────────────────────────────────────────────
        html.Append("<footer class=\"kaki\">");
        html.Append("<p>Kuitansi ini dibuat otomatis dari catatan pembayaran server lisensi SuDesApp. ");
        html.Append("Keasliannya diperiksa dengan mencocokkan Order ID di atas pada riwayat pembayaran desa ");
        html.Append("di pusat administer.</p>");
        html.Append("<p class=\"kecil\">Dicetak dari SuDesApp — bukan bukti setor bank; bukti setor tetap dari bank/e-wallet pembayar.</p>");
        html.Append("</footer>");

        html.Append("</main>");
        html.Append("<button class=\"cetak\" type=\"button\" onclick=\"window.print()\">Cetak / simpan PDF</button>");
        html.Append("</body></html>\n");
        return html.ToString();
    }

    // ── Bantuan ──────────────────────────────────────────────────────

    private static string E(string? teks) => WebUtility.HtmlEncode(teks ?? string.Empty);

    private static void Baris(StringBuilder html, string label, string nilaiHtml) =>
        html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(nilaiHtml).Append("</td></tr>");

    private static DateTime WaktuLokal(DateTime waktu) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            waktu.Kind == DateTimeKind.Utc ? waktu : DateTime.SpecifyKind(waktu, DateTimeKind.Utc), Wib);

    private static string Date(DateTime waktu) => WaktuLokal(waktu).ToString("dd MMMM yyyy", Indonesia);

    private static readonly string[] Satuan =
    {
        "", "satu", "dua", "tiga", "empat", "lima", "enam", "tujuh", "delapan", "sembilan", "sepuluh", "sebelas"
    };

    private static string Kata(long n)
    {
        if (n < 12) return Satuan[n];
        if (n < 20) return Kata(n - 10) + " belas";
        if (n < 100) return Kata(n / 10) + " puluh " + Kata(n % 10);
        if (n < 200) return "seratus " + Kata(n - 100);
        if (n < 1_000) return Kata(n / 100) + " ratus " + Kata(n % 100);
        if (n < 2_000) return "seribu " + Kata(n - 1_000);
        if (n < 1_000_000) return Kata(n / 1_000) + " ribu " + Kata(n % 1_000);
        if (n < 1_000_000_000) return Kata(n / 1_000_000) + " juta " + Kata(n % 1_000_000);
        if (n < 1_000_000_000_000) return Kata(n / 1_000_000_000) + " miliar " + Kata(n % 1_000_000_000);
        return Kata(n / 1_000_000_000_000) + " triliun " + Kata(n % 1_000_000_000_000);
    }

    private static string Rapikan(string teks) =>
        string.Join(' ', teks.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private const string Gaya = """
        <style>
          :root { color-scheme: light; }
          * { box-sizing: border-box; }
          body { margin: 0; padding: 32px 20px 64px; background: #f2f5f3; color: #1f2a24;
                 font-family: "Segoe UI", Arial, sans-serif; font-size: 14px; }
          .lembar { max-width: 186mm; margin: 0 auto; background: #fff; border: 1px solid #d7e0da;
                    border-radius: 10px; padding: 28px 32px; }
          .kepala { display: flex; justify-content: space-between; gap: 24px; align-items: flex-start;
                    border-bottom: 2px solid #1f6f4a; padding-bottom: 16px; }
          h1 { margin: 0; font-size: 22px; letter-spacing: .04em; text-transform: uppercase; }
          .nomor { margin: 6px 0 0; font-family: Consolas, monospace; font-size: 13px; color: #46564e; }
          .penerbit { text-align: right; font-size: 12.5px; line-height: 1.5; color: #33443b; }
          .penerbit strong { font-size: 14px; color: #1f2a24; }
          .awalan { margin: 20px 0 4px; font-size: 12px; text-transform: uppercase;
                    letter-spacing: .08em; color: #5b6b62; }
          table { width: 100%; border-collapse: collapse; }
          .pihak th, .keterangan th { width: 38%; text-align: left; font-weight: 600; color: #33443b;
                                      padding: 6px 0; vertical-align: top; }
          .pihak td, .keterangan td { padding: 6px 0; }
          .mono { font-family: Consolas, monospace; }
          .kecil { font-size: 11.5px; color: #6b7a72; }
          .nominal { margin: 0; font-size: 26px; font-weight: 700; color: #14532d;
                     display: flex; align-items: center; gap: 14px; }
          .lunas { font-size: 11px; font-weight: 700; letter-spacing: .12em; color: #14532d;
                   border: 2px solid #14532d; border-radius: 4px; padding: 3px 8px; transform: rotate(-4deg); }
          .terbilang { margin: 6px 0 18px; color: #46564e; }
          .rincian th, .rincian td { border-bottom: 1px solid #e2e9e5; padding: 9px 0; text-align: left; }
          .rincian thead th { font-size: 12px; text-transform: uppercase; letter-spacing: .06em; color: #5b6b62; }
          .rincian tfoot th { border-bottom: 0; border-top: 2px solid #1f6f4a; padding-top: 10px; font-size: 15px; }
          .kanan { text-align: right !important; white-space: nowrap; }
          .keterangan { margin-top: 18px; }
          .kaki { margin-top: 24px; border-top: 1px dashed #c9d5ce; padding-top: 12px;
                  font-size: 11.5px; color: #5b6b62; }
          .kaki p { margin: 0 0 6px; }
          .cetak { position: fixed; right: 24px; bottom: 24px; padding: 12px 20px; border: 0;
                   border-radius: 999px; background: #1f6f4a; color: #fff; font: inherit;
                   font-weight: 600; cursor: pointer; box-shadow: 0 6px 18px rgba(0,0,0,.18); }
          .cetak:hover { background: #17573a; }
          @page { size: A4; margin: 16mm; }
          @media print {
            body { background: #fff; padding: 0; }
            .lembar { max-width: none; border: 0; border-radius: 0; padding: 0; }
            .cetak { display: none; }
          }
        </style>
        """;
}
