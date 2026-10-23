using SuDesApp.Data.Models;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Penomoran;

/// <summary>
/// Aturan penomoran surat: pola nomor, uraian nomor terbit, dan pemeriksaan
/// keutuhan deret untuk tutup buku tahunan.
/// </summary>
public sealed class PenomoranNomorTests
{
    private const string PolaBawaan = "470/{0:D3}/Ds/{2:yyyy}";

    /// <summary>Pola dengan kode jenis pada segmen kedua, mis. "470/SKD/001/…".</summary>
    private const string PolaKodeJenis = "470/{1}/{0:D3}/Ds/{2:yyyy}";

    // ================= UraiNomor =================

    [Theory]
    [InlineData("470/001/Ds/2026", "470", 1, 2026)]
    [InlineData("470/015/Ds/2026", "470", 15, 2026)]
    [InlineData("474.3/007/Ds/2026", "474.3", 7, 2026)]
    [InlineData("TMPL/001/Ds/2026", "TMPL", 1, 2026)]
    public void Urai_nomor_bawaan_membaca_awalan_urut_dan_tahun(
        string nomor, string awalan, int urut, int tahun)
    {
        var hasil = VerifikasiPenomoranService.UraiNomor(nomor);

        Assert.NotNull(hasil);
        Assert.Equal(awalan, hasil!.Awalan);
        Assert.Equal(urut, hasil.Nomor);
        Assert.Equal(tahun, hasil.Tahun);
    }

    [Fact]
    public void Urai_nomor_membaca_urut_walau_pola_menyisipkan_kode_jenis()
    {
        // Pola "470/{1}/{0:D3}/Ds/{2:yyyy}" menurunkan nomor "470/SKD/001/Ds/2026".
        // Nomor urut ada di segmen ketiga, bukan kedua.
        var hasil = VerifikasiPenomoranService.UraiNomor("470/SKD/001/Ds/2026");

        Assert.NotNull(hasil);
        Assert.Equal("470", hasil!.Awalan);
        Assert.Equal(1, hasil.Nomor);
        Assert.Equal(2026, hasil.Tahun);
    }

    [Fact]
    public void Urai_nomor_tanpa_segmen_tahun_tetap_dibaca_sebagai_urut()
    {
        var hasil = VerifikasiPenomoranService.UraiNomor("470/001");

        Assert.NotNull(hasil);
        Assert.Equal("470", hasil!.Awalan);
        Assert.Equal(1, hasil.Nomor);
        Assert.Null(hasil.Tahun);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("470")]
    [InlineData("ABC/DEF")]
    [InlineData("/001/Ds/2026")]
    public void Urai_nomor_tidak_terbaca_kembalikan_null(string nomor)
    {
        Assert.Null(VerifikasiPenomoranService.UraiNomor(nomor));
    }

    // ================= Analisis (tutup buku) =================

    [Fact]
    public void Deret_dengan_pola_kode_jenis_tidak_memblokir_tutup_buku()
    {
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "470/SKD/001/Ds/2026", TanggalSurat = "2026-01-10", NamaJenis = "SKD_UMUM" },
            new() { NomorSurat = "470/SKD/002/Ds/2026", TanggalSurat = "2026-02-11", NamaJenis = "SKD_UMUM" },
            new() { NomorSurat = "470/SKD/003/Ds/2026", TanggalSurat = "2026-03-12", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2026]);

        Assert.False(hasil.AdaMasalahBlocking);
        Assert.False(hasil.AdaNomorTidakTerbaca);
        var deret = Assert.Single(hasil.Deret);
        Assert.Equal("470", deret.Awalan);
        Assert.Equal(3, deret.Jumlah);
        Assert.Equal(1, deret.NomorPertama);
        Assert.Equal(3, deret.NomorTerakhir);
    }

    [Fact]
    public void Deret_dengan_pola_bawaan_tetap_rapi()
    {
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "470/001/Ds/2026", TanggalSurat = "2026-01-10", NamaJenis = "SKD_UMUM" },
            new() { NomorSurat = "470/002/Ds/2026", TanggalSurat = "2026-02-11", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2026]);

        Assert.False(hasil.AdaMasalahBlocking);
        var deret = Assert.Single(hasil.Deret);
        Assert.Equal(2, deret.Jumlah);
        Assert.True(deret.MulaiDariSatu);
    }

    [Fact]
    public void Nomor_ganda_masih_memblokir_tutup_buku()
    {
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "470/001/Ds/2026", TanggalSurat = "2026-01-10", NamaJenis = "SKD_UMUM" },
            new() { NomorSurat = "470/001/Ds/2026", TanggalSurat = "2026-01-11", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2026]);

        Assert.True(hasil.AdaMasalahBlocking);
        Assert.True(hasil.AdaNomorGanda);
    }

    [Fact]
    public void Jumlah_surat_menghitung_semua_surat_walau_nomor_tak_terbaca()
    {
        // Nomor tak terbaca tidak masuk deret mana pun, tetapi TIDAK boleh
        // membuat jumlah surat tahun ini terlihat kosong — kalau begitu tutup
        // buku akan melaporkan "tidak ada surat" padahal register penuh.
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "470/001/Ds/2026", TanggalSurat = "2026-01-10", NamaJenis = "SKD_UMUM" },
            new() { NomorSurat = "ABC/DEF", TanggalSurat = "2026-01-11", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2026]);

        Assert.Equal(1, hasil.JumlahSeluruhSurat);
        Assert.True(hasil.AdaNomorTidakTerbaca);
        Assert.True(hasil.AdaMasalahBlocking);
    }

    [Fact]
    public void Ringkasan_deret_tak_terbaca_tidak_mengaku_tidak_ada_surat()
    {
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "ABC/DEF", TanggalSurat = "2026-01-11", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2026]);

        var deret = Assert.Single(hasil.Deret);
        Assert.Contains("tak terbaca", deret.Ringkasan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("belum ada surat", deret.Ringkasan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tahun_pada_nomor_yang_tidak_cocok_dengan_tanggal_memblokir_tutup_buku()
    {
        var baris = new List<BarisNomorTerbit>
        {
            new() { NomorSurat = "470/001/Ds/2025", TanggalSurat = "2026-01-10", NamaJenis = "SKD_UMUM" },
        };

        var hasil = VerifikasiPenomoranService.Analisis(2026, baris, [2025, 2026]);

        Assert.True(hasil.AdaMasalahBlocking);
        Assert.True(hasil.AdaTahunTidakCocok);
    }

    // ================= Pola & penyusunan nomor =================

    [Fact]
    public void Format_valid_menerima_pola_dengan_kode_jenis()
    {
        Assert.True(PenomoranSuratService.FormatValid(PolaKodeJenis, out var pesan));
        Assert.Equal(string.Empty, pesan);
    }

    [Fact]
    public void Format_valid_menolak_pola_tanpa_nomor_urut()
    {
        Assert.False(PenomoranSuratService.FormatValid("470/{1}/Ds/{2:yyyy}", out var pesan));
        Assert.NotEqual(string.Empty, pesan);
    }

    [Fact]
    public void Susun_nomor_mengisi_urut_kode_jenis_dan_tahun()
    {
        Assert.Equal("470/007/Ds/2026",
            PenomoranSuratService.SusunNomor(PolaBawaan, 7, "SKD", "2026"));

        Assert.Equal("470/SKD/007/Ds/2026",
            PenomoranSuratService.SusunNomor(PolaKodeJenis, 7, "SKD", "2026"));
    }

    [Fact]
    public void Tahun_pada_pola_dikirim_sebagai_teks_agar_tahun_tertulis_benar()
    {
        // Argumen tahun bertipe angka akan menghasilkan teks literal "yyyy".
        Assert.Equal("470/001/Ds/2026",
            PenomoranSuratService.SusunNomor(PolaBawaan, 1, "SKD", "2026"));
    }

    [Fact]
    public void Contoh_nomor_menampilkan_kode_jenis_sesuai_yang_akan_terbit()
    {
        Assert.Equal("470/001/Ds/2026",
            PenomoranSuratService.Contoh(PolaBawaan, 1, 2026, "SKD"));

        Assert.Equal("470/SKD/001/Ds/2026",
            PenomoranSuratService.Contoh(PolaKodeJenis, 1, 2026, "SKD"));
    }

    [Theory]
    [InlineData(PolaBawaan, "470/001/Ds/2026", 1)]
    [InlineData(PolaBawaan, "470/015/Ds/2026", 15)]
    [InlineData(PolaKodeJenis, "470/SKD/001/Ds/2026", 1)]
    [InlineData(PolaKodeJenis, "470/SKD/015/Ds/2026", 15)]
    public void Urut_dari_nomor_membaca_nomor_urut_dari_pola_aktif(
        string pola, string nomor, int urut)
    {
        Assert.Equal(urut, PenomoranSuratService.UrutDariNomor(pola, nomor, "2026"));
    }

    [Fact]
    public void Urut_dari_nomor_mengabaikan_nomor_dari_tahun_lain()
    {
        Assert.Null(PenomoranSuratService.UrutDariNomor(PolaBawaan, "470/001/Ds/2025", "2026"));
    }
}
