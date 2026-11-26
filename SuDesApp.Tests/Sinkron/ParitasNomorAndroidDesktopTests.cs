using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// Rantai penomoran desktop → Supabase → Android. Desktop menerbitkan pola nomor
/// per jenis surat di <c>pengaturan_desa.nomor.format</c>; Android mengambilnya dan
/// HANYA jatuh ke awalan global ("470", masih bisa diubah operator) kalau polanya
/// tidak ada. Tanpa penjaga ini, jenis surat baru yang lupa didaftarkan di katalog
/// tercetak dengan awalan 470 di HP sementara register desktop memakai kelas
/// klasifikasinya — divergen diam-diam, persis yang dijaga ZONA PARITAS.
/// </summary>
public sealed class ParitasNomorAndroidDesktopTests
{
    private static readonly string AkarRepo = CariAkarRepo();

    private static readonly string SkemaDesktop = Path.Combine(
        AkarRepo, "SuDesApp.Core", "Configuration", "SkemaFormulir.json");

    private static readonly string Katalog = Path.Combine(
        AkarRepo, "SuDesApp.Core", "Configuration", "JenisSuratConfig.json");

    private static readonly string Penerbit = Path.Combine(
        AkarRepo, "SuDesApp.Core", "Sinkron", "PenerbitPengaturanDesa.cs");

    private static readonly string SuratAndroid = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes",
        "android", "PengaturanSurat.kt");

    private static readonly string SinkronAndroid = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes",
        "android", "PengaturanSinkron.kt");

    [Fact]
    public void SetiapJenisDiSkemaAndroidPunyaPolaNomorDariKatalog()
    {
        var pola = PolaKatalog();
        var skema = JsonDocument.Parse(File.ReadAllText(SkemaDesktop, System.Text.Encoding.UTF8));

        var hilang = skema.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .Where(id => !pola.TryGetValue(id.ToUpperInvariant(), out string? format) ||
                         string.IsNullOrWhiteSpace(format))
            .ToList();

        Assert.True(hilang.Count == 0,
            "jenis surat tanpa pola nomor di JenisSuratConfig.json — Android akan diam-diam " +
            "memakai awalan global 470: " + string.Join(", ", hilang));
    }

    /// <summary>
    /// Kunci peta yang diterbitkan adalah NamaJenis katalog, dan Android mencari dengan
    /// idJenis skema. Keduanya harus satu kosakata, tidak peduli besar-kecil huruf.
    /// </summary>
    [Fact]
    public void KunciPolaKatalogSamaDenganKunciSkema()
    {
        var pola = PolaKatalog();
        var skema = JsonDocument.Parse(File.ReadAllText(SkemaDesktop, System.Text.Encoding.UTF8));
        var idSkema = skema.RootElement.EnumerateObject().Select(p => p.Name.ToUpperInvariant()).ToHashSet();

        Assert.True(idSkema.SetEquals(pola.Keys),
            $"katalog punya {pola.Count} kunci, skema {idSkema.Count}; beda: " +
            string.Join(", ", pola.Keys.Except(idSkema).Concat(idSkema.Except(pola.Keys))));
    }

    /// <summary>
    /// Kontrak dua sisi: penerbit desktop menulis <c>nomor.format</c> dari
    /// SuratNumberFormats, Android membacanya dan lebih mengutamakan pola itu daripada
    /// awalan global. Nama kunci yang berubah di salah satu sisi membuat Android kembali
    /// ke 470 untuk semua jenis tanpa galat apa pun.
    /// </summary>
    [Fact]
    public void KontrakKunciNomorFormatSamaDiDuaSisi()
    {
        string terbit = File.ReadAllText(Penerbit, System.Text.Encoding.UTF8);
        Assert.Contains("SuratNumberFormats", terbit, StringComparison.Ordinal);
        Assert.Contains("[\"format\"] = format", terbit, StringComparison.Ordinal);
        Assert.Contains("new NpgsqlParameter(\"nomor\", JsonSerializer.Serialize(nomor))", terbit, StringComparison.Ordinal);

        string sinkron = File.ReadAllText(SinkronAndroid, System.Text.Encoding.UTF8);
        Assert.Contains("optJSONObject(\"nomor\")", sinkron, StringComparison.Ordinal);
        Assert.Contains("optJSONObject(\"format\")", sinkron, StringComparison.Ordinal);

        string surat = File.ReadAllText(SuratAndroid, System.Text.Encoding.UTF8);
        int pakaiPola = surat.IndexOf("formatNomor[idJenis]", StringComparison.Ordinal);
        int bawaan = surat.IndexOf("awalanNomor.trim()", StringComparison.Ordinal);
        Assert.True(pakaiPola >= 0 && bawaan >= 0 && pakaiPola < bawaan,
            "Android harus mencoba pola terbitan sebelum awalan global 470");
    }

    private static Dictionary<string, string> PolaKatalog()
    {
        using var dokumen = JsonDocument.Parse(File.ReadAllText(Katalog, System.Text.Encoding.UTF8));
        var hasil = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entri in dokumen.RootElement.EnumerateArray())
        {
            string nama = entri.GetProperty("NamaJenis").GetString() ?? string.Empty;
            string format = entri.TryGetProperty("NomorFormat", out JsonElement f)
                ? f.GetString() ?? string.Empty
                : string.Empty;
            hasil[nama.ToUpperInvariant()] = format;
        }
        return hasil;
    }

    private static string CariAkarRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo tidak ditemukan.");
    }
}
