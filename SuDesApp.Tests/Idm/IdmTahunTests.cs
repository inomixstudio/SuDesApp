using System.Collections.Generic;
using System.Linq;
using SuDesApp.Services;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Halaman IDM mengambil beberapa tahun sekaligus, lalu menampilkan tahun
/// TERAKHIR yang datanya benar-benar terbit. Dua hal yang mudah rusak tanpa
/// terlihat diuji di sini: daftar tahun tidak boleh memuat tahun kembar (pusat
/// kadang mengabaikan parameter tahun), dan kalimat ringkasan harus menyebut
/// tahun terakhir — bukan tahun berjalan.
/// </summary>
public sealed class IdmTahunTests
{
    private static HasilIdm Tahun(int tahun, double skor = 0.7) => new(
        new RingkasanIdm(tahun, skor, "MAJU", "MANDIRI", 0.8, 0.1, 0.6, 0.7, 0.8),
        null,
        System.Array.Empty<BarisIdm>());

    [Fact]
    public void TahunUnik_membuang_tahun_kembar_dan_mengurutkan_terbaru_dulu()
    {
        var hasil = IdmViewModel.TahunUnik(new[]
        {
            Tahun(2024, 0.74),
            Tahun(2024, 0.74), // jawaban kembar untuk permintaan tahun lain
            Tahun(2022),
            Tahun(2023),
        });

        Assert.Equal(new[] { 2024, 2023, 2022 }, hasil.Select(h => h.Ringkasan.Tahun));
        Assert.Equal(0.74, hasil[0].Ringkasan.Skor, 4);
    }

    [Fact]
    public void RingkasanTahun_menyebut_tahun_terakhir_bukan_tahun_berjalan()
    {
        var kalimat = IdmViewModel.RingkasanTahun(new List<int> { 2022, 2020, 2024, 2021, 2023 });

        Assert.Contains("5 tahun dimuat", kalimat);
        Assert.Contains("2020–2024", kalimat);
        Assert.Contains("tahun terakhir, 2024", kalimat);
    }

    [Fact]
    public void RingkasanTahun_satu_tahun_tidak_menyebut_rentang()
    {
        var kalimat = IdmViewModel.RingkasanTahun(new List<int> { 2024 });

        Assert.Contains("hanya tersedia untuk tahun 2024", kalimat);
        Assert.DoesNotContain("–", kalimat);
    }

    [Fact]
    public void RingkasanTahun_kosong_tidak_menghasilkan_kalimat()
    {
        Assert.Equal(string.Empty, IdmViewModel.RingkasanTahun(new List<int>()));
    }
}
