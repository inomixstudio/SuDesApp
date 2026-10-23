using System.IO.Compression;
using System.Xml.Linq;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Perdes;

/// <summary>
/// Contoh Perdes bawaan memuat identitas desa lain (Sumberjaya / Kec. Tempuran /
/// Kab. Karawang) yang tertanam di dalam teks Word. Yang dijaga di sini: saat
/// contoh dibuka dari menu SOTK Desa, SALINannya benar-benar terisi identitas
/// desa/kabupaten dari Pengaturan Surat — termasuk frasa komposit, huruf kapital
/// pada kepala surat, dan gelar Bupati/Wali Kota — tanpa mengubah berkas asli
/// dan tanpa merusak struktur XML dokumennya.
/// </summary>
public sealed class TemplatePerdesFillerTests : IDisposable
{
    private readonly string _folderKerja =
        Path.Combine(Path.GetTempPath(), "uji-template-perdes-" + Guid.NewGuid().ToString("n"));

    public TemplatePerdesFillerTests() => Directory.CreateDirectory(_folderKerja);

    public void Dispose()
    {
        try { Directory.Delete(_folderKerja, recursive: true); } catch { /* folder sementara */ }
    }

    private static DesaData DesaContoh(string jenis = "Kabupaten") => new()
    {
        NamaDesa = "Sukamaju",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = jenis,
        KepalaDesa = "H. Ahmad Suryana",
        Alamat = "Jl. Raya Desa No. 1",
        Kodepos = "17530"
    };

    private string BuatDocxCoba(string isiDokumen)
    {
        var jalur = Path.Combine(_folderKerja, $"contoh-{Guid.NewGuid():n}.docx");
        using var zip = ZipFile.Open(jalur, ZipArchiveMode.Create);
        zip.CreateEntry("[Content_Types].xml");
        zip.CreateEntry("word/header1.xml");
        var dokumen = zip.CreateEntry("word/document.xml");
        using var tulis = new StreamWriter(dokumen.Open());
        // Bungkus teks uji dalam kerangka document.xml yang sah supaya hasil
        // pengisian tetap bisa diuji strukturnya lewat XDocument.Parse.
        tulis.Write(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p><w:r><w:t xml:space=\"preserve\">" + isiDokumen +
            "</w:t></w:r></w:p></w:body></w:document>");
        return jalur;
    }

    [Fact]
    public void IsianNomorTanggalTahunMasukKeNaskah()
    {
        const string isiAsli = """
            PERATURAN DESA SUMBERJAYA
            NOMOR 5 TAHUN 2023
            TENTANG ANGGARAN PENDAPATAN DAN BELANJA DESA
            TAHUN ANGGARAN 2023
            Hasil musyawarah desa yang diselenggarakan pada tanggal 29 September 2022.
            Ditetapkan di Sumberjaya pada tanggal 28 Februari 2023
            Diundangkan pada tanggal 2 Maret 2023 di Sumberjaya
            Pada tanggal: 28 Februari 2023
            disesuaikan dengan Peraturan Daerah Kabupaten Karawang Nomor 13 Tahun 2014 tentang Desa;
            """;
        var sumber = BuatDocxCoba(isiAsli);

        var hasil = TemplatePerdesFiller.Isi(sumber, DesaContoh(), _folderKerja,
            new TemplatePerdesFiller.PerdesTemplateIsian("7", new DateTime(2026, 10, 23)));

        string xml = BacaDocumentXml(hasil);

        // Kepala surat & tahun anggaran mengikuti isian form.
        Assert.Contains("NOMOR 7 TAHUN 2026", xml);
        Assert.DoesNotContain("NOMOR 5 TAHUN 2023", xml);
        Assert.Contains("TAHUN ANGGARAN 2026", xml);
        Assert.DoesNotContain("TAHUN ANGGARAN 2023", xml);

        // Tanggal penetapan & pengundangan pada blok penutup mengikuti form…
        Assert.Contains("Ditetapkan di Sukamaju pada tanggal 23 Oktober 2026", xml);
        Assert.Contains("Diundangkan pada tanggal 23 Oktober 2026 di Sukamaju", xml);
        Assert.Contains("Pada tanggal: 23 Oktober 2026", xml);

        // …tetapi tanggal riwayat di badan naskah TIDAK ikut terganti.
        Assert.Contains("pada tanggal 29 September 2022", xml);

        // Angka Perda kabupaten khas contoh dilepas; nama kabupaten tetap.
        Assert.Contains("Peraturan Daerah Kabupaten Bekasi tentang Desa", xml);
        Assert.DoesNotContain("Nomor 13 Tahun 2014", xml);
    }

    private static string BacaDocumentXml(string jalurDocx)
    {
        using var zip = ZipFile.OpenRead(jalurDocx);
        var entri = zip.GetEntry("word/document.xml");
        Assert.NotNull(entri);
        using var baca = new StreamReader(entri!.Open());
        return baca.ReadToEnd();
    }

    [Fact]
    public void IdentitasContohTerisiDenganPengaturanSurat()
    {
        const string isiAsli = """
            PERATURAN DESA SUMBERJAYA
            KECAMATAN TEMPURAN KABUPATEN KARAWANG
            bahwa perlu ditetapkan Peraturan Desa tentang Kewenangan Desa Sumberjaya
            Kecamatan Tempuran Kabupaten Karawang;
            disesuaikan dengan Peraturan Bupati Karawang Nomor 61 Tahun 2019;
            Peraturan Daerah Kabupaten Karawang Nomor 13 Tahun 2014;
            kecamatan Tempuran dan masyarakat Karawang;
            """;
        var sumber = BuatDocxCoba(isiAsli);

        var hasil = TemplatePerdesFiller.Isi(sumber, DesaContoh(), Path.Combine(_folderKerja, "keluar"));
        string xml = BacaDocumentXml(hasil);

        Assert.NotEqual(sumber, hasil);
        Assert.Contains("PERATURAN DESA SUKAMAJU", xml);                 // kapital ikut menyesuaikan
        Assert.Contains("KECAMATAN CIKARANG KABUPATEN BEKASI", xml);     // frasa kapital gabungan
        Assert.Contains("Desa Sukamaju", xml);
        Assert.Contains("Kecamatan Cikarang Kabupaten Bekasi", xml);
        Assert.Contains("Peraturan Bupati Bekasi Nomor 61 Tahun 2019", xml);
        Assert.Contains("Peraturan Daerah Kabupaten Bekasi Nomor 13 Tahun 2014", xml);
        Assert.Contains("kecamatan Cikarang dan masyarakat Bekasi", xml);

        Assert.DoesNotContain("Sumberjaya", xml);
        Assert.DoesNotContain("SUMBERJAYA", xml);
        Assert.DoesNotContain("Karawang", xml);
        Assert.DoesNotContain("Tempuran", xml);
    }

    [Fact]
    public void JenisKotaMenggantiGelarBupatiMenjadiWaliKota()
    {
        const string isiAsli = "Peraturan Bupati Karawang tentang Desa; Kabupaten Karawang.";
        var sumber = BuatDocxCoba(isiAsli);

        var hasil = TemplatePerdesFiller.Isi(sumber, DesaContoh(jenis: "Kota"), _folderKerja);
        string xml = BacaDocumentXml(hasil);

        Assert.Contains("Peraturan Wali Kota Bekasi tentang Desa", xml);
        Assert.Contains("Kota Bekasi", xml);
        Assert.DoesNotContain("Bupati", xml);
    }

    [Fact]
    public void NamaDesaDenganKarakterKhususTidakMerusakXml()
    {
        var desa = DesaContoh();
        desa.NamaDesa = "Sumber & Jaya <Utama>";
        var sumber = BuatDocxCoba("PERATURAN DESA SUMBERJAYA");

        var hasil = TemplatePerdesFiller.Isi(sumber, desa, _folderKerja);
        string xml = BacaDocumentXml(hasil);

        Assert.Contains("SUMBER &amp; JAYA &lt;UTAMA&gt;", xml);   // sampel kapital -> kapital
        var parse = XDocument.Parse(xml);   // melempar bila XML rusak
        Assert.NotNull(parse.Root);
    }

    [Fact]
    public void BerkasAsliTidakDiubah()
    {
        var sumber = BuatDocxCoba("PERATURAN DESA SUMBERJAYA");
        byte[] sebelum = File.ReadAllBytes(sumber);

        var hasil = TemplatePerdesFiller.Isi(sumber, DesaContoh(), _folderKerja);

        Assert.NotEqual(Path.GetFullPath(sumber), Path.GetFullPath(hasil));
        Assert.Equal(sebelum, File.ReadAllBytes(sumber));
        Assert.Contains("SUKAMAJU", BacaDocumentXml(hasil));
    }

    [Fact]
    public void BerkasBukanDokumenWordDitolak()
    {
        var bukanDocx = Path.Combine(_folderKerja, "acak.zip");
        using (var zip = ZipFile.Open(bukanDocx, ZipArchiveMode.Create))
            zip.CreateEntry("isi-teks.txt");

        Assert.Throws<InvalidDataException>(() =>
            TemplatePerdesFiller.Isi(bukanDocx, DesaContoh(), _folderKerja));
    }

    [Fact]
    public void IsiUlangNaskahPerdes_NomorDanTanggalSelaluMengikutiFormulir()
    {
        const string isiAsli = """
            PERATURAN DESA SUMBERJAYA
            NOMOR 5 TAHUN 2023
            TAHUN ANGGARAN 2023
            Ditetapkan di Karawang pada tanggal 30 Juli 2023
            Diundangkan pada tanggal 31 Juli 2023
            Peraturan Daerah Kabupaten Karawang Nomor 13 Tahun 2014 tentang Desa
            """;
        var sumber = BuatDocxCoba(isiAsli);
        var folder = Path.Combine(_folderKerja, "isi-ulang");

        // Pengisian awal saat contoh dibuka dari halaman SOTK Desa.
        var isianAwal = new TemplatePerdesFiller.PerdesTemplateIsian("5", new DateTime(2023, 7, 30));
        string hasilAwal = TemplatePerdesFiller.Isi(sumber, DesaContoh(), folder, isianAwal);

        // Saat Simpan formulir diubah menjadi nomor 7 tahun 2025 — naskah harus
        // mengikuti, meski sumbernya berkas hasil pengisian awal (idempoten).
        var isianBaru = new TemplatePerdesFiller.PerdesTemplateIsian("7", new DateTime(2025, 3, 15));
        string hasil = TemplatePerdesFiller.IsiUlangNaskahPerdes(hasilAwal, folder, isianBaru);
        string xml = BacaDocumentXml(hasil);

        Assert.NotEqual(hasilAwal, hasil);
        Assert.Contains("NOMOR 7 TAHUN 2025", xml);
        Assert.Contains("TAHUN ANGGARAN 2025", xml);
        Assert.Contains("15 Maret 2025", xml);
        Assert.DoesNotContain("NOMOR 5 TAHUN 2023", xml);
        Assert.DoesNotContain("30 Juli 2023", xml);
        Assert.DoesNotContain("31 Juli 2023", xml);

        // Identitas desa hasil pengisian awal tetap utuh, dan aturan pengisian
        // awal tidak "dibangkitkan" lagi oleh isi ulang.
        Assert.Contains("PERATURAN DESA SUKAMAJU", xml);
        Assert.Contains("Bekasi", xml);
        Assert.Contains("Peraturan Daerah Kabupaten Bekasi tentang Desa", xml);
        Assert.DoesNotContain("Nomor 13", xml);

        // Berkas sumber (hasil pengisian awal) tidak diubah oleh isi ulang.
        Assert.Contains("NOMOR 5 TAHUN 2023", BacaDocumentXml(hasilAwal));
    }

    /// <summary>
    /// Uji pada SELURUH berkas contoh asli di repo (baik yang lama maupun yang
    /// baru ditambahkan): setelah diisi dengan identitas desa lain, tidak ada
    /// satu pun teks Sumberjaya/Tempuran/Karawang yang tersisa.
    /// Bila repo tidak ditemukan dari folder test (mis. lingkungan terpasang),
    /// pemeriksaan dilewati tanpa kegagalan.
    /// </summary>
    [Fact]
    public void ContohPerdesAsliTerisiPenuh()
    {
        string? root = CariRootRepo();
        string folderContoh = root == null ? string.Empty : Path.Combine(root, "Templates", "Perdes");
        if (root == null || !Directory.Exists(folderContoh)) return;

        var berkas = Directory.GetFiles(folderContoh, "*.docx");
        Assert.NotEmpty(berkas);

        foreach (var sumber in berkas)
        {
            var hasil = TemplatePerdesFiller.Isi(sumber, DesaContoh(), _folderKerja);
            string xml = BacaDocumentXml(hasil);

            Assert.DoesNotContain("Sumberjaya", xml);
            Assert.DoesNotContain("SUMBERJAYA", xml);
            Assert.DoesNotContain("Karawang", xml);
            Assert.DoesNotContain("KARAWANG", xml);
            Assert.DoesNotContain("Tempuran", xml);
            Assert.Contains("Sukamaju", xml);
            Assert.Contains("Bekasi", xml);
            Assert.NotNull(XDocument.Parse(xml).Root);
        }
    }

    private static string? CariRootRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
