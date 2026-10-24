using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using SuDesApp.Services;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>Satu titik tren: angka penilaian satu tahun, apa adanya dari pusat.</summary>
    /// <param name="Tahun">Tahun penilaian yang diterbitkan pusat.</param>
    /// <param name="Skor">Skor IDM tahun itu (null bila pusat tidak mengirim).</param>
    /// <param name="Iks">Skor IKS tahun itu (null bila pusat tidak mengirim).</param>
    /// <param name="Ike">Skor IKE tahun itu (null bila pusat tidak mengirim).</param>
    /// <param name="Ikl">Skor IKL tahun itu (null bila pusat tidak mengirim).</param>
    public sealed record TitikTren(int Tahun, double? Skor, double? Iks, double? Ike, double? Ikl);

    /// <summary>
    /// Grafik tren penilaian IDM — garis skor IDM (tebal) plus IKS/IKE/IKL
    /// beberapa tahun terakhir, digambar sendiri lewat <c>DrawingContext</c>.
    ///
    /// Tiga alasan memakai gambar sendiri, bukan pustaka grafik:
    ///   1. warna diambil lewat kuas bertema (DynamicResource di tampilan),
    ///      jadi grafik ikut berganti saat pengguna menukar tema;
    ///   2. tidak menambah dependensi ke aplikasi desa;
    ///   3. jumlah titik sangat kecil (≤ TahunDitampilkan) — cukup satu
    ///      <c>Polyline</c> per seri, tanpa mesin grafik penuh.
    ///
    /// Angka TIDAK pernah dihitung di sini: nilai datang apa adanya dari
    /// <c>idm.kemendesa.go.id</c> lewat properti <see cref="Deret"/>.
    /// </summary>
    public class GrafikTrenIdm : FrameworkElement
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new GrafikTrenIdmAutomationPeer(this);

        private string RingkasanAksesibilitas(IReadOnlyList<TitikTren> titik)
        {
            static string Nilai(double? nilai) => nilai.HasValue
                ? IdmTeks.Angka(nilai, "0.0000")
                : "tidak tersedia";

            return string.Join(" ", titik.Select(t =>
                $"{t.Tahun}: IDM {Nilai(t.Skor)}; IKS sosial {Nilai(t.Iks)}; "
                + $"IKE ekonomi {Nilai(t.Ike)}; IKL lingkungan {Nilai(t.Ikl)}."));
        }

        private sealed class GrafikTrenIdmAutomationPeer : FrameworkElementAutomationPeer
        {
            private readonly GrafikTrenIdm _grafik;

            public GrafikTrenIdmAutomationPeer(GrafikTrenIdm grafik) : base(grafik) => _grafik = grafik;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

            protected override string GetNameCore()
            {
                string nama = base.GetNameCore();
                var terbaru = (_grafik.Deret ?? Array.Empty<TitikTren>())
                    .Where(t => t != null)
                    .OrderByDescending(t => t.Tahun)
                    .FirstOrDefault();
                if (terbaru == null) return nama;

                string ringkas = _grafik.RingkasanAksesibilitas(new[] { terbaru });
                return string.IsNullOrWhiteSpace(nama) ? ringkas : nama + ". Data terbaru: " + ringkas;
            }

            protected override string GetHelpTextCore()
            {
                string ringkasan = _grafik.RingkasanAksesibilitas(
                    (_grafik.Deret ?? Array.Empty<TitikTren>()).Where(t => t != null).OrderBy(t => t.Tahun).ToList());
                return string.IsNullOrWhiteSpace(ringkasan) ? "Belum ada data tren." : "Nilai grafik per tahun: " + ringkasan;
            }
        }

        // ===================== Sifat (dependency properties) =====================

        public static readonly DependencyProperty DeretProperty = DependencyProperty.Register(
            nameof(Deret), typeof(IReadOnlyList<TitikTren>), typeof(GrafikTrenIdm),
            new PropertyMetadata(null, OnBerubah));

        /// <summary>Deret titik per tahun (urut bebas — kontrol mengurutkan sendiri menaik).</summary>
        public IReadOnlyList<TitikTren>? Deret
        {
            get => (IReadOnlyList<TitikTren>?)GetValue(DeretProperty);
            set => SetValue(DeretProperty, value);
        }

        public static readonly DependencyProperty KuasIdmProperty = DependencyProperty.Register(
            nameof(KuasIdm), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna garis skor IDM (seri utama: paling tebal, berlabel angka).</summary>
        public Brush? KuasIdm
        {
            get => (Brush?)GetValue(KuasIdmProperty);
            set => SetValue(KuasIdmProperty, value);
        }

        public static readonly DependencyProperty KuasIksProperty = DependencyProperty.Register(
            nameof(KuasIks), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna garis IKS.</summary>
        public Brush? KuasIks
        {
            get => (Brush?)GetValue(KuasIksProperty);
            set => SetValue(KuasIksProperty, value);
        }

        public static readonly DependencyProperty KuasIkeProperty = DependencyProperty.Register(
            nameof(KuasIke), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna garis IKE (bergaris putus-putus — membedakan dari IKS).</summary>
        public Brush? KuasIke
        {
            get => (Brush?)GetValue(KuasIkeProperty);
            set => SetValue(KuasIkeProperty, value);
        }

        public static readonly DependencyProperty KuasIklProperty = DependencyProperty.Register(
            nameof(KuasIkl), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna garis IKL (titik-titik — membedakan dari IKE).</summary>
        public Brush? KuasIkl
        {
            get => (Brush?)GetValue(KuasIklProperty);
            set => SetValue(KuasIklProperty, value);
        }

        public static readonly DependencyProperty KuasGarisProperty = DependencyProperty.Register(
            nameof(KuasGaris), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna garis bantu (grid) sumbu Y.</summary>
        public Brush? KuasGaris
        {
            get => (Brush?)GetValue(KuasGarisProperty);
            set => SetValue(KuasGarisProperty, value);
        }

        public static readonly DependencyProperty KuasTeksProperty = DependencyProperty.Register(
            nameof(KuasTeks), typeof(Brush), typeof(GrafikTrenIdm), new PropertyMetadata(null, OnBerubah));

        /// <summary>Warna tulisan sumbu (angka skor dan tahun).</summary>
        public Brush? KuasTeks
        {
            get => (Brush?)GetValue(KuasTeksProperty);
            set => SetValue(KuasTeksProperty, value);
        }

        private static void OnBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((GrafikTrenIdm)d).InvalidateVisual();

        // ===================== Gambar =====================

        // Font label grafik: Google Sans — sama dengan seluruh antarmuka aplikasi
        // (kunci AppFontFamily di Themes/*.xaml). Resolusi dilakukan sekali saat
        // kelas dipakai pertama kali (grafik baru dirender setelah jendela terbuka,
        // jadi tema sudah termuat); cadangan pack URI identik dengan isi kunci itu,
        // sehingga label tetap Google Sans walau resource belum tersedia.
        private static readonly Typeface Huruf =
            new(
                Application.Current?.TryFindResource("AppFontFamily") is FontFamily fontAntarmuka
                    ? fontAntarmuka
                    : new FontFamily("pack://application:,,,/SuDesApp;component/Fonts/GoogleSans.ttc#Google Sans, Segoe UI Variable, Segoe UI"),
                FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private static readonly CultureInfo Budaya = CultureInfo.GetCultureInfo("id-ID");

        /// <summary>Langkah grid "rapi" (0,02 bukan 0,0173…) dari kecil ke besar.</summary>
        private static readonly double[] LangkahRapi = { 0.005, 0.01, 0.02, 0.025, 0.05, 0.1, 0.2, 0.25, 0.5, 1 };

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var titik = (Deret ?? Array.Empty<TitikTren>())
                .Where(t => t != null)
                .OrderBy(t => t.Tahun)
                .ToList();

            double lebar = ActualWidth, tinggi = ActualHeight;
            if (titik.Count == 0 || lebar < 140 || tinggi < 90) return;

            // Ruang tepi: kiri untuk angka sumbu Y, bawah untuk tahun.
            const double Kiri = 56, Kanan = 16, Atas = 30, Bawah = 28;
            double x0 = Kiri, x1 = lebar - Kanan, y0 = Atas, y1 = tinggi - Bawah;
            if (x1 - x0 < 40 || y1 - y0 < 40) return;

            // Rentang nilai dari SEMUA seri yang punya angka.
            double? min = null, max = null;
            foreach (var t in titik)
            {
                foreach (double? v in new[] { t.Skor, t.Iks, t.Ike, t.Ikl })
                {
                    if (v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) continue;
                    min = min.HasValue ? Math.Min(min.Value, v.Value) : v.Value;
                    max = max.HasValue ? Math.Max(max.Value, v.Value) : v.Value;
                }
            }
            if (min == null || max == null) return;

            // Rentang datar (satu tahun, atau semua nilai sama) tetap punya
            // ruang vertikal supaya titiknya tidak menempel di tepi.
            double rentang = max.Value - min.Value;
            if (rentang < 0.0001)
            {
                double pad = Math.Max(Math.Abs(min.Value) * 0.05, 0.02);
                min -= pad;
                max += pad;
            }
            else
            {
                min -= rentang * 0.2;
                max += rentang * 0.2;
            }
            rentang = max.Value - min.Value;

            int n = titik.Count;
            double Y(double nilai) => y1 - (nilai - min!.Value) / rentang * (y1 - y0);
            double X(int i) => n == 1 ? (x0 + x1) / 2 : x0 + i * (x1 - x0) / (n - 1);

            double pikselDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var kuasGaris = KuasGaris;
            var kuasTeks = KuasTeks ?? KuasGaris;

            // Grid + angka sumbu Y (kira-kira lima garis, langkah bulat).
            if (kuasGaris != null)
            {
                double langkah = LangkahRapi.FirstOrDefault(s => rentang / s <= 5.5);
                if (langkah <= 0) langkah = Math.Max(Math.Ceiling(rentang / 4 * 1000) / 1000, 0.001);

                var penGrid = new Pen(kuasGaris, 1);
                penGrid.Freeze();

                for (double v = Math.Ceiling(min.Value / langkah) * langkah; v <= max.Value + 1e-9; v += langkah)
                {
                    double y = Y(v);
                    dc.DrawLine(penGrid, new Point(x0, y), new Point(x1, y));

                    if (kuasTeks != null)
                    {
                        var label = Teks(IdmTeks.Angka(v, "0.000"), 11, kuasTeks, pikselDip);
                        dc.DrawText(label, new Point(x0 - 8 - label.Width, y - label.Height / 2));
                    }
                }
            }

            // Empat seri. IDM paling tebal + berlabel; IKS garis penuh; IKE
            // putus-putus; IKL titik-titik — pola berbeda membedakan seri walau
            // dua tema memberi warna senada.
            var latar = LatarKartu();
            GambarSeri(dc, titik, t => t.Skor, KuasIdm, 2.75, null, 5, labelNilai: true, latar, lebar, X, Y, pikselDip);
            GambarSeri(dc, titik, t => t.Iks, KuasIks, 2, null, 3.5, labelNilai: false, null, lebar, X, Y, pikselDip);
            GambarSeri(dc, titik, t => t.Ike, KuasIke, 2, new DoubleCollection { 4, 2 }, 3.5, labelNilai: false, null, lebar, X, Y, pikselDip);
            GambarSeri(dc, titik, t => t.Ikl, KuasIkl, 2, new DoubleCollection { 1.5, 2 }, 3.5, labelNilai: false, null, lebar, X, Y, pikselDip);

            // Label tahun di bawah sumbu X (dibatasi supaya tidak terpotong
            // tepi kanan/kiri pada titik ujung).
            if (kuasTeks != null)
            {
                for (int i = 0; i < n; i++)
                {
                    var label = Teks(titik[i].Tahun.ToString(CultureInfo.InvariantCulture), 11, kuasTeks, pikselDip);
                    double xTeks = Math.Clamp(X(i) - label.Width / 2, 2, lebar - label.Width - 2);
                    dc.DrawText(label, new Point(xTeks, y1 + 8));
                }
            }
        }

        /// <summary>
        /// Latar kartu tempat grafik berada (Border pertama berlatar warna di
        /// atas grafik). Dipakai sebagai latar keping label angka supaya angka
        /// tetap terbaca saat melewati garis grid atau garis seri lain.
        /// </summary>
        private Brush? LatarKartu()
        {
            var induk = VisualTreeHelper.GetParent(this);
            while (induk != null)
            {
                if (induk is Border b && b.Background is Brush bg && !ReferenceEquals(bg, Brushes.Transparent))
                {
                    return bg;
                }
                induk = VisualTreeHelper.GetParent(induk);
            }
            return null;
        }

        /// <summary>
        /// Gambar satu seri: garis antar titik berurutan (putus di tahun yang
        /// nilainya kosong), bulatan penanda, dan bila diminta label angka IDM
        /// — diberi keping latar kartu dan digeser ke samping pada titik ujung
        /// supaya tidak menimpa angka sumbu atau garis seri lain.
        /// </summary>
        private static void GambarSeri(
            DrawingContext dc,
            IReadOnlyList<TitikTren> titik,
            Func<TitikTren, double?> nilai,
            Brush? kuas,
            double tebal,
            DoubleCollection? polaGaris,
            double radius,
            bool labelNilai,
            Brush? latarLabel,
            double lebar,
            Func<int, double> X,
            Func<double, double> Y,
            double pikselDip)
        {
            if (kuas == null) return;

            var pen = new Pen(kuas, tebal)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            if (polaGaris != null) pen.DashStyle = new DashStyle(polaGaris, 0);
            pen.Freeze();

            bool menggambar = false;
            var sebelum = new Point();
            int n = titik.Count;

            for (int i = 0; i < n; i++)
            {
                double? v = nilai(titik[i]);
                if (v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                {
                    menggambar = false;
                    continue;
                }

                var p = new Point(X(i), Y(v.Value));
                if (menggambar) dc.DrawLine(pen, sebelum, p);
                menggambar = true;
                sebelum = p;

                dc.DrawEllipse(kuas, null, p, radius, radius);

                if (labelNilai)
                {
                    var label = Teks(IdmTeks.Angka(v, "0.0000"), 11.5, kuas, pikselDip);

                    // Titik ujung: label di samping dalam (kiri/kanan titik),
                    // bukan di atas — di atas bisa menimpa angka sumbu (ujung
                    // kiri) atau keluar tepi (ujung kanan). Tengah: di atas.
                    Point pos;
                    if (n == 1)
                    {
                        pos = new Point(p.X - label.Width / 2, p.Y - radius - 6 - label.Height);
                    }
                    else if (i == 0)
                    {
                        pos = new Point(p.X + radius + 6, p.Y - label.Height / 2);
                    }
                    else if (i == n - 1)
                    {
                        pos = new Point(p.X - radius - 6 - label.Width, p.Y - label.Height / 2);
                    }
                    else
                    {
                        pos = new Point(
                            Math.Clamp(p.X - label.Width / 2, 2, lebar - label.Width - 2),
                            p.Y - radius - 6 - label.Height);
                    }

                    // Keping latar: angka tetap terbaca saat melewati garis
                    // grid atau garis seri lain yang berwarna.
                    if (latarLabel != null)
                    {
                        dc.DrawRoundedRectangle(
                            latarLabel, null,
                            new Rect(pos.X - 4, pos.Y - 2, label.Width + 8, label.Height + 4),
                            3, 3);
                    }

                    dc.DrawText(label, pos);
                }
            }
        }

        private static FormattedText Teks(string teks, double ukuran, Brush kuas, double pikselDip) =>
            new(teks, Budaya, FlowDirection.LeftToRight, Huruf, ukuran, kuas, pikselDip);
    }
}
