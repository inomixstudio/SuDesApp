using System.Text.Json;
using SuDesApp.Data.Models;
using SuDesApp.Sinkron;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// Skema formulir adalah kontrak antara desktop dan Android: Android membangun
/// formulir dari skema ini, desktop memetakan isian Android kembali ke
/// <see cref="SuratData"/>. Uji ini menjaga hal yang paling mudah rusak tanpa
/// terlihat — nama kunci, path target, dan kasus khusus (payload
/// <c>additionalData</c>, daftar garapan, daftar anak) — supaya penambahan
/// field di desktop tidak membuat Android diam-diam membuang data.
/// </summary>
public sealed class SkemaFormulirTests
{
    private static readonly string JalurSkema = CariSkema();

    private static SkemaFormulir Muat() => SkemaFormulir.Muat(JalurSkema);

    private static string CariSkema()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx")))
            {
                var jalur = Path.Combine(folder.FullName, "SuDesApp.Core", "Configuration", "SkemaFormulir.json");
                Assert.True(File.Exists(jalur), $"Skema formulir tidak ditemukan: {jalur}");
                return jalur;
            }

            folder = folder.Parent;
        }

        throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement.GetProperty("fields");

    [Fact]
    public void Skema_memuat_seluruh_jenis_surat()
    {
        var skema = Muat();
        Assert.Equal(27, skema.IdJenis.Count);
        Assert.NotNull(skema.Cari("SKTM"));
        Assert.NotNull(skema.Cari("NTCR_N1"));
        Assert.NotNull(skema.Cari("TEMPLATE_SURAT"));
    }

    [Fact]
    public void Bangun_SKTV_memetakan_field_warga_dan_surat()
    {
        var skema = Muat();
        var json = """
        {
          "tanggal": "2026-11-07",
          "fields": {
            "nik": "3201010101900001",
            "nama": "Budi Santoso",
            "jenisKelamin": "Laki-laki",
            "agama": "Islam",
            "dusun": "Cempaka",
            "rt": "001",
            "rw": "002",
            "desa": "Sumberjaya",
            "kecamatan": "Bangkong",
            "kabupaten": "Karawang",
            "pejabatPenandatangan": "Kepala Desa"
          }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var surat = skema.Bangun("SKTM", doc.RootElement.GetProperty("fields"), "AND-0001", doc.RootElement.GetProperty("tanggal").GetDateTime(), out var diterapkan);

        Assert.Equal("SKTM", surat.NamaJenis);
        Assert.Equal("AND-0001", surat.NomorSurat);
        Assert.Equal("Active", surat.Status);
        Assert.Equal("3201010101900001", surat.Warga?.NIK);
        Assert.Equal("Budi Santoso", surat.Warga?.Nama);
        Assert.Equal("Cempaka", surat.Warga?.Dusun);
        Assert.Equal("Kepala Desa", surat.PejabatPenandatangan);
        Assert.True(diterapkan >= 10, $"Field diterapkan: {diterapkan}");
    }

    [Fact]
    public void Bangun_field_tidak_dikenal_tidak_melempar()
    {
        var skema = Muat();
        var json = """{ "fields": { "kodeRahasia": "abc", "nama": "Budi" } }""";

        var surat = skema.Bangun("SKTM", Fields(json), null, null, out var diterapkan);

        Assert.Equal("Budi", surat.Warga?.Nama);
        Assert.Equal(1, diterapkan);
    }

    [Fact]
    public void Bangun_RekeningKoran_membuat_payload_AdditionalData()
    {
        var skema = Muat();
        var json = """
        {
          "fields": {
            "namaKades": "H. Asep",
            "jabatan": "Kepala Desa",
            "namaRekening": "H. Asep",
            "nomorRekening": "1234567890",
            "bank": "BRI",
            "kcp": "Karawang"
          }
        }
        """;

        var surat = skema.Bangun("REKENING_KORAN", Fields(json), "RK-1", null, out _);

        Assert.False(string.IsNullOrWhiteSpace(surat.AdditionalData));
        using var payload = JsonDocument.Parse(surat.AdditionalData!);
        Assert.Equal("H. Asep", payload.RootElement.GetProperty("NamaPejabat").GetString());
        Assert.Equal("Kepala Desa", payload.RootElement.GetProperty("Jabatan").GetString());
        Assert.Equal("BRI", payload.RootElement.GetProperty("Bank").GetString());
    }

    [Fact]
    public void Bangun_Garapan_membuat_daftar_rincian()
    {
        var skema = Muat();
        var json = """
        {
          "fields": {
            "lokasi": "Blok Sawah",
            "luas": 1500,
            "nomorPersil": "P-01",
            "keteranganGarapan": "Sawah irigasi",
            "rincianTambahan": [
              { "lokasi": "Blok Baru", "luas": 800, "keteranganGarapan": "Sawah tadah hujan" }
            ]
          }
        }
        """;

        var surat = skema.Bangun("GARAPAN_SAWAH", Fields(json), null, null, out _);

        Assert.Equal(2, surat.RincianGarapans.Count);
        Assert.Equal("Blok Sawah", surat.RincianGarapans[0].Lokasi);
        Assert.Equal(1500, surat.RincianGarapans[0].Luas);
        Assert.Equal("Blok Baru", surat.RincianGarapans[1].Lokasi);
    }

    [Fact]
    public void Bangun_AhliWaris_membuat_daftar_anak()
    {
        var skema = Muat();
        var json = """
        {
          "fields": {
            "namaPasangan": "Siti",
            "anak": [
              { "nama": "Ani", "jenisKelamin": "Perempuan" },
              { "nama": "Budi", "jenisKelamin": "Laki-laki" }
            ]
          }
        }
        """;

        var surat = skema.Bangun("AHLI_WARIS", Fields(json), null, null, out _);

        Assert.Equal("Siti", surat.AhliWarisData?.NamaPasangan);
        Assert.Equal(2, surat.AhliWarisData?.Anak.Count);
        Assert.Equal("Ani", surat.AhliWarisData!.Anak[0].Nama);
    }
}
