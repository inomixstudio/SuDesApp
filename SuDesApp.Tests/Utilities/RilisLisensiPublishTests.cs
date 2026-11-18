using System;
using System.IO;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Penjaga alat penerbitan aset desktop (<c>Tools/publish-release.ps1</c>) untuk
/// mode lisensi lokal.
///
/// <para>
/// Rilis desktop ditolak selama <c>Lisensi:UrlServer</c> bukan HTTPS produksi dan
/// kuncinya belum dicocokkan dengan <c>/kunci-publik</c>. Paket 2.15.0 yang sudah
/// beredar ternyata membawa <c>http://127.0.0.1:5110</c> (server lisensi dijalankan
/// aplikasi di komputer desa), sehingga rilis berikutnya butuh jalur yang sama —
/// tetapi hanya lewat keputusan eksplisit <c>-IzinkanLisensiLokal</c>. Tes ini
/// mengunci bahwa saklar itu (1) tetap sempit, (2) kunci publiknya masih divalidasi
/// beneran, dan (3) keadaannya benar-benar sampai ke RELEASE.md dan UNGGAH.txt
/// sehingga halaman rilis tidak bisa tampil seolah paket tersambung ke produksi.
/// </para>
/// <para>
/// Poin (3) menutup bug yang tidak bersuara: PowerShell 5.1 tidak melempar untuk
/// variabel yang belum didefinisikan, jadi mendefinisikan <c>$statusLisensi</c> di
/// bawah pemakaiannya membuat baris status hilang tanpa galat apa pun.
/// </para>
/// </summary>
public sealed class RilisLisensiPublishTests
{
    private static readonly string AkarRepo = CariAkarRepo();
    private static readonly string Naskah = Path.Combine(AkarRepo, "Tools", "publish-release.ps1");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("SuDesApp.slnx tidak ditemukan.");
    }

    private static string Baca() => File.ReadAllText(Naskah).Replace("\r\n", "\n");

    [Fact]
    public void Saklar_LisensiLokal_DideklarasikanTerang()
    {
        var t = Baca();
        Assert.Contains("[switch]$IzinkanLisensiLokal,", t, StringComparison.Ordinal);
        // Pesan penolakan tanpa saklar wajib menyebut saklarnya, supaya operator
        // tidak "menemukan" cara lain (mis. mengedit appsettings diam-diam).
        Assert.Contains("-IzinkanLisensiLokal", t, StringComparison.Ordinal);
        Assert.Contains("jalankan ulang dengan -IzinkanLisensiLokal", t, StringComparison.Ordinal);
    }

    [Fact]
    public void Saklar_BukanPelewatUmum_HttpKeHostLainTetapDitolak()
    {
        var t = Baca();
        Assert.Contains("if (-not $hostLoopback)", t, StringComparison.Ordinal);
        Assert.Contains("hanya menerima loopback", t, StringComparison.Ordinal);
        // Host .local dan URL berpath juga tetap ditolak dalam mode lokal.
        Assert.Contains("$hostLocalName -or -not $bentukSama", t, StringComparison.Ordinal);
    }

    [Fact]
    public void KunciPublik_TetivalidasiDanTidakPakaiApiYangHilangDiPowerShell51()
    {
        var t = Baca();
        Assert.Contains("Test-KunciP256", t, StringComparison.Ordinal);
        Assert.Contains("bukan kunci ES256 yang sah", t, StringComparison.Ordinal);
        // ImportSubjectPublicKeyInfo tidak ada di .NET Framework (Windows PowerShell
        // 5.1) — memakainya membuat validasi selalu gagal/empty dan rilis macet.
        // Yang dilarang expressly adalah PANGGILANNYA; komentar yang menjelaskan
        // alasannya justru harus tinggal.
        Assert.DoesNotContain(".ImportSubjectPublicKeyInfo(", t, StringComparison.Ordinal);
        // Bentuk SPKI P-256 diperiksa lewat panjang + kepala DER (prime256v1).
        Assert.Contains("$der.Length -ne 91", t, StringComparison.Ordinal);
        // Ekor kepala SPKI P-256: ...06 08 2A 86 48 CE 3D 03 01 07 | 03 42 00 | 04
        // (OID prime256v1, bit string 66 byte, titik publik uncompressed).
        Assert.Contains("0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04", t, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLisensi_DidefinisikanSebelumDipakai()
    {
        var t = Baca();
        int definisi = t.IndexOf("$statusLisensi = @(", StringComparison.Ordinal);
        int pemakaian = t.IndexOf("$lines += $statusLisensi", StringComparison.Ordinal);
        Assert.True(definisi >= 0, "definisi $statusLisensi tidak ditemukan");
        Assert.True(pemakaian >= 0, "pemakaian $lines += $statusLisensi tidak ditemukan");
        Assert.True(definisi < pemakaian,
            "$statusLisensi dipakai sebelum didefinisikan - baris status akan hilang tanpa galat.");
    }

    [Fact]
    public void ModeLokal_TercatatDiManifesDanDaftarUnggah()
    {
        var t = Baca();
        Assert.Contains("Lisensi: MODE LOKAL", t, StringComparison.Ordinal);
        Assert.Contains("Paket ini MODE LISENSI LOKAL", t, StringComparison.Ordinal);
        Assert.Contains("SERVER PRODUKSI", t, StringComparison.Ordinal);
    }

    [Fact]
    public void Produksi_JalurLamaMasihDiperiksaKunciOnline()
    {
        // Menambah mode lokal tidak boleh melonggarkan jalur produksi: pencocokan
        // /kunci-publik dan penolakan HTTPS-kurang harus tetap ada di cabangnya.
        var t = Baca();
        Assert.Contains("if (-not $modeLisensiLokal) {", t, StringComparison.Ordinal);
        Assert.Contains("/kunci-publik", t, StringComparison.Ordinal);
        Assert.Contains("Pastikan server produksi aktif.", t, StringComparison.Ordinal);
    }
}
