using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Controls;

/// <summary>
/// Penata kartu dashboard yang menyesuaikan jumlah kolom menurut lebar yang
/// tersedia: tiga kolom pada layar lebar, dua kolom pada layar sedang (kartu
/// terakhir turun dan melebar penuh), dan satu kolom pada layar sempit.
///
/// Panel ini PEMILIK TUNGGAL kebijakan tata letak kartu atas: ambang lebar,
/// jarak antar kartu, dan pemerataan tinggi tiap baris. Halaman cukup
/// menaruh kartu sebagai anak <em>berurutan</em>; tidak ada lagi kode geser
/// <c>Grid.Row</c>/<c>Grid.Column</c> di code-behind halaman.
///
/// Tinggi setiap baris = tinggi anak tertinggi di baris itu, sehingga kartu
/// yang bersebelahan selalu rata atas dan rata bawah. Lebar tersedia harus
/// berhingga (halaman menaruh panel di dalam wadah yang lebarnya mengikuti
/// viewport); bila tidak, panel memakai lebar cadangan agar tetap terukur.
/// </summary>
public sealed class PanelKartuResponsif : Panel
{
    /// <summary>Lebar pengganti bila induk mengukur dengan lebar tak berhingga.</summary>
    private const double LebarCadangan = 1200;

    public static readonly DependencyProperty JarakProperty = DependencyProperty.Register(
        nameof(Jarak), typeof(double), typeof(PanelKartuResponsif),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Jarak horizontal dan vertikal antar kartu, dalam piksel.</summary>
    public double Jarak
    {
        get => (double)GetValue(JarakProperty);
        set => SetValue(JarakProperty, value);
    }

    public static readonly DependencyProperty AmbangTigaKolomProperty = DependencyProperty.Register(
        nameof(AmbangTigaKolom), typeof(double), typeof(PanelKartuResponsif),
        new FrameworkPropertyMetadata(900d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Lebar minimum panel agar tiga kartu masih nyaman berdampingan.</summary>
    public double AmbangTigaKolom
    {
        get => (double)GetValue(AmbangTigaKolomProperty);
        set => SetValue(AmbangTigaKolomProperty, value);
    }

    public static readonly DependencyProperty AmbangDuaKolomProperty = DependencyProperty.Register(
        nameof(AmbangDuaKolom), typeof(double), typeof(PanelKartuResponsif),
        new FrameworkPropertyMetadata(620d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Lebar minimum panel untuk dua kolom; di bawahnya kartu menumpuk.</summary>
    public double AmbangDuaKolom
    {
        get => (double)GetValue(AmbangDuaKolomProperty);
        set => SetValue(AmbangDuaKolomProperty, value);
    }

    protected override Size MeasureOverride(Size tersedia)
    {
        var anak = AnakTampak();

        // Anak yang disembunyikan tetap diukur nol supaya tidak menyisakan
        // ruang lama saat Visibility berubah.
        foreach (UIElement kandidat in InternalChildren)
        {
            if (kandidat.Visibility != Visibility.Visible) kandidat.Measure(new Size(0, 0));
        }

        if (anak.Count == 0) return new Size(0, 0);

        double lebar = LebarTersedia(tersedia.Width);
        int kolom = TentukanKolom(lebar);
        var baris = BagiBaris(anak, kolom);
        double tinggi = 0;

        for (int i = 0; i < baris.Count; i++)
        {
            double lebarAnak = LebarAnak(baris[i].Count, kolom, lebar);
            double tinggiBaris = 0;

            foreach (UIElement kartu in baris[i])
            {
                kartu.Measure(new Size(lebarAnak, double.PositiveInfinity));
                tinggiBaris = Math.Max(tinggiBaris, kartu.DesiredSize.Height);
            }

            tinggi += tinggiBaris;
            if (i < baris.Count - 1) tinggi += Jarak;
        }

        return new Size(double.IsInfinity(tersedia.Width) ? lebar : tersedia.Width, tinggi);
    }

    protected override Size ArrangeOverride(Size akhir)
    {
        var anak = AnakTampak();

        foreach (UIElement kandidat in InternalChildren)
        {
            if (kandidat.Visibility != Visibility.Visible) kandidat.Arrange(new Rect());
        }

        if (anak.Count == 0) return akhir;

        double lebar = LebarTersedia(akhir.Width);
        int kolom = TentukanKolom(lebar);
        var baris = BagiBaris(anak, kolom);
        double atas = 0;

        foreach (var isiBaris in baris)
        {
            double lebarAnak = LebarAnak(isiBaris.Count, kolom, lebar);
            double tinggiBaris = 0;
            foreach (UIElement kartu in isiBaris) tinggiBaris = Math.Max(tinggiBaris, kartu.DesiredSize.Height);

            double kiri = 0;
            foreach (UIElement kartu in isiBaris)
            {
                kartu.Arrange(new Rect(kiri, atas, lebarAnak, tinggiBaris));
                kiri += lebarAnak + Jarak;
            }

            atas += tinggiBaris + Jarak;
        }

        return akhir;
    }

    private int TentukanKolom(double lebar) =>
        lebar >= AmbangTigaKolom ? 3 : lebar >= AmbangDuaKolom ? 2 : 1;

    private static double LebarTersedia(double lebar) =>
        double.IsInfinity(lebar) ? LebarCadangan : Math.Max(0, lebar);

    /// <summary>
    /// Lebar satu kartu. Baris terakhir yang tidak penuh dilebarkan agar
    /// menutup sisa lebar barisnya — inilah yang membuat kartu terakhir turun
    /// selebar dua kolom pada tata letak dua kolom, tanpa aturan khusus di halaman.
    /// </summary>
    private double LebarAnak(int jumlahAnak, int kolom, double lebar)
    {
        if (kolom <= 1) return lebar;
        if (jumlahAnak >= kolom) return (lebar - Jarak * (kolom - 1)) / kolom;
        return (lebar - Jarak * (jumlahAnak - 1)) / jumlahAnak;
    }

    private static List<List<UIElement>> BagiBaris(IReadOnlyList<UIElement> anak, int kolom)
    {
        var baris = new List<List<UIElement>>();
        for (int i = 0; i < anak.Count; i += kolom)
        {
            var isi = new List<UIElement>(Math.Min(kolom, anak.Count - i));
            for (int j = i; j < i + kolom && j < anak.Count; j++) isi.Add(anak[j]);
            baris.Add(isi);
        }

        return baris;
    }

    private List<UIElement> AnakTampak()
    {
        var hasil = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement anak in InternalChildren)
        {
            if (anak.Visibility == Visibility.Visible) hasil.Add(anak);
        }

        return hasil;
    }
}
