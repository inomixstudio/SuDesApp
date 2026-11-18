using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Sinkron;
using SuDesApp.Supabase;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// JAGA-KONTRAK kode pemasangan (kode 4 angka + QR) desktop → Android.
///
/// QR hanya memuat dua baris: URL project dan kode. Ponsel menukar kodenya lewat
/// RPC <c>public.ambil_pasangan</c>. Kontrak yang rapuh di sini tidak memunculkan
/// galat yang jujur: kalau URL di baris pertama tidak berbentuk yang diterima
/// ponsel, desktop sudah terlanjur membuat akun Auth + baris perangkat dan kode
/// yang diterbitkan pasti gagal ditebus — operator hanya melihat "kode/QR tidak
/// bisa dipakai". Karena itu bentuk URL diperiksa desktop SEBELUM ada kerja ke
/// Supabase, dan aturan ponsel dipakai sebagai pembanding di tes ini.
/// </summary>
public sealed class KodePasanganSupabaseTests
{
    private static readonly string AkarRepo = CariAkarRepo();

    private static readonly string LoginAndroid = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes",
        "android", "LoginActivity.kt");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("SuDesApp.slnx tidak ditemukan.");
    }

    private static SupabasePengaturan PengaturanLengkap(
        string urlRest,
        string host = "127.0.0.1",
        int port = 1) => new()
    {
        // Host sengaja port tertutup di loopback: kalau ada yang lupa bahwa
        // gerbang URL jalan SEBELUM koneksi, tes ini gagal cepat (connection
        // refused), bukan menggantung ke server orang lain.
        Host = host,
        Port = port,
        Pengguna = "postgres.abcdefghijklmno",
        KataSandi = "sandi-pooler",
        Tenant = "abcdefghijklmno",
        UrlRest = urlRest,
        KunciPublik = "anon-key",
    };

    /// <summary>Ambil beberapa baris sumber di sekitar pola (untuk penjaga bentuk sumber).</summary>
    private static string BlokSekitar(string sumber, string pola, int jumlahBaris)
    {
        var baris = sumber.Replace("\r\n", "\n").Split('\n');
        var i = Array.FindIndex(baris, b => b.Contains(pola, StringComparison.Ordinal));
        Assert.True(i >= 0, pola + " tidak ditemukan di sumber");
        return string.Join("\n", baris.Skip(i).Take(jumlahBaris));
    }

    // ------------------------------------------------------------------
    // Bentuk URL yang masuk baris pertama QR
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("https://abcdefghijklmno.supabase.co")]
    [InlineData("https://abcdefghijklmno.supabase.co/")]
    [InlineData("  https://abcdefghijklmno.supabase.co  ")]
    [InlineData("HTTPS://AbcdefghIJKLMNo.Supabase.CO/")]
    // Nama kuncinya "UrlRest", jadi basis REST yang ditulis adalah kekeliruan
    // paling mungkin — nilai ini harus dipulihkan, bukan ditolak.
    [InlineData("https://abcdefghijklmno.supabase.co/rest/v1")]
    [InlineData("https://abcdefghijklmno.supabase.co/rest/v1/")]
    [InlineData("https://abcdefghijklmno.supabase.co/auth/v1")]
    [InlineData("https://abcdefghijklmno.supabase.co/rest")]
    public void PeriksaUrlProject_menerimaDanMembersihkan(string nilai)
    {
        var hasil = PerangkaianPerangkat.PeriksaUrlProject(nilai);

        Assert.True(hasil.Layak, hasil.PesanGalat);
        Assert.Equal("https://abcdefghijklmno.supabase.co", hasil.Url);
    }

    [Theory]
    [InlineData("", "Supabase:UrlRest")]
    [InlineData("   ", "Supabase:UrlRest")]
    [InlineData("abcdefghijklmno.supabase.co", "https")]
    [InlineData("http://abcdefghijklmno.supabase.co", "https")]
    [InlineData("https://10.8.0.5/rest/v1", ".supabase.co")]
    [InlineData("https://palsu.example.com", ".supabase.co")]
    [InlineData("https://supabase.co", ".supabase.co")]
    [InlineData("https://abcdefghijklmno.supabase.co/api/rest/v1", "tanpa path")]
    [InlineData("https://abcdefghijklmno.supabase.co?apikey=x", "tanpa path")]
    public void PeriksaUrlProject_menolakDenganPesanYangMenunjukKunci(string nilai, string kataKunci)
    {
        var hasil = PerangkaianPerangkat.PeriksaUrlProject(nilai);

        Assert.False(hasil.Layak);
        Assert.Null(hasil.Url);
        Assert.Contains(kataKunci, hasil.PesanGalat, StringComparison.OrdinalIgnoreCase);
        // Pesan wajib menyebut kunci konfigurasi yang harus diisi operator.
        Assert.Contains("Supabase:UrlRest", hasil.PesanGalat, StringComparison.Ordinal);
    }

    [Fact]
    public void PeriksaUrlProject_nilaiNullTidakMelempar()
    {
        var hasil = PerangkaianPerangkat.PeriksaUrlProject(null);

        Assert.False(hasil.Layak);
        Assert.Contains("belum diisi", hasil.PesanGalat, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Paritas dengan aturan yang dipakai ponsel saat membaca QR
    // ------------------------------------------------------------------

    /// <summary>Predikat yang sama dengan <c>LoginActivity.dariQr</c>.</summary>
    private static bool PonselMenerimaUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        url.TrimEnd('/').EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Desktop_hanyaMenerbitkanKodeUntukUrlYangDiterimaPonsel()
    {
        var kandidat = new[]
        {
            "https://abcdefghijklmno.supabase.co",
            "https://abcdefghijklmno.supabase.co/rest/v1",
            "https://abcdefghijklmno.supabase.co/auth/v1/",
            "https://abcdefghijklmno.supabase.co/",
        };

        foreach (var nilai in kandidat)
        {
            var hasil = PerangkaianPerangkat.PeriksaUrlProject(nilai);
            Assert.True(hasil.Layak, nilai + " seharusnya layak: " + hasil.PesanGalat);
            Assert.True(PonselMenerimaUrl(hasil.Url!), nilai + " menghasilkan URL yang ditolak ponsel: " + hasil.Url);
        }
    }

    [Fact]
    public void Ponsel_masihMenolakUrlYangDesktopPulaMenolak()
    {
        // Kalau salah satu sisi melonggarkan aturannya, pasangan ini pecah:
        // desktop menerbitkan kode yang ponsel tolak (atau sebaliknya, ponsel
        // menerima URL phishing). Tes ini mengunci kedua sisi sekaligus.
        var sumber = File.ReadAllText(LoginAndroid);
        Assert.Contains(".supabase.co", sumber, StringComparison.Ordinal);
        Assert.Contains("startsWith(\"https://\"", sumber, StringComparison.Ordinal);

        foreach (var nilai in new[] { "http://abcdefghijklmno.supabase.co", "https://palsu.example.com" })
        {
            var hasil = PerangkaianPerangkat.PeriksaUrlProject(nilai);
            Assert.False(hasil.Layak, nilai + " seharusnya ditolak desktop");
            Assert.False(PonselMenerimaUrl(nilai), nilai + " seharusnya ditolak ponsel");
        }
    }

    [Fact]
    public void PayloadQR_pakaiHasilPemeriksaanUrlProject()
    {
        // QR disusun di halaman Sinkron; ia wajib memakai penyervis yang sama
        // dengan gerbang pemasangan supaya baris pertama QR bukan nilai mentah.
        var vm = Path.Combine(
            AkarRepo, "SuDesApp.Wpf", "ViewModels", "SinkronViewModel.cs");
        var blok = BlokSekitar(File.ReadAllText(vm), "public string QrPayload", 10);

        Assert.Contains("PeriksaUrlProject", blok, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Kode dan kata sandi
    // ------------------------------------------------------------------

    [Fact]
    public void BuatKode_selaluEmpatDigit()
    {
        for (var i = 0; i < 400; i++)
        {
            var kode = PerangkaianPerangkat.BuatKode();
            Assert.Equal(4, kode.Length);
            Assert.Matches("^[0-9]{4}$", kode);
        }
    }

    [Fact]
    public void BuatKode_tidakSeragamDanNolDepanBoleh()
    {
        var himpunan = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            himpunan.Add(PerangkaianPerangkat.BuatKode());
        }

        // RNG per digit (bukan modulo byte): masih 4 digit, tetapi spread-nya
        // tidak boleh runtuh ke sedikit nilai.
        Assert.True(himpunan.Count > 400, "kode terlalu sering berulang: " + himpunan.Count);
    }

    [Fact]
    public void BuatKode_nolDiDepanDipertahankanSebagaiString()
    {
        // Kode dibaca manusia dan kolomnya text; "0007" tidak boleh jadi "7".
        // Satu-satunya cara memastikan itu tanpa menstabilkan RNG: periksa
        // panjang, karena pangkas angka depan akan mengurangi digit.
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^[0-9]{4}$", PerangkaianPerangkat.BuatKode());
        }
    }

    [Theory]
    [InlineData(20)]
    [InlineData(8)]
    public void BuatKataSandi_hanyaAlfabetTanpaBisaSalahBaca(int panjang)
    {
        var sandi = PerangkaianPerangkat.BuatKataSandi(panjang);

        Assert.Equal(panjang, sandi.Length);
        Assert.Matches("^[a-km-np-zA-HJ-NP-Z2-9]+$", sandi);
        // Tidak ada karakter yang mudah tertukar (0/O, 1/l/I).
        Assert.DoesNotContain("0", sandi);
        Assert.DoesNotContain("O", sandi);
        Assert.DoesNotContain("1", sandi);
        Assert.DoesNotContain("l", sandi);
        Assert.DoesNotContain("I", sandi);
    }

    // ------------------------------------------------------------------
    // Gerbang sebelum ada kerja ke Supabase
    // ------------------------------------------------------------------

    [Fact]
    public async Task PasangAsync_tolakUrlRestKosongSebelumSentuhSupabase()
    {
        var layanan = new PerangkaianPerangkat(
            PengaturanLengkap(urlRest: ""),
            NullLogger<PerangkaianPerangkat>.Instance);

        // Konfigurasi "Lengkap" (Host/Pengguna/KataSandi/Tenant terisi) tetapi
        // UrlRest kosong. Tidak boleh ada koneksi percobaan: kalau kode
        // diterbitkan di sini, akun Auth dan baris perangkat tercipta untuk QR
        // yang pasti ditolak ponsel.
        var hasil = await layanan.PasangAsync("Hp 1");

        Assert.False(hasil.Berhasil);
        Assert.Null(hasil.Kode);
        Assert.Contains("Supabase:UrlRest", hasil.PesanGalat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasangAsync_menerimaUrlBentukBasisRestDanTetapCoba()
    {
        var layanan = new PerangkaianPerangkat(
            PengaturanLengkap("https://abcdefghijklmno.supabase.co/rest/v1"),
            NullLogger<PerangkaianPerangkat>.Instance);

        var hasil = await layanan.PasangAsync("Hp 2");

        // Gerbang URL harus LULUS di bentuk ini, jadi kegagalan yang tersisa
        // adalah kegagalan jaringan/Supabase — bukan penolakan URL.
        Assert.DoesNotContain("Supabase:UrlRest", hasil.PesanGalat ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Lengkap_tidakMenjaminQrDipakai_sehinggaGerbangUrlHarusAda()
    {
        // Dokumen lama menyebut "QR berisi URL Supabase, surel, dan kata sandi".
        // Itu sudah tidak benar (dan tidak boleh benar): kredensial ditukar lewat
        // ambil_pasangan. Tes ini mengunci dokumen agar tidak kembali menyesatkan.
        var sumber = Path.Combine(
            AkarRepo, "SuDesApp.Core", "Sinkron", "PerangkaianPerangkat.cs");
        var isi = File.ReadAllText(sumber);

        Assert.DoesNotContain("QR berisi URL Supabase, surel, dan kata sandi", isi,
            StringComparison.Ordinal);
        Assert.Contains("HANYA memuat URL project dan kode verifikasi", isi,
            StringComparison.Ordinal);

        // Lengkap = Host + Pengguna + KataSandi + Tenant. Karena UrlRest TIDAK
        // termasuk di dalamnya, gerbang pemasangan wajib memeriksa URL secara
        // terpisah; kalau nanti ada yang memaknai Lengkap sebagai "siap pasang",
        // QR kosong kembali lahir.
        var p = PengaturanLengkap(urlRest: "");
        Assert.True(p.Lengkap);
        Assert.True(string.IsNullOrWhiteSpace(p.UrlRest));
    }
}
