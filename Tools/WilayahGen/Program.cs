using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// Pembuat & pemeriksa berkas data wilayah pada folder Data/Wilayah.
//
// Data wilayah administratif Indonesia (provinsi → kabupaten/kota → kecamatan →
// desa/kelurahan + kode pos) dibundel aplikasi supaya kantor desa bisa memilih
// wilayah tanpa internet. Sumbernya dua dump SQL MySQL milik cahyadsn (MIT):
//
//   db/wilayah.sql         — hierarki kode wilayah (acuan Kepmendagri)
//   db/wilayah_kodepos.sql — kode pos per desa/kelurahan
//
// Cara pakai (dotnet run dengan proyek ini):
//
//   (tanpa argumen)  unduh sumber bila belum ada, lalu tulis
//                    Data/Wilayah/wilayah.tsv.gz
//   cek / --cek      periksa berkas terbundel: struktur, jumlah per tingkat, dan
//                    bila sumber tersedia — hasil bangun ulang harus identik.
//                    Dipakai Tools/check-rilis.ps1 (tanpa jaringan).
//
// Berkas keluaran sengaja DETERMINISTIK: headernya tidak memuat waktu pembuatan
// melainkan tanggal dari berkas sumber, sehingga menjalankan ulang alat ini tanpa
// perubahan sumber menghasilkan berkas yang identik — tidak ada tambalan palsu
// hanya karena alatnya dijalankan lagi.

string? akar = CariAkar();
if (akar == null)
{
    Console.WriteLine("GAGAL: SuDesApp.slnx tidak ditemukan dari " + AppContext.BaseDirectory);
    return 2;
}

string folderSumber = Path.Combine(akar, "artifacts", "wilayah-sumber");
string berkasKeluaran = Path.Combine(akar, "Data", "Wilayah", "wilayah.tsv.gz");

return ApakahCek(args)
    ? Cek(berkasKeluaran, folderSumber)
    : Bangun(folderSumber, berkasKeluaran);

// =====================================================================
// Bangun: unduh sumber → konversi → tulis berkas terbundel
// =====================================================================

static int Bangun(string folderSumber, string berkasKeluaran)
{
    Directory.CreateDirectory(folderSumber);

    string berkasWilayah = Path.Combine(folderSumber, "wilayah.sql");
    string berkasKodepos = Path.Combine(folderSumber, "wilayah_kodepos.sql");

    int siap = SiapkanSumber(
        berkasWilayah,
        "https://raw.githubusercontent.com/cahyadsn/wilayah/master/db/wilayah.sql",
        "kode wilayah (provinsi s.d. desa/kelurahan)");
    if (siap != 0)
    {
        return siap;
    }

    siap = SiapkanSumber(
        berkasKodepos,
        "https://raw.githubusercontent.com/cahyadsn/wilayah_kodepos/main/db/wilayah_kodepos.sql",
        "kode pos per desa/kelurahan");
    if (siap != 0)
    {
        return siap;
    }

    var masalah = new List<string>();
    var wilayah = BacaWilayah(berkasWilayah, masalah);
    if (wilayah.Count == 0)
    {
        Console.WriteLine("GAGAL: tidak ada baris wilayah terbaca dari " + berkasWilayah);
        return 2;
    }

    var kodepos = BacaKodepos(berkasKodepos);
    var keluaran = SusunKeluaran(wilayah, kodepos, BacaMeta(berkasWilayah), BacaMeta(berkasKodepos), masalah);

    Console.WriteLine(Ringkasan(keluaran.Jumlah));
    foreach (string m in masalah)
    {
        Console.WriteLine("PERINGATAN: " + m);
    }

    byte[] baru = TulisGz(keluaran.Isi);
    byte[]? lama = File.Exists(berkasKeluaran) ? File.ReadAllBytes(berkasKeluaran) : null;
    if (lama != null && lama.AsSpan().SequenceEqual(baru))
    {
        Console.WriteLine("tidak berubah: " + Relatif(berkasKeluaran));
        return 0;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(berkasKeluaran)!);
    File.WriteAllBytes(berkasKeluaran, baru);
    Console.WriteLine("tulis  " + Relatif(berkasKeluaran) + " (" + baru.Length + " byte)");
    return 0;
}

static string Ringkasan(JumlahWilayah j) =>
    $"baris: {j.Total} — {j.Provinsi} provinsi, {j.Kabupaten + j.Kota} kabupaten/kota, " +
    $"{j.Kecamatan} kecamatan, {j.Desa + j.Kelurahan} desa/kelurahan, {j.Berkodepos} ber-kode pos";

// =====================================================================
// Cek: berkas terbundel harus utuh, konsisten, dan (bila ada sumber) segar
// =====================================================================

static int Cek(string berkasKeluaran, string folderSumber)
{
    if (!File.Exists(berkasKeluaran))
    {
        Console.WriteLine("GAGAL: berkas data wilayah tidak ada: " + berkasKeluaran);
        return 2;
    }

    var (jumlah, isi, masalah) = BacaGz(berkasKeluaran);
    if (jumlah == null)
    {
        Console.WriteLine("GAGAL: berkas data wilayah tidak bisa dibaca: " + masalah.FirstOrDefault());
        return 2;
    }

    foreach (string m in masalah)
    {
        Console.WriteLine("GAGAL: " + m);
    }

    if (masalah.Count > 0)
    {
        return 2;
    }

    string berkasWilayah = Path.Combine(folderSumber, "wilayah.sql");
    string berkasKodepos = Path.Combine(folderSumber, "wilayah_kodepos.sql");
    if (File.Exists(berkasWilayah) && File.Exists(berkasKodepos))
    {
        var masalahSumber = new List<string>();
        var wilayah = BacaWilayah(berkasWilayah, masalahSumber);
        var keluaran = SusunKeluaran(
            wilayah, BacaKodepos(berkasKodepos), BacaMeta(berkasWilayah), BacaMeta(berkasKodepos), masalahSumber);
        byte[] seharusnya = TulisGz(keluaran.Isi);
        byte[] tersimpan = File.ReadAllBytes(berkasKeluaran);
        if (!tersimpan.AsSpan().SequenceEqual(seharusnya))
        {
            Console.WriteLine("GAGAL: berkas terbundel tidak sama dengan hasil olahan sumber terbaru. " +
                              "Jalankan: dotnet run --project Tools/WilayahGen");
            return 2;
        }

        Console.WriteLine("segar: hasil olahan sumber identik dengan berkas terbundel");
    }
    else
    {
        Console.WriteLine("catatan: sumber belum ada di " + Relatif(folderSumber) +
                          " — hanya struktur yang diperiksa (jalankan tanpa argumen untuk mengunduh)");
    }

    Console.WriteLine("lolos: " + Relatif(berkasKeluaran) + " — " + Ringkasan(jumlah));
    return 0;
}

// =====================================================================
// Pembacaan sumber
// =====================================================================

static int SiapkanSumber(string path, string url, string keterangan)
{
    if (File.Exists(path))
    {
        Console.WriteLine("pakai  " + Relatif(path));
        return 0;
    }

    Console.WriteLine("unduh  " + keterangan);
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        byte[] isi = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(path, isi);
        Console.WriteLine("  → " + Relatif(path) + " (" + isi.Length + " byte)");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("GAGAL mengunduh " + url);
        Console.WriteLine("      " + ex.Message);
        Console.WriteLine("      Letakkan berkas sumbernya manual di " + Relatif(path) + " lalu jalankan lagi.");
        return 2;
    }
}

static List<BarisWilayah> BacaWilayah(string path, List<string> masalah)
{
    var hasil = new List<BarisWilayah>(92000);
    var terlihat = new HashSet<string>(StringComparer.Ordinal);

    foreach (string baris in File.ReadLines(path))
    {
        var cocok = Pola.BarisWilayah.Match(baris.Trim());
        if (!cocok.Success)
        {
            continue;
        }

        string kode = cocok.Groups[1].Value;
        string nama = cocok.Groups[2].Value.Replace("''", "'").Trim();
        if (kode.Length == 0 || nama.Length == 0)
        {
            masalah.Add("baris tanpa kode/nama dilewati: " + Potong(baris.Trim()));
            continue;
        }

        if (!terlihat.Add(kode))
        {
            masalah.Add("kode ganda dilewati: " + kode);
            continue;
        }

        hasil.Add(new BarisWilayah(kode, nama));
    }

    return hasil;
}

static Dictionary<string, string> BacaKodepos(string path)
{
    var hasil = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string baris in File.ReadLines(path))
    {
        var cocok = Pola.BarisNilai.Match(baris.Trim());
        if (!cocok.Success)
        {
            continue;
        }

        string kode = cocok.Groups[1].Value;
        string nilai = cocok.Groups[2].Value.Trim();
        if (kode.Length > 0 && nilai.Length > 0)
        {
            hasil[kode] = nilai;
        }
    }

    return hasil;
}

/// <summary>Metadata dari blok komentar sumber: acuan Kepmendagri dan tanggal sunting terakhir.</summary>
static MetaSumber BacaMeta(string path)
{
    string acuan = string.Empty;
    string terakhir = string.Empty;
    int dibaca = 0;

    foreach (string baris in File.ReadLines(path))
    {
        if (++dibaca > 30)
        {
            break;
        }

        if (acuan.Length == 0)
        {
            var m = Pola.Acuan.Match(baris);
            if (m.Success)
            {
                acuan = "Kepmendagri " + m.Groups[1].Value.Replace("No ", string.Empty).Replace("No. ", string.Empty) +
                        "/" + m.Groups[2].Value;
            }
            else if (baris.Contains("Kepmendagri", StringComparison.OrdinalIgnoreCase))
            {
                acuan = baris[(baris.IndexOf("Kepmendagri", StringComparison.OrdinalIgnoreCase))..].Trim();
            }
        }

        var m2 = Pola.Terakhir.Match(baris);
        if (m2.Success && terakhir.Length == 0)
        {
            terakhir = m2.Groups[1].Value.Trim();
        }

        if (acuan.Length > 0 && terakhir.Length > 0)
        {
            break;
        }
    }

    return new MetaSumber(acuan, terakhir);
}

// =====================================================================
// Penyusunan berkas keluaran
// =====================================================================

static (JumlahWilayah Jumlah, string Isi) SusunKeluaran(
    List<BarisWilayah> wilayah,
    Dictionary<string, string> kodepos,
    MetaSumber metaWilayah,
    MetaSumber metaKodepos,
    List<string> masalah)
{
    var jumlah = new JumlahWilayah();
    var keluaran = new StringBuilder(4 * 1024 * 1024);
    var terurut = wilayah
        .OrderBy(w => KunciUrut(w.Kode), StringComparer.Ordinal)
        .ToList();

    foreach (var w in terurut)
    {
        char jenis = JenisWilayah(w.Kode, w.Nama, masalah);
        if (jenis == '?')
        {
            masalah.Add("jenis wilayah tidak dikenal, dilewati: " + w.Kode + " " + w.Nama);
            continue;
        }

        string kodePos = kodepos.TryGetValue(w.Kode, out string? kp) ? kp : string.Empty;
        if (kodePos.Length > 0 && jenis is 'd' or 'l')
        {
            jumlah.Berkodepos++;
        }

        // Nama disimpan tanpa awalan "Kabupaten"/"Kota": aplikasi sudah mencetak
        // awalan itu sendiri pada kop surat, dan jenis wilayah sudah membedakan
        // kabupaten dari kota.
        string nama = NamaTampil(w.Nama, jenis);

        switch (jenis)
        {
            case 'p': jumlah.Provinsi++; break;
            case 'b': jumlah.Kabupaten++; break;
            case 'k': jumlah.Kota++; break;
            case 'c': jumlah.Kecamatan++; break;
            case 'd': jumlah.Desa++; break;
            case 'l': jumlah.Kelurahan++; break;
        }

        jumlah.Total++;
        keluaran.Append(w.Kode).Append('\t')
            .Append(nama).Append('\t')
            .Append(jenis).Append('\t')
            .Append(kodePos).Append('\n');
    }

    if (!PeriksaHierarki(terurut, masalah))
    {
        throw new InvalidOperationException("Hierarki wilayah rusak: " + masalah[^1]);
    }

    var kepala = new StringBuilder();
    kepala.Append("# wilayah.tsv — data wilayah administratif Indonesia (provinsi s.d. desa/kelurahan)\n");
    kepala.Append("# sumber: github.com/cahyadsn/wilayah + github.com/cahyadsn/wilayah_kodepos ");
    kepala.Append("(lisensi MIT, copyright (c) cahya dsn)\n");
    kepala.Append("# acuan: ").Append(AcuanRingkas(metaWilayah)).Append('\n');
    kepala.Append("# sumber-diperbarui: wilayah ").Append(Orisinil(metaWilayah))
        .Append(", kode pos ").Append(Orisinil(metaKodepos)).Append('\n');
    kepala.Append("# kolom: kode<TAB>nama<TAB>jenis<TAB>kodepos  ");
    kepala.Append("(jenis: p=provinsi b=kabupaten k=kota c=kecamatan d=desa l=kelurahan)\n");
    kepala.Append("# nama: tanpa awalan \"Kabupaten\"/\"Kota\" — jenis wilayah sudah membedakan keduanya\n");
    kepala.Append("# jumlah: ").Append(jumlah.Total).Append(" baris — ")
        .Append(jumlah.Provinsi).Append(" provinsi, ")
        .Append(jumlah.Kabupaten + jumlah.Kota).Append(" kabupaten/kota, ")
        .Append(jumlah.Kecamatan).Append(" kecamatan, ")
        .Append(jumlah.Desa + jumlah.Kelurahan).Append(" desa/kelurahan\n")
        .Append("# catatan: kode pos adalah saran per desa/kelurahan — satu kode pos dapat dipakai beberapa\n")
        .Append("#          desa, dan satu kecamatan dapat memakai beberapa kode pos, jadi tetap bisa dikoreksi\n");

    return (jumlah, kepala + keluaran.ToString());
}

static string AcuanRingkas(MetaSumber meta) =>
    meta.Acuan.Length > 0 ? meta.Acuan : "Kepmendagri (acuan tidak terbaca dari sumber)";

static string Orisinil(MetaSumber meta) => meta.Terakhir.Length > 0 ? meta.Terakhir : "tidak diketahui";

/// <summary>Nama wilayah tanpa awalan tingkat, mengikuti kebiasaan isian aplikasi.</summary>
static string NamaTampil(string nama, char jenis)
{
    if (jenis == 'b' && nama.StartsWith("Kabupaten ", StringComparison.OrdinalIgnoreCase))
    {
        return nama[10..].Trim();
    }

    if (jenis == 'k' && nama.StartsWith("Kota ", StringComparison.OrdinalIgnoreCase))
    {
        return nama[5..].Trim();
    }

    return nama;
}

/// <summary>
/// Jenis wilayah dari bentuk kodenya: 1 segmen provinsi, 2 kabupaten/kota
/// (dibedakan awalan namanya), 3 kecamatan, 4 desa/kelurahan (segmen terakhir
/// 1xxx = kelurahan, 2xxx = desa, 3xxx = desa adat Papua yang diperlakukan
/// sebagai desa).
/// </summary>
static char JenisWilayah(string kode, string nama, List<string> masalah)
{
    var segmen = kode.Split('.');
    switch (segmen.Length)
    {
        case 1:
            return 'p';
        case 2:
            if (nama.StartsWith("Kota ", StringComparison.OrdinalIgnoreCase))
            {
                return 'k';
            }

            if (nama.StartsWith("Kabupaten ", StringComparison.OrdinalIgnoreCase))
            {
                return 'b';
            }

            masalah.Add("kabupaten/kota tanpa awalan yang dikenal: " + kode + " " + nama);
            return '?';
        case 3:
            return 'c';
        case 4:
            char awalan = segmen[3].Length > 0 ? segmen[3][0] : '?';
            return awalan switch
            {
                '1' => 'l',
                '2' => 'd',
                '3' => 'd',
                _ => '?',
            };
        default:
            return '?';
    }
}

/// <summary>
/// Setiap wilayah harus punya induknya (kode tanpa segmen terakhir) di daftar.
/// Hierarki rusak membuat pilihan berjenjang menampilkan wilayah yang tidak bisa
/// dipilih, jadi lebih baik gagal di sini daripada di aplikasi.
/// </summary>
static bool PeriksaHierarki(List<BarisWilayah> wilayah, List<string> masalah)
{
    var kode = new HashSet<string>(wilayah.Select(w => w.Kode), StringComparer.Ordinal);
    foreach (var w in wilayah)
    {
        int pisah = w.Kode.LastIndexOf('.');
        if (pisah < 0)
        {
            continue;
        }

        string induk = w.Kode[..pisah];
        if (!kode.Contains(induk))
        {
            masalah.Add("induk tidak ada: " + w.Kode + " → " + induk);
            return false;
        }
    }

    return true;
}

/// <summary>Baca berkas terbundel: jumlah per tingkat + daftar masalah struktur.</summary>
static (JumlahWilayah? Jumlah, string Isi, List<string> Masalah) BacaGz(string path)
{
    var masalah = new List<string>();
    try
    {
        byte[] mentah = File.ReadAllBytes(path);
        if (mentah.Length < 2 || mentah[0] != 0x1F || mentah[1] != 0x8B)
        {
            masalah.Add("berkas bukan gzip");
            return (null, string.Empty, masalah);
        }

        using var sumber = new MemoryStream(mentah);
        using var gz = new GZipStream(sumber, CompressionMode.Decompress);
        using var pembaca = new StreamReader(gz, Encoding.UTF8);
        string isi = pembaca.ReadToEnd();

        var jumlah = new JumlahWilayah();
        foreach (string baris in isi.Split('\n'))
        {
            if (baris.Length == 0 || baris[0] == '#')
            {
                continue;
            }

            var kolom = baris.Split('\t');
            if (kolom.Length < 3)
            {
                masalah.Add("baris cacat: " + Potong(baris));
                continue;
            }

            switch (kolom[2])
            {
                case "p": jumlah.Provinsi++; break;
                case "b": jumlah.Kabupaten++; break;
                case "k": jumlah.Kota++; break;
                case "c": jumlah.Kecamatan++; break;
                case "d": jumlah.Desa++; break;
                case "l": jumlah.Kelurahan++; break;
                default: masalah.Add("jenis tidak dikenal: " + Potong(baris)); break;
            }

            if (kolom.Length >= 4 && kolom[3].Trim().Length > 0 && kolom[2] is "d" or "l")
            {
                jumlah.Berkodepos++;
            }

            jumlah.Total++;
        }

        if (jumlah.Provinsi == 0)
        {
            masalah.Add("tidak ada baris provinsi");
        }

        if (jumlah.Kabupaten + jumlah.Kota == 0)
        {
            masalah.Add("tidak ada baris kabupaten/kota");
        }

        if (jumlah.Kecamatan == 0)
        {
            masalah.Add("tidak ada baris kecamatan");
        }

        if (jumlah.Desa + jumlah.Kelurahan == 0)
        {
            masalah.Add("tidak ada baris desa/kelurahan");
        }

        return (jumlah, isi, masalah);
    }
    catch (Exception ex)
    {
        masalah.Add(ex.Message);
        return (null, string.Empty, masalah);
    }
}

static byte[] TulisGz(string isi)
{
    using var keluaran = new MemoryStream();
    using (var gz = new GZipStream(keluaran, CompressionLevel.Optimal, leaveOpen: true))
    {
        byte[] data = Encoding.UTF8.GetBytes(isi);
        gz.Write(data, 0, data.Length);
    }

    return keluaran.ToArray();
}

// =====================================================================
// Utilitas
// =====================================================================

static bool ApakahCek(string[] args) => args.Any(a =>
    a.Equals("cek", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--cek", StringComparison.OrdinalIgnoreCase));

static string? CariAkar()
{
    var folder = new DirectoryInfo(AppContext.BaseDirectory);
    while (folder != null)
    {
        if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
        {
            return folder.FullName;
        }

        folder = folder.Parent;
    }

    return null;
}

static string Relatif(string path)
{
    string? akar = CariAkar();
    return akar != null && path.StartsWith(akar, StringComparison.OrdinalIgnoreCase)
        ? path[(akar.Length + 1)..].Replace('\\', '/')
        : path;
}

/// <summary>Kunci urut hierarkis: tiap segmen kode digenapi nol supaya "11.02" tidak mendahului "11.10".</summary>
static string KunciUrut(string kode)
{
    var segmen = kode.Split('.');
    for (int i = 0; i < segmen.Length; i++)
    {
        if (segmen[i].Length < 4)
        {
            segmen[i] = segmen[i].PadLeft(4, '0');
        }
    }

    return string.Join('|', segmen);
}

static string Potong(string teks) => teks.Length <= 80 ? teks : teks[..80] + "…";internal static class Pola
{
    /// <summary>Baris "('kode', 'nama')" pada dump wilayah.</summary>
    public static readonly Regex BarisWilayah = new(@"^\('([^']*)',\s*'((?:[^']|'')*)'\)", RegexOptions.Compiled);

    /// <summary>Baris "('kode', 'nilai')" pada dump kode pos.</summary>
    public static readonly Regex BarisNilai = new(@"^\('([^']*)',\s*'([^']*)'\)", RegexOptions.Compiled);

    /// <summary>Acuan pada blok komentar sumber, mis. "Kepmendagri No 300.2.2-2138 Tahun 2025".</summary>
    public static readonly Regex Acuan =
        new(@"Kepmendagri\s+No\.?\s*((?:\d|\.|-)+)\s+[Tt]ahun\s+(\d{4})", RegexOptions.Compiled);

    /// <summary>Tanggal sunting terakhir dari blok komentar sumber.</summary>
    public static readonly Regex Terakhir =
        new(@"last edit\s*:\s*([0-9]{4}-[0-9]{2}-[0-9]{2})", RegexOptions.Compiled);
}

internal sealed record BarisWilayah(string Kode, string Nama);

internal sealed record MetaSumber(string Acuan, string Terakhir);

internal sealed class JumlahWilayah
{
    public int Total { get; set; }
    public int Provinsi { get; set; }
    public int Kabupaten { get; set; }
    public int Kota { get; set; }
    public int Kecamatan { get; set; }
    public int Desa { get; set; }
    public int Kelurahan { get; set; }
    public int Berkodepos { get; set; }
}
