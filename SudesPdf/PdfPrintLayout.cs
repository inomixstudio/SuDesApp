using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;

namespace SudesPdf;

/// <summary>
/// Geometri cetak satu halaman PDF ke lembar kertas. Sengaja dipisahkan dari
/// <see cref="PdfPrinter"/> supaya bisa diuji tanpa printer: inti aturan kertas
/// bukan proses rendering, tetapi "halaman ini jadi seberapa besar di atas kertas"
/// dan "halaman diletakkan di mana".
/// </summary>
internal static class PdfPrintLayout
{
    /// <summary>Batas piksel raster per titik PDF (16 ≈ raster 1152 dpi). Di atas itu
    /// hanya memakan memori tanpa ketajaman tambahan, dan <c>Document.Render</c>
    /// memang menolak skala lebih besar.</summary>
    internal const double SkalaRasterMaksimal = 16.0;

    /// <summary>Batas bawah skala raster (piksel per titik) untuk input yang rusak.</summary>
    internal const double SkalaRasterMinimal = 0.05;

    /// <summary>Toleransi pemilihan ukuran kertas driver terhadap halaman PDF, dalam
    /// inci (0,1 inci ≈ 2,5 mm). Di luar itu kertas driver dianggap kertas lain.</summary>
    internal const double ToleransiKertasIn = 0.1;

    /// <summary>
    /// Keadaan kertas sebenarnya: ukuran FISIK lembar, berapa jauh printer harus
    /// berhenti dari tepinya (margin keras), dan kotak tinta dalam piksel printer.
    /// </summary>
    internal readonly record struct Kertas(
        double LebarIn,
        double TinggiIn,
        double TepiKiriIn,
        double TepiAtasIn,
        double KotakLebarPx,
        double KotakTinggiPx,
        double DpiX,
        double DpiY);

    /// <summary>Hasil tata letak: kotak halaman dalam koordinat graphics (titik
    /// (0,0) = kiri-atas kotak tinta, jadi bisa bernilai negatif ketika halaman
    /// menutupi margin keras), plus skala yang dipakai untuk raster.</summary>
    internal readonly record struct Hasil(
        double SkalaFisik,
        double SkalaRaster,
        int HalamanX,
        int HalamanY,
        int HalamanLebarPx,
        int HalamanTinggiPx,
        int KotakLebarPx,
        int KotakTinggiPx,
        double LembarLebarIn,
        double LembarTinggiIn)
    {
        /// <summary>Kotak halaman utuh di koordinat graphics (boleh keluar kotak tinta).</summary>
        public Rectangle Halaman => new(HalamanX, HalamanY, HalamanLebarPx, HalamanTinggiPx);

        /// <summary>Bagian halaman yang benar-benar tercetak driver.</summary>
        public Rectangle Tujuan => Rectangle.Intersect(
            Halaman,
            new Rectangle(0, 0, KotakLebarPx, KotakTinggiPx));
    }

    /// <summary>
    /// Cetak pada UKURAN SEBENARNYA: halaman dipetakan 1:1 ke lembar kertas dan
    /// baru diperkecil bila halamannya memang lebih besar dari kertas yang tersedia.
    /// Tidak pernah memperbesar.
    ///
    /// Ini yang dulu salah: halaman selalu "dipaskan ke kotak tinta" printer,
    /// sehingga margin keras (umumnya 4-5 mm per sisi pada laser, lebih besar pada
    /// inkjet) ikut memakan ukuran hasil cetak — surat 210 mm keluar ±4% lebih kecil
    /// dan bergeser, yang terlihat sebagai "ukurannya tidak pas tapi cuma sedikit".
    /// </summary>
    internal static Hasil Hitung(double halamanLebarPt, double halamanTinggiPt, Kertas kertas)
    {
        double dpiX = Pozitif(kertas.DpiX, 300);
        double dpiY = Pozitif(kertas.DpiY, dpiX);

        double lebarHalamanIn = Aman(halamanLebarPt, 1) / 72.0;
        double tinggiHalamanIn = Aman(halamanTinggiPt, 1) / 72.0;

        // Ukuran lembar: kalau driver tidak memberi ukuran kertas, kotak tinta yang
        // dipakai (perilaku lama) supaya tetap ada angka yang masuk akal.
        double lebarLembarIn = Pozitif(kertas.LebarIn, 0);
        double tinggiLembarIn = Pozitif(kertas.TinggiIn, 0);
        double kotakLebarIn = Pozitif(kertas.KotakLebarPx, 0) / dpiX;
        double kotakTinggiIn = Pozitif(kertas.KotakTinggiPx, 0) / dpiY;
        if (lebarLembarIn <= 0 || tinggiLembarIn <= 0)
        {
            lebarLembarIn = Math.Max(1.0, kotakLebarIn + 2 * Aman(kertas.TepiKiriIn, 0));
            tinggiLembarIn = Math.Max(1.0, kotakTinggiIn + Aman(kertas.TepiAtasIn, 0) * 2);
        }

        double tepiKiriIn = Aman(kertas.TepiKiriIn, 0);
        double tepiAtasIn = Aman(kertas.TepiAtasIn, 0);

        double muatLebar = lebarLembarIn / lebarHalamanIn;
        double muatTinggi = tinggiLembarIn / tinggiHalamanIn;
        double skalaFisik = Math.Min(1.0, Math.Min(muatLebar, muatTinggi));
        if (!double.IsFinite(skalaFisik) || skalaFisik <= 0) skalaFisik = 1.0;

        int lebarPx = (int)Math.Round(lebarHalamanIn * skalaFisik * dpiX);
        int tinggiPx = (int)Math.Round(tinggiHalamanIn * skalaFisik * dpiY);
        if (lebarPx < 1) lebarPx = 1;
        if (tinggiPx < 1) tinggiPx = 1;

        // Halaman dipusatkan pada LETAR KERTAS, bukan pada kotak tinta: dengan begitu
        // kop surat dan tempat tanda tangan jatuh pada jarak yang sama seperti di
        // layar. Margin keras hanya memotong tepi yang toh tidak dipakai surat desa
        // (margin desain halaman surat 45 pt ≈ 16 mm).
        double tepiKiriPx = tepiKiriIn * dpiX;
        double tepiAtasPx = tepiAtasIn * dpiY;
        int x = (int)Math.Round((lebarLembarIn * dpiX - lebarPx) / 2.0 - tepiKiriPx);
        int y = (int)Math.Round((tinggiLembarIn * dpiY - tinggiPx) / 2.0 - tepiAtasPx);

        int kotakLebarPx = (int)Math.Round(Pozitif(kertas.KotakLebarPx, lebarLembarIn * dpiX));
        int kotakTinggiPx = (int)Math.Round(Pozitif(kertas.KotakTinggiPx, tinggiLembarIn * dpiY));

        // Skala raster = piksel per titik PDF. Dibatasi supaya printer 1200/2400 dpi
        // tidak meminta raster yang ditolak Document.Render (lewat 16) atau habis
        // memori; kalau terpotong, gambar tetap digambar ke kotak tujuan (dikurangi
        // oleh penskalaan satu kali, bukan dua kali seperti dulu).
        double skalaRaster = (double)lebarPx / Aman(halamanLebarPt, 1);
        skalaRaster = Math.Clamp(skalaRaster, SkalaRasterMinimal, SkalaRasterMaksimal);

        return new Hasil(
            skalaFisik,
            skalaRaster,
            x,
            y,
            lebarPx,
            tinggiPx,
            kotakLebarPx,
            kotakTinggiPx,
            lebarLembarIn,
            tinggiLembarIn);
    }

    /// <summary>
    /// Kotak sumber di dalam raster yang cocok dengan kotak tujuan. Dibatasi supaya
    /// tidak pernah keluar raster (sisi halaman yang tidak tercetak driver tidak
    /// ikut dibaca).
    /// </summary>
    internal static Rectangle SumberUntukTujuan(Hasil letak, Rectangle tujuan, int rasterLebar, int rasterTinggi)
    {
        if (rasterLebar <= 0 || rasterTinggi <= 0) return Rectangle.Empty;
        Rectangle halaman = letak.Halaman;
        if (halaman.Width <= 0 || halaman.Height <= 0)
            return new Rectangle(0, 0, rasterLebar, rasterTinggi);

        double fx = (double)rasterLebar / halaman.Width;
        double fy = (double)rasterTinggi / halaman.Height;

        int x = (int)Math.Round((tujuan.X - halaman.X) * fx);
        int y = (int)Math.Round((tujuan.Y - halaman.Y) * fy);
        int lebar = (int)Math.Round(tujuan.Width * fx);
        int tinggi = (int)Math.Round(tujuan.Height * fy);

        x = Math.Clamp(x, 0, Math.Max(0, rasterLebar - 1));
        y = Math.Clamp(y, 0, Math.Max(0, rasterTinggi - 1));
        lebar = Math.Clamp(lebar, 1, rasterLebar - x);
        tinggi = Math.Clamp(tinggi, 1, rasterTinggi - y);
        return new Rectangle(x, y, lebar, tinggi);
    }

    /// <summary>
    /// Ukuran kertas driver yang paling dekat dengan halaman PDF, atau null bila
    /// tidak ada yang cocok. Perbandingan dilakukan tanpa peduli orientasi (kertas
    /// A4 driver tersimpan 210x297 mm sementara halaman landscape 297x210 mm).
    /// </summary>
    internal static PaperSize? CariUkuranKertas(
        IEnumerable<PaperSize>? daftar,
        double halamanLebarIn,
        double halamanTinggiIn)
    {
        if (daftar is null) return null;
        if (halamanLebarIn <= 0 || halamanTinggiIn <= 0) return null;

        double besarHalaman = Math.Max(halamanLebarIn, halamanTinggiIn);
        double kecilHalaman = Math.Min(halamanLebarIn, halamanTinggiIn);

        PaperSize? terbaik = null;
        double selisihTerbaik = double.MaxValue;
        foreach (PaperSize ukuran in daftar)
        {
            if (ukuran is null) continue;
            double besar = Math.Max(ukuran.Width, ukuran.Height) / 100.0;
            double kecil = Math.Min(ukuran.Width, ukuran.Height) / 100.0;
            if (ukuran.Width <= 0 || ukuran.Height <= 0 || besar <= 0 || kecil <= 0) continue;

            double selisih = Math.Max(Math.Abs(besar - besarHalaman), Math.Abs(kecil - kecilHalaman));
            if (selisih < selisihTerbaik)
            {
                selisihTerbaik = selisih;
                terbaik = ukuran;
            }
        }

        return selisihTerbaik <= ToleransiKertasIn ? terbaik : null;
    }

    /// <summary>
    /// Bendera Landscape yang membuat sisi panjang kertas driver berbaris dengan
    /// sisi panjang halaman. PaperSize driver biasanya tersimpan potret, jadi untuk
    /// halaman mendatar kertasnya dibalik.
    /// </summary>
    internal static bool MiringUntuk(PaperSize ukuran, double halamanLebarPt, double halamanTinggiPt)
    {
        if (ukuran is null) return halamanLebarPt > halamanTinggiPt;
        bool kertasMendatar = ukuran.Width > ukuran.Height;
        return (halamanLebarPt > halamanTinggiPt) ^ kertasMendatar;
    }

    /// <summary>Sisi terpanjang formulir kustom yang masih layak diminta ke printer
    /// kantor. Di atas ini driver umumnya menolak dan kertasnya bukan kertas surat.</summary>
    internal const double BatasKustomInc = 17.0;

    /// <summary>Sisi terpendek yang masih diterima driver (amplop sudah lewat dialog sendiri).</summary>
    internal const double BatasKustomMinInc = 2.0;

    /// <summary>
    /// Formulir kertas dengan ukuran HALAMAN PDF, untuk printer yang tidak punya
    /// padanannya di daftar (mis. "Microsoft Print to PDF" atau driver laser lama
    /// tanpa F4). Nama formulir dibatasi 31 karakter karena DEVMODE menyimpannya
    /// sebagai WCHAR[32]; nilai ukuran dalam persepuluh inci.
    ///
    /// Arah aturannya: ukuran aplikasi yang dipasang ke printer, bukan surat yang
    /// dikecilkan supaya muat kertas printer. Kalau nanti driver ternyata mengabaikan
    /// formulir ini, <see cref="PdfPrinter"/> membandingkan lembar yang dilaporkan
    /// saat cetak dan memberi tahu pengguna, bukan diam-diam.
    /// </summary>
    internal static PaperSize? BuatKertasKustom(double halamanLebarIn, double halamanTinggiIn)
    {
        if (!double.IsFinite(halamanLebarIn) || !double.IsFinite(halamanTinggiIn)) return null;
        double besar = Math.Max(halamanLebarIn, halamanTinggiIn);
        double kecil = Math.Min(halamanLebarIn, halamanTinggiIn);
        if (kecil < BatasKustomMinInc || besar > BatasKustomInc) return null;

        int lebar = (int)Math.Round(halamanLebarIn * 100.0);
        int tinggi = (int)Math.Round(halamanTinggiIn * 100.0);
        if (lebar < 1 || tinggi < 1) return null;
        return new PaperSize(NamaKertasKustom(lebar, tinggi), lebar, tinggi);
    }

    /// <summary>Nama formulir dari ukuran persepuluh inci, mis. "F4 215x330mm".</summary>
    internal static string NamaKertasKustom(int lebarPersepuluhIn, int tinggiPersepuluhIn)
    {
        int mmLebar = (int)Math.Round(lebarPersepuluhIn * 25.4 / 100.0);
        int mmTinggi = (int)Math.Round(tinggiPersepuluhIn * 25.4 / 100.0);
        string label = (mmLebar, mmTinggi) switch
        {
            (215, 330) => "F4 ",
            (216, 330) => "Folio ",
            (210, 297) => "A4 ",
            (297, 420) => "A3 ",
            (148, 210) => "A5 ",
            (216, 279) => "Letter ",
            (216, 356) => "Legal ",
            _ => string.Empty,
        };
        string nama = $"{label}{mmLebar}x{mmTinggi}mm";
        return nama.Length <= 31 ? nama : nama[..31];
    }

    /// <summary>
    /// Apakah lembar yang dilaporkan driver sama dengan yang diminta, dalam inci.
    /// Orientasi diabaikan (lembar bisa dilaporkan menyamping).
    /// </summary>
    internal static bool LembarSama(PaperSize? lembar, double lebarIn, double tinggiIn, double toleransiIn)
    {
        if (lembar is null || lembar.Width <= 0 || lembar.Height <= 0) return false;
        double besar = Math.Max(lembar.Width, lembar.Height) / 100.0;
        double kecil = Math.Min(lembar.Width, lembar.Height) / 100.0;
        return Math.Abs(besar - Math.Max(lebarIn, tinggiIn)) <= toleransiIn
            && Math.Abs(kecil - Math.Min(lebarIn, tinggiIn)) <= toleransiIn;
    }

    private static double Pozitif(double nilai, double cadangan) =>
        double.IsFinite(nilai) && nilai > 0 ? nilai : cadangan;

    private static double Aman(double nilai, double cadangan) =>
        double.IsFinite(nilai) && nilai > 0 ? nilai : cadangan;
}
