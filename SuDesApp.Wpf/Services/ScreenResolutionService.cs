using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SuDesApp.Wpf.Services
{
    /// <summary>Kategori ukuran layar berdasarkan lebar efektif (DIP).</summary>
    public enum ScreenSizeCategory
    {
        /// <summary>&lt; 1280 DIP (netbook / layar lama).</summary>
        Small,
        /// <summary>1280–1599 DIP (HD).</summary>
        Medium,
        /// <summary>1600–1919 DIP (HD+).</summary>
        Large,
        /// <summary>≥ 1920 DIP (Full HD ke atas).</summary>
        Xlarge
    }

    /// <summary>Hasil pemeriksaan kelayakan tampilan aplikasi pada layar.</summary>
    public sealed class ScreenCheckResult
    {
        /// <summary>Lebar layar utama dalam px fisik.</summary>
        public double PhysicalWidth { get; init; }
        /// <summary>Tinggi layar utama dalam px fisik.</summary>
        public double PhysicalHeight { get; init; }
        /// <summary>Faktor skala DPI (1.0 = 100%, 1.5 = 150%, dst).</summary>
        public double ScaleFactor { get; init; }
        /// <summary>Skala dalam format Windows, mis. "100%", "125%".</summary>
        public string DeviceScale { get; init; } = string.Empty;
        /// <summary>Lebar efektif yang dilihat aplikasi (DIP, setelah skala).</summary>
        public double EffectiveWidth { get; init; }
        /// <summary>Tinggi efektif yang dilihat aplikasi (DIP, setelah skala).</summary>
        public double EffectiveHeight { get; init; }
        /// <summary>Lebar area kerja (tanpa taskbar) dalam DIP.</summary>
        public double WorkAreaWidth { get; init; }
        /// <summary>Tinggi area kerja (tanpa taskbar) dalam DIP.</summary>
        public double WorkAreaHeight { get; init; }
        /// <summary>Kategori ukuran layar efektif.</summary>
        public ScreenSizeCategory Category { get; init; }
        /// <summary>True bila layar mampu menampilkan aplikasi dengan nyaman.</summary>
        public bool IsCompatible { get; init; }
        /// <summary>Ukuran window minimum yang disarankan untuk layar ini.</summary>
        public double RecommendedMinWidth { get; init; }
        public double RecommendedMinHeight { get; init; }
        /// <summary>Ukuran window startup yang disarankan (dibatasi ukuran layar kerja).</summary>
        public double RecommendedStartWidth { get; init; }
        public double RecommendedStartHeight { get; init; }
        /// <summary>True bila ukuran window disarankan berbeda signifikan dari default.</summary>
        public bool ShouldResizeWindow { get; init; }
        /// <summary>Daftar masalah yang ditemukan (kosong bila layar ideal).</summary>
        public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
        /// <summary>Saran perbaikan bagi pengguna.</summary>
        public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
        /// <summary>Satu baris ringkas untuk status bar, mis. "1920×1080 @ 100%".</summary>
        public string SummaryLine { get; init; } = string.Empty;
    }

    /// <summary>
    /// Layanan pemeriksaan resolusi layar — memastikan SuDesApp tampil proporsional
    /// pada semua jenis monitor (1366×768 laptop hingga 4K desktop).
    /// Semua perhitungan memakai API WPF murni (tanpa P/Invoke maupun WinForms)
    /// sehingga aman dijalankan kapan pun; dipanggil saat MainWindow termuat
    /// dan tersedia juga sebagai alat di jendela Alat Canggih.
    /// </summary>
    public class ScreenResolutionService
    {
        /// <summary>Batas minimum yang didukung aplikasi (sama dengan MinWidth/MinHeight window).</summary>
        public const double MinSupportedWidth = 1024;
        public const double MinSupportedHeight = 640;

        /// <summary>Ukuran desain referensi layout (window utama dirancang pada ukuran ini).</summary>
        private const double DesignWidth = 1280;
        private const double DesignHeight = 800;

        private static readonly string[] CategoryNames =
            { "Kecil", "Sedang (HD)", "Besar (HD+)", "Sangat Besar (FHD+)" };

        /// <summary>
        /// Memeriksa layar utama: resolusi fisik, skala DPI, area kerja, dan
        /// menghasilkan ukuran window yang direkomendasikan.
        /// </summary>
        /// <param name="dpiSource">Visual apa pun yang sudah dirender (mis. MainWindow)
        /// agar skala DPI terbaca persis; boleh null — dipakai fallback 100%.</param>
        public ScreenCheckResult CheckPrimaryScreen(Visual? dpiSource = null)
        {
            var scale = GetDpiScale(dpiSource);

            // SystemParameters mengembalikan ukuran layar utama dalam DIP.
            var screenW = SystemParameters.PrimaryScreenWidth;
            var screenH = SystemParameters.PrimaryScreenHeight;
            var workW = SystemParameters.WorkArea.Width;
            var workH = SystemParameters.WorkArea.Height;

            var physicalW = Math.Round(screenW * scale);
            var physicalH = Math.Round(screenH * scale);
            var effectiveW = Math.Round(screenW); // DIP = yang "dirasakan" aplikasi
            var effectiveH = Math.Round(screenH);

            var issues = BuildIssues(effectiveW, effectiveH, scale);
            var recs = BuildRecommendations(effectiveW, effectiveH, scale);

            // Ukuran window startup: desain 1280×800, dibatasi 92% area kerja.
            var startW = Math.Min(DesignWidth, Math.Floor(workW * 0.92));
            var startH = Math.Min(DesignHeight, Math.Floor(workH * 0.92));

            // Minimum dinamis: tidak lebih kecil dari 90% area kerja bila layar sempit,
            // dan tidak melebihi batas minimum yang didukung layout.
            var minW = Math.Clamp(Math.Floor(workW * 0.90), 800, MinSupportedWidth);
            var minH = Math.Clamp(Math.Floor(workH * 0.90), 560, MinSupportedHeight);

            // Perlu resize hanya bila layar tidak muat ukuran default (mis. 1366×768 @ 125%).
            var shouldResize = workW < DesignWidth + 20 || workH < DesignHeight + 20;

            var category = effectiveW switch
            {
                >= 1920 => ScreenSizeCategory.Xlarge,
                >= 1600 => ScreenSizeCategory.Large,
                >= 1280 => ScreenSizeCategory.Medium,
                _ => ScreenSizeCategory.Small
            };

            return new ScreenCheckResult
            {
                PhysicalWidth = physicalW,
                PhysicalHeight = physicalH,
                ScaleFactor = scale,
                DeviceScale = $"{Math.Round(scale * 100)}%",
                EffectiveWidth = effectiveW,
                EffectiveHeight = effectiveH,
                WorkAreaWidth = Math.Round(workW),
                WorkAreaHeight = Math.Round(workH),
                Category = category,
                IsCompatible = issues.Count == 0,
                RecommendedMinWidth = minW,
                RecommendedMinHeight = minH,
                RecommendedStartWidth = startW,
                RecommendedStartHeight = startH,
                ShouldResizeWindow = shouldResize,
                Issues = issues,
                Recommendations = recs,
                SummaryLine =
                    $"🖥️ {physicalW:0}×{physicalH:0} px @ {Math.Round(scale * 100)}% • {CategoryNames[(int)category]}"
            };
        }

        /// <summary>
        /// Menjalankan pemeriksaan dengan toleransi: bila SystemParameters belum siap
        /// (0×0 saat startup sangat awal) atau terjadi exception, kembalikan null —
        /// pemanggil boleh melewati fitur ini tanpa mengganggu aplikasi.
        /// </summary>
        public ScreenCheckResult? TryCheck(int attempts = 3, Visual? dpiSource = null)
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    var result = CheckPrimaryScreen(dpiSource);
                    if (result.EffectiveWidth > 100 && result.EffectiveHeight > 100) return result;
                }
                catch
                {
                    // Coba lagi; jika tetap gagal kembalikan null (fitur dilewati diam-diam).
                }
            }
            return null;
        }

        /// <summary>Skala DPI layar utama (1.0 = 100%) dari CompositionTarget WPF.</summary>
        private static double GetDpiScale(Visual? dpiSource)
        {
            // Prioritas: visual yang diberikan pemanggil (mis. MainWindow saat Loaded).
            var source = dpiSource != null ? PresentationSource.FromVisual(dpiSource) : null;

            // Fallback: window pertama yang aktif.
            source ??= Application.Current?.MainWindow is Window w
                ? PresentationSource.FromVisual(w)
                : null;

            try
            {
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformToDevice;
                    if (m.M11 > 0.01 && !double.IsNaN(m.M11) && !double.IsInfinity(m.M11))
                        return m.M11;
                }
            }
            catch
            {
                // Gagal membaca DPI → anggap 100%.
            }

            return 1.0;
        }

        private static IReadOnlyList<string> BuildIssues(double w, double h, double scale)
        {
            var issues = new List<string>();

            if (w < MinSupportedWidth || h < MinSupportedHeight)
                issues.Add($"Resolusi efektif {w:0}×{h:0} di bawah minimum aplikasi ({MinSupportedWidth:0}×{MinSupportedHeight:0} — beberapa elemen bisa terpotong.");

            if (scale > 2.0)
                issues.Add($"Skala DPI {Math.Round(scale * 100)}% sangat besar — sebagian dialog bisa terlihat sempit.");

            if (h < 700 && scale > 1.25)
                issues.Add("Layar pendek dengan skala tinggi — bilah status & toolbar bisa tersembunyi.");

            return issues;
        }

        private static IReadOnlyList<string> BuildRecommendations(double w, double h, double scale)
        {
            var recs = new List<string>();

            if (w < 1280)
                recs.Add("Gunakan resolusi minimal 1280×720, atau turunkan skala tampilan Windows ke 100%.");
            if (h < 720)
                recs.Add("Aktifkan auto-hide taskbar agar area layar vertikal lebih luas.");
            if (scale >= 1.25 && h < 800)
                recs.Add($"Skala {Math.Round(scale * 100)}% pada layar {h:0}px: pertimbangkan 100–125% agar lebih banyak baris register terlihat.");
            if (w >= 1920 && scale <= 1.0)
                recs.Add("Layar lebar terdeteksi — skala 125% membuat teks lebih nyaman dibaca.");

            if (recs.Count == 0)
                recs.Add("Konfigurasi layar ideal untuk SuDesApp — tidak ada penyesuaian diperlukan.");

            return recs;
        }
    }
}
