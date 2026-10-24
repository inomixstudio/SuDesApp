using System;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Tautan <c>wa.me</c> adalah satu-satunya cara tombol WhatsApp membawa desa ke
/// nomor admin yang benar; salah nomor berarti pembayaran manual dibuktikan ke
/// orang yang salah. Uji ini mengunci nomor dan encoding pesannya supaya tombol
/// dan pembaca bisa percaya pada tujuan yang sama.
/// </summary>
public sealed class BangunTautanWaTests
{
    [Fact]
    public void Nomor_admin_tertulis_lengkap_dan_satu_sumber()
    {
        Assert.Equal("0851 1166 9951", LanggananViewModel.NomorWaAdminTampil);
        Assert.Equal("6285111669951", LanggananViewModel.NomorWaAdminWa);
    }

    [Fact]
    public void Tautan_menuju_wa_me_dengan_nomor_internasional()
    {
        var tautan = LanggananViewModel.BangunTautanWa("Halo");

        Assert.StartsWith("https://wa.me/6285111669951?text=", tautan, StringComparison.Ordinal);
    }

    [Fact]
    public void Pesan_di_url_encoded_benar()
    {
        var pesan = "Halo admin, nama desa saya \"Sumberjaya\" & num=100%.";

        var tautan = LanggananViewModel.BangunTautanWa(pesan);

        Assert.Equal(
            "https://wa.me/6285111669951?text=" + Uri.EscapeDataString(pesan),
            tautan);
    }

    [Fact]
    public void Pesan_dengan_baris_baru_ikut_ter_encode()
    {
        var pesan = "Baris satu\nBaris dua\r\nBaris tiga";

        var tautan = LanggananViewModel.BangunTautanWa(pesan);

        Assert.DoesNotContain("\n", tautan, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", tautan, StringComparison.Ordinal);
        Assert.EndsWith(Uri.EscapeDataString(pesan), tautan, StringComparison.Ordinal);
    }
}