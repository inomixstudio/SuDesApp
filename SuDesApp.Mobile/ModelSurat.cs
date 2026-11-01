using System.Globalization;
using System.Text;

namespace SuDesApp.Mobile;

/// <summary>Kelompok menu yang tersedia di aplikasi Android.</summary>
public enum KelompokMenu
{
    /// <summary>Menu "Buat Surat Baru": surat keterangan/pengantar desa.</summary>
    SuratBaru,

    /// <summary>Menu "NTCR": blanko N1-N6 dan N8.</summary>
    Ntcr,

    /// <summary>Menu "Formulir": contoh formulir surat desa (padanan contoh template desktop).</summary>
    Formulir
}

/// <summary>Satu kolom isian pada formulir surat.</summary>
public sealed class KolomIsian
{
    public string Kunci { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>teks | paragraf | tanggal | angka | pilihan</summary>
    public string Tipe { get; set; } = "teks";

    public bool Wajib { get; set; }

    public List<string> Pilihan { get; set; } = new();

    public string Bawaan { get; set; } = string.Empty;
}

/// <summary>Satu jenis surat yang bisa dibuat dari ponsel.</summary>
public sealed class DefinisiSurat
{
    public string Kode { get; set; } = string.Empty;

    public string Nama { get; set; } = string.Empty;

    public string Kategori { get; set; } = string.Empty;

    public string Ringkasan { get; set; } = string.Empty;

    public KelompokMenu Kelompok { get; set; } = KelompokMenu.SuratBaru;

    /// <summary>Judul yang tercetak (kapital, bergaris bawah).</summary>
    public string Judul { get; set; } = string.Empty;

    /// <summary>Awalan nomor surat, mis. 470.</summary>
    public string AwalanNomor { get; set; } = "470";

    public List<string> Pembuka { get; set; } = new();

    public List<KolomIsian> Kolom { get; set; } = new();

    public List<string> Penutup { get; set; } = new();

    public bool TandaTangan { get; set; } = true;

    /// <summary>Cetak kop desa. Surat kuasa tidak memakai kop karena ditandatangani warga.</summary>
    public bool PakaiKop { get; set; } = true;

    /// <summary>Jabatan penanda tangan; kosong berarti Kepala Desa.</summary>
    public string JabatanPenandatangan { get; set; } = string.Empty;

    /// <summary>
    /// Kunci kolom yang namanya dicetak pada blok tanda tangan, mis. surat kuasa
    /// ("nama-pemberi"). Kosong berarti nama Kepala Desa dari pengaturan.
    /// </summary>
    public string PenandatanganDariKunci { get; set; } = string.Empty;

    public string LabelNomor => $"Nomor {AwalanNomor}";
}

/// <summary>Identitas desa untuk kop surat dan blok tanda tangan.</summary>
public sealed class ProfilDesa
{
    public string Kabupaten { get; set; } = string.Empty;

    public string Kecamatan { get; set; } = string.Empty;

    public string NamaDesa { get; set; } = string.Empty;

    public string Alamat { get; set; } = string.Empty;

    public string Kodepos { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NamaKepalaDesa { get; set; } = string.Empty;

    /// <summary>Lengkap bila kop bisa dicetak tanpa penanda kurung siku.</summary>
    public bool Lengkap =>
        !string.IsNullOrWhiteSpace(Kabupaten) &&
        !string.IsNullOrWhiteSpace(Kecamatan) &&
        !string.IsNullOrWhiteSpace(NamaDesa) &&
        !string.IsNullOrWhiteSpace(NamaKepalaDesa);
}

/// <summary>Satu surat yang sudah dibuat, disimpan di ponsel untuk dicetak ulang.</summary>
public sealed class SuratTersimpan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Waktu { get; set; } = DateTime.Now;

    public string Kode { get; set; } = string.Empty;

    public string NamaJenis { get; set; } = string.Empty;

    public KelompokMenu Kelompok { get; set; }

    public string Judul { get; set; } = string.Empty;

    public string Nomor { get; set; } = string.Empty;

    public Dictionary<string, string> Isian { get; set; } = new();

    /// <summary>Isi bebas untuk menu "Buat Surat Sendiri".</summary>
    public List<string> ParagrafBebas { get; set; } = new();

    public string RingkasanPemohon =>
        Isian.TryGetValue("nama", out var nama) && !string.IsNullOrWhiteSpace(nama)
            ? nama.Trim()
            : (ParagrafBebas.Count > 0 ? ParagrafBebas[0] : string.Empty);
}

/// <summary>Pembantu format tanggal dan teks surat.</summary>
public static class FormatSurat
{
    private static readonly CultureInfo Budaya = new("id-ID");

    public static string TanggalPanjang(DateTime tanggal) => tanggal.ToString("dd MMMM yyyy", Budaya);

    public static string TanggalPendek(DateTime tanggal) => tanggal.ToString("dd-MM-yyyy", Budaya);

    /// <summary>Ubah isian tanggal (yyyy-MM-dd dari input HTML) menjadi tanggal.</summary>
    public static DateTime? BacaTanggal(string? teks)
    {
        if (string.IsNullOrWhiteSpace(teks)) return null;

        string nilai = teks.Trim();
        string[] pola = { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d MMMM yyyy" };
        foreach (var p in pola)
        {
            if (DateTime.TryParseExact(nilai, p, Budaya, DateTimeStyles.None, out var hasil))
            {
                return hasil;
            }
        }

        return DateTime.TryParse(nilai, Budaya, DateTimeStyles.None, out var bebas) ? bebas : null;
    }

    /// <summary>Rapikan teks isian: buang spasi tepi dan spasi ganda.</summary>
    public static string Bersih(string? teks)
    {
        if (string.IsNullOrWhiteSpace(teks)) return string.Empty;
        return string.Join(' ', teks.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public static string Kapital(string? teks)
    {
        string bersih = Bersih(teks);
        return bersih.ToUpperInvariant();
    }

    /// <summary>Nama berkas aman dari nomor/judul surat.</summary>
    public static string NamaBerkas(string? teks)
    {
        string dasar = string.IsNullOrWhiteSpace(teks) ? "surat" : teks.Trim();
        var hasil = new StringBuilder();
        foreach (char huruf in dasar)
        {
            hasil.Append(char.IsLetterOrDigit(huruf) ? huruf : '-');
        }

        string rapi = hasil.ToString().Trim('-');
        while (rapi.Contains("--", StringComparison.Ordinal))
        {
            rapi = rapi.Replace("--", "-", StringComparison.Ordinal);
        }

        return rapi.Length == 0 ? "surat" : rapi.ToLowerInvariant();
    }
}
