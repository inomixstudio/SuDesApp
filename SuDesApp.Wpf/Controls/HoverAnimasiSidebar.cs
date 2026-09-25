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
    /// Sorotan hover untuk baris navigasi sidebar (menu langsung, header grup, dan
    /// menu anak), dipasang dari XAML sebagai properti terlampir:
    ///
    /// <code>
    /// &lt;Button ctrl:HoverAnimasiSidebar.Aktif="True"&gt;
    /// </code>
    ///
    /// Elemen yang dianimasikan dicari menurut nama di dalam templat pemiliknya —
    /// <c>HoverOverlay</c> (latar sorotan memudar), <c>HoverChevron</c> (tanda panah
    /// masuk dari kiri), dan <c>AccentPill</c> bila
    /// <see cref="AnimasikanPillProperty"/> dinyalakan (tombol anak sidebar yang
    /// tidak punya status "halaman sedang dibuka").
    ///
    /// Kenapa di kode, bukan sebagai EventTrigger di XAML: durasi animasi di
    /// EventTrigger tidak bisa diubah saat aplikasi berjalan, sedangkan kecepatan
    /// animasi adalah preferensi pengguna. Di sini setiap sorotan mengambil durasinya
    /// dari <see cref="KecepatanAnimasiPrefs"/>, sehingga pilihan Lambat/Normal/Cepat
    /// langsung berlaku tanpa membuka ulang aplikasi.
    /// </summary>
    public static class HoverAnimasiSidebar
    {
        /// <summary>Nyalakan sorotan hover beranimasi pada elemen ini.</summary>
        public static readonly DependencyProperty AktifProperty =
            DependencyProperty.RegisterAttached(
                "Aktif", typeof(bool), typeof(HoverAnimasiSidebar),
                new PropertyMetadata(false, OnAktifBerubah));

        /// <summary>Ikut memudarkan elemen bernama <c>AccentPill</c> saat hover.</summary>
        public static readonly DependencyProperty AnimasikanPillProperty =
            DependencyProperty.RegisterAttached(
                "AnimasikanPill", typeof(bool), typeof(HoverAnimasiSidebar),
                new PropertyMetadata(false));

        /// <summary>
        /// Status "halaman ini sedang dibuka" untuk pill aksen kiri. Nilainya diikat
        /// dari XAML (mis. <c>{Binding IsActive}</c>) supaya pill menyala &amp; memudar
        /// memakai durasi kecepatan animasi yang berlaku — dulu durasinya dipaku di
        /// XAML sehingga jalannya tidak ikut pilihan pengguna.
        /// </summary>
        public static readonly DependencyProperty PillAktifProperty =
            DependencyProperty.RegisterAttached(
                "PillAktif", typeof(bool?), typeof(HoverAnimasiSidebar),
                new PropertyMetadata(null, OnPillAktifBerubah));

        public static void SetAktif(DependencyObject elemen, bool nilai)
            => elemen.SetValue(AktifProperty, nilai);

        public static bool GetAktif(DependencyObject elemen)
            => (bool)elemen.GetValue(AktifProperty);

        public static void SetAnimasikanPill(DependencyObject elemen, bool nilai)
            => elemen.SetValue(AnimasikanPillProperty, nilai);

        public static bool GetAnimasikanPill(DependencyObject elemen)
            => (bool)elemen.GetValue(AnimasikanPillProperty);

        public static void SetPillAktif(DependencyObject elemen, bool? nilai)
            => elemen.SetValue(PillAktifProperty, nilai);

        public static bool? GetPillAktif(DependencyObject elemen)
            => (bool?)elemen.GetValue(PillAktifProperty);

        private static void OnAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement elemen)
            {
                return;
            }

            // Selalu lepas dulu: elemen bisa saja disiapkan ulang oleh templat.
            elemen.MouseEnter -= Elemen_MouseEnter;
            elemen.MouseLeave -= Elemen_MouseLeave;

            if (e.NewValue is true)
            {
                elemen.MouseEnter += Elemen_MouseEnter;
                elemen.MouseLeave += Elemen_MouseLeave;
            }
        }

        private static void OnPillAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement elemen || e.NewValue is not bool aktif)
            {
                return;
            }

            TerapkanPill(elemen, aktif);
        }

        /// <summary>
        /// Nyalakan/matikan pill aksen dengan durasi yang mengikuti pengaturan. Bila nilai
        /// ikatan datang sebelum templat selesai disusun, penerapannya ditunda sampai
        /// elemen dimuat supaya pill halaman yang aktif tidak tinggal padam.
        /// </summary>
        private static void TerapkanPill(FrameworkElement elemen, bool aktif)
        {
            if (CariAnak<Border>(elemen, "AccentPill") is Border pill)
            {
                Animasi(pill, UIElement.OpacityProperty, aktif ? 1d : 0d,
                    KecepatanAnimasiPrefs.Skala(KecepatanAnimasiPrefs.DasarSorotKeluar));
                return;
            }

            if (elemen.IsLoaded)
            {
                return;
            }

            RoutedEventHandler? ulang = null;
            ulang = (_, _) =>
            {
                elemen.Loaded -= ulang;
                if (GetPillAktif(elemen) is bool kini)
                {
                    TerapkanPill(elemen, kini);
                }
            };
            elemen.Loaded += ulang;
        }

        private static void Elemen_MouseEnter(object sender, MouseEventArgs e)
            => Sorot(sender as FrameworkElement, masuk: true);

        private static void Elemen_MouseLeave(object sender, MouseEventArgs e)
            => Sorot(sender as FrameworkElement, masuk: false);

        /// <summary>Jalankan sorotan masuk/keluar memakai durasi sesuai kecepatan terpilih.</summary>
        private static void Sorot(FrameworkElement? elemen, bool masuk)
        {
            if (elemen is null)
            {
                return;
            }

            var durasi = KecepatanAnimasiPrefs.Skala(
                masuk ? KecepatanAnimasiPrefs.DasarSorotMasuk : KecepatanAnimasiPrefs.DasarSorotKeluar);

            if (CariAnak<Border>(elemen, "HoverOverlay") is Border sorot)
            {
                Animasi(sorot, UIElement.OpacityProperty, masuk ? 1d : 0d, durasi);
            }

            if (CariAnak<FrameworkElement>(elemen, "HoverChevron") is FrameworkElement chevron)
            {
                // Transform hasil templat dibekukan WPF, jadi dianimasikan pada klonnya.
                var geser = SiapkanGeser(chevron);
                if (geser is not null)
                {
                    Animasi(geser, TranslateTransform.XProperty, masuk ? 0d : -4d, durasi);
                }

                Animasi(chevron, UIElement.OpacityProperty, masuk ? 0.9d : 0d, durasi);
            }

            if (GetAnimasikanPill(elemen) && CariAnak<Border>(elemen, "AccentPill") is Border pill)
            {
                Animasi(pill, UIElement.OpacityProperty, masuk ? 1d : 0d, durasi);
            }
        }

        /// <summary>
        /// Animasi nilai tunggal dari keadaan sekarang ke <paramref name="ke"/>.
        /// Nilai akhir dipegang animasi (HoldEnd), jadi tidak ada kilatan kembali ke
        /// nilai dasar di ujung gerakan dan sorotan yang dibalik di tengah jalan tetap
        /// berangkat dari posisi nyatanya.
        /// </summary>
        private static void Animasi(DependencyObject target, DependencyProperty properti, double ke, TimeSpan durasi)
        {
            var animasi = new DoubleAnimation
            {
                To = ke,
                Duration = durasi,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
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

        /// <summary>
        /// Kembalikan TranslateTransform yang aman dianimasikan dari kode. Transform yang
        /// dideklarasikan di XAML templat dibekukan WPF, sehingga BeginAnimation
        /// melontarkan InvalidOperationException — solusinya klon ke instans yang bisa diedit.
        /// </summary>
        private static TranslateTransform? SiapkanGeser(FrameworkElement elemen)
        {
            if (elemen.RenderTransform is TranslateTransform transform)
            {
                if (!transform.IsFrozen)
                {
                    return transform;
                }

                transform = transform.Clone();
                elemen.RenderTransform = transform;
                return transform;
            }

            var baru = new TranslateTransform();
            elemen.RenderTransform = baru;
            return baru;
        }

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
