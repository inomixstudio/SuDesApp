using System.Text.Json;
using SuDesApp.Sinkron;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// Baris antrean yang gagal harus dicoba lagi, bukan ditinggalkan selamanya.
///
/// Uji ini menjaga dua sisi kebijakan di <see cref="SinkronSuratService"/>:
/// baris <c>gagal</c> tetap diimpor selama percobaannya belum habis, dan baris
/// dengan status lain tidak pernah diimpor. Tanpa cabang retry, satu baris
/// buruk menempati slot batas 10 sampai perangkat berhenti bisa mengirim.
/// </summary>
public sealed class SinkronAntreanTests
{
    private static AntreanSuratBaris Baris(string status, short batasCoba) =>
        new(
            Guid.NewGuid(),
            "tenant-uji",
            "pengguna-uji",
            Guid.NewGuid(),
            "SKTM",
            "470/001/Ds/2026",
            "3201010101010001",
            JsonDocument.Parse("""{"tanggal":"2026-11-07T09:00:00+07:00","fields":{}}""").RootElement.Clone(),
            status,
            batasCoba,
            null,
            new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Baris_menunggu_selalu_diimpor()
    {
        Assert.True(SinkronSuratService.PerluDiimpor(Baris("menunggu", 0)));
    }

    [Theory]
    [InlineData("Menunggu")]
    [InlineData("MENUNGGU")]
    public void Status_menunggu_tidak_peka_huruf_besar_kecil(string status)
    {
        Assert.True(SinkronSuratService.PerluDiimpor(Baris(status, 0)));
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)2)]
    public void Baris_gagal_diimpor_selama_percobaan_belum_habis(short batasCoba)
    {
        Assert.True(SinkronSuratService.PerluDiimpor(Baris("gagal", batasCoba)));
    }

    [Fact]
    public void Baris_gagal_yang_percobaannya_habis_tidak_diimpor_lagi()
    {
        Assert.False(SinkronSuratService.PerluDiimpor(
            Baris("gagal", SinkronSuratService.BatasCobaMaks)));
    }

    [Theory]
    [InlineData("sinkron")]
    [InlineData("")]
    [InlineData("diproses")]
    public void Status_lain_tidak_diimpor(string status)
    {
        Assert.False(SinkronSuratService.PerluDiimpor(Baris(status, 0)));
    }
}
