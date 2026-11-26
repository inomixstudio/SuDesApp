using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SuDesApp.Wpf.Controls
{
    /// <summary>
    /// Pertanyaan kecil yang jawabnya salah secara tidak intuitif di WPF: apakah sebuah
    /// elemen sudah membawa transform sendiri?
    ///
    /// <see cref="UIElement.RenderTransform"/> TIDAK bernilai null pada elemen baru —
    /// metadata defaultnya adalah <see cref="Transform.Identity"/>, sebuah MatrixTransform.
    /// Karena itu pemeriksaan <c>RenderTransform != null</c> selalu benar, dan behavior yang
    /// memakainya sebagai "jangan rusak transform milik XAML" tidak pernah memasang
    /// transformnya sendiri. Gejala aslinya: lift kartu dan skala tekan tombol diam-diam
    /// tidak pernah jalan, tanpa galat dan tanpa pesan.
    ///
    /// Yang ditanyakan seharusnya "apakah ada transform NYATA di sini?".
    /// </summary>
    internal static class TransformAman
    {
        /// <summary>True bila elemen belum membawa transform yang mengubah apa pun,
        /// sehingga aman dipasang transform milik behavior.</summary>
        internal static bool TanpaTransformSendiri(FrameworkElement elemen)
            => elemen.RenderTransform is null
               || elemen.RenderTransform == Transform.Identity
               || (elemen.RenderTransform is MatrixTransform identitas && identitas.Value == Matrix.Identity);

        /// <summary>
        /// Apakah kursor masih berada di pita yang ditinggalkan elemen saat terangkat?
        ///
        /// Mengangkat kartu 3 px menyisakan pita kosong setinggi 3 px di tepi bawahnya.
        /// Kursor yang berhenti persis di pita itu bukan "keluar": ia keluar (kartu turun)
        /// lalu masuk lagi (kartu naik) tanpa henti, dan kartunya bergetar di tepi bawah.
        /// Selama kursor masih di dalam batas lama ditambah jarak angkat, anggap masih
        /// disorot dan biarkan elemennya tetap terangkat.
        /// </summary>
        internal static bool KursorMasihDiZonaAngkat(FrameworkElement elemen, double jarak)
        {
            if (jarak <= 0d) return false;

            var titik = Mouse.GetPosition(elemen);
            return titik.X >= -1d
                   && titik.X <= elemen.ActualWidth + 1d
                   && titik.Y <= elemen.ActualHeight + jarak + 0.5d;
        }
    }
}
