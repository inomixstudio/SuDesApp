using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities;

/// <summary>
/// Preferensi menyimpan hal yang tidak bisa direkonstruksi sendiri — alamat
/// server lisensi, kode aktivasi, pilihan tema. Penulisan yang terpotong tidak
/// boleh menghapus semuanya; berkas lama yang utuh harus tetap terbaca.
/// </summary>
public sealed class BerkasPreferensiTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "sudes-prefs-" + Guid.NewGuid().ToString("N"));

    private string Berkas => Path.Combine(_folder, "prefs.json");

    public BerkasPreferensiTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Pembersihan uji tidak boleh menutupi hasil asersi.
        }
    }

    [Fact]
    public void Tulis_LaluBaca_MengembalikanNilaiYangSama()
    {
        BerkasPreferensi.Tulis(Berkas, "lisensi_url_server", "https://lisensi.contoh.id");
        BerkasPreferensi.Tulis(Berkas, "tema", "gelap");

        Assert.Equal("https://lisensi.contoh.id", BerkasPreferensi.Baca(Berkas, "lisensi_url_server"));
        Assert.Equal("gelap", BerkasPreferensi.Baca(Berkas, "tema"));
        Assert.Null(BerkasPreferensi.Baca(Berkas, "tidak-ada"));
    }

    [Fact]
    public void Tulis_TidakMeninggalkanBerkasSementara()
    {
        BerkasPreferensi.Tulis(Berkas, "a", "1");
        BerkasPreferensi.Tulis(Berkas, "a", "2");

        Assert.False(File.Exists(Berkas + BerkasPreferensi.AkhiranSementara));
        Assert.Equal("2", BerkasPreferensi.Baca(Berkas, "a"));
    }

    [Fact]
    public void Tulis_MempertahankanKunciLain()
    {
        BerkasPreferensi.Tulis(Berkas, "a", "1");
        BerkasPreferensi.Tulis(Berkas, "b", "2");
        BerkasPreferensi.Tulis(Berkas, "a", "3");

        Assert.Equal("3", BerkasPreferensi.Baca(Berkas, "a"));
        Assert.Equal("2", BerkasPreferensi.Baca(Berkas, "b"));
    }

    [Fact]
    public void Baca_BerkasUtamaTerpotong_MemakaiCadangan()
    {
        BerkasPreferensi.Tulis(Berkas, "kode_aktivasi", "ABC-123");
        BerkasPreferensi.Tulis(Berkas, "kode_aktivasi", "DEF-456");

        // Cadangan berisi versi sebelumnya; berkas utama dibuat kosong seperti
        // penulisan lama yang berhenti di tengah jalan.
        Assert.True(File.Exists(Berkas + BerkasPreferensi.AkhiranCadangan));
        File.WriteAllText(Berkas, string.Empty);

        Assert.Equal("ABC-123", BerkasPreferensi.Baca(Berkas, "kode_aktivasi"));
    }

    [Fact]
    public void Tulis_SetelahBerkasUtamaRusak_TidakMenghapusKunciDariCadangan()
    {
        BerkasPreferensi.Tulis(Berkas, "a", "1");
        BerkasPreferensi.Tulis(Berkas, "b", "2");
        File.WriteAllText(Berkas, string.Empty);

        BerkasPreferensi.Tulis(Berkas, "c", "3");

        // Kunci yang masih ada di cadangan tidak ikut hilang; kunci baru ditambahkan.
        Assert.Equal("3", BerkasPreferensi.Baca(Berkas, "c"));
        Assert.Equal("1", BerkasPreferensi.Baca(Berkas, "a"));

        // "b" hanya sempat ada di berkas yang terpotong — bukan di cadangan —
        // jadi memang tidak bisa dipulihkan. Yang penting tidak ada kunci lain
        // yang ikut terhapus hanya karena satu penulisan gagal.
    }

    [Fact]
    public void Baca_NilaiKosong_DibedakanDariKunciTidakAda()
    {
        BerkasPreferensi.Tulis(Berkas, "kosong", string.Empty);

        Assert.Equal(string.Empty, BerkasPreferensi.Baca(Berkas, "kosong"));
        Assert.Null(BerkasPreferensi.Baca(Berkas, "lain"));
    }
}
