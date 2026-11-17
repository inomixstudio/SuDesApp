using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SuDesApp.Data.Models;
using SuDesApp.Sinkron;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// Impor antrean Android untuk path bertingkat (wargaKK.*, kenalLahir.anak.*,
/// kenalLahir.ibu.*). Regresi nyata: <c>SkemaFormulir.Terapkan</c> dulu
/// berhenti diam-diam bila objek antara masih null, sehingga seluruh isian
/// "Data 2" Beda Nama dan data orang tua/anak Kenal Lahir dari Android
/// tidak pernah masuk ke surat desktop — tanpa日志, tanpa galat.
/// </summary>
public sealed class SkemaFormulirImporBertingkatTests
{
    [Fact]
    public void WargaKK_dari_android_terpetakan_walau_objek_awalnya_null()
    {
        var skema = SkemaFormulir.Kosong();
        using var fields = JsonDocument.Parse("""
        { "nama2": "SITI AMINAH", "nik2": 3201010101010002, "desa2": "Karangsari" }
        """);

        var jalur = new List<SkemaBidang>
        {
            new("nama2", "Nama Data 2", "text", "wargaKK.nama"),
            new("nik2", "NIK Data 2", "number", "wargaKK.nik"),
            new("desa2", "Desa Data 2", "text", "wargaKK.desa"),
        };
        var denganSkema = SkemaFormulir.MuatDariJson(BangunJson(jalur));

        var surat = denganSkema.Bangun("BEDANAMA", fields.RootElement, null, null, out var diterapkan);

        Assert.Equal(3, diterapkan);
        Assert.NotNull(surat.WargaKK);
        // Teks disimpan POLOS tanpa kutip JSON (regresi: JsonElement.ToString).
        Assert.Equal("SITI AMINAH", surat.WargaKK!.Nama);
        Assert.Equal("Karangsari", surat.WargaKK.Desa);
    }

    [Fact]
    public void Tanggal_android_di_properti_teks_dinormalkan_ke_ISO()
    {
        // Generator desktop (BedaNama/Kematian) membaca TanggalLahir*/
        // TanggalKematian dengan DateFormatDb "yyyy-MM-dd". Android menulis
        // "dd/MM/yyyy" dari pemilih tanggal — tanpa normalisasi, tanggal
        // lolos simpan tapi PDF resmi kehilangan baris tanggal.
        var skema = SkemaFormulir.MuatDariJson("""
        { "BEDANAMA": { "label": "Beda Nama", "fields": [
            { "key": "tanggalLahir2", "label": "Tgl Lahir 2", "type": "date", "target": "wargaKK.tanggalLahir" },
            { "key": "nik2", "label": "NIK Data 2", "type": "number", "target": "wargaKK.nik" }
        ] } }
        """);
        using var fields = JsonDocument.Parse("""{ "tanggalLahir2": "05/11/2020", "nik2": "3201010101010002" }""");

        var surat = skema.Bangun("BEDANAMA", fields.RootElement, null, null, out var diterapkan);

        Assert.Equal(2, diterapkan);
        Assert.Equal("2020-11-05", surat.WargaKK!.TanggalLahir);
        Assert.Equal("3201010101010002", surat.WargaKK.NIK);
    }

    [Fact]
    public void KenalLahir_anak_dan_ibu_terpetakan_ke_objek_bertingkat()
    {
        var jalur = new List<SkemaBidang>
        {
            new("ibuNama", "Nama Ibu", "text", "kenalLahir.ibu.nama"),
            new("namaAnak", "Nama Anak", "text", "kenalLahir.anak.nama"),
            new("tanggalLahirAnak", "Tgl Lahir Anak", "date", "kenalLahir.anak.tanggalLahir"),
        };
        var skema = SkemaFormulir.MuatDariJson(BangunJson(jalur, "KENAL_LAHIR"));
        using var fields = JsonDocument.Parse("""
        { "ibuNama": "SITI", "namaAnak": "BILAL", "tanggalLahirAnak": "05/11/2020" }
        """);

        var surat = skema.Bangun("KENAL_LAHIR", fields.RootElement, null, null, out var diterapkan);

        Assert.Equal(3, diterapkan);
        Assert.NotNull(surat.KenalLahir);
        Assert.Equal("SITI", surat.KenalLahir!.Ibu?.Nama);
        Assert.Equal("BILAL", surat.KenalLahir.Anak?.Nama);
        Assert.Equal("2020-11-05", surat.KenalLahir.Anak?.TanggalLahir);
    }

    private static string BangunJson(List<SkemaBidang> jalur, string idJenis = "BEDANAMA")
    {
        var fields = new List<string>();
        foreach (var f in jalur)
            fields.Add($"{{\"key\":\"{f.Key}\",\"label\":\"{f.Label}\",\"type\":\"{f.Type}\",\"target\":\"{f.Target}\"}}");

        return $"{{\"{idJenis}\":{{\"label\":\"{idJenis}\",\"fields\":[{string.Join(",", fields)}]}}}}";
    }
}
