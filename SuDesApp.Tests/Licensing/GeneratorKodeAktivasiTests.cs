using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Kode aktivasi otomatis adalah identitas desa yang dikirim lewat WhatsApp,
/// jadi bentuknya harus bisa dibacakan tanpa salah ketik: format tetap
/// <c>SUDES-XXXX-XXXX</c> dan alfabetnya tanpa karakter rancu (0/O, 1/I/L).
/// Uji ini juga menjaga bahwa pendaftaran dengan kode otomatis benar-benar
/// menghasilkan kode yang tersimpan — bukan kode yang hanya tampil di layar.
/// </summary>
public sealed class GeneratorKodeAktivasiTests
{
    private static readonly Regex Pola = new(
        "^SUDES-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{4}-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{4}$",
        RegexOptions.Compiled);

    [Fact]
    public void Kode_selalu_mengikuti_pola_SUDES_XXXX_XXXX()
    {
        for (var i = 0; i < 500; i++)
        {
            Assert.Matches(Pola, GeneratorKodeAktivasi.Buat());
        }
    }

    [Fact]
    public void Alfabet_tidak_memuat_karakter_rancu()
    {
        Assert.Equal(31, GeneratorKodeAktivasi.Alfabet.Length);
        Assert.DoesNotContain('0', GeneratorKodeAktivasi.Alfabet);
        Assert.DoesNotContain('O', GeneratorKodeAktivasi.Alfabet);
        Assert.DoesNotContain('1', GeneratorKodeAktivasi.Alfabet);
        Assert.DoesNotContain('I', GeneratorKodeAktivasi.Alfabet);
        Assert.DoesNotContain('L', GeneratorKodeAktivasi.Alfabet);
    }

    [Fact]
    public void Sumber_acak_yang_sama_menghasilkan_kode_yang_sama()
    {
        Assert.Equal(
            GeneratorKodeAktivasi.Buat(new Random(42)),
            GeneratorKodeAktivasi.Buat(new Random(42)));
    }

    [Fact]
    public void Ribuan_kode_tidak_berbenturan()
    {
        var sudahDibuat = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var acak = new Random(12345);
        for (var i = 0; i < 10_000; i++)
        {
            Assert.True(sudahDibuat.Add(GeneratorKodeAktivasi.Buat(acak)));
        }
    }

    [Fact]
    public async Task DaftarkanDenganKodeBaruAsync_mendaftarkan_kode_unik_yang_tersimpan()
    {
        var repo = new LanggananMemori();
        var admin = new LayananAdmin(repo, NullLogger<LayananAdmin>.Instance);

        var p = await admin.DaftarkanDenganKodeBaruAsync("Desa Sumberjaya", "0812-000-000", 3);

        Assert.Matches(Pola, p.KodeAktivasi);
        Assert.Equal("Desa Sumberjaya", p.NamaDesa);
        Assert.Equal(3, p.MaxPerangkat);

        var tersimpan = await repo.CariByKodeAsync(p.KodeAktivasi);
        Assert.NotNull(tersimpan);
        Assert.Equal(p.KodeAktivasi, tersimpan.KodeAktivasi);
        Assert.Equal(p.NamaDesa, tersimpan.NamaDesa);
    }

    [Fact]
    public async Task DaftarkanDenganKodeBaruAsync_tanpa_nama_desa_ditolak()
    {
        var repo = new LanggananMemori();
        var admin = new LayananAdmin(repo, NullLogger<LayananAdmin>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => admin.DaftarkanDenganKodeBaruAsync("   ", "", 2));
    }
}