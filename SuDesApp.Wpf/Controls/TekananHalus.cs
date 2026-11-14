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
    /// Umpan balik tekan halus untuk tombol: saat ditekan tombol mengecil sedikit
    /// (skala 0,97), saat dilepas kembalinya memakai kurva EaseOut — memberi rasa
    /// "hidup" tanpa menunda apa pun. Dipasang dari gaya implicit Button:
    ///
    /// <code>
    /// &lt;Setter Property="ctrl:TekananHalus.Aktif" Value="True"/&gt;
    /// </code>
    ///
    /// Seperti <see cref="HoverAnimasiSidebar"/>, durasinya diambil dari
    /// <see cref="KecepatanAnimasiPrefs"/> sehingga pilihan Lambat/Normal/Cepat
    /// berlaku juga di sini; animasi dilewati bila pengguna mematikan animasi
    /// pada setelan sistem Windows.
    ///
    /// Hanya menyentuh RenderTransform (lapisan komposisi) dan hanya bila tombol
    /// belum punya transform sendiri — tombol dengan transform dari templat
    /// dibiarkan apa adanya agar animasi lain tidak rusak.
    /// </summary>
    public static class TekananHalus
    {
        /// <summary>Nyalakan umpan balik tekan pada tombol ini.</summary>
        public static readonly DependencyProperty AktifProperty =
            DependencyProperty.RegisterAttached(
                "Aktif", typeof(bool), typeof(TekananHalus),
                new PropertyMetadata(false, OnAktifBerubah));

        public static void SetAktif(DependencyObject elemen, bool nilai)
            => elemen.SetValue(AktifProperty, nilai);

        public static bool GetAktif(DependencyObject elemen)
            => (bool)elemen.GetValue(AktifProperty);

        private static void OnAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button tombol) return;

            if ((bool)e.NewValue)
            {
                tombol.PreviewMouseLeftButtonDown += OnTekan;
                tombol.PreviewMouseLeftButtonUp += OnLepas;
                tombol.MouseLeave += OnLepas;
            }
            else
            {
                tombol.PreviewMouseLeftButtonDown -= OnTekan;
                tombol.PreviewMouseLeftButtonUp -= OnLepas;
                tombol.MouseLeave -= OnLepas;
            }
        }

        private static void OnTekan(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button tombol || !tombol.IsEnabled) return;
            if (!SystemParameters.ClientAreaAnimation) return;
            var skala = SiapkanSkala(tombol);
            if (skala is null) return;

            var durasi = KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(80));
            var kurva = new CubicEase { EasingMode = EasingMode.EaseOut };
            var masuk = new DoubleAnimation(1, 0.97, durasi) { EasingFunction = kurva };
            skala.BeginAnimation(ScaleTransform.ScaleXProperty, masuk);
            skala.BeginAnimation(ScaleTransform.ScaleYProperty, masuk);
        }

        private static void OnLepas(object sender, MouseEventArgs e)
        {
            if (sender is not Button tombol) return;
            if (tombol.GetValue(skalaSementara) is not ScaleTransform skala) return;
            if (!SystemParameters.ClientAreaAnimation) return;

            // Kembalinya sedikit lebih lama dari tekanannya supaya mendarat lembut,
            // bukan memantul balik ke ukuran penuh dalam sekejap.
            var durasi = KecepatanAnimasiPrefs.Skala(TimeSpan.FromMilliseconds(150));
            var kurva = new CubicEase { EasingMode = EasingMode.EaseOut };
            var keluar = new DoubleAnimation(0.97, 1, durasi) { EasingFunction = kurva };
            skala.BeginAnimation(ScaleTransform.ScaleXProperty, keluar);
            skala.BeginAnimation(ScaleTransform.ScaleYProperty, keluar);
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
            if (tombol.RenderTransform != null) return null;

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
    }
}
