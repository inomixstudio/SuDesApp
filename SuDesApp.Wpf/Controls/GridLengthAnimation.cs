using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>Kurva peluruhan animasi.</summary>
    public enum GridLengthEasingMode
    {
        /// <summary>Mulai cepat, mendarat pelan (umum untuk membuka/melebarkan).</summary>
        EaseOut,
        /// <summary>Mulai pelan, mengebut di akhir (umum untuk menutup/menciutkan).</summary>
        EaseIn,
        /// <summary>Pelan di awal dan akhir (transisi netral).</summary>
        EaseInOut
    }

    /// <summary>
    /// Animasi untuk <see cref="GridLength"/> (lebar kolom Grid). WPF tidak menyediakan
    /// animasi bawaan untuk tipe ini, sehingga sidebar akan melompat saat dikecilkan
    /// atau dilebarkan tanpa kelas kecil ini.
    /// </summary>
    public sealed class GridLengthAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

        /// <summary>Lebar awal. Bila tidak diisi, nilai properti saat ini yang dipakai.</summary>
        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        /// <summary>Lebar tujuan animasi.</summary>
        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        /// <summary>Kurva easing yang dipakai. Default EaseOut (landing lembut).</summary>
        public GridLengthEasingMode EasingMode { get; set; } = GridLengthEasingMode.EaseOut;

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            double dari = From.IsAbsolute ? From.Value : ((GridLength)defaultOriginValue).Value;
            double ke = To.IsAbsolute ? To.Value : ((GridLength)defaultDestinationValue).Value;
            double progres = animationClock.CurrentProgress ?? 0;

            // Kurva quintic (pangkat 5): sedikit lebih "menyedot" daripada cubic,
            // terasa mengalir tanpa melompat saat mendekati nilai akhir.
            progres = EasingMode switch
            {
                GridLengthEasingMode.EaseIn => progres * progres * progres * progres * progres,
                GridLengthEasingMode.EaseInOut => progres < 0.5
                    ? 16 * progres * progres * progres * progres * progres
                    : 1 - Math.Pow(-2 * progres + 2, 5) / 2,
                _ => 1 - Math.Pow(1 - progres, 5)
            };

            return new GridLength(dari + ((ke - dari) * progres), GridUnitType.Pixel);
        }
    }
}