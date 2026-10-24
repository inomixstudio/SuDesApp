using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Membuat ikon jendela (taskbar / Alt-Tab) yang mengikuti tema aktif.
    /// Ikon dibuat sebagai ubin membulat dengan warna aksen + amplop putih,
    /// sehingga menyatu dengan logo di dalam aplikasi dan berubah otomatis
    /// saat pengguna mengganti tema.
    /// </summary>
    public static class PenataIkonJendela
    {
        /// <summary>
        /// Baca warna aksen dari ResourceDictionary aplikasi lalu buat ikon temanya.
        /// Jatuh ke hijau baku bila resource tidak ditemukan.
        /// </summary>
        public static ImageSource? BuatUntukTemaAktif()
        {
            var aksen = (Color)ColorConverter.ConvertFromString("#16A34A");
            if (Application.Current?.FindResource("AccentColor") is Color warna)
            {
                aksen = warna;
            }
            return Buat(aksen);
        }

        /// <summary>Gambar ubin aksen membulat + amplop putih dengan panah lipatan
        /// berwarna aksen gelap + segel kecil — padanan ringkas ikon aplikasi asli.</summary>
        public static ImageSource Buat(Color aksen)
        {
            const double s = 128;   // gambar besar, diturunkan WPF untuk taskbar/Alt-Tab

            var vis = new DrawingVisual();
            using (var ctx = vis.RenderOpen())
            {
                // Latar ubin: gradien aksen (lebih terang di atas, lebih gelap di bawah)
                // dibandingkan dengan piksel aksen agar ikon tetap dua dimensi di layar.
                var atas = Campur(aksen, Colors.White, 0.16);
                var bawah = Campur(aksen, Colors.Black, 0.20);

                var bg = new LinearGradientBrush(atas, bawah, 90.0);
                var radius = s * 0.225;
                ctx.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), radius, radius);

                // Kilap kaca diagonal (cahaya dari kiri-atas).
                var gloss = new LinearGradientBrush(
                    Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
                    Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90.0);
                ctx.DrawRoundedRectangle(gloss, null,
                    new Rect(s * 0.04, s * 0.04, s * 0.92, s * 0.42), radius * 0.9, radius * 0.9);

                // Amplop putih.
                var envX = s * 0.16; var envY = s * 0.27;
                var envW = s * 0.68; var envH = s * 0.47;
                var envRound = s * 0.07;

                ctx.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
                    null,
                    new Rect(envX, envY, envW, envH), envRound, envRound);

                // Lipatan panah ke bawah (fold) di tengah amplop: garis aksen gelap
                // yang membentuk chevron — menegaskan "dokumen dikirim/terkirim".
                var cx = envX + envW / 2;
                var foldY = envY + envH * 0.30;
                var penLipat2x = new Pen(new SolidColorBrush(Campur(aksen, Colors.Black, 0.30)), s * 0.05)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                var lipatan = new StreamGeometry();
                using (var g = lipatan.Open())
                {
                    g.BeginFigure(new Point(cx, envY + s * 0.005), false, false);
                    g.LineTo(new Point(cx, foldY), true, false);
                    g.BeginFigure(new Point(envX + s * 0.010, foldY), false, false);
                    g.LineTo(new Point(cx, envY + s * 0.005), true, false);
                    g.BeginFigure(new Point(cx, envY + s * 0.005), false, false);
                    g.LineTo(new Point(envX + envW - s * 0.010, foldY), true, false);
                }
                lipatan.Freeze();
                ctx.DrawGeometry(null, penLipat2x, lipatan);

                // Segel kecil di bawah tengah — kenang segel lilin ikon asli.
                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                    null,
                    new Point(cx, envY + envH * 0.80),
                    s * 0.045, s * 0.045);
                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    null,
                    new Point(cx - s * 0.014, envY + envH * 0.80 - s * 0.014),
                    s * 0.014, s * 0.014);
            }

            var bmp = new RenderTargetBitmap((int)s, (int)s, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(vis);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Campurkan warna dasar dengan warna lain sebesar proporsi (0..1).</summary>
        private static Color Campur(Color dasar, Color pencampur, double proporsi)
        {
            var t = Math.Clamp(proporsi, 0, 1);
            return Color.FromRgb(
                (byte)(dasar.R + (pencampur.R - dasar.R) * t),
                (byte)(dasar.G + (pencampur.G - dasar.G) * t),
                (byte)(dasar.B + (pencampur.B - dasar.B) * t));
        }
    }
}