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
    /// Kartu yang bisa diklik terasa bisa ditekan: mengangkat sedikit saat kursor
    /// datang, mengempis saat ditekan, dan mendarat kembali dengan pantulan halus
    /// (<see cref="PegasEase"/>) saat dilepas.
    ///
    /// <code>
    /// &lt;Border ctrl:KartuHalus.Aktif="True" ...&gt;
    /// &lt;Setter Property="ctrl:KartuHalus.Aktif" Value="True"/&gt;
    /// </code>
    ///
    /// Sengaja TIDAK dipasang pada <c>CardStyle</c>/<c>KpiCardStyle</c> global:
    /// kartu panel (form, toolbar, KPI) tidak bisa diklik, jadi mengangkatnya saat
    /// disorot menjanjikan sesuatu yang tidak pernah terjadi. Yang dipasang di sini
    /// hanya kartu yang benar-benar membuka sesuatu — pintasan Beranda, daftar
    /// template, kartu catatan rilis (item ListView), kartu tautan di About.
    /// Baris daftar yang hanya menampilkan data (mis. baris cadangan di Ekspor/Impor
    /// & Backup) sengaja tidak diangkat walau punya trigger sorot: mengangkatnya
    /// menjanjikan aksi yang tidak pernah terjadi.
    ///
    /// Ringan: hanya RenderTransform (lapisan kompositor) dan tidak menyentuh
    /// Margin/Height, jadi tidak ada layout pass dan tetangga kartu tidak bergeser
    /// barang sepx pun. Durasi mengikuti <see cref="KecepatanAnimasiPrefs"/>; bila
    /// Windows diminta mengurangi animasi, nilai akhir dicapai tanpa gerakan.
    /// </summary>
    public static class KartuHalus
    {
        /// <summary>Nyalakan angkat/tekan beranimasi pada elemen ini.</summary>
        public static readonly DependencyProperty AktifProperty =
            DependencyProperty.RegisterAttached(
                "Aktif", typeof(bool), typeof(KartuHalus),
                new PropertyMetadata(false, OnAktifBerubah));

        /// <summary>Tinggi angkat saat disorot, dalam piksel.</summary>
        public static readonly DependencyProperty JarakAngkatProperty =
            DependencyProperty.RegisterAttached(
                "JarakAngkat", typeof(double), typeof(KartuHalus),
                new PropertyMetadata(3d));

        /// <summary>Skala saat ditekan (1 = tidak mengempis).</summary>
        public static readonly DependencyProperty SkalaTekanProperty =
            DependencyProperty.RegisterAttached(
                "SkalaTekan", typeof(double), typeof(KartuHalus),
                new PropertyMetadata(0.96d));

        public static void SetAktif(DependencyObject elemen, bool nilai)
            => elemen.SetValue(AktifProperty, nilai);

        public static bool GetAktif(DependencyObject elemen)
            => (bool)elemen.GetValue(AktifProperty);

        public static void SetJarakAngkat(DependencyObject elemen, double nilai)
            => elemen.SetValue(JarakAngkatProperty, nilai);

        public static double GetJarakAngkat(DependencyObject elemen)
            => (double)elemen.GetValue(JarakAngkatProperty);

        public static void SetSkalaTekan(DependencyObject elemen, double nilai)
            => elemen.SetValue(SkalaTekanProperty, nilai);

        public static double GetSkalaTekan(DependencyObject elemen)
            => (double)elemen.GetValue(SkalaTekanProperty);

        private static void OnAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement elemen) return;

            elemen.MouseEnter -= OnMasuk;
            elemen.MouseLeave -= OnKeluar;
            elemen.PreviewMouseLeftButtonDown -= OnTekan;
            elemen.PreviewMouseLeftButtonUp -= OnLepas;
            elemen.IsEnabledChanged -= OnKondisiBerubah;

            if (e.NewValue is true)
            {
                elemen.MouseEnter += OnMasuk;
                elemen.MouseLeave += OnKeluar;
                elemen.PreviewMouseLeftButtonDown += OnTekan;
                elemen.PreviewMouseLeftButtonUp += OnLepas;
                elemen.IsEnabledChanged += OnKondisiBerubah;
            }
            else
            {
                Terapkan(elemen, angkat: false, tekan: false);
            }
        }

        private static void OnKondisiBerubah(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is FrameworkElement elemen && !elemen.IsEnabled)
            {
                Terapkan(elemen, angkat: false, tekan: false);
            }
        }

        private static void OnMasuk(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement elemen && elemen.IsEnabled)
            {
                Terapkan(elemen, angkat: true, tekan: false);
            }
        }

        private static void OnKeluar(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement elemen) return;

            // Jangan turunkan kartu hanya karena kursor jatuh ke pita 2-3 px yang
            // barusan ia tinggalkan — itu sumber getar di tepi bawah kartu.
            if (TransformAman.KursorMasihDiZonaAngkat(elemen, GetJarakAngkat(elemen))) return;

            Terapkan(elemen, angkat: false, tekan: false);
        }

        private static void OnTekan(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elemen && elemen.IsEnabled)
            {
                Terapkan(elemen, angkat: true, tekan: true);
            }
        }

        private static void OnLepas(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elemen)
            {
                Terapkan(elemen, angkat: elemen.IsMouseOver, tekan: false);
            }
        }

        private static void Terapkan(FrameworkElement elemen, bool angkat, bool tekan)
        {
            var pasangan = SiapkanTransform(elemen);
            if (pasangan is null) return;

            var (geser, skala) = pasangan.Value;

            double jarak = GetJarakAngkat(elemen);
            double skalaTujuan = tekan ? GetSkalaTekan(elemen) : 1d;

            var durasiAngkat = KecepatanAnimasiPrefs.Skala(
                angkat ? KecepatanAnimasiPrefs.DasarKartuAngkat : KecepatanAnimasiPrefs.DasarKartuLepas);
            var durasiTekan = KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarKartuTekan);

            Ubah(geser, TranslateTransform.YProperty, angkat ? -jarak : 0d,
                durasiAngkat, angkat ? KurvaPegas() : KurvaLembut());
            Ubah(skala, ScaleTransform.ScaleXProperty, skalaTujuan, durasiTekan,
                tekan ? KurvaLembut() : KurvaPegas());
            Ubah(skala, ScaleTransform.ScaleYProperty, skalaTujuan, durasiTekan,
                tekan ? KurvaLembut() : KurvaPegas());
        }

        /// <summary>
        /// Pasang (atau ambil milik kita sendiri) TransformGroup berisi geser + skala.
        /// Elemen yang sudah membawa RenderTransform dari XAML halaman tidak disentuh:
        /// mengganti transformnya akan merusak animasi yang sudah ada di sana.
        /// </summary>
        private static (TranslateTransform, ScaleTransform)? SiapkanTransform(FrameworkElement elemen)
        {
            if (elemen.GetValue(transformMilikKita) is TransformGroup punya
                && punya.Children.Count == 2
                && punya.Children[0] is TranslateTransform geserLama
                && punya.Children[1] is ScaleTransform skalaLama)
            {
                return (geserLama, skalaLama);
            }

            if (!TransformAman.TanpaTransformSendiri(elemen)) return null;

            var geser = new TranslateTransform();
            var skala = new ScaleTransform(1, 1);
            var grup = new TransformGroup();
            grup.Children.Add(geser);
            grup.Children.Add(skala);
            elemen.RenderTransformOrigin = new Point(0.5, 0.5);
            elemen.RenderTransform = grup;
            elemen.SetValue(transformMilikKita, grup);
            return (geser, skala);
        }

        private static readonly DependencyProperty transformMilikKita =
            DependencyProperty.RegisterAttached(
                "TransformMilikKita", typeof(TransformGroup), typeof(KartuHalus),
                new PropertyMetadata(null));

        private static IEasingFunction KurvaPegas()
            => new PegasEase { EasingMode = EasingMode.EaseOut };

        private static IEasingFunction KurvaLembut()
            => new CubicEase { EasingMode = EasingMode.EaseOut };

        private static void Ubah(Animatable target, DependencyProperty properti, double ke,
            TimeSpan durasi, IEasingFunction kurva)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                target.SetValue(properti, ke);
                return;
            }

            target.BeginAnimation(properti, new DoubleAnimation
            {
                To = ke,
                Duration = durasi,
                EasingFunction = kurva,
                FillBehavior = FillBehavior.HoldEnd
            });
        }
    }
}
