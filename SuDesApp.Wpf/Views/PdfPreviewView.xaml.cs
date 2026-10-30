using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SudesPdf;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>Pratinjau PDF multi-halaman berbasis mesin SudesPdf sendiri.</summary>
    public partial class PdfPreviewView : UserControl
    {
        private Document? _document;
        private int _loadGeneration;
        private string _title = string.Empty;
        private string? _currentPdfPath;
        private long _cachedLength = -1;
        private DateTime _cachedLastWrite = DateTime.MinValue;
        private double _scale = 1.0d;

        /// <summary>
        /// Batas bitmap halaman yang disimpan sekaligus. Dokumen panjang bisa
        /// berpuluh halaman; tanpa batas, seluruh bitmap (beberapa MB per
        /// halaman) menumpuk di memori walaupun halaman itu jauh di luar layar.
        /// </summary>
        private const int MaksHalamanTersimpan = 10;

        /// <summary>Anggaran memori bitmap halaman (byte). Pada zoom besar satu
        /// halaman bisa jauh lebih besar dari biasanya, jadi jumlah halaman saja
        /// tidak cukup untuk membatasi pemakaian memori.</summary>
        private const long AnggaranByteHalaman = 96L * 1024 * 1024;

        private readonly List<HalamanTampil> _halaman = new();

        /// <summary>
        /// Dokumen yang kerangka kartunya sedang terpasang. Kerangka hanya boleh
        /// dipakai ulang untuk dokumen yang SAMA: PDF lain dengan jumlah halaman
        /// kebetulan sama akan sempat menampilkan bitmap dokumen sebelumnya bila
        /// yang dibandingkan hanya jumlah halamannya.
        /// </summary>
        private Document? _dokumenKerangka;
        private int _versiRender;
        private bool _renderBerjalan;
        private bool _renderUlang;

        /// <summary>
        /// Satu halaman di daftar pratinjau. Kartu dan ukuran placeholder dibuat
        /// lebih dulu; bitmap baru diisi saat halaman itu (atau tetangganya)
        /// terlihat di layar.
        /// </summary>
        private sealed class HalamanTampil
        {
            public int Index;
            public Border Kartu = null!;
            public Image Gambar = null!;
            public double SkalaGambar;
            public double Kerapatan;
            public long PerkiraanByte;
            public bool SudahDirender;
        }

        public PdfPreviewView()
        {
            InitializeComponent();
            DataContextChanged += PdfPreviewView_DataContextChanged;
        }

        private void PdfPreviewView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            // Saat navigasi ke PDF baru (termasuk saat elemen direcycle oleh
            // ContentPresenter), muat ulang konten.
            if (IsLoaded)
            {
                _ = LoadDocumentAsync();
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => _ = LoadDocumentAsync();

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _loadGeneration++;
            _versiRender++;
            _halaman.Clear();
            _dokumenKerangka = null;
            _ = DisposeDocumentAsync();
            _currentPdfPath = null;
        }

        private async Task LoadDocumentAsync(bool force = false)
        {
            if (force) _currentPdfPath = null;
            int generation = ++_loadGeneration;
            if (DataContext is not PdfPreviewViewModel viewModel) return;

            // Skip reload hanya bila path sama DAN file belum berubah (ukuran+mtime).
            // Tanpa cek isi, PDF hasil edit yang ditulis ulang ke nama file yang sama
            // akan tampil sebagai dokumen basi.
            if (_currentPdfPath == viewModel.PdfPath && _document is not null)
            {
                try
                {
                    var info = new FileInfo(viewModel.PdfPath!);
                    if (_cachedLength == info.Length && _cachedLastWrite == info.LastWriteTimeUtc) return;
                }
                catch (Exception) { /* berkas berubah/terkunci: lanjutkan muat ulang */ }
            }

            _title = viewModel.Title;
            _currentPdfPath = viewModel.PdfPath;
            _cachedLength = -1;
            _cachedLastWrite = DateTime.MinValue;
            await DisposeDocumentAsync();

            if (viewModel.IsMissing || string.IsNullOrEmpty(viewModel.PdfPath))
            {
                ShowMissing();
                return;
            }

            try
            {
                var document = await Document.LoadAsync(viewModel.PdfPath);
                if (generation != _loadGeneration || !IsLoaded)
                {
                    await document.DisposeAsync();
                    return;
                }
                _document = document;
                var info = new FileInfo(viewModel.PdfPath);
                _cachedLength = info.Exists ? info.Length : -1;
                _cachedLastWrite = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
                TampilkanDokumen();
                await FitWidthAsync();
            }
            catch (Exception)
            {
                if (generation == _loadGeneration) ShowMissing();
            }
        }

        /// <summary>
        /// Kembalikan tampilan ke keadaan "dokumen ada". ShowMissing hanya
        /// mematikan, tidak pernah menyalakan kembali — padahal PdfPreviewView
        /// dipakai ulang untuk tiap dokumen yang dinavigasi. Tanpa ini, satu PDF
        /// yang gagal dimuat membuat panel "dokumen tidak ditemukan" dan tombol
        /// Cetak/Ekspor/zoom mati PERMANEN untuk sisa sesi.
        /// </summary>
        private async Task DisposeDocumentAsync()
        {
            var document = _document;
            _document = null;
            if (document is not null) await document.DisposeAsync();
        }

        private void TampilkanDokumen()
        {
            ScrollViewer.Visibility = Visibility.Visible;
            MissingPanel.Visibility = Visibility.Collapsed;
            PrintBtn.IsEnabled = _document is not null;
            ExportBtn.IsEnabled = _document is not null;
            ZoomInBtn.IsEnabled = _document is not null;
            ZoomOutBtn.IsEnabled = _document is not null;
            FitWidthBtn.IsEnabled = _document is not null;
        }

        private void ShowMissing()
        {
            ScrollViewer.Visibility = Visibility.Collapsed;
            MissingPanel.Visibility = Visibility.Visible;
            PrintBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;
            ZoomInBtn.IsEnabled = false;
            ZoomOutBtn.IsEnabled = false;
            FitWidthBtn.IsEnabled = false;
        }

        private void FitWidth_Click(object sender, RoutedEventArgs e) => _ = FitWidthAsync();

        private async Task FitWidthAsync()
        {
            if (_document is null || _document.PageCount == 0) return;
            double available = ScrollViewer.ViewportWidth > 0 ? ScrollViewer.ViewportWidth : 600;
            // Lebar halaman 100% (DIP) tidak bergantung pada skala yang sedang aktif.
            double lebarPenuh = _document.PageSizes[0].Width * 96.0 / 72.0;
            _scale = Math.Clamp((available - 40) / lebarPenuh, 0.1, 8.0);
            await RenderPagesAsync();
        }

        /// <summary>Lebar halaman pada skala tampil (DIP) — 100% = ukuran kertas sebenarnya.</summary>
        private double LebarHalamanDip(int halaman) =>
            _document!.PageSizes[halaman].Width * _scale * 96.0 / 72.0;

        /// <summary>
        /// Rasio piksel layar terhadap DIP (1,0 pada 96 dpi, 1,25 pada 125%).
        /// Bitmap halaman dibuat tepat sebanyak piksel layar, bukan sekadar DIP,
        /// supaya teks tidak melewati penskalaan apa pun saat digambar WPF.
        /// </summary>
        private double SkalaPerangkat() => Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => _ = SetScaleAsync(_scale * 1.2);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => _ = SetScaleAsync(_scale / 1.2);

        private async Task SetScaleAsync(double value)
        {
            _scale = Math.Clamp(value, 0.1, 8.0);
            await RenderPagesAsync();
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
            _ = RenderTerlihatAsync(_versiRender);

        /// <summary>
        /// Bangun kerangka semua halaman, lalu gambar hanya halaman yang sedang
        /// terlihat. Halaman lain digambar saat digulir ke layar. Dulu seluruh
        /// halaman diraster lebih dulu, sehingga membuka PDF panjang terasa
        /// berat dan zoom harus menunggu semua halaman selesai.
        /// </summary>
        private async Task RenderPagesAsync()
        {
            if (_document is null)
            {
                return;
            }

            int versi = ++_versiRender;
            _renderUlang = false;

            double skalaPerangkat = SkalaPerangkat();
            double kerapatan = skalaPerangkat * 96.0;

            // Kerangka halaman dipakai ulang bila jumlah halamannya tidak berubah
            // (zoom, ganti kertas): bitmap yang sudah tampil tetap terpasang dan
            // hanya ukurannya yang diubah, lalu digambar ulang tepat di tempatnya.
            // Membuang lalu membangun seluruh kartu seketika membuat panel kosong
            // setiap kali zoom — kedipan putih yang terasa sebagai "berat".
            bool pakaiUlang = ReferenceEquals(_dokumenKerangka, _document)
                              && _halaman.Count == _document.PageCount;
            if (!pakaiUlang)
            {
                PagesPanel.Children.Clear();
                _halaman.Clear();
                _dokumenKerangka = _document;
            }

            for (int i = 0; i < _document.PageCount; i++)
            {
                if (pakaiUlang)
                {
                    AturUkuranHalaman(_halaman[i], skalaPerangkat, kerapatan);
                }
                else
                {
                    _halaman.Add(BuatKerangkaHalaman(i, skalaPerangkat, kerapatan));
                }
            }

            ZoomText.Text = $"{_scale * 100.0:0}%";

            // Pastikan posisi kartu sudah terukur sebelum menentukan halaman
            // mana yang tampak.
            if (IsLoaded) ScrollViewer.UpdateLayout();

            await RenderTerlihatAsync(versi);
        }

        /// <summary>
        /// Skala render untuk satu halaman. Ukuran gambar ditentukan dalam PIKsel
        /// layar, bukan DIP, lalu skala render diturunkan dari jumlah piksel itu:
        /// dengan begitu lebar bitmap persis sama dengan lebar yang diminta tata
        /// letak, sehingga tidak ada selisih pecahan piksel yang membuat WPF
        /// menyaring ulang seluruh halaman (itulah yang mengaburkan huruf).
        /// </summary>
        private double SkalaRender(int halaman, double skalaPerangkat)
        {
            double lebarDip = LebarHalamanDip(halaman);
            int lebarPiksel = Math.Max(1, (int)Math.Round(lebarDip * skalaPerangkat));
            // Epsilon kecil supaya pembulatan ke atas di mesin render tidak
            // menambah satu piksel (yang membuat WPF menyaring ulang).
            return Math.Min(16.0,
                lebarPiksel / Math.Max(1.0, _document!.PageSizes[halaman].Width) * (1 - 1e-9));
        }

        /// <summary>
        /// Ukuran tampil diturunkan dari jumlah PIKsel bitmap (bukan dari DIP
        /// pecahan), persis seperti ukuran asli bitmap nanti — jadi WPF tidak
        /// perlu menskalakan satu piksel pun saat bitmap itu terpasang.
        /// </summary>
        private void UkuranTampil(double skalaGambar, double kerapatan,
            out double lebarTampil, out double tinggiTampil, out long perkiraanByte, int halaman = 0)
        {
            var size = _document!.PageSizes[halaman];
            int lebarPiksel = Math.Max(1, (int)Math.Round(size.Width * skalaGambar));
            int tinggiPiksel = Math.Max(1, (int)Math.Ceiling(size.Height * skalaGambar));
            lebarTampil = lebarPiksel * 96.0 / kerapatan;
            tinggiTampil = tinggiPiksel * 96.0 / kerapatan;
            perkiraanByte = (long)lebarPiksel * tinggiPiksel * 4;
        }

        private HalamanTampil BuatKerangkaHalaman(int i, double skalaPerangkat, double kerapatan)
        {
            double skalaGambar = SkalaRender(i, skalaPerangkat);
            UkuranTampil(skalaGambar, kerapatan, out double lebarTampil, out double tinggiTampil,
                out long perkiraanByte, i);

            var halaman = new Image
            {
                Width = lebarTampil,
                Height = tinggiTampil,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(halaman, BitmapScalingMode.HighQuality);

            var card = new Border
            {
                // Ukuran kartu ditetapkan sejak awal supaya penggulungan
                // tidak melompat ketika bitmap halaman selesai digambar.
                Width = lebarTampil + 16,
                Height = tinggiTampil + 16,
                Background = TryFindResource("KertasBrush") as Brush ?? Brushes.White,
                BorderBrush = TryFindResource("KertasTepiBrush") as Brush
                              ?? new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7),
                Child = halaman
            };

            // Label "Halaman N dari M" di bawah setiap halaman DIHAPUS.
            // Angka halaman sudah bisa dibaca dari jumlah kartu halaman yang
            // berbentuk edge-to-edge (dan dari nomor surat pada dokumen),
            // sementara label ini memakan tempat, mengulang informasi yang
            // sama untuk setiap kartu, dan selalu tampil "Halaman 1 dari 1"
            // pada surat satu halaman — seakan-ini pesan, bukan keterangan.

            // Bayangan TIDAK dipasang pada kartu yang memuat halaman.
            // Effect pada WPF menggambar seluruh isi elemen ke bitmap antara
            // sebelum dikomposisikan, jadi memasang DropShadowEffect pada
            // kartu membuat gambar halaman ikut disaring — teksnya kembali
            // kabur. Bayangan ditaruh pada elemen kosong di belakang kartu.
            var bayangan = new Border
            {
                Background = card.Background,
                CornerRadius = new CornerRadius(3),
                Effect = new DropShadowEffect
                {
                    Color = TryFindResource("BayanganHitamColor") is Color warnaBayangan
                        ? warnaBayangan
                        : Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.3
                },
                // Bayangan tidak berubah saat halaman digulir; simpan
                // hasilnya sebagai bitmap supaya efek blur tidak dihitung
                // ulang setiap kali halaman bergerak di layar.
                CacheMode = new BitmapCache()
            };

            var wadah = new Grid
            {
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            wadah.Children.Add(bayangan);
            wadah.Children.Add(card);
            PagesPanel.Children.Add(wadah);

            return new HalamanTampil
            {
                Index = i,
                Kartu = card,
                Gambar = halaman,
                SkalaGambar = skalaGambar,
                Kerapatan = kerapatan,
                PerkiraanByte = perkiraanByte
            };
        }

        /// <summary>
        /// Sesuaikan halaman yang sudah ada dengan skala baru tanpa melepas
        /// bitmapnya: gambar lama tetap tampil (diregangkan sesaat) sampai
        /// penggambaran ulang selesai, jadi panel tidak pernah kosong.
        /// </summary>
        private void AturUkuranHalaman(HalamanTampil halaman, double skalaPerangkat, double kerapatan)
        {
            double skalaGambar = SkalaRender(halaman.Index, skalaPerangkat);
            UkuranTampil(skalaGambar, kerapatan, out double lebarTampil, out double tinggiTampil,
                out long perkiraanByte, halaman.Index);

            halaman.SkalaGambar = skalaGambar;
            halaman.Kerapatan = kerapatan;
            halaman.PerkiraanByte = perkiraanByte;
            halaman.SudahDirender = false;
            halaman.Gambar.Width = lebarTampil;
            halaman.Gambar.Height = tinggiTampil;
            halaman.Kartu.Width = lebarTampil + 16;
            halaman.Kartu.Height = tinggiTampil + 16;
        }

        /// <summary>
        /// Gambar halaman yang terlihat satu per satu. Permintaan yang datang
        /// saat penggambaran berjalan (mis. pengguna menggulir atau memperbesar)
        /// tidak dibuang: setelah halaman berjalan selesai, putaran baru dimulai
        /// dengan versi tata letak terbaru.
        /// </summary>
        private async Task RenderTerlihatAsync(int versi)
        {
            if (_document is null || versi != _versiRender) return;

            _renderUlang = true;
            if (_renderBerjalan) return;

            _renderBerjalan = true;
            try
            {
                while (true)
                {
                    _renderUlang = false;
                    int versiKini = _versiRender;
                    var dokumen = _document;
                    if (dokumen is null) break;

                    while (versiKini == _versiRender)
                    {
                        var halaman = HalamanBerikutnya();
                        if (halaman is null) break;

                        BitmapSource bitmap;
                        try
                        {
                            bitmap = await dokumen.RenderAsync(
                                halaman.Index, halaman.SkalaGambar, halaman.Kerapatan);
                        }
                        catch (Exception)
                        {
                            return;
                        }

                        if (versiKini != _versiRender || !ReferenceEquals(_document, dokumen))
                        {
                            break;
                        }

                        halaman.Gambar.Source = bitmap;
                        halaman.SudahDirender = true;
                        BuangHalamanJauh();
                    }

                    if (versiKini == _versiRender && !_renderUlang) break;
                }
            }
            finally
            {
                _renderBerjalan = false;
                // Bila permintaan baru datang tepat saat penggambaran berakhir,
                // jalankan sekali lagi untuk versi yang sekarang.
                if (_renderUlang && _document is not null) _ = RenderTerlihatAsync(_versiRender);
            }
        }

        /// <summary>Halaman tak terlihat pertama, atau null bila semua yang tampak sudah digambar.</summary>
        private HalamanTampil? HalamanBerikutnya()
        {
            bool adaYangDirender = false;
            foreach (var halaman in _halaman)
            {
                if (halaman.SudahDirender)
                {
                    adaYangDirender = true;
                    continue;
                }

                if (Terlihat(halaman)) return halaman;
            }

            // Bila ukuran viewport belum siap (mis. baru pertama ditampilkan),
            // jangan biarkan pratinjau kosong: halaman pertama selalu digambar.
            return !adaYangDirender && _halaman.Count > 0 ? _halaman[0] : null;
        }

        /// <summary>
        /// Benar bila kartu halaman menyentuh area pandang, dengan cadangan
        /// setengah layar di atas dan di bawah supaya menggulir terasa mulus.
        /// </summary>
        private bool Terlihat(HalamanTampil halaman)
        {
            try
            {
                double atas = halaman.Kartu.TranslatePoint(new Point(0, 0), ScrollViewer).Y;
                double bawah = atas + halaman.Kartu.Height;
                double tinggi = ScrollViewer.ViewportHeight > 0 ? ScrollViewer.ViewportHeight : 800;
                return bawah > -tinggi * 0.5 && atas < tinggi * 1.5;
            }
            catch (InvalidOperationException)
            {
                // Kartu belum terpasang di pohon visual (mis. saat berpindah
                // dokumen); anggap tidak terlihat dan biarkan putaran berikutnya
                // yang menggambar.
                return false;
            }
        }

        /// <summary>Lepas bitmap halaman yang paling jauh dari layar bila sudah terlalu banyak.</summary>
        private void BuangHalamanJauh()
        {
            int tersimpan = 0;
            long totalByte = 0;
            foreach (var halaman in _halaman)
            {
                if (!halaman.SudahDirender) continue;
                tersimpan++;
                totalByte += halaman.PerkiraanByte;
            }

            if (tersimpan <= MaksHalamanTersimpan && totalByte <= AnggaranByteHalaman) return;

            while (tersimpan > MaksHalamanTersimpan || totalByte > AnggaranByteHalaman)
            {
                HalamanTampil? terjauh = null;
                double jarakTerjauh = -1;
                foreach (var halaman in _halaman)
                {
                    // Halaman yang masih tampak (termasuk cadangan setengah
                    // layar) tidak pernah dilepas.
                    if (!halaman.SudahDirender || Terlihat(halaman)) continue;

                    double jarak;
                    try
                    {
                        double atas = halaman.Kartu.TranslatePoint(new Point(0, 0), ScrollViewer).Y;
                        double tengah = ScrollViewer.ViewportHeight / 2.0;
                        jarak = Math.Abs(atas - tengah);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    if (jarak > jarakTerjauh)
                    {
                        jarakTerjauh = jarak;
                        terjauh = halaman;
                    }
                }

                if (terjauh is null) return;
                terjauh.Gambar.Source = null;
                terjauh.SudahDirender = false;
                tersimpan--;
                totalByte -= terjauh.PerkiraanByte;
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e) => _ = PrintAsync();

        private async Task PrintAsync()
        {
            var document = _document;
            if (document is null || document.PageCount == 0) return;
            try
            {
                if (DataContext is PdfPreviewViewModel vm && !await vm.BolehCetakAsync()) return;
                if (!ReferenceEquals(document, _document)) return;
                await document.PrintAsync(_title);
            }
            catch (Exception)
            {
                // Cetak dibatalkan atau gagal; biarkan pengguna mencoba lagi.
            }
        }

    }
}