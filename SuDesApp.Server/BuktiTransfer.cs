using System.Text;

namespace SuDesApp.Server;

/// <summary>Hasil pembacaan lampiran bukti transfer dari isi permintaan admin.</summary>
public sealed record HasilBacaBukti(BuktiBayar? Bukti, string? Pesan)
{
    /// <summary>True bila lampiran sah, ATAU bila memang tidak ada lampiran.</summary>
    public bool Diterima => Pesan is null;

    public static HasilBacaBukti Kosong => new(null, null);
}

/// <summary>
/// Pembaca & penilai lampiran bukti transfer untuk pembukuan manual.
///
/// Kenapa dipisah dari layanan dan endpoint: inilah satu-satunya pintu masuk
/// berkas MENTAH dari luar ke dalam tabel <c>pembayaran</c> — jadi seluruh
/// penilaiannya harus bisa diuji tanpa HTTP maupun database.
///
/// Aturan yang dijaga:
/// <list type="number">
/// <item>jenis berkas ditentukan dari ISI (magic bytes), bukan dari kata
/// pengirim: berkas yang mengaku <c>image/png</c> tapi isinya PDF tetap
/// ditolak, dan sebaliknya gambar asli diterima walau tipenya tidak disebut;</item>
/// <item>hanya JPEG, PNG, dan WEBP — tiga jenis yang bisa ditampilkan peramban
/// tanpa risiko skrip (SVG sengaja TIDAK diterima karena ia bisa memuat
/// JavaScript);</item>
/// <item>ukuran dibatasi <see cref="MaksUkuran"/>; berkas dari ponsel sering
/// jauh lebih besar dari itu, jadi halaman admin mengecilkannya lebih dulu;</item>
/// <item>nama berkas hanya untuk tampilan: jalur dibuang, karakter kendali
/// dipotong, dan panjangnya dibatasi.</item>
/// </list>
/// </summary>
public static class BuktiTransfer
{
    /// <summary>Batas ukuran gambar yang disimpan (2 MB).</summary>
    public const int MaksUkuran = 2 * 1024 * 1024;

    /// <summary>Jenis gambar yang diterima — semuanya bisa ditampilkan peramban.</summary>
    public static readonly IReadOnlyList<string> TipeDiizinkan =
        new[] { "image/jpeg", "image/png", "image/webp" };

    /// <summary>
    /// Baca lampiran dari teks base64 (boleh berupa data URL
    /// <c>data:image/png;base64,…</c> hasil pembaca berkas di peramban).
    ///
    /// Teks kosong/null berarti operator memang tidak melampirkan apa pun —
    /// itu BUKAN galat: pembukuan manual dengan catatan teks saja tetap sah
    /// seperti sebelumnya.
    /// </summary>
    public static HasilBacaBukti Baca(string? teksBase64, string? namaBerkas)
    {
        var teks = (teksBase64 ?? string.Empty).Trim();
        if (teks.Length == 0)
        {
            return HasilBacaBukti.Kosong;
        }

        if (teks.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int pisah = teks.IndexOf(',');
            if (pisah < 0)
            {
                return Tolak("Lampiran bukti tidak terbaca: data URL tidak memuat isi.");
            }

            if (!teks[..pisah].Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                return Tolak("Lampiran bukti tidak terbaca: hanya data URL base64 yang didukung.");
            }

            teks = teks[(pisah + 1)..].Trim();
        }

        byte[] isi;
        try
        {
            isi = Convert.FromBase64String(teks);
        }
        catch (FormatException)
        {
            return Tolak("Lampiran bukti tidak terbaca: data base64 tidak sah.");
        }

        if (isi.Length == 0)
        {
            return Tolak("Lampiran bukti kosong (0 byte).");
        }

        if (isi.Length > MaksUkuran)
        {
            return Tolak(
                $"Lampiran bukti terlalu besar ({UkuranTeks(isi.Length)}). Maksimum {UkuranTeks(MaksUkuran)}.");
        }

        var tipe = TipeGambar(isi);
        if (tipe is null)
        {
            return Tolak("Lampiran bukti harus gambar JPEG, PNG, atau WEBP.");
        }

        return new HasilBacaBukti(new BuktiBayar(isi, tipe, RapikanNama(namaBerkas)), null);
    }

    /// <summary>
    /// Nama berkas untuk tampilan saja: jalur dibuang (peramban boleh mengirim
    /// nama lengkap), karakter kendali dihilangkan, dan dipotong 120 huruf.
    /// </summary>
    public static string? RapikanNama(string? nama)
    {
        if (string.IsNullOrWhiteSpace(nama))
        {
            return null;
        }

        var bersih = new string(nama.Where(c => !char.IsControl(c)).ToArray()).Trim();

        int pisah = Math.Max(bersih.LastIndexOf('/'), bersih.LastIndexOf('\\'));
        if (pisah >= 0)
        {
            bersih = bersih[(pisah + 1)..].Trim();
        }

        if (bersih.Length == 0)
        {
            return null;
        }

        return bersih.Length > 120 ? bersih[..120].TrimEnd() : bersih;
    }

    /// <summary>Jenis gambar menurut magic bytes-nya, atau null bila bukan ketiga jenis itu.</summary>
    public static string? TipeGambar(byte[] isi)
    {
        if (isi is null || isi.Length < 4)
        {
            return null;
        }

        if (isi[0] == 0xFF && isi[1] == 0xD8 && isi[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (isi[0] == 0x89 && isi[1] == 0x50 && isi[2] == 0x4E && isi[3] == 0x47)
        {
            return "image/png";
        }

        if (isi.Length >= 12 &&
            isi[0] == (byte)'R' && isi[1] == (byte)'I' && isi[2] == (byte)'F' && isi[3] == (byte)'F' &&
            isi[8] == (byte)'W' && isi[9] == (byte)'E' && isi[10] == (byte)'B' && isi[11] == (byte)'P')
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>Nama jenis untuk dibaca manusia: "JPEG", "PNG", "WEBP".</summary>
    public static string LabelTipe(string? tipe) => tipe switch
    {
        "image/jpeg" => "JPEG",
        "image/png" => "PNG",
        "image/webp" => "WEBP",
        _ => "gambar",
    };

    /// <summary>Ukuran dalam bentuk yang enak dibaca: "240 KB", "1,4 MB".</summary>
    public static string UkuranTeks(int ukuran)
    {
        if (ukuran >= 1024 * 1024)
        {
            return (ukuran / 1024.0 / 1024.0).ToString("0.#",
                System.Globalization.CultureInfo.GetCultureInfo("id-ID")) + " MB";
        }

        return Math.Max(1, (int)Math.Round(ukuran / 1024.0)) + " KB";
    }

    /// <summary>Ringkasan dari lampiran yang baru dibaca, mis. "JPEG · 240 KB".</summary>
    public static string Ringkas(BuktiBayar? bukti) =>
        bukti is null ? "tanpa lampiran" : $"{LabelTipe(bukti.Tipe)} · {UkuranTeks(bukti.Isi.Length)}";

    /// <summary>Ringkasan dari metadata pembayaran (tanpa membaca gambarnya).</summary>
    public static string? Ringkas(bool adaBukti, string? tipe, int ukuran) =>
        adaBukti ? $"{LabelTipe(tipe)} · {UkuranTeks(ukuran)}" : null;

    /// <summary>Nama berkas unduhan; dipakai saat gambar disajikan ke peramban.</summary>
    public static string NamaUnduhan(string? tipe, string orderId) =>
        $"bukti-{orderId}.{Ekstensi(tipe)}";

    /// <summary>Ekstensi berkas menurut jenis gambarnya.</summary>
    public static string Ekstensi(string? tipe) => tipe switch
    {
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "jpg",
    };

    /// <summary>Teks yang aman disisipkan ke header Content-Disposition (tanpa kutip &amp; kendali).</summary>
    public static string NamaHeader(string nama) =>
        new string(nama.Where(c => !char.IsControl(c) && c is not ('"' or '\\')).ToArray());

    private static HasilBacaBukti Tolak(string pesan) => new(null, pesan);
}
