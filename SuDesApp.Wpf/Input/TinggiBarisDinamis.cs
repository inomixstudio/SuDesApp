using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Tinggi baris DataGrid yang MENYESUAIKAN panjang teks — tanpa memakai
    /// <c>RowHeight="Auto"</c> (yang terbukti tiga kali memicu loop layout tanpa
    /// henti → StackOverflowException dan menjatuhkan aplikasi ini).
    ///
    /// Cara kerja: jumlah baris teks dihitung per baris data lewat
    /// <see cref="FormattedText"/> (melipat pada lebar kolom sebenarnya), lalu
    /// hasilnya dipasang sebagai <see cref="DataGridRow.Height"/> baris itu —
    /// sebuah BILANGAN TETAP, bukan "Auto". Karena tingginya selalu angka,
    /// panel layout tidak pernah mengukur baris dua kali dan loop layout
    /// mustahil terjadi; teks sepanjang apa pun tetap terbaca.
    ///
    /// Tantangan waktu yang dipecahkan di sini: lebar kolom bintang (*) berubah
    /// beberapa kali selama tata letak awal (0 → ±60 px sementara → lebar final).
    /// Pengukuran yang jatuh pada lebar sementara menghasilkan tinggi yang salah.
    /// Karena itu sebuah <see cref="DispatcherTimer"/> ringan memantau "sidik
    /// lebar" kolom: begitu lebar stabil dua tick (±500 ms), seluruh baris yang
    /// tampil diukur ulang dengan lebar final — dan begitu pula saat jendela
    /// di-resize. Timer tidur otomatis setelah lama tenang dan terbangun lagi
    /// setiap ada baris baru yang dimuat.
    ///
    /// Pemakaian pada XAML (DataGrid apa pun):
    ///     xmlns:input="clr-namespace:SuDesApp.Wpf.Input"
    ///     input:TinggiBarisDinamis.Aktif="True"
    ///
    /// Kolom yang diukur: yang TextBlock-nya melipat (TextWrapping pada
    /// ElementStyle kolom teks, atau pada TextBlock template yang sudah
    /// ter-render). Kolom lain tetap satu baris + tooltip.
    /// </summary>
    public static class TinggiBarisDinamis
    {
        /// <summary>Tinggi minimum baris — serumpun dengan ModernDataGrid (38 px).</summary>
        public const double TinggiMinimum = 38;

        /// <summary>
        /// Batas tinggi baris BAWAAN: teks ekstrem berhenti di sini bila operator belum
        /// pernah mengubah setelannya (≈10 baris teks). Nilai yang benar-benar dipakai
        /// ada di <see cref="TinggiMaksimumEfektif"/>.
        /// </summary>
        public const double TinggiMaksimum = 200;

        private static double? _tinggiMaksimumEfektif;

        /// <summary>
        /// Batas tinggi maksimum yang sedang berlaku: pilihan operator di Pengaturan
        /// Aplikasi (40-600 px, tersimpan di preferensi aplikasi), atau
        /// <see cref="TinggiMaksimum"/> bila belum pernah diubah. Dibaca sekali lalu
        /// diingat — berkas preferensi tidak boleh dibaca ulang untuk setiap baris
        /// tabel yang diukur.
        /// </summary>
        public static double TinggiMaksimumEfektif =>
            _tinggiMaksimumEfektif ??= AppPreferenceStore.GetTinggiBarisMaksimumPx();

        /// <summary>
        /// Muat ulang batas dari preferensi lalu ukur ulang seluruh tabel yang sedang
        /// tampil. Dipanggil halaman Pengaturan Aplikasi begitu operator mengubah
        /// setelannya, supaya tabel yang terbuka langsung menyesuaikan tanpa perlu
        /// dibuka ulang.
        /// </summary>
        public static void SegarkanBatasMaksimum()
        {
            _tinggiMaksimumEfektif = AppPreferenceStore.GetTinggiBarisMaksimumPx();

            foreach (var grid in _kondisi.Keys.ToList())
            {
                if (!_kondisi.TryGetValue(grid, out var kondisi)) continue;

                // Sidik lebar dikosongkan + sidik "perlu ukur" dinyalakan agar tick
                // berikutnya mengukur dengan batas baru walau lebar kolom tak berubah.
                kondisi.SidikLebar = null;
                kondisi.Tenang = 0;
                kondisi.PerluUkur = true;
                kondisi.Timer?.Start();

                try
                {
                    UkurBarisTampak(grid);
                }
                catch
                {
                    // Tabel sudah lepas dari pohon visual — tinggi barisnya tidak lagi
                    // terlihat, jadi tidak ada yang perlu disesuaikan.
                }
            }
        }

        /// <summary>Pengali jarak baris di atas tinggi huruf (≈ leading normal).</summary>
        private const double FaktorBaris = 1.33;

        /// <summary>Padding sel atas + bawah + jarak aman (sel ModernDataGrid 10,6).</summary>
        private const double PaddingSel = 16;

        /// <summary>Periode pemeriksaan kestabilan lebar kolom.</summary>
        private static readonly TimeSpan IntervalPemeriksaan = TimeSpan.FromMilliseconds(250);

        /// <summary>Lebar dianggap final setelah stabil sebanyak ini (2 × 250 ms).</summary>
        private const int TickTenangMinimal = 2;

        /// <summary>Timer tidur setelah tenang selama ini tanpa antrean (±10 detik).</summary>
        private const int TickTenangTidur = 40;

        /// <summary>Aktifkan penghitungan tinggi otomatis pada sebuah DataGrid.</summary>
        public static readonly DependencyProperty AktifProperty =
            DependencyProperty.RegisterAttached(
                "Aktif", typeof(bool), typeof(TinggiBarisDinamis),
                new PropertyMetadata(false, PadaAktifBerubah));

        public static bool GetAktif(DependencyObject obj) => (bool)obj.GetValue(AktifProperty);

        public static void SetAktif(DependencyObject obj, bool nilai) => obj.SetValue(AktifProperty, nilai);

        private static void PadaAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
            {
                var kondisi = new KondisiGrid();
                _kondisi[grid] = kondisi;

                // LoadingRow terpicu setiap kali baris disiapkan untuk tampil —
                // termasuk saat wadah daur ulang (virtualisasi) memuat data baru
                // dan setelah filter/refresh membangun ulang daftar.
                grid.LoadingRow += PadaMemuatBaris;
                grid.Unloaded += PadaGridLepas;

                kondisi.Timer = BuatTimer(grid);
                kondisi.Timer.Start();
            }
            else
            {
                LepasLangganan(grid);
            }
        }

        // ---------- keadaan per grid ----------

        private sealed class KondisiGrid
        {
            /// <summary>Baris yang perlu diukur ulang setelah lebar kolom final.</summary>
            public readonly HashSet<DataGridRow> Antrean = new();

            /// <summary>Sidik lebar kolom pada tick terakhir (untuk deteksi perubahan).</summary>
            public string? SidikLebar;

            /// <summary>Berapa tick berturut lebar tidak berubah.</summary>
            public int Tenang;

            /// <summary>Benar bila ada baris yang menunggu pengukuran dengan lebar final.</summary>
            public bool PerluUkur;

            public DispatcherTimer? Timer;
        }

        private static readonly Dictionary<DataGrid, KondisiGrid> _kondisi = new();

        private static void PadaGridLepas(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                LepasLangganan(grid);
            }
        }

        private static void LepasLangganan(DataGrid grid)
        {
            grid.LoadingRow -= PadaMemuatBaris;
            grid.Unloaded -= PadaGridLepas;

            if (_kondisi.Remove(grid, out var kondisi))
            {
                kondisi.Timer?.Stop();
            }
        }

        private static DispatcherTimer BuatTimer(DataGrid grid)
        {
            var timer = new DispatcherTimer(IntervalPemeriksaan, DispatcherPriority.Background,
                (_, _) => PadaPemeriksaanLebar(grid), grid.Dispatcher);
            return timer;
        }

        /// <summary>
        /// Tick pemeriksaan: bandingkan sidik lebar kolom. Bila lebar berubah,
        /// tandai perlu ukur ulang; bila sudah stabil dua tick dan ada yang
        /// menunggu, ukur seluruh baris yang tampil dengan lebar final.
        /// </summary>
        private static void PadaPemeriksaanLebar(DataGrid grid)
        {
            if (!_kondisi.TryGetValue(grid, out var kondisi)) return;

            var sidik = SidikLebar(grid);
            if (sidik != kondisi.SidikLebar)
            {
                kondisi.SidikLebar = sidik;
                kondisi.Tenang = 0;
                kondisi.PerluUkur = true;
            }
            else
            {
                kondisi.Tenang++;
            }

            if (kondisi.Tenang >= TickTenangMinimal && kondisi.PerluUkur)
            {
                kondisi.PerluUkur = false;
                UkurBarisTampak(grid);
                kondisi.Antrean.Clear();
            }

            if (kondisi.Tenang >= TickTenangTidur && kondisi.Antrean.Count == 0)
            {
                kondisi.Timer?.Stop(); // tenang lama — tidur; terbangun saat ada baris baru
            }
        }

        /// <summary>Sidik lebar seluruh kolom — berubah bila kolom tersusun/resize.</summary>
        private static string SidikLebar(DataGrid grid) =>
            string.Join(",", grid.Columns.Select(c => Math.Round(c.ActualWidth)));

        private static void UkurBarisTampak(DataGrid grid)
        {
            foreach (var baris in CariAnak<DataGridRow>(grid).ToList())
            {
                try
                {
                    UkurInti(grid, baris);
                }
                catch
                {
                    // Satu baris bermasalah tidak menggagalkan yang lain.
                }
            }
        }

        /// <summary>Semua turunan bertipe T di bawah pohon visual induk.</summary>
        private static System.Collections.Generic.IEnumerable<T> CariAnak<T>(DependencyObject induk)
            where T : DependencyObject
        {
            int jumlah = VisualTreeHelper.GetChildrenCount(induk);
            for (int i = 0; i < jumlah; i++)
            {
                var anak = VisualTreeHelper.GetChild(induk, i);
                if (anak is T cocok) yield return cocok;
                foreach (var dalam in CariAnak<T>(anak)) yield return dalam;
            }
        }

        // ---------- pemasangan tinggi ----------

        private static void PadaMemuatBaris(object? sender, DataGridRowEventArgs e)
        {
            var baris = e.Row;
            var grid = (DataGrid)sender!;

            // Ukur segera dengan lebar saat ini (bisa lebar sementara) supaya baris
            // tidak pernah tanpa tinggi — lalu antrekan untuk koreksi oleh timer.
            UkurDanPasang(grid, baris);

            // Kolom template (lampiran, aksi, keterangan berwrap) baru punya TextBlock
            // nyata SETELAH sel ter-render; ukur ulang sekali pada Loaded.
            baris.Loaded -= BarisSelesaiDimuat;
            baris.Loaded += BarisSelesaiDimuat;
        }

        private static void BarisSelesaiDimuat(object sender, RoutedEventArgs e)
        {
            if (sender is DataGridRow baris
                && ItemsControl.ItemsControlFromItemContainer(baris) is DataGrid grid)
            {
                UkurDanPasang(grid, baris);
            }

            // Detach supaya tidak menumpuk handler saat baris daur ulang berulang kali.
            if (sender is DataGridRow selesai)
            {
                selesai.Loaded -= BarisSelesaiDimuat;
            }
        }

        /// <summary>Ukur segera dengan lebar saat ini + antrekan untuk koreksi stabil.</summary>
        private static void UkurDanPasang(DataGrid grid, DataGridRow baris)
        {
            try
            {
                UkurInti(grid, baris);
            }
            catch
            {
                // Pengukuran gagal (font/DPI belum siap) — biarkan tinggi bawaan grid;
                // timer stabilisasi akan mengukur ulang dengan lebar final.
            }

            if (_kondisi.TryGetValue(grid, out var kondisi))
            {
                kondisi.Antrean.Add(baris);
                kondisi.PerluUkur = true;
                kondisi.Timer?.Start(); // bangunkan timer yang sedang tidur
            }
        }

        /// <summary>Inti pengukuran: jumlah baris teks tiap kolom melipat → tinggi tetap per baris.</summary>
        private static void UkurInti(DataGrid grid, DataGridRow baris)
        {
            var tipeGrid = new Typeface(
                grid.FontFamily, grid.FontStyle, grid.FontWeight, FontStretches.Normal);
            double dpi = DpiGrid(grid);

            double tinggiTeks = 0;

            foreach (var kolom in grid.Columns)
            {
                if (kolom.Visibility != Visibility.Visible) continue;

                var terRender = CariTextBlockSel(kolom, baris);
                double ukuranKolom;
                double lebar;
                string? teks;
                Typeface tipe;

                if (terRender is { ActualWidth: > 1 } && !string.IsNullOrWhiteSpace(terRender.Text))
                {
                    // Sumber kebenaran: TextBlock sel yang SUDAH ter-render. Huruf, lebar
                    // lipat, dan tinggi barisnya persis yang dipakai render — jadi jumlah
                    // barisnya sama dengan yang terlihat dan tidak menyisakan baris kosong.
                    if (terRender.TextWrapping == TextWrapping.NoWrap) continue;

                    teks = terRender.Text;
                    ukuranKolom = terRender.FontSize > 0 ? terRender.FontSize : 12;
                    tipe = new Typeface(
                        terRender.FontFamily, terRender.FontStyle,
                        terRender.FontWeight, terRender.FontStretch);

                    // Lebar lipat: lebar yang benar-benar dipakai teks bila sudah melipat;
                    // bila teks masih satu baris (lebar = panjang teksnya sendiri), lebar
                    // kotak sel di kolom itu yang dipakai supaya teks yang sedikit lebih
                    // panjang tidak salah dihitung menjadi dua baris.
                    lebar = Math.Max(terRender.ActualWidth, LebarKolom(kolom));
                }
                else
                {
                    // Sel belum ter-render (baris baru dimuat): perkirakan dari lebar kolom
                    // dan gaya kolomnya; timer stabilisasi akan mengukur ulang setelahnya.
                    if (!KolomMembungkusTeks(kolom, baris, out ukuranKolom)) continue;

                    teks = AmbilTeksKolom(kolom, baris);
                    if (string.IsNullOrWhiteSpace(teks)) continue;

                    lebar = LebarKolom(kolom);
                    tipe = tipeGrid;
                }

                int jumlah = HitungBarisTeks(teks, lebar, tipe, ukuranKolom, dpi, out double tinggiBaris);
                tinggiTeks = Math.Max(tinggiTeks, jumlah * tinggiBaris + PaddingSel);
            }

            // Batas atas mengikuti setelan operator (bawaan 200 px) dan selalu di atas
            // TinggiMinimum, jadi rentang Clamp ini tidak pernah terbalik.
            double batasMaksimum = TinggiMaksimumEfektif;
            double tinggi = tinggiTeks <= 0
                ? TinggiMinimum
                : Math.Clamp(Math.Ceiling(tinggiTeks), TinggiMinimum, batasMaksimum);

            // Beda dengan "Auto": ini angka tetap per baris — panel layout tidak
            // pernah mengukur ulang barisnya sehingga loop layout mustahil.
            baris.Height = tinggi;
        }

        /// <summary>TextBlock sel yang sudah ter-render untuk kolom & baris ini (null bila belum ada).</summary>
        private static TextBlock? CariTextBlockSel(DataGridColumn kolom, DataGridRow baris)
        {
            var konten = kolom.GetCellContent(baris);
            if (konten is TextBlock langsung) return langsung;
            if (konten is ContentPresenter isi) return CariTextBlockKontrol(isi);
            return null;
        }

        /// <summary>Lebar tersedia untuk teks: lebar kolom dikurangi padding sel.</summary>
        private static double LebarKolom(DataGridColumn kolom)
        {
            double lebar = double.IsNaN(kolom.ActualWidth) || kolom.ActualWidth <= 0
                ? kolom.Width.IsAbsolute ? kolom.Width.Value : 120
                : kolom.ActualWidth;
            return Math.Max(40, lebar - PaddingSel - 4);
        }

        /// <summary>
        /// Ambil teks terpanjang yang tampil pada kolom untuk baris ini:
        /// kolom teks dari nilai datanya (ikon konverter ikut bekerja), kolom
        /// template dari TextBlock pertama sel yang sudah ter-render.
        /// </summary>
        private static string? AmbilTeksKolom(DataGridColumn kolom, DataGridRow baris)
        {
            if (kolom is DataGridTextColumn kolomTeks)
            {
                return kolomTeks.OnCopyingCellClipboardContent(baris.Item) as string;
            }

            if (kolom is DataGridTemplateColumn && kolom.GetCellContent(baris) is ContentPresenter isi)
            {
                return CariTextBlock(isi);
            }

            return null;
        }

        private static string? CariTextBlock(DependencyObject induk)
        {
            return CariTextBlockKontrol(induk)?.Text;
        }

        /// <summary>Cari TextBlock pertama yang berisi teks di bawah pohon visual induk.</summary>
        private static TextBlock? CariTextBlockKontrol(DependencyObject induk)
        {
            var jumlah = VisualTreeHelper.GetChildrenCount(induk);
            for (int i = 0; i < jumlah; i++)
            {
                var anak = VisualTreeHelper.GetChild(induk, i);
                if (anak is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    return tb;
                }

                var dalam = CariTextBlockKontrol(anak);
                if (dalam != null) return dalam;
            }
            return null;
        }

        /// <summary>Apakah kolom mengizinkan teks melipat: dari TextBlock sel yang sudah
        /// ter-render (kolom teks maupun kolom template) atau ElementStyle kolom teks.</summary>
        private static bool KolomMembungkusTeks(DataGridColumn kolom, DataGridRow baris, out double ukuranHuruf)
        {
            ukuranHuruf = 12;

            // TextBlock nyata di sel adalah sumber kebenaran — TextWrapping dan
            // FontSize-nya memang yang dipakai render. Penting untuk kolom TEKS juga:
            // teks bisa membungkus dari gaya lain (mis. setter pada CellStyle) yang
            // tidak terbaca dari XAML kolomnya, dan itu tetap harus terhitung — kalau
            // tidak, barisnya berhenti di tinggi minimum dan teksnya terpotong.
            TextBlock? terRender = CariTextBlockSel(kolom, baris);

            if (terRender != null)
            {
                ukuranHuruf = terRender.FontSize > 0 ? terRender.FontSize : 12;
                if (terRender.TextWrapping != TextWrapping.NoWrap) return true;
            }

            if (kolom is DataGridTextColumn kolomTeks
                && kolomTeks.ElementStyle?.Setters.OfType<Setter>() is { } gaya)
            {
                double dariStyle = gaya
                    .OfType<Setter>()
                    .Where(s => s.Property == TextBlock.FontSizeProperty)
                    .Select(s => s.Value)
                    .OfType<double>()
                    .FirstOrDefault();
                if (dariStyle > 0) ukuranHuruf = dariStyle;

                var bungkus = gaya
                    .OfType<Setter>()
                    .Where(s => s.Property == TextBlock.TextWrappingProperty)
                    .Select(s => s.Value)
                    .OfType<TextWrapping>()
                    .FirstOrDefault();

                // Belum pernah diatur = default TextBlock (NoWrap): kolom teks tanpa
                // penanda tidak melipat, tingginya tetap minimum — teks panjang
                // terpotong dan tampil utuh lewat tooltip (pola halaman-halaman ini).
                return bungkus != TextWrapping.NoWrap && bungkus != default;
            }

            return false;
        }

        /// <summary>DPI per unit tampilan visual grid — ukuran huruf akurat pada layar 125%/150%.</summary>
        private static double DpiGrid(DataGrid grid)
        {
            try
            {
                return VisualTreeHelper.GetDpi(grid).PixelsPerDip;
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// Hitung jumlah baris teks pada lebar tertentu: <see cref="FormattedText.MaxTextWidth"/>
        /// melipatkan teks persis seperti TextBlock (sama termasuk pemenggalan kata),
        /// lalu jumlah baris = tinggi total ÷ tinggi barisnya. Satu pengukuran murah
        /// per kolom per baris — tanpa membuat kontrol nyata.
        /// </summary>
        private static int HitungBarisTeks(
            string teks, double lebar, Typeface tipe, double ukuranHuruf, double dpi,
            out double tinggiBaris)
        {
            var terformat = new FormattedText(
                teks,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                tipe,
                ukuranHuruf,
                Brushes.Black,
                dpi)
            {
                MaxTextWidth = lebar,
                Trimming = TextTrimming.None
            };

            // Tinggi baris diambil dari metrik huruf yang bersangkutan (sama dengan yang
            // dipakai render) — bukan pengali tetap — supaya tingginya pas, tidak sisa.
            tinggiBaris = terformat.LineHeight > 0
                ? terformat.LineHeight
                : ukuranHuruf * FaktorBaris;
            return Math.Max(1, (int)Math.Ceiling(terformat.Height / tinggiBaris - 0.01));
        }
    }
}
