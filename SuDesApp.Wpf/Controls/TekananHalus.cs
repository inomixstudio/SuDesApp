using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Umpan balik hidup untuk tombol: mengecil sedikit saat ditekan (skala 0,97),
    /// mengangkat 1 px saat disorot, dan memudarkan lapisan sorot/tekan di dalam
    /// templatnya. Dipasang dari gaya implicit Button:
    ///
    /// <code>
    /// &lt;Setter Property="ctrl:TekananHalus.Aktif" Value="True"/&gt;
    /// </code>
    ///
    /// Seperti <see cref="HoverAnimasiSidebar"/>, durasinya diambil dari
    /// <see cref="KecepatanAnimasiPrefs"/> sehingga pilihan Lambat/Normal/Cepat
    /// berlaku juga di sini — itu sebabnya gerakan ini ada di kode dan bukan di
    /// EventTrigger XAML: durasi EventTrigger tidak bisa berubah saat aplikasi
    /// berjalan, padahal kecepatan animasi adalah preferensi pengguna.
    ///
    /// Ringan: yang disentuh hanya Opacity dan RenderTransform (lapisan komposisi),
    /// jadi tidak ada pengukuran ulang atau layout pass per frame. Bagian templat
    /// dicari menurut nama — <c>SorotLapis</c>, <c>TekanLapis</c>, <c>Kulit</c> —
    /// dan tombol yang tidak memilikinya (templat khusus seperti IconButton atau
    /// toggle combo) dibiarkan apa adanya, tetap dengan pemicu XAML-nya sendiri.
    ///
    /// Bila Windows diminta mengurangi animasi (<see cref="SystemParameters.ClientAreaAnimation"/>
    /// mati), nilai diterapkan seketika tanpa transisi: umpan balik tetap ada,
    /// hanya gerakannya yang hilang.
    /// </summary>
    public static class TekananHalus
    {
        /// <summary>Nyalakan umpan balik tekan pada tombol ini.</summary>
        public static readonly DependencyProperty AktifProperty =
            DependencyProperty.RegisterAttached(
                "Aktif", typeof(bool), typeof(TekananHalus),
                new PropertyMetadata(false, OnAktifBerubah));

        /// <summary>
        /// Kuas yang dipakai lapisan sorot di dalam templat tombol. Dimiliki di sini supaya
        /// gaya tombol bisa menentukan warna sorotnya sendiri (tombol berisi memakai kilau
        /// putih, tombol garis memakai tint aksen) tanpa menyalin seluruh templat.
        /// </summary>
        public static readonly DependencyProperty KuasSorotProperty =
            DependencyProperty.RegisterAttached(
                "KuasSorot", typeof(Brush), typeof(TekananHalus),
                new PropertyMetadata(null));

        public static void SetKuasSorot(DependencyObject elemen, Brush? nilai)
            => elemen.SetValue(KuasSorotProperty, nilai);

        public static Brush? GetKuasSorot(DependencyObject elemen)
            => (Brush?)elemen.GetValue(KuasSorotProperty);

        public static void SetAktif(DependencyObject elemen, bool nilai)
            => elemen.SetValue(AktifProperty, nilai);

        public static bool GetAktif(DependencyObject elemen)
            => (bool)elemen.GetValue(AktifProperty);

        /// <summary>Kedalaman tekan tombol. 0,955 dipilih supaya pemantulannya saat lepas
        /// terlihat: dengan 0,97 overshoot pegas hanya ≈0,3% — hilang di bawah pembulatan
        /// piksel, jadi tombol terasa datar walau animasinya jalan.</summary>
        private const double SkalaTekan = 0.955;

        private static void OnAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button tombol) return;

            // Lepas dulu: sebuah tombol bisa disiapkan ulang oleh templatnya.
            tombol.PreviewMouseLeftButtonDown -= OnTekan;
            tombol.PreviewMouseLeftButtonUp -= OnLepas;
            tombol.MouseLeave -= OnKeluar;
            tombol.MouseEnter -= OnMasuk;
            tombol.IsEnabledChanged -= OnKondisiBerubah;

            if (e.NewValue is true)
            {
                tombol.PreviewMouseLeftButtonDown += OnTekan;
                tombol.PreviewMouseLeftButtonUp += OnLepas;
                tombol.MouseLeave += OnKeluar;
                tombol.MouseEnter += OnMasuk;
                tombol.IsEnabledChanged += OnKondisiBerubah;
            }
            else
            {
                // Kembalikan ke keadaan dasar supaya tombol yang dinonaktifkan
                // tidak tertinggal dalam keadaan tersorot.
                Terapkan(tombol, sorot: false, tekan: false, angkat: false);
            }
        }

        private static void OnKondisiBerubah(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Button tombol && tomboltidakAktif(tombol))
            {
                Terapkan(tombol, sorot: false, tekan: false, angkat: false);
            }
        }

        private static bool tomboltidakAktif(Button tombol) => !tombol.IsEnabled;

        private static void OnMasuk(object sender, MouseEventArgs e)
        {
            if (sender is not Button tombol || !tombol.IsEnabled) return;
            Terapkan(tombol, sorot: true, tekan: tombol.IsPressed, angkat: true);
        }

        private static void OnKeluar(object sender, MouseEventArgs e)
        {
            if (sender is not Button tombol) return;

            // Kulit tombol terangkat 1 px; kursor yang jatuh ke pita 1 px itu bukan
            // keluar, dan menurunkannya akan membuat tombol bergetar di tepi bawah.
            if (TransformAman.KursorMasihDiZonaAngkat(tombol, 1d)) return;

            Terapkan(tombol, sorot: false, tekan: false, angkat: false);
        }

        private static void OnTekan(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button tombol || !tombol.IsEnabled) return;

            if (BolehBeranimasi())
            {
                var skala = SiapkanSkala(tombol);
                if (skala is not null)
                {
                    var durasi = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarTekanTombol);
                    var kurva = new CubicEase { EasingMode = EasingMode.EaseOut };
                    var masuk = new DoubleAnimation(1, SkalaTekan, durasi) { EasingFunction = kurva };
                    skala.BeginAnimation(ScaleTransform.ScaleXProperty, masuk);
                    skala.BeginAnimation(ScaleTransform.ScaleYProperty, masuk);
                }
            }

            Terapkan(tombol, sorot: true, tekan: true, angkat: false);
        }

        private static void OnLepas(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button tombol) return;

            if (BolehBeranimasi() && tombol.GetValue(skalaSementara) is ScaleTransform skala)
            {
                // Kembalinya sedikit lebih lama dari tekanannya dan memakai kurva pegas:
                // tombol lewat sedikit di atas ukuran penuh lalu mendarat — itu yang bikin
                // klik terasa membal, bukan sekadar kembali ke tempat.
                var durasi = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarSorotTombolKeluar);
                var kurva = new PegasEase { EasingMode = EasingMode.EaseOut };
                var keluar = new DoubleAnimation(SkalaTekan, 1, durasi) { EasingFunction = kurva };
                skala.BeginAnimation(ScaleTransform.ScaleXProperty, keluar);
                skala.BeginAnimation(ScaleTransform.ScaleYProperty, keluar);
            }

            bool masihDisorot = tombol.IsMouseOver;
            Terapkan(tombol, sorot: masihDisorot, tekan: false, angkat: masihDisorot);
        }

        /// <summary>
        /// Terapkan keadaan sorot/tekan/angkat pada bagian templat tombol.
        /// Semua nilai akhir dipegang animasi (HoldEnd), jadi sorotan yang dibalik
        /// di tengah jalan berangkat dari posisi nyatanya, bukan berkedip ke nol.
        /// </summary>
        private static void Terapkan(Button tombol, bool sorot, bool tekan, bool angkat)
        {
            var durasiMasuk = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarSorotTombol);
            var durasiKeluar = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarSorotTombolKeluar);

            if (CariAnak<FrameworkElement>(tombol, "SorotLapis") is { } lapisanSorot)
            {
                Ubah(lapisanSorot, UIElement.OpacityProperty, sorot ? 1d : 0d,
                    sorot ? durasiMasuk : durasiKeluar);
            }

            if (CariAnak<FrameworkElement>(tombol, "TekanLapis") is { } lapisanTekan)
            {
                Ubah(lapisanTekan, UIElement.OpacityProperty, tekan ? 1d : 0d,
                    tekan ? KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarTekanTombol) : durasiKeluar);
            }

            if (CariAnak<FrameworkElement>(tombol, "Kulit") is { } kulit)
            {
                var geser = SiapkanGeser(kulit);
                if (geser is not null)
                {
                    // Angkat: kurva lembut supaya tidak mengayun di atas teks yang
                    // sedang dibaca. Lepas: pegas, supaya lepasnya terasa memantul.
                    Ubah(geser, TranslateTransform.YProperty, angkat ? -1d : 0d,
                        angkat ? durasiMasuk : durasiKeluar,
                        angkat ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut }
                               : new PegasEase { EasingMode = EasingMode.EaseOut });
                }
            }
        }

        private static void Ubah(DependencyObject target, DependencyProperty properti, double ke, TimeSpan durasi)
            => Ubah(target, properti, ke, durasi, new CubicEase { EasingMode = EasingMode.EaseOut });

        private static void Ubah(DependencyObject target, DependencyProperty properti, double ke,
            TimeSpan durasi, IEasingFunction kurva)
        {
            if (!BolehBeranimasi())
            {
                // Setelan Windows "kurangi animasi": capai nilai akhir tanpa gerakan.
                if (target is UIElement uiTanpaAnimasi) uiTanpaAnimasi.SetValue(properti, ke);
                else if (target is Animatable animatableTanpaAnimasi) animatableTanpaAnimasi.SetValue(properti, ke);
                return;
            }

            var animasi = new DoubleAnimation
            {
                To = ke,
                Duration = durasi,
                EasingFunction = kurva,
                FillBehavior = FillBehavior.HoldEnd
            };

            switch (target)
            {
                case UIElement ui:
                    ui.BeginAnimation(properti, animasi);
                    break;
                case Animatable animatable:
                    animatable.BeginAnimation(properti, animasi);
                    break;
            }
        }

        /// <summary>Animasi boleh jalan? Tidak bila pengguna meminta Windows mengurangi animasi.</summary>
        private static bool BolehBeranimasi() => SystemParameters.ClientAreaAnimation;

        /// <summary>
        /// Kembalikan TranslateTransform yang aman dianimasikan. Transform yang
        /// dideklarasikan di XAML templat dibekukan WPF, sehingga BeginAnimation
        /// melontarkan InvalidOperationException — solusinya klon yang bisa diedit.
        /// </summary>
        private static TranslateTransform? SiapkanGeser(FrameworkElement elemen)
        {
            if (elemen.RenderTransform is TranslateTransform transform)
            {
                if (!transform.IsFrozen) return transform;

                var klon = transform.Clone();
                elemen.RenderTransform = klon;
                return klon;
            }

            var baru = new TranslateTransform();
            elemen.RenderTransform = baru;
            return baru;
        }

        /// <summary>
        /// Pasang ScaleTransform milik behavior ini sekali saja per tombol.
        /// Tombol yang sudah membawa RenderTransform sendiri (mis. dari XAML halaman)
        /// dikembalikan null dan tidak ikut dianimasikan — mengganti transformnya
        /// berisiko merusak animasi yang sudah ada.
        /// </summary>
        private static ScaleTransform? SiapkanSkala(Button tombol)
        {
            if (tombol.GetValue(skalaSementara) is ScaleTransform sudah) return sudah;
            // Transform default WPF adalah identity (bukan null), jadi "!= null" selalu
            // benar dan skala tekan tidak pernah terpasang — lihat TransformAman.
            if (!TransformAman.TanpaTransformSendiri(tombol)) return null;

            var skala = new ScaleTransform(1, 1);
            tombol.RenderTransformOrigin = new Point(0.5, 0.5);
            tombol.RenderTransform = skala;
            tombol.SetValue(skalaSementara, skala);
            return skala;
        }

        // Penanda bahwa ScaleTransform pada tombol ini milik behavior (bukan milik
        // XAML), supaya OnLepas tidak menganimasikan transform milik orang lain.
        private static readonly DependencyProperty skalaSementara =
            DependencyProperty.RegisterAttached(
                "SkalaSementara", typeof(ScaleTransform), typeof(TekananHalus),
                new PropertyMetadata(null));

        /// <summary>Cari turunan pertama bernama <paramref name="nama"/> di pohon visual.</summary>
        private static T? CariAnak<T>(DependencyObject root, string nama) where T : FrameworkElement
        {
            int jumlah = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < jumlah; i++)
            {
                var anak = VisualTreeHelper.GetChild(root, i);
                if (anak is T cocok && string.Equals(cocok.Name, nama, StringComparison.Ordinal))
                {
                    return cocok;
                }

                var temuan = CariAnak<T>(anak, nama);
                if (temuan is not null)
                {
                    return temuan;
                }
            }

            return null;
        }
    }
}
