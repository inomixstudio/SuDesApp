using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Server;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Server lisensi yang dijalankan lokal (mode Development tanpa Supabase) harus
/// MENYIMPAN langganan, bukan menahannya di memori: pusat administrasi dipakai
/// operator untuk mengelola desa sungguhan (paket, berlaku sampai, perpanjang,
/// tandai lunas), dan datanya tidak boleh hilang begitu server/aplikasi berhenti.
///
/// Uji ini menutup berkas simpanannya lalu membukanya kembali — persis seperti
/// server yang dimatikan lalu dinyalakan lagi oleh aplikasi.
/// </summary>
public sealed class PenyimpananLokalTests : IDisposable
{
    private readonly string _folder;

    public PenyimpananLokalTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sudesapp-penyimpanan-lokal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* sisa berkas uji */ }
    }

    private string Berkas => Path.Combine(_folder, "langganan.json");

    private static PaketLangganan Bulanan => new("bulanan", "Bulanan", 150_000, 30);

    [Fact]
    public async Task LanggananTetapAdaSetelahServerBerhenti()
    {
        string kode = "SUDES-AB12-CD34";

        // === Sesi pertama: operator mengisi desa lewat pusat administrasi ===
        var pertama = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);
        var desa = await pertama.DaftarkanAsync(kode, "Desa Sukamaju", "0812-3456-7890", maxPerangkat: 3);
        Assert.True((await pertama.CatatPerangkatAsync(desa.Id, "perangkat-1")).Bolehkan);

        await pertama.SimpanTagihanAsync(desa.Id, "ORDER-1", Bulanan);
        var perpanjang = await pertama.PerpanjangAsync("ORDER-1", Bulanan.Hari, "lunas", "QRIS 17/10 21:04");
        Assert.True(perpanjang.Diterima);
        Assert.False(perpanjang.Duplikat);

        await pertama.BuatPengumumanAsync("Pemeliharaan", "Server berhenti sebentar.", "info", null, null);
        pertama.Dispose();

        Assert.True(File.Exists(Berkas), "Berkas simpanan tidak dibuat.");

        // === Sesi kedua: server dinyalakan lagi oleh aplikasi ===
        var kedua = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);

        var pelanggan = await kedua.CariByKodeAsync(kode);
        Assert.NotNull(pelanggan);
        Assert.Equal("Desa Sukamaju", pelanggan!.NamaDesa);
        Assert.Equal(3, pelanggan.MaxPerangkat);
        Assert.True(pelanggan.TrialSudah == false);
        Assert.NotNull(pelanggan.Langganan);
        Assert.Equal("bulanan", pelanggan.Langganan!.Paket);
        Assert.NotNull(pelanggan.Langganan.BerlakuSampai);
        Assert.True(pelanggan.Langganan.BerlakuSampai > DateTime.UtcNow.AddDays(25),
            "Masa aktif 30 hari seharusnya masih jauh di depan setelah dibuka ulang.");
        Assert.Equal(1, pelanggan.Langganan.TotalTerbayar);

        // Ringkasan untuk tabel pusat administrasi ikut utuh.
        var daftar = await kedua.DaftarPelangganAsync();
        var ringkas = Assert.Single(daftar);
        Assert.Equal(kode, ringkas.KodeAktivasi);
        Assert.Equal("bulanan", ringkas.Paket);
        Assert.Equal(1, ringkas.JumlahPerangkat);
        Assert.NotNull(ringkas.BerlakuSampai);

        // Perangkat, tagihan, dan pembayaran juga bertahan: "tandai lunas" tidak
        // boleh bisa diulang dan dicatat dua kali hanya karena server dinyalakan
        // ulang (idempotensi order disimpan bersama datanya).
        var perangkat = await kedua.DaftarPerangkatAsync(kode);
        Assert.NotNull(perangkat);
        Assert.Single(perangkat!);

        var tagihan = await kedua.CariTagihanAsync("ORDER-1");
        Assert.NotNull(tagihan);
        Assert.Equal(Bulanan.Harga, tagihan!.Value.Nominal);

        var pembayaran = await kedua.DaftarPembayaranAsync(kode);
        Assert.NotNull(pembayaran);
        var bayar = Assert.Single(pembayaran!);
        Assert.Equal("QRIS 17/10 21:04", bayar.Catatan);

        var ulang = await kedua.PerpanjangAsync("ORDER-1", Bulanan.Hari, "lunas");
        Assert.True(ulang.Duplikat, "Order yang sudah diproses harus tetap dikenali sebagai duplikat.");

        var pengumuman = await kedua.DaftarPengumumanSemuaAsync();
        Assert.Single(pengumuman);
        Assert.Equal("Pemeliharaan", pengumuman[0].Judul);

        kedua.Dispose();
    }

    [Fact]
    public async Task TrialYangSudahDipakaiTetapDitolakSetelahDibukaUlang()
    {
        const string kode = "SUDES-TR14-TR14";

        var pertama = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);
        var desa = await pertama.DaftarkanAsync(kode, "Desa Sumberjaya");
        var trial = await pertama.MulaiTrialAsync(desa.Id, 30, DateTime.UtcNow);
        Assert.True(trial.Boleh);
        pertama.Dispose();

        var kedua = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);
        var lagi = await kedua.CariByKodeAsync(kode);
        Assert.NotNull(lagi);
        Assert.True(lagi!.TrialSudah, "Trial sekali per desa tidak boleh longgar setelah server dinyalakan ulang.");

        var trialKedua = await kedua.MulaiTrialAsync(lagi.Id, 30, DateTime.UtcNow);
        Assert.False(trialKedua.Boleh);
        kedua.Dispose();
    }

    [Fact]
    public async Task BerkasRusakDipindahkanDanServerTetapBisaJalan()
    {
        await File.WriteAllTextAsync(Berkas, "{ ini bukan json }");

        var penyimpanan = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);

        Assert.Empty(await penyimpanan.DaftarPelangganAsync());
        Assert.NotEmpty(Directory.GetFiles(_folder, "langganan.json.rusak-*"));

        // Server tetap bisa dipakai: desa baru tersimpan ke berkas yang baru.
        await penyimpanan.DaftarkanAsync("SUDES-BARU-0001", "Desa Baru");
        penyimpanan.Dispose();

        var kedua = await PenyimpananLokal.BukaAsync(Berkas, NullLogger<PenyimpananLokal>.Instance);
        Assert.Single(await kedua.DaftarPelangganAsync());
        kedua.Dispose();
    }
}
