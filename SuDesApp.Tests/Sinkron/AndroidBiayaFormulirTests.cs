using System;
using System.IO;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// Penjaga biaya bangun formulir Android. Buka surat di perangkat kelas bawah
/// pernah memakan 3 detik main thread dan setiap pembacaan sesi membayar kunci
/// Android Keystore ulang (45-230 ms per panggilan), sehingga aplikasi terasa
/// berat. Perbaikan ini tidak mengubah hasil cetak apa pun, jadi tidak ada uji
/// paritas yang menolaknya kalau seseorang mengembalikannya ke cara lama —
/// karena itu invariannya dikunci di sini lewat sumber Kotlin aslinya.
/// </summary>
public sealed class AndroidBiayaFormulirTests
{
    private static readonly string AkarRepo = CariAkarRepo();

    private static readonly string BuatSurat = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android",
        "BuatSuratActivity.kt");

    private static readonly string Sesi = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android",
        "SupabaseSesi.kt");

    private static readonly string PdfZoom = Path.Combine(
        AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android",
        "PdfZoomActivity.kt");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Akar repo tidak ditemukan.");
    }

    private static string Baca(string jalur) => File.ReadAllText(jalur);

    /// <summary>Isi satu fungsi Kotlin, dari kurung buka badan sampai kurung tutup selevel.</summary>
    private static string IsiFungsi(string sumber, string signature)
    {
        var i = sumber.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i >= 0, $"fungsi '{signature}' tidak ada di sumber");
        // Parameter boleh mengandung kurung (mis. default "() -> Unit = {}"), jadi
        // badan dicari setelah daftar parameter ditutup, bukan kurung pertama.
        var bukaParam = sumber.IndexOf('(', i);
        Assert.True(bukaParam >= 0, $"parameter '{signature}' tidak ditemukan");
        var kedalamanParam = 0;
        var j = bukaParam;
        for (; j < sumber.Length; j++)
        {
            if (sumber[j] == '(') kedalamanParam++;
            else if (sumber[j] == ')')
            {
                kedalamanParam--;
                if (kedalamanParam == 0) break;
            }
        }

        var buka = sumber.IndexOf('{', j + 1);
        Assert.True(buka >= 0, $"kurung buka '{signature}' tidak ditemukan");
        var kedalaman = 0;
        for (var k = buka; k < sumber.Length; k++)
        {
            if (sumber[k] == '{') kedalaman++;
            else if (sumber[k] == '}')
            {
                kedalaman--;
                if (kedalaman == 0) return sumber.Substring(buka, k - buka + 1);
            }
        }
        throw new InvalidOperationException($"kurung tutup '{signature}' tidak seimbang");
    }

    /// <summary>Sekeliling teks di suatu tanda, untuk fungsi berbadan ekspresi satu baris.</summary>
    private static string Sekitar(string sumber, string pola, int lebar = 200)
    {
        var i = sumber.IndexOf(pola, StringComparison.Ordinal);
        Assert.True(i >= 0, $"'{pola}' tidak ada di sumber");
        return sumber.Substring(i, Math.Min(lebar, sumber.Length - i));
    }

    [Fact]
    public void SesiTerenkripsiDibuatSekaliPerProses()
    {
        var sumber = Baca(Sesi);

        // EncryptedSharedPreferences.create() membuka Android Keystore dan
        // mendekripsi seluruh berkas: harus dipakai lewat cache, bukan per panggilan.
        var prefs = IsiFungsi(sumber, "private fun prefs(ctx: Context)");
        Assert.Contains("cachePrefs", prefs);
        Assert.Equal(1, CountOf(prefs, "EncryptedSharedPreferences.create("));
        Assert.Contains("computeIfAbsent", prefs);
    }

    [Fact]
    public void BidangFormulirDibangunBertahapBukanSekaligus()
    {
        var sumber = Baca(BuatSurat);

        Assert.Contains("antreanLangkah", sumber);
        var bangunForm = IsiFungsi(sumber, "private fun bangunForm(jenis: SkemaJenis?)");
        Assert.Contains("bangunDalamKuota(", bangunForm);
        Assert.Contains("jadwalkanLangkahTertunda()", bangunForm);

        // Antrean harus per bidang, bukan per kartu: satu kartu KEMATIAN saja
        // bisa memakan hampir dua detik, jadi kartu pertama pun harus bertahap.
        Assert.Contains("antreanLangkah.addLast(judul to null)", bangunForm);
        Assert.Contains("antreanLangkah.addLast(judul to b)", bangunForm);

        var jadwalkan = IsiFungsi(sumber, "private fun jadwalkanLangkahTertunda()");
        Assert.Contains("wadahForm.post", jadwalkan);
        Assert.Contains("generasi != generasiForm", jadwalkan);
    }

    [Fact]
    public void NilaiDibacaHanyaSetelahSemuaBidangSelesaiDibangun()
    {
        var sumber = Baca(BuatSurat);

        Assert.Contains("pastikanSemuaBidangTerbangun()",
            IsiFungsi(sumber, "private fun buatPdf(selesai: () -> Unit = {})"));
        Assert.Contains("pastikanSemuaBidangTerbangun()",
            IsiFungsi(sumber, "private fun isiDariEdit(json: String)"));

        // Fungsi paksanya harus benar-benar menghabiskan antrean.
        var paksa = IsiFungsi(sumber, "private fun pastikanSemuaBidangTerbangun()");
        Assert.Contains("while (antreanLangkah.isNotEmpty())", paksa);
    }

    [Fact]
    public void AdapterDropdownDanWarnaTidakDibuatUlangPerBidang()
    {
        var sumber = Baca(BuatSurat);

        Assert.Contains("setAdapter(adapterPilihan(opsi))",
            IsiFungsi(sumber, "private fun bidangPilih(label: String, opsiAsli: List<String>)"));
        Assert.Contains("cacheAdapter", sumber);
        Assert.Contains("cacheWarna", Sekitar(sumber, "private fun warna(id: Int)"));
    }

    [Fact]
    public void RenderPdfTidakMainThreadDanRendererDitutupDiUtasSama()
    {
        var sumber = Baca(PdfZoom);

        Assert.Contains("newSingleThreadExecutor", sumber);
        var tampilkan = IsiFungsi(sumber, "private fun tampilkanHalaman(halamanBaru: Int)");
        Assert.Contains("renderUtas.execute", tampilkan);
        Assert.Contains("tampilan.setBitmap(bitmap, lebarAsli, tinggiAsli)", tampilkan);

        // PdfRenderer yang ditutup saat masih dirender = native crash, jadi
        // penutupannya harus lewat utas render yang sama.
        var hancur = IsiFungsi(sumber, "override fun onDestroy()");
        Assert.Contains("renderUtas.execute", hancur);
        Assert.DoesNotContain("perender?.close()", IsiFungsi(sumber, "private fun bukaBerkas()"));
    }

    private static int CountOf(string teks, string pola)
    {
        var n = 0;
        for (var i = teks.IndexOf(pola, StringComparison.Ordinal);
             i >= 0;
             i = teks.IndexOf(pola, i + pola.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
