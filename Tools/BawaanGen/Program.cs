using SuDesApp.Data.Models;

// Pembuat & pemeriksa berkas contoh Template Surat pada folder Bawaan.
//
// Katalog C# (TemplateSuratBawaan.KatalogTertanam) adalah sumber kebenaran;
// folder Bawaan/TemplateSurat berisi JSON hasil ekspor yang ikut terkirim
// aplikasi dan dapat diperbarui lewat tambalan. Karena itu dua perintah:
//
//   (tanpa argumen)  tulis/segarkan JSON dari katalog — jalankan saat katalog berubah
//   --cek            bandingkan isi folder dengan katalog; exit 1 bila hilang/rusak/basi
//                    (dipakai check-rilis.ps1 supaya JSON tidak pernah tertinggal)
//
// Berkas tambahan di folder yang TIDAK ada di katalog adalah contoh yang dikirim
// penerbit lewat tambalan — tidak dihapus dan tidak dianggap kesalahan.

string? akar = CariAkar();
if (akar == null)
{
    Console.WriteLine("GAGAL: SuDesApp.slnx tidak ditemukan dari " + AppContext.BaseDirectory);
    return 2;
}

string folder = Path.Combine(akar,
    TemplateSuratBawaan.NamaFolderBawaan,
    TemplateSuratBawaan.NamaSubFolderTemplate);
var katalog = TemplateSuratBawaan.KatalogTertanam;

return (args.Contains("--cek", StringComparer.OrdinalIgnoreCase) ||
        args.Contains("cek", StringComparer.OrdinalIgnoreCase))
    ? Cek(folder, katalog)
    : Tulis(folder, katalog);

static int Tulis(string folder, IReadOnlyList<ContohTemplateSurat> katalog)
{
    Directory.CreateDirectory(folder);

    int berubah = 0;
    foreach (var contoh in katalog)
    {
        string path = Path.Combine(folder, contoh.Kode + ".json");
        string isi = TemplateSuratBawaan.Serialisasi(contoh);
        string lama = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

        if (Normalis(lama) != Normalis(isi))
        {
            File.WriteAllText(path, isi);
            berubah++;
            Console.WriteLine("tulis  " + contoh.Kode + ".json");
        }
    }

    var ekstra = Directory.GetFiles(folder, "*.json")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(n => n != null && !katalog.Any(k =>
            string.Equals(k.Kode, n, StringComparison.OrdinalIgnoreCase)))
        .ToList();
    if (ekstra.Count > 0)
    {
        Console.WriteLine("catatan: " + ekstra.Count +
            " berkas bukan dari katalog (dikirim penerbit) dibiarkan: " +
            string.Join(", ", ekstra));
    }

    Console.WriteLine(katalog.Count + " contoh katalog, " + berubah +
        " berkas ditulis/diperbarui di " + folder);
    return 0;
}

static int Cek(string folder, IReadOnlyList<ContohTemplateSurat> katalog)
{
    if (!Directory.Exists(folder))
    {
        Console.WriteLine("GAGAL: folder contoh tidak ada: " + folder);
        return 1;
    }

    int gagal = 0;
    foreach (var contoh in katalog)
    {
        string path = Path.Combine(folder, contoh.Kode + ".json");

        if (!File.Exists(path))
        {
            Console.WriteLine("GAGAL: hilang — " + contoh.Kode + ".json");
            gagal++;
            continue;
        }

        try
        {
            var dariBerkas = TemplateSuratBawaan.BacaBerkas(path);
            if (Normalis(TemplateSuratBawaan.Serialisasi(dariBerkas)) !=
                Normalis(TemplateSuratBawaan.Serialisasi(contoh)))
            {
                Console.WriteLine("GAGAL: basi — " + contoh.Kode +
                    ".json tidak sama dengan katalog C#; jalankan: " +
                    "dotnet run --project Tools/BawaanGen");
                gagal++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("GAGAL: rusak — " + contoh.Kode + ".json (" + ex.Message + ")");
            gagal++;
        }
    }

    if (gagal > 0)
    {
        Console.WriteLine(gagal + " berkas contoh bermasalah.");
        return 1;
    }

    Console.WriteLine("OK: " + katalog.Count + " JSON contoh segar dengan katalog C#.");
    return 0;
}

static string? CariAkar()
{
    string? folder = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(folder))
    {
        if (File.Exists(Path.Combine(folder, "SuDesApp.slnx"))) return folder;
        folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar));
    }

    return null;
}

static string Normalis(string teks) => teks.Replace("\r\n", "\n").TrimEnd();
