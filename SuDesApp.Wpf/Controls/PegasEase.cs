using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Kurva pegas teredam: nilainya melewati tujuan sedikit lalu mendarat kembali,
    /// seperti kartu yang dilepas setelah ditekan.
    ///
    /// <code>f(t) = 1 − e^(−Damping·t) · cos(2π · Frekuensi · t)</code>
    ///
    /// Ujungnya presisi: EaseInCore(0) dan EaseInCore(1) dikembalikan sebagai nilai
    /// pasti, jadi dengan EasingMode.EaseOut nilai akhir PERSIS 1 — tidak ada sisa
    /// ayunan yang tertahan (HoldEnd) membuat tombol berhenti dalam keadaan 0,5%
    /// lebih besar dari ukuran penuh.
    ///
    /// Puncak ≈1,20 pada t≈0,30 (20% lewat tujuan), lalu mengendap dengan satu
    /// ayunan kecil. Pada kedalaman tekan tombol 0,955 itu berarti tombol sempat
    /// ~0,9% lebih besar dari ukuran penuh (≈2 px untuk tombol 220 px): terlihat
    /// memantul, tidak terlihat seperti kesalahan layout.
    ///
    /// Dipakai hanya pada transform (geser/skala/putar). Di atas Opacity atau MaxHeight
    /// kurva ini percuma: Opacity mentok di 1 sehingga pantulannya tidak terlihat, dan
    /// MaxHeight yang melebihi tinggi isinya menggeser tetangga satu-dua piksel —
    /// justru itu yang membuat tampilan terasa goyah, bukan halus.
    ///
    /// Dipakai dengan <see cref="EasingMode"/> = EaseOut.
    /// </summary>
    public sealed class PegasEase : EasingFunctionBase
    {
        /// <summary>Seberapa cepat ayunan berhenti. Kecil = memantul lama.</summary>
        public double Damping { get; set; } = 4.8d;

        /// <summary>Jumlah setengah gelombang dalam satu animasi.</summary>
        public double Frekuensi { get; set; } = 1.35d;

        protected override Freezable CreateInstanceCore() => new PegasEase();

        /// <summary>
        /// WPF hanya meminta kurva "EaseIn" lalu membaliknya sendiri untuk EaseOut
        /// (hasil = 1 − EaseInCore(1 − t)). Kurva pegas yang diinginkan justru bentuk
        /// "out" — naik cepat, melewati tujuan, mengendap — jadi di sini ditulis bentuk
        ///baliknya, sehingga pemanggil cukup menulis EasingMode.EaseOut.
        /// </summary>
        protected override double EaseInCore(double normalisedTime)
        {
            if (normalisedTime >= 1d) return 1d;
            if (normalisedTime <= 0d) return 0d;
            return 1d - Pegas(1d - normalisedTime);
        }

        /// <summary>Kurva pegas sebenarnya, dipakai saat mode EaseOut.</summary>
        private double Pegas(double t)
        {
            double omega = 2d * Math.PI * Frekuensi;
            return 1d - Math.Exp(-Damping * t) * Math.Cos(omega * t);
        }
    }
}
