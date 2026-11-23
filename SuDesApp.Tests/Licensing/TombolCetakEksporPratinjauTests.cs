using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Aturan tombol Cetak dan Ekspor pada pratinjau PDF: keduanya ADA di semua
/// pratinjau selama lisensi masih berlaku (trial berjalan atau berlangganan),
/// dan baru MATI ketika trial habis / langganan berakhir / trial belum dimulai.
/// Cetak dan Ekspor dibaca dari gerbang yang sama — tidak ada satu tombol yang
/// terkunci sementara kembarannya masih bisa dipakai.
///
/// <para>Gejala yang dijaga: <c>PdfPreviewView</c> pernah mengikat
/// <c>Visibility</c> tombol Cetak pada aturan "hanya pratinjau Kematian dan
/// Daftar Hadir yang boleh dicetak langsung", sehingga pengguna berlisensi tidak
/// melihat tombol Cetak sama sekali pada surat lain dan menyimpulkan fitur
/// cetak dihapus. Sejak itu satu-satunya kunci tombol adalah keadaan lisensi,
/// dibaca dari <see cref="PenjagaLisensi.BolehCetakPdf"/>.</para>
///
/// <para>Pengecualian yang tetap harus berlaku: Daftar Hadir masih boleh
/// dicetak/diekspor walau trial habis, dan PDF yang gagal dimuat (atau tidak ada
/// di disk) tidak bisa dicetak/diekspor.</para>
/// </summary>
[Collection("LisensiBerbagiBerkas")]
public sealed class TombolCetakEksporPratinjauTests : IDisposable
{
    /// <summary>Judul pratinjau jenis biasa — dulu inilah yang membuat tombol lenyap.</summary>
    private const string JudulLain = "Pratinjau Surat Keterangan Domisili";

    private const string JudulKematian = "Pratinjau Surat Kematian";
    private const string JudulDaftarHadir = "Pratinjau Daftar Hadir Rapat Desa";

    private readonly string _folder;

    public TombolCetakEksporPratinjauTests()
    {
        _folder = Path.Combine(Path.GetTempPath(),
            "sudesapp-tombol-cetak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        TrialLokal.FolderOverride = _folder;
        LisensiManager.FolderCacheOverride = _folder;
    }

    public void Dispose()
    {
        TrialLokal.FolderOverride = null;
        LisensiManager.FolderCacheOverride = null;
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Folder sementara yang gagal dihapus tidak menggagalkan uji.
        }
    }

    // ------------------------------------------------------------------
    // Pembantu keadaan lisensi
    // ------------------------------------------------------------------

    /// <summary>Tulis berkas trial lokal; umur &gt; 30 hari = trial sudah habis.</summary>
    private void CatatTrial(int umurHari) =>
        File.WriteAllText(
            Path.Combine(_folder, "trial-lokal.json"),
            JsonSerializer.Serialize(new { MulaiUtc = DateTime.UtcNow.AddDays(-umurHari) }));

    private static LisensiManager BuatLisensi() =>
        new(NullLogger<LisensiManager>.Instance);

    /// <summary>Instalasi baru: belum pernah trial, belum berlangganan (KeadaanLisensi.Demo).</summary>
    private static PenjagaLisensi PenjagaDemo() =>
        new(BuatLisensi(), new TrialLokal(), NullLogger<PenjagaLisensi>.Instance);

    /// <summary>Trial 30 hari lokal sedang berjalan.</summary>
    private static PenjagaLisensi PenjagaTrial()
    {
        var trial = new TrialLokal();
        _ = trial.MulaiSekarang();
        var penjaga = new PenjagaLisensi(BuatLisensi(), trial, NullLogger<PenjagaLisensi>.Instance);
        penjaga.Terapkan(new StatusLisensi { Aktif = false });
        return penjaga;
    }

    /// <summary>Langganan aktif menurut token yang terverifikasi.</summary>
    private static PenjagaLisensi PenjagaBerlangganan()
    {
        var penjaga = PenjagaDemo();
        penjaga.Terapkan(AktifSampai(30));
        return penjaga;
    }

    /// <summary>Trial lokal sudah lewat (baca-saja).</summary>
    private PenjagaLisensi PenjagaTrialHabis()
    {
        CatatTrial(umurHari: 40);
        var penjaga = PenjagaDemo();
        penjaga.Terapkan(new StatusLisensi { Aktif = false });
        return penjaga;
    }

    private static StatusLisensi AktifSampai(int hari) => new()
    {
        Aktif = true,
        BerlakuSampai = DateTime.UtcNow.AddDays(hari),
        SahSampai = DateTime.UtcNow.AddDays(hari)
    };

    private PdfPreviewViewModel BuatPratinjau(string judul, PenjagaLisensi? penjaga) =>
        new(judul, Path.Combine(_folder, "dokumen.pdf"), BuatNavigasi(), penjaga: penjaga);

    private static NavigationService BuatNavigasi()
    {
        var layanan = new ServiceCollection();
        layanan.AddLogging();
        return new NavigationService(layanan.BuildServiceProvider(),
            NullLogger<NavigationService>.Instance);
    }

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    private static string BacaSumber(string proyek, string folder, string berkas) =>
        File.ReadAllText(Path.Combine(CariAkarRepo(), proyek, folder, berkas));

    /// <summary>Blok elemen pembuka sebuah tombol di XAML pratinjau.</summary>
    private static string BlokTombol(string nama)
    {
        var xaml = BacaSumber("SuDesApp.Wpf", "Views", "PdfPreviewView.xaml");
        var blok = Regex.Match(xaml, @"<Button\b[^>]*x:Name=""" + nama + @"""[^>]*>");
        Assert.True(blok.Success, $"Tombol {nama} tidak ditemukan di PdfPreviewView.xaml.");
        return blok.Value;
    }

    // ------------------------------------------------------------------
    // Keadaan lisensi → keadaan tombol (Cetak dan Ekspor pembaca yang sama)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(JudulLain)]
    [InlineData(JudulKematian)]
    public void LisensiBerlaku_TombolCetakHidupUntukSemuaJenis(string judul)
    {
        // Regresi utama: jenis lain pernah membuat tombolnya HILANG.
        Assert.True(BuatPratinjau(judul, PenjagaBerlangganan()).BolehCetak);
        Assert.True(BuatPratinjau(judul, PenjagaTrial()).BolehCetak);
    }

    [Theory]
    [InlineData(JudulLain)]
    [InlineData(JudulKematian)]
    public void LisensiBerlaku_TombolEksporHidupUntukSemuaJenis(string judul)
    {
        Assert.True(BuatPratinjau(judul, PenjagaBerlangganan()).BolehEkspor);
        Assert.True(BuatPratinjau(judul, PenjagaTrial()).BolehEkspor);
    }

    [Fact]
    public async Task LisensiBerlaku_KlikCetakTidakDitolak()
    {
        Assert.True(await BuatPratinjau(JudulLain, PenjagaTrial()).BolehCetakAsync());
        Assert.True(await BuatPratinjau(JudulLain, PenjagaBerlangganan()).BolehCetakAsync());
    }

    [Fact]
    public async Task TrialHabis_TombolCetakMatiBukanHilang()
    {
        var vm = BuatPratinjau(JudulLain, PenjagaTrialHabis());

        Assert.False(vm.BolehCetak,
            "Setelah trial habis cetak PDF dikunci — tombolnya mati, bukan hilang.");
        Assert.False(await vm.BolehCetakAsync());

        // Tooltip menjelaskan sebabnya, supaya tombol abu-abu tidak dibaca "fitur dihapus".
        Assert.Contains("dikunci", vm.TooltipCetak, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrialHabis_TombolEksporMatiPula()
    {
        var vm = BuatPratinjau(JudulLain, PenjagaTrialHabis());

        // Kembar Cetak tidak boleh lolos: keduanya membaca gerbang yang sama.
        Assert.False(vm.BolehEkspor);
        Assert.Contains("dikunci", vm.TooltipEkspor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LisensiBerlaku_TooltipTombolMenerangkanFungsinya()
    {
        foreach (var vm in new[] { BuatPratinjau(JudulLain, PenjagaTrial()), BuatPratinjau(JudulLain, PenjagaBerlangganan()) })
        {
            Assert.DoesNotContain("dikunci", vm.TooltipCetak, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dikunci", vm.TooltipEkspor, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("printer", vm.TooltipCetak, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Simpan salinan", vm.TooltipEkspor, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BelumPernahTrial_TombolCetakMati()
    {
        var penjaga = PenjagaDemo();
        var vm = BuatPratinjau(JudulLain, penjaga);

        Assert.Equal(KeadaanLisensi.Demo, penjaga.Keadaan);
        Assert.False(vm.BolehCetak);
        Assert.False(vm.BolehEkspor);
        Assert.False(await vm.BolehCetakAsync());
    }

    [Fact]
    public void TrialHabis_DaftarHadirTetapBolehCetak()
    {
        // Pengecualian layanan harian desa: cetak/ekspor Daftar Hadir tidak pernah
        // dikunci — dan tidak boleh bocor ke jenis lain pada penjaga yang sama.
        var penjaga = PenjagaTrialHabis();
        Assert.True(BuatPratinjau(JudulDaftarHadir, penjaga).BolehCetak);
        Assert.True(BuatPratinjau(JudulDaftarHadir, penjaga).BolehEkspor);
        Assert.False(BuatPratinjau(JudulLain, penjaga).BolehCetak);
    }

    [Fact]
    public void TanpaPenjaga_TombolTetapHidup()
    {
        // Pemanggil tanpa penjaga lisensi (jalur alat/periksa) tidak kehilangan cetak.
        var vm = BuatPratinjau(JudulLain, null);
        Assert.True(vm.BolehCetak);
        Assert.True(vm.BolehEkspor);
    }

    [Fact]
    public void LisensiDibukaSaatHalamanTerbuka_TombolIkutHidup()
    {
        var penjaga = PenjagaTrialHabis();
        var vm = BuatPratinjau(JudulLain, penjaga);
        Assert.False(vm.BolehCetak);
        Assert.False(vm.BolehEkspor);

        var naik = new System.Collections.Generic.HashSet<string>();
        vm.PropertyChanged += (_, e) => naik.Add(e.PropertyName ?? string.Empty);

        penjaga.Terapkan(AktifSampai(30));

        Assert.Contains(nameof(PdfPreviewViewModel.BolehCetak), naik, StringComparer.Ordinal);
        Assert.Contains(nameof(PdfPreviewViewModel.BolehEkspor), naik, StringComparer.Ordinal);
        Assert.True(vm.BolehCetak,
            "Keadaan lisensi berubah → pratinjau yang sedang terbuka harus ikut segar, " +
            "tanpa menunggu pengguna membuka ulang dokumennya.");
        Assert.True(vm.BolehEkspor);
    }

    [Fact]
    public void PdfGagalDimuat_TombolCetakDanEksporMatiWalauBerlisensi()
    {
        var vm = BuatPratinjau(JudulLain, PenjagaBerlangganan());

        vm.TerapkanKesiapanDokumen(false);
        Assert.False(vm.BolehCetak);
        Assert.False(vm.BolehEkspor);

        vm.TerapkanKesiapanDokumen(true);
        Assert.True(vm.BolehCetak);
        Assert.True(vm.BolehEkspor);
    }

    [Fact]
    public void TanpaBerkasPdf_TombolEksporMati()
    {
        // Ekspor tidak punya apa-apa untuk disalin kalau PDF-nya tidak ada di disk.
        var vm = new PdfPreviewViewModel(JudulLain, null, BuatNavigasi(),
            penjaga: PenjagaBerlangganan());

        Assert.False(vm.BolehEkspor);
    }

    // ------------------------------------------------------------------
    // Pagar tampilan: aturan lama tidak boleh balik lewat XAML/kode-behind
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("PrintBtn", "BolehCetak", "TooltipCetak")]
    [InlineData("ExportBtn", "BolehEkspor", "TooltipEkspor")]
    public void XamlPratinjau_tombolTerkunciLisensiBukanDisembunyikanJenisSurat(
        string nama, string propertiGerbang, string propertiTooltip)
    {
        var blok = BlokTombol(nama);

        Assert.DoesNotContain("BolehCetakPratinjauLangsung", blok, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=", blok, StringComparison.Ordinal);
        Assert.Contains($"IsEnabled=\"{{Binding {propertiGerbang}", blok, StringComparison.Ordinal);
        Assert.Contains($"ToolTip=\"{{Binding {propertiTooltip}", blok, StringComparison.Ordinal);

        // Tooltip harus tetap muncul walau tombol mati — itulah penjelasan kuncinya.
        Assert.Contains("ToolTipService.ShowOnDisabled=\"True\"", blok, StringComparison.Ordinal);
    }

    [Fact]
    public void KodeBehind_TombolCetakEksporTidakDisetelLangsungLagi()
    {
        var sumber = BacaSumber("SuDesApp.Wpf", "Views", "PdfPreviewView.xaml.cs");

        // Nilai lokal IsEnabled melepas binding BolehCetak/BolehEkspor — sumber
        // gejala "tombol mati walau lisensi aktif".
        Assert.DoesNotContain("PrintBtn.IsEnabled", sumber, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportBtn.IsEnabled", sumber, StringComparison.Ordinal);
        Assert.Contains("TerapkanKesiapanDokumen", sumber, StringComparison.Ordinal);
    }

    [Fact]
    public void AturanLamaHanyaKematianTidakPunyaTempatLagi()
    {
        var sumber = BacaSumber("SuDesApp.Wpf", "ViewModels", "PdfPreviewViewModel.cs");

        Assert.DoesNotContain("BolehCetakPratinjauLangsung", sumber, StringComparison.Ordinal);
        Assert.DoesNotContain("Hanya pratinjau Surat Kematian", sumber, StringComparison.Ordinal);
    }
}
