using System.Reflection;
using System.Text.RegularExpressions;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Mengunci aturan isolating data di <c>schema.sql</c>.
///
/// <para>Skema ini dijalankan server saat start, jadi file-nya bisa berubah
/// tanpa ada yang menyadari — dan dua dari aturan di bawah ini tidak terlihat
/// dari kode C# sama sekali: hak EXECUTE atas fungsi <c>SECURITY DEFINER</c>
/// milik role platform, dan kewajiban kolom <c>tenant_id</c>.</para>
///
/// <para>Aturan yang dijaga di sini:</para>
/// <list type="number">
/// <item>fungsi pencarian lintas-tenant tidak boleh bisa dipanggil role
/// platform (<c>anon</c>/<c>authenticated</c>) — kuncinya menempel di
/// aplikasi desa dan tidak pernah rahasia;</item>
/// <item>tabel tenant tidak boleh terbuka untuk role platform;</item>
/// <item><c>tenant_id</c> wajib diisi dan terikat ke baris pemiliknya.</item>
/// </list>
/// </summary>
public sealed class SkemaLisensiTests
{
    private static readonly string[] TabelTenant =
    {
        "pelanggan", "langganan", "tagihan", "pembayaran", "perangkat"
    };

    private static readonly string[] RolePlatform = { "anon", "authenticated" };

    /// <summary>Isi schema.sql yang tertanam di assembly server.</summary>
    private static string BacaSkema()
    {
        var asm = typeof(SupabaseStore).Assembly;
        var nama = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("schema.sql", StringComparison.Ordinal));
        Assert.NotNull(nama);

        using var stream = asm.GetManifestResourceStream(nama!)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Adakah pernyataan REVOKE yang menyebut <paramref name="objek"/> dan
    /// berakhir dengan "FROM <paramref name="role"/>"?
    /// </summary>
    private static bool AdaRevoke(string skema, string objek, string role)
    {
        var pola = new Regex(
            $@"REVOKE\b[^;]*\b{Regex.Escape(objek)}\b[^;]*\bFROM\s+{Regex.Escape(role)}\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return pola.IsMatch(skema);
    }

    [Fact]
    public void Fungsi_pencarian_lintas_tenant_tidak_bisa_dipanggil_role_platform()
    {
        var skema = BacaSkema();

        // Semua fungsi yang bodies-nya lintas-tenant (SECURITY DEFINER) —
        // daftar ini diambil dari skemanya sendiri, bukan ditulis manual,
        // supaya fungsi baru ikut dijaga. Batas tiap bagian diambil sampai
        // penutup body ("$$"), supaya komentar di antaranya tidak ikut
        // mengaitkan kata "SECURITY DEFINER" ke fungsi yang salah.
        var fungsiLintasTenant = skema
            .Split("CREATE OR REPLACE FUNCTION", StringSplitOptions.None)
            .Skip(1)
            .Where(bagian =>
            {
                var badan = bagian;
                var penutup = badan.IndexOf("$$", StringComparison.Ordinal);
                if (penutup >= 0) badan = badan[..penutup];
                return badan.Contains("SECURITY DEFINER", StringComparison.OrdinalIgnoreCase);
            })
            .Select(bagian => Regex.Match(bagian, @"^\s*(?<nama>\w+)\s*\(")
                .Groups["nama"].Value)
            .Where(nama => nama.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(fungsiLintasTenant);

        foreach (var fungsi in fungsiLintasTenant)
        {
            foreach (var role in RolePlatform)
            {
                Assert.True(
                    AdaRevoke(skema, fungsi, role),
                    $"Fungsi lintas-tenant '{fungsi}' masih bisa dipanggil role '{role}'. " +
                    "RLS tidak berlaku pada fungsi SECURITY DEFINER, jadi role platform " +
                    "bisa membaca data desa lain lewat PostgREST RPC.");
            }
        }
    }

    [Fact]
    public void Tabel_tenant_tidak_terbuka_untuk_role_platform()
    {
        var skema = BacaSkema();

        foreach (var tabel in TabelTenant)
        {
            foreach (var role in RolePlatform)
            {
                Assert.True(
                    AdaRevoke(skema, tabel, role),
                    $"Tabel tenant '{tabel}' belum dicabut haknya dari role '{role}'. " +
                    "RLS menutupnya selama GUC app.tenant_id tidak bisa disetel, " +
                    "tetapi hak tulis yang tersisa jadi jebakan bila kebijakan RLS longgar.");
            }
        }
    }

    [Fact]
    public void Pengumuman_tetap_bisa_dibaca_publik()
    {
        var skema = BacaSkema();

        // Pengumuman memang dimaksudkan publik (dibaca aplikasi desa tanpa
        // kunci), jadi cabut hak yang lebih dari SELECT tidak boleh ikut
        // diterapkan ke tabel ini.
        foreach (var role in RolePlatform)
        {
            Assert.True(
                AdaRevoke(skema, "pengumuman", role),
                "Hak tulis role platform atas pengumuman seharusnya dicabut.");
        }
    }

    [Fact]
    public void Kolom_tenant_id_wajib_diisi_dan_terikat_pemilik()
    {
        var skema = BacaSkema();

        foreach (var tabel in TabelTenant)
        {
            Assert.True(
                Regex.IsMatch(skema,
                    $@"ALTER TABLE\s+{Regex.Escape(tabel)}\s+ALTER COLUMN tenant_id SET NOT NULL",
                    RegexOptions.IgnoreCase),
                $"Kolom tenant_id pada tabel '{tabel}' tidak wajib diisi — " +
                "satu INSERT yang lupa mengisinya akan menulis baris tanpa pemilik.");
        }

        // CHECK inilah yang mencegah baris mengaku milik desa lain.
        Assert.Contains("pelanggan_tenant_sama", skema, StringComparison.Ordinal);
        Assert.Contains("langganan_tenant_sama", skema, StringComparison.Ordinal);
        Assert.Contains("tagihan_tenant_sama", skema, StringComparison.Ordinal);
        Assert.Contains("pembayaran_tenant_sama", skema, StringComparison.Ordinal);
        Assert.Contains("perangkat_tenant_sama", skema, StringComparison.Ordinal);
    }

    [Fact]
    public void Peran_server_bukan_peran_khusus_dan_memang_boleh_menjalankan_fungsi_pencarian()
    {
        var skema = BacaSkema();

        Assert.Contains("NOBYPASSRLS", skema, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON FUNCTION dir_cari_pelanggan(text) TO sudes_server",
            skema, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON FUNCTION dir_daftar_pelanggan() TO sudes_server",
            skema, StringComparison.Ordinal);
    }
}

/// <summary>
/// Aturan trial yang WAJIB sama antara penyimpanan memori dan Supabase:
/// server memanggil salah satunya, jadi kalau keduanya berbeda, satu jalur
/// diam-diam memberi trial berbeda dari yang dijanjikan pengguna.
/// </summary>
public sealed class AturanTrialTests
{
    [Fact]
    public async Task Desa_tak_dikenal_tidak_bisa_mendapat_trial()
    {
        var repo = new LanggananMemori();
        await repo.InitializeAsync();

        var hasil = await repo.MulaiTrialAsync("id-yang-tidak-ada", 30, DateTime.UtcNow);

        Assert.False(hasil.Boleh);
        Assert.Null(hasil.BerlakuSampai);
    }

    [Fact]
    public async Task Trial_hanya_satu_kali_per_desa()
    {
        var repo = new LanggananMemori();
        await repo.InitializeAsync();
        var desa = await repo.DaftarkanAsync("DESAUJI", "Desa Uji");
        var sekarang = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

        var pertama = await repo.MulaiTrialAsync(desa.Id, 30, sekarang);
        Assert.True(pertama.Boleh);
        Assert.Equal(sekarang.AddDays(30), pertama.BerlakuSampai);

        var kedua = await repo.MulaiTrialAsync(desa.Id, 30, sekarang.AddDays(1));
        Assert.False(kedua.Boleh);
        Assert.Contains("sudah pernah dipakai", kedua.Pesan ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trial_ditolak_saat_langganan_masih_aktif()
    {
        var repo = new LanggananMemori();
        await repo.InitializeAsync();
        var desa = await repo.DaftarkanAsync("DESAUJI", "Desa Uji");
        var paket = new PaketLangganan("bulanan", "Bulanan", 150_000, 30);

        // Langganan dibayar lebih dulu (mis. transfer manual), masih aktif.
        await repo.SimpanTagihanAsync(desa.Id, "ORDER-1", paket);
        var bayar = await repo.PerpanjangAsync("ORDER-1", paket.Hari, "manual");
        Assert.True(bayar.Diterima);

        var hasil = await repo.MulaiTrialAsync(desa.Id, 30, DateTime.UtcNow);

        Assert.False(hasil.Boleh);
        Assert.Contains("masih aktif", hasil.Pesan ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
