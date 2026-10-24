using System;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Kalimat dan nada di halaman Langganan adalah kebijakan lisensi, bukan
/// hiasan: "sah sampai" harus memakai batas efektif, dan langganan yang tanggal
/// bayarnya sudah lewat tetapi masih dalam masa tenggang TIDAK boleh tampil
/// hijau "semua beres". Uji ini menjaga aturan itu tanpa membuka jendela.
/// </summary>
public sealed class RingkasanLanggananTests
{
    private static readonly DateTime Sekarang = new(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Aktif_tanpa_lewat_tanggal_bayar_dinilai_hijau_dan_tombol_trial_hilang()
    {
        var status = new StatusLisensi
        {
            Aktif = true,
            NamaDesa = "Sumberjaya",
            Paket = "tahunan",
            BerlakuSampai = Sekarang.AddDays(300),
            SahSampai = Sekarang.AddDays(300),
            SisaHariTenggang = 7,
            CekTerakhir = Sekarang
        };

        var ringkasan = RingkasanLangganan.Dari(status, "SUDES-1");

        Assert.Equal(1, ringkasan.Nada);
        Assert.False(ringkasan.TampilTombolTrial);
        Assert.StartsWith("Aktif s.d. ", ringkasan.Judul, StringComparison.Ordinal);
        Assert.Contains("Langganan sah — Sumberjaya.", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.Contains("Sisa 7 hari", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.Equal("7 hari", ringkasan.SisaHariTampil);
        Assert.Equal("Tahunan", ringkasan.PaketNama);
    }

    [Fact]
    public void Tanggal_bayar_lewat_tetapi_masih_tenggang_dinilai_perlu_tindakan()
    {
        // Relatif terhadap jam sekarang: uji ini soal "tanggal bayar sudah lewat",
        // jadi tanggal tetap akan berubah artinya seiring waktu berjalan.
        var sah = DateTime.UtcNow.AddDays(5);
        var status = new StatusLisensi
        {
            Aktif = true,
            NamaDesa = "Sumberjaya",
            Paket = "bulanan",
            BerlakuSampai = DateTime.UtcNow.AddDays(-2),
            SahSampai = sah,
            SisaHariTenggang = 5,
            CekTerakhir = DateTime.UtcNow
        };

        var ringkasan = RingkasanLangganan.Dari(status, "SUDES-1");

        Assert.Equal(2, ringkasan.Nada);
        Assert.Contains($"masa tenggang {LisensiManager.MASA_TENGGANG_HARI} hari", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.Contains("Cek Status", ringkasan.Keterangan, StringComparison.Ordinal);

        // Petak "SAH SAMPAI" memakai batas efektif, bukan tanggal bayar yang sudah lewat.
        Assert.Equal(sah.ToLocalTime().ToString("dd MMM yyyy"), ringkasan.SahSampaiTampil);
    }

    [Fact]
    public void Langganan_berakhir_ditampilkan_sebagai_berakhir_dengan_tanggalnya()
    {
        var berakhir = Sekarang.AddDays(-10);
        var status = new StatusLisensi
        {
            Aktif = false,
            Paket = "enambulan",
            BerlakuSampai = berakhir,
            CekTerakhir = Sekarang
        };

        var ringkasan = RingkasanLangganan.Dari(status, "SUDES-1");

        Assert.Equal("Tidak aktif — langganan berakhir", ringkasan.Judul);
        Assert.Equal(2, ringkasan.Nada);
        Assert.True(ringkasan.TampilTombolTrial);
        Assert.Equal(berakhir.ToLocalTime().ToString("dd MMM yyyy"), ringkasan.SahSampaiTampil);
        Assert.Contains("Perpanjang", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.Equal("6 Bulan", ringkasan.PaketNama);
    }

    [Fact]
    public void Belum_pernah_berlangganan_membedakan_sudah_isi_kode_dan_belum()
    {
        var status = new StatusLisensi { Aktif = false };

        Assert.Equal("Tidak aktif", RingkasanLangganan.Dari(status, null).Judul);
        Assert.Equal(
            "Masukkan kode aktivasi lalu tekan Cek Status.",
            RingkasanLangganan.Dari(status, "   ").Keterangan);
        Assert.Equal(
            "Kode aktivasi belum dikenal server atau belum pernah bayar.",
            RingkasanLangganan.Dari(status, "SUDES-1").Keterangan);
    }

    [Fact]
    public void Trial_lokal_memakai_ringkasan_trial_bukan_tidak_aktif()
    {
        // Trial lokal belum punya token, jadi status server masih "tidak aktif".
        // Banner biasa akan berbunyi "Tidak aktif" padahal menu sudah terbuka.
        var berakhir = Sekarang.AddDays(9);

        var ringkasan = RingkasanLangganan.DariTrial(berakhir, 9);

        Assert.Equal("Trial 14 hari aktif", ringkasan.Judul);
        Assert.Equal(2, ringkasan.Nada);
        Assert.False(ringkasan.TampilTombolTrial);
        Assert.Equal("Trial 14 hari", ringkasan.PaketNama);
        Assert.Equal("9 hari", ringkasan.SisaHariTampil);
        Assert.Equal(berakhir.ToLocalTime().ToString("dd MMM yyyy"), ringkasan.SahSampaiTampil);
        Assert.Contains("kode aktivasi", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.True(ringkasan.SelamaTrial);
    }

    [Fact]
    public void Trial_server_memakai_ringkasan_trial_bukan_aktif_berbayar()
    {
        // Token trial server SAH (status aktif, paket "trial"). Menampilkan itu
        // sebagai "Aktif s.d. …" membuat desa mengira sudah membayar Full
        // Service, sementara lencana chipnya Trial — banner harus memakai
        // ringkasan trial dengan tanggal & sisa dari STATUS, bukan trial lokal.
        var berakhir = Sekarang.AddDays(9);
        var status = new StatusLisensi
        {
            Aktif = true,
            NamaDesa = "Sumberjaya",
            Paket = "trial",
            BerlakuSampai = berakhir,
            SahSampai = berakhir,
            SisaHariTenggang = 9,
            CekTerakhir = Sekarang
        };

        var ringkasan = RingkasanLangganan.Dari(status, "SUDES-1");

        Assert.Equal("Trial 14 hari aktif", ringkasan.Judul);
        Assert.Equal(2, ringkasan.Nada);
        Assert.False(ringkasan.TampilTombolTrial);
        Assert.True(ringkasan.SelamaTrial);
        Assert.Equal("Trial 14 hari", ringkasan.PaketNama);
        Assert.Equal("9 hari", ringkasan.SisaHariTampil);
        Assert.Equal(berakhir.ToLocalTime().ToString("dd MMM yyyy"), ringkasan.SahSampaiTampil);
        Assert.Contains("uji coba", ringkasan.Keterangan, StringComparison.Ordinal);
        Assert.Contains("bayar", ringkasan.Keterangan, StringComparison.Ordinal);
        // Sumber tanggal & sisa tetap keterbacaan status: terakhir diperiksa tampil,
        // bukan "—" seperti trial lokal (yang tidak punya status server sama sekali).
        Assert.StartsWith(Sekarang.ToLocalTime().ToString("dd MMM yyyy"),
            ringkasan.TerakhirDiperiksaTampil, StringComparison.Ordinal);
    }

    [Fact]
    public void Keadaan_selain_trial_tidak_ditandai_selama_trial()
    {
        var status = new StatusLisensi
        {
            Aktif = true,
            Paket = "tahunan",
            BerlakuSampai = Sekarang.AddDays(300),
            SahSampai = Sekarang.AddDays(300),
            SisaHariTenggang = 7,
            CekTerakhir = Sekarang
        };

        Assert.False(RingkasanLangganan.Dari(status, "SUDES-1").SelamaTrial);
        Assert.False(RingkasanLangganan.BelumDiperiksa.SelamaTrial);
    }

    [Fact]
    public void Status_kosong_menampilkan_strip_bukan_angka_palsu()
    {
        var ringkasan = RingkasanLangganan.Dari(new StatusLisensi { CekTerakhir = DateTime.MinValue }, null);

        Assert.Equal("—", ringkasan.SahSampaiTampil);
        Assert.Equal("—", ringkasan.SisaHariTampil);
        Assert.Equal("—", ringkasan.TerakhirDiperiksaTampil);
        Assert.Equal("—", ringkasan.PaketNama);

        Assert.Equal("—", RingkasanLangganan.BelumDiperiksa.TerakhirDiperiksaTampil);
        Assert.Equal(0, RingkasanLangganan.BelumDiperiksa.Nada);
    }
}
