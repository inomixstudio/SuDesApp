using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Perdes;

/// <summary>
/// Berkas contoh Perdes bawaan dibuka lewat pipeline yang sama dengan alur klik
/// "buka contoh" di halaman SOTK Desa: pembaca Word menghasilkan paragraf, lalu
/// ekstraksi mengambil tanggal yang menjadi isian awal formulir Buku Perdes
/// (nomor dan "tentang" diambil dari katalog, jadi kecocokannya dengan isi
/// berkas ikut dijaga di sini). Uji ini juga memastikan contoh baru — APBDes
/// dan BPD — benar-benar ikut terpasang di repo dan terbaca seperti contoh
/// lainnya.
/// </summary>
public sealed class ContohPerdesBawaanTests
{
    [Fact]
    public async Task SemuaContohTerbacaDenganNomorDanTanggal()
    {
        string? folder = FolderContoh();
        if (folder == null) return;   // lingkungan terpasang tanpa sumber repo

        var berkas = Directory.GetFiles(folder, "*.docx");
        Assert.NotEmpty(berkas);

        // Dua contoh baru wajib ada; bila berkas hilang, katalog menawarkan
        // tombol yang gagal dibuka.
        Assert.Contains(Path.Combine(folder, "Contoh-Perdes-APBDes.docx"), berkas);
        Assert.Contains(Path.Combine(folder, "Contoh-Perdes-BPD.docx"), berkas);

        var pembaca = new WordFileReaderService(NullLogger<WordFileReaderService>.Instance);
        foreach (var jalur in berkas)
        {
            var isi = await pembaca.ReadAsync(jalur);
            Assert.False(string.IsNullOrWhiteSpace(isi.FullText),
                $"{Path.GetFileName(jalur)} tidak menghasilkan teks.");

            var fields = WordTextParser.ExtractKeputusanFields(isi.FullText, isi.Paragraphs);
            Assert.False(string.IsNullOrWhiteSpace(fields.Nomor),
                $"{Path.GetFileName(jalur)}: nomor Perdes tidak terbaca.");
            Assert.False(string.IsNullOrWhiteSpace(fields.TanggalDdMmYyyy),
                $"{Path.GetFileName(jalur)}: tanggal Perdes tidak terbaca.");
            Assert.False(string.IsNullOrWhiteSpace(fields.Tentang),
                $"{Path.GetFileName(jalur)}: judul 'tentang' tidak terbaca.");
        }
    }

    /// <summary>
    /// Isian awal yang diterima formulir Buku Perdes untuk dua contoh baru
    /// harus persis seperti yang ditetapkan katalog: nomor & judul dari katalog,
    /// tanggal dari isi berkas (pembacaan tanggal inilah yang diandalkan alur
    /// klik di SOTK Desa).
    /// </summary>
    [Fact]
    public async Task ContohAPBDesDanBPDMenghasilkanIsianAwalKatalog()
    {
        string? folder = FolderContoh();
        if (folder == null) return;

        var pembaca = new WordFileReaderService(NullLogger<WordFileReaderService>.Instance);

        var apbdes = await pembaca.ReadAsync(Path.Combine(folder, "Contoh-Perdes-APBDes.docx"));
        var fApbdes = WordTextParser.ExtractKeputusanFields(apbdes.FullText, apbdes.Paragraphs);
        Assert.Equal("1 TAHUN 2026", fApbdes.Nomor);
        Assert.Equal("12-08-2026", fApbdes.TanggalDdMmYyyy);
        Assert.Equal("ANGGARAN PENDAPATAN DAN BELANJA DESA", fApbdes.Tentang);

        var bpd = await pembaca.ReadAsync(Path.Combine(folder, "Contoh-Perdes-BPD.docx"));
        var fBpd = WordTextParser.ExtractKeputusanFields(bpd.FullText, bpd.Paragraphs);
        Assert.Equal("2 TAHUN 2026", fBpd.Nomor);
        Assert.Equal("14-08-2026", fBpd.TanggalDdMmYyyy);
        Assert.Equal("BADAN PERMUSYAWARATAN DESA", fBpd.Tentang);
    }

    /// <summary>Folder Templates/Perdes dari sumber repo, atau null bila tidak ditemukan.</summary>
    private static string? FolderContoh()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
            {
                var folder = Path.Combine(dir.FullName, "Templates", "Perdes");
                return Directory.Exists(folder) ? folder : null;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
